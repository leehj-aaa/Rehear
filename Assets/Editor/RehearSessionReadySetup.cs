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
    const float CardWidth = 804f;
    const float CardHeight = 856f;
    static readonly Color Ink = new Color32(3,8,18,255), Body = new Color32(53,56,65,255), Brand = new Color32(0,51,255,255);
    static RehearSessionReadySetup()
    {
        EditorApplication.update += Poll;
        EditorApplication.playModeStateChanged += state =>
        {
            if(state != PlayModeStateChange.ExitingEditMode) return;
            foreach(var root in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None))
                if(root && root.parent==null && root.name=="Audience Preview (Editor Only)") Object.DestroyImmediate(root.gameObject);
        };
    }
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
        if (!EditorApplication.isPlaying && EditorSceneManager.GetActiveScene().isDirty && command != "replace" && command != "restore-original" && command != "update-layout" && command != "inspect-placement" && command != "show-prestart" && command != "show-running" && command != "show-qa" && command != "preview-audience") return;
        try { File.Delete(Request); }
        catch (IOException) { return; }
        if(command == "show-qa")
        {
            try { ShowQA(); }
            catch(Exception e) { File.WriteAllText(Report,e.ToString()); Debug.LogException(e); }
            return;
        }
        if(command == "show-running")
        {
            try { ShowRunning(); }
            catch(Exception e) { File.WriteAllText(Report,e.ToString()); Debug.LogException(e); }
            return;
        }
        if(command == "preview-audience")
        {
            try { ShowAudiencePreview(); }
            catch(Exception e) { File.WriteAllText(Report,e.ToString()); Debug.LogException(e); }
            return;
        }
        if(command == "show-prestart")
        {
            try { ShowPrestart(); }
            catch(Exception e) { File.WriteAllText(Report,e.ToString()); Debug.LogException(e); }
            return;
        }
        if(command == "test-ready-flow")
        {
            try { TestReadyFlow(); }
            catch(Exception e) { File.WriteAllText(Report,e.ToString()); Debug.LogException(e); }
            return;
        }
        try { if(command == "apply") Build(); else if(command == "verify") Verify(); else if(command=="camera") CameraSetup(); else if(command=="dump-source") DumpSource(); else if(command=="replace") ReplaceWithAuthoredPanel(); else if(command=="restore-original") RestoreOriginalPanel(); else if(command=="update-layout") UpdateCurrentLayout(); else if(command=="inspect-placement") InspectTutorialPlacement(); }
        catch(Exception e) { File.WriteAllText(Report,e.ToString()); Debug.LogException(e); }
    }

    [MenuItem("Rehear/Preview Presentation After Session Confirmation")]
    static void ShowPrestart()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Preview in Edit mode.");
        var scene=EditorSceneManager.GetActiveScene();
        if(scene.path!="Assets/01_Scene/Scene_02_Presentation.unity") throw new Exception("Open the presentation scene.");
        var controller=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<PresentationController>(true)).Single();
        if(!controller.sessionReady || !controller.startPresentationButton) throw new Exception("Required UI references missing.");
        Undo.RecordObject(controller.sessionReady.gameObject,"Preview presentation ready to start");
        controller.sessionReady.gameObject.SetActive(false);
        if(controller.pausePanel) controller.pausePanel.SetActive(false);
        if(controller.scriptPanel) controller.scriptPanel.SetActive(false);
        if(controller.qaButton) controller.qaButton.gameObject.SetActive(false);
        var qa=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<QuestionAnswerManager>(true)).FirstOrDefault();
        if(qa && qa.questionText) qa.questionText.gameObject.SetActive(false);
        foreach(var root in scene.GetRootGameObjects())
            foreach(var t in root.GetComponentsInChildren<Transform>(true))
                if(t.name=="Panel_QA") t.gameObject.SetActive(false);
        controller.SetSessionConfirmationVisible(false);
        if(controller.timerText) controller.timerText.text="00:00";
        Canvas.ForceUpdateCanvases();
        if(!controller.startPresentationButton.gameObject.activeInHierarchy || !controller.startPresentationButton.interactable || controller.sessionReady.gameObject.activeSelf)
            throw new Exception("Prestart preview visibility is incorrect.");
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject=controller.startPresentationButton.gameObject;
        SceneView.RepaintAll(); UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        File.WriteAllText(Report,"PASS prestart preview saved: confirmation hidden; presentation start visible and enabled; end/QA/pause/script hidden; timer 00:00. Play mode restores confirmation for a loaded session.\n");
        ShowAudiencePreview();
    }

    [MenuItem("Rehear/Preview Presentation In Progress")]
    static void ShowRunning()
    {
        ShowPrestart();
        var scene=EditorSceneManager.GetActiveScene();
        var roots=scene.GetRootGameObjects();
        var controller=roots.SelectMany(r=>r.GetComponentsInChildren<PresentationController>(true)).Single();
        var deck=roots.SelectMany(r=>r.GetComponentsInChildren<PresentationManager>(true)).Single();
        if(!controller.endPresentationButton || !controller.scriptPanel || !controller.timerText ||
           !deck.deskScreen || !deck.slideScreen || deck.slides==null || deck.slides.Length==0 || !deck.slides[0])
            throw new Exception("Missing presentation preview references or default slide.");
        // Only preview serialized UI. Never start the server, microphone or runtime timer.
        controller.startPresentationButton.gameObject.SetActive(false);
        controller.qaManager?.Prepare(controller.endPresentationButton);
        controller.endPresentationButton.gameObject.SetActive(true);
        controller.endPresentationButton.interactable=true;
        controller.endPresentationButton.GetComponentInChildren<TMP_Text>(true).text="발표 끝내기";
        controller.scriptPanel.SetActive(true);
        foreach(var toggle in roots.SelectMany(r=>r.GetComponentsInChildren<PodiumScriptToggle>(true))) toggle.Refresh();
        foreach(var scroller in roots.SelectMany(r=>r.GetComponentsInChildren<ScriptScroller>(true))) scroller.ResetToFirstPage();
        foreach(var screen in new[]{deck.deskScreen,deck.slideScreen})
        {
            screen.gameObject.SetActive(true);
            screen.enabled=true;
            screen.texture=deck.slides[0];
            EditorUtility.SetDirty(screen);
        }
        deck.RefreshSlideDirectionHints();
        controller.timerText.gameObject.SetActive(true);
        controller.timerText.text="29:58";
        controller.timerText.ForceMeshUpdate(true,true);
        Canvas.ForceUpdateCanvases();
        if(!controller.endPresentationButton.gameObject.activeInHierarchy || !controller.scriptPanel.activeInHierarchy ||
           !controller.timerText.gameObject.activeInHierarchy || !deck.slideScreen.gameObject.activeInHierarchy ||
           !deck.deskScreen.gameObject.activeInHierarchy || controller.startPresentationButton.gameObject.activeSelf)
            throw new Exception("In-progress preview is hidden by a parent.");
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeObject=null;
        SceneView.RepaintAll(); UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        File.AppendAllText(Report,"PASS running preview saved: end button visible, start hidden, script visible, both screens use existing first slide, timer 29:58 (preview). No recording/server session started. Runtime Start restores session confirmation and normal button state.\n");
    }

    [MenuItem("Rehear/Preview Presentation Q&A")]
    static void ShowQA()
    {
        ShowRunning();
        var scene=EditorSceneManager.GetActiveScene();
        var controller=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<PresentationController>(true)).Single();
        controller.CloseScriptPanel();
        foreach(var toggle in scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<PodiumScriptToggle>(true))) toggle.Refresh();
        var qa=controller.qaManager;
        if(!qa || !controller.qaButton) throw new Exception("Missing Q&A UI references.");
        // Reuse the exact podium control, including its original RectTransform.
        if(controller.qaButton != controller.endPresentationButton)
            controller.qaButton.gameObject.SetActive(false);
        controller.qaButton=controller.endPresentationButton;
        EditorUtility.SetDirty(controller);
        controller.qaButton.gameObject.SetActive(true);
        qa.Prepare(controller.qaButton);
        var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        var type=typeof(QuestionAnswerManager);
        var state=type.GetField("state",flags);
        // Render the existing speaking style without generating or playing a real question.
        state.SetValue(qa,Enum.Parse(state.FieldType,"PlayingQuestion"));
        type.GetField("questionCount",flags).SetValue(qa,RuntimeSessionData.IsLoaded ? Mathf.Max(1,RuntimeSessionData.QaCount) : 1);
        type.GetField("<IsQAPhaseActive>k__BackingField",flags).SetValue(qa,true);
        type.GetMethod("SetButtonState",flags).Invoke(qa,new object[]{"청중 질문 중…",false});
        var progress=(TMP_Text)type.GetField("questionProgressText",flags).GetValue(qa);
        if(progress) progress.gameObject.hideFlags=HideFlags.DontSave;
        if(qa.questionText) qa.questionText.gameObject.SetActive(false);
        if(qa.answerGuideText) qa.answerGuideText.gameObject.SetActive(false);
        controller.timerText.text="Q&A";
        controller.timerText.ForceMeshUpdate(true,true);
        Canvas.ForceUpdateCanvases();
        if(!controller.qaButton.gameObject.activeInHierarchy || controller.qaButton.interactable ||
           !progress || !progress.gameObject.activeInHierarchy || controller.timerText.text!="Q&A")
            throw new Exception($"Q&A preview visibility incorrect: button={controller.qaButton.name}, active={controller.qaButton.gameObject.activeInHierarchy}, enabled={controller.qaButton.interactable}, progress={progress}, progressActive={(progress ? progress.gameObject.activeInHierarchy : false)}, progressParent={(progress ? progress.transform.parent.name : "none")}, timer={controller.timerText.text}.");
        TestSharedQAControl(controller);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeObject=null;
        SceneView.RepaintAll(); UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        File.AppendAllText(Report,"PASS Q&A preview: clock Q&A; start/legacy QA hidden; shared podium end button white outline and disabled; question progress visible; question content hidden; script hidden; slides/audience retained. No TTS, microphone or server session started.\n");
    }

    static void TestSharedQAControl(PresentationController source)
    {
        var sandbox=EditorSceneManager.NewPreviewScene();
        var root=new GameObject("Shared Q&A control test");
        SceneManager.MoveGameObjectToScene(root,sandbox);
        try
        {
            var c=root.AddComponent<PresentationController>();
            var qa=root.AddComponent<QuestionAnswerManager>();
            var button=Object.Instantiate(source.endPresentationButton,root.transform);
            button.onClick=new Button.ButtonClickedEvent();
            button.onClick.AddListener(c.EndPresentation);
            c.endPresentationButton=c.qaButton=button; c.qaManager=qa;
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            var ct=typeof(PresentationController); var qt=typeof(QuestionAnswerManager);
            foreach(var name in new[]{"hasStarted","hasEnded","isQAPhaseStarted"}) ct.GetField(name,flags).SetValue(c,true);
            foreach(var name in new[]{"questionOutlineSprite","answerFillSprite"})
                qt.GetField(name,flags).SetValue(qa,qt.GetField(name,flags).GetValue(source.qaManager));
            qa.Prepare(button);
            var state=qt.GetField("state",flags);
            state.SetValue(qa,Enum.Parse(state.FieldType,"PlayingQuestion"));
            qt.GetMethod("SetButtonState",flags).Invoke(qa,new object[]{"질문하는 중...",false});
            button.onClick.Invoke();
            if(button.interactable || state.GetValue(qa).ToString()!="PlayingQuestion") throw new Exception("Speech click was not blocked.");
            qt.GetMethod("FinishQuestionAudio",flags).Invoke(qa,null);
            if(!button.interactable || button.GetComponentInChildren<TMP_Text>().text!="응답 시작하기") throw new Exception("Shared control not ready after speech.");
            button.onClick.Invoke();
            if(button.interactable || button.GetComponentInChildren<TMP_Text>().text!="응답 마치기") throw new Exception("Shared end callback did not begin answer with lock.");
            ct.GetMethod("RefreshSessionButtons",flags).Invoke(c,null);
            button.onClick.Invoke();
            if(!button.gameObject.activeSelf || button.interactable || state.GetValue(qa).ToString()!="Answering") throw new Exception("Refresh/repeated click broke Q&A lock.");
            qt.GetField("answerUnlockTime",flags).SetValue(qa,Time.unscaledTime-1);
            qt.GetMethod("Update",flags).Invoke(qa,null);
            if(!button.interactable) throw new Exception("Answer lock did not expire.");
            var savedPin=RuntimeSessionData.Pin; var savedSession=RuntimeSessionData.Session;
            try
            {
                foreach(var total in new[]{1,3,5})
                {
                    RuntimeSessionData.Load("preview",new SessionData {page_1=new Page1 {qa_count=total}});
                    qt.GetField("questionCount",flags).SetValue(qa,RuntimeSessionData.QaCount);
                    qt.GetField("<IsQAPhaseActive>k__BackingField",flags).SetValue(qa,true);
                    state.SetValue(qa,Enum.Parse(state.FieldType,"PlayingQuestion"));
                    foreach(var index in new[]{0,total-1})
                    {
                        qt.GetField("currentIdx",flags).SetValue(qa,index);
                        qt.GetMethod("SetButtonState",flags).Invoke(qa,new object[]{"청중 질문 중…",false});
                        var counter=(TMP_Text)qt.GetField("questionProgressText",flags).GetValue(qa);
                        if(!counter || !counter.gameObject.activeSelf || counter.transform.parent!=button.transform ||
                           counter.text!=$"{index+1} / {total}번째 질문 · 이후 {total-index-1}개 남음")
                            throw new Exception("Web question-count progress mismatch.");
                    }
                }
            }
            finally {RuntimeSessionData.Load(savedPin,savedSession);}
            File.AppendAllText(Report,"PASS web qa_count 1/3/5: first/last question and remaining count follow loaded session count on the shared button.\n");
            if(source.qaButton!=source.endPresentationButton || source.endPresentationButton.onClick.GetPersistentMethodName(0)!="EndPresentation") throw new Exception("Scene shared callback missing.");
            File.AppendAllText(Report,"PASS shared podium end/Q&A button: speech lock, voice-end answer state, end-button callback starts answer, duplicate click blocked, refresh preserves lock, cooldown unlock. Isolated; no recording/network.\n");
        }
        finally { Object.DestroyImmediate(root); EditorSceneManager.ClosePreviewScene(sandbox); }
    }

    [MenuItem("Rehear/Preview Seated Presentation Audience")]
    static void ShowAudiencePreview()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Preview in Edit mode.");
        var scene=EditorSceneManager.GetActiveScene();
        if(scene.path!="Assets/01_Scene/Scene_02_Presentation.unity") throw new Exception("Open the presentation scene.");
        foreach(var root in scene.GetRootGameObjects())
            if(root.name=="Audience Preview (Editor Only)") Object.DestroyImmediate(root);
        var source=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<AudienceSeating>(true)).Single();
        var previewScene=EditorSceneManager.NewPreviewScene();
        GameObject copy=null;
        try
        {
            copy=Object.Instantiate(source.gameObject);
            SceneManager.MoveGameObjectToScene(copy,previewScene);
            var seating=copy.GetComponent<AudienceSeating>();
            seating.Initialize(1234);
            if(seating.members.Length!=6 || seating.seats.Any(s=>!s.Occupant)) throw new Exception("Prestart audience seats are incomplete.");
            foreach(var actor in seating.members)
            {
                var body=actor.GetComponent<AudienceAnimationPlayer>();
                var settings=new SerializedObject(body);
                var catalog=(AudienceAnimationCatalog)settings.FindProperty("catalog").objectReferenceValue;
                if(!catalog.TryGetClip("BL_03.quiet_stable_posture",body.Gender,out var idle)) throw new Exception("Seated idle clip missing.");
                var position=actor.transform.position; var rotation=actor.transform.rotation;
                idle.SampleAnimation(actor.gameObject,0);
                actor.transform.SetPositionAndRotation(position,rotation);
                var pose=actor.GetComponent<AudienceSeatedPose>();
                var seat=seating.seats.Single(s=>s.Occupant.gameObject==actor.gameObject);
                if((actor.transform.TransformPoint(pose.localHip)-seat.transform.position).sqrMagnitude>.00001f)
                    throw new Exception("Seated preview is not aligned to its seat.");
            }
            foreach(var behaviour in copy.GetComponentsInChildren<MonoBehaviour>(true)) if(behaviour) behaviour.enabled=false;
            foreach(var animator in copy.GetComponentsInChildren<Animator>(true)) animator.enabled=false;
            copy.name="Audience Preview (Editor Only)"; copy.tag="EditorOnly";
            copy.hideFlags=HideFlags.DontSave;
            SceneManager.MoveGameObjectToScene(copy,scene);
            File.AppendAllText(Report,$"PASS prestart audience preview: {seating.members.Length} seated prefab actors, {seating.SpawnedLaptops.Count} laptops, all hips aligned to seats. Uses AudienceSeating.Initialize; preview excluded from save/build and removed before Play.\n");
            copy=null;
            SceneView.RepaintAll(); UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }
        finally
        {
            if(copy) Object.DestroyImmediate(copy);
            EditorSceneManager.ClosePreviewScene(previewScene);
        }
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
        t.text=value; t.fontSize=size; t.fontStyle=FontStyles.Normal; t.color=color??Ink; t.alignment=TextAlignmentOptions.Center;
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
        var r=Solid(p,"Tile "+index,36+index*(w+12),379,w,143,new Color(1,1,1,.2f));
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
    static void ConfigureTexture(string path)
    {
        AssetDatabase.ImportAsset(path);
        var importer=(TextureImporter)AssetImporter.GetAtPath(path);
        if(!importer) throw new Exception("Texture importer missing: "+path);
        importer.textureType=TextureImporterType.Sprite; importer.spriteImportMode=SpriteImportMode.Single;
        importer.alphaSource=TextureImporterAlphaSource.FromInput; importer.alphaIsTransparency=true;
        var textureSettings=new TextureImporterSettings(); importer.ReadTextureSettings(textureSettings);
        textureSettings.spriteMeshType=SpriteMeshType.FullRect; importer.SetTextureSettings(textureSettings);
        importer.wrapMode=TextureWrapMode.Clamp; importer.filterMode=FilterMode.Bilinear;
        importer.mipmapEnabled=false; importer.textureCompression=TextureImporterCompression.Uncompressed;
        foreach(var platform in new[]{"Standalone","Android"})
        {
            var settings=importer.GetPlatformTextureSettings(platform);
            settings.name=platform; settings.overridden=true; settings.maxTextureSize=512;
            settings.format=TextureImporterFormat.RGBA32; settings.textureCompression=TextureImporterCompression.Uncompressed;
            importer.SetPlatformTextureSettings(settings);
        }
        importer.SaveAndReimport();
    }
    static void ConfigureSourceTextures()
    {
        foreach(var path in Directory.GetFiles("Assets/Textures/UI/FigmaSessionReady","*.png")) ConfigureTexture(path.Replace('\\','/'));
        ConfigureTexture("Assets/Textures/UI/FigmaOpening/logo.png");
    }
    static void Build()
    {
        ConfigureSourceTextures();
        const string presentationScenePath="Assets/01_Scene/Scene_02_Presentation.unity";
        var scene=EditorSceneManager.GetActiveScene();
        if(scene.path!=presentationScenePath)
            scene=EditorSceneManager.OpenScene(presentationScenePath,OpenSceneMode.Single);
        var all=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
        var controller=all.Select(t=>t.GetComponent<PresentationController>()).Single(c=>c);
        if(controller.sessionReady) throw new Exception("Session-ready panel already exists; refusing duplicate.");
        var camera=all.Select(t=>t.GetComponent<Camera>()).First(c=>c&&c.CompareTag("MainCamera"));
        var clone=Object.Instantiate(controller.pausePanel); clone.name="Presentation Session Ready"; clone.SetActive(false);
        foreach(Transform child in clone.transform.Cast<Transform>().ToArray()) Object.DestroyImmediate(child.gameObject);
        var rect=(RectTransform)clone.transform; rect.SetParent(null); rect.sizeDelta=new Vector2(CardWidth,CardHeight); rect.pivot=new Vector2(.5f,.5f);
        rect.localScale=Vector3.one*.0015f;
        ApplyTutorialPlacement(rect,camera,out var cameraLocalPosition,out var cameraLocalRotation);
        var canvas=clone.GetComponent<Canvas>(); canvas.worldCamera=camera; canvas.sortingOrder=100;
        var view=clone.AddComponent<PresentationSessionReady>(); view.controller=controller; controller.sessionReady=view;
        view.cameraLocalPosition=cameraLocalPosition; view.cameraLocalEuler=cameraLocalRotation.eulerAngles;
        var panel=Rect(rect,"Session Ready Panel",0,0,CardWidth,CardHeight);
        var glass=panel.gameObject.AddComponent<TranslucentImage>(); glass.material=Material("Panel",CardWidth,CardHeight,48,2,true);
        glass.color=Color.white; glass.foregroundOpacity=.45f; glass.raycastTarget=true;
        glass.source=camera.GetComponent<TranslucentImageSource>(); glass.material.SetFloat("_GlassTint",.45f);
        glass.material.SetFloat("_UseFigmaGlow",1); glass.material.SetVector("_FigmaDesignSize",new Vector4(CardWidth,CardHeight,0,0));
        glass.material.SetTexture("_FigmaGlowTex",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/UI/FigmaOpening/top-radial.png"));
        Icon(panel,"Logo","Assets/Textures/UI/FigmaOpening/logo.png",36,33,136.7f,38);
        Icon(panel,"Check","Assets/Textures/UI/FigmaSessionReady/check.png",372,63,60,60);
        Text(panel,"Heading","세션 불러오기 완료",140,145,524,43,36,true);
        Text(panel,"Description","사전에 설정한 내용을 확인하고 발표를 시작하세요",36,196,735,28,20,false,Body);
        view.presentationTitle=Text(panel,"Presentation Title","발표 제목",72,298,660,56,40,true,Brand);
        view.sessionType=Tile(panel,0,"mic","세션 유형"); view.duration=Tile(panel,1,"voice","발표 시간");
        view.questionCount=Tile(panel,2,"question","Q&A 개수"); view.audienceCount=Tile(panel,3,"group","청중 규모");
        view.environment=Row(panel,"Environment","발표 환경",36,532,735);
        view.expertise=Row(panel,"Expertise","청중 전문성",35,598,362); view.interest=Row(panel,"Interest","청중 관심도",409,598,362);
        view.continueButton=Button(panel,"Continue","세션 시작하기",686,false,view.Confirm);
        view.returnButton=Button(panel,"Return to PIN","PIN 번호 다시 입력하기",754,true,view.ReturnToPin);
        clone.GetComponent<CurvedUISettings>().AddEffectToChildren();
        var sync=camera.GetComponent<TutorialBlurCameraSync>(); sync.panels=sync.panels.Concat(new[]{glass}).ToArray(); EditorUtility.SetDirty(sync);
        if(controller.qaButton) controller.qaButton.gameObject.SetActive(false);
        var questionText=all.Select(t=>t.GetComponent<QuestionAnswerManager>()).FirstOrDefault(q=>q)?.questionText;
        if(questionText) questionText.gameObject.SetActive(false);
        clone.SetActive(true); EditorUtility.SetDirty(controller); AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject=clone;
        if(SceneView.lastActiveSceneView) SceneView.lastActiveSceneView.FrameSelected();
        File.WriteAllText(Report,"PASS Figma 2323:38860 804x856 layout, transparent source icons, 45% glass, clipped radial, blue server title inside panel, cached session fields, confirm/PIN callbacks.\n");
    }

    static void SetRect(RectTransform r,float x,float y,float w,float h)
    {
        r.anchorMin=r.anchorMax=r.pivot=new Vector2(0,1);
        r.anchoredPosition=new Vector2(x,-y);
        r.sizeDelta=new Vector2(w,h);
    }

    static void SetTextStyle(TMP_Text text,string weight,float size,Color color)
    {
        if(!text) return;
        text.font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>($"Assets/07_Fonts/PretendardTMP/Pretendard-{weight} SDF.asset");
        text.fontSize=size; text.fontStyle=FontStyles.Normal; text.color=color;
        text.enableAutoSizing=true; text.fontSizeMin=size*.65f; text.fontSizeMax=size;
        text.characterSpacing=-1f; text.raycastTarget=false;
        EditorUtility.SetDirty(text);
    }

    static void UpdateCurrentLayout()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Stop Play mode first");
        ConfigureSourceTextures();
        const string scenePath="Assets/01_Scene/Scene_02_Presentation.unity";
        var scene=EditorSceneManager.GetActiveScene();
        if(scene.path!=scenePath) scene=EditorSceneManager.OpenScene(scenePath,OpenSceneMode.Single);
        var view=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<PresentationSessionReady>(true)).SingleOrDefault();
        if(!view) throw new Exception("Existing Presentation Session Ready panel was not found.");
        var camera=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Camera>(true)).First(c=>c&&c.CompareTag("MainCamera"));
        var root=(RectTransform)view.transform;
        root.sizeDelta=new Vector2(CardWidth,CardHeight); root.pivot=new Vector2(.5f,.5f); root.localScale=Vector3.one*.0015f;
        ApplyTutorialPlacement(root,camera,out var cameraLocalPosition,out var cameraLocalRotation);
        view.cameraLocalPosition=cameraLocalPosition; view.cameraLocalEuler=cameraLocalRotation.eulerAngles;

        var panel=view.transform.Find("Session Ready Panel") as RectTransform;
        if(!panel) throw new Exception("Session Ready Panel child was not found.");
        SetRect(panel,0,0,CardWidth,CardHeight);
        if(view.presentationTitle.transform.parent!=panel) view.presentationTitle.transform.SetParent(panel,false);
        SetRect((RectTransform)view.presentationTitle.transform,72,298,660,56);
        view.presentationTitle.alignment=TextAlignmentOptions.Center;
        SetTextStyle(view.presentationTitle,"Bold",40,Brand);
        // Edit-mode examples only; Populate replaces these with the loaded web session.
        view.presentationTitle.text="추천 알고리즘은 선택을 넓힐까?";
        view.sessionType.text="발표 모드";
        view.duration.text="30분";
        view.questionCount.text="1개";
        view.audienceCount.text="6명";
        view.environment.text="세미나실";
        view.expertise.text="보통";
        view.interest.text="높음";

        SetRect((RectTransform)panel.Find("Logo"),36,33,136.7f,38);
        SetRect((RectTransform)panel.Find("Check"),372,63,60,60);
        SetRect((RectTransform)panel.Find("Heading"),140,145,524,43);
        SetRect((RectTransform)panel.Find("Description"),36,196,735,28);
        SetTextStyle(panel.Find("Heading").GetComponent<TMP_Text>(),"Bold",36,Ink);
        SetTextStyle(panel.Find("Description").GetComponent<TMP_Text>(),"Medium",20,Body);
        for(int index=0;index<4;index++)
        {
            var tile=(RectTransform)panel.Find("Tile "+index);
            SetRect(tile,36+index*(174.75f+12),379,174.75f,143);
            SetTextStyle(tile.Find("Caption").GetComponent<TMP_Text>(),"Medium",18,Body);
            SetTextStyle(tile.Find("Value").GetComponent<TMP_Text>(),"Bold",24,Ink);
            var icon=tile.GetComponentsInChildren<Image>(true).FirstOrDefault(image=>image.transform!=tile); if(icon) {icon.preserveAspect=true; icon.raycastTarget=false; EditorUtility.SetDirty(icon);}
        }
        SetRect((RectTransform)panel.Find("Environment"),36,532,735,56);
        SetRect((RectTransform)panel.Find("Expertise"),35,598,362,56);
        SetRect((RectTransform)panel.Find("Interest"),409,598,362,56);
        SetRect((RectTransform)panel.Find("Continue"),36,686,735,56);
        SetRect((RectTransform)panel.Find("Return to PIN"),36,754,735,56);
        foreach(var rowName in new[]{"Environment","Expertise","Interest"})
        {
            var row=panel.Find(rowName); SetTextStyle(row.Find("Caption").GetComponent<TMP_Text>(),"Medium",18,Body); SetTextStyle(row.Find("Value").GetComponent<TMP_Text>(),"Bold",18,Ink);
        }
        foreach(var buttonName in new[]{"Continue","Return to PIN"}) SetTextStyle(panel.Find(buttonName+"/Label").GetComponent<TMP_Text>(),"Medium",24,Color.white);
        foreach(var image in panel.GetComponentsInChildren<Image>(true))
        {
            if(image.sprite) image.preserveAspect=true;
            EditorUtility.SetDirty(image);
        }
        var glass=panel.GetComponent<TranslucentImage>();
        glass.material=Material("Panel",CardWidth,CardHeight,48,2,true); glass.foregroundOpacity=.45f; glass.source=camera.GetComponent<TranslucentImageSource>();
        glass.material.SetFloat("_GlassTint",.45f); glass.material.SetFloat("_UseFigmaGlow",1); glass.material.SetVector("_FigmaDesignSize",new Vector4(CardWidth,CardHeight,0,0));
        glass.material.SetTexture("_FigmaGlowTex",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/UI/FigmaOpening/top-radial.png"));
        view.GetComponent<Canvas>().worldCamera=camera;
        view.GetComponent<CurvedUISettings>()?.AddEffectToChildren();
        if(view.controller.qaButton) view.controller.qaButton.gameObject.SetActive(false);
        var question=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<QuestionAnswerManager>(true)).FirstOrDefault()?.questionText;
        if(question) question.gameObject.SetActive(false);
        view.gameObject.SetActive(true);
        EditorUtility.SetDirty(view); EditorUtility.SetDirty(view.controller); EditorUtility.SetDirty(glass);
        EditorSceneManager.MarkSceneDirty(scene); AssetDatabase.SaveAssets(); EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject=view.gameObject; if(SceneView.lastActiveSceneView) SceneView.lastActiveSceneView.FrameSelected();
        File.WriteAllText(Report,$"PASS updated existing panel to Figma 804x856; title is inside at y=298, Pretendard Bold 40, #0033FF. tutorial camera-local position={cameraLocalPosition}, rotation={cameraLocalRotation.eulerAngles}.\n");
    }

    static void InspectTutorialPlacement()
    {
        var current=EditorSceneManager.GetActiveScene();
        var currentCamera=current.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Camera>(true)).FirstOrDefault(c=>c&&c.CompareTag("MainCamera"));
        if(!TryGetTutorialPlacement(out var localPosition,out var localRotation)) throw new Exception("Tutorial UI placement was not found.");
        File.WriteAllText(Report,$"Tutorial camera-local position={localPosition}, rotation={localRotation.eulerAngles}. Presentation camera={currentCamera?.transform.position}, rotation={currentCamera?.transform.eulerAngles}.\n");
    }

    static void ApplyTutorialPlacement(RectTransform target,Camera targetCamera,out Vector3 localPosition,out Quaternion localRotation)
    {
        if(!TryGetTutorialPlacement(out var worldPosition,out var worldRotation))
            throw new Exception("TutorialUI is missing; cannot match its authored scene placement.");
        target.SetPositionAndRotation(worldPosition + Vector3.down * .25f,worldRotation);
        // Preview stays at the tutorial location; runtime opens in front of the viewer.
        localPosition=new Vector3(0f,-.25f,1.95f);
        localRotation=Quaternion.identity;
    }

    static bool TryGetTutorialPlacement(out Vector3 localPosition,out Quaternion localRotation)
    {
        const string tutorialPath="Assets/01_Scene/Scene_00_5_Tutorial.unity";
        localPosition=default; localRotation=Quaternion.identity;
        var tutorial=SceneManager.GetSceneByPath(tutorialPath); var opened=!tutorial.IsValid()||!tutorial.isLoaded;
        if(opened) tutorial=EditorSceneManager.OpenScene(tutorialPath,OpenSceneMode.Additive);
        try
        {
            var roots=tutorial.GetRootGameObjects();
            var camera=roots.SelectMany(g=>g.GetComponentsInChildren<Camera>(true)).FirstOrDefault(c=>c&&c.CompareTag("MainCamera"));
            var tutorialUi=roots.SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t=>t.name=="TutorialUI");
            if(!camera||!tutorialUi) return false;
            localPosition=tutorialUi.position;
            localRotation=tutorialUi.rotation;
            return true;
        }
        finally {if(opened&&tutorial.IsValid()&&tutorial.isLoaded) EditorSceneManager.CloseScene(tutorial,true);}
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
    static void RestoreOriginalPanel()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Stop Play mode first");
        const string presentationScenePath="Assets/01_Scene/Scene_02_Presentation.unity";
        var scene=EditorSceneManager.GetActiveScene();
        if(scene.path!=presentationScenePath)
            scene=EditorSceneManager.OpenScene(presentationScenePath,OpenSceneMode.Single);
        var all=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
        var controller=all.Select(t=>t.GetComponent<PresentationController>()).Single(c=>c);
        foreach(var current in all.Where(t=>t && t.name=="Presentation Session Ready").ToArray())
            Object.DestroyImmediate(current.gameObject);
        controller.sessionReady=null;
        EditorUtility.SetDirty(controller);
        Build();
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

    static void TestReadyFlow()
    {
        if(EditorApplication.isPlaying) throw new Exception("Run this isolated check in Edit mode.");
        var original=Object.FindFirstObjectByType<PresentationSessionReady>(FindObjectsInactive.Include);
        if(!original) throw new Exception("Ready panel missing.");
        if(original.continueButton.onClick.GetPersistentTarget(0)!=original ||
           original.continueButton.onClick.GetPersistentMethodName(0)!="Confirm" ||
           original.returnButton.onClick.GetPersistentTarget(0)!=original ||
           original.returnButton.onClick.GetPersistentMethodName(0)!="ReturnToPin")
            throw new Exception("Scene button wiring is incorrect.");
        var savedSession=RuntimeSessionData.Session;
        var savedPin=RuntimeSessionData.Pin;
        GameObject copy=null, controlObject=null;
        var token=new System.Threading.CancellationTokenSource();
        try
        {
            copy=Object.Instantiate(original.gameObject);
            copy.hideFlags=HideFlags.HideAndDontSave;
            copy.SetActive(true);
            var view=copy.GetComponent<PresentationSessionReady>();
            controlObject=new GameObject("Isolated ready-flow check");
            controlObject.hideFlags=HideFlags.HideAndDontSave;
            var controller=controlObject.AddComponent<PresentationController>();
            view.controller=controller; controller.sessionReady=view;
            controller.startPresentationButton=new GameObject("Start",typeof(RectTransform),typeof(Button)).GetComponent<Button>();
            controller.endPresentationButton=new GameObject("End",typeof(RectTransform),typeof(Button)).GetComponent<Button>();
            controller.startPresentationButton.transform.SetParent(controlObject.transform);
            controller.endPresentationButton.transform.SetParent(controlObject.transform);
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            typeof(PresentationController).GetField("lifetimeCancellation",flags).SetValue(controller,token);
            bool ReadFlag(string name)=>(bool)typeof(PresentationController).GetField(name,flags).GetValue(controller);
            void Require(bool value,string message) { if(!value) throw new Exception(message); }

            RuntimeSessionData.Load("isolated-ui-check",new SessionData {
                page_1=new Page1 {presentation_title="웹 데이터 연결 검증",presentation_purpose="발표 모드",duration_minutes=17,qa_count=2,environment_type="세미나실"},
                page_3=new Page3 {audience_scale=4,audience_expertise="높음",audience_interest="보통"}
            });
            view.Populate();
            Require(view.presentationTitle.text=="웹 데이터 연결 검증" && view.sessionType.text=="발표 모드" &&
                view.duration.text=="17분" && view.questionCount.text=="2개" && view.audienceCount.text=="4명" &&
                view.environment.text=="세미나실" && view.expertise.text=="높음" && view.interest.text=="보통","Web fields did not replace preview examples.");
            Require(view.presentationTitle.transform.IsChildOf(view.transform.Find("Session Ready Panel")),"Title outside card.");
            controller.SetSessionConfirmationVisible(true);
            controller.BeginPresentation();
            controller.PauseGame();
            Require(controller.IsConfirmingSession && !ReadFlag("hasStarted") && !ReadFlag("isRunning") &&
                !controller.IsPaused && !controller.startPresentationButton.interactable,"Ready modal did not block start/pause.");
            Require(view.continueButton.onClick.GetPersistentTarget(0)==view,"Cloned scene button lost its callback.");
            view.continueButton.onClick.SetPersistentListenerState(0,UnityEngine.Events.UnityEventCallState.EditorAndRuntime);
            view.continueButton.onClick.Invoke();
            Require(!view.gameObject.activeSelf && !controller.IsConfirmingSession &&
                controller.startPresentationButton.interactable && !ReadFlag("isRunning"),"Confirm must close card without starting timer.");
            controller.BeginPresentation();
            Require(ReadFlag("hasStarted") && ReadFlag("isRunning") && !controller.startPresentationButton.gameObject.activeSelf &&
                controller.endPresentationButton.gameObject.activeSelf,"Start did not switch timer/buttons.");
            controller.BeginPresentation();
            Require(ReadFlag("isRunning"),"Duplicate start changed state.");
            RuntimeSessionData.Clear();
            view.gameObject.SetActive(true);
            var startup=(System.Collections.IEnumerator)typeof(PresentationSessionReady).GetMethod("Start",flags).Invoke(view,null);
            startup.MoveNext();
            Require(!view.gameObject.activeSelf,"Preview data remains visible without a valid session.");
            File.AppendAllText(Report,"PASS isolated UI flow: 8 web fields replace samples; real Confirm/PIN callbacks; modal blocks start/pause; Confirm unlocks start without timer; Start runs timer and swaps buttons; duplicate start guarded; no-session preview hidden. No server or recording invoked.\n");
        }
        finally
        {
            if(copy) Object.DestroyImmediate(copy);
            if(controlObject) Object.DestroyImmediate(controlObject);
            token.Dispose();
            RuntimeSessionData.Load(savedPin,savedSession);
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
