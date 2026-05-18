using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace VRDowntownDriving.Editor
{
    public static class SceneCaptureEditor
    {
        [MenuItem("Tools/Capture Scene Screenshot %&S")]
        private static void CaptureEditorScreenshot()
        {
            var path = BuildImgPath("");
            CaptureEditorScreenshot(path);
        }

        public static void CaptureEditorScreenshot(string filePath)
        {
            var sw = SceneView.lastActiveSceneView;

            if (!sw)
            {
                Debug.LogError("Unable to capture editor screenshot, no scene view found");
                return;
            }

            var cam = sw.camera;

            if (!cam)
            {
                Debug.LogError("Unable to capture editor screenshot, no camera attached to current scene view");
                return;
            }

            var renderTexture = cam.targetTexture;

            if (!renderTexture)
            {
                Debug.LogError("Unable to capture editor screenshot, camera has no render texture attached");
                return;
            }

            RenderCameraToFile(cam, renderTexture, filePath, ownedRt: false);
        }

        [MenuItem("Tools/Capture Top-Down Screenshot %&T")]
        private static void CaptureTopDownScreenshot()
        {
            var path = BuildImgPath("topdown_");
            CaptureTopDownScreenshot(path);
        }

        // Renders an orthographic top-down screenshot centered on the ego vehicle (f_0.0),
        // falling back to the scene center if the ego vehicle is not found.
        // LOD bias is temporarily maximized so full-detail geometry appears in the shot.
        // orthoSize: half-height of the orthographic view in world units
        // resolution: pixel width and height of the output image
        public static void CaptureTopDownScreenshot(string filePath, float orthoSize = 30f, int resolution = 2048)
        {
            var camGo = new GameObject("__TopDownCaptureCamera");
            var cam = camGo.AddComponent<Camera>();

            // Maximize LOD quality so the shot captures full-detail geometry.
            var savedLodBias = QualitySettings.lodBias;
            QualitySettings.lodBias = 1000f;

            try
            {
                var ego = GameObject.Find("f_0.0");
                var center = ego != null ? ego.transform.position : GetSceneCenter();

                if (ego == null)
                    Debug.LogWarning("Top-down screenshot: ego vehicle 'f_0.0' not found, centering on scene bounds instead");

                camGo.transform.position = new Vector3(center.x, center.y + 200f, center.z);
                // Match ego vehicle yaw so the car always faces "up" in the screenshot.
                var yaw = ego != null ? ego.transform.eulerAngles.y : 0f;
                camGo.transform.rotation = Quaternion.Euler(90f, yaw, 0f);

                cam.orthographic = true;
                cam.orthographicSize = orthoSize;
                cam.nearClipPlane = 1f;
                cam.farClipPlane = 400f;
                cam.clearFlags = CameraClearFlags.Skybox;
                cam.cullingMask = ~0;

                var renderTexture = new RenderTexture(resolution, resolution, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = renderTexture;

                RenderCameraToFile(cam, renderTexture, filePath, ownedRt: true);
            }
            finally
            {
                QualitySettings.lodBias = savedLodBias;
                UnityEngine.Object.DestroyImmediate(camGo);
            }
        }

        // Renders cam into rt, blits to sRGB, reads pixels, and saves as PNG.
        // If ownedRt is true the RenderTexture is released and destroyed after use.
        private static void RenderCameraToFile(Camera cam, RenderTexture rt, string filePath, bool ownedRt)
        {
            var width = rt.width;
            var height = rt.height;

            // Temporarily remove draw distance limit on all decal projectors so
            // they appear in the screenshot regardless of camera distance.
            var decals = UnityEngine.Object.FindObjectsByType<DecalProjector>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var savedDistances = new Dictionary<DecalProjector, float>(decals.Length);
            foreach (var d in decals)
            {
                savedDistances[d] = d.drawDistance;
                d.drawDistance = float.MaxValue;
            }

            // Render fresh content into cam.targetTexture (rt)
            cam.Render();

            // Restore decal draw distances
            foreach (var d in decals)
                d.drawDistance = savedDistances[d];

            // Blit into a temporary sRGB texture so ReadPixels gets
            // gamma-corrected values matching what the Scene view displays.
            var srgbTexture = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(rt, srgbTexture);

            var outputTexture = new Texture2D(width, height, TextureFormat.RGB24, false);
            RenderTexture.active = srgbTexture;
            outputTexture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            var pngData = outputTexture.EncodeToPNG();

            UnityEngine.Object.DestroyImmediate(outputTexture);
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(srgbTexture);

            if (ownedRt)
            {
                cam.targetTexture = null;
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
            }

            File.WriteAllBytes(filePath, pngData);
            AssetDatabase.Refresh();
            Debug.Log("Screenshot written to file " + filePath);
        }

        // Returns the center of the RoadNetworkRoot bounds, or falls back to all scene renderers.
        private static Vector3 GetSceneCenter()
        {
            var root = GameObject.Find("RoadNetworkRoot");
            if (root != null)
            {
                var renderers = root.GetComponentsInChildren<Renderer>();
                if (renderers.Length > 0)
                {
                    var bounds = renderers[0].bounds;
                    foreach (var r in renderers)
                        bounds.Encapsulate(r.bounds);
                    return bounds.center;
                }
            }

            var allRenderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            if (allRenderers.Length > 0)
            {
                var bounds = allRenderers[0].bounds;
                foreach (var r in allRenderers)
                    bounds.Encapsulate(r.bounds);
                return bounds.center;
            }

            return Vector3.zero;
        }

        // Builds a timestamped path inside the root img/ folder.
        private static string BuildImgPath(string prefix)
        {
            // Save to the root img/ folder two levels above Assets/
            var imgDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "img"));
            Directory.CreateDirectory(imgDir);
            return Path.Combine(imgDir, prefix + DateTime.Now.ToString("yyyy_MM_dd-HH_mm_ss") + ".png");
        }
    }
}
