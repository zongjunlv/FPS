using System;
using System.IO;
using System.Linq;
using FPS.Networking.Session;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Off-screen inspection of the exact pure models used by coop clients.</summary>
public static class CoopEnemyPreviewCapture
{
    public static string Capture(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        Scene preview = EditorSceneManager.NewPreviewScene();
        RenderTexture previous = RenderTexture.active;
        RenderTexture texture = null;
        Texture2D pixels = null;
        try
        {
            var cameraObject = new GameObject("Offscreen inspection camera");
            SceneManager.MoveGameObjectToScene(cameraObject, preview);
            var camera = cameraObject.AddComponent<Camera>();
            camera.scene = preview;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.12f, 0.15f, 0.18f);
            camera.fieldOfView = 38f;
            texture = new RenderTexture(720, 540, 24);
            camera.targetTexture = texture;
            var lightObject = new GameObject("Inspection light");
            SceneManager.MoveGameObjectToScene(lightObject, preview);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.7f;
            lightObject.transform.rotation = Quaternion.Euler(35, -30, 0);
            pixels = new Texture2D(720, 540, TextureFormat.RGB24, false);
            int count = 0;
            foreach (GameObject prefab in Resources.LoadAll<GameObject>("CoopPresentation/EnemyModels").OrderBy(value => value.name))
            {
                var model = UnityEngine.Object.Instantiate(prefab);
                SceneManager.MoveGameObjectToScene(model, preview);
                try
                {
                    model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
                    Bounds bounds = renderers[0].bounds;
                    foreach (Renderer renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
                    float size = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
                    cameraObject.transform.position = bounds.center + new Vector3(1.1f, 0.6f, 1.8f).normalized * size * 2.25f;
                    cameraObject.transform.LookAt(bounds.center);
                    foreach (string state in new[] { "Idle", "Run", "Attack" })
                    {
                        foreach (Animator animator in model.GetComponentsInChildren<Animator>(true))
                        {
                            animator.Rebind();
                            animator.Play(state, 0, 0.3f);
                            animator.Update(0f);
                        }
                        camera.Render();
                        RenderTexture.active = texture;
                        pixels.ReadPixels(new Rect(0, 0, 720, 540), 0, 0);
                        pixels.Apply();
                        File.WriteAllBytes(Path.Combine(outputDirectory, prefab.name + "-" + state + ".png"), pixels.EncodeToPNG());
                        count++;
                    }
                }
                finally { UnityEngine.Object.DestroyImmediate(model); }
            }
            return $"Captured {count} model/state images off-screen: {outputDirectory}";
        }
        finally
        {
            RenderTexture.active = previous;
            if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
            if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels);
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }
}
