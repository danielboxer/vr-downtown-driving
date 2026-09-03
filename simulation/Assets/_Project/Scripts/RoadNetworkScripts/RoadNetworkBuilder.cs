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
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using NetFile;

public class RoadNetworkBuilder : MonoBehaviour
{
    public static RoadNetworkBuilder Singleton { get; private set; }
    private const string StopSignPrefabAssetPath = "Assets/_Project/Resources/Signs/StopSign.prefab";
    private const string SignLampPrefabAssetPath = "Assets/_Project/Resources/StreetLamps/Sign Street Lamp.prefab";
    private const string ThroughDecalMaterialAssetPath = "Assets/_Project/Materials/Through.mat";
    private const string LeftDecalMaterialAssetPath = "Assets/_Project/Materials/Left.mat";
    private const string ThroughLeftDecalMaterialAssetPath = "Assets/_Project/Materials/ThroughLeft.mat";
    private const string RightDecalMaterialAssetPath = "Assets/_Project/Materials/Right.mat";
    private const string ThroughRightDecalMaterialAssetPath = "Assets/_Project/Materials/ThroughRight.mat";
    private const string ThroughRightLeftDecalMaterialAssetPath = "Assets/_Project/Materials/ThroughRightLeft.mat";
    private const string StopLineDecalMaterialAssetPath = "Assets/_Project/Materials/StopLine.mat";
    private const string AsphaltMaterialAssetPath = "Assets/_Project/Materials/Asphalt.mat";
    private const string RoadMarkingMaterialAssetPath = "Assets/_Project/Materials/RoadMarking.mat";
    private const string WoodMaterialAssetPath = "Assets/_Project/Materials/WoodMaterial.mat";
    private const string RoadsideMaterialAssetPath = "Assets/_Project/Materials/RoadsideMaterial.mat";
    private const string ResidentialMaterialAssetPath = "Assets/_Project/Materials/ResidentialMaterial.mat";
    private static readonly HashSet<string> loggedMissingDefaultAssets = new();

    private void Awake()
    {
        Singleton = this;
        EnsureDefaultReferences();
    }

    private void Reset() => EnsureDefaultReferences();

    private void OnValidate() => EnsureDefaultReferences();

