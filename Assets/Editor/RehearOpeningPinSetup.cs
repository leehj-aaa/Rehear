using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEditor.Events;
using CurvedUI;

[InitializeOnLoad]
internal static class RehearOpeningPinSetup
{
    const string Request = "Temp/RehearOpeningPin.request";
    static RehearOpeningPinSetup() { EditorApplication.update += Poll; EditorApplication.update += WatchRuntime; }
    static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning) return;
        string command;
        try { command = File.ReadAllText(Request).Trim(); if(command.Length==0)return; File.Delete(Request); }
        catch (IOException) { return; }
        try { if(command=="reimportplugins") ReimportPlugins(); else if(command=="plugins") InspectPlugins(); else if(command=="apply") Apply(); else if(command=="verify") Verify(); else if(command=="test") Test();
            else if(command=="playtest") { SessionState.SetBool("RehearPinRuntimeTest",true); EditorApplication.EnterPlaymode(); } else Inspect(); }
        catch (Exception e) { File.WriteAllText("Temp/RehearOpeningPin.txt", "FAIL\n" + e); }
    }
    static void ReimportPlugins()
    {
        foreach(var plugin in PluginImporter.GetAllImporters().Where(p=>p.assetPath.StartsWith("Assets/Plugins/x86_64/FirebaseCpp") && p.assetPath.EndsWith(".dll")))
        {
            plugin.SetCompatibleWithEditor(true); plugin.SetEditorData("OS","Windows");plugin.SetEditorData("CPU","x86_64");
            plugin.SaveAndReimport();
            AssetDatabase.ForceReserializeAssets(new[]{plugin.assetPath},ForceReserializeAssetsOptions.ReserializeMetadata);
        }
        File.WriteAllText("Temp/RehearFirebaseReimport.txt","PASS Windows Firebase DLL importers reserialized and reimported. Android untouched.\n");
    }
    static void InspectPlugins()
    {
        var report=new StringBuilder();
        foreach(var plugin in PluginImporter.GetAllImporters().Where(p=>p.assetPath.Contains("Firebase") && (p.assetPath.Contains("x86_64") || p.assetPath.EndsWith("Firebase.App.dll"))))
            report.AppendLine($"{plugin.assetPath}: native={plugin.isNativePlugin} editor={plugin.GetCompatibleWithEditor()} OS={plugin.GetEditorData("OS")} CPU={plugin.GetEditorData("CPU")} any={plugin.GetCompatibleWithAnyPlatform()} win64={plugin.GetCompatibleWithPlatform(BuildTarget.StandaloneWindows64)}");
        foreach(System.Diagnostics.ProcessModule module in System.Diagnostics.Process.GetCurrentProcess().Modules)
            if(module.ModuleName.IndexOf("Firebase",StringComparison.OrdinalIgnoreCase)>=0) report.AppendLine("LOADED "+module.FileName);
        report.AppendLine("pointerSize="+IntPtr.Size+" project="+Application.dataPath);
        File.WriteAllText("Temp/RehearFirebasePlugins.txt",report.ToString());
    }
    static void Inspect()
    {
        var scene = EditorSceneManager.OpenPreviewScene("Assets/01_Scene/Scene_01_Intro.unity");
        try
        {
            var report = new StringBuilder();
            var all = scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
            foreach (var t in all)
            {
                var text=t.GetComponent<TMP_Text>(); var button=t.GetComponent<Button>();
                report.AppendLine($"{Path(t)} active={t.gameObject.activeSelf} pos={t.position} scale={t.lossyScale} rect={(t as RectTransform)?.rect} components={string.Join(",",t.GetComponents<Component>().Select(c=>c?c.GetType().Name:"MISSING"))} text={text?.text} font={text?.font?.name}");
                if(button) for(int i=0;i<button.onClick.GetPersistentEventCount();i++)
                    report.AppendLine($"  CLICK {button.onClick.GetPersistentTarget(i)}.{button.onClick.GetPersistentMethodName(i)}");
            }
            File.WriteAllText("Temp/RehearOpeningPin.txt",report.ToString());
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }
    static string Path(Transform t) => t.parent ? Path(t.parent)+"/"+t.name : t.name;

    static void Apply()
    {
        var scene=EditorSceneManager.GetActiveScene();
        if(scene.name!="Scene_00" || UnityEngine.SceneManagement.SceneManager.sceneCount!=1)
            throw new Exception("Keep only Scene_00 open before applying.");
        var all=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
        if(all.Any(t=>t.name=="OpeningPinCanvas")) throw new Exception("OpeningPinCanvas already exists; inspect it instead of duplicating it.");
        var start=all.Select(t=>t.GetComponent<Button>()).Single(b=>b && b.name=="Btn_Scene00_to_Scene01");
        var flow=all.Select(t=>t.GetComponent<Scene00_to_Scene01>()).Single(c=>c);
        var logo=all.Single(t=>t.name=="Rehear Logo Intro");
        Sprite logoSprite=null;
        var preview=EditorSceneManager.OpenPreviewScene("Assets/01_Scene/Scene_01_Intro.unity");
        try
        {
            var source=preview.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true));
            logoSprite=source.First(t=>t.name=="Image_Logo").GetComponent<Image>().sprite;
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); }

        var root=new GameObject("OpeningPinCanvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));
        Undo.RegisterCreatedObjectUndo(root,"Add opening PIN screen");
        root.SetActive(false);
        var canvas=root.GetComponent<Canvas>(); canvas.renderMode=RenderMode.WorldSpace; canvas.worldCamera=Camera.main;
        Rect(root.transform,Vector2.zero,new Vector2(1000,1180));
        root.transform.localScale=Vector3.one*.0018f;
        root.transform.SetPositionAndRotation(Camera.main.transform.position+Camera.main.transform.forward*3,Camera.main.transform.rotation);
        var manager=root.AddComponent<PinInputManager>();
        var panel=Box(root.transform,"PinPanel",new Vector2(0,175),new Vector2(1000,720),new Color(.86f,.90f,1,.52f),44,2);
        if(logoSprite) { var image=Image(panel.transform,"BrandLogo",new Vector2(-375,295),new Vector2(165,45)); image.sprite=logoSprite; image.preserveAspect=true; }
        Text(panel.transform,"Title","PIN 번호 입력",new Vector2(0,225),new Vector2(850,64),42,true,Color.black);
        Text(panel.transform,"Instruction","웹에서 생성된 PIN 번호 4자리를 입력하고 설정한 세션을 불러오세요",new Vector2(0,155),new Vector2(900,52),24,false,new Color(.12f,.15f,.22f));
        var slots=new TMP_Text[4];
        for(int i=0;i<4;i++)
        {
            var slot=Box(panel.transform,"DigitSlot"+(i+1),new Vector2((i-1.5f)*225,0),new Vector2(210,235),new Color(1,1,1,.25f),42,0);
            var button=slot.AddComponent<Button>(); button.targetGraphic=slot.GetComponent<Image>();
            SetColors(button,new Color(1,1,1,.25f));
            UnityEventTools.AddPersistentListener(button.onClick,manager.OpenKeyboard);
            slots[i]=Text(slot.transform,"Digit","",Vector2.zero,new Vector2(190,210),94,true,new Color(.025f,.045f,.14f));
        }
        var submit=Button(panel.transform,"SubmitPin","입력하기",new Vector2(0,-225),new Vector2(910,76),new Color(0,.2f,1));
        UnityEventTools.AddPersistentListener(submit.onClick,manager.OnSubmitButtonClicked);
        var error=Text(panel.transform,"Error","",new Vector2(0,-310),new Vector2(900,72),24,false,new Color(.46f,.02f,.05f));
        var keys=Box(root.transform,"NumberKeyboard",new Vector2(0,-430),new Vector2(780,440),new Color(.035f,.055f,.18f,.86f),32,0);
        for(int number=1;number<=9;number++)
        {
            int index=number-1;
            var key=Button(keys.transform,"Key"+number,number.ToString(),new Vector2((index%3-1)*246,148-(index/3)*98),new Vector2(222,84),new Color(.19f,.25f,.50f));
            UnityEventTools.AddStringPersistentListener(key.onClick,manager.AddNumber,number.ToString());
        }
        var zero=Button(keys.transform,"Key0","0",new Vector2(0,-146),new Vector2(222,84),new Color(.19f,.25f,.50f));
        UnityEventTools.AddStringPersistentListener(zero.onClick,manager.AddNumber,"0");
        var reset=Button(keys.transform,"ResetPin","지우기",new Vector2(-246,-146),new Vector2(222,84),new Color(.19f,.25f,.50f));
        UnityEventTools.AddPersistentListener(reset.onClick,manager.ResetInput);
        var confirm=Button(keys.transform,"ConfirmPin","확인",new Vector2(246,-146),new Vector2(222,84),new Color(0,.2f,1));
        UnityEventTools.AddPersistentListener(confirm.onClick,manager.OnSubmitButtonClicked);
        keys.SetActive(false);
        var loading=Box(root.transform,"LoadingPanel",new Vector2(0,175),new Vector2(1000,720),new Color(.86f,.90f,1,.52f),44,2);
        if(logoSprite) { var image=Image(loading.transform,"BrandLogo",new Vector2(-375,295),new Vector2(165,45)); image.sprite=logoSprite; image.preserveAspect=true; }
        Text(loading.transform,"Title","세션 불러오는 중",new Vector2(0,20),new Vector2(900,70),42,true,Color.black);
        Text(loading.transform,"Description","잠시만 기다려 주세요.\n웹에서 설정한 세션 정보를 불러오고 있습니다.",new Vector2(0,-85),new Vector2(900,100),28,false,new Color(.12f,.15f,.22f));
        loading.SetActive(false);
        var data=new SerializedObject(manager);
        var slotArray=data.FindProperty("pinTextSlots");slotArray.arraySize=4;
        for(int i=0;i<4;i++)slotArray.GetArrayElementAtIndex(i).objectReferenceValue=slots[i];
        data.FindProperty("panel_PinInput").objectReferenceValue=panel;
        data.FindProperty("numberKeyboardPanel").objectReferenceValue=keys;
        data.FindProperty("panel_Loading").objectReferenceValue=loading;
        data.FindProperty("errorText").objectReferenceValue=error;
        data.FindProperty("continueToTutorial").boolValue=true;
        // Opening PIN always uses the web-issued record, never a silent local demo substitution.
        data.FindProperty("enableDemoFallback").boolValue=false;
        data.ApplyModifiedPropertiesWithoutUndo();
        foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=LayerMask.NameToLayer("UI");
        var curved=root.AddComponent<CurvedUISettings>();curved.Shape=CurvedUISettings.CurvedUIShape.CYLINDER;curved.Angle=20;curved.PreserveAspect=true;curved.Interactable=true;curved.BlocksRaycasts=true;curved.AddEffectToChildren();
        root.AddComponent<CurvedUIRaycaster>();
        var flowData=new SerializedObject(flow);
        flowData.FindProperty("openingPinRoot").objectReferenceValue=root;
        flowData.FindProperty("openingLogo").objectReferenceValue=logo.gameObject;
        flowData.FindProperty("startButton").objectReferenceValue=start;
        flowData.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
        File.WriteAllText("Temp/RehearOpeningPin.txt","PASS saved opening PIN canvas; 4 digit slots, 10 single-bound number keys, input/clear/confirm, loading panel; demo fallback off.\n");
        Verify();
    }
    static void Rect(Transform t,Vector2 position,Vector2 size)
    { var r=(RectTransform)t;r.anchorMin=r.anchorMax=r.pivot=new Vector2(.5f,.5f);r.anchoredPosition=position;r.sizeDelta=size;r.localRotation=Quaternion.identity;r.localScale=Vector3.one; }
    static Image Image(Transform parent,string name,Vector2 position,Vector2 size)
    { var go=new GameObject(name,typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));go.transform.SetParent(parent,false);Rect(go.transform,position,size);var image=go.GetComponent<Image>();image.raycastTarget=false;return image; }
    static GameObject Box(Transform parent,string name,Vector2 position,Vector2 size,Color color,float radius,float border)
    {
        var image=Image(parent,name,position,size);image.color=color;image.raycastTarget=true;
        string path="Assets/Settings/Opening PIN "+name+".mat";
        var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!material){material=new Material(Shader.Find("Rehear/UI/Rounded Translucent Panel"));AssetDatabase.CreateAsset(material,path);}
        material.SetVector("_PanelSize",new Vector4(size.x,size.y,0,0));material.SetFloat("_Radius",radius);material.SetFloat("_BorderWidth",border);material.SetFloat("_UseBlur",0);material.SetFloat("_FillAlphaOffset",0);EditorUtility.SetDirty(material);image.material=material;
        return image.gameObject;
    }
    static TMP_Text Text(Transform parent,string name,string value,Vector2 position,Vector2 size,float fontSize,bool bold,Color color)
    {
        var go=new GameObject(name,typeof(RectTransform),typeof(CanvasRenderer),typeof(TextMeshProUGUI));go.transform.SetParent(parent,false);Rect(go.transform,position,size);
        var text=go.GetComponent<TextMeshProUGUI>();text.font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/07_Fonts/PretendardTMP/Pretendard-"+(bold?"Bold":"Regular")+" SDF.asset");
        text.fontSharedMaterial=text.font.material;text.text=value;text.fontSize=fontSize;text.color=color;text.alignment=TextAlignmentOptions.Center;text.raycastTarget=false;return text;
    }
    static Button Button(Transform parent,string name,string label,Vector2 position,Vector2 size,Color color)
    { var go=Box(parent,name,position,size,color,size.y*.5f,0);var button=go.AddComponent<Button>();button.targetGraphic=go.GetComponent<Image>();SetColors(button,color);Text(go.transform,"Label",label,Vector2.zero,size-new Vector2(18,8),30,false,Color.white);return button; }
    static void SetColors(Button button,Color normal)
    { var colors=button.colors;colors.normalColor=normal;colors.highlightedColor=Color.Lerp(normal,Color.white,.2f);colors.pressedColor=Color.Lerp(normal,Color.black,.24f);colors.selectedColor=normal;colors.fadeDuration=.08f;button.colors=colors;button.targetGraphic.color=Color.white;button.navigation=new Navigation{mode=Navigation.Mode.None}; }
    static void Verify()
    {
        var scene=EditorSceneManager.GetActiveScene();
        var root=scene.GetRootGameObjects().Single(g=>g.name=="OpeningPinCanvas");
        if(root.activeSelf)throw new Exception("PIN canvas should be hidden on scene start.");
        var manager=root.GetComponent<PinInputManager>();var data=new SerializedObject(manager);
        if(data.FindProperty("enableDemoFallback").boolValue)throw new Exception("Opening must not use demo fallback.");
        var keys=root.GetComponentsInChildren<Button>(true).Where(b=>b.name.StartsWith("Key")).ToArray();
        if(keys.Length!=10 || keys.Any(b=>b.onClick.GetPersistentEventCount()!=1 || b.onClick.GetPersistentTarget(0)!=manager))throw new Exception("Number binding mismatch.");
        File.AppendAllText("Temp/RehearOpeningPin.txt","PASS initial state hidden; every numeric button has exactly one PIN handler; no duplicate XR collider events.\n");
    }
    static void Test()
    {
        File.WriteAllText("Temp/RehearOpeningPin.txt","");
        Verify();
        var scene=EditorSceneManager.GetActiveScene();
        var root=scene.GetRootGameObjects().Single(g=>g.name=="OpeningPinCanvas");
        var manager=root.GetComponent<PinInputManager>();
        var panel=root.transform.Find("PinPanel").gameObject;
        var keyboard=root.transform.Find("NumberKeyboard").gameObject;
        var loading=root.transform.Find("LoadingPanel").gameObject;
        var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        var all=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true));
        var logo=all.Single(t=>t.name=="Rehear Logo Intro").gameObject;
        bool logoActive=logo.activeSelf;
        var start=all.Single(t=>t.name=="Btn_Scene00_to_Scene01").gameObject;
        bool startActive=start.activeSelf;
        try
        {
            root.SetActive(true);logo.SetActive(false);start.SetActive(false);
            typeof(PinInputManager).GetMethod("Awake",flags).Invoke(manager,null);
            manager.OpenKeyboard();
            manager.AddNumber("12");manager.AddNumber("a");
            if(manager.GetFullPin()!="")throw new Exception("Invalid digit accepted.");
            var zero=root.GetComponentsInChildren<Button>(true).Single(b=>b.name=="Key0");
            // Serialized Button callbacks are RuntimeOnly; temporarily enable this one for the edit-mode test.
            zero.onClick.SetPersistentListenerState(0,UnityEngine.Events.UnityEventCallState.EditorAndRuntime);
            try { zero.onClick.Invoke(); }
            finally { zero.onClick.SetPersistentListenerState(0,UnityEngine.Events.UnityEventCallState.RuntimeOnly); }
            if(manager.GetFullPin()!="0")throw new Exception("One key added multiple digits or lost leading zero.");
            manager.OnSubmitButtonClicked();
            if(!panel.activeSelf || loading.activeSelf || !manager.GetComponentInChildren<TMP_Text>(true))throw new Exception("Incomplete PIN navigated.");
            var error=root.GetComponentsInChildren<TMP_Text>(true).Single(t=>t.name=="Error");
            if(!error.text.Contains("4자리"))throw new Exception("Missing incomplete PIN feedback.");
            manager.AddNumber("1");manager.AddNumber("2");manager.AddNumber("3");manager.AddNumber("4");
            if(manager.GetFullPin()!="0123" || keyboard.activeSelf)throw new Exception("Four-digit limit or keyboard completion failed.");
            Capture("pin-input",0);
            manager.ResetInput();
            if(manager.GetFullPin()!="" || !keyboard.activeSelf)throw new Exception("Clear PIN failed.");
            Capture("pin-keyboard",0);
            typeof(PinInputManager).GetField("isLoading",flags).SetValue(manager,true);
            panel.SetActive(false);keyboard.SetActive(false);loading.SetActive(true);
            manager.AddNumber("7");manager.ResetInput();
            if(manager.GetFullPin()!="")throw new Exception("Loading allowed input.");
            Capture("pin-loading",0);
            typeof(PinInputManager).GetField("isLoading",flags).SetValue(manager,false);
            typeof(PinInputManager).GetMethod("ShowError",flags).Invoke(manager,new object[]{"테스트 연결 오류"});
            if(!panel.activeSelf || loading.activeSelf)throw new Exception("Failed request did not restore PIN screen.");
            manager.ResetInput();
            File.AppendAllText("Temp/RehearOpeningPin.txt","PASS offline input: leading zero, single increment, reject non-digit, 4-digit limit, incomplete submit, clear, loading lock and error recovery. No Firebase query or server session was created.\n");
        }
        finally
        {
            typeof(PinInputManager).GetField("isLoading",flags).SetValue(manager,false);
            panel.SetActive(true);manager.ResetInput();keyboard.SetActive(false);loading.SetActive(false);
            root.SetActive(false);logo.SetActive(logoActive);start.SetActive(startActive);
        }
        Capture("sky-motion-0",0);Capture("sky-motion-4",4);
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
    }
    static void WatchRuntime()
    {
        if(EditorApplication.isPlaying && SessionState.GetBool("RehearPinWaitFirebase",false))
        {
            var manager=UnityEngine.Object.FindObjectsByType<PinInputManager>(FindObjectsInactive.Include,FindObjectsSortMode.None)
                .FirstOrDefault(m=>m.gameObject.scene.name=="Scene_00");
            bool ready=manager && (bool)typeof(PinInputManager).GetField("firebaseReady",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(manager);
            if(!ready && EditorApplication.timeSinceStartup<SessionState.GetFloat("RehearPinFirebaseDeadline",0))return;
            SessionState.SetBool("RehearPinWaitFirebase",false);
            File.AppendAllText("Temp/RehearOpeningPin.txt",ready?"PASS Firebase native initialization ready; no PIN lookup performed.\n":"BLOCKED Firebase initialization not ready; inspect Editor log before server testing.\n");
            EditorApplication.delayCall+=EditorApplication.ExitPlaymode;
            return;
        }
        if(!EditorApplication.isPlaying || !SessionState.GetBool("RehearPinRuntimeTest",false))return;
        var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if(scene.name!="Scene_00")return;
        var all=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
        var start=all.Select(t=>t.GetComponent<Button>()).Single(b=>b && b.name=="Btn_Scene00_to_Scene01");
        if(!start.isActiveAndEnabled || !start.IsInteractable() || start.GetComponent<CanvasGroup>().alpha<1)return;
        SessionState.SetBool("RehearPinRuntimeTest",false);
        try
        {
            var root=scene.GetRootGameObjects().Single(g=>g.name=="OpeningPinCanvas");
            if(root.activeSelf)throw new Exception("PIN was visible before clicking start.");
            start.onClick.Invoke();
            if(!root.activeInHierarchy || start.gameObject.activeSelf || all.Single(t=>t.name=="Rehear Logo Intro").gameObject.activeSelf)
                throw new Exception("Start did not open PIN and hide logo/CTA.");
            var manager=root.GetComponent<PinInputManager>();manager.OpenKeyboard();
            root.GetComponentsInChildren<Button>(true).Single(b=>b.name=="Key0").onClick.Invoke();
            if(manager.GetFullPin()!="0")throw new Exception("Runtime numeric key did not add exactly one digit.");
            manager.OnSubmitButtonClicked();
            if(!root.transform.Find("PinPanel").gameObject.activeSelf || root.transform.Find("LoadingPanel").gameObject.activeSelf)
                throw new Exception("Incomplete PIN left input state.");
            manager.ResetInput();Capture("pin-runtime",0);
            File.AppendAllText("Temp/RehearOpeningPin.txt","PASS PLAY MODE: logo finished -> actual start onClick -> PIN in Scene_00; logo and CTA hidden; numeric callback once; incomplete submit stays on PIN. No PIN was sent to Firebase.\n");
            SessionState.SetBool("RehearPinWaitFirebase",true);
            SessionState.SetFloat("RehearPinFirebaseDeadline",(float)EditorApplication.timeSinceStartup+15f);
        }
        catch(Exception e){ File.AppendAllText("Temp/RehearOpeningPin.txt","FAIL PLAY MODE: "+e+"\n"); }
        finally { if(!SessionState.GetBool("RehearPinWaitFirebase",false))EditorApplication.delayCall += EditorApplication.ExitPlaymode; }
    }
    static void Capture(string name,float time)
    {
        typeof(RehearOpeningStartSetup).GetMethod("Capture",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)
            .Invoke(null,new object[]{name,time});
    }
}
