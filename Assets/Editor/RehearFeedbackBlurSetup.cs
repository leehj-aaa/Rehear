using System;
using System.IO;
using System.Linq;
using System.Reflection;
using CurvedUI;
using LeTai.Asset.TranslucentImage;
using LeTai.Asset.TranslucentImage.UniversalRP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object=UnityEngine.Object;

[InitializeOnLoad]
internal static class RehearFeedbackBlurSetup
{
    static RehearFeedbackBlurSetup()=>EditorApplication.update+=Poll;
    static void Poll()
    {
        const string path="Temp/RehearFeedbackBlur.request";
        if(!File.Exists(path)||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)return;
        File.Delete(path);
        try { Run(); }catch(Exception e){File.WriteAllText("Temp/RehearFeedbackBlur.txt",e.ToString());}
    }
    static void Run()
    {
        typeof(RehearFeedbackPreview).GetMethod("Stop",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,null);
        var scene=SceneManager.GetSceneByPath("Assets/01_Scene/Scene_03_Feedback.unity");
        var roots=scene.GetRootGameObjects();
        var image=roots.SelectMany(g=>g.GetComponentsInChildren<Image>(true)).Single(i=>i.name=="Panel_Result");
        var canvas=image.GetComponentInParent<Canvas>();
        var camera=roots.SelectMany(g=>g.GetComponentsInChildren<Camera>(true)).Single(c=>c.CompareTag("MainCamera"));
        var panel=image as TranslucentImage;
        if(!panel) {
            var go=image.gameObject; var sprite=image.sprite; var imageType=image.type;
            Undo.DestroyObjectImmediate(image);
            panel=Undo.AddComponent<TranslucentImage>(go);panel.sprite=sprite;panel.type=imageType;
        }
        const string configPath="Assets/Settings/Feedback Result Blur.asset";
        var config=AssetDatabase.LoadAssetAtPath<ScalableBlurConfig>(configPath);
        if(!config){config=ScriptableObject.CreateInstance<ScalableBlurConfig>();AssetDatabase.CreateAsset(config,configPath);}
        config.Mode=ScalableBlurConfig.BlurMode.Performance;config.Strength=18;config.UseStrength=true;config.ReferenceResolution=new Vector2(1920,1080);EditorUtility.SetDirty(config);
        var renderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/Project Configuration/Android Preset.asset");
        if(!renderer.rendererFeatures.OfType<TranslucentImageBlurSource>().Any(f=>f.isActive))throw new Exception("URP blur renderer feature is not active.");
        var source=camera.GetComponent<TranslucentImageSource>();if(!source)source=Undo.AddComponent<TranslucentImageSource>(camera.gameObject);
        source.BlurConfig=config;source.Downsample=2;source.MaxUpdateRate=float.PositiveInfinity;source.BlurRegion=new Rect(0,0,1,1);source.SkipCulling=true;source.Preview=false;
        const string materialPath="Assets/Settings/Feedback Result Glass.mat";
        var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if(!material){material=new Material(Shader.Find("Rehear/UI/Rounded Translucent Panel"));AssetDatabase.CreateAsset(material,materialPath);}
        var rect=panel.rectTransform.rect;
        material.SetVector("_PanelSize",new Vector4(rect.width,rect.height,0,0));
        material.SetFloat("_Radius",32);material.SetFloat("_BorderWidth",1.5f);material.SetFloat("_UseBlur",1);material.SetFloat("_GlassTint",.38f);
        panel.material=material;panel.source=source;panel.color=Color.white;panel.foregroundOpacity=.38f;panel.vibrancy=1;panel.brightness=0;panel.flatten=0;
        panel.textureAlphaMode=TranslucentImage.TextureAlphaMode.Alpha;panel.raycastTarget=false;
        canvas.additionalShaderChannels|=AdditionalCanvasShaderChannels.TexCoord1|AdditionalCanvasShaderChannels.TexCoord2|AdditionalCanvasShaderChannels.TexCoord3;
        int uiLayer=LayerMask.NameToLayer("UI");
        foreach(var t in canvas.GetComponentsInChildren<Transform>(true))t.gameObject.layer=uiLayer;
        canvas.worldCamera=camera;
        canvas.GetComponent<CurvedUISettings>()?.AddEffectToChildren();
        var overlay=camera.transform.Find("Feedback UI Overlay Camera");
        if(!overlay){var go=new GameObject("Feedback UI Overlay Camera");go.transform.SetParent(camera.transform,false);overlay=go.transform;}
        var uiCamera=overlay.GetComponent<Camera>();if(!uiCamera)uiCamera=Undo.AddComponent<Camera>(overlay.gameObject);uiCamera.CopyFrom(camera);uiCamera.cullingMask=1<<uiLayer;
        var uiData=uiCamera.GetUniversalAdditionalCameraData();uiData.renderType=CameraRenderType.Overlay;var serializedUi=new SerializedObject(uiData);serializedUi.FindProperty("m_ClearDepth").boolValue=false;serializedUi.ApplyModifiedPropertiesWithoutUndo();uiData.renderPostProcessing=false;uiData.renderShadows=false;uiData.allowXRRendering=true;uiData.SetRenderer(0);
        camera.cullingMask&=~(1<<uiLayer);
        var baseData=camera.GetUniversalAdditionalCameraData();if(!baseData.cameraStack.Contains(uiCamera))baseData.cameraStack.Add(uiCamera);
        var sync=camera.GetComponent<TutorialBlurCameraSync>();if(!sync)sync=Undo.AddComponent<TutorialBlurCameraSync>(camera.gameObject);
        sync.sceneCamera=camera;sync.uiCamera=uiCamera;sync.blurSource=source;sync.panels=new[]{panel};
        foreach(var obj in new Object[]{panel,canvas,camera,source,baseData,uiCamera,uiData,sync,material}) {EditorUtility.SetDirty(obj);PrefabUtility.RecordPrefabInstancePropertyModifications(obj);}
        panel.SetAllDirty();AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        File.WriteAllText("Temp/RehearFeedbackBlur.txt","PASS result panel uses live blur; strength=18, tint=.38; text unchanged; world/UI camera stack saved.");
        File.WriteAllText("Temp/RehearFeedbackPreview.request","show");
    }
}