    public void InitializeInEditMode()
    {
        EnsureDefaultReferences();
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

    private void EnsureDefaultReferences()
    {
        bool changed = false;

        changed |= AssignDefaultReference(ref stopSignPrefab, StopSignPrefabAssetPath);
        changed |= AssignDefaultReference(ref throughDecalMaterial, ThroughDecalMaterialAssetPath);
        changed |= AssignDefaultReference(ref leftDecalMaterial, LeftDecalMaterialAssetPath);
        changed |= AssignDefaultReference(ref throughLeftDecalMaterial, ThroughLeftDecalMaterialAssetPath);
        changed |= AssignDefaultReference(ref rightDecalMaterial, RightDecalMaterialAssetPath);
        changed |= AssignDefaultReference(ref throughRightDecalMaterial, ThroughRightDecalMaterialAssetPath);
        changed |= AssignDefaultReference(ref throughRightLeftDecalMaterial, ThroughRightLeftDecalMaterialAssetPath);
        changed |= AssignDefaultReference(ref stopLineDecalMaterial, StopLineDecalMaterialAssetPath);
        changed |= AssignDefaultReference(ref roadSurfaceMaterial, AsphaltMaterialAssetPath);
        changed |= AssignDefaultReference(ref junctionSurfaceMaterial, AsphaltMaterialAssetPath);
        changed |= AssignDefaultReference(ref roadMarkingMaterial, RoadMarkingMaterialAssetPath);
        changed |= AssignDefaultReference(ref polygonWoodMaterial, WoodMaterialAssetPath);
        changed |= AssignDefaultReference(ref polygonTerrainMaterial, RoadsideMaterialAssetPath);
        changed |= AssignDefaultReference(ref polygonRoadsideMaterial, RoadsideMaterialAssetPath);
        changed |= AssignDefaultReference(ref polygonResidentialMaterial, ResidentialMaterialAssetPath);
        changed |= AssignDefaultReference(ref sidewalkWallMaterial, RoadsideMaterialAssetPath);
        changed |= AssignDefaultReference(ref lampWithSignPrefab, SignLampPrefabAssetPath);

        if (!changed || Application.isPlaying)
            return;

        EditorUtility.SetDirty(this);
        if (gameObject != null && gameObject.scene.IsValid())
            EditorSceneManager.MarkSceneDirty(gameObject.scene);
    }

    private static bool AssignDefaultReference<T>(ref T field, string assetPath) where T : UnityEngine.Object
    {
        if (field != null)
            return false;

        T asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
        if (asset == null)
        {
            if (loggedMissingDefaultAssets.Add(assetPath))
                Debug.LogWarning($"[RoadNetworkBuilder] Default asset not found at '{assetPath}'.");
            return false;
        }

        field = asset;
        return true;
    }

    [Header("Road Signs")]
    [Tooltip("Stop sign prefab placed at minor approaches of priority and allway-stop junctions.")]
    public GameObject stopSignPrefab;
    [Tooltip("Height offset above road surface for sign base placement.")]
    public float signHeightOffset = 0f;

    [Header("Lane Arrow Decals")]
    [Tooltip("Decal material for straight-ahead only lanes.")]
    public Material throughDecalMaterial;
    [Tooltip("Decal material for left-turn-only lanes.")]
    public Material leftDecalMaterial;
    [Tooltip("Decal material for left-turn + straight lanes.")]
    public Material throughLeftDecalMaterial;
    [Tooltip("Decal material for right-turn-only lanes.")]
    public Material rightDecalMaterial;
    [Tooltip("Decal material for right-turn + straight lanes.")]
    public Material throughRightDecalMaterial;
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
    [Tooltip("Distance threshold (meters) between primary and mirror traffic lights. When closer than this, both use the MiddleTrafficLight prefab instead of ThreeLight.")]
    public float middleTrafficLightThreshold = 12f;
    [Tooltip("How far past the road edge (meters) each traffic light pole is placed. Increase to move lights farther from the road.")]
    public float trafficLightCurbOffset = 1.5f;

    [Header("Evaluation Triggers")]
    [Tooltip("Minimum trigger width in lanes. Increase to cover more lanes on narrow roads.")]
    public float triggerMinimumLaneCount = 2f;
    [Tooltip("Stop-line trigger length along the approach direction.")]
    public float stopLineTriggerDepth = 3f;
    [Tooltip("Stop-line trigger offset past the stop line. Increase to move farther into the junction; decrease to move closer to the road.")]
    public float stopLineTriggerForwardOffset = 4.5f;
    [Tooltip("Turn trigger length along the exit-lane direction.")]
    public float turnTriggerDepth = 1.5f;
    [Tooltip("Turn trigger forward offset as a road-width multiplier. Increase to move farther toward the junction center.")]
    public float turnTriggerForwardWidthMultiplier = 1.125f;
    [Tooltip("Turn trigger side offset. Increase to move farther toward the right-side exit lane; decrease to move toward the junction center.")]
    public float turnTriggerSideOffset = 4.5f;

    [Tooltip("Add a BoxCollider to each stop sign post so vehicles can collide with it.")]
    public bool addStopSignColliders = true;
    [Tooltip("Size of the box collider added to each stop sign (width, height, depth).")]
    public Vector3 stopSignColliderSize = new Vector3(0.1f, 2.5f, 0.1f);
    [Tooltip("Local-space center of the stop sign box collider. Y=1.25 places the base at ground level.")]
    public Vector3 stopSignColliderCenter = new Vector3(0f, 1.25f, 0f);

    [Header("Street Lamps")]
    [Tooltip("Enable or disable street lamp generation along sidewalk edges.")]
    public bool generateStreetLamps = true;
    [Tooltip("Distance between consecutive lamps along each sidewalk edge (meters).")]
    public float lampSpacing = 80f;
    [Tooltip("Offset from the start of the flat sidewalk top inward to the lamp base (meters).")]
    public float lampCurbOffset = 1.0f;
    [Tooltip("Minimum distance from each junction end of an edge before placing a lamp (meters).")]
    public float lampJunctionClearance = 10f;
    [Tooltip("Add a BoxCollider to each street lamp so vehicles can collide with the pole.")]
    public bool addStreetLampColliders = true;
    [Tooltip("Size of the box collider added to each street lamp (width, height, depth).")]
    public Vector3 streetLampColliderSize = new Vector3(0.15f, 3f, 0.15f);
    [Tooltip("Local-space center of the street lamp box collider. Y=1.5 places the base at ground level.")]
    public Vector3 streetLampColliderCenter = new Vector3(0f, 1.5f, 0f);
    [Tooltip("Minimum world-space distance (XZ) between any two lamp bases. Prevents lamps from touching on narrow roads.")]
    public float lampMinProximity = 6f;
    [Tooltip("Lamp prefab (with speed limit sign) used for the single lamp placed closest to the midpoint of each block side. Falls back to the regular lamp if null.")]
    public GameObject lampWithSignPrefab;

    [Header("Decals")]
    [Tooltip("Draw distance (meters) applied to every generated DecalProjector (lane arrows, stop lines, lane marking dashes). Lower = cheaper; 50-100m is suitable for driving scenes.")]
    public float decalDrawDistance = 50f;


    private GameObject roadNetworkRoot;

    // ★ NEW: Ground-layer support --------------------------------------------
    private const string groundLayerName = "Ground";
    private int groundLayer = -1;
    // props on this layer are hidden from mirror cameras
    private const string envDetailLayerName = "EnvDetail";
    private int envDetailLayer = -1;
    // ------------------------------------------------------------------------

    public Dictionary<string, RoadJunctionData> junctionRecords;
    public Dictionary<string, RoadLaneData> laneRecords;
    public Dictionary<string, RoadEdgeData> edgeRecords;
    public Dictionary<string, PolygonShapeData> polygonShapes;

    private string sumoXmlFolderPath;

    private float minX = 0f, minY = 0f, maxX = 0f, maxY = 0f;
    private float originX = 0f, originY = 0f;

    private const float laneUvVerticalScale = 5f;
    private const float laneUvHorizontalScale = 1f;

    private const float DecalHeightOffset = 0.4f;
    private const float TriggerColliderHeight = 2f;
    private const float ArrowDecalSize = 2.5f;
    private const float StopLineDecalDepth = 0.5f;
    private const float StopSignCurbOffset = 0.5f;
    private const float MarkingStepSize = 3f;
    private const float MarkingSampleStep = 0.25f;
    private const float MarkingDecalWidth = 0.1f;
    private const float MarkingDecalDepth = 0.2f;
    private const float MarkingDecalLength = 3f;
    private const float PolygonTerrainYOffset = -0.02f;
    private const float PolygonDefaultYOffset = -0.01f;
    private const float LargePolygonDiagonalThreshold = 500f;
    private const float CurbTaperDistance = 2.0f;
    private const float CurbSubdivisionMaxLen = 1.0f;

    private readonly Dictionary<string, float> laneWidthMap = new();

    // NEW: cache junction polygons (Unity XZ plane)
    private readonly List<Vector2[]> _junctionPolys2D = new();


    private NetType _netFile;

    public void LoadSumoXmlFiles(string sumoFilesFolder)
    {
        EnsureDefaultReferences();

        if (roadNetworkRoot != null)
        {
            DestroyImmediate(roadNetworkRoot);
            roadNetworkRoot = null;
        }

        var oldRoot = GameObject.Find("RoadNetwork");
        if (oldRoot != null)
            DestroyImmediate(oldRoot);

        var oldJunctions = GameObject.Find("Junctions");
        if (oldJunctions != null)
            DestroyImmediate(oldJunctions);

        // legacy roots from an older layout
        var oldSigns = GameObject.Find("RoadSignsRoot");
        if (oldSigns != null)
            DestroyImmediate(oldSigns);

        var oldDecals = GameObject.Find("LaneDecalsRoot");
        if (oldDecals != null)
            DestroyImmediate(oldDecals);

        ParseSumoXmlFiles(sumoFilesFolder);
    }

    public void ParseSumoXmlFiles(string sumoFilesFolder)
    {
        EnsureDefaultReferences();

        laneWidthMap.Clear();
        _junctionPolys2D.Clear();
        laneRecords = new();
        edgeRecords = new();
        junctionRecords = new();
        polygonShapes = new();

        sumoXmlFolderPath = sumoFilesFolder;

        // ★ NEW: cache “Ground” layer index once
        if (groundLayer < 0) groundLayer = LayerMask.NameToLayer(groundLayerName);
        if (groundLayer < 0)
            Debug.LogWarning($"Layer \"{groundLayerName}\" does not exist - objects will keep their current layer.");
        if (envDetailLayer < 0) envDetailLayer = LayerMask.NameToLayer(envDetailLayerName);
        if (envDetailLayer < 0)
            Debug.LogWarning($"Layer \"{envDetailLayerName}\" does not exist - env-detail objects will keep their current layer.");

        if (roadNetworkRoot == null)
        {
            FindExistingRoot();
        }
        if (roadNetworkRoot == null)
        {
            roadNetworkRoot = new GameObject("RoadNetwork");
            if (groundLayer >= 0) roadNetworkRoot.layer = groundLayer;
        }

        var netFilePath = Directory.GetFiles(sumoXmlFolderPath, "*.net.xml").FirstOrDefault();
        var polyFilePath = Directory.GetFiles(sumoXmlFolderPath, "*.poly.xml").FirstOrDefault();

        if (netFilePath == null)
        {
            Debug.LogError($"[RoadNetworkBuilder] No *.net.xml found in '{sumoXmlFolderPath}'.");
            return;
        }

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
                // internal junction edges (id starts with ':') have no 'from' by design
                if (!et.Id.StartsWith(":"))
                    Debug.LogWarning($"Edge {et.Id} has no 'from'. Skipping.");
                continue;
            }

            var newEdge = new RoadEdgeData(et.Id, et.From, et.To, et.Priority, et.Shape);
            edgeRecords[et.Id] = newEdge;

            if (et.Lane == null) continue;

            foreach (LaneType laneType in et.Lane)
            {
                float width = laneType.WidthSpecified ? laneType.Width : DefaultLaneWidth;
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

    public bool FindExistingRoot()
    {
        if (roadNetworkRoot != null) return true;
        var existing = GameObject.Find("RoadNetwork");
        if (existing != null) { roadNetworkRoot = existing; return true; }
        return false;
    }

    public void GenerateRoadsAndJunctions()
    {
        EnsureDefaultReferences();

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

                float laneWidth = GetMappedLaneWidth(laneData.laneId);

                Mesh laneMesh = CreateLaneMesh(lanePoints, laneWidth, laneUvHorizontalScale, laneUvVerticalScale);
                if (laneMesh == null) continue;

                var laneObj = new GameObject($"LaneSegment_{laneCounter++}");
                laneObj.transform.SetParent(roadNetworkRoot.transform);
                if (groundLayer >= 0) laneObj.layer = groundLayer;  // ★ NEW
                var mf = laneObj.AddComponent<MeshFilter>();
                var mr = laneObj.AddComponent<MeshRenderer>();
                mf.sharedMesh = laneMesh;
                mr.sharedMaterial = roadSurfaceMaterial ?? GetFallbackMaterial();

                var laneCol = laneObj.AddComponent<MeshCollider>();
                laneCol.sharedMesh = laneMesh;
                laneCol.convex = false;

                // Skip lane markings on the outer road edges (where curbs are)
                bool isLeftmost = laneData.laneIndex == maxLaneIndex;
                bool isRightmost = laneData.laneIndex == 0;

                // an opposite-direction edge means the left side is a median, not the road boundary
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
            jMf.sharedMesh = junctionMesh;
            jMr.sharedMaterial = junctionSurfaceMaterial ?? GetFallbackMaterial();
        }

        // ★ NEW: make sure every child built above is on the Ground layer
        SetLayerRecursively(roadNetworkRoot, groundLayer);

        if (markGeneratedAsStatic) SetStaticRecursively(roadNetworkRoot);

        if (generateCurbs && sidewalkHeight > 0.01f)
            GenerateCurbs();
    }

    private static void SetLayerRecursively(GameObject obj, int layer)
    {
        if (layer < 0) return;
        obj.layer = layer;
        foreach (Transform child in obj.transform)
            SetLayerRecursively(child.gameObject, layer);
    }

    // ContributeGI excluded, UV-unwrapping this many procedural meshes for lightmaps crashes
#pragma warning disable CS0618 // NavigationStatic/OffMeshLinkGeneration deprecated but still functional
    private const StaticEditorFlags RoadStaticFlags =
        StaticEditorFlags.OccluderStatic |
        StaticEditorFlags.OccludeeStatic |
        StaticEditorFlags.BatchingStatic |
        StaticEditorFlags.NavigationStatic |
        StaticEditorFlags.OffMeshLinkGeneration |
        StaticEditorFlags.ReflectionProbeStatic;
#pragma warning restore CS0618

    private static void SetStaticRecursively(GameObject obj)
    {
        // polygon buildings already set ContributeGI, don't clear it
        var existing = GameObjectUtility.GetStaticEditorFlags(obj);
        GameObjectUtility.SetStaticEditorFlags(obj, RoadStaticFlags | (existing & StaticEditorFlags.ContributeGI));
        foreach (Transform child in obj.transform)
            SetStaticRecursively(child.gameObject);
    }


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

            var firstLane = lanes[0];
            if (firstLane.shapePoints.Count >= 2)
            {
                float w = GetMappedLaneWidth(firstLane.laneId);
                var edgePts = ComputeLaneEdge(firstLane, w, false);
                if (edgePts.Length >= 2)
                    BuildCurbStrip(edgePts, $"Curb_{curbIdx++}", curbRoot.transform, -1);
            }

            // skip when an opposite edge exists, that side is the median
            if (!hasOpposite)
            {
                var lastLane = lanes[lanes.Count - 1];
                if (lastLane.shapePoints.Count >= 2)
                {
                    float w = GetMappedLaneWidth(lastLane.laneId);
                    var edgePts = ComputeLaneEdge(lastLane, w, true);
                    if (edgePts.Length >= 2)
                        BuildCurbStrip(edgePts, $"Curb_{curbIdx++}", curbRoot.transform, 1);
                }
            }
        }
        SetLayerRecursively(curbRoot, envDetailLayer);
    }

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

    private Vector3[] ComputeLaneEdge(RoadLaneData lane, float width, bool left, float extraOffset = 0f, float height = 0f)
    {
        int n = lane.shapePoints.Count;
        var pts = new Vector3[n];
        float totalOffset = width * 0.5f + extraOffset;

        for (int i = 0; i < n; i++)
        {
            Vector3 center = ToUnity(lane.shapePoints[i][0], lane.shapePoints[i][1]);

            Vector3 dir;
            if (i < n - 1)
                dir = (ToUnity(lane.shapePoints[i + 1][0], lane.shapePoints[i + 1][1]) - center).normalized;
            else
                dir = (center - ToUnity(lane.shapePoints[i - 1][0], lane.shapePoints[i - 1][1])).normalized;

            // Perpendicular (left of travel direction)
            Vector3 perp = new Vector3(-dir.z, 0f, dir.x);
            pts[i] = center + perp * totalOffset * (left ? 1f : -1f);
            if (height != 0f) pts[i].y = height;
        }
        return pts;
    }

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

    private void BuildCurbStrip(Vector3[] edgePts, string name, Transform parent, int outwardSign)
    {
        edgePts = SubdividePolyline(edgePts, CurbSubdivisionMaxLen);

        int segCount = edgePts.Length - 1;
        Vector2[] profile = BuildCurbProfile();
        int profileCount = profile.Length;
        int strips = profileCount - 1;

        int vertsPerSeg = profileCount * 2;
        int trisPerSeg = strips * 6;
        var verts = new Vector3[segCount * vertsPerSeg];
        var tris = new int[segCount * trisPerSeg];

        float[] cumDist = new float[edgePts.Length];
        cumDist[0] = 0f;
        for (int k = 1; k < edgePts.Length; k++)
            cumDist[k] = cumDist[k - 1] + Vector3.Distance(edgePts[k], edgePts[k - 1]);
        float totalLen = cumDist[cumDist.Length - 1];

        for (int i = 0; i < segCount; i++)
        {
            Vector3 a = edgePts[i];
            Vector3 b = edgePts[i + 1];

            float dA = Mathf.Min(cumDist[i], totalLen - cumDist[i]);
            float dB = Mathf.Min(cumDist[i + 1], totalLen - cumDist[i + 1]);
            float hA = Mathf.Clamp01(dA / CurbTaperDistance) * sidewalkHeight;
            float hB = Mathf.Clamp01(dB / CurbTaperDistance) * sidewalkHeight;
            float wA = Mathf.Clamp01(dA / CurbTaperDistance);
            float wB = Mathf.Clamp01(dB / CurbTaperDistance);

            Vector3 dir = (b - a).normalized;
            Vector3 outDir = new Vector3(-dir.z, 0f, dir.x) * outwardSign;

            int vi = i * vertsPerSeg;
            int ti = i * trisPerSeg;

            // width taper applies only to the road side so the mesh never degenerates
            for (int p = 0; p < profileCount; p++)
            {
                float offset = profile[p].x;
                float hFrac = profile[p].y;
                float scaledOffsetA = offset <= 0f ? offset * wA : offset;
                float scaledOffsetB = offset <= 0f ? offset * wB : offset;

                verts[vi + p * 2] = new Vector3(
                    a.x + outDir.x * scaledOffsetA, hA * hFrac, a.z + outDir.z * scaledOffsetA);
                verts[vi + p * 2 + 1] = new Vector3(
                    b.x + outDir.x * scaledOffsetB, hB * hFrac, b.z + outDir.z * scaledOffsetB);
            }

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

        // winding is correct for outwardSign=+1 only
        if (outwardSign < 0)
            FlipTriangleWinding(curbMesh);

        curbMesh.RecalculateNormals();
        curbMesh.RecalculateBounds();

        GameObject go = new GameObject(name);
        go.transform.SetParent(parent);

        go.AddComponent<MeshFilter>().sharedMesh = curbMesh;
        var curbMr = go.AddComponent<MeshRenderer>();
        curbMr.sharedMaterial = sidewalkWallMaterial != null ? sidewalkWallMaterial : GetPolygonMaterial("terrain");

        // non-convex is fine, curbs never move
        var col = go.AddComponent<MeshCollider>();
        col.sharedMesh = curbMesh;
        col.convex = false;
    }

    private Vector2[] BuildCurbProfile()
    {
        const int arcSegments = 4;
        float h = sidewalkHeight;
        if (h < 0.001f) return new[] { new Vector2(-innerSlopeWidth, 0f), new Vector2(curbWidth + outerSlopeWidth, 0f) };

        var pts = new List<Vector2>();

        pts.Add(new Vector2(-innerSlopeWidth, 0f));

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

        pts.Add(new Vector2(curbWidth + outerSlopeWidth, 0f));

        for (int i = 0; i < pts.Count; i++)
            pts[i] = new Vector2(pts[i].x, pts[i].y / h);

        return pts.ToArray();
    }

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

    private void DestroyChildByName(string exactName)
    {
        if (roadNetworkRoot == null) return;
        var t = roadNetworkRoot.transform.Find(exactName);
        if (t != null) DestroyImmediate(t.gameObject);
    }

    public void DeleteRoadObjects()
    {
        DestroyChildrenByPrefix("LaneSegment_", "Junction_");
        DestroyChildByName("Curbs");
    }

    public void DeletePolygonObjects()
    {
        DestroyChildrenByPrefix("Shape_");
    }

    public void DeleteTrafficLightObjects()
    {
        var junctions = GameObject.Find("Junctions");
        if (junctions != null)
            DestroyImmediate(junctions);
    }

    private struct TurnTriggerBuildData
    {
        public string edgeId;
        public Vector3 approachDir;
        public Vector3 stopLinePos;
        public float totalRoadWidth;
    }

    private static Vector3 NormalizeHorizontal(Vector3 dir)
    {
        dir.y = 0f;
        return dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.zero;
    }

    private const float DefaultLaneWidth = 3.2f;

    private float GetMappedLaneWidth(string laneId) =>
        laneWidthMap.TryGetValue(laneId, out float w) ? w : DefaultLaneWidth;

    private static float GetLaneWidth(RoadLaneData lane) =>
        lane.laneWidth > 0 ? (float)lane.laneWidth : DefaultLaneWidth;

    private static Vector3 RightDirection(Vector3 forward) => new(forward.z, 0f, -forward.x);

    private bool GetEdgeApproachGeometry(
        RoadEdgeData edge,
        out Vector3 laneEnd, out Vector3 approachDir,
        out float laneWidth, out float totalRoadWidth)
    {
        laneEnd = Vector3.zero;
        approachDir = Vector3.forward;
        laneWidth = DefaultLaneWidth;
        totalRoadWidth = 0f;

        var lanes = edge.GetLaneDataList();
        if (lanes.Count == 0) return false;
        var lane = lanes[0];
        if (lane.shapePoints == null || lane.shapePoints.Count < 2) return false;

        foreach (var ln in lanes)
            totalRoadWidth += GetLaneWidth(ln);

        laneWidth = GetLaneWidth(lane);

        int last = lane.shapePoints.Count - 1;
        laneEnd = ToUnity(lane.shapePoints[last][0], lane.shapePoints[last][1]);
        Vector3 prevPt = ToUnity(lane.shapePoints[last - 1][0], lane.shapePoints[last - 1][1]);
        approachDir = (laneEnd - prevPt).normalized;

        return true;
    }

    private void AddTrafficLightCollider(GameObject go, bool isMirrored)
    {
        if (!addTrafficLightColliders) return;

        if (isMirrored)
        {
            GameObject colHost = new GameObject("Collider");
            colHost.transform.SetParent(go.transform);
            colHost.transform.localPosition = Vector3.zero;
            colHost.transform.localRotation = Quaternion.identity;
            colHost.transform.localScale = new Vector3(-1f, 1f, 1f);
            var col = colHost.AddComponent<BoxCollider>();
            col.size = trafficLightColliderSize;
            col.center = trafficLightColliderCenter;
        }
        else
        {
            var col = go.AddComponent<BoxCollider>();
            col.size = trafficLightColliderSize;
            col.center = trafficLightColliderCenter;
        }
    }

    private bool ComputeEdgeLightPositions(
        RoadEdgeData edge, RoadEdgeData excludeEdge, string junctionId, Vector3 junctionCenter,
        out Vector3 primaryPos, out Vector3 mirrorPos)
    {
        primaryPos = mirrorPos = junctionCenter;

        if (!GetEdgeApproachGeometry(edge, out Vector3 laneEnd, out Vector3 dir, out float lw, out float totalWidth))
            return false;

        float forwardDist = Vector3.Dot(junctionCenter - laneEnd, dir);
        Vector3 farSideBase = laneEnd + dir * (2f * forwardDist);
        Vector3 rightDir = RightDirection(dir);
        float curbOffset = trafficLightCurbOffset;

        primaryPos = farSideBase + rightDir * (lw * 0.5f + curbOffset);

        mirrorPos = primaryPos - rightDir * (2f * totalWidth + 2f * curbOffset);

        float bestAlignment = 0.5f;
        foreach (var cand in edgeRecords.Values)
        {
            var candTo = cand.GetToJunction();
            if (candTo == null || candTo.junctionId != junctionId) continue;
            if (cand == edge || cand == excludeEdge) continue;

            if (!GetEdgeApproachGeometry(cand, out Vector3 opEnd, out Vector3 opDir, out float opLaneW, out _))
                continue;

            float alignment = Vector3.Dot(opDir, -dir);
            if (alignment > bestAlignment)
            {
                bestAlignment = alignment;
                Vector3 opRightDir = RightDirection(opDir);
                mirrorPos = opEnd + opRightDir * (opLaneW * 0.5f + curbOffset);
            }
        }

        return true;
    }

    private float GetTriggerWidth(float roadWidth, float laneWidth)
    {
        if (laneWidth <= 0f)
            laneWidth = DefaultLaneWidth;

        return Mathf.Max(roadWidth, laneWidth * Mathf.Max(1f, triggerMinimumLaneCount));
    }

    private bool CreateTurnDirectionTrigger(Transform parent, string junctionId, string suffix, Vector3 stopLinePos, Vector3 approachDir, float totalRoadWidth)
    {
        approachDir = NormalizeHorizontal(approachDir);
        if (approachDir == Vector3.zero)
            return false;

        if (totalRoadWidth <= 0f)
            totalRoadWidth = GetTriggerWidth(0f, DefaultLaneWidth);

        Vector3 tdRightDir = RightDirection(approachDir);

        GameObject tdGO = new GameObject($"TurnTrigger_{junctionId}_{suffix}");
        tdGO.transform.SetParent(parent);
        tdGO.transform.position = stopLinePos;
        tdGO.transform.rotation = Quaternion.LookRotation(tdRightDir, Vector3.up);

        var tdBox = tdGO.AddComponent<BoxCollider>();
        tdBox.isTrigger = true;
        tdBox.size = new Vector3(totalRoadWidth, TriggerColliderHeight, Mathf.Max(0.1f, turnTriggerDepth));
        tdBox.center = new Vector3(
            -(totalRoadWidth * turnTriggerForwardWidthMultiplier),
            0f,
            turnTriggerSideOffset);

        var tdTrigger = tdGO.AddComponent<TurnDirectionTrigger>();
        tdTrigger.junctionId = junctionId;
        tdTrigger.approachDir = approachDir;

        return true;
    }

    private void CreateMissingThreeWayTurnTrigger(List<TurnTriggerBuildData> turnTriggers, Transform parent, string junctionId, Vector3 junctionCenter)
    {
        // Add the missing fourth trigger for T-intersections.
        if (turnTriggers.Count != 3)
            return;

        int firstOpposingIndex = -1;
        int secondOpposingIndex = -1;
        float mostOpposingDot = 1f;

        for (int i = 0; i < turnTriggers.Count; i++)
        {
            Vector3 a = NormalizeHorizontal(turnTriggers[i].approachDir);
            if (a == Vector3.zero) continue;

            for (int j = i + 1; j < turnTriggers.Count; j++)
            {
                Vector3 b = NormalizeHorizontal(turnTriggers[j].approachDir);
                if (b == Vector3.zero) continue;

                float dot = Vector3.Dot(a, b);
                if (dot < mostOpposingDot)
                {
                    mostOpposingDot = dot;
                    firstOpposingIndex = i;
                    secondOpposingIndex = j;
                }
            }
        }

        if (firstOpposingIndex < 0 || secondOpposingIndex < 0 || mostOpposingDot > -0.5f)
            return;

        int unpairedIndex = -1;
        for (int i = 0; i < turnTriggers.Count; i++)
        {
            if (i != firstOpposingIndex && i != secondOpposingIndex)
            {
                unpairedIndex = i;
                break;
            }
        }

        if (unpairedIndex < 0)
            return;

        TurnTriggerBuildData source = turnTriggers[unpairedIndex];
        Vector3 sourceApproachDir = NormalizeHorizontal(source.approachDir);
        if (sourceApproachDir == Vector3.zero)
            return;

        Vector3 missingApproachDir = -sourceApproachDir;

        float stopLineDistance = Vector3.Dot(junctionCenter - source.stopLinePos, sourceApproachDir);
        if (stopLineDistance <= 0.1f)
            stopLineDistance = Vector3.Distance(junctionCenter, source.stopLinePos);
        if (stopLineDistance <= 0.1f)
            stopLineDistance = Mathf.Max(source.totalRoadWidth, GetTriggerWidth(0f, DefaultLaneWidth));

        Vector3 virtualStopLinePos = junctionCenter - missingApproachDir * stopLineDistance;
        virtualStopLinePos.y = source.stopLinePos.y;

        CreateTurnDirectionTrigger(
            parent,
            junctionId,
            $"MissingOppositeOfE{source.edgeId}",
            virtualStopLinePos,
            missingApproachDir,
            source.totalRoadWidth);
    }

    public void GenerateTrafficLights()
    {
        EnsureDefaultReferences();

        if (_netFile == null) { Debug.LogError("Net file not loaded."); return; }

        GameObject tlPrefab = Resources.Load<GameObject>("TrafficLight/ThreeLight");
        if (tlPrefab == null)
        {
            Debug.LogError("TrafficLight/ThreeLight prefab not found in Resources.");
            return;
        }

        // used when primary and mirror end up too close together
        GameObject middlePrefab = Resources.Load<GameObject>("TrafficLight/MiddleTrafficLight");
        if (middlePrefab == null)
            Debug.LogWarning("[RoadNetworkBuilder] TrafficLight/MiddleTrafficLight prefab not found; will fall back to ThreeLight on narrow roads.");

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

        var tlJunctionIds = new HashSet<string>();
        foreach (TlLogicType tl in _netFile.TlLogic)
            tlJunctionIds.Add(tl.Id);

        // SimulationController expects a scene-root "Junctions" object
        GameObject junctionsRoot = new GameObject("Junctions");

        foreach (string jId in tlJunctionIds)
        {
            if (!junctionRecords.TryGetValue(jId, out RoadJunctionData jData)) continue;
            if (!tlConnections.TryGetValue(jId, out var linkToEdge)) continue;

            Vector3 junctionCenter = ToUnity(jData.xPos, jData.yPos);

            GameObject junctionGO = new GameObject(jId);
            junctionGO.transform.SetParent(junctionsRoot.transform);
            junctionGO.transform.position = Vector3.zero;

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

            var turnTriggerBuildData = new List<TurnTriggerBuildData>();

            foreach (var edgeEntry in edgeToLinks)
            {
                string edgeId = edgeEntry.Key;
                List<int> linkIndices = edgeEntry.Value;
                linkIndices.Sort();

                Vector3 laneEndPos = junctionCenter;
                Vector3 approachDir = Vector3.forward;
                Vector3 stopLinePos = junctionCenter;
                float totalRoadWidth = 0f;
                float laneW = DefaultLaneWidth;

                if (edgeRecords.TryGetValue(edgeId, out RoadEdgeData edgeData)
                    && GetEdgeApproachGeometry(edgeData, out Vector3 laneEnd, out approachDir, out laneW, out totalRoadWidth))
                {
                    stopLinePos = laneEnd;

                    // Move the light to the far side of the junction (opposite the stop line)
                    float forwardDist = Vector3.Dot(junctionCenter - laneEnd, approachDir);
                    Vector3 farSideBase = laneEnd + approachDir * (2f * forwardDist);
                    Vector3 rightDir = RightDirection(approachDir);

                    laneEndPos = farSideBase + rightDir * (laneW * 0.5f + trafficLightCurbOffset);
                }
                if (totalRoadWidth <= 0f) totalRoadWidth = GetTriggerWidth(0f, laneW);
                float triggerRoadWidth = GetTriggerWidth(totalRoadWidth, laneW);

                // the opposing edge's lane endpoint beats estimating from totalRoadWidth on asymmetric junctions
                Vector3 mirrorRightDir = RightDirection(approachDir);
                // Default fallback: assume symmetric road (incoming width == opposing width)
                Vector3 leftSidePos = laneEndPos - mirrorRightDir * (2f * totalRoadWidth + 2f * trafficLightCurbOffset);
                bool foundOpposingEdge = false;
                {
                    float bestOpposingAlignment = 0.5f; // minimum dot-product threshold
                    foreach (var cand in edgeRecords.Values)
                    {
                        var candTo = cand.GetToJunction();
                        if (candTo == null || candTo.junctionId != jId) continue;
                        if (cand == edgeData) continue;

                        if (!GetEdgeApproachGeometry(cand, out Vector3 opLaneEnd, out Vector3 opApproachDir, out float opLaneW, out _))
                            continue;

                        float alignment = Vector3.Dot(opApproachDir, -approachDir);
                        if (alignment > bestOpposingAlignment)
                        {
                            bestOpposingAlignment = alignment;
                            foundOpposingEdge = true;
                            Vector3 opRightDir = RightDirection(opApproachDir);
                            leftSidePos = opLaneEnd + opRightDir * (opLaneW * 0.5f + trafficLightCurbOffset);
                        }
                    }
                }

                    // no opposing edge, so place the stem's lights inward from the adjacent ones to avoid overlap
                if (!foundOpposingEdge)
                {
                    Vector3 rightDir = RightDirection(approachDir);
                    float curbOffset = trafficLightCurbOffset;

                    var adjacentPositions = new List<Vector3>();
                    foreach (var cand in edgeRecords.Values)
                    {
                        var candTo = cand.GetToJunction();
                        if (candTo == null || candTo.junctionId != jId) continue;
                        if (cand == edgeData) continue;

                        if (ComputeEdgeLightPositions(cand, edgeData, jId, junctionCenter, out var candPrimary, out var candMirror))
                        {
                            adjacentPositions.Add(candPrimary);
                            adjacentPositions.Add(candMirror);
                        }
                    }

                    // find the right-most and left-most adjacent lights on the stem's far side
                    Vector3? bestRight = null;
                    Vector3? bestLeft = null;
                    float bestRightScore = float.NegativeInfinity;
                    float bestLeftScore = float.NegativeInfinity;

                    foreach (var pos in adjacentPositions)
                    {
                        float fwd = Vector3.Dot(pos - stopLinePos, approachDir);
                        if (fwd < 0f) continue; // behind the stop line, not on far side

                        float lateral = Vector3.Dot(pos - stopLinePos, rightDir);
                        if (lateral > 0f && fwd > bestRightScore)
                        {
                            bestRightScore = fwd;
                            bestRight = pos;
                        }
                        if (lateral < 0f && fwd > bestLeftScore)
                        {
                            bestLeftScore = fwd;
                            bestLeft = pos;
                        }
                    }

                    if (bestRight.HasValue)
                        laneEndPos = bestRight.Value - rightDir * curbOffset;
                    if (bestLeft.HasValue)
                        leftSidePos = bestLeft.Value + rightDir * curbOffset;
                }

                int primaryLink = linkIndices[0];
                float tlSeparation = Vector3.Distance(laneEndPos, leftSidePos);
                // T-intersections have no opposing roadway, always use the middle prefab
                bool useMiddlePrefab = middlePrefab != null && (!foundOpposingEdge || tlSeparation < middleTrafficLightThreshold);
                GameObject activePrefab = useMiddlePrefab ? middlePrefab : tlPrefab;
                GameObject head;

                if (!foundOpposingEdge && useMiddlePrefab)
                {
                    Vector3 centerPos = (laneEndPos + leftSidePos) * 0.5f;
                    head = (GameObject)PrefabUtility.InstantiatePrefab(activePrefab);
                    head.name = $"Head{primaryLink}";
                    head.transform.SetParent(junctionGO.transform);
                    head.transform.position = centerPos;
                    head.transform.rotation = Quaternion.LookRotation(-approachDir, Vector3.up);
                    AddTrafficLightCollider(head, isMirrored: false);
                    SetLayerRecursively(head, envDetailLayer);
                }
                else
                {
                    // MiddleTrafficLight faces the other way, so swap head and mirror positions
                    Vector3 headPos = useMiddlePrefab ? leftSidePos : laneEndPos;
                    Vector3 mirrorPos = useMiddlePrefab ? laneEndPos : leftSidePos;
                    head = (GameObject)PrefabUtility.InstantiatePrefab(activePrefab);
                    head.name = $"Head{primaryLink}";
                    head.transform.SetParent(junctionGO.transform);
                    head.transform.position = headPos;
                    // face toward oncoming traffic
                    head.transform.rotation = Quaternion.LookRotation(-approachDir, Vector3.up);
                    AddTrafficLightCollider(head, isMirrored: false);
                    SetLayerRecursively(head, envDetailLayer);

                    // child of primary so the state syncs
                    GameObject mirror = (GameObject)PrefabUtility.InstantiatePrefab(activePrefab);
                    mirror.name = "Mirror";
                    mirror.transform.SetParent(head.transform);
                    mirror.transform.position = mirrorPos;
                    mirror.transform.rotation = Quaternion.LookRotation(-approachDir, Vector3.up);
                    mirror.transform.localScale = new Vector3(-1f, 1f, 1f);
                    AddTrafficLightCollider(mirror, isMirrored: true);
                }

                GameObject stopLineGO = new GameObject($"StopLine_{jId}_E{edgeId}");
                stopLineGO.transform.SetParent(junctionGO.transform);
                stopLineGO.transform.position = stopLinePos;
                stopLineGO.transform.rotation = Quaternion.LookRotation(approachDir, Vector3.up);

                var slBox = stopLineGO.AddComponent<BoxCollider>();
                slBox.isTrigger = true;
                slBox.size = new Vector3(triggerRoadWidth, TriggerColliderHeight, Mathf.Max(0.1f, stopLineTriggerDepth));
                slBox.center = new Vector3(
                    -(triggerRoadWidth * 0.5f - laneW * 0.5f),
                    0f,
                    stopLineTriggerForwardOffset);

                var slTrigger = stopLineGO.AddComponent<StopLineTrigger>();
                slTrigger.junctionId = jId;
                slTrigger.linkIndex = primaryLink;

                if (CreateTurnDirectionTrigger(junctionGO.transform, jId, $"E{edgeId}", stopLinePos, approachDir, triggerRoadWidth))
                {
                    turnTriggerBuildData.Add(new TurnTriggerBuildData
                    {
                        edgeId = edgeId,
                        approachDir = approachDir,
                        stopLinePos = stopLinePos,
                        totalRoadWidth = triggerRoadWidth
                    });
                }

                for (int i = 1; i < linkIndices.Count; i++)
                {
                    int linkIdx = linkIndices[i];
                    GameObject stub = new GameObject($"Head{linkIdx}");
                    stub.transform.SetParent(junctionGO.transform);
                    stub.transform.position = head.transform.position;

                    // Create minimal children so SimulationController's SetSignalState works
                    new GameObject("green_light").transform.SetParent(stub.transform);
                    new GameObject("yellow_light").transform.SetParent(stub.transform);
                    new GameObject("red_light").transform.SetParent(stub.transform);
                }
            }

            CreateMissingThreeWayTurnTrigger(turnTriggerBuildData, junctionGO.transform, jId, junctionCenter);
        }
        if (markGeneratedAsStatic) SetStaticRecursively(junctionsRoot);

        // EnablePicking first clears stale descendant state that would show the mixed cube icon
        SceneVisibilityManager.instance.EnablePicking(junctionsRoot, true);
        SceneVisibilityManager.instance.DisablePicking(junctionsRoot, true);
        Debug.Log($"[Sumo2Unity] Generated traffic lights for {tlJunctionIds.Count} junctions under 'Junctions' root.");
    }

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

    public void DeleteRoadSignObjects()
    {
        if (FindExistingRoot())
        {
            var signsChild = roadNetworkRoot.transform.Find("RoadSigns");
            if (signsChild != null)
                DestroyImmediate(signsChild.gameObject);
        }
        // Also clean up any legacy scene-root from an older generation run
        var legacyRoot = GameObject.Find("RoadSignsRoot");
        if (legacyRoot != null)
            DestroyImmediate(legacyRoot);
    }

    public void GenerateRoadSigns()
    {
        EnsureDefaultReferences();

        if (_netFile == null) { Debug.LogError("Net file not loaded."); return; }

        if (stopSignPrefab == null)
        {
            stopSignPrefab = Resources.Load<GameObject>("Signs/StopSign");
        }

        if (stopSignPrefab == null)
        {
            Debug.LogError("[RoadNetworkBuilder] stopSignPrefab is not assigned and 'Signs/StopSign' was not found in Resources.");
            return;
        }

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

        // own child so signs can be deleted independently of TL heads
        GameObject signsRoot = new GameObject("RoadSigns");
        signsRoot.transform.SetParent(roadNetworkRoot.transform);
        int placedCount = 0;

        foreach (var jData in junctionRecords.Values)
        {
            bool isAllwayStop = jData.junctionType == JunctionTypeType.AllwayStop;
            bool isPriority = jData.junctionType == JunctionTypeType.Priority
                             || jData.junctionType == JunctionTypeType.PriorityStop;

            if (!isAllwayStop && !isPriority) continue;
            if (!edgesByToJunction.TryGetValue(jData.junctionId, out var incomingEdges)) continue;
            if (incomingEdges.Count == 0) continue;

            // approaches below the maximum incoming priority are minor roads
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
                if (isPriority && edge.GetEdgePriority() >= maxPriority) continue;

                if (!GetEdgeApproachGeometry(edge, out Vector3 laneEnd, out Vector3 approachDir, out float laneW, out _))
                    continue;

                // Place sign on the right curb (same convention as traffic lights)
                Vector3 rightDir = RightDirection(approachDir);
                Vector3 signPos = laneEnd + rightDir * (laneW * 0.5f + StopSignCurbOffset);
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
        SetLayerRecursively(signsRoot, envDetailLayer);
        Debug.Log($"[Sumo2Unity] Placed {placedCount} stop signs under 'Junctions/RoadSigns'.");
    }

    public void DeleteStreetLampObjects()
    {
        if (!FindExistingRoot()) return;
        var lampsChild = roadNetworkRoot.transform.Find("StreetLamps");
        if (lampsChild != null) DestroyImmediate(lampsChild.gameObject);
    }

    public void GenerateStreetLamps()
    {
        EnsureDefaultReferences();

        if (!FindExistingRoot())
        {
            Debug.LogError("[RoadNetworkBuilder] No road network root found. Generate roads first.");
            return;
        }

        var existingLamps = roadNetworkRoot.transform.Find("StreetLamps");
        if (existingLamps != null) DestroyImmediate(existingLamps.gameObject);

        GameObject lampPrefab = Resources.Load<GameObject>("StreetLamps/Street Lamp 02");
        if (lampPrefab == null)
        {
            Debug.LogError("[RoadNetworkBuilder] Prefab not found at Resources/StreetLamps/Street Lamp 02.");
            return;
        }

        GameObject signLampPrefab = lampWithSignPrefab;
        if (signLampPrefab == null)
            signLampPrefab = Resources.Load<GameObject>("StreetLamps/Sign Street Lamp");
        if (signLampPrefab == null)
            Debug.LogWarning("[RoadNetworkBuilder] Sign Street Lamp prefab not found; midpoint lamps will use the regular prefab.");

        GameObject lampsRoot = new GameObject("StreetLamps");
        lampsRoot.transform.SetParent(roadNetworkRoot.transform);

        int lampCount = 0;
        // half lane width reaches the lane edge, innerSlopeWidth clears the ramp, lampCurbOffset sits on the flat top
        float sidewalkInset = innerSlopeWidth + lampCurbOffset;

        // shared so lamps on opposite sidewalks of a narrow road don't land on each other
        var placedLampPositions = new List<Vector3>();

        foreach (var edgeData in edgeRecords.Values)
        {
            var lanes = edgeData.GetLaneDataList();
            if (lanes.Count == 0) continue;

            bool hasOpposite = HasOppositeEdge(edgeData);

            var firstLane = lanes[0];
            if (firstLane.shapePoints.Count >= 2)
            {
                float w = GetMappedLaneWidth(firstLane.laneId);
                var sidewalkPts = ComputeLaneEdge(firstLane, w, false, sidewalkInset, sidewalkHeight);
                PlaceLampsAlongEdge(sidewalkPts, lampPrefab, signLampPrefab, lampsRoot.transform, leftSide: false, ref lampCount, placedLampPositions);
            }

            // left side only when there is no opposite edge
            if (!hasOpposite)
            {
                var lastLane = lanes[lanes.Count - 1];
                if (lastLane.shapePoints.Count >= 2)
                {
                    float w = GetMappedLaneWidth(lastLane.laneId);
                    var sidewalkPts = ComputeLaneEdge(lastLane, w, true, sidewalkInset, sidewalkHeight);
                    PlaceLampsAlongEdge(sidewalkPts, lampPrefab, signLampPrefab, lampsRoot.transform, leftSide: true, ref lampCount, placedLampPositions);
                }
            }
        }

        if (markGeneratedAsStatic)
            SetStaticRecursively(lampsRoot);
        SetLayerRecursively(lampsRoot, envDetailLayer);

        Debug.Log($"[Sumo2Unity] Placed {lampCount} street lamps.");
    }

    private void PlaceLampsAlongEdge(Vector3[] pts, GameObject prefab, GameObject signPrefab, Transform parent, bool leftSide, ref int count, List<Vector3> placedPositions)
    {
        if (pts.Length < 2) return;

        float totalLength = 0f;
        for (int i = 0; i < pts.Length - 1; i++)
            totalLength += Vector3.Distance(pts[i], pts[i + 1]);

        float clearance = lampJunctionClearance;
        // If the edge is too short to fit any lamp with clearance on both sides, skip it
        if (totalLength <= clearance * 2f) return;

        float nextDist = clearance;
        float cumDist = 0f;

        for (int i = 0; i < pts.Length - 1; i++)
        {
            Vector3 a = pts[i];
            Vector3 b = pts[i + 1];
            float segLen = Vector3.Distance(a, b);
            if (segLen < 0.001f) continue;

            Vector3 dir = (b - a) / segLen;

            while (nextDist <= cumDist + segLen && nextDist <= totalLength - clearance)
            {
                float t = nextDist - cumDist;
                Vector3 pos = a + dir * t;

                // each sidewalk rotates inward toward the road center
                float yaw = leftSide ? 90f : -90f;
                Quaternion rot = Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(0f, yaw, 0f);

                // keeps lamps on opposite narrow-road sidewalks from touching
                bool tooClose = false;
                float minProxSq = lampMinProximity * lampMinProximity;
                foreach (var existing in placedPositions)
                {
                    float dx = existing.x - pos.x;
                    float dz = existing.z - pos.z;
                    if (dx * dx + dz * dz < minProxSq)
                    {
                        tooClose = true;
                        break;
                    }
                }

                if (!tooClose)
                {
                    bool isMidpointLamp = signPrefab != null
                        && Mathf.Abs(nextDist - totalLength * 0.5f) < lampSpacing * 0.5f;
                    GameObject lampPrefabToUse = isMidpointLamp ? signPrefab : prefab;

                    GameObject lamp = (GameObject)PrefabUtility.InstantiatePrefab(lampPrefabToUse);
                    lamp.name = $"StreetLamp_{count++}";
                    lamp.transform.SetParent(parent);
                    lamp.transform.position = pos;
                    lamp.transform.rotation = rot;

                    if (addStreetLampColliders)
                    {
                        var col = lamp.AddComponent<BoxCollider>();
                        col.size = streetLampColliderSize;
                        col.center = streetLampColliderCenter;
                    }

                    placedPositions.Add(pos);
                }

                nextDist += lampSpacing;
            }

            cumDist += segLen;
        }
    }

    public void DeleteLaneDecalObjects()
    {
        DestroyChildByName("LaneDecals");
        // Also clean up any legacy scene-root from an older generation run
        var legacyRoot = GameObject.Find("LaneDecalsRoot");
        if (legacyRoot != null)
            DestroyImmediate(legacyRoot);
    }

    public void GenerateLaneDecals()
    {
        EnsureDefaultReferences();

        if (_netFile == null) { Debug.LogError("Net file not loaded."); return; }

        bool anyMaterial = throughDecalMaterial != null
                        || leftDecalMaterial != null
                        || throughLeftDecalMaterial != null
                        || rightDecalMaterial != null
                        || throughRightDecalMaterial != null
                        || throughRightLeftDecalMaterial != null;
        if (!anyMaterial)
        {
            Debug.LogWarning("[RoadNetworkBuilder] No lane arrow decal materials assigned. Skipping GenerateLaneDecals().");
            return;
        }

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

            RoadLaneData lane = null;
            foreach (var l in edgeData.GetLaneDataList())
            {
                if (l.laneIndex == laneIdx) { lane = l; break; }
            }
            if (lane == null || lane.shapePoints == null || lane.shapePoints.Count < 2) continue;

            Material mat = PickArrowMaterial(dirs);
            if (mat == null) continue;

            int last = lane.shapePoints.Count - 1;
            Vector3 laneEnd = ToUnity(lane.shapePoints[last][0], lane.shapePoints[last][1]);
            Vector3 prevPt = ToUnity(lane.shapePoints[last - 1][0], lane.shapePoints[last - 1][1]);
            Vector3 approachDir = (laneEnd - prevPt).normalized;

            Vector3 decalPos = laneEnd - approachDir * arrowSetbackFromJunction;
            decalPos.y += DecalHeightOffset;

            float laneW = GetLaneWidth(lane);

            if (mat != null)
            {
                GameObject decalObj = new GameObject($"ArrowDecal_{edgeId}_L{laneIdx}");
                decalObj.transform.SetParent(decalsRoot.transform);
                if (groundLayer >= 0) decalObj.layer = groundLayer;
                decalObj.transform.position = decalPos;
                // Euler(90, yaw, 0): X=90 points the projector down, Y=yaw aligns with travel direction
                float yaw = Mathf.Atan2(approachDir.x, approachDir.z) * Mathf.Rad2Deg;
                decalObj.transform.rotation = Quaternion.Euler(90f, yaw, 0f);

                var proj = decalObj.AddComponent<DecalProjector>();
                proj.material = mat;
                proj.size = new Vector3(ArrowDecalSize, ArrowDecalSize, DecalHeightOffset);
                proj.drawDistance = decalDrawDistance;
                placedCount++;
            }

            if (stopLineDecalMaterial != null)
            {
                GameObject slDecalObj = new GameObject($"StopLineDecal_{edgeId}_L{laneIdx}");
                slDecalObj.transform.SetParent(decalsRoot.transform);
                if (groundLayer >= 0) slDecalObj.layer = groundLayer;
                Vector3 slPos = laneEnd;
                slPos.y += DecalHeightOffset;
                slDecalObj.transform.position = slPos;
                // Z+90° rotates the stripe 90° within the horizontal plane so it runs across the lane
                float slYaw = Mathf.Atan2(approachDir.x, approachDir.z) * Mathf.Rad2Deg + 90f;
                slDecalObj.transform.rotation = Quaternion.Euler(90f, slYaw, 90f);

                var slProj = slDecalObj.AddComponent<DecalProjector>();
                slProj.material = stopLineDecalMaterial;
                slProj.size = new Vector3(laneW, StopLineDecalDepth, DecalHeightOffset);
                slProj.drawDistance = decalDrawDistance;
            }
        }
        Debug.Log($"[Sumo2Unity] Placed {placedCount} lane arrow decals under 'RoadNetworkRoot/LaneDecals'.");
    }

    private Material PickArrowMaterial(HashSet<ConnectionTypeDir> dirs)
    {
        bool hasLeft = dirs.Contains(ConnectionTypeDir.L) || dirs.Contains(ConnectionTypeDir.L1);
        bool hasRight = dirs.Contains(ConnectionTypeDir.R) || dirs.Contains(ConnectionTypeDir.R1);
        bool hasStraight = dirs.Contains(ConnectionTypeDir.S);

        if (hasStraight && !hasLeft && !hasRight) return throughDecalMaterial;
        if (hasLeft && !hasRight && !hasStraight) return leftDecalMaterial;
        if (hasLeft && hasStraight && !hasRight) return throughLeftDecalMaterial;
        if (hasRight && !hasLeft && !hasStraight) return rightDecalMaterial;
        if (hasRight && hasStraight && !hasLeft) return throughRightDecalMaterial;
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

        if (!string.IsNullOrEmpty(polygonType))
        {
            string polyTypeLower = polygonType.ToLowerInvariant();
            if (polyTypeLower.Contains("terrain"))
                polyGO.transform.localPosition = new Vector3(0f, PolygonTerrainYOffset, 0f);
            else if (polyTypeLower.Contains("roadside") || polyTypeLower.Contains("wood") || polyTypeLower.Contains("residential"))
                polyGO.transform.localPosition = new Vector3(0f, PolygonDefaultYOffset, 0f);
        }

        var mf = polyGO.AddComponent<MeshFilter>();
        var mr = polyGO.AddComponent<MeshRenderer>();
        mf.sharedMesh = polyMesh;
        mr.sharedMaterial = GetPolygonMaterial(polygonType);

        // PhysX warns on MeshCollider triangles with vertices over 500 units apart
        Bounds polyBounds = polyMesh.bounds;
        float polyDiag = Mathf.Sqrt(polyBounds.size.x * polyBounds.size.x + polyBounds.size.z * polyBounds.size.z);
        bool isLargePoly = polyDiag > LargePolygonDiagonalThreshold;
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
        return GetFallbackMaterial();
    }

    private bool IsKnownPolygonType(string t)
    {
        if (string.IsNullOrEmpty(t)) return false;
        var l = t.ToLowerInvariant();
        return l.Contains("wood") || l.Contains("terrain") || l.Contains("roadside") || l.Contains("residential");
    }

    private Material _cachedFallbackMaterial;
    private Material GetFallbackMaterial() =>
        _cachedFallbackMaterial ??= new Material(Shader.Find("Universal Render Pipeline/Lit"));

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

        for (float d = 0f; d <= total; d += MarkingSampleStep)
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
            for (float d = sp.s; d <= sp.e; d += MarkingStepSize)
            {
                GetPointOnPolyline(boundaryPts, cum, d, out Vector3 center, out Vector3 dir);

                float halfLen = MarkingDecalLength * 0.5f;

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
                proj.size = new Vector3(MarkingDecalWidth, MarkingDecalDepth, length * 2f);
                proj.drawDistance = decalDrawDistance;
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
