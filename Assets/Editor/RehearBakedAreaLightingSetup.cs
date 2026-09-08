using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

internal static class RehearBakedAreaLightingSetup
{
    private const string ScenePath = "Assets/01_Scene/Scene_00_5_Tutorial.unity";
    private const string RootName = "Baked Area Lighting";

    [MenuItem("Rehear/Set Up Tutorial Baked Area Lighting")]
    private static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath || EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning) return;
        // Never overwrite hand-tuned intensities or create duplicate lights.
        if (scene.GetRootGameObjects().Any(g => g.name == RootName)) return;
        var renderers = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
            .Where(r => r.gameObject.scene == scene && r.gameObject.activeInHierarchy).ToArray();
        var windows = renderers.Where(r => r.name == "Window_Glass").OrderBy(r => r.bounds.center.x).ToArray();
        var fixtures = renderers.Where(r => r.name.StartsWith("Ceiling_Lamp")).OrderBy(r => r.bounds.center.x).ToArray();
        var ceilings = renderers.Where(r => r.name.StartsWith("Ceiling_3x3m")).OrderBy(r => r.bounds.center.x).ThenBy(r => r.bounds.center.z).ToArray();
        if (windows.Length != 2 || fixtures.Length != 4 || ceilings.Length != 6)
        {
            Debug.LogError($"Baked light setup stopped: room geometry changed ({windows.Length} windows, {fixtures.Length} fixtures, {ceilings.Length} ceiling panels).");
            return;
        }

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Set up baked room lighting");
        var undoGroup = Undo.GetCurrentGroup();
        var root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Add baked light rig");
        var windowGroup = Group(root.transform, "Window Daylight - 6500K");
        var fixtureGroup = Group(root.transform, "Pendant Downlights - 4300K");
        var fillGroup = Group(root.transform, "Ceiling Soft Fill - 4800K");

        for (var i = 0; i < windows.Length; i++)
        {
            var b = windows[i].bounds;
            // The observed windows face +Z into this room. Offset beyond the
            // blinds so the emitter isn't trapped inside glass or opaque slats.
            Area(windowGroup, $"Window Area {i + 1:00}", b.center + Vector3.forward * 0.47f,
                Quaternion.identity, new Vector2(b.size.x * 0.92f, b.size.y * 0.9f), 2f, 6500f);
        }
        for (var i = 0; i < fixtures.Length; i++)
        {
            var b = fixtures[i].bounds;
            Area(fixtureGroup, $"Pendant Area {i + 1:00}", new Vector3(b.center.x, b.min.y - 0.035f, b.center.z),
                Quaternion.Euler(90f, 0f, 0f), new Vector2(Mathf.Max(b.size.x, 0.12f), b.size.z * 0.92f), 2f, 4300f);
        }
        for (var i = 0; i < ceilings.Length; i++)
        {
            // Ceiling meshes extend past the meeting-room walls. Use an inset
            // 3x2 grid over the window/pendant span, not the outer tile centers.
            var x = Mathf.Lerp(windows[0].bounds.center.x - 0.6f,
                windows[1].bounds.center.x + 0.6f, (i / 2) / 2f);
            var z = fixtures.Average(f => f.bounds.center.z) + (i % 2 == 0 ? -0.85f : 1.15f);
            var y = ceilings.Min(c => c.bounds.min.y) - 0.26f;
            // Below both the ceiling and its timber beams; low intensity broad
            // fill complements the visible fixtures instead of overpowering them.
            Area(fillGroup, $"Ceiling Fill {i + 1:00}", new Vector3(x, y, z),
                Quaternion.Euler(90f, 0f, 0f), new Vector2(1.4f, 1.3f), 0.3f, 4800f);
        }

        var lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(l => l.gameObject.scene == scene).ToArray();
        foreach (var light in lights)
        {
            Undo.RecordObject(light, "Use baked lighting");
            light.lightmapBakeType = LightmapBakeType.Baked;
            EditorUtility.SetDirty(light);
            PrefabUtility.RecordPrefabInstancePropertyModifications(light);
        }

        // Dynamic people/controllers need probe lighting when all direct lights
        // are baked. Keep existing probe groups and animated/static flags intact.
        var probeObject = Group(root.transform, "Audience Light Probes").gameObject;
        var probes = probeObject.AddComponent<LightProbeGroup>();
        var points = new List<Vector3>();
        var xmin = windows.Min(w => w.bounds.center.x) - 1f;
        var xmax = windows.Max(w => w.bounds.center.x) + 1f;
        var zmin = windows.Average(w => w.bounds.center.z) + 0.8f;
        var zmax = Mathf.Min(zmin + 3.2f, -14.45f);
        for (var x = 0; x < 6; x++)
            for (var z = 0; z < 5; z++)
                foreach (var y in new[] { 0.4f, 1.15f, 1.9f, 2.65f })
                    points.Add(new Vector3(Mathf.Lerp(xmin, xmax, x / 5f), y, Mathf.Lerp(zmin, zmax, z / 4f)));
        probes.probePositions = points.ToArray();
        var dynamicRenderers = Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(r => r.gameObject.scene == scene).ToArray();
        foreach (var renderer in dynamicRenderers)
        {
            if (renderer.lightProbeUsage != LightProbeUsage.Off) continue;
            Undo.RecordObject(renderer, "Receive baked probe lighting");
            renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
        }
        // Lighting Settings may be actively tuned by the user: do not replace
        // their asset or start a potentially long bake as a side effect of setup.
        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new System.InvalidOperationException("Could not save baked lights.");
        var report = new StringBuilder($"scene={scene.path}\nlights={lights.Length}\nareas=12\nprobes={points.Count}\nbakeStarted=false\n");
        foreach (var l in lights.OrderBy(l => l.name))
            report.AppendLine($"{l.name}: {l.type}, {l.lightmapBakeType}, pos={l.transform.position:F3}, forward={l.transform.forward:F2}, area={l.areaSize}, intensity={l.intensity}, kelvin={l.colorTemperature}");
        System.IO.File.WriteAllText("Temp/RehearBakedLighting-validation.txt", report.ToString());
        Selection.activeGameObject = root;
        Debug.Log("Rehear: 12 baked Area Lights and 120 audience probes saved. All scene lights are Baked. Generate Lighting is still required.", root);
    }

    private static Transform Group(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    private static void Area(Transform parent, string name, Vector3 position, Quaternion rotation,
        Vector2 size, float intensity, float temperature)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(position, rotation);
        var light = go.AddComponent<Light>();
        light.type = LightType.Rectangle;
        light.lightmapBakeType = LightmapBakeType.Baked;
        light.areaSize = size;
        light.intensity = intensity;
        light.range = 12f;
        light.color = Color.white;
        light.useColorTemperature = true;
        light.colorTemperature = temperature;
        light.bounceIntensity = 1f;
        light.shadows = LightShadows.Soft;
    }
}
