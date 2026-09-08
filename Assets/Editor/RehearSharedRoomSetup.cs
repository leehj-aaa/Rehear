using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UObject = UnityEngine.Object;

[InitializeOnLoad]
internal static class RehearSharedRoomSetup
{
    static readonly string[] Scenes = { "Assets/01_Scene/Scene_00_5_Tutorial.unity", "Assets/01_Scene/Scene_02_Presentation.unity", "Assets/01_Scene/Scene_03_Feedback.unity" };
    const string PrefabFolder = "Assets/03_Prefabs/SharedRoom";
    static bool bakeFinished;
    static RehearSharedRoomSetup() { EditorApplication.update += Poll; Lightmapping.bakeCompleted += () => bakeFinished = true; }
    static void Poll()
    {
        const string request = "Temp/RehearSharedRoom.request";
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning) return;
        if (bakeFinished && SessionState.GetInt("Rehear.RoomBake", -1) >= 0)
        {
            bakeFinished = false;
            try { FinishBake(); } catch (Exception e) { SessionState.SetInt("Rehear.RoomBake", -1); File.AppendAllText("Temp/RehearSharedRoom-apply.txt", "\nBAKE FAIL\n"+e); }
        }
        if (!File.Exists(request)) return;
        string command = File.ReadAllText(request).Trim(); File.Delete(request);
        try { if(command=="verify") VerifySaved(); else if(command=="retry") { var partial=SceneManager.GetSceneByPath(Scenes[1]); if(partial.isLoaded) EditorSceneManager.CloseScene(partial,true); Apply(true); } else if (command == "apply" || command == "resume") Apply(command=="resume"); else if (command == "bake") StartBake(0); else if(command == "audience") ConnectCurrentAudience(); else Inspect(); }
        catch (Exception e) { File.WriteAllText("Temp/RehearSharedRoom.txt", "FAIL\n" + e); }
    }
    static Transform[] All(Scene scene) => scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
    static string Path(Transform t) => t.parent ? Path(t.parent) + "/" + t.name : t.name;
    static void Dirty(UObject o) { EditorUtility.SetDirty(o); PrefabUtility.RecordPrefabInstancePropertyModifications(o); }
    static void SetPose(Transform target, Transform source)
    {
        target.SetPositionAndRotation(source.position, source.rotation);
        var scale = target.parent ? target.parent.lossyScale : Vector3.one;
        target.localScale = new Vector3(source.lossyScale.x/scale.x, source.lossyScale.y/scale.y, source.lossyScale.z/scale.z);
        Dirty(target);
    }
    static void Apply(bool resume=false)
    {
        if ((!resume && SceneManager.sceneCount != 1) || SceneManager.GetActiveScene().path != Scenes[0]) throw new Exception("Keep only Tutorial open before syncing.");
        var source = SceneManager.GetActiveScene();
        var sourceAll = All(source);
        var counter = sourceAll.Single(t=>t.name=="Counter");
        var chairs = sourceAll.Single(t=>!t.parent && t.name=="Chair");
        if (chairs.childCount!=6) throw new Exception("Expected six source chairs.");
        var lightRoot = sourceAll.Single(t=>!t.parent && t.name=="Baked Area Lighting");
        var sun = sourceAll.Select(t=>t.GetComponent<Light>()).Single(l=>l && l.type==LightType.Directional);
        if (!Lightmapping.TryGetLightingSettings(out var lighting)) throw new Exception("Tutorial lighting settings missing.");
        var environment = new EnvironmentState();
        var volume = sourceAll.Select(t=>t.GetComponent<Volume>()).Single(v=>v && v.isActiveAndEnabled && v.isGlobal && !v.transform.parent && v.name=="Global Volume");
        var cameraData = sourceAll.Select(t=>t.GetComponent<Camera>()).Single(c=>c && c.CompareTag("MainCamera")).GetUniversalAdditionalCameraData();
        if (!AssetDatabase.IsValidFolder(PrefabFolder)) AssetDatabase.CreateFolder("Assets/03_Prefabs", "SharedRoom");
        foreach(string name in new[]{"Counter","SixChairs","BakedRoomLighting"})
            if (!resume && AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder+"/"+name+".prefab")) throw new Exception("Refusing to overwrite an existing shared prefab: "+name);
        var report = new StringBuilder("APPLY\n");
        foreach(var filter in counter.GetComponentsInChildren<MeshFilter>(true).Where(f=>AssetDatabase.GetAssetPath(f.sharedMesh).StartsWith("Assets/Settings/TutorialUI/")))
        {
            var mesh=filter.sharedMesh;
            RehearGeneratedMeshLighting.Unwrap(mesh); EditorUtility.SetDirty(mesh); AssetDatabase.SaveAssetIfDirty(mesh);
            RehearGeneratedMeshLighting.Configure(filter.GetComponent<MeshRenderer>());
            report.AppendLine($"UV2 {mesh.name}: margin=0.04, interiorOverlaps={RehearGeneratedMeshLighting.Overlaps(mesh)}, lightmapScale=2");
        }
        var collider=counter.GetComponent<MeshCollider>();
        if(collider && !collider.sharedMesh)
        {
            collider.sharedMesh=AssetDatabase.LoadAllAssetsAtPath("Assets/03_Prefabs/Counter.FBX").OfType<Mesh>().Single(m=>m.name=="ULT2_Counter"); Dirty(collider);
        }
        GameObject podiumPrefab=resume?AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder+"/Counter.prefab"):Export(counter,"Counter");
        GameObject chairsPrefab=resume?AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder+"/SixChairs.prefab"):Export(chairs,"SixChairs");
        GameObject lightsPrefab=resume?AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder+"/BakedRoomLighting.prefab"):Export(lightRoot,"BakedRoomLighting");
        if(!podiumPrefab || !chairsPrefab || !lightsPrefab) throw new Exception("Missing prepared shared prefab.");
        var opened = new List<Scene>();
        try
        {
            // Preflight all target layouts before modifying their objects.
            foreach(var path in Scenes.Skip(1))
            {
                var scene=SceneManager.GetSceneByPath(path);
                if(!scene.isLoaded) scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
                opened.Add(scene);
                var all=All(scene);
                if(all.Count(t=>t.name=="Counter")!=1 || all.Count(t=>t.name=="_Props")!=1) throw new Exception("Ambiguous target podium: "+path);
                if(all.Any(t=>t.name=="Baked Area Lighting")) throw new Exception("Target already has a baked light rig; inspect before replacing.");
                foreach(var table in sourceAll.Where(t=>t.name=="Conference_table"))
                    if(all.Count(t=>Path(t)==Path(table))!=1) throw new Exception("Table hierarchy differs: "+path);
            }
            foreach(var scene in opened)
            {
                SceneManager.SetActiveScene(scene);
                var all=All(scene);
                var oldCounter=all.Single(t=>t.name=="Counter");
                var oldDesk=all.SingleOrDefault(t=>t.name=="DeskScreen");
                var oldScript=all.SingleOrDefault(t=>t.name=="Panel_Script_New");
                if(PrefabUtility.GetCorrespondingObjectFromSource(oldCounter.gameObject)!=podiumPrefab)
                    ReplacePodium(scene,oldCounter,oldDesk,oldScript,podiumPrefab,counter,report);
                all=All(scene);
                foreach(var table in sourceAll.Where(t=>t.name=="Conference_table"))
                    SetPose(all.Single(t=>Path(t)==Path(table)),table);
                var oldChairs=all.SingleOrDefault(t=>!t.parent && t.name=="Chair");
                if(oldChairs)
                {
                    if(oldChairs.childCount!=6) throw new Exception("Target must have six chairs.");
                    var previous=oldChairs.Cast<Transform>().ToArray();
                    // Preserve each existing audience actor and animation, moving it with its seat.
                    var audience=all.SingleOrDefault(t=>!t.parent && t.name=="Audience");
                    var actors=audience ? audience.Cast<Transform>().ToArray() : Array.Empty<Transform>();
                    var seats=actors.Select(a=>Array.IndexOf(previous,previous.OrderBy(c=>(c.position-a.position).sqrMagnitude).First())).ToArray();
                    var relativePositions=actors.Select((a,i)=>previous[seats[i]].InverseTransformPoint(a.position)).ToArray();
                    var relativeRotations=actors.Select((a,i)=>Quaternion.Inverse(previous[seats[i]].rotation)*a.rotation).ToArray();
                    SetPose(oldChairs,chairs);
                    for(int i=0;i<6;i++) SetPose(previous[i],chairs.GetChild(i));
                    for(int i=0;i<actors.Length;i++)
                    {
                        actors[i].SetPositionAndRotation(previous[seats[i]].TransformPoint(relativePositions[i]),previous[seats[i]].rotation*relativeRotations[i]); Dirty(actors[i]);
                    }
                }
                else
                {
                    oldChairs=((GameObject)PrefabUtility.InstantiatePrefab(chairsPrefab,scene)).transform;
                    oldChairs.name="Chair"; SetPose(oldChairs,chairs);
                }
                var targetSun=all.Select(t=>t.GetComponent<Light>()).Single(l=>l && l.type==LightType.Directional);
                EditorUtility.CopySerialized(sun,targetSun); SetPose(targetSun.transform,sun.transform); Dirty(targetSun);
                var rig=(GameObject)PrefabUtility.InstantiatePrefab(lightsPrefab,scene); rig.name="Baked Area Lighting"; SetPose(rig.transform,lightRoot);
                Lightmapping.lightingSettings=lighting;
                environment.Apply(targetSun);
                // Keep scene-specific effects, replacing only the environment global profile.
                var targetVolumes=all.Select(t=>t.GetComponent<Volume>()).Where(v=>v && v.isGlobal && v.isActiveAndEnabled && v.name=="Global Volume").ToArray();
                if(targetVolumes.Length>1) throw new Exception("Multiple active global volumes need review.");
                Volume targetVolume=targetVolumes.SingleOrDefault();
                if(!targetVolume) { var go=new GameObject("Global Volume"); SceneManager.MoveGameObjectToScene(go,scene); targetVolume=go.AddComponent<Volume>(); }
                targetVolume.isGlobal=true; targetVolume.sharedProfile=volume.sharedProfile; targetVolume.weight=volume.weight; targetVolume.priority=volume.priority; Dirty(targetVolume);
                var camera=all.Select(t=>t.GetComponent<Camera>()).Single(c=>c && c.CompareTag("MainCamera"));
                var data=camera.GetUniversalAdditionalCameraData(); data.renderPostProcessing=cameraData.renderPostProcessing;
                data.volumeLayerMask=cameraData.volumeLayerMask; data.volumeTrigger=camera.transform; Dirty(data);
                ValidateScene(scene,podiumPrefab,chairs,sourceAll,report);
                ConnectAudience(scene,report);
                EditorSceneManager.MarkSceneDirty(scene);
                if(!EditorSceneManager.SaveScene(scene)) throw new Exception("Failed to save "+scene.path);
                File.WriteAllText("Temp/RehearSharedRoom-apply.txt",report.ToString());
            }
            SceneManager.SetActiveScene(source);
            ReplacePodium(source,counter,null,null,podiumPrefab,counter,report);
            ConnectAudience(source,report);
            ValidateScene(source,podiumPrefab,chairs,sourceAll.Where(t=>t).ToArray(),report);
            EditorSceneManager.MarkSceneDirty(source); if(!EditorSceneManager.SaveScene(source)) throw new Exception("Tutorial save failed.");
            AssetDatabase.SaveAssets(); report.AppendLine("PASS: scenes saved; independent bakes required next.");
            File.WriteAllText("Temp/RehearSharedRoom-apply.txt",report.ToString());
        }
        finally
        {
            SceneManager.SetActiveScene(source);
            foreach(var scene in opened) if(scene.IsValid() && scene.isLoaded && !scene.isDirty) EditorSceneManager.CloseScene(scene,true);
        }
    }
    static GameObject Export(Transform source,string name)
    {
        var preview=EditorSceneManager.NewPreviewScene();
        var clone=UObject.Instantiate(source.gameObject); SceneManager.MoveGameObjectToScene(clone,preview);
        try
        {
            clone.name=name; clone.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity); clone.transform.localScale=source.lossyScale;
            foreach(var c in clone.GetComponentsInChildren<Component>(true))
            {
                if(!c) throw new Exception("Missing component inside export "+name);
                var so=new SerializedObject(c); var p=so.GetIterator(); bool changed=false;
                while(p.Next(true))
                {
                    if(p.propertyType!=SerializedPropertyType.ObjectReference || !p.objectReferenceValue || EditorUtility.IsPersistent(p.objectReferenceValue)) continue;
                    var target=p.objectReferenceValue as GameObject;
                    if(p.objectReferenceValue is Component component) target=component.gameObject;
                    if(target && target!=clone && !target.transform.IsChildOf(clone.transform)) { p.objectReferenceValue=null; changed=true; }
                }
                if(changed) so.ApplyModifiedPropertiesWithoutUndo();
            }
            foreach(var r in clone.GetComponentsInChildren<Renderer>(true)) { r.lightmapIndex=-1; r.realtimeLightmapIndex=-1; }
            var prefab=PrefabUtility.SaveAsPrefabAsset(clone,PrefabFolder+"/"+name+".prefab",out bool success);
            if(!success || !prefab) throw new Exception("Prefab export failed: "+name);
            return prefab;
        }
        finally { UObject.DestroyImmediate(clone); EditorSceneManager.ClosePreviewScene(preview); }
    }
    static void ConnectCurrentAudience()
    {
        var scene=SceneManager.GetActiveScene();
        if(scene.path!=Scenes[0]) throw new Exception("Open Tutorial.");
        var report=new StringBuilder(); ConnectAudience(scene,report);
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        File.WriteAllText("Temp/RehearAudienceConnections.txt",report.ToString());
    }
    static void ConnectAudience(Scene scene,StringBuilder report)
    {
        var audience=scene.GetRootGameObjects().SingleOrDefault(g=>g.name=="Audience");
        if(!audience) return;
        foreach(var actor in audience.transform.Cast<Transform>())
        {
            string path="Assets/03_Prefabs/Audience/"+actor.name+".prefab";
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if(!prefab) throw new Exception("Audience prefab missing: "+path);
            if(PrefabUtility.GetCorrespondingObjectFromSource(actor.gameObject)==prefab) continue;
            var renderers=actor.GetComponentsInChildren<Renderer>(true);
            var materials=renderers.Select(r=>r.sharedMaterials).ToArray();
            var position=actor.position; var rotation=actor.rotation;
            var root=PrefabUtility.GetOutermostPrefabInstanceRoot(actor.gameObject);
            if(root && root!=actor.gameObject) throw new Exception("Audience has an unexpected prefab parent.");
            if(root) PrefabUtility.UnpackPrefabInstance(root,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
            PrefabUtility.ConvertToPrefabInstance(actor.gameObject,prefab,new ConvertToPrefabInstanceSettings {
                objectMatchMode=ObjectMatchMode.ByHierarchy,
                recordPropertyOverridesOfMatches=true,
                componentsNotMatchedBecomesOverride=true,
                gameObjectsNotMatchedBecomesOverride=true,
                changeRootNameToAssetName=false
            },InteractionMode.AutomatedAction);
            if(PrefabUtility.GetCorrespondingObjectFromSource(actor.gameObject)!=prefab || actor.position!=position || Quaternion.Angle(actor.rotation,rotation)>.001f)
                throw new Exception("Audience prefab/pose verification failed.");
            for(int i=0;i<renderers.Length;i++) if(!renderers[i] || !renderers[i].sharedMaterials.SequenceEqual(materials[i])) throw new Exception("Audience materials changed.");
            report.AppendLine(scene.name+"/"+actor.name+": connected="+path+", pose/materials preserved");
        }
    }
    static void MapTree(Transform before,Transform after,Dictionary<UObject,UObject> map)
    {
        foreach(var t in before.GetComponentsInChildren<Transform>(true))
        {
            string relative=AnimationUtility.CalculateTransformPath(t,before);
            var target=relative.Length==0?after:after.Find(relative);
            if(!target) continue;
            map[t.gameObject]=target.gameObject;
            foreach(var c in t.GetComponents<Component>().Where(c=>c))
            {
                var same=t.GetComponents(c.GetType()); int index=Array.IndexOf(same,c);
                var others=target.GetComponents(c.GetType()); if(index<others.Length) map[c]=others[index];
            }
        }
    }
    static void ReplacePodium(Scene scene,Transform old,Transform oldDesk,Transform oldScript,GameObject prefab,Transform pose,StringBuilder report)
    {
        // The old counter is a child of the broad props prefab. Unpack that scene
        // instance only; never modify the shared props asset or unrelated objects.
        var outer=PrefabUtility.GetOutermostPrefabInstanceRoot(old.gameObject);
        if(outer) PrefabUtility.UnpackPrefabInstance(outer,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
        var replacement=((GameObject)PrefabUtility.InstantiatePrefab(prefab,scene)).transform;
        replacement.name="Counter"; replacement.SetParent(old.parent,false); SetPose(replacement,pose);
        var map=new Dictionary<UObject,UObject>(); MapTree(old,replacement,map);
        var remove=new List<Transform>{old};
        var desk=replacement.Find("DeskScreen"); var script=replacement.Find("Panel_Script_New");
        if(oldDesk && !oldDesk.IsChildOf(old))
        {
            MapTree(oldDesk,desk,map); desk.GetComponent<RawImage>().texture=oldDesk.GetComponent<RawImage>().texture; remove.Add(oldDesk);
        }
        if(oldScript && !oldScript.IsChildOf(old))
        {
            MapTree(oldScript,script,map); script.gameObject.SetActive(oldScript.gameObject.activeSelf);
            var oldText=oldScript.GetComponentsInChildren<TMP_Text>(true).Single(t=>t.name=="Text_Script_New");
            var newText=script.GetComponentsInChildren<TMP_Text>(true).Single(t=>t.name=="Text_Script_New");
            newText.text=oldText.text; Dirty(newText); remove.Add(oldScript);
        }
        int references=0;
        foreach(var t in All(scene).Where(t=>!remove.Any(r=>t==r || t.IsChildOf(r))))
            foreach(var c in t.GetComponents<Component>().Where(c=>c && !(c is Transform)))
            {
                var so=new SerializedObject(c); var p=so.GetIterator(); bool changed=false;
                while(p.Next(true))
                    if(p.propertyType==SerializedPropertyType.ObjectReference && p.objectReferenceValue && map.TryGetValue(p.objectReferenceValue,out var target))
                    { p.objectReferenceValue=target; changed=true; references++; }
                if(changed) { so.ApplyModifiedPropertiesWithoutUndo(); Dirty(c); }
            }
        var all=All(scene);
        var camera=all.Select(t=>t.GetComponent<Camera>()).Single(c=>c && c.CompareTag("MainCamera"));
        foreach(var canvas in replacement.GetComponentsInChildren<Canvas>(true)) { canvas.worldCamera=camera; Dirty(canvas); }
        var controller=all.Select(t=>t.GetComponent<PresentationController>()).SingleOrDefault(c=>c);
        var toggle=replacement.GetComponentInChildren<PodiumScriptToggle>(true); toggle.controller=controller;
        if(controller) { controller.scriptPanel=script.gameObject; Dirty(controller); }
        var manager=all.Select(t=>t.GetComponent<PresentationManager>()).SingleOrDefault(c=>c);
        if(manager)
        {
            manager.deskScreen=desk.GetComponent<RawImage>();
            var so=new SerializedObject(manager);
            so.FindProperty("previousSlideHint").objectReferenceValue=desk.GetComponentsInChildren<TutorialDirectionArrow>(true).First(t=>t.name.Contains("Left"));
            so.FindProperty("nextSlideHint").objectReferenceValue=desk.GetComponentsInChildren<TutorialDirectionArrow>(true).First(t=>t.name.Contains("Right"));
            so.ApplyModifiedPropertiesWithoutUndo(); Dirty(manager);
        }
        var scroller=all.Select(t=>t.GetComponent<ScriptScroller>()).SingleOrDefault(c=>c);
        if(!scroller)
        {
            // Feedback has no presentation manager: reuse loaded session text locally,
            // without starting a presentation, microphone, or remote session.
            scroller=replacement.gameObject.AddComponent<ScriptScroller>();
            var text=script.GetComponentsInChildren<TMP_Text>(true).Single(t=>t.name=="Text_Script_New"); text.text=""; Dirty(text);
            script.gameObject.SetActive(false);
        }
        var scrollState=new SerializedObject(scroller);
        scrollState.FindProperty("scriptText").objectReferenceValue=script.GetComponentsInChildren<TMP_Text>(true).Single(t=>t.name=="Text_Script_New");
        scrollState.FindProperty("previousPageHint").objectReferenceValue=script.Find("ScriptDirectionHints/Arrow_Up").gameObject;
        scrollState.FindProperty("nextPageHint").objectReferenceValue=script.Find("ScriptDirectionHints/Arrow_Down").gameObject;
        scrollState.ApplyModifiedPropertiesWithoutUndo(); Dirty(scroller);
        bool tutorial=scene.path==Scenes[0];
        desk.Find("SlideDirectionHints").gameObject.SetActive(!tutorial && manager);
        script.Find("ScriptDirectionHints").gameObject.SetActive(!tutorial && manager);
        toggle.Refresh(); Dirty(toggle); Dirty(toggle.label); Dirty(toggle.background);
        foreach(var root in remove) UObject.DestroyImmediate(root.gameObject);
        foreach(var t in replacement.GetComponentsInChildren<Transform>(true)) { Dirty(t); Dirty(t.gameObject); }
        report.AppendLine(scene.name+": Counter prefab connected, scene references remapped="+references);
    }
    static void ValidateScene(Scene scene,GameObject prefab,Transform chairs,Transform[] source,StringBuilder report)
    {
        var all=All(scene); var podium=all.Single(t=>t.name=="Counter");
        if(PrefabUtility.GetCorrespondingObjectFromSource(podium.gameObject)!=prefab) throw new Exception("Podium is not connected.");
        var targetChairs=all.Single(t=>!t.parent && t.name=="Chair");
        if(targetChairs.childCount!=6) throw new Exception("Missing chairs.");
        for(int i=0;i<6;i++)
            if(Vector3.Distance(targetChairs.GetChild(i).position,chairs.GetChild(i).position)>.001f || Quaternion.Angle(targetChairs.GetChild(i).rotation,chairs.GetChild(i).rotation)>.01f) throw new Exception("Chair pose mismatch.");
        foreach(var table in source.Where(t=>t && t.name=="Conference_table"))
            if(Vector3.Distance(all.Single(t=>Path(t)==Path(table)).position,table.position)>.001f) throw new Exception("Table mismatch.");
        foreach(var c in podium.GetComponentsInChildren<Component>(true))
        {
            if(!c) throw new Exception("Missing podium component.");
            var so=new SerializedObject(c); var p=so.GetIterator();
            while(p.Next(true))
            {
                if(p.propertyType!=SerializedPropertyType.ObjectReference || !p.objectReferenceValue || EditorUtility.IsPersistent(p.objectReferenceValue)) continue;
                var go=p.objectReferenceValue as GameObject; if(p.objectReferenceValue is Component component) go=component.gameObject;
                if(go && go.scene!=scene) throw new Exception("Cross-scene reference: "+c.GetType().Name+"."+p.propertyPath);
            }
        }
        report.AppendLine(scene.name+": six chair poses, tables, prefab binding, scene references PASS");
    }
    sealed class EnvironmentState
    {
        readonly Material sky=RenderSettings.skybox;
        readonly AmbientMode mode=RenderSettings.ambientMode;
        readonly Color ambientSky=RenderSettings.ambientSkyColor,equator=RenderSettings.ambientEquatorColor,ground=RenderSettings.ambientGroundColor;
        readonly float intensity=RenderSettings.ambientIntensity,reflection=RenderSettings.reflectionIntensity;
        readonly bool fog=RenderSettings.fog;
        readonly Color fogColor=RenderSettings.fogColor;
        readonly float fogDensity=RenderSettings.fogDensity;
        internal void Apply(Light sun)
        {
            RenderSettings.skybox=sky; RenderSettings.ambientMode=mode; RenderSettings.ambientSkyColor=ambientSky;
            RenderSettings.ambientEquatorColor=equator; RenderSettings.ambientGroundColor=ground; RenderSettings.ambientIntensity=intensity;
            RenderSettings.reflectionIntensity=reflection; RenderSettings.sun=sun; RenderSettings.fog=fog; RenderSettings.fogColor=fogColor; RenderSettings.fogDensity=fogDensity;
        }
    }
    static void StartBake(int index)
    {
        if(SceneManager.sceneCount!=1 || SceneManager.GetActiveScene().isDirty) throw new Exception("Save and close other scenes before independent baking.");
        var scene=EditorSceneManager.OpenScene(Scenes[index],OpenSceneMode.Single);
        SessionState.SetInt("Rehear.RoomBake",index); bakeFinished=false;
        File.AppendAllText("Temp/RehearSharedRoom-apply.txt","\nBAKE START "+scene.name+"\n");
        if(!Lightmapping.BakeAsync()) { SessionState.SetInt("Rehear.RoomBake",-1); throw new Exception("Bake did not start."); }
    }
    static void FinishBake()
    {
        int index=SessionState.GetInt("Rehear.RoomBake",-1); var scene=SceneManager.GetActiveScene();
        if(scene.path!=Scenes[index]) throw new Exception("Scene changed during bake.");
        foreach(var filter in All(scene).Select(t=>t.GetComponent<MeshFilter>()).Where(f=>f && AssetDatabase.GetAssetPath(f.sharedMesh).StartsWith("Assets/Settings/TutorialUI/")))
        {
            var renderer=filter.GetComponent<MeshRenderer>();
            int lightmap=renderer.lightmapIndex;
            if(lightmap<0 || lightmap>=LightmapSettings.lightmaps.Length || !LightmapSettings.lightmaps[lightmap].lightmapColor) throw new Exception("Generated mesh not baked: "+Path(filter.transform));
            File.AppendAllText("Temp/RehearSharedRoom-apply.txt",$"BAKED {scene.name}/{filter.name}: lightmap={lightmap}, UV2={filter.sharedMesh.uv2.Length}, scaleOffset={renderer.lightmapScaleOffset}\n");
        }
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        File.AppendAllText("Temp/RehearSharedRoom-apply.txt","BAKE PASS "+scene.name+"\n");
        SessionState.SetInt("Rehear.RoomBake",-1);
        if(index+1<Scenes.Length) StartBake(index+1);
        else { EditorSceneManager.OpenScene(Scenes[0],OpenSceneMode.Single); File.AppendAllText("Temp/RehearSharedRoom-apply.txt","ALL THREE BAKES COMPLETE\n"); }
    }
    static void VerifySaved()
    {
        if(SceneManager.sceneCount!=1 || SceneManager.GetActiveScene().isDirty) throw new Exception("Save before verifying scene reloads.");
        var report=new StringBuilder();
        var overlaps=typeof(Lightmapping).GetMethod("HasUVOverlaps",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic);
        var expected=new Dictionary<string,(Vector3 position,Quaternion rotation,Vector3 scale)>();
        try
        {
            for(int index=0;index<Scenes.Length;index++)
            {
                var scene=EditorSceneManager.OpenScene(Scenes[index],OpenSceneMode.Single); var all=All(scene);
                report.AppendLine("SCENE "+scene.name+" loadedAlone="+(SceneManager.sceneCount==1));
                foreach(var t in all.Where(t=>t.name=="Counter" || t.name=="Conference_table" || (t.parent && t.parent.name=="Chair")))
                {
                    string key=Path(t);
                    if(index==0) expected[key]=(t.position,t.rotation,t.lossyScale);
                    else if(!expected.TryGetValue(key,out var pose) || Vector3.Distance(t.position,pose.position)>.001f || Quaternion.Angle(t.rotation,pose.rotation)>.01f || Vector3.Distance(t.lossyScale,pose.scale)>.001f) throw new Exception("Furniture mismatch "+scene.name+"/"+key);
                }
                foreach(var f in all.Select(t=>t.GetComponent<MeshFilter>()).Where(f=>f && AssetDatabase.GetAssetPath(f.sharedMesh).StartsWith("Assets/Settings/TutorialUI/")))
                {
                    var r=f.GetComponent<MeshRenderer>(); bool? warning=overlaps==null?null:(bool)overlaps.Invoke(null,new object[]{r});
                    report.AppendLine($"MESH {Path(f.transform)} UVOverlapWarning={warning}, lightmap={r.lightmapIndex}, gi={r.receiveGI}");
                    if(warning==true || r.lightmapIndex<0 || r.lightmapIndex>=LightmapSettings.lightmaps.Length) throw new Exception("Mesh bake validation failed: "+f.name);
                }
                var podium=all.Single(t=>t.name=="Counter");
                report.AppendLine("PREFAB "+PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(podium.gameObject));
                var desk=podium.Find("DeskScreen").GetComponent<RawImage>();
                Canvas.ForceUpdateCanvases(); desk.SetAllDirty(); Canvas.ForceUpdateCanvases();
                report.AppendLine($"DISPLAY active={desk.isActiveAndEnabled}, camera={desk.canvas.worldCamera?.name}, white={desk.color}, position={desk.transform.position:F3}");
                var controller=all.Select(t=>t.GetComponent<PresentationController>()).SingleOrDefault(c=>c);
                if(controller && controller.scriptPanel!=podium.Find("Panel_Script_New").gameObject) throw new Exception("Script binding broken.");
                var manager=all.Select(t=>t.GetComponent<PresentationManager>()).SingleOrDefault(c=>c);
                if(manager && manager.deskScreen!=desk) throw new Exception("Slide binding broken.");
                var toggle=podium.GetComponentInChildren<PodiumScriptToggle>(true);
                bool visible=toggle.scriptPanel.activeSelf;
                toggle.ToggleScript(); if(toggle.scriptPanel.activeSelf==visible || !toggle.gameObject.activeInHierarchy) throw new Exception("Toggle failed.");
                toggle.ToggleScript(); if(toggle.scriptPanel.activeSelf!=visible) throw new Exception("Toggle restore failed.");
                foreach(var actor in all.Where(t=>t.parent && t.parent.name=="Audience"))
                {
                    string asset=PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(actor.gameObject);
                    if(!asset.StartsWith("Assets/03_Prefabs/Audience/")) throw new Exception("Audience is still an FBX instance.");
                    report.AppendLine("AUDIENCE "+actor.name+" -> "+asset);
                }
                report.AppendLine("LIGHTS "+all.Select(t=>t.GetComponent<Light>()).Count(l=>l && l.isActiveAndEnabled && l.lightmapBakeType==LightmapBakeType.Baked));
                report.AppendLine("PASS furniture, links, toggle round-trip, prefab connections");
                // Toggle test only restores transient state; no scene content is changed.
            }
        }
        finally
        {
            EditorSceneManager.OpenScene(Scenes[0],OpenSceneMode.Single);
            File.WriteAllText("Temp/RehearSharedRoom-verify.txt",report.ToString());
        }
    }
    static void Inspect()
    {
        var original = SceneManager.GetActiveScene();
        var report = new StringBuilder();
        foreach (string path in Scenes)
        {
            var scene = SceneManager.GetSceneByPath(path);
            bool opened = !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                var all = All(scene);
                string lighting = "unassigned";
                if (Lightmapping.TryGetLightingSettings(out var settings)) lighting = settings.name;
                report.AppendLine("SCENE " + path + " dirty=" + scene.isDirty + " lighting=" + lighting);
                foreach (var root in scene.GetRootGameObjects()) report.AppendLine($"ROOT {root.name} pos={root.transform.position:F3} rot={root.transform.eulerAngles:F2} scale={root.transform.lossyScale:F3}");
                foreach (var f in all.Select(t => t.GetComponent<MeshFilter>()).Where(f => f && AssetDatabase.GetAssetPath(f.sharedMesh).StartsWith("Assets/Settings/TutorialUI/")))
                    report.AppendLine($"GENERATED {Path(f.transform)} mesh={f.sharedMesh.name} verts={f.sharedMesh.vertexCount} uv2={f.sharedMesh.uv2.Length} static={GameObjectUtility.GetStaticEditorFlags(f.gameObject)} GI={f.GetComponent<MeshRenderer>()?.receiveGI} lightmap={f.GetComponent<Renderer>()?.lightmapIndex}");
                foreach (var t in all.Where(t => t.name == "Counter" || t.name == "Chair" || t.name.IndexOf("table", StringComparison.OrdinalIgnoreCase) >= 0 || t.name == "DeskScreen" || t.name.StartsWith("Panel_Script") || (t.parent && (t.parent.name == "Chair" || t.parent.name == "Audience"))))
                {
                    report.AppendLine($"OBJECT {Path(t)} active={t.gameObject.activeSelf} pos={t.position:F3} rot={t.eulerAngles:F2} scale={t.lossyScale:F3} children={t.childCount} prefab={PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject)}");
                    foreach (var child in t.Cast<Transform>()) report.AppendLine(" CHILD " + child.name);
                }
                foreach (var t in all)
                    foreach (var c in t.GetComponents<Component>())
                    {
                        if (!c) { report.AppendLine("MISSING " + Path(t)); continue; }
                        if (c is Light l) report.AppendLine($"LIGHT {Path(t)} {l.type} {l.lightmapBakeType} intensity={l.intensity} active={l.isActiveAndEnabled}");
                        if (c is UnityEngine.Rendering.Volume volume) report.AppendLine($"VOLUME {Path(t)} active={volume.isActiveAndEnabled} global={volume.isGlobal} profile={volume.sharedProfile?.name}");
                        if (c is Camera camera) report.AppendLine($"CAMERA {Path(t)} main={camera.CompareTag("MainCamera")} pos={t.position:F2}");
                        if (!(c is MonoBehaviour) && !(c is Canvas)) continue;
                        if (c.GetType().Name.Contains("CurvedUI")) continue;
                        var so = new SerializedObject(c); var p = so.GetIterator();
                        while (p.Next(true))
                        {
                            if (p.propertyType != SerializedPropertyType.ObjectReference || !p.objectReferenceValue || EditorUtility.IsPersistent(p.objectReferenceValue)) continue;
                            var target = p.objectReferenceValue as GameObject;
                            if (p.objectReferenceValue is Component component) target = component.gameObject;
                            if (target && (target.name.Contains("Script") || target.name.Contains("Screen") || t.name.Contains("Screen") || t.name.Contains("Script")))
                                report.AppendLine($"REF {Path(t)} {c.GetType().Name}.{p.propertyPath} -> {Path(target.transform)}");
                        }
                    }
            }
            finally { SceneManager.SetActiveScene(original); if (opened) EditorSceneManager.CloseScene(scene, true); }
        }
        File.WriteAllText("Temp/RehearSharedRoom.txt", report.ToString());
    }
}
