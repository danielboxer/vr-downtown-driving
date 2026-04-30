// ==============================
// RoadNetworkBuilder.cs
// (Decals clipped by sampling spans outside junction polygons)
// ==============================
#if UNITY_EDITOR
using Assets.Scripts.SUMOImporter.NetFileComponents;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using NetFile;

public class RoadNetworkBuilder : MonoBehaviour
{
    public static RoadNetworkBuilder Singleton { get; private set; }
    private void Awake() => Singleton = this;
    public void InitializeInEditMode()
    {
        if (Singleton != this)
        {
            Singleton = this;
            Debug.Log("RoadNetworkBuilder: Editor-based initialization complete.");
        }
    }
    private void OnDestroy()
    {
        if (Singleton == this) Singleton = null;
    }

    [Header("Road Signs")]
    [Tooltip("Stop sign prefab placed at minor approaches of priority and allway-stop junctions.")]
    public GameObject stopSignPrefab;
    [Tooltip("Height offset above road surface for sign base placement.")]
    public float signHeightOffset = 0f;

    [Header("Lane Arrow Decals")]
    [Tooltip("Decal material for straight-ahead only lanes.")]
    public Material throughDecalMaterial;
    [Tooltip("Decal material for left-turn lanes (left + forward arrow combined).")]
    public Material leftTurnDecalMaterial;
    [Tooltip("Decal material for right-turn-only lanes.")]
    public Material rightTurnDecalMaterial;
    [Tooltip("Decal material for right-turn + straight lanes.")]
    public Material rightTurnAndThroughDecalMaterial;
    [Tooltip("Decal material for lanes with forward, left, and right arrows.")]
    public Material throughRightLeftDecalMaterial;
    [Tooltip("Decal material for the stop line painted on the road at each junction approach.")]
    public Material stopLineDecalMaterial;
    [Tooltip("How far back from the junction endpoint to center the arrow decal (meters).")]
    public float arrowSetbackFromJunction = 5f;

    [Header("Materials (Road, Junction, Decals)")]
    public Material roadSurfaceMaterial;
    public Material junctionSurfaceMaterial;
    public Material roadMarkingMaterial;

    [Header("Polygon Types (Wood/Terrain/Roadside/Residential)")]
    public Material polygonWoodMaterial;
    public Material polygonTerrainMaterial;
    public Material polygonRoadsideMaterial;
    public Material polygonResidentialMaterial;
    private Material polygonFallbackMaterial;

    [Header("Sidewalk")]
    [Tooltip("Enable or disable curb generation entirely.")]
    public bool generateCurbs = true;
    [Tooltip("Height of raised curb above the road surface. Low values let vehicles drive over.")]
    public float sidewalkHeight = 0.2f;
    [Tooltip("Width of the flat sidewalk top extending outward from the road edge.")]
    public float curbWidth = 5f;
    [Tooltip("Width of the slope ramp on the road side.")]
    public float innerSlopeWidth = 0.15f;
    [Tooltip("Width of the slope ramp on the outer side blending into terrain.")]
    public float outerSlopeWidth = 1.0f;
    [Tooltip("Fillet radius at curb corners (meters). Larger values = more rounding.")]
    public float curbFilletRadius = 0.05f;
    [Tooltip("Material for sidewalk curb walls. Falls back to terrain material if null.")]
    public Material sidewalkWallMaterial;

    [Header("Lane Markings")]
    [Tooltip("Skip lane markings on the outer road edges where curbs are.")]
    public bool skipOuterEdgeMarkings = true;

    [Header("Generation Options")]
    [Tooltip("Mark all generated GameObjects as static (enables batching, GI, occlusion culling, navmesh).")]
    public bool markGeneratedAsStatic = true;
    [Tooltip("Add a BoxCollider to each traffic light head so vehicles can collide with the pole.")]
    public bool addTrafficLightColliders = true;
    [Tooltip("Size of the box collider added to each traffic light head (width, height, depth).")]
    public Vector3 trafficLightColliderSize = new Vector3(0.2f, 3f, 0.2f);
    [Tooltip("Local-space center of the traffic light box collider. Y=1.5 places the base at ground level.")]
    public Vector3 trafficLightColliderCenter = new Vector3(0f, 1.5f, 0f);
    [Tooltip("Add a BoxCollider to each stop sign post so vehicles can collide with it.")]
    public bool addStopSignColliders = true;
    [Tooltip("Size of the box collider added to each stop sign (width, height, depth).")]
    public Vector3 stopSignColliderSize = new Vector3(0.1f, 2.5f, 0.1f);
    [Tooltip("Local-space center of the stop sign box collider. Y=1.25 places the base at ground level.")]
    public Vector3 stopSignColliderCenter = new Vector3(0f, 1.25f, 0f);


    private GameObject roadNetworkRoot;

    // ★ NEW: Ground-layer support --------------------------------------------
    private const string groundLayerName = "Ground";
    private int groundLayer = -1;
    // ------------------------------------------------------------------------

    public Dictionary<string, RoadJunctionData> junctionRecords;
    public Dictionary<string, RoadLaneData> laneRecords;
    public Dictionary<string, RoadEdgeData> edgeRecords;
    public Dictionary<string, PolygonShapeData> polygonShapes;

    private string sumoXmlFolderPath;

    private float minX = 0f, minY = 0f, maxX = 0f, maxY = 0f;
    private float originX = 0f, originY = 0f;

    private const float laneMeshScaleWidth = 3.2f;
    private const float laneUvVerticalScale = 5f;
    private const float laneUvHorizontalScale = 1f;

    private readonly Dictionary<string, float> laneWidthMap = new();

    // NEW: cache junction polygons (Unity XZ plane)
    private readonly List<Vector2[]> _junctionPolys2D = new();


    // Parsed net file, kept for traffic light generation
    private NetType _netFile;

    public void LoadSumoXmlFiles(string sumoFilesFolder)
    {
        if (roadNetworkRoot != null)
        {
            DestroyImmediate(roadNetworkRoot);
            roadNetworkRoot = null;
        }

        // Also destroy any orphaned root left from a previous builder instance
        var oldRoot = GameObject.Find("RoadNetworkRoot");
        if (oldRoot != null)
            DestroyImmediate(oldRoot);

        // Destroy orphaned Junctions root (not parented to roadNetworkRoot)
        var oldJunctions = GameObject.Find("Junctions");
        if (oldJunctions != null)
            DestroyImmediate(oldJunctions);

        // Destroy orphaned road signs and lane decals from an older generation
        // (new layout nests these under Junctions/RoadSigns and RoadNetworkRoot/LaneDecals,
        //  so they are cleaned up automatically when those parents are destroyed above)
        var oldSigns = GameObject.Find("RoadSignsRoot");
        if (oldSigns != null)
            DestroyImmediate(oldSigns);

        var oldDecals = GameObject.Find("LaneDecalsRoot");
        if (oldDecals != null)
            DestroyImmediate(oldDecals);

        ParseSumoXmlFiles(sumoFilesFolder);
    }

