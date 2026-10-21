using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.Rendering.Universal;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
internal static class RehearConferenceHallSetup
{
    const string ScenePath = "Assets/01_Scene/ConferenceHall.unity";
    const string MaterialFolder = "Assets/Conference hall - meeting/Conference hall - meeting/models/Materials";
    const string RequestPath = "Temp/RehearConferenceHallUrp.request";
    const string ReportPath = "Temp/RehearConferenceHallUrp.txt";
    const string LightingRequest = "Temp/RehearConferenceHallLighting-v6.request";
    const string LightingReport = "Temp/RehearConferenceHallLighting.txt";
    const string BakingKey = "Rehear.ConferenceHall.Baking";
    static bool bakeFinished;
    static double nextProgress;

    static RehearConferenceHallSetup()
    {
        File.WriteAllText("Temp/RehearConferenceHallLighting.ready", "v11");
        EditorApplication.update += Poll;
        Lightmapping.bakeCompleted += () => { if (SessionState.GetBool(BakingKey, false)) bakeFinished = true; };
    }

    [MenuItem("Rehear/Conference Hall/Convert Materials To URP")]
    static void RequestApply() => File.WriteAllText(RequestPath, "apply");

    [MenuItem("Rehear/Conference Hall/Audit Materials")]
    static void RequestAudit() => File.WriteAllText(RequestPath, "audit");

    static void Poll()
    {
        if (AssetDatabase.IsAssetImportWorkerProcess() ||
            EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer)
            return;
        try
        {
            if (SessionState.GetBool(BakingKey, false))
            {
                if (bakeFinished) { bakeFinished = false; CompleteBake(); }
                else if (EditorApplication.timeSinceStartup >= nextProgress)
                {
                    nextProgress = EditorApplication.timeSinceStartup + 20;
                    File.WriteAllText("Temp/RehearConferenceHallLighting-progress.txt", $"running={Lightmapping.isRunning}\nprogress={Lightmapping.buildProgress:P1}\n");
                }
            }
            if (Lightmapping.isRunning) return;
            if (File.Exists(LightingRequest))
            {
                string lightingCommand = File.ReadAllText(LightingRequest).Trim();
                File.Delete(LightingRequest);
                if (lightingCommand != "bake") throw new ArgumentException("Unknown lighting command: " + lightingCommand);
                BakeCurrent();
                return;
            }
            if (!File.Exists(RequestPath)) return;
            string command = File.ReadAllText(RequestPath).Trim();
            File.Delete(RequestPath);
            if (command == "wall-audit") { AuditWall(); return; }
            if (command == "fixture-audit") { AuditFixtures(); return; }
            if (command == "surface-fix") { FixSurfaces(); return; }
            if (command == "capture") { CaptureViews(RequireHall(), "current"); AuditFixtures(); return; }
            if (command == "safety-fix") { FixSafetyFixtures(); return; }
            if (command == "lamps-only") { LampsOnly(); return; }
            if (command == "chairs-only") { ChairsOnly(); return; }
            if (command == "chair-diagnostics") { DiagnoseChairs(); return; }
            if (command == "chair-probe-fix") { ChairProbeFix(); return; }
            if (command == "fixture-chair-fix") { FixtureAndChairFix(); return; }
            if (command == "lamp-mesh-fix") { RepairLamps(); return; }
            if (command == "wood-seamless-fix") { ApplyWoodSeamless(); return; }
            if (command != "apply" && command != "audit")
                throw new ArgumentException("Unknown command: " + command);
            Run(command == "apply");
        }
        catch (Exception exception)
        {
            SessionState.SetBool(BakingKey, false);
            File.AppendAllText(LightingReport, "\nFAILED\n" + exception);
            File.WriteAllText(ReportPath, "FAILED\n" + exception);
            Debug.LogException(exception);
        }
    }

    static bool IsUrp(Material material) => material.shader &&
        material.shader.name.StartsWith("Universal Render Pipeline/", StringComparison.Ordinal);

