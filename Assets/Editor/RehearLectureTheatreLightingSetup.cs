using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
internal static class RehearLectureTheatreLightingSetup
{
    const string ScenePath = "Assets/01_Scene/UniversityLectureTheatre.unity";
    const string RequestPath = "Temp/RehearLectureTheatreLighting-v11.request";
    const string ReportPath = "Temp/RehearLectureTheatreLighting.txt";
    const string PendingKey = "Rehear.LectureTheatre.Baking";
    [Serializable] class PointSpec { public string name, path; public Color color; public float intensity, range, angle, innerAngle, shadowRadius; public int projection; public string cookieGuid; }
    [Serializable] class RigSpec { public PointSpec[] points; }
    static double nextProgress;
    static bool finished;

    static RehearLectureTheatreLightingSetup()
    {
        File.WriteAllText("Temp/RehearLectureTheatreLighting.ready", "v11");
        EditorApplication.update += Poll;
        Lightmapping.bakeCompleted += () => { if (SessionState.GetBool(PendingKey, false)) finished = true; };
    }

    [MenuItem("Rehear/Lecture Theatre/Remove Wall Lettering And Rebake")]
    static void RequestApply() => File.WriteAllText(RequestPath, "apply");

    static string Hierarchy(Transform transform) => transform.parent ? Hierarchy(transform.parent) + "/" + transform.name : transform.name;
    static Transform[] All(Scene scene) => scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToArray();
    static Scene RequireScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath || SceneManager.sceneCount != 1)
            throw new InvalidOperationException("Keep only UniversityLectureTheatre open for this bake.");
        return scene;
    }

    static void Poll()
    {
        if (AssetDatabase.IsAssetImportWorkerProcess() || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        try
        {
            if (SessionState.GetBool(PendingKey, false))
            {
                if (finished) { finished = false; Complete(); }
                else if (EditorApplication.timeSinceStartup >= nextProgress)
                {
                    nextProgress = EditorApplication.timeSinceStartup + 20;
                    File.WriteAllText("Temp/RehearLectureTheatreLighting-progress.txt", $"running={Lightmapping.isRunning}\nprogress={Lightmapping.buildProgress:P1}\nutc={DateTime.UtcNow:O}\n");
                }
            }
            if (Lightmapping.isRunning || !File.Exists(RequestPath)) return;
            string command = File.ReadAllText(RequestPath).Trim();
            File.Delete(RequestPath);
            if (command == "apply") Apply();
            else if (command == "audit") Audit();
            else if (command == "brighten") Brighten();
            else if (command == "smooth") Smooth();
            else if (command == "inspect") InspectScreen();
            else if (command == "ceiling-audit") InspectCeiling();
            else if (command == "ceiling-fix") FixCeilingBoundary();
            else throw new ArgumentException("Unknown command: " + command);
        }
        catch (Exception exception)
        {
            SessionState.SetBool(PendingKey, false);
            File.AppendAllText(ReportPath, "\nFAILED\n" + exception);
            Debug.LogException(exception);
        }
    }

    [Serializable] class ViewPose { public Vector3 position; public Quaternion rotation; public float fov, size; public bool orthographic; }
    static void Capture(string path, bool reuse)
    {
        Camera source = SceneView.lastActiveSceneView ? SceneView.lastActiveSceneView.camera : null;
        if (!source) return;
        ViewPose pose;
        if (reuse && !string.IsNullOrEmpty(SessionState.GetString("Rehear.LectureTheatre.View", "")))
            pose = JsonUtility.FromJson<ViewPose>(SessionState.GetString("Rehear.LectureTheatre.View", ""));
        else
        {
            pose = new ViewPose { position = source.transform.position, rotation = source.transform.rotation, fov = source.fieldOfView, size = source.orthographicSize, orthographic = source.orthographic };
            SessionState.SetString("Rehear.LectureTheatre.View", JsonUtility.ToJson(pose));
        }
        var obj = new GameObject("Lecture Theatre Verification Camera") { hideFlags = HideFlags.HideAndDontSave };
        var camera = obj.AddComponent<Camera>();
        RenderTexture target = RenderTexture.GetTemporary(1600, 900, 24, RenderTextureFormat.ARGB32);
        RenderTexture previous = RenderTexture.active;
        Texture2D image = null;
        try
        {
            camera.CopyFrom(source);
            camera.enabled = false;
            camera.transform.SetPositionAndRotation(pose.position, pose.rotation);
            camera.fieldOfView = pose.fov;
            camera.orthographicSize = pose.size;
            camera.orthographic = pose.orthographic;
            camera.aspect = 1600f / 900;
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            image = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
            image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            camera.targetTexture = null;
            if (image) Object.DestroyImmediate(image);
            Object.DestroyImmediate(obj);
            RenderTexture.ReleaseTemporary(target);
        }
    }
    static void Audit()
    {
        Scene scene = RequireScene();
        var report = new StringBuilder();
        foreach (Transform transform in All(scene))
        {
            Light light = transform.GetComponent<Light>();
            if (light) report.AppendLine($"LIGHT | {light.name} | {light.type} | {light.lightmapBakeType} | intensity={light.intensity} | color={light.color} | area={light.areaSize} | pos={transform.position}");
            MeshFilter filter = transform.GetComponent<MeshFilter>();
            if (filter && filter.sharedMesh && !filter.sharedMesh.HasVertexAttribute(VertexAttribute.TexCoord1))
                report.AppendLine("MISSING UV2 | " + transform.name + " | " + AssetDatabase.GetAssetPath(filter.sharedMesh));
            int missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject);
            if (missing > 0) report.AppendLine($"MISSING SCRIPT | {transform.name} | count={missing}");
            if (transform.name == "ULT2_Fonts") report.AppendLine("LETTERING | " + transform.position);
        }
        report.AppendLine("LIGHTMAPS | " + LightmapSettings.lightmaps.Length);
        File.WriteAllText(ReportPath, report.ToString());
        Capture("Temp/UniversityLectureTheatre-before.png", false);
    }

    static void Apply()
    {
        Scene scene = RequireScene();
        Transform[] all = All(scene);
        Transform lettering = all.SingleOrDefault(transform => transform.name == "ULT2_Fonts");
        var spec = JsonUtility.FromJson<RigSpec>(File.ReadAllText("Temp/RehearLectureTheatreLighting-source.json"));
        if (spec == null || spec.points == null || spec.points.Length != 42)
            throw new InvalidOperationException("The original 42 spotlight definitions are required.");
        foreach (PointSpec point in spec.points)
            if (all.Count(transform => Hierarchy(transform) == point.path) != 1)
                throw new InvalidOperationException("Spotlight not uniquely found: " + point.name);
        if (!Directory.Exists("Temp/RehearLectureTheatreLightingBackup")) Directory.CreateDirectory("Temp/RehearLectureTheatreLightingBackup");
        EditorSceneManager.SaveScene(scene, "Temp/RehearLectureTheatreLightingBackup/Before.unity", true);
        File.WriteAllText(ReportPath, "BEGIN | " + DateTime.UtcNow.ToString("O") + "\n");
        if (lettering)
        {
            Renderer[] letters = lettering.GetComponentsInChildren<Renderer>(true);
            if (letters.Length > 0)
            {
                Bounds bounds = letters[0].bounds;
                foreach (Renderer renderer in letters.Skip(1)) bounds.Encapsulate(renderer.bounds);
                Vector3 target = bounds.center;
                float distance = Mathf.Max(10, bounds.extents.x / Mathf.Tan(30 * Mathf.Deg2Rad) / (1600f / 900) + 2);
                Vector3 position = target + Vector3.forward * distance + Vector3.down * 1.2f;
                SessionState.SetString("Rehear.LectureTheatre.View", JsonUtility.ToJson(new ViewPose { position = position, rotation = Quaternion.LookRotation(target - position), fov = 60 }));
                Capture("Temp/UniversityLectureTheatre-wall-before.png", true);
            }
            Undo.DestroyObjectImmediate(lettering.gameObject);
            File.AppendAllText(ReportPath, "REMOVED | ULT2_Fonts\n");
        }
        foreach (ftLightmapsStorage storage in all.Where(transform => transform).Select(transform => transform.GetComponent<ftLightmapsStorage>()).Where(storage => storage))
        {
            Undo.DestroyObjectImmediate(storage);
            File.AppendAllText(ReportPath, "REMOVED | Bakery lightmap restore component\n");
        }
        // Bakery's cached mode otherwise keeps overriding the native lightmapper after hierarchy changes.
        typeof(ftLightmaps).GetField("directionalMode", BindingFlags.NonPublic | BindingFlags.Static)?.SetValue(null, -1);
        int nativeAreaLights = 0;
        foreach (Light light in All(scene).Select(transform => transform.GetComponent<Light>()).Where(light => light))
        {
            Undo.RecordObject(light, "Prepare lecture theatre native light bake");
            light.lightmapBakeType = LightmapBakeType.Baked;
            if (light.type == LightType.Rectangle) nativeAreaLights++;
            EditorUtility.SetDirty(light);
            PrefabUtility.RecordPrefabInstancePropertyModifications(light);
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(light.gameObject);
        }
        foreach (PointSpec point in spec.points)
        {
            GameObject obj = All(scene).Single(transform => Hierarchy(transform) == point.path).gameObject;
            Light light = obj.GetComponent<Light>();
            if (!light) light = Undo.AddComponent<Light>(obj);
            Undo.RecordObject(light, "Restore original lecture theatre spotlight");
            light.type = LightType.Spot;
            light.color = point.color;
            light.intensity = point.intensity;
            light.range = point.range;
            light.spotAngle = point.angle;
            light.innerSpotAngle = point.innerAngle;
            light.shadows = LightShadows.Soft;
            light.shadowRadius = point.shadowRadius;
            light.lightmapBakeType = LightmapBakeType.Baked;
            light.bounceIntensity = 1;
            // The packaged IES lights have no executable Bakery renderer; use their preserved native spot cone.
            EditorUtility.SetDirty(light);
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(obj);
        }
        var uvModels = new HashSet<string>();
        foreach (MeshFilter filter in All(scene).Select(transform => transform.GetComponent<MeshFilter>()).Where(filter => filter && filter.sharedMesh && filter.gameObject.activeInHierarchy))
            if (!filter.sharedMesh.HasVertexAttribute(VertexAttribute.TexCoord1)) uvModels.Add(AssetDatabase.GetAssetPath(filter.sharedMesh));
        foreach (string path in uvModels)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (!importer) throw new InvalidOperationException("Cannot generate lightmap UVs: " + path);
            importer.generateSecondaryUV = true;
            importer.SaveAndReimport();
            File.AppendAllText(ReportPath, "GENERATED UV2 | " + path + "\n");
        }
        if (!Lightmapping.TryGetLightingSettings(out var settings)) throw new InvalidOperationException("Scene lighting settings missing.");
        settings.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
        settings.bakedGI = true;
        settings.realtimeGI = false;
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssetIfDirty(settings);
        Lightmapping.Clear();
        Lightmapping.lightingDataAsset = null;
        LightmapSettings.lightmapsMode = settings.directionalityMode;
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Scene save failed.");
        File.AppendAllText(ReportPath, $"RIG | bakedAreas={nativeAreaLights} | restoredSpots={spec.points.Length}\nSETTINGS | resolution={settings.lightmapResolution} | atlas={settings.lightmapMaxSize} | indirectSamples={settings.indirectSampleCount}\n");
        SessionState.SetBool(PendingKey, true);
        nextProgress = 0;
        if (!Lightmapping.BakeAsync()) { SessionState.SetBool(PendingKey, false); throw new InvalidOperationException("Bake did not start."); }
        File.AppendAllText(ReportPath, "BAKE STARTED\n");
    }

    [MenuItem("Rehear/Lecture Theatre/Fix Light Directions And Brighten")]
    static void RequestBrighten() => File.WriteAllText(RequestPath, "brighten");

    static void CaptureViews(Scene scene, string label)
    {
        string stored = SessionState.GetString("Rehear.LectureTheatre.View", "");
        try
        {
            Capture("Temp/UniversityLectureTheatre-" + label + "-wall.png", true);
            Vector3 position = new Vector3(0, 4.5f, -2.5f);
            SessionState.SetString("Rehear.LectureTheatre.View", JsonUtility.ToJson(new ViewPose { position = position, rotation = Quaternion.LookRotation(new Vector3(0, 4.8f, 13) - position), fov = 75 }));
            Capture("Temp/UniversityLectureTheatre-" + label + "-seats.png", true);
            foreach (int side in new[] { -1, 1 })
            {
                position = new Vector3(side < 0 ? 3 : -3, 4, 5);
                Vector3 sideTarget = new Vector3(side < 0 ? -8 : 10, 7, 9);
                SessionState.SetString("Rehear.LectureTheatre.View", JsonUtility.ToJson(new ViewPose { position = position, rotation = Quaternion.LookRotation(sideTarget - position), fov = 75 }));
                Capture("Temp/UniversityLectureTheatre-" + label + (side < 0 ? "-left.png" : "-right.png"), true);
            }
            Renderer ceiling = All(scene).Select(transform => transform.GetComponent<Renderer>()).FirstOrDefault(renderer => renderer && renderer.name.StartsWith("ULT2_Ceiling"));
            if (ceiling)
            {
                Vector3 target = ceiling.bounds.center;
                position = new Vector3(target.x, target.y - 5.5f, target.z - 4);
                SessionState.SetString("Rehear.LectureTheatre.View", JsonUtility.ToJson(new ViewPose { position = position, rotation = Quaternion.LookRotation(target - position), fov = 90 }));
                Capture("Temp/UniversityLectureTheatre-" + label + "-ceiling.png", true);
            }
        }
        finally { SessionState.SetString("Rehear.LectureTheatre.View", stored); }
    }

    static void Brighten()
    {
        Scene scene = RequireScene();
        Light[] lights = All(scene).Select(transform => transform.GetComponent<Light>()).Where(light => light).ToArray();
        Light[] spots = lights.Where(light => light.type == LightType.Spot && light.name.StartsWith("ULT2_")).ToArray();
        if (spots.Length != 42) throw new InvalidOperationException("Expected exactly 42 lecture theatre spotlights.");
        EditorSceneManager.SaveScene(scene, "Temp/RehearLectureTheatreLightingBackup/BeforeBrightening.unity", true);
        CaptureViews(scene, "before-brightening");
        File.WriteAllText(ReportPath, "BRIGHTEN BEGIN | " + DateTime.UtcNow.ToString("O") + "\n");
        var warmWhite = new Color(1, 0.94f, 0.86f, 1);
        foreach (Light light in spots)
        {
            Undo.RecordObjects(new Object[] { light, light.transform }, "Correct lecture theatre downlight");
            light.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            light.spotAngle = 95;
            light.innerSpotAngle = 60;
            light.range = 16;
            light.intensity = light.name.StartsWith("ULT2_Spot_Light_") ? 18 : light.name.Contains("Middle") ? 20 : 14;
            light.color = warmWhite;
            light.cookie = null;
            light.shadowRadius = 0.15f;
            light.lightmapBakeType = LightmapBakeType.Baked;
            EditorUtility.SetDirty(light);
            EditorUtility.SetDirty(light.transform);
        }
        foreach (Light light in lights.Where(light => light.type == LightType.Rectangle))
        {
            Undo.RecordObject(light, "Brighten lecture theatre area lights");
            if (light.name.StartsWith("Area_Feel_Light_")) light.intensity = 32;
            else if (light.name.StartsWith("Corridor_Feel_Light_")) light.intensity = 25;
            else if (light.name == "Stage_Feel_Light") light.intensity = 25;
            else if (light.name == "Stage_Bounce_Light") light.intensity = 8;
            else continue;
            light.color = warmWhite;
            light.lightmapBakeType = LightmapBakeType.Baked;
            EditorUtility.SetDirty(light);
        }
        Transform rig = All(scene).Single(transform => transform.name == "Light_System");
        foreach (int side in new[] { -1, 1 })
        {
            string name = side < 0 ? "Ceiling Diffuse Fill Left" : "Ceiling Diffuse Fill Right";
            Transform existing = rig.Find(name);
            GameObject obj = existing ? existing.gameObject : new GameObject(name);
            if (!existing) { Undo.RegisterCreatedObjectUndo(obj, "Add diffuse ceiling fill"); obj.transform.SetParent(rig, true); }
            Light light = obj.GetComponent<Light>();
            if (!light) light = Undo.AddComponent<Light>(obj);
            Undo.RecordObjects(new Object[] { light, obj.transform }, "Set diffuse ceiling fill");
            obj.transform.SetPositionAndRotation(new Vector3(side * 5, 7, 10), Quaternion.LookRotation(Vector3.up, Vector3.forward));
            light.type = LightType.Rectangle;
            light.areaSize = new Vector2(9, 18);
            light.intensity = 6;
            light.color = warmWhite;
            light.shadows = LightShadows.Soft;
            light.lightmapBakeType = LightmapBakeType.Baked;
            light.bounceIntensity = 1;
            EditorUtility.SetDirty(light);
        }
        if (!Lightmapping.TryGetLightingSettings(out var settings)) throw new InvalidOperationException("Lighting settings missing.");
        Undo.RecordObject(settings, "Improve lecture theatre bounce lighting");
        settings.maxBounces = 4;
        settings.indirectScale = 1.4f;
        settings.directSampleCount = 64;
        settings.aoMaxDistance = 0.25f;
        settings.aoExponentIndirect = 0.6f;
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssetIfDirty(settings);
        Lightmapping.Clear();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        File.AppendAllText(ReportPath, "DIRECTION | 42 spots point down\nAREA | existing fill lights brightened; 2 broad ceiling fills added\nGI | 4 bounces; indirect scale 1.4\n");
        SessionState.SetBool("Rehear.LectureTheatre.Brightening", true);
        SessionState.SetBool(PendingKey, true);
        nextProgress = 0;
        if (!Lightmapping.BakeAsync()) { SessionState.SetBool(PendingKey, false); throw new InvalidOperationException("Bake did not start."); }
        File.AppendAllText(ReportPath, "BAKE STARTED\n");
    }

    [MenuItem("Rehear/Lecture Theatre/Remove Overlapping Light Patterns")]
    static void RequestSmooth() => File.WriteAllText(RequestPath, "smooth");

    static void Smooth()
    {
        Scene scene = RequireScene();
        Transform rig = All(scene).Single(transform => transform.name == "Light_System");
        Light[] fixtures = rig.GetComponentsInChildren<Light>(true).Where(light => light.name.StartsWith("ULT2_")).ToArray();
        if (fixtures.Length != 42) throw new InvalidOperationException("Expected 42 ceiling fixture lights.");
        EditorSceneManager.SaveScene(scene, "Temp/RehearLectureTheatreLightingBackup/BeforeSmoothing.unity", true);
        CaptureViews(scene, "before-smoothing");
        File.WriteAllText(ReportPath, "SMOOTH BEGIN | " + DateTime.UtcNow.ToString("O") + "\n");
        foreach (string name in new[] { "Ceiling Diffuse Fill Left", "Ceiling Diffuse Fill Right" })
        {
            Transform artificialFill = rig.Find(name);
            if (artificialFill) Undo.DestroyObjectImmediate(artificialFill.gameObject);
        }
        File.AppendAllText(ReportPath, "REMOVED | 2 upward ceiling fills causing bright bands\n");
        FixSurfaceMaterials();
        foreach (Light light in fixtures)
        {
            Undo.RecordObjects(new Object[] { light, light.transform }, "Remove overlapping spotlight cones");
            light.enabled = false;
            light.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            light.type = LightType.Rectangle;
            light.areaSize = Vector2.one;
            light.intensity = light.name.StartsWith("ULT2_Spot_Light_") ? 6 : light.name.Contains("Middle") ? 12 : 8;
            light.color = new Color(1, 0.94f, 0.86f, 1);
            light.cookie = null;
            light.lightmapBakeType = LightmapBakeType.Baked;
            light.shadows = LightShadows.Soft;
            light.bounceIntensity = 1;
            EditorUtility.SetDirty(light);
            EditorUtility.SetDirty(light.transform);
            File.AppendAllText(ReportPath, $"DISABLED LOCAL LIGHT | {light.name}\n");
        }
        Light[] roomFills = rig.GetComponentsInChildren<Light>(true).Where(light => light.name.StartsWith("Area_Feel_Light_")).ToArray();
        if (roomFills.Length != 4) throw new InvalidOperationException("Expected four room fill strips.");
        foreach (Light light in roomFills)
        {
            Undo.RecordObjects(new Object[] { light, light.transform }, "Distribute room illumination below ceiling");
            Vector3 position = light.transform.position;
            position.y = 9.1f;
            position.z = 7;
            light.transform.SetPositionAndRotation(position, Quaternion.LookRotation(Vector3.down, Vector3.forward));
            light.enabled = true;
            light.type = LightType.Rectangle;
            light.areaSize = new Vector2(3.5f, 18);
            light.range = 40;
            light.intensity = 9;
            light.color = new Color(1, 0.94f, 0.86f, 1);
            light.lightmapBakeType = LightmapBakeType.Baked;
            EditorUtility.SetDirty(light);
            EditorUtility.SetDirty(light.transform);
            File.AppendAllText(ReportPath, $"ROOM FILL | {light.name} | position={position} | area={light.areaSize} | intensity={light.intensity}\n");
        }
        foreach (Light light in rig.GetComponentsInChildren<Light>(true).Where(light => light.name == "Stage_Feel_Light" || light.name == "Stage_Bounce_Light"))
        {
            Undo.RecordObject(light, "Remove stage light hotspots");
            light.enabled = false;
            EditorUtility.SetDirty(light);
        }
        Vector3 frontPosition = new Vector3(0.9f, 4.5f, 10);
        SessionState.SetString("Rehear.LectureTheatre.View", JsonUtility.ToJson(new ViewPose { position = frontPosition, rotation = Quaternion.LookRotation(new Vector3(0.9f, 4.4f, -4.8f) - frontPosition), fov = 60 }));
        if (!Lightmapping.TryGetLightingSettings(out var settings)) throw new InvalidOperationException("Lighting settings missing.");
        Undo.RecordObject(settings, "Sample diffuse ceiling lights");
        settings.directSampleCount = 256;
        settings.indirectScale = 1.8f;
        settings.aoMaxDistance = 0.1f;
        settings.aoExponentIndirect = 0.3f;
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssetIfDirty(settings);
        Lightmapping.Clear();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        SessionState.SetBool("Rehear.LectureTheatre.Smoothing", true);
        SessionState.SetBool(PendingKey, true);
        nextProgress = 0;
        if (!Lightmapping.BakeAsync()) { SessionState.SetBool(PendingKey, false); throw new InvalidOperationException("Bake did not start."); }
        File.AppendAllText(ReportPath, "BAKE STARTED\n");
    }

    static void FixSurfaceMaterials()
    {
        const string folder = "Assets/UniversityLectureTheatre/";
        foreach (string textureName in new[] { "ULT2_Wall", "ULT2_Wall_Concrete" })
        {
            string path = folder + "Textures/" + textureName + "_URP_MetallicSmoothness.png";
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (!importer) throw new InvalidOperationException("Packed roughness texture missing: " + path);
            importer.sRGBTexture = false;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;
            importer.SaveAndReimport();
        }
        foreach (string name in new[] { "ULT2_Ceiling", "ULT2_Wall", "ULT2_Wall_Concrete", "ULT2_Display_Board_White" })
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(folder + "Mesh/Materials/" + name + ".mat");
            if (!material) throw new InvalidOperationException("Surface material missing: " + name);
            Undo.RecordObject(material, "Correct nonmetal lecture theatre surfaces");
            material.SetFloat("_Metallic", 0);
            material.SetFloat("_SmoothnessTextureChannel", 0);
            if (name == "ULT2_Display_Board_White")
            {
                material.SetTexture("_MetallicGlossMap", null);
                material.DisableKeyword("_METALLICSPECGLOSSMAP");
                material.SetFloat("_Smoothness", 0.08f);
                material.SetColor("_BaseColor", new Color(0.8f, 0.8f, 0.8f, 1));
            }
            else
            {
                string textureName = name == "ULT2_Wall_Concrete" ? name : "ULT2_Wall";
                material.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(folder + "Textures/" + textureName + "_URP_MetallicSmoothness.png"));
                material.SetFloat("_Smoothness", 1);
                material.EnableKeyword("_METALLICSPECGLOSSMAP");
            }
            EditorUtility.SetDirty(material);
            File.AppendAllText(ReportPath, "NONMETAL SURFACE | " + name + "\n");
        }
        AssetDatabase.SaveAssets();
    }

    static void InspectScreen()
    {
        Scene scene = RequireScene();
        Vector3 position = new Vector3(1.5f, 4.5f, 10);
        string stored = SessionState.GetString("Rehear.LectureTheatre.View", "");
        SessionState.SetString("Rehear.LectureTheatre.View", JsonUtility.ToJson(new ViewPose { position = position, rotation = Quaternion.LookRotation(new Vector3(1.5f, 4.5f, -6) - position), fov = 60 }));
        Material[] materials = All(scene).SelectMany(t => t.GetComponents<Renderer>()).SelectMany(r => r.sharedMaterials).Where(m => m && m.HasProperty("_EnvironmentReflections")).Distinct().ToArray();
        float[] reflection = materials.Select(m => m.GetFloat("_EnvironmentReflections")).ToArray();
        float[] specular = materials.Select(m => m.GetFloat("_SpecularHighlights")).ToArray();
        try
        {
            Capture("Temp/UniversityLectureTheatre-inspect-normal.png", true);
            for (int i = 0; i < materials.Length; i++)
            {
                materials[i].SetFloat("_EnvironmentReflections", 0);
                materials[i].SetFloat("_SpecularHighlights", 0);
                materials[i].EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
                materials[i].EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            }
            Capture("Temp/UniversityLectureTheatre-inspect-diffuse.png", true);
            File.WriteAllText("Temp/UniversityLectureTheatre-screen-audit.txt", string.Join("\n", All(scene).Select(t => t.GetComponent<Renderer>()).Where(r => r && (r.name.Contains("Board") || r.name.Contains("Wall"))).Select(r => $"{Hierarchy(r.transform)} | bounds={r.bounds} | lightmap={r.lightmapIndex} | materials={string.Join(",", r.sharedMaterials.Where(m => m).Select(m => m.name))}")));
        }
        finally
        {
            for (int i = 0; i < materials.Length; i++)
            {
                materials[i].SetFloat("_EnvironmentReflections", reflection[i]);
                materials[i].SetFloat("_SpecularHighlights", specular[i]);
                if (reflection[i] != 0) materials[i].DisableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
                if (specular[i] != 0) materials[i].DisableKeyword("_SPECULARHIGHLIGHTS_OFF");
            }
            SessionState.SetString("Rehear.LectureTheatre.View", stored);
        }
    }

    [Serializable] class CeilingMeshData
    {
        public string name, material;
        public Vector3[] vertices, normals;
        public Vector2[] uv, uv2;
        public int[] triangles;
        public int lightmap;
        public Vector4 lightmapScaleOffset;
    }

    static void InspectCeiling()
    {
        Scene scene = RequireScene();
        string stored = SessionState.GetString("Rehear.LectureTheatre.View", "");
        Vector3 position = new Vector3(0.9f, 5, 7);
        SessionState.SetString("Rehear.LectureTheatre.View", JsonUtility.ToJson(new ViewPose { position = position, rotation = Quaternion.LookRotation(new Vector3(0.9f, 9.32f, 0) - position), fov = 80 }));
        Capture("Temp/UniversityLectureTheatre-ceiling-line-before.png", true);
        SessionState.SetString("Rehear.LectureTheatre.CeilingView", SessionState.GetString("Rehear.LectureTheatre.View", ""));
        SessionState.SetString("Rehear.LectureTheatre.View", stored);
        var report = new StringBuilder();
        foreach (MeshFilter filter in All(scene).Select(t => t.GetComponent<MeshFilter>()).Where(f => f && f.sharedMesh && (f.name.Contains("Ceiling") || f.name.Contains("Spot_Light_Beam"))))
        {
            Mesh mesh = filter.sharedMesh;
            Renderer renderer = filter.GetComponent<Renderer>();
            report.AppendLine($"{Hierarchy(filter.transform)} | mesh={mesh.name} | bounds={renderer.bounds} | lightmap={renderer.lightmapIndex} | material={string.Join(",", renderer.sharedMaterials.Where(m => m).Select(m => m.name))}");
            var data = new CeilingMeshData { name = Hierarchy(filter.transform), material = string.Join(",", renderer.sharedMaterials.Where(m => m).Select(m => m.name)), vertices = mesh.vertices.Select(v => filter.transform.TransformPoint(v)).ToArray(), normals = mesh.normals.Select(n => filter.transform.TransformDirection(n).normalized).ToArray(), uv = mesh.uv, uv2 = mesh.uv2, triangles = mesh.triangles, lightmap = renderer.lightmapIndex, lightmapScaleOffset = renderer.lightmapScaleOffset };
            File.WriteAllText("Temp/lecture-mesh-" + filter.name.Replace("/", "_") + ".json", JsonUtility.ToJson(data));
        }
        File.WriteAllText("Temp/UniversityLectureTheatre-ceiling-audit.txt", report.ToString());
    }

    static void FixCeilingBoundary()
    {
        Scene scene = RequireScene();
        Transform rail = All(scene).Single(t => t.name == "ULT2_Spot_Light_Beam");
        EditorSceneManager.SaveScene(scene, "Temp/RehearLectureTheatreLightingBackup/BeforeCeilingBoundary.unity", true);
        File.WriteAllText(ReportPath, "CEILING BOUNDARY BEGIN | " + DateTime.UtcNow.ToString("O") + "\n");
        foreach (Renderer renderer in rail.GetComponentsInChildren<Renderer>(true))
        {
            Undo.RecordObject(renderer, "Remove rail lightmap occlusion at ceiling");
            Undo.RecordObject(renderer.gameObject, "Remove rail lightmap occlusion at ceiling");
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            GameObjectUtility.SetStaticEditorFlags(renderer.gameObject, GameObjectUtility.GetStaticEditorFlags(renderer.gameObject) & ~StaticEditorFlags.ContributeGI);
            renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
            EditorUtility.SetDirty(renderer);
            File.AppendAllText(ReportPath, "EXCLUDE RAIL FROM BAKE | " + Hierarchy(renderer.transform) + "\n");
        }
        Lightmapping.Clear();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        SessionState.SetBool("Rehear.LectureTheatre.CeilingBoundary", true);
        SessionState.SetBool(PendingKey, true);
        nextProgress = 0;
        if (!Lightmapping.BakeAsync()) { SessionState.SetBool(PendingKey, false); throw new InvalidOperationException("Bake did not start."); }
        File.AppendAllText(ReportPath, "BAKE STARTED\n");
    }

    static void Complete()
    {
        SessionState.SetBool(PendingKey, false);
        Scene scene = RequireScene();
        if (!Lightmapping.lightingDataAsset || LightmapSettings.lightmaps.Length == 0)
            throw new InvalidOperationException("Bake ended without a lighting data asset and lightmaps.");
        if (All(scene).Any(transform => transform.name == "ULT2_Fonts"))
            throw new InvalidOperationException("Wall lettering is still present.");
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        File.AppendAllText(ReportPath, $"BAKE COMPLETED | {DateTime.UtcNow:O}\nLIGHTMAPS | {LightmapSettings.lightmaps.Length}\nLIGHTING DATA | {AssetDatabase.GetAssetPath(Lightmapping.lightingDataAsset)}\nPASS\n");
        Capture("Temp/UniversityLectureTheatre-after.png", true);
        if (SessionState.GetBool("Rehear.LectureTheatre.Brightening", false)) { CaptureViews(scene, "brighter"); SessionState.SetBool("Rehear.LectureTheatre.Brightening", false); }
        if (SessionState.GetBool("Rehear.LectureTheatre.Smoothing", false)) { CaptureViews(scene, "smooth"); SessionState.SetBool("Rehear.LectureTheatre.Smoothing", false); }
        if (SessionState.GetBool("Rehear.LectureTheatre.CeilingBoundary", false))
        {
            string stored = SessionState.GetString("Rehear.LectureTheatre.View", "");
            SessionState.SetString("Rehear.LectureTheatre.View", SessionState.GetString("Rehear.LectureTheatre.CeilingView", stored));
            Capture("Temp/UniversityLectureTheatre-ceiling-line-after.png", true);
            SessionState.SetString("Rehear.LectureTheatre.View", stored);
            CaptureViews(scene, "ceiling-fixed");
            SessionState.SetBool("Rehear.LectureTheatre.CeilingBoundary", false);
        }
        File.WriteAllText("Temp/RehearLectureTheatreLighting-progress.txt", "running=False\nprogress=100%\n");
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        Debug.Log("Lecture theatre lettering removed and lighting rebaked: " + ReportPath);
    }
}
