using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

[InitializeOnLoad]
internal static class RehearPodiumFinishSetup
{
    const string Folder = "Assets/Settings/TutorialUI";
    static RehearPodiumFinishSetup() { EditorApplication.update += Poll; }
    static void Poll()
    {
        const string request = "Temp/RehearPodiumFinish.request";
        if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning) return;
        File.Delete(request);
        try { Apply(); }
        catch (Exception e) { File.WriteAllText("Temp/RehearPodiumFinish.txt", "FAIL\n" + e); Debug.LogException(e); }
    }

    [MenuItem("Rehear/Tutorial/Add Podium Script Toggle And Display Housing")]
    static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/01_Scene/Scene_00_5_Tutorial.unity" || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new Exception("Open tutorial in Edit mode.");
        var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
        var screen = (RectTransform)all.Single(t => t.name == "DeskScreen");
        var panel = (RectTransform)all.Single(t => t.name == "Panel_Script_New");
        var counter = screen.parent;
        var controller = all.Select(t => t.GetComponent<PresentationController>()).Single(c => c);
        if (counter.name != "Counter" || panel.parent != counter) throw new Exception("Unexpected podium hierarchy.");
        Vector3 position = counter.position, scale = counter.localScale, screenPosition = screen.position;
        Quaternion rotation = counter.rotation, screenRotation = screen.rotation;
        var texture = screen.GetComponent<RawImage>().texture;
        Undo.IncrementCurrentGroup();
        int undo = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Finish podium display and script toggle");
        try
        {
            var buttonRoot = counter.Find("ScriptToggle");
            if (!buttonRoot)
            {
                var go = new GameObject("ScriptToggle", typeof(RectTransform), typeof(Canvas), typeof(Image), typeof(Button), typeof(PodiumScriptToggle));
                Undo.RegisterCreatedObjectUndo(go, "Create script toggle");
                buttonRoot = go.transform;
                buttonRoot.SetParent(counter, false);
            }
            Undo.RegisterFullObjectHierarchyUndo(buttonRoot.gameObject, "Configure script toggle");
            buttonRoot.gameObject.layer = LayerMask.NameToLayer("UI");
            var rect = (RectTransform)buttonRoot;
            rect.sizeDelta = new Vector2(150, 56);
            rect.pivot = new Vector2(.5f, .5f);
            rect.localScale = new Vector3(.00075f / counter.lossyScale.x, .00075f / counter.lossyScale.y, .00075f / counter.lossyScale.z);
            var panelCorners = new Vector3[4]; panel.GetWorldCorners(panelCorners);
            Vector3 panelRight = (panelCorners[3] - panelCorners[0]).normalized;
            Vector3 panelUp = (panelCorners[1] - panelCorners[0]).normalized;
            // Keep the control on the clear right-hand ledge, outside the reading panel.
            Vector3 mountTarget = panelCorners[3] + panelRight * .07f + panelUp * .025f;
            FindMount(counter, mountTarget, out Vector3 mountPoint, out Vector3 mountNormal);
            // The button face follows the existing prompter slope, not the flat ledge.
            rect.SetPositionAndRotation(mountTarget - panel.forward * .003f, panel.rotation);
            var canvas = rect.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = screen.GetComponent<Canvas>().worldCamera;
            var sourceRaycaster = screen.GetComponents<Component>().Single(c => c.GetType().Name == "TrackedDeviceGraphicRaycaster");
            var raycaster = rect.GetComponent(sourceRaycaster.GetType()) ?? Undo.AddComponent(rect.gameObject, sourceRaycaster.GetType());
            EditorUtility.CopySerialized(sourceRaycaster, raycaster);
            var image = rect.GetComponent<Image>();
            image.material = MaterialAsset("Script Toggle Rounded", "Rehear/UI/Rounded Translucent Panel");
            image.material.SetVector("_PanelSize", new Vector4(150, 56, 0, 0));
            image.material.SetFloat("_Radius", 28);
            image.material.SetFloat("_BorderWidth", 0);
            image.material.SetFloat("_UseBlur", 0);
            image.raycastTarget = true;
            Save(image.material);
            var button = rect.GetComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            var colors = ColorBlock.defaultColorBlock;
            colors.highlightedColor = new Color(.82f, .9f, 1);
            colors.pressedColor = new Color(.58f, .68f, .88f);
            colors.selectedColor = Color.white;
            colors.fadeDuration = .1f;
            button.colors = colors;
            var labelTransform = rect.Find("Label");
            if (!labelTransform)
            {
                var go = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
                Undo.RegisterCreatedObjectUndo(go, "Create toggle label");
                labelTransform = go.transform; labelTransform.SetParent(rect, false);
            }
            labelTransform.gameObject.layer = rect.gameObject.layer;
            var label = labelTransform.GetComponent<TMP_Text>();
            var labelRect = label.rectTransform;
            labelRect.anchorMin = Vector2.zero; labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
            label.font = RehearPretendardFontBake.Font("Medium");
            label.fontSharedMaterial = label.font.material;
            label.fontSize = 22; label.enableAutoSizing = false;
            label.fontStyle = FontStyles.Normal; label.color = Color.white;
            label.alignment = TextAlignmentOptions.Center; label.raycastTarget = false;
            var toggle = rect.GetComponent<PodiumScriptToggle>();
            toggle.controller = controller; toggle.scriptPanel = panel.gameObject;
            toggle.label = label; toggle.background = image;
            if (!Enumerable.Range(0, button.onClick.GetPersistentEventCount()).Any(i => button.onClick.GetPersistentTarget(i) == toggle))
                UnityEventTools.AddPersistentListener(button.onClick, toggle.ToggleScript);
            toggle.Refresh();

            var housing = screen.Find("DisplayHousing");
            if (!housing)
            {
                var go = new GameObject("DisplayHousing", typeof(MeshFilter), typeof(MeshRenderer));
                Undo.RegisterCreatedObjectUndo(go, "Create display housing");
                housing = go.transform; housing.SetParent(screen, false);
            }
            Undo.RegisterFullObjectHierarchyUndo(housing.gameObject, "Fit display housing");
            housing.localPosition = new Vector3(screen.rect.center.x, screen.rect.center.y, 0);
            housing.localRotation = Quaternion.identity; housing.localScale = Vector3.one;
            housing.gameObject.layer = 0;
            var meshPath = Folder + "/Podium Display Housing.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (!mesh) { mesh = new Mesh { name = "Podium Display Housing" }; AssetDatabase.CreateAsset(mesh, meshPath); }
            BuildHousing(mesh, screen.rect.width, screen.rect.height);
            Save(mesh);
            housing.GetComponent<MeshFilter>().sharedMesh = mesh;
            bool newHousingMaterial = !AssetDatabase.LoadAssetAtPath<Material>(Folder + "/Podium Display Charcoal.mat");
            var material = MaterialAsset("Podium Display Charcoal", "Universal Render Pipeline/Lit");
            if (newHousingMaterial)
            {
                material.SetColor("_BaseColor", new Color(.035f, .04f, .05f, 1));
                material.SetFloat("_Metallic", .15f); material.SetFloat("_Smoothness", .28f);
            }
            Save(material);
            var renderer = housing.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material; renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            RehearGeneratedMeshLighting.Configure(renderer);

            var mount = rect.Find("ToggleMount");
            if (mount) Undo.DestroyObjectImmediate(mount.gameObject);
            IntegrateSlopedControlFace(counter, rect, mountPoint.y);
            if (Quaternion.Angle(rect.rotation, panel.rotation) > .01f)
                throw new Exception("Button face must match prompter angle.");

            // Hiding and restoring must preserve page and leave this button reachable.
            var scroller = all.Where(t => t).Select(t => t.GetComponent<ScriptScroller>()).Single(s => s);
            int page = scroller.CurrentPage; bool visible = panel.gameObject.activeSelf;
            Undo.RecordObject(panel.gameObject, "Verify script toggle");
            toggle.ToggleScript();
            if (panel.gameObject.activeSelf == visible || !button.gameObject.activeInHierarchy) throw new Exception("Toggle failed or hid itself.");
            toggle.ToggleScript();
            if (panel.gameObject.activeSelf != visible || scroller.CurrentPage != page) throw new Exception("Toggle changed current page.");
            if (counter.position != position || counter.rotation != rotation || counter.localScale != scale ||
                screen.position != screenPosition || screen.rotation != screenRotation || screen.GetComponent<RawImage>().texture != texture)
                throw new Exception("Existing podium/display changed.");
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new Exception("Save failed.");
            Selection.activeGameObject = rect.gameObject;
            if (SceneView.lastActiveSceneView)
                SceneView.lastActiveSceneView.LookAt((panel.position + screen.position) * .5f, screen.rotation * Quaternion.Euler(18, -12, 0), .85f);
            Undo.CollapseUndoOperations(undo);
            File.WriteAllText("Temp/RehearPodiumFinish.txt", $"PASS\ntoggle=off/on, current page preserved\nbuttonIndependent=true\nXRRaycaster=true\nbuttonWorldSize=112.5x42mm\ncontrolFace=integrated Counter mesh, matches prompter angle\nhousingVertices={mesh.vertexCount}\nhousingTriangles={mesh.triangles.Length / 3}\npodiumAndScreenPose=unchanged\nslideTexture=retained\nscriptSize={panel.rect.size}, scale={panel.lossyScale}, position={panel.position}, localPosition={panel.localPosition}\nworldRight={panelRight}, transformRight={panel.right}\nbuttonWorld={rect.position}, local={rect.localPosition}\ncorners={string.Join(";", panelCorners.Select(c => c.ToString()))}\n");
        }
        catch { Undo.RevertAllDownToGroup(undo); throw; }
    }

    static Material MaterialAsset(string name, string shader)
    {
        var path = Folder + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material) { material = new Material(Shader.Find(shader)) { name = name }; AssetDatabase.CreateAsset(material, path); }
        return material;
    }
    static void Save(UnityEngine.Object value) { EditorUtility.SetDirty(value); AssetDatabase.SaveAssetIfDirty(value); }

    static void FindMount(Transform counter, Vector3 target, out Vector3 point, out Vector3 normal)
    {
        var mesh = SourceCounterMesh();
        var vertices = mesh.vertices; var triangles = mesh.triangles;
        var ray = new Ray(target + Vector3.up * .5f, Vector3.down);
        float best = float.PositiveInfinity; point = Vector3.zero; normal = Vector3.up;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 a = counter.TransformPoint(vertices[triangles[i]]), b = counter.TransformPoint(vertices[triangles[i + 1]]), c = counter.TransformPoint(vertices[triangles[i + 2]]);
            Vector3 edge1 = b - a, edge2 = c - a, p = Vector3.Cross(ray.direction, edge2);
            float determinant = Vector3.Dot(edge1, p);
            if (Mathf.Abs(determinant) < 1e-8f) continue;
            float inverse = 1 / determinant;
            Vector3 t = ray.origin - a;
            float u = Vector3.Dot(t, p) * inverse;
            if (u < 0 || u > 1) continue;
            Vector3 q = Vector3.Cross(t, edge1);
            float v = Vector3.Dot(ray.direction, q) * inverse;
            if (v < 0 || u + v > 1) continue;
            float distance = Vector3.Dot(edge2, q) * inverse;
            Vector3 n = Vector3.Cross(edge1, edge2).normalized;
            if (n.y < 0) n = -n;
            if (distance < 0 || distance >= best || n.y < .5f) continue;
            best = distance; point = ray.GetPoint(distance); normal = n;
        }
        if (float.IsInfinity(best)) throw new Exception("No physical podium surface beneath toggle. Placement not saved.");
    }

    static Mesh SourceCounterMesh() => AssetDatabase.LoadAllAssetsAtPath("Assets/03_Prefabs/Counter.FBX")
        .OfType<Mesh>().Single(m => m.name == "ULT2_Counter");

    static void IntegrateSlopedControlFace(Transform counter, RectTransform button, float surfaceY)
    {
        var source = SourceCounterMesh();
        var wedge = new Mesh();
        try
        {
            BuildHousing(wedge, 166, 72, 36);
            var vertices = wedge.vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                var world = button.TransformPoint(vertices[i]);
                // Extend the bevelled inclined face down into the original countertop.
                if (vertices[i].z > 1) world.y = surfaceY - .002f;
                vertices[i] = counter.InverseTransformPoint(world);
            }
            wedge.vertices = vertices; wedge.RecalculateNormals();
            var combined = UnityEngine.Object.Instantiate(source);
            combined.name = "Counter Integrated Script Control";
            int offset = source.vertexCount;
            combined.vertices = source.vertices.Concat(vertices).ToArray();
            combined.normals = source.normals.Concat(wedge.normals).ToArray();
            combined.uv = source.uv.Concat(wedge.uv).ToArray();
            combined.SetTriangles(source.GetTriangles(1).Concat(wedge.triangles.Select(i => i + offset)).ToArray(), 1);
            combined.RecalculateBounds(); combined.RecalculateTangents();
            RehearGeneratedMeshLighting.Unwrap(combined);
            string path = Folder + "/Counter Integrated Script Control.asset";
            var asset = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (!asset) { AssetDatabase.CreateAsset(combined, path); asset = combined; }
            else
            {
                // Update native mesh buffers as well as serialized fields on repeat edits.
                asset.Clear(); asset.indexFormat = combined.indexFormat;
                asset.vertices = combined.vertices; asset.normals = combined.normals;
                asset.tangents = combined.tangents; asset.uv = combined.uv; asset.uv2 = combined.uv2;
                asset.subMeshCount = combined.subMeshCount;
                for (int i = 0; i < combined.subMeshCount; i++) asset.SetTriangles(combined.GetTriangles(i), i);
                asset.RecalculateBounds(); asset.UploadMeshData(false);
                UnityEngine.Object.DestroyImmediate(combined);
            }
            Save(asset);
            var filter = counter.GetComponent<MeshFilter>();
            Undo.RecordObject(filter, "Integrate control face into Counter mesh");
            filter.sharedMesh = null;
            filter.sharedMesh = asset;
            PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
            // The old bake targets a different mesh/UV layout; use probes until rebaked.
            var renderer = counter.GetComponent<MeshRenderer>();
            RehearGeneratedMeshLighting.Configure(renderer);
            Undo.RecordObject(renderer, "Invalidate outdated countertop lightmap");
            renderer.lightmapIndex = -1;
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
        }
        finally { UnityEngine.Object.DestroyImmediate(wedge); }
    }

    // Rounded, bevelled solid enclosure: original screen remains in front at z=0.
    static void BuildHousing(Mesh mesh, float width, float height, float cornerRadius = .75f)
    {
        var vertices = new List<Vector3>(); var triangles = new List<int>();
        const int segments = 8, count = 4 * (segments + 1);
        float[] widths = { width + 1.9f, width + 2.4f, width + 2.4f, width + 1.9f };
        float[] heights = { height + 1.9f, height + 2.4f, height + 2.4f, height + 1.9f };
        float[] depths = { .2f, .45f, 2.45f, 2.7f };
        for (int ring = 0; ring < 4; ring++)
        {
            float radius = cornerRadius + (ring == 0 || ring == 3 ? 0 : .25f);
            for (int corner = 0; corner < 4; corner++)
            {
                float cx = (corner == 0 || corner == 3 ? 1 : -1) * (widths[ring] / 2 - radius);
                float cy = (corner < 2 ? 1 : -1) * (heights[ring] / 2 - radius);
                for (int i = 0; i <= segments; i++)
                {
                    float angle = (corner * 90 + i * 90f / segments) * Mathf.Deg2Rad;
                    vertices.Add(new Vector3(cx + Mathf.Cos(angle) * radius, cy + Mathf.Sin(angle) * radius, depths[ring]));
                }
            }
        }
        for (int ring = 0; ring < 3; ring++)
            for (int i = 0; i < count; i++)
            {
                int a = ring * count + i, b = ring * count + (i + 1) % count, c = a + count, d = b + count;
                triangles.AddRange(new[] { a, b, c, b, d, c });
            }
        int front = vertices.Count; vertices.Add(new Vector3(0, 0, depths[0]));
        int back = vertices.Count; vertices.Add(new Vector3(0, 0, depths[3]));
        for (int i = 0; i < count; i++)
        {
            int n = (i + 1) % count;
            triangles.AddRange(new[] { front, n, i, back, 3 * count + i, 3 * count + n });
        }
        mesh.Clear(); mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0);
        mesh.SetUVs(0, vertices.Select(v => new Vector2(v.x / (width + 2.4f) + .5f, v.y / (height + 2.4f) + .5f)).ToList());
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        RehearGeneratedMeshLighting.Unwrap(mesh);
    }
}
