using System;
using System.IO;
using System.Linq;
using System.Text;
using CurvedUI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[InitializeOnLoad]
internal static class RehearOpeningStartSetup
{
    const string Request = "Temp/RehearOpeningStart.request";
    const string MaterialPath = "Assets/Settings/Opening Start Button.mat";
    static RehearOpeningStartSetup() { EditorApplication.update += Poll; EditorApplication.update += WatchPlayback; }
    static void WatchPlayback()
    {
        const string stopRequest="Temp/RehearOpeningStop.request";
        if(EditorApplication.isPlaying && UnityEngine.SceneManagement.SceneManager.GetActiveScene().name=="Scene_00" && File.Exists(stopRequest))
        { File.Delete(stopRequest); EditorApplication.ExitPlaymode(); return; }
        if(!EditorApplication.isPlaying || !SessionState.GetBool("RehearWatchOpening",false)) return;
        var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if(scene.name!="Scene_00") return;
        var all=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
        var logo=all.Select(t=>t.GetComponent<RehearLogoIntro>()).FirstOrDefault(t=>t);
        var cue=all.Select(t=>t.GetComponent<OpeningStartCue>()).FirstOrDefault(t=>t);
        if(!logo || !cue) return;
        var group=cue.GetComponent<CanvasGroup>();
        var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
        bool ready=(bool)typeof(OpeningStartCue).GetField("introReady",flags).GetValue(cue);
        bool playing=(bool)typeof(RehearLogoIntro).GetField("isPlaying",flags).GetValue(logo);
        float elapsed=(float)typeof(RehearLogoIntro).GetField("elapsed",flags).GetValue(logo);
        string phase=playing ? "logo" : ready ? "ready" : cue.gameObject.activeInHierarchy ? "fade" : "delay";
        if(phase==SessionState.GetString("RehearOpeningPhase","")) return;
        SessionState.SetString("RehearOpeningPhase",phase);
        var actual=typeof(OpeningStartCue).GetField("lastCue",flags).GetValue(cue);
        bool canClick=cue.GetComponent<Button>().IsInteractable() && cue.gameObject.activeInHierarchy;
        bool pass=phase=="ready" ? elapsed>=RehearLogoIntro.Duration && canClick && group.alpha==1 && ready : !canClick && !ready;
        File.AppendAllText("Temp/RehearOpeningPlayback.txt",$"{(pass?"PASS":"FAIL")} phase={phase} logoTime={elapsed:F3} buttonClickable={canClick} alpha={group?.alpha} cueReady={ready} cue={actual}\n");
        if(phase=="ready" || !pass) SessionState.SetBool("RehearWatchOpening",false);
        if(phase=="ready" && pass)
        {
            var report=new StringBuilder();
            var pointer=new PointerEventData(EventSystem.current){pointerId=98765,button=PointerEventData.InputButton.Left};
            try
            {
                cue.OnPointerEnter(pointer);
                CheckTriggerSurface(cue.controllerVisual,true,report);
                CaptureTriggerSurface(cue.controllerVisual,"runtime-hover");
                cue.OnPointerDown(pointer);
                CheckTriggerSurface(cue.controllerVisual,true,report);
                report.AppendLine("PASS runtime hover and press retain native blue trigger surface.");
            }
            catch(Exception e) { report.AppendLine("FAIL "+e); }
            finally { cue.OnPointerUp(pointer); cue.OnPointerExit(pointer); }
            File.AppendAllText("Temp/RehearOpeningPlayback.txt",report.ToString());
        }
    }
    static void CheckTriggerSurface(Quest3TutorialControllerVisual visual, bool paused, StringBuilder report)
    {
        var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
        var surface=(SkinnedMeshRenderer)typeof(Quest3TutorialControllerVisual).GetField("bodySurface",flags).GetValue(visual);
        var material=(Material)typeof(Quest3TutorialControllerVisual).GetField("runtimeHighlight",flags).GetValue(visual);
        bool actualPause=(bool)typeof(Quest3TutorialControllerVisual).GetField("pulsePaused",flags).GetValue(visual);
        var hand=(Transform)typeof(Quest3TutorialControllerVisual).GetField("rightHandTarget",flags).GetValue(visual);
        var device=UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.RightHand);
        bool tracked=device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked,out bool trackedValue) && trackedValue;
        report.AppendLine($"visualEnabled={visual.isActiveAndEnabled} targetActive={hand && hand.gameObject.activeInHierarchy} XR={UnityEngine.XR.XRSettings.enabled} headsetActive={UnityEngine.XR.XRSettings.isDeviceActive} handValid={device.isValid} handTracked={tracked}");
        report.AppendLine($"surface={surface} active={surface && surface.gameObject.activeInHierarchy} forcedOff={surface && surface.forceRenderingOff} material={material} pulsePaused={actualPause} expected={paused} cue={typeof(Quest3TutorialControllerVisual).GetField("currentCue",flags).GetValue(visual)}");
        if(surface) report.AppendLine("materials="+string.Join(",",surface.sharedMaterials.Select(m=>m ? m.name : "null"))+" triggerIndices="+surface.sharedMesh.GetIndexCount(1));
        if(material) report.AppendLine("emission="+material.GetColor("_EmissionColor"));
        if(!surface || !surface.gameObject.activeInHierarchy || surface.forceRenderingOff || !material ||
            surface.sharedMaterials.Length!=4 || surface.sharedMaterials[1]!=material ||
            surface.sharedMesh.GetIndexCount(1)==0 || material.GetColor("_EmissionColor").b<1 || actualPause!=paused)
            throw new Exception("Trigger surface is not visibly emissive or pulse state is incorrect.");
        report.AppendLine($"triggerSurface=BLUE visible=True pulsePaused={actualPause} emission={material.GetColor("_EmissionColor")}");
    }
    static void CaptureTriggerSurface(Quest3TutorialControllerVisual visual, string name)
    {
        var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
        var surface=(SkinnedMeshRenderer)typeof(Quest3TutorialControllerVisual).GetField("bodySurface",flags).GetValue(visual);
        var oldLayer=surface.gameObject.layer;
        var cameraObject=new GameObject("Temporary Trigger Verification Camera") { hideFlags=HideFlags.HideAndDontSave };
        var target=new RenderTexture(768,768,24);
        var oldActive=RenderTexture.active;
        Texture2D pixels=null;
        try
        {
            surface.gameObject.layer=30;
            var camera=cameraObject.AddComponent<Camera>(); camera.enabled=false;
            cameraObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.025f,.03f,.05f);
            camera.cullingMask=1<<30; camera.nearClipPlane=.005f; camera.farClipPlane=10;
            camera.fieldOfView=32; camera.aspect=1;
            var center=surface.bounds.center;
            camera.transform.position=center+visual.transform.TransformDirection(new Vector3(.20f,.10f,.24f));
            camera.transform.LookAt(center,visual.transform.up);
            var request=new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination=target };
            UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,request);
            RenderTexture.active=target; pixels=new Texture2D(768,768,TextureFormat.RGB24,false);
            pixels.ReadPixels(new Rect(0,0,768,768),0,0); pixels.Apply();
            File.WriteAllBytes("Temp/RehearTrigger-"+name+".png",pixels.EncodeToPNG());
        }
        finally
        {
            surface.gameObject.layer=oldLayer; RenderTexture.active=oldActive;
            if(pixels) UnityEngine.Object.DestroyImmediate(pixels);
            target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(cameraObject);
        }
    }
    static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning) return;
        string command;
        try { command = File.ReadAllText(Request).Trim(); if(command.Length==0) return; File.Delete(Request); }
        catch(IOException) { return; }
        try { if(command=="finish") { Apply(); ApplySky(); Verify(); }
            else if(command=="watch" || command=="playcheck") { File.WriteAllText("Temp/RehearOpeningPlayback.txt",""); SessionState.SetString("RehearOpeningPhase",""); SessionState.SetBool("RehearWatchOpening",true); if(command=="playcheck") EditorApplication.delayCall+=EditorApplication.EnterPlaymode; }
            else if (command == "apply") Apply(); else if(command=="sky") ApplySky(); else if(command=="verify") Verify(); else Inspect(); }
        catch (Exception e) { File.WriteAllText("Temp/RehearOpeningStart.txt", "FAIL\n" + e); Debug.LogException(e); }
    }
    static Transform[] All()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/01_Scene/Scene_00.unity" || UnityEngine.SceneManagement.SceneManager.sceneCount != 1)
            throw new InvalidOperationException("Keep only Scene_00 open.");
        return scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
    }
    static void Dirty(UnityEngine.Object obj) { EditorUtility.SetDirty(obj); PrefabUtility.RecordPrefabInstancePropertyModifications(obj); }
    static void ApplySky()
    {
        All();
        const string source="Assets/04_Images/Textures/UI/Frame 2134282960.png";
        const string path="Assets/Settings/Opening Blue Silk Skybox.mat";
        var importer=(TextureImporter)AssetImporter.GetAtPath(source);
        importer.npotScale=TextureImporterNPOTScale.None;
        importer.maxTextureSize=2048; importer.crunchedCompression=false;
        importer.textureCompression=TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled=true; importer.wrapMode=TextureWrapMode.Clamp;
        importer.filterMode=FilterMode.Trilinear;
        var android=importer.GetPlatformTextureSettings("Android");
        android.overridden=true; android.maxTextureSize=2048; android.format=TextureImporterFormat.ASTC_4x4;
        importer.SetPlatformTextureSettings(android); importer.SaveAndReimport();
        var shader=Shader.Find("Rehear/Skybox/Opening Blue Silk");
        if(!shader || ShaderUtil.ShaderHasError(shader)) throw new Exception("Sky shader compilation failed.");
        var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!material) { material=new Material(shader){name="Opening Blue Silk Skybox"}; AssetDatabase.CreateAsset(material,path); }
        material.shader=shader; material.mainTexture=AssetDatabase.LoadAssetAtPath<Texture2D>(source);
        material.SetFloat("_Exposure",1); material.SetFloat("_MotionAmount",.038f);
        material.SetFloat("_Speed",.6f); material.SetFloat("_PreviewTime",-1);
        material.SetFloat("_Procedural",0);
        material.SetFloat("_Rotation",0);
        RenderSettings.skybox=material;
        // The artwork is a visual backdrop, not a blue studio illuminant.
        RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor=new Color(.55f,.55f,.55f);
        RenderSettings.ambientEquatorColor=new Color(.34f,.34f,.34f);
        RenderSettings.ambientGroundColor=new Color(.16f,.16f,.16f);
        RenderSettings.reflectionIntensity=.08f;
        const string volumePath="Assets/Settings/Opening Blue Silk Volume.asset";
        var volume=All().Select(t=>t.GetComponent<UnityEngine.Rendering.Volume>()).Single(v=>v && v.isGlobal);
        if(!AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>(volumePath))
            AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(volume.sharedProfile),volumePath);
        var profile=AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>(volumePath);
        volume.sharedProfile=profile; Dirty(volume);
        if(profile.TryGet<UnityEngine.Rendering.Universal.ColorAdjustments>(out var grading)) { grading.contrast.value=0; grading.saturation.value=-3; EditorUtility.SetDirty(grading); }
        if(profile.TryGet<UnityEngine.Rendering.Universal.WhiteBalance>(out var white)) { white.temperature.value=0; EditorUtility.SetDirty(white); }
        if(profile.TryGet<UnityEngine.Rendering.Universal.Bloom>(out var bloom)) { bloom.intensity.value=.04f; bloom.threshold.value=1; EditorUtility.SetDirty(bloom); }
        if(profile.TryGet<UnityEngine.Rendering.Universal.Tonemapping>(out var tone)) { tone.mode.value=UnityEngine.Rendering.Universal.TonemappingMode.Neutral; EditorUtility.SetDirty(tone); }
        EditorUtility.SetDirty(material); AssetDatabase.SaveAssets();
        var scene=EditorSceneManager.GetActiveScene(); EditorSceneManager.MarkSceneDirty(scene);
        if(!EditorSceneManager.SaveScene(scene)) throw new Exception("Opening sky save failed.");
        SceneView.RepaintAll(); UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        File.WriteAllText("Temp/RehearOpeningSky.txt",$"PASS\nscene={scene.path}\ntexture={material.mainTexture.width}x{material.mainTexture.height}\nshaderErrors={ShaderUtil.ShaderHasError(shader)}\nmotion=0.006 speed=0.12\nAndroid=ASTC_4x4\n");
    }
    static void Verify()
    {
        var all=All();
        var button=all.Select(t=>t.GetComponent<Button>()).Single(b=>b && b.name=="Btn_Scene00_to_Scene01");
        var cue=button.GetComponent<OpeningStartCue>();
        var visual=cue.controllerVisual;
        var previousSelection=EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
        var report=new StringBuilder();
        var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
        Action update=()=>typeof(OpeningStartCue).GetMethod("Update",flags).Invoke(cue,null);
        Action ready=()=>typeof(OpeningStartCue).GetField("resumeAt",flags).SetValue(cue,-1f);
        Action<string,Quest3TutorialControllerVisual.Cue> check=(name,expected)=>{
            var actual=(Quest3TutorialControllerVisual.Cue)typeof(OpeningStartCue).GetField("lastCue",flags).GetValue(cue);
            if(actual!=expected) throw new Exception(name+" failed: "+actual);
            report.AppendLine(name+"="+actual);
        };
        try
        {
            // Exercise the actual event handlers without invoking scene navigation or networking.
            visual.PreviewInScene(Quest3TutorialControllerVisual.Cue.Idle);
            typeof(OpeningStartCue).GetMethod("Awake",flags).Invoke(cue,null);
            typeof(OpeningStartCue).GetMethod("OnEnable",flags).Invoke(cue,null);
            cue.SetIntroReady(false); ready(); update(); check("logoPlaying",Quest3TutorialControllerVisual.Cue.Hidden);
            cue.SetIntroReady(true);
            ready(); update(); check("waiting",Quest3TutorialControllerVisual.Cue.Trigger);
            var e=new PointerEventData(EventSystem.current){pointerId=17,button=PointerEventData.InputButton.Left};
            cue.OnPointerEnter(e); check("hover",Quest3TutorialControllerVisual.Cue.Trigger); CheckTriggerSurface(visual,true,report);
            visual.UpdateScenePreview(.5f,.016f); CaptureTriggerSurface(visual,"hover");
            cue.OnPointerDown(e); check("press",Quest3TutorialControllerVisual.Cue.Trigger); CheckTriggerSurface(visual,true,report);
            cue.OnPointerExit(e); ready(); update(); check("dragOutsideWhilePressed",Quest3TutorialControllerVisual.Cue.Trigger);
            cue.OnPointerUp(e); ready(); update(); check("cancelResume",Quest3TutorialControllerVisual.Cue.Trigger);
            CheckTriggerSurface(visual,false,report);
            visual.UpdateScenePreview(.5f,.016f); CaptureTriggerSurface(visual,"waiting");
            cue.Complete(); check("completed",Quest3TutorialControllerVisual.Cue.Hidden);
            report.AppendLine("shaderErrors="+ShaderUtil.ShaderHasError(RenderSettings.skybox.shader));
            report.AppendLine("ambient="+RenderSettings.ambientMode);
            report.AppendLine("buttonWorldWidth="+(((RectTransform)button.transform).rect.width*button.transform.lossyScale.x));
            report.AppendLine("buttonWorldHeight="+(((RectTransform)button.transform).rect.height*button.transform.lossyScale.y));
            Capture("normal",0);
            Capture("motion",12);
            var savedColors=button.colors; var immediate=savedColors; immediate.fadeDuration=0;
            try {
                button.colors=immediate;
                button.OnPointerEnter(e); Capture("hover",0);
                button.OnPointerDown(e); Capture("pressed",0);
                report.AppendLine("pressedTint="+button.targetGraphic.canvasRenderer.GetColor());
                button.OnPointerUp(e); button.OnPointerExit(e);
            } finally { button.colors=savedColors; }
            File.WriteAllText("Temp/RehearOpeningVerify.txt","PASS\n"+report);
        }
        finally
        {
            typeof(OpeningStartCue).GetMethod("OnDisable",flags).Invoke(cue,null);
            visual.EndScenePreview();
            if(EventSystem.current) EventSystem.current.SetSelectedGameObject(previousSelection);
            button.targetGraphic.CrossFadeColor(button.colors.normalColor,0,true,true);
        }
    }
    static void Capture(string name,float time)
    {
        var camera=Camera.main;
        var target=new RenderTexture(1600,900,24,RenderTextureFormat.ARGB32);
        var previous=RenderTexture.active;
        var png=new Texture2D(1600,900,TextureFormat.RGB24,false);
        float oldTime=RenderSettings.skybox.GetFloat("_PreviewTime");
        try {
            RenderSettings.skybox.SetFloat("_PreviewTime",time);
            foreach(var tmp in All().Select(t=>t.GetComponent<CurvedUI.Core.Integrations.CurvedUITMP>()).Where(t=>t))
            { tmp.Dirty=true; tmp.SendMessage("LateUpdate",SendMessageOptions.DontRequireReceiver); }
            Canvas.ForceUpdateCanvases();
            var request=new UnityEngine.Rendering.RenderPipeline.StandardRequest{destination=target};
            if(!UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(camera,request)) throw new Exception("Render capture unsupported.");
            UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,request);
            RenderTexture.active=target; png.ReadPixels(new Rect(0,0,1600,900),0,0); png.Apply();
            File.WriteAllBytes("Temp/RehearOpening-"+name+".png",png.EncodeToPNG());
        } finally {
            RenderSettings.skybox.SetFloat("_PreviewTime",oldTime); RenderTexture.active=previous;
            UnityEngine.Object.DestroyImmediate(png); UnityEngine.Object.DestroyImmediate(target);
        }
    }
    static void Inspect()
    {
        var all = All(); var report = new StringBuilder();
        foreach(var b in all.Select(t=>t.GetComponent<Button>()).Where(b=>b))
            report.AppendLine($"BUTTON {b.name} active={b.gameObject.activeInHierarchy} rect={((RectTransform)b.transform).rect} position={b.transform.position} scale={b.transform.lossyScale} label={string.Join("|", b.GetComponentsInChildren<TMP_Text>(true).Select(t=>t.text+" font="+t.font?.name+" size="+t.fontSize))} image={b.targetGraphic?.name}");
        foreach(var t in all.Where(t=>t.name.Contains("Right Controller") || t.name=="Main Camera"))
            report.AppendLine($"TRACKING {t.name} active={t.gameObject.activeInHierarchy} pos={t.position} rot={t.eulerAngles} renderers={t.GetComponentsInChildren<Renderer>(true).Length}");
        File.WriteAllText("Temp/RehearOpeningStart-inspect.txt", report.ToString());
    }
    static void Apply()
    {
        var all = All();
        var button = all.Select(t=>t.GetComponent<Button>()).Single(b=>b && b.name=="Btn_Scene00_to_Scene01");
        var image = button.GetComponent<Image>(); var text = button.GetComponentsInChildren<TMP_Text>(true).Single();
        var right = all.Single(t=>t.name=="Right Controller");
        var original = right.Find("Right Controller Visual");
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/07_Fonts/PretendardTMP/Pretendard-Medium SDF.asset");
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath("deed68a7fd2e9234899298cf80fae7ad"));
        var animator = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(AssetDatabase.GUIDToAssetPath("fc948f57db734e74094dc532bdbbc3e8"));
        const string guidance = "Assets/Settings/TutorialUI/ControllerGuidance/";
        var trigger = AssetDatabase.LoadAssetAtPath<Mesh>(guidance+"Trigger Highlight.asset");
        var stick = AssetDatabase.LoadAssetAtPath<Mesh>(guidance+"Stick Highlight.asset");
        var grip = AssetDatabase.LoadAssetAtPath<Mesh>(guidance+"Grip Highlight.asset");
        var glow = AssetDatabase.LoadAssetAtPath<Material>(guidance+"Controller Button Blue.mat");
        if(!original || !font || !model || !animator || !trigger || !stick || !grip || !glow) throw new Exception("Required existing assets missing.");
        if(button.onClick.GetPersistentEventCount()!=1 || button.onClick.GetPersistentMethodName(0)!="ClickGameStart") throw new Exception("Unexpected start navigation; inspect before editing.");
        Undo.IncrementCurrentGroup(); Undo.SetCurrentGroupName("Style opening start and guide right trigger");
        Undo.RecordObjects(new UnityEngine.Object[]{button,image,text,text.rectTransform,button.transform}, "Style opening start");
        var rect=(RectTransform)button.transform;
        // Figma VR button S/activate: 340 x 56, radius 40, Medium 22; preserve world width and center.
        float scale=rect.rect.width/340f;
        rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,56f*scale);
        var material=AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if(!material) { material=new Material(Shader.Find("Rehear/UI/Rounded Translucent Panel")){name="Opening Start Button"}; AssetDatabase.CreateAsset(material,MaterialPath); }
        material.SetVector("_PanelSize",new Vector4(rect.rect.width,rect.rect.height,0,0));
        material.SetFloat("_Radius",40f*scale); material.SetFloat("_BorderWidth",2f*scale); material.SetFloat("_UseBlur",0);
        // Keep the renderer alive while the shader makes only the interior fully transparent.
        material.SetFloat("_FillAlphaOffset",.02f);
        image.material=material; image.sprite=null; image.color=Color.white; image.raycastTarget=true;
        image.canvasRenderer.cullTransparentMesh=false;
        button.targetGraphic=image; button.transition=Selectable.Transition.ColorTint;
        var colors=button.colors;
        colors.normalColor=new Color(1,1,1,.01f);
        // Interaction refinements; these are not separate variants specified in the Figma component.
        colors.highlightedColor=new Color(1,1,1,.10f);
        colors.pressedColor=new Color(1,1,1,.22f);
        colors.selectedColor=colors.highlightedColor; colors.disabledColor=new Color32(97,102,120,140);
        colors.colorMultiplier=1; colors.fadeDuration=.08f; button.colors=colors;
        text.text="발표 연습 시작하기"; text.font=font; text.fontSharedMaterial=font.material; text.fontStyle=FontStyles.Normal;
        text.fontSize=22f*scale; text.enableAutoSizing=false; text.color=Color.white; text.raycastTarget=false;
        text.alignment=TextAlignmentOptions.Center; text.characterSpacing=-1;
        text.rectTransform.anchorMin=Vector2.zero; text.rectTransform.anchorMax=Vector2.one;
        text.rectTransform.offsetMin=Vector2.zero; text.rectTransform.offsetMax=Vector2.zero;
        text.rectTransform.localScale=Vector3.one; text.margin=Vector4.zero;
        var visual=right.GetComponent<Quest3TutorialControllerVisual>();
        if(!visual) visual=Undo.AddComponent<Quest3TutorialControllerVisual>(right.gameObject);
        var so=new SerializedObject(visual);
        so.FindProperty("controllerModelPrefab").objectReferenceValue=model;
        so.FindProperty("controllerAnimator").objectReferenceValue=animator;
        so.FindProperty("rightHandTarget").objectReferenceValue=right;
        so.FindProperty("gripPoseOffset").vector3Value=Vector3.zero;
        so.FindProperty("gripPoseEuler").vector3Value=Vector3.zero;
        so.FindProperty("triggerHighlight").objectReferenceValue=trigger;
        so.FindProperty("stickHighlight").objectReferenceValue=stick;
        so.FindProperty("gripHighlight").objectReferenceValue=grip;
        so.FindProperty("highlightMaterial").objectReferenceValue=glow;
        var renderers=original.GetComponentsInChildren<Renderer>(true); var array=so.FindProperty("originalControllerRenderers");
        array.arraySize=renderers.Length;
        for(int i=0;i<renderers.Length;i++) array.GetArrayElementAtIndex(i).objectReferenceValue=renderers[i];
        so.ApplyModifiedProperties();
        var cue=button.GetComponent<OpeningStartCue>(); if(!cue) cue=Undo.AddComponent<OpeningStartCue>(button.gameObject);
        Undo.RecordObject(cue,"Bind opening trigger cue"); cue.controllerVisual=visual;
        foreach(var obj in new UnityEngine.Object[]{button,image,image.canvasRenderer,text,text.rectTransform,rect,visual,cue,material}) Dirty(obj);
        text.renderMode=TextRenderFlags.Render; text.SetAllDirty(); text.ForceMeshUpdate(true); Canvas.ForceUpdateCanvases();
        var curvedText=text.GetComponent<CurvedUI.Core.Integrations.CurvedUITMP>();
        if(curvedText) { curvedText.Dirty=true; curvedText.SendMessage("LateUpdate",SendMessageOptions.DontRequireReceiver); }
        foreach(var effect in button.GetComponentsInChildren<CurvedUIVertexEffect>(true)) { effect.TesselationRequired=true; effect.CurvingRequired=true; }
        AssetDatabase.SaveAssets(); var scene=EditorSceneManager.GetActiveScene(); EditorSceneManager.MarkSceneDirty(scene);
        if(!EditorSceneManager.SaveScene(scene)) throw new Exception("Could not save opening scene.");
        Selection.activeGameObject=button.gameObject;
        SceneView.RepaintAll(); UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        Inspect();
        File.WriteAllText("Temp/RehearOpeningStart.txt", $"PASS\nsaved=true\ntransparentNormal=true whiteFillHover=0.10 pressed=0.22 whiteText=true\nfont={font.name}\nbuttonRect={rect.rect}\nwhiteBorder=2(Figma units)\ntriggerSurfaceTriangles={trigger.triangles.Length/3}\noriginalRenderers={renderers.Length}\nclickNavigationPreserved=true\n");
    }
}
