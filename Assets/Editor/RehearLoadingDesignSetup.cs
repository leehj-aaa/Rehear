using System;
using System.IO;
using System.Linq;
using TMPro;
using CurvedUI;
using LeTai.Asset.TranslucentImage;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

// Explicit authoring/preview requests; normal editor reloads do not modify the scene.
[InitializeOnLoad]
internal static class RehearLoadingDesignSetup
{
    const string Request = "Temp/RehearLoadingDesign.request";
    const string Report = "Temp/RehearLoadingDesign.txt";
    const string Art = "Assets/Textures/UI/FigmaOpening/";
    const string Caption = "잠시만 기다려주세요.\n정교한 환경 조성을 위해 데이터를 불러오는 중입니다.";
    static RehearLoadingDesignSetup() => EditorApplication.update += Poll;
    static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        string command;
        try { command = File.ReadAllText(Request).Trim(); File.Delete(Request); }
        catch (IOException) { return; }
        try
        {
            if (command == "apply") Apply();
            else if (command == "preview" || command == "restore") Preview(command == "preview");
            else if (command == "lighting") Lighting();
            else if (command == "capture") ScreenCapture.CaptureScreenshot("Temp/RehearLoading-final.png");
            else if (command == "capture-room") CaptureRoom();
            else if (command == "authored-postfx") PreferAuthoredPostFx();
            else if (command == "open-tutorial" && !EditorApplication.isPlaying)
                EditorSceneManager.OpenScene("Assets/01_Scene/Scene_00_5_Tutorial.unity");
            else if (command == "open-opening" && !EditorApplication.isPlaying)
                EditorSceneManager.OpenScene("Assets/01_Scene/Scene_00.unity");
        }
        catch (Exception e) { File.AppendAllText(Report, "FAIL " + e + "\n"); Debug.LogException(e); }
    }
    static Transform Root() => Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None)
        .Single(c => c.name == "OpeningPinCanvas").transform;
    static void Rect(RectTransform r, float x, float y, float width, float height, float scale)
    {
        r.anchorMin = r.anchorMax = r.pivot = new Vector2(.5f,.5f);
        r.anchoredPosition = new Vector2(x+width/2-402,288.5f-y-height/2)*scale;
        r.sizeDelta = new Vector2(width,height)*scale;
        r.localScale = Vector3.one;
    }
    static Sprite Sprite(string file)
    {
        string path = Art + file;
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }
    static void Apply()
    {
        if (EditorApplication.isPlaying || EditorSceneManager.GetActiveScene().name != "Scene_00")
            throw new InvalidOperationException("Apply only in opening scene Edit mode.");
        var root = Root();
        var panel = (RectTransform)root.Find("LoadingPanel");
        Undo.RegisterFullObjectHierarchyUndo(root.gameObject,"Match Figma session loading");
        float scale = ((RectTransform)root.Find("PinPanel")).rect.width/804f;
        panel.sizeDelta = new Vector2(804,577)*scale;
        panel.anchoredPosition = ((RectTransform)root.Find("PinPanel")).anchoredPosition;
        var glass = panel.GetComponent<TranslucentImage>();
        glass.color = Color.white;
        glass.foregroundOpacity = .45f;
        var mat = glass.material;
        mat.SetVector("_PanelSize",new Vector4(panel.rect.width,panel.rect.height,0,0));
        mat.SetFloat("_Radius",48*scale);
        mat.SetFloat("_BorderWidth",2*scale);
        mat.SetFloat("_GlassTint",.45f);
        mat.SetFloat("_TopGlowStrength",0);
        Sprite("top-radial.png");
        mat.SetTexture("_FigmaGlowTex",AssetDatabase.LoadAssetAtPath<Texture2D>(Art+"top-radial.png"));
        mat.SetFloat("_UseFigmaGlow",1);
        // The original ellipse is clipped by the rounded glass shader, as in Figma.
        var oldGlow = panel.Find("Top Radial Light");
        if (oldGlow) Undo.DestroyObjectImmediate(oldGlow.gameObject);
        var icon = panel.Find("Session Loading Icon");
        if (!icon)
        {
            var go = new GameObject("Session Loading Icon",typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));
            Undo.RegisterCreatedObjectUndo(go,"Figma loading icon");
            go.transform.SetParent(panel,false); icon=go.transform;
        }
        var iconImage=icon.GetComponent<Image>();
        iconImage.sprite=Sprite("session-loading.png"); iconImage.color=Color.white; iconImage.raycastTarget=false;
        Rect((RectTransform)icon,344,157,116,116,scale);
        var logo=panel.Find("BrandLogo").GetComponent<Image>();
        logo.sprite=Sprite("logo.png"); logo.color=Color.white; logo.preserveAspect=true; logo.raycastTarget=false;
        Rect(logo.rectTransform,36,33,132.709f,34,scale);
        var title=panel.Find("Title").GetComponent<TMP_Text>();
        SetText(title,"세션 불러오는 중","Assets/07_Fonts/PretendardTMP/Pretendard-Bold SDF.asset",36*scale,new Color32(3,8,18,255));
        Rect(title.rectTransform,288,313,232,43,scale);
        var description=panel.Find("Description").GetComponent<TMP_Text>();
        const string fontPath="Assets/07_Fonts/PretendardTMP/Pretendard-Medium SDF.asset";
        var font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontPath);
        if (!font) throw new InvalidOperationException("Pretendard Medium SDF asset missing.");
        SetText(description,"<line-height=135%>"+Caption,fontPath,20*scale,new Color32(53,56,65,255));
        Rect(description.rectTransform,190,372,424,54,scale);
        foreach(var child in panel.GetComponentsInChildren<Transform>(true)) child.gameObject.layer=LayerMask.NameToLayer("UI");
        root.GetComponent<CurvedUISettings>().AddEffectToChildren();
        EditorUtility.SetDirty(font); EditorUtility.SetDirty(mat);
        EditorSceneManager.MarkSceneDirty(panel.gameObject.scene);
        AssetDatabase.SaveAssets(); EditorSceneManager.SaveScene(panel.gameObject.scene);
        File.AppendAllText(Report,"PASS Figma 2323:38954: panel804x577 radius48 border2 tint45%, icon116, title36, caption20, clipped source radial.\n");
    }
    static void SetText(TMP_Text text,string value,string fontPath,float size,Color color)
    {
        text.text=value; text.font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontPath);
        text.fontSharedMaterial=text.font.material; text.fontStyle=FontStyles.Normal;
        text.fontSize=size; text.enableAutoSizing=false; text.color=color;
        text.alignment=TextAlignmentOptions.Center; text.characterSpacing=-1;
        text.textWrappingMode=TextWrappingModes.NoWrap; text.overflowMode=TextOverflowModes.Overflow;
        text.margin=Vector4.zero; text.raycastTarget=false;
    }
    static void Preview(bool show)
    {
        if(!EditorApplication.isPlaying) throw new InvalidOperationException("Preview in Play mode after opening PIN.");
        var root=Root(); root.Find("PinPanel").gameObject.SetActive(!show);
        root.Find("NumberKeyboard").gameObject.SetActive(false);
        root.Find("LoadingPanel").gameObject.SetActive(show);
        File.AppendAllText(Report,show?"PREVIEW loading only; no PIN query.\n":"RESTORED PIN input.\n");
    }
    internal static void Lighting()
    {
        var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var renderers=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MeshRenderer>(true)).ToArray();
        var maps=LightmapSettings.lightmaps;
        string rows=string.Join("\n",renderers.Where(r=>r.gameObject.isStatic).Select(r=>$"{r.name}: index={r.lightmapIndex} scale={r.lightmapScaleOffset} enabled={r.enabled} GI={r.receiveGI} layer={r.gameObject.layer} bounds={r.bounds}"));
        string cameraRows=string.Join("\n",scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Camera>(true))
            .Select(c=>$"CAMERA {c.name} active={c.isActiveAndEnabled} post={c.GetUniversalAdditionalCameraData().renderPostProcessing} mask={c.cullingMask} position={c.transform.position} rotation={c.transform.eulerAngles}"));
        string volumeRows=string.Join("\n",scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Volume>(true))
            .Select(v=>$"VOLUME {v.name} active={v.isActiveAndEnabled} weight={v.weight} profile={v.sharedProfile?.name} temperature="+
                (v.sharedProfile && v.sharedProfile.TryGet<WhiteBalance>(out var white)?white.temperature.value.ToString():"none")));
        string probeRows=$"PROBES count={LightmapSettings.lightProbes?.count} groups="+scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<LightProbeGroup>(true)).Count()+"\n"+
            string.Join("\n",scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<SkinnedMeshRenderer>(true)).Take(20)
                .Select(r=>$"SKIN {r.name} probes={r.lightProbeUsage} anchor={r.probeAnchor?.name} position={r.bounds.center}"));
        string lightRows=string.Join("\n",scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Light>(true))
            .Select(l=>$"LIGHT {l.name} type={l.type} bake={l.lightmapBakeType} intensity={l.intensity} temperature={l.colorTemperature} useTemperature={l.useColorTemperature} color={l.color}"));
        File.WriteAllText("Temp/RehearLighting-"+scene.name+"-"+(EditorApplication.isPlaying?"play":"edit")+".txt",
            $"scene={scene.name} play={EditorApplication.isPlaying} data={AssetDatabase.GetAssetPath(Lightmapping.lightingDataAsset)} maps={maps.Length} mode={LightmapSettings.lightmapsMode}\n"+
            string.Join("\n",maps.Select(m=>"color="+AssetDatabase.GetAssetPath(m.lightmapColor)+" direction="+AssetDatabase.GetAssetPath(m.lightmapDir)))+
            $"\nambient={RenderSettings.ambientMode} intensity={RenderSettings.ambientIntensity} reflection={RenderSettings.reflectionIntensity} sky={RenderSettings.skybox?.name}\n"+rows+"\n"+cameraRows+"\n"+volumeRows+"\n"+probeRows+"\n"+lightRows);
    }
    static void PreferAuthoredPostFx()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.name != "Scene_00_5_Tutorial") throw new InvalidOperationException("Tutorial only.");
        var volume = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Volume>(true))
            .Single(v => v.name == "Global Volume");
        // Keep the room's authored PostFX at priority 0 above the generic XR defaults.
        // No profile values, lights, probes or baked textures are changed.
        Undo.RecordObject(volume, "Preserve authored tutorial PostFX priority");
        volume.priority = -1;
        if (!EditorApplication.isPlaying)
        {
            EditorUtility.SetDirty(volume);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        File.AppendAllText(Report, "Authored room PostFX overrides generic XR defaults; profile values unchanged.\n");
    }
    static void CaptureRoom()
    {
        if(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name!="Scene_00_5_Tutorial")throw new InvalidOperationException("Capture tutorial only.");
        var original=Camera.main;
        var go=new GameObject("Temporary lighting comparison camera"){hideFlags=HideFlags.HideAndDontSave};
        var target=new RenderTexture(1600,900,24,RenderTextureFormat.ARGB32);
        var previous=RenderTexture.active;
        var pixels=new Texture2D(1600,900,TextureFormat.RGB24,false);
        try
        {
            var camera=go.AddComponent<Camera>();camera.CopyFrom(original);camera.enabled=false;
            camera.cullingMask&=~(1<<LayerMask.NameToLayer("UI"));camera.stereoTargetEye=StereoTargetEyeMask.None;
            var data=camera.GetUniversalAdditionalCameraData();data.renderType=CameraRenderType.Base;data.renderPostProcessing=true;data.SetRenderer(0);
            go.transform.SetPositionAndRotation(new Vector3(33.8f,1.5f,-14.45f),Quaternion.Euler(0,180,0));camera.fieldOfView=65;
            var request=new RenderPipeline.StandardRequest{destination=target};
            RenderPipeline.SubmitRenderRequest(camera,request);
            RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1600,900),0,0);pixels.Apply();
            File.WriteAllBytes("Temp/RehearLighting-after.png",pixels.EncodeToPNG());
        }
        finally {RenderTexture.active=previous;Object.DestroyImmediate(pixels);Object.DestroyImmediate(target);Object.DestroyImmediate(go);}
    }
}

