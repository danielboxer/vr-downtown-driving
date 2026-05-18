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
            // Save to the root img/ folder two levels above Assets/
            var imgDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "img"));
            Directory.CreateDirectory(imgDir);
            var path = Path.Combine(imgDir, DateTime.Now.ToString("yyyy_MM_dd-HH_mm_ss") + ".png");
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

            var width = renderTexture.width;
            var height = renderTexture.height;

            // Temporarily remove draw distance limit on all decal projectors so
            // they appear in the screenshot regardless of camera distance.
            var decals = UnityEngine.Object.FindObjectsByType<DecalProjector>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var savedDistances = new Dictionary<DecalProjector, float>(decals.Length);
            foreach (var d in decals)
            {
                savedDistances[d] = d.drawDistance;
                d.drawDistance = float.MaxValue;
            }

            // Render fresh content into cam.targetTexture (renderTexture)
            cam.Render();

            // Restore decal draw distances
            foreach (var d in decals)
                d.drawDistance = savedDistances[d];

            // Blit into a temporary sRGB texture so ReadPixels gets
            // gamma-corrected values matching what the Scene view displays.
            var srgbTexture = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(renderTexture, srgbTexture);

            var outputTexture = new Texture2D(width, height, TextureFormat.RGB24, false);

            RenderTexture.active = srgbTexture;

            outputTexture.ReadPixels(new Rect(0, 0, width, height), 0, 0);

            var pngData = outputTexture.EncodeToPNG();

            UnityEngine.Object.DestroyImmediate(outputTexture);

            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(srgbTexture);

            File.WriteAllBytes(filePath, pngData);

            AssetDatabase.Refresh();

            Debug.Log("Screenshot written to file " + filePath);
        }
    }
}