    static void AuditWall()
    {
        var report = new StringBuilder("Active scene: " + SceneManager.GetActiveScene().path + "\n");
        foreach (var selected in Selection.objects) report.AppendLine("SELECTED | " + selected.name + " | " + AssetDatabase.GetAssetPath(selected));
        var renderers = SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Renderer>(true));
        foreach (var renderer in renderers)
        {
            foreach (var material in renderer.sharedMaterials.Where(m => m && (m.name.ToLowerInvariant().Contains("wall") || renderer.name.ToLowerInvariant().Contains("wall"))))
            {
                report.AppendLine("RENDERER | " + renderer.name + " | MATERIAL | " + material.name + " | " + AssetDatabase.GetAssetPath(material));
                foreach (var property in new[] { "_BaseMap", "_BumpMap", "_MetallicGlossMap", "_ParallaxMap", "_OcclusionMap" })
                    if (material.HasProperty(property)) report.AppendLine(property + " | " + AssetDatabase.GetAssetPath(material.GetTexture(property)) + " | scale=" + material.GetTextureScale(property));
            }
        }
        File.WriteAllText("Temp/RehearWallAudit.txt", report.ToString());
    }

    static void AuditFixtures()
    {
        Scene scene = RequireHall();
        var report = new StringBuilder();
        foreach (var renderer in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MeshRenderer>(true)))
        {
            var filter = renderer.GetComponent<MeshFilter>();
            report.AppendLine($"MESH | {renderer.name} | bounds={renderer.bounds} | materials={string.Join(",", renderer.sharedMaterials.Where(m => m).Select(m => m.name))}");
            if (filter && filter.sharedMesh && renderer.sharedMaterials.Any(m => m && m.name.ToLowerInvariant().Contains("light")))
            {
                var mesh = filter.sharedMesh;
                report.AppendLine("FIXTURE MESH | " + AssetDatabase.GetAssetPath(mesh) + " | submeshes=" + mesh.subMeshCount);
                for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                {
                    var vertices = mesh.vertices;
                    var points = mesh.GetTriangles(submesh).Distinct().Select(i => renderer.transform.TransformPoint(vertices[i])).ToArray();
                    Bounds bounds = new Bounds(points[0], Vector3.zero);
                    foreach (var point in points) bounds.Encapsulate(point);
                    report.AppendLine($"SUBMESH | {submesh} | material={renderer.sharedMaterials[Math.Min(submesh, renderer.sharedMaterials.Length - 1)].name} | bounds={bounds}");
                    if (renderer.sharedMaterials[Math.Min(submesh, renderer.sharedMaterials.Length - 1)].name == "light")
                    {
                        var clusters = new List<List<Vector3>>();
                        foreach (var point in points)
                        {
                            var cluster = clusters.FirstOrDefault(group => Vector3.Distance(group[0], point) < 0.6f);
                            if (cluster == null) { cluster = new List<Vector3>(); clusters.Add(cluster); }
                            cluster.Add(point);
                        }
                        foreach (var cluster in clusters)
                            report.AppendLine("LAMP CENTER | " + (cluster.Aggregate(Vector3.zero, (sum, point) => sum + point) / cluster.Count));
                    }
                }
            }
        }
        foreach (var material in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Renderer>(true)).SelectMany(r => r.sharedMaterials).Where(m => m && m.name.ToLowerInvariant().Contains("light")).Distinct())
            report.AppendLine($"EMISSION | {material.name} | color={material.GetColor("_EmissionColor")} | flags={material.globalIlluminationFlags} | keywords={string.Join(",", material.shaderKeywords)}");
        File.WriteAllText("Temp/RehearFixtureAudit.txt", report.ToString());
    }

    static void FixSurfaces()
    {
        Scene scene = RequireHall();
        var report = new StringBuilder();
        foreach (string name in new[] { "wall", "fire_alarm", "Sprinkler", "wall megaphone" })
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/" + name + ".mat");
            Undo.RecordObject(material, "Correct room surface reflections");
            material.SetColor("_EmissionColor", Color.black);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            if (name != "Sprinkler")
            {
                material.SetTexture("_MetallicGlossMap", null);
                material.SetFloat("_Metallic", 0);
                material.SetFloat("_Smoothness", name == "wall" ? 0.05f : 0.12f);
            }
            else material.SetFloat("_Smoothness", 0.25f);
            if (name == "wall")
            {
                material.SetFloat("_BumpScale", 0.35f);
                material.SetFloat("_EnvironmentReflections", 1);
                material.SetFloat("_SpecularHighlights", 1);
                material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(MaterialFolder.Replace("/Materials", "/textures") + "/wall_AlbedoTransparency.png"));
            }
            Validate(material);
            EditorUtility.SetDirty(material);
            report.AppendLine($"SURFACE | {name} | emission={material.IsKeywordEnabled("_EMISSION")} | metallic={material.GetFloat("_Metallic")} | smoothness={material.GetFloat("_Smoothness")}");
        }
        var lamp = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/light.mat");
        Undo.RecordObject(lamp, "Keep ceiling lamp emission visible");
        lamp.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        Validate(lamp);
        lamp.EnableKeyword("_EMISSION");
        EditorUtility.SetDirty(lamp);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        CaptureViews(scene, "surface-fixed");
        report.AppendLine($"LAMP | emission={lamp.IsKeywordEnabled("_EMISSION")} | flags={lamp.globalIlluminationFlags}");
        File.WriteAllText("Temp/RehearSurfaceFix.txt", report.ToString());
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
    }

    static void FixSafetyFixtures()
    {
        Scene scene = RequireHall();
        var report = new StringBuilder();
        foreach (string name in new[] { "fire_alarm", "Sprinkler" })
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/" + name + ".mat");
            Undo.RecordObject(material, "Remove safety fixture glow");
            material.SetColor("_EmissionColor", Color.black);
            material.SetTexture("_EmissionMap", null);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            material.SetTexture("_MetallicGlossMap", null);
            material.SetFloat("_Metallic", name == "fire_alarm" ? 0 : 0.5f);
            material.SetFloat("_Smoothness", 0.12f);
            Validate(material);
            EditorUtility.SetDirty(material);
        }
        foreach (var renderer in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MeshRenderer>(true)).Where(renderer => renderer.name.StartsWith("Firealarm") || renderer.name.StartsWith("Sprinkler")))
        {
            Undo.RecordObjects(new UnityEngine.Object[] { renderer, renderer.gameObject }, "Remove direct baked light from safety fixtures");
            GameObjectUtility.SetStaticEditorFlags(renderer.gameObject, GameObjectUtility.GetStaticEditorFlags(renderer.gameObject) | StaticEditorFlags.ContributeGI);
            renderer.receiveGI = ReceiveGI.LightProbes;
            renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
            renderer.lightmapIndex = -1;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            var anchor = renderer.transform.Find("Ambient Lighting Anchor");
            if (!anchor)
            {
                var obj = new GameObject("Ambient Lighting Anchor");
                Undo.RegisterCreatedObjectUndo(obj, "Set safety fixture ambient lighting sample");
                obj.transform.SetParent(renderer.transform, false);
                anchor = obj.transform;
            }
            anchor.position = new Vector3(0, 1.4f, renderer.bounds.center.z);
            renderer.probeAnchor = anchor;
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer.gameObject);
            EditorUtility.SetDirty(renderer);
            report.AppendLine($"SAFETY | {renderer.name} | emission=False | receiveGI={renderer.receiveGI} | lightmapIndex={renderer.lightmapIndex}");
        }
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        CaptureViews(scene, "safety-fixed");
        File.WriteAllText("Temp/RehearSafetyFix.txt", report.ToString());
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
    }

    static void LampsOnly()
    {
        Scene scene = RequireHall();
        var renderers = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MeshRenderer>(true)).ToArray();
        var fixture = renderers.Single(renderer => renderer.name == "lamps");
        Light[] lights = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Light>(true)).ToArray();
        Light[] active = lights.Where(light => light.enabled && light.gameObject.activeInHierarchy).ToArray();
        if (active.Length != 12) throw new InvalidOperationException("Expected 12 active lamps before restricting lights to the lamp model.");
        for (int i = 0; i < active.Length; i++)
        {
            Undo.RecordObject(active[i].gameObject, "Assign ceiling lights to lamps model");
            Undo.SetTransformParent(active[i].transform, fixture.transform, "Assign ceiling lights to lamps model");
            active[i].name = "Ceiling Light " + (i + 1).ToString("D2");
        }
        string path = MaterialFolder + "/lamps_Emission.mat";
        Material emitting = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!emitting)
        {
            emitting = new Material(AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/light.mat"));
            emitting.name = "lamps_Emission";
            AssetDatabase.CreateAsset(emitting, path);
        }
        emitting.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        Validate(emitting);
        EditorUtility.SetDirty(emitting);
        var slots = fixture.sharedMaterials;
        for (int i = 0; i < slots.Length; i++) if (slots[i] && (slots[i].name == "light" || slots[i].name == "lamps_Emission")) slots[i] = emitting;
        Undo.RecordObject(fixture, "Restrict luminous surface to ceiling lamps");
        fixture.sharedMaterials = slots;
        PrefabUtility.RecordPrefabInstancePropertyModifications(fixture);
        EditorUtility.SetDirty(fixture);
        var otherMaterials = renderers.Where(renderer => renderer != fixture).SelectMany(renderer => renderer.sharedMaterials).Where(material => material).Distinct();
        foreach (var material in otherMaterials)
        {
            if (!material.HasProperty("_EmissionColor")) continue;
            Undo.RecordObject(material, "Remove emission outside lamps");
            material.SetColor("_EmissionColor", Color.black);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            Validate(material);
            EditorUtility.SetDirty(material);
        }
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        File.WriteAllText("Temp/RehearLampsOnly.txt", $"LAMPS | emitting renderer=lamps | lamp material={path} | active child lights={fixture.GetComponentsInChildren<Light>().Count(light => light.enabled)} | active lights outside lamps={active.Count(light => !light.transform.IsChildOf(fixture.transform))}\n");
        CaptureViews(scene, "lamps-only");
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
    }

    static void ChairsOnly()
    {
        Scene scene = RequireHall();
        File.WriteAllText(LightingReport, "CHAIR REPAIR BEGIN\n");
        CaptureViews(scene, "before-chair-fix");
        RepairChairs(scene);
        AssetDatabase.SaveAssets();
        Lightmapping.Clear();
        Lightmapping.lightingDataAsset = null;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        SessionState.SetBool(BakingKey, true);
        nextProgress = 0;
        if (!Lightmapping.BakeAsync()) { SessionState.SetBool(BakingKey, false); throw new InvalidOperationException("Chair verification bake did not start."); }
        File.AppendAllText(LightingReport, "BAKE STARTED | existing user light settings preserved\n");
    }

    static void DiagnoseChairs()
    {
        Scene scene = RequireHall();
        var chairs = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MeshRenderer>(true)).Where(renderer => renderer.name.StartsWith("Chair_2")).ToArray();
        var materials = chairs.SelectMany(renderer => renderer.sharedMaterials).Distinct().ToArray();
        var maps = materials.Select(material => material.GetTexture("_BumpMap")).ToArray();
        try
        {
            foreach (var material in materials) { material.SetTexture("_BumpMap", null); Validate(material); }
            CaptureViews(scene, "chair-no-normalmap");
        }
        finally { for (int i = 0; i < materials.Length; i++) { materials[i].SetTexture("_BumpMap", maps[i]); Validate(materials[i]); } }
        var saved = chairs.Select(renderer => (renderer, flags: GameObjectUtility.GetStaticEditorFlags(renderer.gameObject), gi: renderer.receiveGI, index: renderer.lightmapIndex, uv: renderer.lightmapScaleOffset, probes: renderer.lightProbeUsage)).ToArray();
        try
        {
            foreach (var item in saved)
            {
                GameObjectUtility.SetStaticEditorFlags(item.renderer.gameObject, item.flags & ~StaticEditorFlags.ContributeGI);
                item.renderer.receiveGI = ReceiveGI.LightProbes;
                item.renderer.lightmapIndex = -1;
                item.renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
            }
            CaptureViews(scene, "chair-probes");
        }
        finally
        {
            foreach (var item in saved)
            {
                GameObjectUtility.SetStaticEditorFlags(item.renderer.gameObject, item.flags);
                item.renderer.receiveGI = item.gi;
                item.renderer.lightmapIndex = item.index;
                item.renderer.lightmapScaleOffset = item.uv;
                item.renderer.lightProbeUsage = item.probes;
            }
        }
    }

    static void ChairProbeFix()
    {
        Scene scene = RequireHall();
        ApplyChairProbeLighting(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        CaptureViews(scene, "chair-probes-fixed");
        BakeCurrent();
    }

    static void FixtureAndChairFix()
    {
        Scene scene = RequireHall();
        EditorSceneManager.SaveScene(scene, "Temp/ConferenceHall-before-fixture-chair-fix.unity", true);
        CaptureViews(scene, "before-fixture-chair-fix");
        LampsOnly();
        FixSafetyFixtures();
        var fixture = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MeshRenderer>(true))
            .Single(renderer => renderer.name == "lamps");
        foreach (var light in fixture.GetComponentsInChildren<Light>(true).Where(light => light.enabled))
        {
            Undo.RecordObjects(new UnityEngine.Object[] { light, light.transform }, "Fit light source inside lamp face");
            light.type = LightType.Rectangle;
            light.areaSize = new Vector2(0.16f, 0.16f);
            light.intensity = 1125f;
            light.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            light.lightmapBakeType = LightmapBakeType.Baked;
            PrefabUtility.RecordPrefabInstancePropertyModifications(light);
            EditorUtility.SetDirty(light);
        }
        ApplyChairProbeLighting(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        BakeCurrent();
    }

    static void RepairLamps()
    {
        Scene scene = RequireHall();
        CaptureViews(scene, "before-lamp-mesh");
        string models = MaterialFolder.Replace("/Materials", "");
        string path = models + "/Lamps_Smooth.fbx";
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (ModelImporter)AssetImporter.GetAtPath(path);
        importer.isReadable = true;
        importer.importAnimation = false;
        importer.importNormals = ModelImporterNormals.Import;
        importer.swapUVChannels = false;
        importer.generateSecondaryUV = true;
        importer.SaveAndReimport();
        var originalFilter = AssetDatabase.LoadAssetAtPath<GameObject>(models + "/room.fbx")
            .GetComponentsInChildren<MeshFilter>(true).Single(filter => filter.name == "lamps");
        var fixedFilter = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponentInChildren<MeshFilter>(true);
        Matrix4x4 conversion = originalFilter.transform.localToWorldMatrix.inverse * fixedFilter.transform.localToWorldMatrix;
        Mesh mesh = UnityEngine.Object.Instantiate(fixedFilter.sharedMesh);
        mesh.name = "Lamps_Smooth";
        mesh.vertices = mesh.vertices.Select(conversion.MultiplyPoint3x4).ToArray();
        Matrix4x4 normalConversion = conversion.inverse.transpose;
        mesh.normals = mesh.normals.Select(normal => normalConversion.MultiplyVector(normal).normalized).ToArray();
        mesh.tangents = mesh.tangents.Select(tangent =>
        {
            Vector3 direction = conversion.MultiplyVector(new Vector3(tangent.x, tangent.y, tangent.z)).normalized;
            return new Vector4(direction.x, direction.y, direction.z, tangent.w * (conversion.determinant < 0 ? -1 : 1));
        }).ToArray();
        if (conversion.determinant < 0)
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                int[] triangles = mesh.GetTriangles(i);
                for (int j = 0; j < triangles.Length; j += 3) { int temp = triangles[j + 1]; triangles[j + 1] = triangles[j + 2]; triangles[j + 2] = temp; }
                mesh.SetTriangles(triangles, i);
            }
        mesh.RecalculateBounds();
        Bounds originalBounds = originalFilter.sharedMesh.bounds;
        if (Vector3.Distance(mesh.bounds.size, originalBounds.size) > 0.005f || Vector3.Distance(mesh.bounds.center, originalBounds.center) > 0.005f)
            throw new InvalidOperationException($"Lamp bounds changed: original={originalBounds}, new={mesh.bounds}");
        string meshPath = models + "/Lamps_Smooth.asset";
        Mesh saved = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        if (saved) { EditorUtility.CopySerialized(mesh, saved); EditorUtility.SetDirty(saved); UnityEngine.Object.DestroyImmediate(mesh); }
        else { AssetDatabase.CreateAsset(mesh, meshPath); saved = mesh; }
        var fixture = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MeshRenderer>(true)).Single(renderer => renderer.name == "lamps");
        string materialPath = MaterialFolder + "/lamps_Housing.mat";
        Material housing = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (!housing) { housing = new Material(fixture.sharedMaterials[0]); housing.name = "lamps_Housing"; AssetDatabase.CreateAsset(housing, materialPath); }
        housing.SetTexture("_BaseMap", null);
        housing.SetTexture("_BumpMap", null);
        housing.SetTexture("_MetallicGlossMap", null);
        housing.SetColor("_BaseColor", new Color(0.45f, 0.45f, 0.45f, 1));
        housing.SetFloat("_Metallic", 0.25f);
        housing.SetFloat("_Smoothness", 0.2f);
        housing.SetColor("_EmissionColor", Color.black);
        housing.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        Validate(housing);
        EditorUtility.SetDirty(housing);
        Undo.RecordObjects(new UnityEngine.Object[] { fixture, fixture.GetComponent<MeshFilter>() }, "Replace smooth lamp geometry");
        fixture.GetComponent<MeshFilter>().sharedMesh = saved;
        fixture.sharedMaterials = new[] { housing, fixture.sharedMaterials[1] };
        GameObjectUtility.SetStaticEditorFlags(fixture.gameObject, GameObjectUtility.GetStaticEditorFlags(fixture.gameObject) | StaticEditorFlags.ContributeGI);
        fixture.receiveGI = ReceiveGI.LightProbes;
        fixture.lightProbeUsage = LightProbeUsage.BlendProbes;
        fixture.lightmapIndex = -1;
        PrefabUtility.RecordPrefabInstancePropertyModifications(fixture);
        PrefabUtility.RecordPrefabInstancePropertyModifications(fixture.GetComponent<MeshFilter>());
        EditorUtility.SetDirty(fixture);
        EditorSceneManager.MarkSceneDirty(scene);
        File.WriteAllText("Temp/RehearLampMeshFix.txt", $"LAMPS | count=12 | round segments=128 | non-overlapping diffuser | bounds preserved={saved.bounds} | receiveGI=LightProbes | existing 12 light transforms preserved\n");
        BakeCurrent();
    }

    static void ApplyWoodSeamless()
    {
        Scene scene = RequireHall();
        string textures = MaterialFolder.Replace("/Materials", "/textures");
        string colorPath = textures + "/wood_diffuse_Seamless.png";
        string normalPath = textures + "/wood_Normal_Seamless.png";
        foreach (string path in new[] { colorPath, normalPath })
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 8;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.sRGBTexture = path == colorPath;
            importer.textureType = path == normalPath ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.convertToNormalmap = path == normalPath;
            importer.heightmapScale = 0.007f;
            importer.SaveAndReimport();
        }
        var original = AssetDatabase.LoadAssetAtPath<Texture2D>(textures + "/wood_diffuse.png");
        var color = AssetDatabase.LoadAssetAtPath<Texture2D>(colorPath);
        var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
        var report = new StringBuilder();
        foreach (var material in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Renderer>(true))
            .SelectMany(renderer => renderer.sharedMaterials).Where(material => material).Distinct()
            .Where(material => material.HasProperty("_BaseMap") && (material.GetTexture("_BaseMap") == original || material.name == "wall_2_Wood_Seamless")))
        {
            Undo.RecordObject(material, "Replace seamless wood texture");
            Vector2 scale = material.GetTextureScale("_BaseMap");
            Vector2 offset = material.GetTextureOffset("_BaseMap");
            material.SetTexture("_BaseMap", color);
            material.SetTexture("_BumpMap", normal);
            material.SetTextureScale("_BaseMap", scale);
            material.SetTextureOffset("_BaseMap", offset);
            material.SetTextureScale("_BumpMap", scale);
            material.SetTextureOffset("_BumpMap", offset);
            Validate(material);
            EditorUtility.SetDirty(material);
            report.AppendLine($"WOOD | material={material.name} | tiling preserved={scale} | revised color and matching normal");
        }
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        File.WriteAllText("Temp/RehearWoodSeamlessFix.txt", report.ToString());
        BakeCurrent();
    }

    static void ApplyChairProbeLighting(Scene scene)
    {
        var chairs = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MeshRenderer>(true))
            .Where(renderer => renderer.name.StartsWith("Chair_2")).ToArray();
        foreach (var renderer in chairs)
        {
            Undo.RecordObject(renderer, "Smooth chair baked lighting with probes");
            GameObjectUtility.SetStaticEditorFlags(renderer.gameObject,
                GameObjectUtility.GetStaticEditorFlags(renderer.gameObject) | StaticEditorFlags.ContributeGI);
            renderer.receiveGI = ReceiveGI.LightProbes;
            renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
            renderer.lightmapIndex = -1;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            EditorUtility.SetDirty(renderer);
        }
        File.WriteAllText("Temp/RehearChairProbeFix.txt", $"CHAIRS | count={chairs.Length} | receiveGI=LightProbes | contributeGI=True | castShadows=On\n");
    }

    static void ConvertUnlit(Material material, string oldShader)
    {
        Texture texture = material.HasProperty("_MainTex") ? material.GetTexture("_MainTex") : null;
        Vector2 scale = material.HasProperty("_MainTex") ? material.GetTextureScale("_MainTex") : Vector2.one;
        Vector2 offset = material.HasProperty("_MainTex") ? material.GetTextureOffset("_MainTex") : Vector2.zero;
        Color color = material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white;
        float cutoff = material.HasProperty("_Cutoff") ? material.GetFloat("_Cutoff") : 0.5f;
        material.shader = Shader.Find("Universal Render Pipeline/Unlit");
        material.shaderKeywords = Array.Empty<string>();
        material.SetTexture("_BaseMap", texture);
        material.SetTextureScale("_BaseMap", scale);
        material.SetTextureOffset("_BaseMap", offset);
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Cutoff", cutoff);
        material.SetFloat("_Surface", oldShader == "Unlit/Transparent" ? 1 : 0);
        material.SetFloat("_Blend", 0);
        material.SetFloat("_AlphaClip", oldShader == "Unlit/Transparent Cutout" ? 1 : 0);
    }

    static void Validate(Material material)
    {
        string guiName = material.shader.name == "Universal Render Pipeline/Unlit" ? "UnlitShader" : "LitShader";
        Type type = typeof(StandardUpgrader).Assembly.GetType("UnityEditor.Rendering.Universal.ShaderGUI." + guiName, true);
        ((ShaderGUI)Activator.CreateInstance(type, true)).ValidateMaterial(material);
        if (!material.shader.isSupported || ShaderUtil.ShaderHasError(material.shader))
            throw new InvalidOperationException("Unsupported or failed shader: " + material.name);
    }

    static void Run(bool apply)
    {
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool preview = !scene.IsValid() || !scene.isLoaded;
        if (preview) scene = EditorSceneManager.OpenPreviewScene(ScenePath);
        try
        {
            var renderers = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Renderer>(true)).ToArray();
            var materials = new HashSet<Material>(renderers.SelectMany(renderer => renderer.sharedMaterials).Where(material => material));
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { MaterialFolder }))
                materials.Add(AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid)));
            var report = new StringBuilder();
            report.AppendLine("Scene: " + ScenePath);
            report.AppendLine("Renderers (including inactive): " + renderers.Length);
            report.AppendLine("Unique materials including asset pack: " + materials.Count);
            int converted = 0;
            report.AppendLine("ACTIVE SCENE | " + SceneManager.GetActiveScene().path);
            foreach (Light light in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Light>(true)))
                report.AppendLine($"LIGHT | {light.name} | {light.type} | {light.lightmapBakeType} | enabled={light.enabled} | intensity={light.intensity} | pos={light.transform.position} | direction={light.transform.forward} | area={light.areaSize}");
            foreach (Camera camera in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Camera>(true)))
                report.AppendLine($"CAMERA | {camera.name} | pos={camera.transform.position} | direction={camera.transform.forward} | fov={camera.fieldOfView}");
            foreach (Renderer renderer in renderers.OrderByDescending(r => r.bounds.size.sqrMagnitude).Take(12))
                report.AppendLine($"LARGE MESH | {renderer.name} | bounds={renderer.bounds} | materials={string.Join(",", renderer.sharedMaterials.Where(m => m).Select(m => m.name))}");
            report.AppendLine("LIGHTMAPS | " + LightmapSettings.lightmaps.Length);
            foreach (Material material in materials.OrderBy(AssetDatabase.GetAssetPath))
            {
                string path = AssetDatabase.GetAssetPath(material);
                string oldShader = material.shader ? material.shader.name : "MISSING";
                report.AppendLine($"MATERIAL | {material.name} | {path} | {oldShader}");
                if (!apply || IsUrp(material)) continue;
                if (!path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Material needs extraction before conversion: " + path);
                bool standard = oldShader == "Standard" || oldShader == "Standard (Specular setup)";
                bool unlit = oldShader == "Unlit/Color" || oldShader == "Unlit/Texture" ||
                    oldShader == "Unlit/Transparent" || oldShader == "Unlit/Transparent Cutout";
                if (!standard && !unlit)
                    throw new InvalidOperationException("Unsupported source shader: " + oldShader + " at " + path);
                string backup = "Temp/RehearConferenceHallUrpBackup/" + AssetDatabase.AssetPathToGUID(path) + ".mat";
                Directory.CreateDirectory(Path.GetDirectoryName(backup));
                if (!File.Exists(backup)) File.Copy(path, backup);
                Undo.RecordObject(material, "Convert Conference Hall Material To URP");
                if (standard) new StandardUpgrader(oldShader).Upgrade(material, MaterialUpgrader.UpgradeFlags.None);
                else ConvertUnlit(material, oldShader);
                Validate(material);
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssetIfDirty(material);
                converted++;
                report.AppendLine($"CONVERTED | {material.name} | {material.shader.name} | keywords={string.Join(",", material.shaderKeywords)}");
            }
            int slots = 0;
            int missing = 0;
            int incompatible = 0;
            foreach (Renderer renderer in renderers)
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    slots++;
                    if (!material) { missing++; report.AppendLine("MISSING SLOT | " + renderer.name); }
                    else if (!IsUrp(material)) { incompatible++; report.AppendLine("NON URP SLOT | " + renderer.name + " | " + material.name); }
                }
            }
            report.AppendLine($"SUMMARY | converted={converted} | slots={slots} | missing={missing} | nonUrp={incompatible}");
            report.AppendLine(apply && missing == 0 && incompatible == 0 ? "PASS" : apply ? "INCOMPLETE" : "AUDIT COMPLETE");
            File.WriteAllText(ReportPath, report.ToString());
            if (apply) CaptureViews(scene, "urp");
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            Debug.Log("Conference Hall material audit: " + ReportPath);
        }
        finally { if (preview) EditorSceneManager.ClosePreviewScene(scene); }
    }

    static void CaptureViews(Scene scene, string label)
    {
        string stored = SessionState.GetString("Rehear.LectureTheatre.View", "");
        try
        {
            foreach (var view in new[] {
                (name: "front", position: new Vector3(0, 1.6f, -6.8f), target: new Vector3(0, 1.5f, 6), fov: 75f),
                (name: "rear", position: new Vector3(0, 1.6f, 6.8f), target: new Vector3(0, 1.5f, -6), fov: 75f),
                (name: "ceiling", position: new Vector3(0, 1.5f, 0), target: new Vector3(0, 3, 3), fov: 95f),
                (name: "safety-close", position: new Vector3(-2.1f, 2f, 5.8f), target: new Vector3(-2.24f, 2.93f, 6.56f), fov: 65f),
                (name: "lamp-close", position: new Vector3(-2.05f, 2.6f, 6.32f), target: new Vector3(-2.32f, 2.94f, 6.67f), fov: 40f),
                (name: "wall", position: new Vector3(0, 1.5f, 2), target: new Vector3(0, 1.5f, 7.95f), fov: 75f),
                (name: "chairs", position: new Vector3(1.3f, 1.3f, 5.2f), target: new Vector3(2.1f, 0.38f, 3.65f), fov: 65f)
            })
            {
                string pose = JsonUtility.ToJson(new CapturePose { position = view.position, rotation = Quaternion.LookRotation(view.target - view.position), fov = view.fov });
                SessionState.SetString("Rehear.LectureTheatre.View", pose);
                typeof(RehearLectureTheatreLightingSetup).GetMethod("Capture", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).Invoke(null, new object[] { "Temp/ConferenceHall-" + label + "-" + view.name + ".png", true });
            }
        }
        finally { SessionState.SetString("Rehear.LectureTheatre.View", stored); }
    }

    [Serializable] class CapturePose { public Vector3 position; public Quaternion rotation; public float fov; }

    static Scene RequireHall()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath || SceneManager.sceneCount != 1) throw new InvalidOperationException("Keep only ConferenceHall open for this bake.");
        return scene;
    }

    [MenuItem("Rehear/Conference Hall/Rebake Lighting")]
    static void RequestBake() => File.WriteAllText(LightingRequest, "bake");

    static void BakeCurrent()
    {
        Scene scene = RequireHall();
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        File.WriteAllText(LightingReport, "BAKE CURRENT | existing lights and materials preserved\n");
        SessionState.SetBool(BakingKey, true);
        nextProgress = 0;
        if (!Lightmapping.BakeAsync()) { SessionState.SetBool(BakingKey, false); throw new InvalidOperationException("ConferenceHall bake did not start."); }
    }

    static void BakeHall()
    {
        Scene scene = RequireHall();
        Light[] lights = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Light>(true)).ToArray();
        Light[] ceilingLights = lights.Where(light => Enumerable.Range(8, 12).Any(i => light.name == "Point light (" + i + ")")).ToArray();
        if (ceilingLights.Length != 12) throw new InvalidOperationException("Expected twelve ConferenceHall ceiling lights.");
        Directory.CreateDirectory("Temp/RehearConferenceHallLightingBackup");
        EditorSceneManager.SaveScene(scene, "Temp/RehearConferenceHallLightingBackup/Before.unity", true);
        CaptureViews(scene, "before-bake");
        File.WriteAllText(LightingReport, "BEGIN | " + DateTime.UtcNow.ToString("O") + "\n");
        RepairChairs(scene);
        foreach (var renderer in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MeshRenderer>(true)).Where(renderer => renderer.name.StartsWith("Firealarm") || renderer.name.StartsWith("Sprinkler")))
        {
            Undo.RecordObject(renderer, "Use ambient probes for safety fixtures");
            Undo.RecordObject(renderer.gameObject, "Exclude safety fixtures from near-field lamp baking");
            GameObjectUtility.SetStaticEditorFlags(renderer.gameObject, GameObjectUtility.GetStaticEditorFlags(renderer.gameObject) & ~StaticEditorFlags.ContributeGI);
            renderer.receiveGI = ReceiveGI.LightProbes;
            renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
            renderer.lightmapIndex = -1;
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            EditorUtility.SetDirty(renderer);
        }
        if (!AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/wall_2_Wood_Seamless.mat")) ApplyWallTexture(scene);
        var fixture = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MeshRenderer>(true)).Single(renderer => renderer.name == "lamps");
        var fixtureMesh = fixture.GetComponent<MeshFilter>().sharedMesh;
        int emittingSubmesh = Array.FindIndex(fixture.sharedMaterials, material => material && material.name == "light");
        var fixtureVertices = fixtureMesh.vertices;
        var clusters = new List<List<Vector3>>();
        foreach (int index in fixtureMesh.GetTriangles(emittingSubmesh).Distinct())
        {
            Vector3 point = fixture.transform.TransformPoint(fixtureVertices[index]);
            var cluster = clusters.FirstOrDefault(group => Vector3.Distance(group[0], point) < 0.6f);
            if (cluster == null) { cluster = new List<Vector3>(); clusters.Add(cluster); }
            cluster.Add(point);
        }
        var fixturePositions = clusters.Select(group =>
        {
            Bounds bounds = new Bounds(group[0], Vector3.zero);
            foreach (var point in group) bounds.Encapsulate(point);
            return bounds.center;
        }).OrderBy(point => point.z).ThenBy(point => point.x).ToArray();
        if (fixturePositions.Length != ceilingLights.Length) throw new InvalidOperationException("Every ceiling lamp must have one matching light.");
        for (int i = 0; i < ceilingLights.Length; i++) ceilingLights[i].transform.position = fixturePositions[i] + Vector3.down * 0.035f;
        foreach (Light light in lights)
        {
            Undo.RecordObjects(new UnityEngine.Object[] { light, light.transform }, "Prepare ConferenceHall lighting");
            light.enabled = ceilingLights.Contains(light);
            if (light.enabled)
            {
                Vector3 position = light.transform.position;
                light.transform.SetPositionAndRotation(position, Quaternion.LookRotation(Vector3.down, Vector3.forward));
                light.type = LightType.Rectangle;
                light.areaSize = new Vector2(1.2f, 1.2f);
                light.intensity = 20;
                light.range = 20;
                light.color = new Color(1, 0.97f, 0.92f, 1);
                light.lightmapBakeType = LightmapBakeType.Baked;
                light.shadows = LightShadows.Soft;
                light.bounceIntensity = 1;
                light.cookie = null;
                File.AppendAllText(LightingReport, "CEILING LIGHT | " + position + "\n");
            }
            EditorUtility.SetDirty(light);
            EditorUtility.SetDirty(light.transform);
        }
        var missingUvModels = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MeshFilter>(true)).Where(filter => filter.sharedMesh && !filter.sharedMesh.HasVertexAttribute(VertexAttribute.TexCoord1)).Select(filter => AssetDatabase.GetAssetPath(filter.sharedMesh)).Where(path => !string.IsNullOrEmpty(path)).Distinct().ToArray();
        foreach (string path in missingUvModels)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (!importer) throw new InvalidOperationException("Cannot generate lightmap UVs: " + path);
            importer.generateSecondaryUV = true;
            importer.SaveAndReimport();
            File.AppendAllText(LightingReport, "GENERATED LIGHTMAP UV | " + path + "\n");
        }
        Material lamp = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/light.mat");
        Undo.RecordObject(lamp, "Illuminate ConferenceHall fixtures");
        lamp.SetColor("_BaseColor", new Color(0.95f, 0.93f, 0.88f, 1));
        lamp.SetColor("_EmissionColor", new Color(8, 7.76f, 7.36f, 1));
        lamp.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        Validate(lamp);
        lamp.EnableKeyword("_EMISSION");
        EditorUtility.SetDirty(lamp);
        LightProbeGroup probes = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<LightProbeGroup>(true)).FirstOrDefault();
        if (!probes)
        {
            var obj = new GameObject("Light Probe Group");
            Undo.RegisterCreatedObjectUndo(obj, "Add ConferenceHall light probes");
            probes = Undo.AddComponent<LightProbeGroup>(obj);
            probes.probePositions = (from x in new[] { -3f, 0f, 3f } from z in new[] { -7f, -5f, -3f, -1f, 1f, 3f, 5f, 7f } from y in new[] { 0.3f, 1.4f, 2.65f } select new Vector3(x, y, z)).ToArray();
            EditorUtility.SetDirty(probes);
        }
        const string settingsPath = "Assets/01_Scene/ConferenceHallSettings.lighting";
        LightingSettings settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(settingsPath);
        if (!settings)
        {
            settings = Lightmapping.TryGetLightingSettings(out var previous) && previous ? UnityEngine.Object.Instantiate(previous) : new LightingSettings();
            settings.name = "ConferenceHallSettings";
            AssetDatabase.CreateAsset(settings, settingsPath);
        }
        Lightmapping.lightingSettings = settings;
        settings.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
        settings.bakedGI = true;
        settings.realtimeGI = false;
        settings.lightmapResolution = 40;
        settings.lightmapMaxSize = 1024;
        settings.directSampleCount = 512;
        settings.indirectSampleCount = 2048;
        settings.environmentSampleCount = 128;
        settings.maxBounces = 4;
        settings.indirectScale = 1.8f;
        settings.ao = true;
        settings.aoMaxDistance = 0.1f;
        settings.aoExponentIndirect = 0.3f;
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        Lightmapping.Clear();
        Lightmapping.lightingDataAsset = null;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        File.AppendAllText(LightingReport, $"RIG | diffuse ceiling lights={ceilingLights.Length} | disabled duplicate and fill lights={lights.Length - ceilingLights.Length} | probes={probes.probePositions.Length}\n");
        SessionState.SetBool(BakingKey, true);
        nextProgress = 0;
        if (!Lightmapping.BakeAsync()) { SessionState.SetBool(BakingKey, false); throw new InvalidOperationException("ConferenceHall bake did not start."); }
        File.AppendAllText(LightingReport, "BAKE STARTED\n");
    }

    static void CompleteBake()
    {
        SessionState.SetBool(BakingKey, false);
        Scene scene = RequireHall();
        if (!Lightmapping.lightingDataAsset || LightmapSettings.lightmaps.Length == 0) throw new InvalidOperationException("Bake ended without lightmaps.");
        string lightingPath = AssetDatabase.GetAssetPath(Lightmapping.lightingDataAsset);
        if (!lightingPath.StartsWith("Assets/01_Scene/ConferenceHall/", StringComparison.Ordinal)) throw new InvalidOperationException("Lighting data must belong to ConferenceHall: " + lightingPath);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        File.AppendAllText(LightingReport, $"BAKE COMPLETED | {DateTime.UtcNow:O}\nLIGHTMAPS | {LightmapSettings.lightmaps.Length}\nLIGHTING DATA | {lightingPath}\nPASS\n");
        CaptureViews(scene, "baked");
        File.WriteAllText("Temp/RehearConferenceHallLighting-progress.txt", "running=False\nprogress=100%\n");
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
    }

    static void ApplyWallTexture(Scene scene)
    {
        string textures = MaterialFolder.Replace("/Materials", "/textures");
        string basePath = textures + "/wall_2_Wood_Seamless_BaseColor.png";
        string normalPath = textures + "/wall_2_Wood_Seamless_Normal.png";
        foreach (string path in new[] { basePath, normalPath })
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (!importer) throw new InvalidOperationException("Missing revised wall texture: " + path);
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 8;
            importer.maxTextureSize = 2048;
            importer.mipmapEnabled = true;
            importer.textureType = path == normalPath ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = path != normalPath;
            if (path == normalPath) { importer.convertToNormalmap = true; importer.heightmapScale = 0.007f; }
            importer.SaveAndReimport();
        }
        string materialPath = MaterialFolder + "/wall_2_Wood_Seamless.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (!material)
        {
            material = new Material(AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/wood.mat"));
            material.name = "wall_2_Wood_Seamless";
            AssetDatabase.CreateAsset(material, materialPath);
        }
        Undo.RecordObject(material, "Apply seamless wall wood");
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(basePath));
        material.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(basePath));
        material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath));
        foreach (string property in new[] { "_BaseMap", "_MainTex", "_BumpMap" }) material.SetTextureScale(property, new Vector2(2, 2));
        Validate(material);
        EditorUtility.SetDirty(material);
        var renderer = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MeshRenderer>(true)).Single(r => r.name == "wall_2");
        Undo.RecordObject(renderer, "Set wall_2 dedicated wood material");
        renderer.sharedMaterial = material;
        PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
        EditorUtility.SetDirty(renderer);
        File.AppendAllText(LightingReport, "WALL TEXTURE | revised albedo and matching height-derived normal | tiling=2x2 | " + materialPath + "\n");
    }

    static void RepairChairs(Scene scene)
    {
        string models = MaterialFolder.Replace("/Materials", "");
        string fixedPath = models + "/Chair_Smooth.fbx";
        AssetDatabase.ImportAsset(fixedPath, ImportAssetOptions.ForceSynchronousImport);
        var importer = (ModelImporter)AssetImporter.GetAtPath(fixedPath);
        importer.generateSecondaryUV = false;
        importer.swapUVChannels = false;
        importer.secondaryUVPackMargin = 8;
        importer.isReadable = true;
        importer.importAnimation = false;
        importer.importNormals = ModelImporterNormals.Import;
        importer.importTangents = ModelImporterTangents.CalculateMikk;
        importer.SaveAndReimport();
        var originalFilter = AssetDatabase.LoadAssetAtPath<GameObject>(models + "/Chair.FBX").GetComponentInChildren<MeshFilter>(true);
        var fixedFilter = AssetDatabase.LoadAssetAtPath<GameObject>(fixedPath).GetComponentInChildren<MeshFilter>(true);
        Matrix4x4 conversion = originalFilter.transform.localToWorldMatrix.inverse * fixedFilter.transform.localToWorldMatrix;
        Mesh mesh = UnityEngine.Object.Instantiate(fixedFilter.sharedMesh);
        mesh.name = "Chair_Smooth";
        mesh.vertices = mesh.vertices.Select(conversion.MultiplyPoint3x4).ToArray();
        Matrix4x4 normalConversion = conversion.inverse.transpose;
        mesh.normals = mesh.normals.Select(normal => normalConversion.MultiplyVector(normal).normalized).ToArray();
        mesh.tangents = mesh.tangents.Select(tangent =>
        {
            Vector3 direction = conversion.MultiplyVector(new Vector3(tangent.x, tangent.y, tangent.z)).normalized;
            return new Vector4(direction.x, direction.y, direction.z, tangent.w * (conversion.determinant < 0 ? -1 : 1));
        }).ToArray();
        if (conversion.determinant < 0)
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                int[] triangles = mesh.GetTriangles(i);
                for (int j = 0; j < triangles.Length; j += 3) { int temp = triangles[j + 1]; triangles[j + 1] = triangles[j + 2]; triangles[j + 2] = temp; }
                mesh.SetTriangles(triangles, i);
            }
        mesh.RecalculateBounds();
        Bounds originalBounds = originalFilter.sharedMesh.bounds;
        if (Vector3.Distance(mesh.bounds.size, originalBounds.size) > 0.005f || Vector3.Distance(mesh.bounds.center, originalBounds.center) > 0.005f)
            throw new InvalidOperationException($"Repaired chair dimensions changed: original={originalBounds}, revised={mesh.bounds}, conversion={conversion}");
        if (!mesh.HasVertexAttribute(VertexAttribute.TexCoord1)) throw new InvalidOperationException("Repaired chair requires non-overlapping lightmap UVs.");
        string meshPath = models + "/Chair_Smooth_Baked.asset";
        Mesh saved = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        if (saved) { EditorUtility.CopySerialized(mesh, saved); EditorUtility.SetDirty(saved); UnityEngine.Object.DestroyImmediate(mesh); }
        else { AssetDatabase.CreateAsset(mesh, meshPath); saved = mesh; }
        int replaced = 0;
        foreach (var filter in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MeshFilter>(true)).Where(filter => filter.sharedMesh == originalFilter.sharedMesh || filter.name.StartsWith("Chair_2")))
        {
            Undo.RecordObject(filter, "Replace repaired ConferenceHall chair mesh");
            filter.sharedMesh = saved;
            var renderer = filter.GetComponent<MeshRenderer>();
            Undo.RecordObject(renderer, "Increase chair bake detail");
            renderer.scaleInLightmap = 2;
            renderer.stitchLightmapSeams = true;
            PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            EditorUtility.SetDirty(filter);
            EditorUtility.SetDirty(renderer);
            replaced++;
        }
        File.AppendAllText(LightingReport, $"CHAIRS | Blender repaired normals | instances={replaced} | bounds preserved={saved.bounds} | vertices={saved.vertexCount} | lightmap UV2=True\n");
        ApplyChairProbeLighting(scene);
    }
}
