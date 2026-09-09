using System;
using System.IO;
using System.Linq;
using System.Reflection;
using CurvedUI;
using LeTai.Asset.TranslucentImage;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;

[InitializeOnLoad]
internal static class RehearFeedbackFigmaSetup
{
    static readonly BindingFlags Flags=BindingFlags.NonPublic|BindingFlags.Static;
    static readonly Color Ink=new Color32(3,8,18,255), Blue=new Color32(0,51,255,255);
    static Scene03Manager manager;
    static TranslucentImageSource blur;
    static RehearFeedbackFigmaSetup()=>EditorApplication.update+=Poll;
    static void Poll()
    {
        const string request="Temp/RehearFeedbackFigma.request";
        if(!File.Exists(request)||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)return;
        File.Delete(request);
        try{Build();}catch(Exception e){File.WriteAllText("Temp/RehearFeedbackFigma.txt",e.ToString());}
    }
    static RectTransform Rect(Transform p,string n,float x,float y,float w,float h)=>
        (RectTransform)typeof(RehearSessionReadySetup).GetMethod("Rect",Flags).Invoke(null,new object[]{p,n,x,y,w,h});
    static TMP_Text Text(Transform p,string n,string v,float x,float y,float w,float h,float size,bool bold=false,Color? color=null)=>
        (TMP_Text)typeof(RehearSessionReadySetup).GetMethod("Text",Flags).Invoke(null,new object[]{p,n,v,x,y,w,h,size,bold,color??Ink});
    static void Assign(string name,Object value)=>typeof(Scene03Manager).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(manager,value);
    static Material Mat(string n,float w,float h,float radius,float border,bool glass)
    {
        string path="Assets/Settings/TutorialUI/Feedback "+n+".mat";
        var m=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!m){m=new Material(Shader.Find("Rehear/UI/Rounded Translucent Panel"));AssetDatabase.CreateAsset(m,path);}
        m.SetVector("_PanelSize",new Vector4(w,h,0,0));m.SetFloat("_Radius",radius);m.SetFloat("_BorderWidth",border);m.SetFloat("_UseBlur",glass?1:0);
        m.SetFloat("_GlassTint",.45f);m.SetFloat("_UseFigmaGlow",glass?1:0);
        m.SetTexture("_FigmaGlowTex",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/UI/FigmaOpening/top-radial.png"));
        m.SetVector("_FigmaDesignSize",new Vector4(w,h,0,0));m.SetVector("_FigmaGlowRect",new Vector4(w==1000?-150:-248,w==1000?-390:-425,1300,620));
        EditorUtility.SetDirty(m);return m;
    }
    static RectTransform Panel(Transform p,string n,float x,float y,float w,float h,bool glass=true)
    {
        var r=Rect(p,n,x,y,w,h);
        Image im;
        if(glass){var g=r.gameObject.AddComponent<TranslucentImage>();g.source=blur;g.foregroundOpacity=.45f;im=g;}
        else im=r.gameObject.AddComponent<Image>();
        im.material=Mat(n,w,h,glass?48:24,glass?2:1,glass);im.color=glass?Color.white:new Color(1,1,1,.22f);im.raycastTarget=false;
        im.canvasRenderer.cullTransparentMesh=false;return r;
    }
    static void Icon(Transform p,string n,string path,float x,float y,float w,float h)
    {
        var im=Rect(p,n,x,y,w,h).gameObject.AddComponent<Image>();im.sprite=AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if(!im.sprite)throw new Exception("Missing Figma sprite "+path);
        im.preserveAspect=true;im.raycastTarget=false;
    }
    static void Logo(Transform p)=>Icon(p,"Figma Logo","Assets/Textures/UI/FigmaOpening/logo.png",36,30,140.387f,34);
    static void Button(Transform p,string n,string label,float x,float y,float w,UnityEngine.Events.UnityAction action,bool outline=false)
    {
        var r=Rect(p,n,x,y,w,76);var im=r.gameObject.AddComponent<Image>();im.material=Mat(n,w,76,40,outline?1:0,false);
        im.color=outline?new Color(1,1,1,0):(QualitySettings.activeColorSpace==ColorSpace.Linear?Blue.linear:Blue);
        // Keep the border visible while the center remains transparent.
        if(outline){im.color=Color.white;im.material.SetFloat("_FillAlphaOffset",1);}
        var b=r.gameObject.AddComponent<Button>();b.targetGraphic=im;
        UnityEventTools.AddPersistentListener(b.onClick,action);
        Text(r,"Label",label,0,0,w,76,26,false,Color.white);
    }
    static void Build()
    {
        typeof(RehearFeedbackPreview).GetMethod("Stop",Flags).Invoke(null,null);
        var scene=EditorSceneManager.GetActiveScene();
        if(scene.path!="Assets/01_Scene/Scene_03_Feedback.unity")throw new Exception("Open feedback scene only.");
        foreach(var camera in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Camera>(true))) {
            camera.allowHDR=true;
            EditorUtility.SetDirty(camera);PrefabUtility.RecordPrefabInstancePropertyModifications(camera);
        }
        manager=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Scene03Manager>(true)).Single();
        var score=(TMP_Text)typeof(Scene03Manager).GetField("scoreText",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(manager);
        var canvas=score.GetComponentInParent<Canvas>();var root=(RectTransform)canvas.transform;
        blur=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<TranslucentImageSource>(true)).First(c=>c.GetComponent<Camera>()?.CompareTag("MainCamera")==true);
        foreach(var path in new[]{"engagement","credibility","clarity"}.Select(n=>"Assets/Textures/UI/FigmaFeedback/"+n+"-icon.png"))
        {
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;importer.alphaIsTransparency=true;importer.mipmapEnabled=false;importer.SaveAndReimport();
        }
        foreach(Transform child in root.Cast<Transform>().ToArray())Object.DestroyImmediate(child.gameObject);
        root.sizeDelta=new Vector2(1000,1127);root.pivot=new Vector2(.5f,.5f);root.localScale=Vector3.one*.00085f;
        var panel=Panel(root,"Panel_Result",0,0,1000,1127);Logo(panel);
        Assign("scoreText",Text(panel,"Score","--",337,151,292,251,220,true,Blue));
        var number=(TMP_Text)typeof(Scene03Manager).GetField("scoreText",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(manager);
        number.font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/07_Fonts/ArchivoBlack-Regular SDF.asset");
        number.enableVertexGradient=true;number.color=Color.white;number.colorGradient=new VertexGradient(Blue,Blue,new Color32(0,6,61,255),new Color32(0,6,61,255));
        Text(panel,"Heading","이번 세션 결과",170,100,660,56,40,true);
        Text(panel,"Points","점",640,296,55,55,45.72f,false,new Color32(0,39,205,255));
        string[] keys={"engagement","credibility","clarity"},names={"몰입도","신뢰도","명확도"};
        Color[] colors={Blue,new Color32(69,34,196,255),new Color32(159,227,0,255)};
        for(int i=0;i<3;i++)
        {
            float x=44+i*310;
            Text(panel,keys[i]+" Title",names[i],x,416,292,38,27,true);
            var r=Rect(panel,keys[i]+" Ring",x+74,466,144,144);var ring=r.gameObject.AddComponent<FeedbackScoreRing>();ring.color=colors[i];ring.raycastTarget=false;Assign(keys[i]+"Ring",ring);
            Icon(panel,keys[i]+" Figma Icon","Assets/Textures/UI/FigmaFeedback/"+keys[i]+"-icon.png",x+74,466,144,144);
            Assign(keys[i]+"Text",Text(panel,keys[i]+" Rating","-",x,622,292,36,26,true,colors[i]));
            Assign(keys[i]+"Description",Text(panel,keys[i]+" Description","",x+24,670,244,68,24));
            Assign(keys[i]+"Badge",null);
        }
        var practice=Panel(panel,"Next Practice",44,801,912,126,false);
        var title=Text(practice,"Title","다음 연습",28,24,856,31,22,true);title.alignment=TextAlignmentOptions.Left;Assign("practiceTitle",title);
        var desc=Text(practice,"Description","",28,63,856,39,28,true);desc.alignment=TextAlignmentOptions.Left;Assign("practiceDescription",desc);
        Text(panel,"Web Hint","자세한 분석은 웹 리포트에서 확인하세요.",44,955,912,34,24,false,Blue);
        Button(panel,"Retry","다시 연습하기",44,1007,448,manager.ShowRetryConfirmation,true);
        Button(panel,"End","세션 종료하기",508,1007,448,manager.GoToScene1);
        var retry=Panel(root,"Retry Confirmation",98,318.5f,804,490);Logo(retry);
        Text(retry,"Heading","같은 세션을 다시 연습할까요?",40,124,724,66,44,true);
        Text(retry,"Body","다시 연습해도 방금 완료한 결과는\n웹 리포트에서 확인할 수 있어요.",60,210,684,100,32);
        Button(retry,"Back","이전으로",44,374,350,manager.BackToResults,true);
        Button(retry,"Confirm Retry","다시 연습하기",410,374,350,manager.GoToScene2);
        var ended=Panel(root,"Session Ended",98,318.5f,804,490);Logo(ended);
        Text(ended,"Heading","세션이 종료되었습니다",40,124,724,66,44,true);
        Text(ended,"Body","이제 VR 기기를 내려놓고\n모니터에서 웹 리포트를 확인하세요.",40,210,724,100,32);
        Button(ended,"Return","시작 화면으로 돌아가기",178,374,448,manager.ReturnToStart);
        Assign("retryConfirmationPanel",retry.gameObject);Assign("sessionEndedPanel",ended.gameObject);
        typeof(Scene03Manager).GetField("resultObjects",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(manager,new[]{panel.gameObject});
        retry.gameObject.SetActive(false);ended.gameObject.SetActive(false);
        var sign=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<TMP_Text>(true)).FirstOrDefault(t=>!t.transform.IsChildOf(root)&&(t.text=="Q&A"||t.text=="발표 시작하기"||t.name.Contains("Timer")));
        if(!sign)
        {
            var reference=EditorSceneManager.OpenPreviewScene("Assets/01_Scene/Scene_02_Presentation.unity");
            try
            {
                var original=reference.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<PresentationController>(true)).Single().timerText;
                var board=new GameObject("Feedback Completion Sign",typeof(RectTransform),typeof(Canvas));
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(board,scene);
                board.layer=5;board.transform.SetPositionAndRotation(original.transform.position,original.transform.rotation);board.transform.localScale=original.transform.lossyScale;
                var cv=board.GetComponent<Canvas>();cv.renderMode=RenderMode.WorldSpace;cv.worldCamera=canvas.worldCamera;
                ((RectTransform)board.transform).sizeDelta=original.rectTransform.rect.size;
                sign=Object.Instantiate(original,board.transform,false);sign.name="Feedback Timer Sign";
                sign.rectTransform.anchorMin=sign.rectTransform.anchorMax=sign.rectTransform.pivot=new Vector2(.5f,.5f);
                sign.rectTransform.anchoredPosition3D=Vector3.zero;sign.transform.localRotation=Quaternion.identity;sign.transform.localScale=Vector3.one;
                sign.rectTransform.sizeDelta=original.rectTransform.rect.size;
            }
            finally{EditorSceneManager.ClosePreviewScene(reference);}
        }
        if(sign){sign.text="수고하셨습니다";sign.enableAutoSizing=true;sign.fontSizeMax=220;sign.ForceMeshUpdate();Assign("completionSign",sign);EditorUtility.SetDirty(sign);PrefabUtility.RecordPrefabInstancePropertyModifications(sign);}
        var sync=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<TutorialBlurCameraSync>(true)).Single();sync.panels=root.GetComponentsInChildren<TranslucentImage>(true);EditorUtility.SetDirty(sync);
        canvas.GetComponent<CurvedUISettings>()?.AddEffectToChildren();
        foreach(var ring in root.GetComponentsInChildren<FeedbackScoreRing>(true))ring.GetComponent<CurvedUIVertexEffect>().DoNotTesselate=true;
        manager.DisplayReport(null);Canvas.ForceUpdateCanvases();
        EditorUtility.SetDirty(manager);EditorUtility.SetDirty(root);EditorSceneManager.MarkSceneDirty(scene);AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);
        File.WriteAllText("Temp/RehearFeedbackFigma.txt","PASS Figma 2883:18140 / 18221 / 18206 applied; sign="+(sign?sign.text:"MISSING"));
        File.WriteAllText("Temp/RehearFeedbackPreview.request","show");
    }
}
