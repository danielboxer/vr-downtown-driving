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

        [MenuItem("Tools/Capture Game Screenshot %&G")]
        private static void CaptureGameViewScreenshot()
        {
            var path = BuildImgPath("game_");
            CaptureGameViewScreenshot(path);
        }

        public static void CaptureGameViewScreenshot(string filePath, int width = 1920, int height = 1080)
        {
            var cam = Camera.main;
            if (cam == null)
            {
                Debug.LogError("Capture Game Screenshot: no Camera.main found. Make sure the main camera is tagged 'MainCamera'.");
                return;
            }

            var savedRt = cam.targetTexture;
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;

            try
            {
                RenderCameraToFile(cam, rt, filePath, ownedRt: false);
            }
            finally
            {
                cam.targetTexture = savedRt;
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
            }
        }

        public static void CaptureTopDownScreenshot(string filePath, float orthoSize = 30f, int resolution = 2048)
        {
            var camGo = new GameObject("__TopDownCaptureCamera");
            var cam = camGo.AddComponent<Camera>();

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

        private static void RenderCameraToFile(Camera cam, RenderTexture rt, string filePath, bool ownedRt)
        {
            var width = rt.width;
            var height = rt.height;

            // decals would otherwise be culled by draw distance in the screenshot
            var decals = UnityEngine.Object.FindObjectsByType<DecalProjector>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var savedDistances = new Dictionary<DecalProjector, float>(decals.Length);
            foreach (var d in decals)
            {
                savedDistances[d] = d.drawDistance;
                d.drawDistance = float.MaxValue;
            }

            cam.Render();

            foreach (var d in decals)
                d.drawDistance = savedDistances[d];

            // sRGB blit so ReadPixels gets gamma-corrected values matching the Scene view
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

        private static string BuildImgPath(string prefix)
        {
            // Save to the root img/ folder two levels above Assets/
            var imgDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "img"));
            Directory.CreateDirectory(imgDir);
            return Path.Combine(imgDir, prefix + DateTime.Now.ToString("yyyy_MM_dd-HH_mm_ss") + ".png");
        }
    }
}