    /// <summary>
    /// Parses SUMO XML files into in-memory records without destroying existing GameObjects.
    /// Creates roadNetworkRoot if it doesn't exist.
    /// </summary>
    public void ParseSumoXmlFiles(string sumoFilesFolder)
    {
        laneWidthMap.Clear();
        junctionRecords?.Clear();
        laneRecords?.Clear();
        edgeRecords?.Clear();
        polygonShapes?.Clear();
        _junctionPolys2D.Clear();

        sumoXmlFolderPath = sumoFilesFolder;

        // ★ NEW: cache “Ground” layer index once
        if (groundLayer < 0) groundLayer = LayerMask.NameToLayer(groundLayerName);
        if (groundLayer < 0)
            Debug.LogWarning($"Layer \"{groundLayerName}\" does not exist – objects will keep their current layer.");

        if (roadNetworkRoot == null)
        {
            // Try to find an existing root in the scene before creating a new one
            FindExistingRoot();
        }
        if (roadNetworkRoot == null)
        {
            roadNetworkRoot = new GameObject("RoadNetworkRoot");
            if (groundLayer >= 0) roadNetworkRoot.layer = groundLayer;
        }

        // Auto-detect the net and poly files by extension so the filenames are not hardcoded
        var netFilePath = Directory.GetFiles(sumoXmlFolderPath, "*.net.xml").FirstOrDefault();
        var polyFilePath = Directory.GetFiles(sumoXmlFolderPath, "*.poly.xml").FirstOrDefault();

        if (netFilePath == null)
        {
            Debug.LogError($"[RoadNetworkBuilder] No *.net.xml found in '{sumoXmlFolderPath}'.");
            return;
        }

        laneRecords = new();
        edgeRecords = new();
        junctionRecords = new();
        polygonShapes = new();

        {
            var serializer = new XmlSerializer(typeof(NetType));
            using var fs = new FileStream(netFilePath, FileMode.Open, FileAccess.Read);
            using var rd = new StreamReader(fs);
            _netFile = (NetType)serializer.Deserialize(rd);
        }

        if (!string.IsNullOrEmpty(_netFile.Location?.ConvBoundary))
        {
            var bounds = _netFile.Location.ConvBoundary.Split(',');
            minX = float.Parse(bounds[0]);
            minY = float.Parse(bounds[1]);
            maxX = float.Parse(bounds[2]);
            maxY = float.Parse(bounds[3]);
        }

        if (!string.IsNullOrEmpty(_netFile.Location?.NetOffset))
        {
            var p = _netFile.Location.NetOffset.Split(',');
            originX = float.Parse(p[0]);
            originY = float.Parse(p[1]);
        }
        else
        {
            originX = minX; originY = minY;
        }

        foreach (JunctionType jt in _netFile.Junction)
        {
            if (jt.Type.ToString().Equals("Internal", StringComparison.OrdinalIgnoreCase))
                continue;

            if (string.IsNullOrEmpty(jt.X) || string.IsNullOrEmpty(jt.Y))
            {
                Debug.LogError($"Junction {jt.Id} missing X/Y. Skipping.");
                continue;
            }

            try
            {
                var newJunction = new RoadJunctionData(
                    jt.Id,
                    jt.Type,
                    float.Parse(jt.X),
                    float.Parse(jt.Y),
                    string.IsNullOrEmpty(jt.Z) ? 0f : float.Parse(jt.Z),
                    jt.IncLanes,
                    jt.Shape);

                if (!junctionRecords.ContainsKey(newJunction.junctionId))
                    junctionRecords.Add(newJunction.junctionId, newJunction);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Junction {jt.Id} parse error: {ex.Message}");
            }
        }

        foreach (EdgeType et in _netFile.Edge)
        {
            if (string.IsNullOrEmpty(et.From))
            {
                Debug.LogWarning($"Edge {et.Id} has no 'from'. Skipping.");
                continue;
            }

            var newEdge = new RoadEdgeData(et.Id, et.From, et.To, et.Priority, et.Shape);
            edgeRecords[et.Id] = newEdge;

            if (et.Lane == null) continue;

            foreach (LaneType laneType in et.Lane)
            {
                float width = laneType.WidthSpecified ? laneType.Width : laneMeshScaleWidth;
                laneWidthMap[laneType.Id] = width;

                newEdge.AddLaneData(
                    laneType.Id,
                    laneType.Index.ToString(),
                    laneType.Speed,
                    laneType.Length,
                    width,
                    laneType.Shape);
            }
        }

        if (!File.Exists(polyFilePath)) return;

        try
        {
            var serializer = new XmlSerializer(typeof(AdditionalType));
            using FileStream fs = new FileStream(polyFilePath, FileMode.Open);
            using TextReader rd = new StreamReader(fs);
            AdditionalType additionalPolygons = (AdditionalType)serializer.Deserialize(rd);

            foreach (PolygonType poly in additionalPolygons.Poly)
            {
                if (!IsKnownPolygonType(poly.Type)) continue;

                var shapeData = new PolygonShapeData();
                foreach (string pair in poly.Shape.Split(' '))
                {
                    var parts = pair.Split(',');
                    shapeData.AddPoint(Convert.ToDouble(parts[0]), Convert.ToDouble(parts[1]));
                }
                shapeData.RemoveDuplicateEndPoint();
                if (!polygonShapes.ContainsKey(poly.Id))
                    polygonShapes.Add(poly.Id, shapeData);

                if (shapeData.polygonPoints.Count >= 3)
                    BuildPolygonGameObject(shapeData, poly.Id, poly.Type);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to deserialize polygon file: {e}");
        }

        // ★ NEW: ensure polygons just created inherit the Ground layer
        SetLayerRecursively(roadNetworkRoot, groundLayer);
    }

    /// <summary>
    /// Attempts to find an existing RoadNetworkRoot in the scene and assign it.
    /// Returns true if one was found.
    /// </summary>
    public bool FindExistingRoot()
    {
        if (roadNetworkRoot != null) return true;
        var existing = GameObject.Find("RoadNetworkRoot");
        if (existing != null) { roadNetworkRoot = existing; return true; }
        return false;
    }

    public void GenerateRoadsAndJunctions()
    {
        // lanes
        int laneCounter = 0;
        foreach (var edgeData in edgeRecords.Values)
        {
            var lanes = edgeData.GetLaneDataList();
            int maxLaneIndex = 0;
            foreach (var ld in lanes)
                if (ld.laneIndex > maxLaneIndex) maxLaneIndex = ld.laneIndex;

            foreach (var laneData in lanes)
            {
                var lanePoints = new Vector3[laneData.shapePoints.Count];
                for (int i = 0; i < laneData.shapePoints.Count; i++)
                    lanePoints[i] = ToUnity(laneData.shapePoints[i][0], laneData.shapePoints[i][1]);

                float laneWidth = laneWidthMap.TryGetValue(laneData.laneId, out float w) ? w : laneMeshScaleWidth;

                Mesh laneMesh = CreateLaneMesh(lanePoints, laneWidth, laneUvHorizontalScale, laneUvVerticalScale);
                if (laneMesh == null) continue;

                var laneObj = new GameObject($"LaneSegment_{laneCounter++}");
                laneObj.transform.SetParent(roadNetworkRoot.transform);
                if (groundLayer >= 0) laneObj.layer = groundLayer;  // ★ NEW
                var mf = laneObj.AddComponent<MeshFilter>();
                var mr = laneObj.AddComponent<MeshRenderer>();
                mf.sharedMesh = laneMesh;
                mr.sharedMaterial = roadSurfaceMaterial ?? GetFallbackMaterial();

                // Physics collider so vehicles don't fall through the road
                var laneCol = laneObj.AddComponent<MeshCollider>();
                laneCol.sharedMesh = laneMesh;
                laneCol.convex = false;

                // Skip lane markings on the outer road edges (where curbs are)
                bool isLeftmost = laneData.laneIndex == maxLaneIndex;
                bool isRightmost = laneData.laneIndex == 0;

                // Left side marking: skip only if this is the leftmost lane AND there's no
                // opposite-direction edge (i.e., it's the actual road boundary, not a median)
                bool skipLeft = skipOuterEdgeMarkings && isLeftmost && !HasOppositeEdge(edgeData);
                bool skipRight = skipOuterEdgeMarkings && isRightmost;

                if (!skipLeft)
                    SpawnMarkingDecals(ExtractLeftSideVertices(laneMesh), "LaneMarking_Right", laneObj.transform);

                if (!skipRight)
                    SpawnMarkingDecals(ExtractRightSideVertices(laneMesh), "LaneMarking_Left", laneObj.transform);

                var ctrl = laneObj.AddComponent<LaneSegmentDecalController>();
                ctrl.solidDepth = 3f;
                ctrl.brokenDepth = 1.5f;
            }
        }

        // junctions
        int junctionCounter = 0;
        foreach (RoadJunctionData j in junctionRecords.Values)
        {
            if (j.shapePoints.Count < 3) continue;

            var verts2D = new Vector2[j.shapePoints.Count];
            for (int i = 0; i < j.shapePoints.Count; i++)
            {
                double[] xy = j.shapePoints[i];
                verts2D[i] = new Vector2((float)(xy[0] - originX), (float)(xy[1] - originY));
            }

            // Cache for decal clipping
            _junctionPolys2D.Add((Vector2[])verts2D.Clone());

            MeshTriangulator triangulator = new MeshTriangulator(verts2D);
            int[] triIndices = triangulator.GenerateIndices();

            var verts3D = new Vector3[verts2D.Length];
            for (int i = 0; i < verts2D.Length; i++)
                verts3D[i] = new Vector3(verts2D[i].x, 0f, verts2D[i].y);

            Mesh junctionMesh = new Mesh
            {
                name = $"Junction_{j.junctionId}",
                vertices = verts3D,
                triangles = triIndices
            };
            junctionMesh.RecalculateNormals();
            junctionMesh.RecalculateBounds();

            Bounds b = junctionMesh.bounds;
            var uvArr = new Vector2[verts3D.Length];
            for (int i = 0; i < verts3D.Length; i++)
                uvArr[i] = new Vector2(
                    (verts3D[i].x - b.min.x) / b.size.x,
                    (verts3D[i].z - b.min.z) / b.size.z);
            junctionMesh.uv = uvArr;

            GameObject jObj = new GameObject($"Junction_{junctionCounter++}");
            jObj.transform.SetParent(roadNetworkRoot.transform);
            if (groundLayer >= 0) jObj.layer = groundLayer;           // ★ NEW
            var jMf = jObj.AddComponent<MeshFilter>();
            var jMr = jObj.AddComponent<MeshRenderer>();
            jMf.mesh = junctionMesh;
            jMr.material = junctionSurfaceMaterial ?? GetFallbackMaterial();
        }

        // ★ NEW: make sure every child built above is on the Ground layer
        SetLayerRecursively(roadNetworkRoot, groundLayer);

        // Mark all road geometry as static for batching, GI, and occlusion culling
        if (markGeneratedAsStatic) SetStaticRecursively(roadNetworkRoot);

        // Generate raised curb strips along road edges
        if (generateCurbs && sidewalkHeight > 0.01f)
            GenerateCurbs();
    }

    // -------------------- helper --------------------------------------------
    private static void SetLayerRecursively(GameObject obj, int layer)
    {
        if (layer < 0) return;
        obj.layer = layer;
        foreach (Transform child in obj.transform)
            SetLayerRecursively(child.gameObject, layer);
    }

    private static void SetStaticRecursively(GameObject obj)
    {
        GameObjectUtility.SetStaticEditorFlags(obj,
            StaticEditorFlags.ContributeGI |
            StaticEditorFlags.OccluderStatic |
            StaticEditorFlags.OccludeeStatic |
            StaticEditorFlags.BatchingStatic |
            StaticEditorFlags.NavigationStatic |
            StaticEditorFlags.OffMeshLinkGeneration |
            StaticEditorFlags.ReflectionProbeStatic);
        foreach (Transform child in obj.transform)
            SetStaticRecursively(child.gameObject);
    }
    // ------------------------------------------------------------------------

    // ======================================================================
    //  Curb / Sidewalk Strip Generation
    // ======================================================================

    /// <summary>
    /// Generates raised curb strips along the outer edges of every road.
    /// Skips the median side when an opposite-direction edge exists so
    /// curbs only appear at road boundaries, not in the middle of the road.
    /// </summary>
    private void GenerateCurbs()
    {
        GameObject curbRoot = new GameObject("Curbs");
        curbRoot.transform.SetParent(roadNetworkRoot.transform);

        int curbIdx = 0;
        foreach (var edgeData in edgeRecords.Values)
        {
            var lanes = edgeData.GetLaneDataList();
            if (lanes.Count == 0) continue;

            bool hasOpposite = HasOppositeEdge(edgeData);

            // Rightmost lane (index 0) outer edge -- always generate
            var firstLane = lanes[0];
            if (firstLane.shapePoints.Count >= 2)
            {
                float w = laneWidthMap.TryGetValue(firstLane.laneId, out float fw) ? fw : laneMeshScaleWidth;
                var edgePts = ComputeLaneEdge(firstLane, w, false);
                if (edgePts.Length >= 2)
                    BuildCurbStrip(edgePts, $"Curb_{curbIdx++}", curbRoot.transform, -1);
            }

            // Leftmost lane outer edge -- skip if opposite edge exists (that side is the median)
            if (!hasOpposite)
            {
                var lastLane = lanes[lanes.Count - 1];
                if (lastLane.shapePoints.Count >= 2)
                {
                    float w = laneWidthMap.TryGetValue(lastLane.laneId, out float lw) ? lw : laneMeshScaleWidth;
                    var edgePts = ComputeLaneEdge(lastLane, w, true);
                    if (edgePts.Length >= 2)
                        BuildCurbStrip(edgePts, $"Curb_{curbIdx++}", curbRoot.transform, 1);
                }
            }
        }
    }

    /// <summary>
    /// Returns true if an opposite-direction edge exists for the given edge.
    /// Two edges are opposite when their from/to junctions are swapped.
    /// </summary>
    private bool HasOppositeEdge(RoadEdgeData edge)
    {
        var from = edge.GetFromJunction();
        var to = edge.GetToJunction();
        if (from == null || to == null) return false;

        foreach (var other in edgeRecords.Values)
        {
            if (other == edge) continue;
            var oFrom = other.GetFromJunction();
            var oTo = other.GetToJunction();
            if (oFrom == null || oTo == null) continue;

            if (oFrom.junctionId == to.junctionId && oTo.junctionId == from.junctionId)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Computes the outer edge points of a lane.
    /// left=true returns the left side, left=false returns the right side.
    /// </summary>
    private Vector3[] ComputeLaneEdge(RoadLaneData lane, float width, bool left)
    {
        int n = lane.shapePoints.Count;
        var pts = new Vector3[n];

        for (int i = 0; i < n; i++)
        {
            Vector3 center = ToUnity(lane.shapePoints[i][0], lane.shapePoints[i][1]);

            // Direction to next or previous point
            Vector3 dir;
            if (i < n - 1)
                dir = (ToUnity(lane.shapePoints[i + 1][0], lane.shapePoints[i + 1][1]) - center).normalized;
            else
                dir = (center - ToUnity(lane.shapePoints[i - 1][0], lane.shapePoints[i - 1][1])).normalized;

            // Perpendicular (left of travel direction)
            Vector3 perp = new Vector3(-dir.z, 0f, dir.x);
            pts[i] = center + perp * (width * 0.5f) * (left ? 1f : -1f);
        }
        return pts;
    }

    /// <summary>
    /// Subdivides a polyline so no segment exceeds maxLen.
    /// Ensures enough vertices for smooth per-vertex effects like height tapering.
    /// </summary>
    private static Vector3[] SubdividePolyline(Vector3[] pts, float maxLen)
    {
        if (pts.Length < 2) return pts;
        var result = new List<Vector3> { pts[0] };
        for (int i = 0; i < pts.Length - 1; i++)
        {
            Vector3 a = pts[i];
            Vector3 b = pts[i + 1];
            float len = Vector3.Distance(a, b);
            int divs = Mathf.CeilToInt(len / maxLen);
            for (int j = 1; j <= divs; j++)
                result.Add(Vector3.Lerp(a, b, (float)j / divs));
        }
        return result.ToArray();
    }

    /// <summary>
    /// Builds a curb strip with sloped inner and outer faces along a polyline.
    /// Cross-section: inner slope (road->top), flat top, outer slope (top->ground).
    /// outwardSign: +1 extends left of travel, -1 extends right of travel.
    /// </summary>
    private void BuildCurbStrip(Vector3[] edgePts, string name, Transform parent, int outwardSign)
    {
        // Subdivide to ensure smooth taper at ends
        edgePts = SubdividePolyline(edgePts, 1.0f);

        int segCount = edgePts.Length - 1;
        // Cross-section profile: straight ramps with rounded corners
        Vector2[] profile = BuildCurbProfile();
        int profileCount = profile.Length;
        int strips = profileCount - 1;

        int vertsPerSeg = profileCount * 2;
        int trisPerSeg = strips * 6;
        var verts = new Vector3[segCount * vertsPerSeg];
        var tris = new int[segCount * trisPerSeg];

        // Distance-based height taper at strip ends
        const float taperDist = 2.0f;
        float[] cumDist = new float[edgePts.Length];
        cumDist[0] = 0f;
        for (int k = 1; k < edgePts.Length; k++)
            cumDist[k] = cumDist[k - 1] + Vector3.Distance(edgePts[k], edgePts[k - 1]);
        float totalLen = cumDist[cumDist.Length - 1];

        for (int i = 0; i < segCount; i++)
        {
            Vector3 a = edgePts[i];
            Vector3 b = edgePts[i + 1];

            // Taper height near strip endpoints
            float dA = Mathf.Min(cumDist[i], totalLen - cumDist[i]);
            float dB = Mathf.Min(cumDist[i + 1], totalLen - cumDist[i + 1]);
            float hA = Mathf.Clamp01(dA / taperDist) * sidewalkHeight;
            float hB = Mathf.Clamp01(dB / taperDist) * sidewalkHeight;

            // Unit outward perpendicular for this segment
            Vector3 dir = (b - a).normalized;
            Vector3 outDir = new Vector3(-dir.z, 0f, dir.x) * outwardSign;

            int vi = i * vertsPerSeg;
            int ti = i * trisPerSeg;

            // Place vertices along cross-section profile
            for (int p = 0; p < profileCount; p++)
            {
                float offset = profile[p].x;
                float hFrac = profile[p].y;

                verts[vi + p * 2] = new Vector3(
                    a.x + outDir.x * offset, hA * hFrac, a.z + outDir.z * offset);
                verts[vi + p * 2 + 1] = new Vector3(
                    b.x + outDir.x * offset, hB * hFrac, b.z + outDir.z * offset);
            }

            // Build quads between adjacent profile strips
            for (int s = 0; s < strips; s++)
            {
                int v0 = vi + s * 2;
                int v1 = vi + s * 2 + 1;
                int v2 = vi + s * 2 + 2;
                int v3 = vi + s * 2 + 3;

                tris[ti + s * 6 + 0] = v0;
                tris[ti + s * 6 + 1] = v2;
                tris[ti + s * 6 + 2] = v1;
                tris[ti + s * 6 + 3] = v1;
                tris[ti + s * 6 + 4] = v2;
                tris[ti + s * 6 + 5] = v3;
            }
        }

        Mesh curbMesh = new Mesh { name = name, vertices = verts, triangles = tris };

        // Winding is correct for outwardSign=+1; flip for -1 so normals face outward
        if (outwardSign < 0)
            FlipTriangleWinding(curbMesh);

        curbMesh.RecalculateNormals();
        curbMesh.RecalculateBounds();

        GameObject go = new GameObject(name);
        go.transform.SetParent(parent);

        go.AddComponent<MeshFilter>().sharedMesh = curbMesh;
        go.AddComponent<MeshRenderer>().sharedMaterial =
            sidewalkWallMaterial != null ? sidewalkWallMaterial : GetPolygonMaterial("terrain");

        // MeshCollider for collision (non-convex is fine since curbs are static)
        var col = go.AddComponent<MeshCollider>();
        col.sharedMesh = curbMesh;
        col.convex = false;
    }

    /// <summary>
    /// Builds the cross-section profile for curb geometry.
    /// Straight inner slope, flat top, straight outer slope, with circular-arc bevels at corners.
    /// Returns (offset, heightFraction) pairs where offset is meters from the road edge point.
    /// </summary>
    private Vector2[] BuildCurbProfile()
    {
        const int arcSegments = 4;
        float h = sidewalkHeight;
        if (h < 0.001f) return new[] { new Vector2(-innerSlopeWidth, 0f), new Vector2(curbWidth + outerSlopeWidth, 0f) };

        var pts = new List<Vector2>();

        // Inner slope bottom (road level)
        pts.Add(new Vector2(-innerSlopeWidth, 0f));

        // Inner corner bevel: circular arc tangent to inner slope and flat top
        {
            float L = Mathf.Sqrt(innerSlopeWidth * innerSlopeWidth + h * h);
            float maxW = h * h / (L + innerSlopeWidth) * 0.9f;
            float W = Mathf.Min(curbFilletRadius, innerSlopeWidth * 0.9f, curbWidth * 0.4f, maxW);

            if (W > 0.001f)
            {
                Vector2 d1 = new Vector2(innerSlopeWidth / L, h / L);
                Vector2 corner = new Vector2(0f, h);
                Vector2 T1 = corner - d1 * W;
                Vector2 T2 = corner + new Vector2(W, 0f);

                float R = W * (L + innerSlopeWidth) / h;
                Vector2 C = new Vector2(W, h - R);

                float startAngle = Mathf.Atan2(T1.y - C.y, T1.x - C.x);
                float endAngle = Mathf.PI * 0.5f;

                for (int a = 0; a <= arcSegments; a++)
                {
                    float t = (float)a / arcSegments;
                    float angle = Mathf.Lerp(startAngle, endAngle, t);
                    pts.Add(C + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * R);
                }
            }
            else
            {
                pts.Add(new Vector2(0f, h));
            }
        }

        // Outer corner bevel: circular arc tangent to flat top and outer slope
        {
            float L = Mathf.Sqrt(outerSlopeWidth * outerSlopeWidth + h * h);
            float maxW = h * h / (L + outerSlopeWidth) * 0.9f;
            float W = Mathf.Min(curbFilletRadius, outerSlopeWidth * 0.9f, curbWidth * 0.4f, maxW);

            if (W > 0.001f)
            {
                Vector2 d2 = new Vector2(outerSlopeWidth / L, -h / L);
                Vector2 corner = new Vector2(curbWidth, h);
                Vector2 T1 = corner - new Vector2(W, 0f);
                Vector2 T2 = corner + d2 * W;

                float R = W * (L + outerSlopeWidth) / h;
                Vector2 C = new Vector2(curbWidth - W, h - R);

                float startAngle = Mathf.PI * 0.5f;
                float endAngle = Mathf.Atan2(T2.y - C.y, T2.x - C.x);

                for (int a = 0; a <= arcSegments; a++)
                {
                    float t = (float)a / arcSegments;
                    float angle = Mathf.Lerp(startAngle, endAngle, t);
                    pts.Add(C + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * R);
                }
            }
            else
            {
                pts.Add(new Vector2(curbWidth, h));
            }
        }

        // Outer slope bottom (ground level)
        pts.Add(new Vector2(curbWidth + outerSlopeWidth, 0f));

        // Normalize height from meters to fraction of sidewalkHeight
        for (int i = 0; i < pts.Count; i++)
            pts[i] = new Vector2(pts[i].x, pts[i].y / h);

        return pts.ToArray();
    }

    // ======================================================================
    //  Traffic Light Generation
    // ======================================================================

    // ======================================================================
    //  Selective Deletion
    // ======================================================================

    /// <summary>
    /// Destroys children of roadNetworkRoot whose names start with any of the given prefixes.
    /// </summary>
    private void DestroyChildrenByPrefix(params string[] prefixes)
    {
        if (roadNetworkRoot == null) return;
        var toDestroy = new List<GameObject>();
        foreach (Transform child in roadNetworkRoot.transform)
        {
            foreach (var prefix in prefixes)
            {
                if (child.name.StartsWith(prefix, StringComparison.Ordinal))
                {
                    toDestroy.Add(child.gameObject);
                    break;
                }
            }
        }
        foreach (var go in toDestroy)
            DestroyImmediate(go);
    }

    /// <summary>
    /// Destroys a direct child of roadNetworkRoot with the exact given name.
    /// </summary>
    private void DestroyChildByName(string exactName)
    {
        if (roadNetworkRoot == null) return;
        var t = roadNetworkRoot.transform.Find(exactName);
        if (t != null) DestroyImmediate(t.gameObject);
    }

    /// <summary>Deletes road lane segments, junction meshes, and curbs.</summary>
    public void DeleteRoadObjects()
    {
        DestroyChildrenByPrefix("LaneSegment_", "Junction_");
        DestroyChildByName("Curbs");
    }

    /// <summary>Deletes non-terrain polygon objects (Shape_ prefix).</summary>
    public void DeletePolygonObjects()
    {
        DestroyChildrenByPrefix("Shape_");
    }

    /// <summary>Deletes traffic light hierarchy (Junctions is a scene-root object).</summary>
    public void DeleteTrafficLightObjects()
    {
        var junctions = GameObject.Find("Junctions");
        if (junctions != null)
            DestroyImmediate(junctions);
    }

    /// <summary>
    /// Generates traffic light GameObjects for every junction whose type is
    /// traffic_light (or variant). Creates the hierarchy expected by
    /// SimulationController: Junctions → {id} → Head0..HeadN → green/yellow/red.
    /// One visible ThreeLight prefab per incoming edge; invisible stubs for
    /// remaining link indices so the state string maps 1-to-1.
    /// </summary>
    public void GenerateTrafficLights()
    {
        if (_netFile == null) { Debug.LogError("Net file not loaded."); return; }

        // Load ThreeLight prefab from Resources
        GameObject tlPrefab = Resources.Load<GameObject>("TrafficLight/ThreeLight");
        if (tlPrefab == null)
        {
            Debug.LogError("TrafficLight/ThreeLight prefab not found in Resources.");
            return;
        }

        // Build a lookup: tlId → Dictionary<linkIndex, fromEdgeId>
        var tlConnections = new Dictionary<string, Dictionary<int, string>>();
        foreach (ConnectionType conn in _netFile.Connection)
        {
            if (string.IsNullOrEmpty(conn.Tl) || string.IsNullOrEmpty(conn.LinkIndex)) continue;
            if (!int.TryParse(conn.LinkIndex, out int linkIdx)) continue;

            if (!tlConnections.TryGetValue(conn.Tl, out var map))
            {
                map = new Dictionary<int, string>();
                tlConnections[conn.Tl] = map;
            }
            map[linkIdx] = conn.From;
        }

        // Build a set of traffic-light junction IDs from tlLogic
        var tlJunctionIds = new HashSet<string>();
        foreach (TlLogicType tl in _netFile.TlLogic)
            tlJunctionIds.Add(tl.Id);

        // Create "Junctions" root (separate from road network, for SimulationController)
        GameObject junctionsRoot = new GameObject("Junctions");

        foreach (string jId in tlJunctionIds)
        {
            if (!junctionRecords.TryGetValue(jId, out RoadJunctionData jData)) continue;
            if (!tlConnections.TryGetValue(jId, out var linkToEdge)) continue;

            // Junction center in Unity coords
            Vector3 junctionCenter = ToUnity(jData.xPos, jData.yPos);

            GameObject junctionGO = new GameObject(jId);
            junctionGO.transform.SetParent(junctionsRoot.transform);
            junctionGO.transform.position = Vector3.zero;

            // Group linkIndices by fromEdge
            var edgeToLinks = new Dictionary<string, List<int>>();
            foreach (var kvp in linkToEdge)
            {
                if (!edgeToLinks.TryGetValue(kvp.Value, out var list))
                {
                    list = new List<int>();
                    edgeToLinks[kvp.Value] = list;
                }
                list.Add(kvp.Key);
            }

            // For each incoming edge, place one visible ThreeLight prefab
            foreach (var edgeEntry in edgeToLinks)
            {
                string edgeId = edgeEntry.Key;
                List<int> linkIndices = edgeEntry.Value;
                linkIndices.Sort();

                // Find lane endpoint to position the traffic light
                Vector3 laneEndPos = junctionCenter;
                Vector3 approachDir = Vector3.forward;
                Vector3 stopLinePos = junctionCenter;
                float totalRoadWidth = 0f;
                float laneW = 3.2f; // width of rightmost lane, used to center the stop-line trigger

                if (edgeRecords.TryGetValue(edgeId, out RoadEdgeData edgeData))
                {
                    // Sum total road width across all lanes
                    foreach (var ln in edgeData.GetLaneDataList())
                    {
                        float w = (float)ln.laneWidth;
                        totalRoadWidth += (w > 0f ? w : 3.2f);
                    }

                    var lanes = edgeData.GetLaneDataList();
                    if (lanes.Count > 0)
                    {
                        // Use the rightmost lane (index 0 in SUMO) which is closest to the curb
                        var lane = lanes[0];
                        if (lane.shapePoints.Count >= 2)
                        {
                            int last = lane.shapePoints.Count - 1;
                            Vector3 laneEnd = ToUnity(lane.shapePoints[last][0], lane.shapePoints[last][1]);
                            stopLinePos = laneEnd;

                            Vector3 prevPt = ToUnity(lane.shapePoints[last - 1][0], lane.shapePoints[last - 1][1]);
                            approachDir = (laneEnd - prevPt).normalized;

                            // Move the light to the far side of the junction (opposite the stop line)
                            float forwardDist = Vector3.Dot(junctionCenter - laneEnd, approachDir);
                            Vector3 farSideBase = laneEnd + approachDir * (2f * forwardDist);

                            float laneW_inner = (float)lane.laneWidth;
                            if (laneW_inner <= 0f) laneW_inner = 3.2f;
                            laneW = laneW_inner;
                            Vector3 rightDir = new Vector3(approachDir.z, 0f, -approachDir.x);
                            float curbOffset = 0.5f;

                            // Primary: right of rightmost lane edge
                            laneEndPos = farSideBase + rightDir * (laneW_inner * 0.5f + curbOffset);
                        }
                    }
                }
                if (totalRoadWidth <= 0f) totalRoadWidth = 6.4f;

                // Left-side mirror position: cross both incoming and opposing lanes
                Vector3 mirrorRightDir = new Vector3(approachDir.z, 0f, -approachDir.x);
                float mirrorCurbOffset = 0.5f;
                // Approximate full road width as 2× incoming lanes (incoming + opposing direction)
                Vector3 leftSidePos = laneEndPos - mirrorRightDir * (2f * totalRoadWidth + 2f * mirrorCurbOffset);

                // Primary Head: first linkIndex for this edge gets the visible ThreeLight
                int primaryLink = linkIndices[0];
                GameObject head = (GameObject)PrefabUtility.InstantiatePrefab(tlPrefab);
                head.name = $"Head{primaryLink}";
                head.transform.SetParent(junctionGO.transform);
                head.transform.position = laneEndPos;
                // Face toward oncoming traffic (the light faces the driver)
                head.transform.rotation = Quaternion.LookRotation(-approachDir, Vector3.up);

                // Optional pole collider on the primary head
                if (addTrafficLightColliders)
                {
                    var headCol = head.AddComponent<BoxCollider>();
                    headCol.size = trafficLightColliderSize;
                    headCol.center = trafficLightColliderCenter;
                }

                // Mirrored light on the left side (child of primary so state syncs)
                GameObject mirror = (GameObject)PrefabUtility.InstantiatePrefab(tlPrefab);
                mirror.name = "Mirror";
                mirror.transform.SetParent(head.transform);
                mirror.transform.position = leftSidePos;
                mirror.transform.rotation = Quaternion.LookRotation(-approachDir, Vector3.up);
                // Flip the mirror along the local X axis
                mirror.transform.localScale = new Vector3(-1f, 1f, 1f);

                // Optional pole collider on the mirror
                if (addTrafficLightColliders)
                {
                    var mirrorCol = mirror.AddComponent<BoxCollider>();
                    mirrorCol.size = trafficLightColliderSize;
                    mirrorCol.center = trafficLightColliderCenter;
                }

                // ── Stop-line trigger for driving evaluation ──
                GameObject stopLineGO = new GameObject($"StopLine_{jId}_E{edgeId}");
                stopLineGO.transform.SetParent(junctionGO.transform);
                stopLineGO.transform.position = stopLinePos;
                stopLineGO.transform.rotation = Quaternion.LookRotation(approachDir, Vector3.up);

                var slBox = stopLineGO.AddComponent<BoxCollider>();
                slBox.isTrigger = true;
                slBox.size = new Vector3(totalRoadWidth, 2f, 3f);
                // Shift center left (into the road) so the box covers the lanes rather than the curb.
                // Positive Z offset places the trigger past the stop line so it fires on crossing.
                slBox.center = new Vector3(-(totalRoadWidth * 0.5f - laneW * 0.5f), 0f, 4.5f);

                var slTrigger = stopLineGO.AddComponent<StopLineTrigger>();
                slTrigger.junctionId = jId;
                slTrigger.linkIndex = primaryLink;

                // ── Turn-direction trigger: one per approach, right-side entry ──
                // Exactly like the stop-line trigger (same position, size, and center offset)
                // but rotated 90° so it faces rightDir. This makes it cover the right-side
                // lanes in the junction exit direction. Cars turning right pass through it;
                // each approach provides one right-exit trigger so all turns are covered.
                Vector3 tdRightDir = new Vector3(approachDir.z, 0f, -approachDir.x);

                GameObject tdGO = new GameObject($"TurnTrigger_{jId}_E{edgeId}");
                tdGO.transform.SetParent(junctionGO.transform);
                tdGO.transform.position = stopLinePos;
                tdGO.transform.rotation = Quaternion.LookRotation(tdRightDir, Vector3.up);

                var tdBox = tdGO.AddComponent<BoxCollider>();
                tdBox.isTrigger = true;
                tdBox.size = new Vector3(totalRoadWidth, 2f, 3f);
                // With LookRotation(tdRightDir), local -X → world +approachDir.
                // center.x places it forward into the junction; center.z = 3.0 shifts it
                // toward the right curb to cover the right-turn exit lane.
                tdBox.center = new Vector3(-(totalRoadWidth * 1.125f), 0f, 3.0f);

                var tdTrigger = tdGO.AddComponent<TurnDirectionTrigger>();
                tdTrigger.junctionId = jId;
                tdTrigger.direction = DrivingEvaluator.SignalDirection.Right;
                tdTrigger.approachDir = approachDir;

                // Secondary Heads: invisible stubs with green_light/yellow_light/red_light children
                for (int i = 1; i < linkIndices.Count; i++)
                {
                    int linkIdx = linkIndices[i];
                    GameObject stub = new GameObject($"Head{linkIdx}");
                    stub.transform.SetParent(junctionGO.transform);
                    stub.transform.position = laneEndPos;

                    // Create minimal children so SimulationController's SetSignalState works
                    new GameObject("green_light").transform.SetParent(stub.transform);
                    new GameObject("yellow_light").transform.SetParent(stub.transform);
                    new GameObject("red_light").transform.SetParent(stub.transform);
                }
            }
        }
        // Mark junction hierarchy as static for batching, GI, and occlusion culling
        if (markGeneratedAsStatic) SetStaticRecursively(junctionsRoot);

        // Make only the root non-selectable so children remain individually toggleable
        // Make only the root non-selectable so children remain individually toggleable.
        // EnablePicking first clears any stale descendant state from previous runs (which would cause the mixed cube icon).
        SceneVisibilityManager.instance.EnablePicking(junctionsRoot, true);
        SceneVisibilityManager.instance.DisablePicking(junctionsRoot, true);
        Debug.Log($"[Sumo2Unity] Generated traffic lights for {tlJunctionIds.Count} junctions under 'Junctions' root.");
    }

    /// <summary>
    /// Applies non-selectable picking state to RoadNetworkRoot and Junctions.
    /// Must be called after ALL generation steps are complete so no pickable children
    /// are added afterward (which would cause the mixed cube icon on the root).
    /// </summary>
    public void ApplyPickingState()
    {
        if (roadNetworkRoot != null)
        {
            SceneVisibilityManager.instance.EnablePicking(roadNetworkRoot, true);
            SceneVisibilityManager.instance.DisablePicking(roadNetworkRoot, true);
        }
        var junctions = GameObject.Find("Junctions");
        if (junctions != null)
        {
            SceneVisibilityManager.instance.EnablePicking(junctions, true);
            SceneVisibilityManager.instance.DisablePicking(junctions, true);
        }
    }

    /// <summary>Deletes road sign GameObjects (under the RoadSigns child of Junctions).</summary>
    public void DeleteRoadSignObjects()
    {
        var junctions = GameObject.Find("Junctions");
        if (junctions != null)
        {
            var signsChild = junctions.transform.Find("RoadSigns");
            if (signsChild != null)
                DestroyImmediate(signsChild.gameObject);
        }
        // Also clean up any legacy scene-root from an older generation run
        var legacyRoot = GameObject.Find("RoadSignsRoot");
        if (legacyRoot != null)
            DestroyImmediate(legacyRoot);
    }

    /// <summary>
    /// Places stop sign prefabs at junction approaches based on SUMO junction type:
    ///   - AllwayStop: signs on every incoming edge.
    ///   - Priority / PriorityStop: signs only on incoming edges whose edge priority
    ///     is strictly below the maximum priority found among all incoming edges.
    /// </summary>
    public void GenerateRoadSigns()
    {
        if (_netFile == null) { Debug.LogError("Net file not loaded."); return; }

        if (stopSignPrefab == null)
        {
            // Fall back to Resources lookup if not assigned in Inspector
            stopSignPrefab = Resources.Load<GameObject>("Signs/StopSign");
        }

        if (stopSignPrefab == null)
        {
            Debug.LogError("[RoadNetworkBuilder] stopSignPrefab is not assigned and 'Signs/StopSign' was not found in Resources.");
            return;
        }

        // Group edges by destination junction for efficient lookup
        var edgesByToJunction = new Dictionary<string, List<RoadEdgeData>>();
        foreach (var edgeData in edgeRecords.Values)
        {
            var toJ = edgeData.GetToJunction();
            if (toJ == null) continue;

            if (!edgesByToJunction.TryGetValue(toJ.junctionId, out var list))
            {
                list = new List<RoadEdgeData>();
                edgesByToJunction[toJ.junctionId] = list;
            }
            list.Add(edgeData);
        }

        // Find (or create) the Junctions scene root so signs sit alongside traffic lights
        GameObject junctionsRoot = GameObject.Find("Junctions") ?? new GameObject("Junctions");

        // Signs go in a dedicated child so they can be deleted independently of TL heads
        GameObject signsRoot = new GameObject("RoadSigns");
        signsRoot.transform.SetParent(junctionsRoot.transform);
        int placedCount = 0;

        foreach (var jData in junctionRecords.Values)
        {
            bool isAllwayStop = jData.junctionType == JunctionTypeType.AllwayStop;
            bool isPriority = jData.junctionType == JunctionTypeType.Priority
                             || jData.junctionType == JunctionTypeType.PriorityStop;

            if (!isAllwayStop && !isPriority) continue;
            if (!edgesByToJunction.TryGetValue(jData.junctionId, out var incomingEdges)) continue;
            if (incomingEdges.Count == 0) continue;

            // For priority junctions, determine the maximum incoming edge priority.
            // Approaches with a lower priority value than the maximum are minor roads (stop sign needed).
            int maxPriority = int.MinValue;
            if (isPriority)
            {
                foreach (var edge in incomingEdges)
                    if (edge.GetEdgePriority() > maxPriority)
                        maxPriority = edge.GetEdgePriority();
            }

            Vector3 junctionCenter = ToUnity(jData.xPos, jData.yPos);

            foreach (var edge in incomingEdges)
            {
                // Skip if this is a major-road approach at a priority junction
                if (isPriority && edge.GetEdgePriority() >= maxPriority) continue;

                var lanes = edge.GetLaneDataList();
                if (lanes == null || lanes.Count == 0) continue;

                // Use the rightmost lane (index 0 in SUMO) to find the approach endpoint
                var rightLane = lanes[0];
                if (rightLane.shapePoints == null || rightLane.shapePoints.Count < 2) continue;

                int last = rightLane.shapePoints.Count - 1;
                Vector3 laneEnd = ToUnity(rightLane.shapePoints[last][0], rightLane.shapePoints[last][1]);
                Vector3 prevPt = ToUnity(rightLane.shapePoints[last - 1][0], rightLane.shapePoints[last - 1][1]);
                Vector3 approachDir = (laneEnd - prevPt).normalized;

                // Place sign on the right curb (same convention as traffic lights)
                Vector3 rightDir = new Vector3(approachDir.z, 0f, -approachDir.x);
                float laneW = (float)rightLane.laneWidth;
                if (laneW <= 0f) laneW = 3.2f;
                float curbOffset = 0.5f;
                Vector3 signPos = laneEnd + rightDir * (laneW * 0.5f + curbOffset);
                signPos.y += signHeightOffset;

                GameObject sign = (GameObject)PrefabUtility.InstantiatePrefab(stopSignPrefab);
                sign.name = $"StopSign_{jData.junctionId}_E{edge.GetEdgeId()}";
                sign.transform.SetParent(signsRoot.transform);
                sign.transform.position = signPos;
                // Face the sign toward oncoming traffic (same as the traffic light heads)
                sign.transform.rotation = Quaternion.LookRotation(-approachDir, Vector3.up);

                if (addStopSignColliders)
                {
                    var signCol = sign.AddComponent<BoxCollider>();
                    signCol.size = stopSignColliderSize;
                    signCol.center = stopSignColliderCenter;
                }

                placedCount++;
            }
        }
        Debug.Log($"[Sumo2Unity] Placed {placedCount} stop signs under 'Junctions/RoadSigns'.");
    }

    /// <summary>Deletes lane arrow decal GameObjects (under the LaneDecals child of RoadNetworkRoot).</summary>
    public void DeleteLaneDecalObjects()
    {
        DestroyChildByName("LaneDecals");
        // Also clean up any legacy scene-root from an older generation run
        var legacyRoot = GameObject.Find("LaneDecalsRoot");
        if (legacyRoot != null)
            DestroyImmediate(legacyRoot);
    }

    /// <summary>
    /// Places lane-direction arrow decals on the road surface before each junction approach,
    /// using SUMO connection direction data to pick the correct material.
    ///
    /// Direction → material mapping:
    ///   Straight only (s)         → throughDecalMaterial
    ///   Left only (l/L)           → leftTurnDecalMaterial
    ///   Right only (r/R)          → rightTurnDecalMaterial
    ///   Any other combination     → throughRightLeftDecalMaterial
    /// </summary>
    public void GenerateLaneDecals()
    {
        if (_netFile == null) { Debug.LogError("Net file not loaded."); return; }

        // Collect which materials are available; skip silently if none are assigned
        bool anyMaterial = throughDecalMaterial != null
                        || leftTurnDecalMaterial != null
                        || rightTurnDecalMaterial != null
                        || rightTurnAndThroughDecalMaterial != null
                        || throughRightLeftDecalMaterial != null;
        if (!anyMaterial)
        {
            Debug.LogWarning("[RoadNetworkBuilder] No lane arrow decal materials assigned. Skipping GenerateLaneDecals().");
            return;
        }

        // Group connection directions by (fromEdge, fromLaneIndex)
        var laneDirections = new Dictionary<(string edge, int lane), HashSet<ConnectionTypeDir>>();
        foreach (ConnectionType conn in _netFile.Connection)
        {
            if (string.IsNullOrEmpty(conn.From) || string.IsNullOrEmpty(conn.FromLane)) continue;
            if (!int.TryParse(conn.FromLane, out int fromLaneIdx)) continue;

            var key = (conn.From, fromLaneIdx);
            if (!laneDirections.TryGetValue(key, out var dirSet))
            {
                dirSet = new HashSet<ConnectionTypeDir>();
                laneDirections[key] = dirSet;
            }
            dirSet.Add(conn.Dir);
        }

        if (roadNetworkRoot == null)
        {
            Debug.LogError("[RoadNetworkBuilder] roadNetworkRoot is null. Generate roads first.");
            return;
        }

        // Parent decals under roadNetworkRoot so they're grouped with the rest of the road network
        DestroyChildByName("LaneDecals");
        GameObject decalsRoot = new GameObject("LaneDecals");
        decalsRoot.transform.SetParent(roadNetworkRoot.transform);
        int placedCount = 0;

        foreach (var kvp in laneDirections)
        {
            string edgeId = kvp.Key.edge;
            int laneIdx = kvp.Key.lane;
            var dirs = kvp.Value;

            if (!edgeRecords.TryGetValue(edgeId, out RoadEdgeData edgeData)) continue;

            // Find the specific lane by its index
            RoadLaneData lane = null;
            foreach (var l in edgeData.GetLaneDataList())
            {
                if (l.laneIndex == laneIdx) { lane = l; break; }
            }
            if (lane == null || lane.shapePoints == null || lane.shapePoints.Count < 2) continue;

            // Pick decal material based on direction set
            Material mat = PickArrowMaterial(dirs);
            if (mat == null) continue;

            // Compute endpoint and approach direction from the last two shape points
            int last = lane.shapePoints.Count - 1;
            Vector3 laneEnd = ToUnity(lane.shapePoints[last][0], lane.shapePoints[last][1]);
            Vector3 prevPt = ToUnity(lane.shapePoints[last - 1][0], lane.shapePoints[last - 1][1]);
            Vector3 approachDir = (laneEnd - prevPt).normalized;

            // Center the arrow setback from the junction, at lane center height
            Vector3 decalPos = laneEnd - approachDir * arrowSetbackFromJunction;
            decalPos.y += 0.4f;

            float laneW = (float)lane.laneWidth;
            if (laneW <= 0f) laneW = 3.2f;

            // Arrow decal
            if (mat != null)
            {
                GameObject decalObj = new GameObject($"ArrowDecal_{edgeId}_L{laneIdx}");
                decalObj.transform.SetParent(decalsRoot.transform);
                if (groundLayer >= 0) decalObj.layer = groundLayer;
                decalObj.transform.position = decalPos;
                // Euler(90, yaw, 0): X=90 pitches the projector to face downward (-Y world),
                // Y=yaw aligns the texture with the road travel direction.
                float yaw = Mathf.Atan2(approachDir.x, approachDir.z) * Mathf.Rad2Deg;
                decalObj.transform.rotation = Quaternion.Euler(90f, yaw, 0f);

                var proj = decalObj.AddComponent<DecalProjector>();
                proj.material = mat;
                proj.size = new Vector3(2.5f, 2.5f, 0.4f);
                proj.drawDistance = 250f;
                placedCount++;
            }

            // Stop line decal: wide thin stripe across the lane, at the junction endpoint
            if (stopLineDecalMaterial != null)
            {
                GameObject slDecalObj = new GameObject($"StopLineDecal_{edgeId}_L{laneIdx}");
                slDecalObj.transform.SetParent(decalsRoot.transform);
                if (groundLayer >= 0) slDecalObj.layer = groundLayer;
                Vector3 slPos = laneEnd;
                slPos.y += 0.4f;
                slDecalObj.transform.position = slPos;
                // Z+90° rotates the stripe 90° within the horizontal plane so it runs across the lane
                float slYaw = Mathf.Atan2(approachDir.x, approachDir.z) * Mathf.Rad2Deg + 90f;
                slDecalObj.transform.rotation = Quaternion.Euler(90f, slYaw, 90f);

                var slProj = slDecalObj.AddComponent<DecalProjector>();
                slProj.material = stopLineDecalMaterial;
                // Width spans the lane, Y = projection depth, Z = stripe thickness
                slProj.size = new Vector3(laneW, 0.5f, 0.4f);
                slProj.drawDistance = 250f;
            }
        }
        Debug.Log($"[Sumo2Unity] Placed {placedCount} lane arrow decals under 'RoadNetworkRoot/LaneDecals'.");
    }

    /// <summary>
    /// Returns the appropriate arrow decal material for a set of SUMO connection directions.
    ///
    /// Material assignments (matching the project's decal textures):
    ///   S only           → throughDecalMaterial               (forward only)
    ///   R only           → rightTurnDecalMaterial              (right only)
    ///   R + S            → rightTurnAndThroughDecalMaterial    (right + forward)
    ///   L only or L + S  → leftTurnDecalMaterial               (left + forward combined texture)
    ///   Everything else  → throughRightLeftDecalMaterial        (forward + left + right)
    /// </summary>
    private Material PickArrowMaterial(HashSet<ConnectionTypeDir> dirs)
    {
        bool hasLeft = dirs.Contains(ConnectionTypeDir.L) || dirs.Contains(ConnectionTypeDir.L1);
        bool hasRight = dirs.Contains(ConnectionTypeDir.R) || dirs.Contains(ConnectionTypeDir.R1);
        bool hasStraight = dirs.Contains(ConnectionTypeDir.S);

        if (hasStraight && !hasLeft && !hasRight) return throughDecalMaterial;
        if (hasRight && !hasStraight && !hasLeft) return rightTurnDecalMaterial;
        if (hasRight && hasStraight && !hasLeft) return rightTurnAndThroughDecalMaterial;
        if (hasLeft && !hasRight) return leftTurnDecalMaterial; // left-only or left+straight (texture shows both)
        return throughRightLeftDecalMaterial;
    }

    private Vector3 ToUnity(double x, double y) => new((float)(x - originX), 0f, (float)(y - originY));

    private void BuildPolygonGameObject(PolygonShapeData shapeData, string polygonId, string polygonType)
    {
        var vertices2D = new Vector2[shapeData.polygonPoints.Count];
        for (int i = 0; i < shapeData.polygonPoints.Count; i++)
        {
            double[] xy = shapeData.polygonPoints[i];
            vertices2D[i] = new Vector2((float)(xy[0] - originX), (float)(xy[1] - originY));
        }

        MeshTriangulator tri = new MeshTriangulator(vertices2D);
        int[] indices = tri.GenerateIndices();

        var vertices3D = new Vector3[vertices2D.Length];
        for (int i = 0; i < vertices2D.Length; i++)
            vertices3D[i] = new Vector3(vertices2D[i].x, 0f, vertices2D[i].y);

        Mesh polyMesh = new Mesh
        {
            name = $"Polygon_{polygonId}",
            vertices = vertices3D,
            triangles = indices
        };
        polyMesh.RecalculateNormals();

        if (polyMesh.normals.Length > 0 && polyMesh.normals[0].y < 0f)
        {
            FlipTriangleWinding(polyMesh);
            polyMesh.RecalculateNormals();
        }
        polyMesh.RecalculateBounds();

        Bounds mBounds = polyMesh.bounds;
        var uvs = new Vector2[vertices3D.Length];
        for (int i = 0; i < vertices3D.Length; i++)
            uvs[i] = new Vector2(
                (vertices3D[i].x - mBounds.min.x) / mBounds.size.x,
                (vertices3D[i].z - mBounds.min.z) / mBounds.size.z);
        polyMesh.uv = uvs;

        GameObject polyGO = new GameObject($"Shape_{polygonId}");
        polyGO.transform.SetParent(roadNetworkRoot.transform);
        if (groundLayer >= 0) polyGO.layer = groundLayer;             // ★ NEW

        if (!string.IsNullOrEmpty(polygonType) && polygonType.ToLowerInvariant().Contains("terrain"))
            polyGO.transform.localPosition = new Vector3(0f, -0.02f, 0f);
        if (!string.IsNullOrEmpty(polygonType) && polygonType.ToLowerInvariant().Contains("roadside"))
            polyGO.transform.localPosition = new Vector3(0f, -0.01f, 0f);
        if (!string.IsNullOrEmpty(polygonType) && polygonType.ToLowerInvariant().Contains("wood"))
            polyGO.transform.localPosition = new Vector3(0f, -0.01f, 0f);
        if (!string.IsNullOrEmpty(polygonType) && polygonType.ToLowerInvariant().Contains("residential"))
            polyGO.transform.localPosition = new Vector3(0f, -0.01f, 0f);

        var mf = polyGO.AddComponent<MeshFilter>();
        var mr = polyGO.AddComponent<MeshRenderer>();
        mf.sharedMesh = polyMesh;
        mr.sharedMaterial = GetPolygonMaterial(polygonType);

        // Physics collider so vehicles don't fall through.
        // Large flat polygons can trigger a PhysX large-triangle warning with a MeshCollider
        // if any two vertices are more than 500 units apart. Check the bounding diagonal
        // (not just one axis) to catch roughly-square large polygons.
        Bounds polyBounds = polyMesh.bounds;
        float polyDiag = Mathf.Sqrt(polyBounds.size.x * polyBounds.size.x + polyBounds.size.z * polyBounds.size.z);
        bool isLargePoly = polyDiag > 500f;
        if (isLargePoly)
        {
            var bc = polyGO.AddComponent<BoxCollider>();
            bc.center = polyBounds.center;
            bc.size = new Vector3(polyBounds.size.x, Mathf.Max(polyBounds.size.y, 0.1f), polyBounds.size.z);
        }
        else if (indices.Length >= 3)
        {
            var mc = polyGO.AddComponent<MeshCollider>();
            mc.sharedMesh = polyMesh;
            mc.convex = false;
        }
    }

    private void FlipTriangleWinding(Mesh mesh)
    {
        int[] tris = mesh.triangles;
        for (int i = 0; i < tris.Length; i += 3)
            (tris[i], tris[i + 2]) = (tris[i + 2], tris[i]);
        mesh.triangles = tris;
    }

    private Material GetPolygonMaterial(string type)
    {
        string t = (type ?? string.Empty).ToLowerInvariant();
        if (t.Contains("wood") && polygonWoodMaterial != null) return polygonWoodMaterial;
        if (t.Contains("terrain") && polygonTerrainMaterial != null) return polygonTerrainMaterial;
        if (t.Contains("roadside") && polygonRoadsideMaterial != null) return polygonRoadsideMaterial;
        if (t.Contains("residential") && polygonResidentialMaterial != null) return polygonResidentialMaterial;
        return polygonFallbackMaterial ?? GetFallbackMaterial();
    }

    private bool IsKnownPolygonType(string t)
    {
        if (string.IsNullOrEmpty(t)) return false;
        var l = t.ToLowerInvariant();
        return l.Contains("wood") || l.Contains("terrain") || l.Contains("roadside") || l.Contains("residential");
    }

    private Material GetFallbackMaterial() => new Material(Shader.Find("Standard"));

    private Mesh CreateLaneMesh(Vector3[] lanePoints, float roadWidth, float uvScaleU, float uvScaleV)
    {
        if (lanePoints.Length < 2) return null;

        int segmentCount = lanePoints.Length - 1;
        var vertices = new Vector3[segmentCount * 4];
        var uvs = new Vector2[segmentCount * 4];
        var triangles = new int[segmentCount * 6];

        float accumulatedDist = 0f;

        for (int i = 0; i < segmentCount; i++)
        {
            Vector3 p0 = lanePoints[i];
            Vector3 p1 = lanePoints[i + 1];
            Vector3 dir = (p1 - p0).normalized;
            float segLen = Vector3.Distance(p0, p1);
            Vector3 perp = new Vector3(-dir.z, 0f, dir.x) * (roadWidth * 0.5f);

            Vector3 leftA = p0 + perp;
            Vector3 rightA = p0 - perp;
            Vector3 leftB = p1 + perp;
            Vector3 rightB = p1 - perp;

            int v = i * 4;
            vertices[v] = leftA;
            vertices[v + 1] = rightA;
            vertices[v + 2] = leftB;
            vertices[v + 3] = rightB;

            int t = i * 6;
            triangles[t] = v;
            triangles[t + 1] = v + 2;
            triangles[t + 2] = v + 1;
            triangles[t + 3] = v + 2;
            triangles[t + 4] = v + 3;
            triangles[t + 5] = v + 1;

            float v0 = accumulatedDist / uvScaleV;
            float v1 = (accumulatedDist + segLen) / uvScaleV;
            float u0 = 0f;
            float u1 = roadWidth / uvScaleU;

            uvs[v] = new Vector2(u0, v0);
            uvs[v + 1] = new Vector2(u1, v0);
            uvs[v + 2] = new Vector2(u0, v1);
            uvs[v + 3] = new Vector2(u1, v1);

            accumulatedDist += segLen;
        }

        Mesh mesh = new Mesh
        {
            name = "LaneMesh",
            vertices = vertices,
            uv = uvs,
            triangles = triangles
        };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private Vector3[] ExtractLeftSideVertices(Mesh laneMesh)
    {
        Vector3[] v = laneMesh.vertices;
        if (v.Length < 4) return Array.Empty<Vector3>();
        int segCount = v.Length / 4;
        var pts = new Vector3[segCount + 1];
        for (int i = 0; i < segCount; i++) pts[i] = v[i * 4];
        pts[segCount] = v[(segCount - 1) * 4 + 2];
        return pts;
    }

    private Vector3[] ExtractRightSideVertices(Mesh laneMesh)
    {
        Vector3[] v = laneMesh.vertices;
        if (v.Length < 4) return Array.Empty<Vector3>();
        int segCount = v.Length / 4;
        var pts = new Vector3[segCount + 1];
        for (int i = 0; i < segCount; i++) pts[i] = v[i * 4 + 1];
        pts[segCount] = v[(segCount - 1) * 4 + 3];
        return pts;
    }

    // ───────────────────────── Decal spawning (SPAN based) ───────────────────
    private struct Span { public float s, e; public Span(float s, float e) { this.s = s; this.e = e; } }

    private void SpawnMarkingDecals(Vector3[] boundaryPts, string name, Transform parent)
    {
        if (roadMarkingMaterial == null)
        {
            Debug.LogWarning("No 'roadMarkingMaterial' assigned!");
            return;
        }
        if (boundaryPts == null || boundaryPts.Length < 2) return;

        const float stepSize = 3f;        // spacing between decals (m)
        const float sampleStep = 0.25f;     // resolution for span detection (m)
        Vector3 baseSize = new Vector3(0.1f, 0.2f, 3f);

        int count = boundaryPts.Length;
        float[] cum = new float[count];
        cum[0] = 0f;
        for (int i = 1; i < count; i++)
            cum[i] = cum[i - 1] + Vector3.Distance(boundaryPts[i - 1], boundaryPts[i]);

        float total = cum[count - 1];

        // 1) Detect outside spans
        var spans = new List<Span>();
        bool wasOutside = false;
        float spanStart = 0f;

        for (float d = 0f; d <= total; d += sampleStep)
        {
            GetPointOnPolyline(boundaryPts, cum, d, out Vector3 pos, out _);
            bool inside = IsInsideAnyJunction(pos);
            if (!inside && !wasOutside)
            {
                wasOutside = true;
                spanStart = d;
            }
            else if (inside && wasOutside)
            {
                wasOutside = false;
                spans.Add(new Span(spanStart, d));
            }
        }
        if (wasOutside) spans.Add(new Span(spanStart, total));

        // 2) Spawn decals inside spans only
        foreach (var sp in spans)
        {
            for (float d = sp.s; d <= sp.e; d += stepSize)
            {
                GetPointOnPolyline(boundaryPts, cum, d, out Vector3 center, out Vector3 dir);

                float halfLen = baseSize.z * 0.5f;

                // clamp projector length if near span edges
                float maxBack = Mathf.Min(halfLen, d - sp.s);
                float maxFwd = Mathf.Min(halfLen, sp.e - d);
                float length = maxBack + maxFwd;
                if (length < 0.11f) continue;

                // shift center so the shortened projector still lies fully in the span
                center += dir * (maxFwd - maxBack) * 0.5f;
                center += Vector3.up * 0.01f;

                GameObject decalObj = new GameObject($"{name}_Decal");
                decalObj.transform.SetParent(parent != null ? parent : roadNetworkRoot.transform);
                if (groundLayer >= 0) decalObj.layer = groundLayer;     // ★ NEW
                decalObj.transform.position = center;
                decalObj.transform.rotation = Quaternion.LookRotation(-dir, Vector3.up);

                var proj = decalObj.AddComponent<DecalProjector>();
                proj.material = roadMarkingMaterial;
                proj.size = new Vector3(baseSize.x, baseSize.y, length * 2f); // because length we computed is halfBack+halfFwd
                proj.drawDistance = 250f;
            }
        }
    }

    private static void GetPointOnPolyline(Vector3[] pts, float[] cum, float dist, out Vector3 pos, out Vector3 dir)
    {
        int idx = FindMarkingSegmentIndex(cum, dist);
        if (idx < 0 || idx >= pts.Length - 1)
        {
            pos = pts[pts.Length - 1];
            dir = Vector3.forward;
            return;
        }

        float segStart = cum[idx];
        float segLen = cum[idx + 1] - segStart;
        float t = segLen <= Mathf.Epsilon ? 0f : (dist - segStart) / segLen;

        Vector3 p0 = pts[idx];
        Vector3 p1 = pts[idx + 1];
        pos = Vector3.Lerp(p0, p1, t);
        dir = (p1 - p0).normalized;
    }

    private static int FindMarkingSegmentIndex(float[] cum, float dist)
    {
        int n = cum.Length;
        if (dist > cum[n - 1]) return -1;
        for (int i = 0; i < n - 1; i++)
            if (dist <= cum[i + 1]) return i;
        return -1;
    }

    private bool IsInsideAnyJunction(Vector3 worldPos)
    {
        Vector2 p = new Vector2(worldPos.x, worldPos.z);
        foreach (var poly in _junctionPolys2D)
        {
            if (PointInPolygon(p, poly)) return true;
        }
        return false;
    }

    private static bool PointInPolygon(Vector2 p, Vector2[] poly)
    {
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            Vector2 pi = poly[i];
            Vector2 pj = poly[j];
            bool intersect = ((pi.y > p.y) != (pj.y > p.y)) &&
                             (p.x < (pj.x - pi.x) * (p.y - pi.y) / (pj.y - pi.y + Mathf.Epsilon) + pi.x);
            if (intersect) inside = !inside;
        }
        return inside;
    }
}
#endif
