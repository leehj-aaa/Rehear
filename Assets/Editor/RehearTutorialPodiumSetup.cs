using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;

[InitializeOnLoad]
internal static class RehearTutorialPodiumSetup
{
    const string Request = "Temp/RehearTutorialPodium.request";
    static double next;
    static RehearTutorialPodiumSetup() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < next) return;
        next = EditorApplication.timeSinceStartup + 1;
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning) return;
        string command = File.ReadAllText(Request).Trim();
        File.Delete(Request);
        try { if (command == "apply") Apply(); else if (command == "align") AlignDisplay(); else if (command == "capture") Capture(); else Inspect(); }
        catch (Exception e) { File.WriteAllText("Temp/RehearTutorialPodium.txt", "FAILED\n" + e); }
    }
    static string Path(Transform t) => t.parent ? Path(t.parent) + "/" + t.name : t.name;
    static void AlignDisplay()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/01_Scene/Scene_00_5_Tutorial.unity") throw new InvalidOperationException("Open tutorial scene.");
        var screen = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<RectTransform>(true))
            .Single(t => t.name == "DeskScreen" && t.parent && t.parent.name == "Counter");
        var position = screen.localPosition; var scale = screen.localScale;
        float tilt = screen.localEulerAngles.x;
        Undo.RecordObject(screen, "Align presenter display with podium");
        screen.localRotation = Quaternion.Euler(tilt, 0, 0);
        if (Vector3.Angle(screen.right, screen.parent.right) > .01f || screen.localPosition != position || screen.localScale != scale)
            throw new InvalidOperationException("Display alignment verification failed.");
        EditorUtility.SetDirty(screen);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Scene save failed.");
        Selection.activeGameObject = screen.gameObject;
        SceneView.RepaintAll();
        File.WriteAllText("Temp/RehearTutorialDisplayAlignment.txt",
            $"widthAlignedToPodium=true\nlocalTilt={tilt:F2}\nlocalYaw=0\nlocalRoll=0\npositionUnchanged=true\nscaleUnchanged=true\nsaved=true\n");
    }
    static void Capture()
    {
        var camera = Camera.main;
        var position = camera.transform.position; var rotation = camera.transform.rotation;
        var projection = camera.projectionMatrix;
        int mask = camera.cullingMask;
        var target = new RenderTexture(1600, 1000, 24, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        var png = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
        try
        {
            // Look down slightly from the real presenter position to inspect the display.
            camera.transform.rotation = Quaternion.LookRotation(new Vector3(32.4f, .95f, -15.45f) - position);
            camera.ResetProjectionMatrix();
            camera.cullingMask |= 1 << LayerMask.NameToLayer("UI");
            Canvas.ForceUpdateCanvases();
            var request = new RenderPipeline.StandardRequest { destination = target };
            if (!RenderPipeline.SupportsRenderRequest(camera, request)) throw new InvalidOperationException("Render request unsupported.");
            RenderPipeline.SubmitRenderRequest(camera, request);
            RenderPipeline.SubmitRenderRequest(camera, request);
            RenderTexture.active = target;
            png.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0); png.Apply();
            File.WriteAllBytes("Temp/RehearPodiumPresenterView.png", png.EncodeToPNG());
        }
        finally
        {
            camera.transform.SetPositionAndRotation(position, rotation);
            camera.projectionMatrix = projection;
            camera.cullingMask = mask;
            RenderTexture.active = previous;
            UnityEngine.Object.DestroyImmediate(png);
            UnityEngine.Object.DestroyImmediate(target);
        }
    }
    static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/01_Scene/Scene_00_5_Tutorial.unity") throw new InvalidOperationException("Open tutorial scene.");
        var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
        var counter = all.Single(t => t.name == "Counter" && t.parent && t.parent.name == "_Props");
        var screen = (RectTransform)all.Single(t => t.name == "DeskScreen");
        var originalCanvas = screen.GetComponentInParent<Canvas>();
        var rawImage = screen.GetComponent<RawImage>();
        var texture = rawImage.texture;
        var renderer = counter.GetComponent<MeshRenderer>();
        var mesh = counter.GetComponent<MeshFilter>().sharedMesh;
        var camera = Camera.main;
        if (!originalCanvas || !mesh || !camera) throw new InvalidOperationException("Missing screen canvas, podium mesh or camera.");
        Undo.IncrementCurrentGroup();
        int undo = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Mount podium screen and clear audience sightline");
        try
        {
            Vector3 oldScale = screen.lossyScale;
            Vector3 oldCenter = screen.position;
            // The user explicitly requested unchanged podium height and size.
            // Only the screen's mounting angle and position are adjusted here.
            Vector3 counterScale = counter.localScale;
            float counterHeight = counter.position.y;
            Vector3 rayOrigin = new Vector3(oldCenter.x, renderer.bounds.max.y + 1, oldCenter.z);
            Vector3 top = Vector3.zero, normal = Vector3.up;
            float best = float.PositiveInfinity;
            var vertices = mesh.vertices; var triangles = mesh.triangles;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 a = counter.TransformPoint(vertices[triangles[i]]);
                Vector3 b = counter.TransformPoint(vertices[triangles[i + 1]]);
                Vector3 c = counter.TransformPoint(vertices[triangles[i + 2]]);
                Vector3 n = Vector3.Cross(b - a, c - a).normalized;
                if (n.y < 0) n = -n;
                if (n.y < 0.5f) continue;
                var plane = new Plane(n, a);
                var ray = new Ray(rayOrigin, Vector3.down);
                if (!plane.Raycast(ray, out float distance) || distance >= best) continue;
                Vector3 p = ray.GetPoint(distance);
                Vector3 v0 = b - a, v1 = c - a, v2 = p - a;
                float d00 = Vector3.Dot(v0, v0), d01 = Vector3.Dot(v0, v1), d11 = Vector3.Dot(v1, v1);
                float denom = d00 * d11 - d01 * d01;
                if (Mathf.Abs(denom) < 1e-8f) continue;
                float u = (d11 * Vector3.Dot(v2, v0) - d01 * Vector3.Dot(v2, v1)) / denom;
                float v = (d00 * Vector3.Dot(v2, v1) - d01 * Vector3.Dot(v2, v0)) / denom;
                if (u < -0.001f || v < -0.001f || u + v > 1.001f) continue;
                best = distance; top = p; normal = n;
            }
            if (float.IsInfinity(best)) throw new InvalidOperationException("No tabletop surface found under DeskScreen; changes rolled back.");
            var canvas = screen.GetComponent<Canvas>();
            if (!canvas)
            {
                canvas = Undo.AddComponent<Canvas>(screen.gameObject);
                EditorUtility.CopySerialized(originalCanvas, canvas);
                var originalRaycaster = originalCanvas.GetComponents<Component>().FirstOrDefault(c => c.GetType().Name == "TrackedDeviceGraphicRaycaster");
                if (originalRaycaster)
                {
                    var raycaster = Undo.AddComponent(screen.gameObject, originalRaycaster.GetType());
                    EditorUtility.CopySerialized(originalRaycaster, raycaster);
                }
            }
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = originalCanvas.worldCamera;
            Undo.SetTransformParent(screen, counter, "Parent screen to podium");
            Undo.RecordObject(screen, "Align screen to tabletop");
            screen.localScale = new Vector3(oldScale.x / counter.lossyScale.x, oldScale.y / counter.lossyScale.y, oldScale.z / counter.lossyScale.z);
            // Match the podium's orientation, not the viewer's current position.
            Vector3 away = Vector3.ProjectOnPlane(counter.forward, normal).normalized;
            // Laptop-replacement display: upright with a modest backward tilt.
            // Its bottom edge rests just above the tabletop, never flat on it.
            const float tilt = 15f;
            float halfHeight = screen.rect.height * oldScale.y * .5f;
            screen.SetPositionAndRotation(top + Vector3.up * (halfHeight * Mathf.Cos(tilt * Mathf.Deg2Rad) + .02f),
                Quaternion.LookRotation(away, Vector3.up) * Quaternion.Euler(tilt, 0, 0));
            EditorUtility.SetDirty(canvas);
            var corners = new Vector3[4]; screen.GetWorldCorners(corners);
            if (counter.localScale != counterScale || counter.position.y != counterHeight)
                throw new InvalidOperationException("Podium height or size changed unexpectedly.");
            var report = new StringBuilder($"parent={Path(screen)}\npodiumHeightUnchanged=true\npodiumScaleUnchanged=true\npodiumHeight={renderer.bounds.size.y:F3}\nscreenTop={corners.Max(c => c.y):F3}\ntexturePreserved={rawImage.texture == texture}\n");
            foreach (var audience in all.Where(t => t.parent && t.parent.name == "Audience" && t.name.StartsWith("Aud_")))
            {
                var eyes = audience.GetComponentsInChildren<Renderer>(true).Where(r => r.name.Contains("EYE_")).ToArray();
                if (eyes.Length == 0) continue;
                Vector3 eye = eyes.Aggregate(Vector3.zero, (sum, r) => sum + r.bounds.center) / eyes.Length;
                var ray = new Ray(camera.transform.position, eye - camera.transform.position);
                bool blocked = renderer.bounds.IntersectRay(ray, out float hit) && hit < Vector3.Distance(camera.transform.position, eye);
                if (blocked) throw new InvalidOperationException($"Podium still blocks {audience.name}; changes rolled back.");
                var screenPlane = new Plane(screen.forward, screen.position);
                if (screenPlane.Raycast(ray, out float screenHit) && screenHit < Vector3.Distance(camera.transform.position, eye))
                {
                    Vector3 point = screen.InverseTransformPoint(ray.GetPoint(screenHit));
                    if (screen.rect.Contains(new Vector2(point.x, point.y)))
                        throw new InvalidOperationException($"Screen blocks {audience.name}; changes rolled back.");
                }
                report.AppendLine($"eyeSightline={audience.name}:clear");
            }
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Scene save failed.");
            Undo.CollapseUndoOperations(undo);
            Selection.activeGameObject = counter.gameObject;
            SceneView.RepaintAll();
            File.WriteAllText("Temp/RehearTutorialPodium-apply.txt", report + "saved=true\nlightingRebaked=false\n");
        }
        catch { Undo.RevertAllDownToGroup(undo); throw; }
    }
    static void Inspect()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var transforms = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
        var report = new StringBuilder(scene.path + "\n");
        foreach (var camera in transforms.Select(t => t.GetComponent<Camera>()).Where(c => c))
        {
            var data = camera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            report.AppendLine($"CAMERA {camera.name}: enabled={camera.enabled}, mask={camera.cullingMask}, target={camera.targetTexture}, renderer={data?.scriptableRenderer?.GetType().Name}");
        }
        foreach (var source in transforms.Select(t => t.GetComponent<LeTai.Asset.TranslucentImage.TranslucentImageSource>()).Where(s => s))
            report.AppendLine($"BLUR source={source.name}, enabled={source.isActiveAndEnabled}, rate={source.MaxUpdateRate}, texture={source.BlurredScreen}, created={(source.BlurredScreen && source.BlurredScreen.IsCreated())}, config={EditorJsonUtility.ToJson(source.BlurConfig)}");
        foreach (var panel in transforms.Select(t => t.GetComponent<LeTai.Asset.TranslucentImage.TranslucentImage>()).Where(p => p))
            report.AppendLine($"PANEL {Path(panel.transform)}: active={panel.IsActive()}, source={panel.source?.name}, canvasEnabled={panel.canvas?.enabled}, tint={panel.foregroundOpacity}, shader={panel.material?.shader?.name}");
        var audio = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/08_Audio/Tutorial_ButtonPrompt.mp3");
        if (audio && audio.LoadAudioData())
        {
            var samples = new float[audio.samples * audio.channels];
            if (audio.GetData(samples, 0))
            {
                int stride = Mathf.Max(1, audio.frequency / 50) * audio.channels;
                for (int offset = 0; offset < samples.Length; offset += stride)
                {
                    int end = Math.Min(samples.Length, offset + stride); double sum = 0;
                    for (int i = offset; i < end; i++) sum += samples[i] * samples[i];
                    report.AppendLine($"AUDIO time={offset / (float)(audio.frequency * audio.channels):F3} rms={Math.Sqrt(sum / (end - offset)):F5}");
                }
            }
        }
        foreach (var t in transforms.Where(t => t.GetComponent<Canvas>() || t.GetComponent<Camera>() ||
            t.name.IndexOf("screen", StringComparison.OrdinalIgnoreCase) >= 0 || t.name == "Counter" ||
            t.name.StartsWith("Aud_") || t.name.IndexOf("chair", StringComparison.OrdinalIgnoreCase) >= 0 ||
            t.name.IndexOf("table", StringComparison.OrdinalIgnoreCase) >= 0 || t.name == "Slide"))
        {
            report.AppendLine($"{Path(t)} | active={t.gameObject.activeInHierarchy} world={t.position.ToString("F4")} local={t.localPosition.ToString("F4")} euler={t.eulerAngles} scale={t.lossyScale}");
            if (t.name.StartsWith("Aud_"))
                foreach (var bone in t.GetComponentsInChildren<Transform>(true).Where(b => b.name == "pelvis"))
                    report.AppendLine($" HIP {t.name} world={bone.position.ToString("F4")}");
            if (t.name == "Canvas" || t.name == "DeskScreen")
            {
                foreach (var child in t.GetComponentsInChildren<Transform>(true).Where(c => c == t || t.name == "DeskScreen"))
                    report.AppendLine($" COMPONENTS {Path(child)}: {string.Join(",", child.GetComponents<Component>().Select(c => c ? c.GetType().Name : "Missing"))}");
                if (t is RectTransform rect)
                {
                    var corners = new Vector3[4]; rect.GetWorldCorners(corners);
                    report.AppendLine($" RECT {t.name} size={rect.rect.size} corners={string.Join(";", corners.Select(c => c.ToString("F4")))}");
                }
            }
            foreach (var renderer in t.GetComponentsInChildren<Renderer>(true))
                report.AppendLine($" RENDERER {renderer.name} center={renderer.bounds.center.ToString("F4")} size={renderer.bounds.size.ToString("F4")}");
            if ((t.name == "Slide" || t.name == "Counter") && t.TryGetComponent<MeshFilter>(out var filter))
            {
                var mesh = filter.sharedMesh;
                var materials = t.GetComponent<MeshRenderer>().sharedMaterials;
                for (int i = 0; i < mesh.subMeshCount; i++)
                {
                    var vertices = mesh.vertices; var indices = mesh.GetIndices(i);
                    var bounds = new Bounds(t.TransformPoint(vertices[indices[0]]), Vector3.zero);
                    foreach (int index in indices) bounds.Encapsulate(t.TransformPoint(vertices[index]));
                    report.AppendLine($" SUBMESH {i} material={materials[i].name} center={bounds.center.ToString("F5")} size={bounds.size.ToString("F5")}");
                }
            }
        }
        File.WriteAllText("Temp/RehearTutorialPodium.txt", report.ToString());
    }
}
