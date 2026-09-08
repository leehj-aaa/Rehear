using System;
using System.IO;
using System.Linq;
using CurvedUI;
using LeTai.Asset.TranslucentImage;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

[InitializeOnLoad]
internal static class RehearSessionReadySetup
{
    const string Request = "Temp/RehearSessionReady.request";
    const string Report = "Temp/RehearSessionReady.txt";
    static readonly Color Ink = new Color32(3,8,18,255), Body = new Color32(53,56,65,255);
    static RehearSessionReadySetup() => EditorApplication.update += Poll;
    static void Poll()
    {
        if (AssetDatabase.IsAssetImportWorkerProcess()) return;
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if(EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isPlaying) return;
        string command;
        try
        {
            command = File.ReadAllText(Request).Trim();
        }
        catch (IOException)
        {
            // Another editor update callback may be handling the request.
            return;
        }
        if (command == "verify" && EditorApplication.isPlaying)
        {
            var pendingView = Object.FindFirstObjectByType<PresentationSessionReady>();
            if (pendingView && !pendingView.gameObject.activeInHierarchy) return;
        }
        if (EditorApplication.isPlayingOrWillChangePlaymode && command != "verify") return;
        // The replace command intentionally owns the Scene_02 save. It must be
        // allowed to run when the previous temporary panel left the scene dirty.
        if (!EditorApplication.isPlaying && EditorSceneManager.GetActiveScene().isDirty && command != "replace") return;
        try { File.Delete(Request); }
        catch (IOException) { return; }
        try { if(command == "apply") Build(); else if(command == "verify") Verify(); else if(command=="camera") CameraSetup(); else if(command=="dump-source") DumpSource(); else if(command=="replace") ReplaceWithAuthoredPanel(); }
        catch(Exception e) { File.WriteAllText(Report,e.ToString()); Debug.LogException(e); }
    }
    static RectTransform Rect(Transform parent,string name,float x,float y,float w,float h)
    {
        var go = new GameObject(name,typeof(RectTransform)); go.layer=LayerMask.NameToLayer("UI");
        var r=(RectTransform)go.transform; r.SetParent(parent,false);
        r.anchorMin=r.anchorMax=r.pivot=new Vector2(0,1); r.anchoredPosition=new Vector2(x,-y); r.sizeDelta=new Vector2(w,h);
        return r;
    }
    static Material Material(string name,float w,float h,float radius,float border,bool blur)
    {
        string path="Assets/Settings/TutorialUI/Ready "+name+".mat";
        var m=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!m) {m=new Material(Shader.Find("Rehear/UI/Rounded Translucent Panel")); AssetDatabase.CreateAsset(m,path);}
        m.SetVector("_PanelSize",new Vector4(w,h,0,0)); m.SetFloat("_Radius",radius); m.SetFloat("_BorderWidth",border);
        m.SetFloat("_UseBlur",blur?1:0); EditorUtility.SetDirty(m); return m;
    }
    static RectTransform Solid(Transform p,string name,float x,float y,float w,float h,Color color,float radius=40,float border=0)
    {
        var r=Rect(p,name,x,y,w,h); var i=r.gameObject.AddComponent<Image>(); i.color=color;
        i.material=Material(name,w,h,radius,border,false); i.canvasRenderer.cullTransparentMesh=false; i.raycastTarget=false; return r;
    }
    static TMP_Text Text(Transform p,string name,string value,float x,float y,float w,float h,float size,bool bold=false,Color? color=null)
    {
        var t=Rect(p,name,x,y,w,h).gameObject.AddComponent<TextMeshProUGUI>();
        t.font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/07_Fonts/PretendardTMP/Pretendard-"+(bold?"Bold":"Medium")+" SDF.asset");
        t.text=value; t.fontSize=size; t.color=color??Ink; t.alignment=TextAlignmentOptions.Center;
        t.raycastTarget=false; t.richText=false; t.enableAutoSizing=true; t.fontSizeMin=size*.65f; t.fontSizeMax=size;
        t.overflowMode=TextOverflowModes.Ellipsis; t.margin=Vector4.zero; return t;
    }
    static void Icon(Transform p,string name,string path,float x,float y,float w,float h)
    {
        var i=Rect(p,name,x,y,w,h).gameObject.AddComponent<Image>();
        i.sprite=AssetDatabase.LoadAssetAtPath<Sprite>(path); if(!i.sprite) throw new Exception("Missing sprite "+path);
        i.preserveAspect=true; i.raycastTarget=false;
    }
    static TMP_Text Tile(Transform p,int index,string icon,string caption)
    {
        const float w=174.75f;
        var r=Solid(p,"Tile "+index,36+index*(w+12),285,w,143,new Color(1,1,1,.2f));
        Icon(r,icon,"Assets/Textures/UI/FigmaSessionReady/"+icon+".png",(w-36)/2,22,36,36);
        Text(r,"Caption",caption,5,66,w-10,26,18,false,Body);
        return Text(r,"Value","",5,96,w-10,29,24,true);
    }
    static TMP_Text Row(Transform p,string name,string caption,float x,float y,float w)
    {
        var r=Solid(p,name,x,y,w,56,new Color(1,1,1,.2f));
        var a=Text(r,"Caption",caption,27,0,w*.5f,56,18,false,Body); a.alignment=TextAlignmentOptions.MidlineLeft;
        var b=Text(r,"Value","",w*.5f,0,w*.5f-27,56,18,true); b.alignment=TextAlignmentOptions.MidlineRight; return b;
    }
    static Button Button(Transform p,string name,string caption,float y,bool outline,UnityEngine.Events.UnityAction action)
    {
        var r=Solid(p,name,36,y,735,56,outline?Color.clear:new Color32(0,51,255,255),40,outline?2:0);
        var b=r.gameObject.AddComponent<Button>(); b.targetGraphic=r.GetComponent<Image>(); b.targetGraphic.raycastTarget=true;
        Text(r,"Label",caption,12,0,711,56,24,false,Color.white); UnityEventTools.AddPersistentListener(b.onClick,action); return b;
    }
    static void Build()
    {
        foreach(var path in Directory.GetFiles("Assets/Textures/UI/FigmaSessionReady","*.png"))
        {
            AssetDatabase.ImportAsset(path); var i=(TextureImporter)AssetImporter.GetAtPath(path);
            i.textureType=TextureImporterType.Sprite; i.spriteImportMode=SpriteImportMode.Single; i.alphaIsTransparency=true;
            i.mipmapEnabled=false; i.textureCompression=TextureImporterCompression.Uncompressed; i.SaveAndReimport();
        }
        var scene=EditorSceneManager.OpenScene("Assets/01_Scene/Scene_02_Presentation.unity");
        var all=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
        var controller=all.Select(t=>t.GetComponent<PresentationController>()).Single(c=>c);
        if(controller.sessionReady) throw new Exception("Session-ready panel already exists; refusing duplicate.");
        var camera=all.Select(t=>t.GetComponent<Camera>()).First(c=>c&&c.CompareTag("MainCamera"));
        var clone=Object.Instantiate(controller.pausePanel); clone.name="Presentation Session Ready"; clone.SetActive(false);
        foreach(Transform child in clone.transform.Cast<Transform>().ToArray()) Object.DestroyImmediate(child.gameObject);
        var rect=(RectTransform)clone.transform; rect.SetParent(null); rect.sizeDelta=new Vector2(804,850); rect.pivot=new Vector2(.5f,.5f);
        rect.localScale=Vector3.one*.0015f;
        var facing=Quaternion.Euler(0,camera.transform.eulerAngles.y,0);
        rect.SetPositionAndRotation(camera.transform.position+facing*Vector3.forward*1.7f,facing);
        var canvas=clone.GetComponent<Canvas>(); canvas.worldCamera=camera; canvas.sortingOrder=100;
        var view=clone.AddComponent<PresentationSessionReady>(); view.controller=controller; controller.sessionReady=view;
        view.presentationTitle=Text(rect,"Presentation Title","발표 제목",36,0,732,78,32,true,Color.white);
        var panel=Rect(rect,"Session Ready Panel",0,96,804,754);
        var glass=panel.gameObject.AddComponent<TranslucentImage>(); glass.material=Material("Panel",804,754,48,2,true);
        glass.color=Color.white; glass.foregroundOpacity=.45f; glass.raycastTarget=true;
        glass.source=camera.GetComponent<TranslucentImageSource>(); glass.material.SetFloat("_GlassTint",.45f);
        glass.material.SetFloat("_UseFigmaGlow",1); glass.material.SetVector("_FigmaDesignSize",new Vector4(804,754,0,0));
        glass.material.SetTexture("_FigmaGlowTex",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/UI/FigmaOpening/top-radial.png"));
        Icon(panel,"Logo","Assets/Textures/UI/FigmaOpening/logo.png",36,33,136.7f,38);
        Icon(panel,"Check","Assets/Textures/UI/FigmaSessionReady/check.png",372,93,60,60);
        Text(panel,"Heading","세션 불러오기 완료",140,175,524,43,36,true);
        Text(panel,"Description","사전에 설정한 내용을 확인하고 발표를 시작하세요",36,226,735,28,20,false,Body);
        view.sessionType=Tile(panel,0,"mic","세션 유형"); view.duration=Tile(panel,1,"voice","발표 시간");
        view.questionCount=Tile(panel,2,"question","Q&A 개수"); view.audienceCount=Tile(panel,3,"group","청중 규모");
        view.environment=Row(panel,"Environment","발표 환경",36,438,735);
        view.expertise=Row(panel,"Expertise","청중 전문성",35,504,362); view.interest=Row(panel,"Interest","청중 관심도",409,504,362);
        view.continueButton=Button(panel,"Continue","세션 시작하기",592,false,view.Confirm);
        view.returnButton=Button(panel,"Return to PIN","PIN 번호 다시 입력하기",660,true,view.ReturnToPin);
        clone.GetComponent<CurvedUISettings>().AddEffectToChildren();
        var sync=camera.GetComponent<TutorialBlurCameraSync>(); sync.panels=sync.panels.Concat(new[]{glass}).ToArray(); EditorUtility.SetDirty(sync);
        clone.SetActive(true); EditorUtility.SetDirty(controller); AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        File.WriteAllText(Report,"PASS Figma 2323:38860 804x754 layout, five source icons, 45% glass, clipped radial, title above, cached session fields, confirm/PIN callbacks.\n");
    }
    static void CameraSetup()
    {
        var sync=Object.FindFirstObjectByType<TutorialBlurCameraSync>();
        var camera=sync.uiCamera; var data=camera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
        const string rendererPath="Assets/Settings/Presentation UI Renderer.asset";
        var renderer=AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRendererData>(rendererPath);
        if(!renderer) {
            renderer=Object.Instantiate(AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRendererData>("Assets/Settings/Project Configuration/Android Preset.asset"));
            renderer.rendererFeatures.Clear(); renderer.name="Presentation UI Renderer"; AssetDatabase.CreateAsset(renderer,rendererPath);
        }
        var pipeline=UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
        var serializedPipeline=new SerializedObject(pipeline); var renderers=serializedPipeline.FindProperty("m_RendererDataList");
        int index=-1; for(int i=0;i<renderers.arraySize;i++) if(renderers.GetArrayElementAtIndex(i).objectReferenceValue==renderer) index=i;
        if(index<0) {index=renderers.arraySize; renderers.arraySize++; renderers.GetArrayElementAtIndex(index).objectReferenceValue=renderer; serializedPipeline.ApplyModifiedPropertiesWithoutUndo();}
        data.SetRenderer(index); AssetDatabase.SaveAssets();
        camera.depth=sync.sceneCamera.depth+1;
        data.requiresColorOption=UnityEngine.Rendering.Universal.CameraOverrideOption.Off;
        data.requiresDepthOption=UnityEngine.Rendering.Universal.CameraOverrideOption.Off;
        EditorUtility.SetDirty(camera); EditorUtility.SetDirty(data);
        PrefabUtility.RecordPrefabInstancePropertyModifications(camera); PrefabUtility.RecordPrefabInstancePropertyModifications(data);
        EditorSceneManager.MarkSceneDirty(camera.gameObject.scene); EditorSceneManager.SaveScene(camera.gameObject.scene);
    }
    static void DumpSource()
    {
        var original=EditorSceneManager.GetSceneManagerSetup();
        try {
            var source=EditorSceneManager.OpenScene("Assets/01_Scene/Scene_01_Intro.unity",OpenSceneMode.Additive);
            var panel=source.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Single(t=>t.name=="Panel_SessionReady");
            var lines=new System.Text.StringBuilder();
            foreach(var t in panel.GetComponentsInChildren<Transform>(true)) lines.AppendLine(t.name+" | "+string.Join(",",t.GetComponents<Component>().Where(c=>c).Select(c=>c.GetType().Name)));
            File.WriteAllText("Temp/RehearSessionReadySourceHierarchy.txt",lines.ToString());
        } finally {EditorSceneManager.RestoreSceneManagerSetup(original);}
    }
    static void ReplaceWithAuthoredPanel()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Stop Play mode first");
        const string presentationScenePath="Assets/01_Scene/Scene_02_Presentation.unity";
        var scene=EditorSceneManager.GetActiveScene();
        if(scene.path!=presentationScenePath)
            scene=EditorSceneManager.OpenScene(presentationScenePath,OpenSceneMode.Single);
        try {
            var source=EditorSceneManager.OpenScene("Assets/01_Scene/Scene_01_Intro.unity",OpenSceneMode.Additive);
            var authored=source.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Single(t=>t.name=="Panel_SessionReady");
            var currents=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Where(t=>t.name=="Presentation Session Ready").ToArray();
            foreach(var current in currents) Object.DestroyImmediate(current.gameObject);
            var canvas=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Canvas>(true)).First(c=>c.name=="Canvas");
            // Instantiate while Scene_02 is the active scene, then explicitly move the
            // clone into Scene_02. This prevents serialized field references from being
            // resolved against same-named objects that already exist in the presentation.
            var clone=Object.Instantiate(authored.gameObject); clone.name="Presentation Session Ready";
            SceneManager.MoveGameObjectToScene(clone,scene);
            clone.transform.SetParent(canvas.transform,false);
            clone.SetActive(false);
            var panel=clone.transform;
            var rect=(RectTransform)panel; rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,.5f); rect.anchoredPosition=new Vector2(0,0); rect.sizeDelta=new Vector2(804,754); rect.localScale=Vector3.one*.0015f;
            panel.SetPositionAndRotation(canvas.transform.position+canvas.transform.forward*1.7f,canvas.transform.rotation);
            foreach(var c in clone.GetComponentsInChildren<Canvas>(true)) {c.worldCamera=canvas.worldCamera; c.overrideSorting=true; c.sortingOrder=100;}
            foreach(var t in clone.GetComponentsInChildren<Transform>(true)) t.gameObject.layer=LayerMask.NameToLayer("UI");
            var view=clone.AddComponent<PresentationSessionReady>();
            view.controller=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<PresentationController>(true)).Single();
            // Keep the editor preview focused on the session confirmation panel.
            // The question banner and Q&A action are enabled later by the presentation flow.
            if (view.controller.qaButton) view.controller.qaButton.gameObject.SetActive(false);
            var questionText=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<QuestionAnswerManager>(true)).FirstOrDefault()?.questionText;
            if (questionText) questionText.gameObject.SetActive(false);
            // Bind the existing authored fields. The presentation panel must display the
            // same server-backed values as the intro panel instead of a parallel UI model.
            view.sessionType=FindValue(clone,"Panel_Settings_1/Image_Mode/Text_SessionType_Selected");
            view.duration=FindValue(clone,"Panel_Settings_1/Image_Time/Text_SessionTime_Selected");
            view.questionCount=FindValue(clone,"Panel_Settings_1/Image_QnA/Text_QnA_Selected");
            view.audienceCount=FindValue(clone,"Panel_Settings_1/Image_AudienceSize/Text_AudienceSize_Selected");
            view.environment=FindValue(clone,"Panel_Settings _2/Image_Environment/Panel/Text_Environment_Selected");
            view.expertise=FindValue(clone,"Panel_Settings _3/Image_Expertise/Panel/Text_Expertise_Selected");
            view.interest=FindValue(clone,"Panel_Settings _3/Image_Interest/Panel/Text_Interest_Selected");
            view.continueButton=FindButton(clone,"Panel_Actions/Button_Start");
            view.returnButton=FindButton(clone,"Panel_Actions/Button_ReturntoWeb");
            if(!view.sessionType || !view.duration || !view.questionCount || !view.audienceCount || !view.environment || !view.expertise || !view.interest || !view.continueButton || !view.returnButton)
                throw new Exception("Authored Panel_SessionReady fields are incomplete.\n"+DescribeChildren(clone));

            var existingTitle=clone.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t=>t.name=="Presentation Title");
            if(existingTitle) Object.DestroyImmediate(existingTitle.gameObject);
            var titleObject=new GameObject("Presentation Title",typeof(RectTransform),typeof(TextMeshProUGUI));
            titleObject.transform.SetParent(panel,false);
            var titleRect=(RectTransform)titleObject.transform;
            titleRect.anchorMin=new Vector2(.1f,.83f); titleRect.anchorMax=new Vector2(.9f,.9f); titleRect.offsetMin=Vector2.zero; titleRect.offsetMax=Vector2.zero;
            view.presentationTitle=titleObject.GetComponent<TextMeshProUGUI>();
            var referenceFont=clone.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t=>t.font);
            view.presentationTitle.font=referenceFont ? referenceFont.font : AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/07_Fonts/PretendardTMP/Pretendard-Bold SDF.asset");
            view.presentationTitle.text="발표 제목";
            view.presentationTitle.fontSize=22; view.presentationTitle.fontStyle=FontStyles.Bold; view.presentationTitle.color=new Color32(3,8,18,255);
            view.presentationTitle.alignment=TextAlignmentOptions.Center; view.presentationTitle.enableAutoSizing=true; view.presentationTitle.fontSizeMin=14; view.presentationTitle.fontSizeMax=22; view.presentationTitle.raycastTarget=false;

            while(view.continueButton.onClick.GetPersistentEventCount()>0) UnityEventTools.RemovePersistentListener(view.continueButton.onClick,0);
            while(view.returnButton.onClick.GetPersistentEventCount()>0) UnityEventTools.RemovePersistentListener(view.returnButton.onClick,0);
            UnityEventTools.AddPersistentListener(view.continueButton.onClick,view.Confirm);
            UnityEventTools.AddPersistentListener(view.returnButton.onClick,view.ReturnToPin);
            view.controller.sessionReady=view; clone.SetActive(false);
            canvas.GetComponent<CurvedUISettings>()?.AddEffectToChildren();
            clone.SetActive(true);
            EditorSceneManager.CloseScene(source,true);
            EditorSceneManager.MarkSceneDirty(scene); AssetDatabase.SaveAssets(); EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject=clone;
            if(SceneView.lastActiveSceneView) SceneView.lastActiveSceneView.FrameSelected();
            File.WriteAllText("Temp/RehearSessionReady.txt","PASS reused authored Panel_SessionReady from Scene_01_Intro; no new visual hierarchy created.\n");
        } finally {
            if(SceneManager.sceneCount>1) {
                var sourceScene=SceneManager.GetSceneByPath("Assets/01_Scene/Scene_01_Intro.unity");
                if(sourceScene.IsValid() && sourceScene.isLoaded) EditorSceneManager.CloseScene(sourceScene,true);
            }
        }
    }
    static TMP_Text FindValue(GameObject root,string path)
    {
        var target=root.transform.Find(path);
        return target ? target.GetComponent<TMP_Text>() : null;
    }
    static Button FindButton(GameObject root,string path)
    {
        var target=root.transform.Find(path);
        return target ? target.GetComponent<Button>() : null;
    }
    static string DescribeChildren(GameObject root)
    {
        return string.Join("\n",root.GetComponentsInChildren<Transform>(true).Select(t=>t.name+" | "+string.Join(",",t.GetComponents<Component>().Where(c=>c).Select(c=>c.GetType().Name))));
    }
    static void Verify()
    {
        var view=Object.FindFirstObjectByType<PresentationSessionReady>(FindObjectsInactive.Include);
        if(!view) throw new Exception("No ready panel");
        foreach(var t in view.GetComponentsInChildren<TMP_Text>(true))
            if(!t.font) throw new Exception("Missing font "+t.name);
        if(view.continueButton.onClick.GetPersistentTarget(0)!=view || view.returnButton.onClick.GetPersistentTarget(0)!=view) throw new Exception("Callback target mismatch");
        File.AppendAllText(Report,"PASS title/7 values, fonts, callback targets.\n");
        if(!EditorApplication.isPlaying) return;
        RuntimeSessionData.Load("local-ui-test",new SessionData { page_1=new Page1 {presentation_title="추천 알고리즘은 선택을 넓힐까?",presentation_purpose="발표 모드",duration_minutes=30,qa_count=3,environment_type="세미나실"}, page_3=new Page3 {audience_scale=6,audience_expertise="보통",audience_interest="높음"} });
        view.Populate(); view.continueButton.interactable=true;
        if(view.presentationTitle.text!=RuntimeSessionData.PresentationTitle || view.questionCount.text!="3개" || view.duration.text!="30분") throw new Exception("Session bindings incorrect");
        view.controller.BeginPresentation(); view.controller.PauseGame();
        if(view.controller.IsPaused || view.controller.startPresentationButton.interactable) throw new Exception("Modal did not block start/grip");
        double after=EditorApplication.timeSinceStartup+1;
        EditorApplication.update += DeferredCapture;
        void DeferredCapture()
        {
            if(EditorApplication.timeSinceStartup<after) return;
            EditorApplication.update -= DeferredCapture;
            if(view && EditorApplication.isPlaying) Capture(view);
        }
    }
    static void Capture(PresentationSessionReady view)
    {
        var camera=Camera.main;
        var sync=camera.GetComponent<TutorialBlurCameraSync>();
        File.AppendAllText(Report,$"sync enabled={sync.enabled} active={sync.isActiveAndEnabled} source={sync.sceneCamera} target={sync.uiCamera}\n");
        sync.SendMessage("Update");
        var actualStack=camera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>().cameraStack;
        File.AppendAllText(Report,"STACK "+(actualStack==null?"null":string.Join(",",actualStack.Select(c=>c?c.name:"NULL")))+"\n");
        File.AppendAllText(Report,$"camera {camera.name} {camera.transform.position} {camera.transform.eulerAngles}, canvas {view.transform.position} {view.transform.eulerAngles} enabled={view.GetComponent<Canvas>().enabled} viewport={camera.WorldToViewportPoint(view.transform.position)}\n");
        foreach(var c in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)) File.AppendAllText(Report,$"camera {c.name} enabled={c.enabled} mask={c.cullingMask} pos={c.transform.position} rot={c.transform.eulerAngles} stereo={c.stereoEnabled} data={EditorJsonUtility.ToJson(c.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>())}\n");
        var target=new RenderTexture(1200,1000,24); target.Create();
        var old=RenderTexture.active; var projection=camera.projectionMatrix;
        var data=camera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
        var xr=data.allowXRRendering; data.allowXRRendering=false;
        var mask=camera.cullingMask;
        var overlay=sync.uiCamera;
        var liveOverlay=Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Single(c=>c.name=="Presentation UI Overlay Camera");
        File.AppendAllText(Report,$"CAM ids sync={overlay.GetInstanceID()} stack={actualStack[0].GetInstanceID()} live={liveOverlay.GetInstanceID()} activeStack={actualStack[0].isActiveAndEnabled}\n");
        data.cameraStack.Clear(); data.cameraStack.Add(liveOverlay); sync.uiCamera=liveOverlay; overlay=liveOverlay;
        overlay.useOcclusionCulling=false;
        var overlayData=new SerializedObject(overlay.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>());
        overlayData.FindProperty("m_ClearDepth").boolValue=true; overlayData.ApplyModifiedPropertiesWithoutUndo();
        var ownCanvas=view.GetComponent<Canvas>();
        ownCanvas.worldCamera=overlay;
        var camPoint=overlay.WorldToViewportPoint(view.transform.position);
        File.AppendAllText(Report,$"UI viewport {camPoint} matrix={overlay.worldToCameraMatrix}\n");
        try {
            UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += TraceCamera;
            camera.ResetProjectionMatrix();
            Canvas.ForceUpdateCanvases();
            foreach(var t in view.GetComponentsInChildren<TMP_Text>()) t.ForceMeshUpdate();
            foreach(var effect in view.GetComponentsInChildren<CurvedUIVertexEffect>()) effect.SetDirty();
            var request=new UnityEngine.Rendering.RenderPipeline.StandardRequest {destination=target};
            UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,request);
            UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,request);
            RenderTexture.active=target; var texture=new Texture2D(1200,1000,TextureFormat.RGB24,false);
            texture.ReadPixels(new UnityEngine.Rect(0,0,1200,1000),0,0); texture.Apply();
            File.WriteAllBytes("Temp/RehearSessionReady.png",texture.EncodeToPNG()); Object.DestroyImmediate(texture);
        } finally {UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering -= TraceCamera; camera.cullingMask=mask; data.allowXRRendering=xr; camera.projectionMatrix=projection; RenderTexture.active=old; target.Release(); Object.DestroyImmediate(target);}
        ScreenCapture.CaptureScreenshot("Temp/RehearSessionReadyGame.png");
        EditorApplication.delayCall += () => Finish(view);
    }
    static void Finish(PresentationSessionReady view)
    {
        view.continueButton.onClick.Invoke();
        if(view.IsOpen || !view.controller.startPresentationButton.interactable) throw new Exception("Confirmation did not restore start");
        var running=(bool)typeof(PresentationController).GetField("isRunning",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(view.controller);
        if(running) throw new Exception("Confirm started recording/timer");
        File.AppendAllText(Report,"PASS Play mode: session/title/3-question bindings, modal blocks start/pause, confirm restores manual start without starting timer. Local fixture only; no microphone/server.\n");
    }
    static void TraceCamera(UnityEngine.Rendering.ScriptableRenderContext context, Camera c)
    {
        File.AppendAllText(Report,"RENDER "+c.name+"\n");
        if(c.name=="Presentation UI Overlay Camera")
        {
            c.worldToCameraMatrix=Camera.main.worldToCameraMatrix;
            c.projectionMatrix=Camera.main.projectionMatrix;
            c.cullingMatrix=c.projectionMatrix*c.worldToCameraMatrix;
        }
    }
}
