using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;
using LeTai.Asset.TranslucentImage;
using Object = UnityEngine.Object;

[InitializeOnLoad]
internal static class RehearFeedbackPreview
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly List<GameObject> hidden = new List<GameObject>();
    static readonly List<GameObject> suspended = new List<GameObject>();
    static GameObject audienceRoot, canvasRoot;
    static AudienceAnimationPlayer[] bodies;
    static PlayableGraph[] graphs;
    static double lastTime;
    static float elapsed;
    static bool captured;
    static Scene feedback;
    static Camera sourceCamera;
    static Canvas originalCanvas;
    static AudioClip previewAudio;
    static Camera blurCamera;
    static RenderTexture blurTarget;
    static TranslucentImage[] glassPanels;
    static TutorialBlurCameraSync blurSync;
    static TranslucentImage[] originalGlassPanels;

    static RehearFeedbackPreview()
    {
        EditorApplication.update += Tick;
        AssemblyReloadEvents.beforeAssemblyReload += Stop;
        EditorSceneManager.sceneSaving += (s, path) => Stop();
        EditorApplication.playModeStateChanged += s => { if(s == PlayModeStateChange.ExitingEditMode) Stop(); };
        EditorSceneManager.sceneClosing += (s, removing) => { if(s == feedback) Stop(); };
        SceneView.duringSceneGui += view => {
            if(!audienceRoot) return;
            Handles.BeginGUI();
            GUI.Box(new Rect(15,45,390,55), "피드백 미리보기 · 예시 결과 82점\n청중 6명 · 서로 다른 박수 애니메이션");
            if(GUI.Button(new Rect(15,105,130,28), "미리보기 종료")) Stop();
            Handles.EndGUI();
        };
    }

    static void Tick()
    {
        if(EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        try
        {
            const string request = "Temp/RehearFeedbackPreview.request";
            if(File.Exists(request)) {
                var command=File.ReadAllText(request).Trim(); File.Delete(request);
                if(command=="refresh") { AssetDatabase.Refresh(); return; }
                if(command=="audio-check") { CheckAudio(); return; }
                if(command=="audio-reset") {
                    bool reset=AudioSettings.Reset(AudioSettings.GetConfiguration());
                    if(audienceRoot)StartPreviewAudio(audienceRoot.GetComponent<FeedbackAudienceApplause>());
                    CheckAudio();File.AppendAllText("Temp/RehearFeedbackAudioCheck.txt","\nAudio device reset="+reset);return;
                }
                if(command=="stop")Stop(); else Show();
            }
            if(!audienceRoot || EditorApplication.isPlayingOrWillChangePlaymode) return;
            float dt=Mathf.Clamp((float)(EditorApplication.timeSinceStartup-lastTime),0,.1f); lastTime=EditorApplication.timeSinceStartup; elapsed+=dt;
            for(int i=0;i<bodies.Length;i++) {
                typeof(AudienceAnimationPlayer).GetMethod("Advance",Private).Invoke(bodies[i],new object[]{dt});
                graphs[i].Evaluate(dt); bodies[i].ApplyPropPoses();
            }
            UpdatePreviewBlur();
            if(!captured && elapsed>=2f) { Capture(); captured=true; }
            SceneView.RepaintAll();
        }
        catch(Exception e) { File.WriteAllText("Temp/RehearFeedbackPreview.txt",e.ToString()); Stop(); }
    }

    [MenuItem("Rehear/Preview Feedback With Applause")]
    static void Show()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play mode before opening this edit-mode preview.");
        Stop();
        var current=SceneManager.GetActiveScene();
        var previous=current.GetRootGameObjects().FirstOrDefault(g=>g.name=="Audience Preview (Editor Only)");
        if(previous) FeedbackAudienceApplause.Capture(previous.GetComponent<AudienceSeating>());
        typeof(RehearAudienceMotionPreview).GetMethod("StopPreview",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,null);
        feedback=SceneManager.GetSceneByPath("Assets/01_Scene/Scene_03_Feedback.unity");
        if(!feedback.isLoaded) feedback=EditorSceneManager.OpenScene("Assets/01_Scene/Scene_03_Feedback.unity",OpenSceneMode.Additive);
        SceneManager.SetActiveScene(feedback);
        for(int i=0;i<SceneManager.sceneCount;i++) {
            var scene=SceneManager.GetSceneAt(i); if(scene==feedback)continue;
            // Scene visibility alone does not isolate lights, volumes, custom
            // UI renderers, or reflection probes from an additive scene.
            foreach(var go in scene.GetRootGameObjects()) Suspend(go);
        }
        var roots=feedback.GetRootGameObjects();
        var template=roots.Single(g=>g.name=="FeedbackAudience");
        audienceRoot=Object.Instantiate(template); audienceRoot.name="Feedback Applause Preview (Editor Only)";
        SceneManager.MoveGameObjectToScene(audienceRoot,feedback); audienceRoot.hideFlags=HideFlags.DontSave; Suspend(template);
        var owner=audienceRoot.GetComponent<FeedbackAudienceApplause>(); owner.InitializeAudience(); bodies=owner.OrderedAudience();
        graphs=bodies.Select(b=> {
            b.enabled=true;
            foreach(var skin in b.GetComponentsInChildren<SkinnedMeshRenderer>()) { skin.updateWhenOffscreen=true; skin.forceMatrixRecalculationPerRender=true; }
            foreach(var animator in b.GetComponentsInChildren<Animator>()) { animator.enabled=true; animator.cullingMode=AnimatorCullingMode.AlwaysAnimate; }
            typeof(AudienceAnimationPlayer).GetMethod("CreateAnimationGraph",Private).Invoke(b,null);
            var graph=(PlayableGraph)typeof(AudienceAnimationPlayer).GetField("playableGraph",Private).GetValue(b);
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual); return graph;
        }).ToArray();
        typeof(FeedbackAudienceApplause).GetMethod("Start",Private).Invoke(owner,null);

        var manager=roots.SelectMany(g=>g.GetComponentsInChildren<Scene03Manager>(true)).Single();
        var score=(TMP_Text)typeof(Scene03Manager).GetField("scoreText",Private).GetValue(manager);
        originalCanvas=score.GetComponentInParent<Canvas>();
        canvasRoot=Object.Instantiate(originalCanvas.gameObject); canvasRoot.name="Feedback Result Preview (Editor Only)";
        SceneManager.MoveGameObjectToScene(canvasRoot,feedback); canvasRoot.hideFlags=HideFlags.DontSave; Suspend(originalCanvas.gameObject);
        Transform Map(Transform t) => canvasRoot.transform.Find(AnimationUtility.CalculateTransformPath(t,originalCanvas.transform));
        foreach(var go in (GameObject[])typeof(Scene03Manager).GetField("resultObjects",Private).GetValue(manager))
            if(go && go.transform.IsChildOf(originalCanvas.transform)) Map(go.transform).gameObject.SetActive(true);
        var ended=(GameObject)typeof(Scene03Manager).GetField("sessionEndedPanel",Private).GetValue(manager);
        if(ended) Map(ended.transform).gameObject.SetActive(false);
        foreach(var field in new[]{"scoreText","engagementText","clarityText","credibilityText"}) {
            var source=(TMP_Text)typeof(Scene03Manager).GetField(field,Private).GetValue(manager);
            Map(source.transform).GetComponent<TMP_Text>().text=field=="scoreText"?"82":field=="clarityText"?"보통":"우수";
        }
        Canvas.ForceUpdateCanvases();
        sourceCamera=roots.SelectMany(g=>g.GetComponentsInChildren<Camera>(true)).First(c=>c.CompareTag("MainCamera"));
        SetupPreviewBlur();
        var view=SceneView.lastActiveSceneView;
        if(view && current!=feedback) { view.orthographic=false; view.LookAtDirect(sourceCamera.transform.position+sourceCamera.transform.forward*3f,sourceCamera.transform.rotation,1.5f); }
        elapsed=0;captured=false;lastTime=EditorApplication.timeSinceStartup;
        File.WriteAllText("Temp/RehearFeedbackPreview.txt","RUNNING feedback scene: sample score 82; six distinct applause clips; original scene objects/data unchanged.\nCamera="+sourceCamera.transform.position);
        var otherLights=Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Count(l=>l.enabled && l.gameObject.scene!=feedback);
        if(otherLights!=0 || originalCanvas.gameObject.activeInHierarchy) throw new Exception("Preview is not isolated from other scenes/original UI.");
        File.AppendAllText("Temp/RehearFeedbackPreview.txt", "\nPASS isolation: other-scene lights=0; original result canvas inactive; preview canvas active="+canvasRoot.activeInHierarchy);
        StartPreviewAudio(owner);
    }

    static void StartPreviewAudio(FeedbackAudienceApplause owner)
    {
        var settings=new SerializedObject(owner);
        var clip=settings.FindProperty("applauseLoop").objectReferenceValue as AudioClip;
        if(!clip)return;
        var audioUtil=typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
        var play=audioUtil?.GetMethod("PlayPreviewClip",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic,null,new[]{typeof(AudioClip),typeof(int),typeof(bool)},null);
        var stop=audioUtil?.GetMethod("StopAllPreviewClips",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic);
        if(play==null || stop==null)return;
        var samples=new float[clip.samples*clip.channels];
        clip.LoadAudioData();
        if(!clip.GetData(samples,0))return;
        float volume=settings.FindProperty("applauseVolume").floatValue;
        for(int i=0;i<samples.Length;i++)samples[i]*=volume;
        // Editor AudioUtil expects an imported asset for reliable native playback.
        const string path="Assets/Editor/FeedbackApplausePreview.wav";
        using(var writer=new BinaryWriter(File.Create(path))) {
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+samples.Length*2);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));writer.Write(16);writer.Write((short)1);writer.Write((short)clip.channels);
            writer.Write(clip.frequency);writer.Write(clip.frequency*clip.channels*2);writer.Write((short)(clip.channels*2));writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));writer.Write(samples.Length*2);
            foreach(float value in samples)writer.Write((short)Mathf.RoundToInt(Mathf.Clamp(value,-1,1)*32767));
        }
        AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
        previewAudio=AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        previewAudio.LoadAudioData();
        play.Invoke(null,new object[]{previewAudio,0,true});
        File.AppendAllText("Temp/RehearFeedbackPreview.txt","\nAUDIO preview: supplied applause loop, volume="+volume);
    }

    static void SetupPreviewBlur()
    {
        glassPanels=canvasRoot.GetComponentsInChildren<TranslucentImage>(true);
        if(glassPanels.Length==0)return;
        var source=sourceCamera.GetComponent<TranslucentImageSource>();
        if(!source)return;
        blurSync=sourceCamera.GetComponent<TutorialBlurCameraSync>();
        if(blurSync){originalGlassPanels=blurSync.panels;blurSync.panels=glassPanels;}
        var go=new GameObject("Feedback scene-view blur camera");SceneManager.MoveGameObjectToScene(go,feedback);go.hideFlags=HideFlags.HideAndDontSave;
        blurCamera=go.AddComponent<Camera>();blurCamera.CopyFrom(sourceCamera);blurCamera.enabled=false;blurCamera.scene=feedback;
        blurCamera.GetUniversalAdditionalCameraData().renderPostProcessing=false;
        var previewSource=go.AddComponent<TranslucentImageSource>();previewSource.BlurConfig=source.BlurConfig;previewSource.Downsample=2;previewSource.SkipCulling=true;previewSource.MaxUpdateRate=float.PositiveInfinity;
        blurTarget=new RenderTexture(1280,720,24);blurCamera.targetTexture=blurTarget;
        foreach(var panel in glassPanels)panel.source=previewSource;
    }

    static void UpdatePreviewBlur()
    {
        if(!blurCamera)return;
        var view=SceneView.lastActiveSceneView;if(!view)return;
        var camera=view.camera;blurCamera.transform.SetPositionAndRotation(camera.transform.position,camera.transform.rotation);
        blurCamera.projectionMatrix=camera.projectionMatrix;blurCamera.Render();
    }

    static void CheckAudio()
    {
        var type=typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
        var methods=type.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static);
        var lines=new List<string>();
        lines.Add("listener pause="+AudioListener.pause+" volume="+AudioListener.volume+" editor mute="+EditorUtility.audioMasterMute);
        lines.Add("PID="+System.Diagnostics.Process.GetCurrentProcess().Id+" dsp="+AudioSettings.dspTime+" output="+AudioSettings.outputSampleRate);
        if(previewAudio){var samples=new float[previewAudio.samples*previewAudio.channels];previewAudio.GetData(samples,0);lines.Add("clip peak="+samples.Max(Mathf.Abs)+" path="+AssetDatabase.GetAssetPath(previewAudio));}
        if(glassPanels!=null)foreach(var panel in glassPanels)lines.Add("GLASS source="+panel.source+" texture="+panel.source?.BlurredScreen+" shader="+panel.material.shader.name);
        foreach(var m in methods.Where(m=>m.Name.Contains("Preview") || m.Name.Contains("Volume") || m.Name.Contains("Device"))) {
            lines.Add(m.ToString());
            if(m.GetParameters().Length==0 && (m.Name.StartsWith("Is")||m.Name.StartsWith("Get")||m.Name.StartsWith("get_")))
                try{lines.Add("VALUE="+m.Invoke(null,null));}catch(Exception e){lines.Add(e.Message);}
        }
        File.WriteAllLines("Temp/RehearFeedbackAudioCheck.txt",lines);
    }

    static void Hide(GameObject go)
    {
        if(!SceneVisibilityManager.instance.IsHidden(go)) { hidden.Add(go); SceneVisibilityManager.instance.Hide(go,true); }
    }

    static void Suspend(GameObject go)
    {
        if(go.activeSelf) { suspended.Add(go); go.SetActive(false); }
    }

    [MenuItem("Rehear/Stop Feedback Preview")]
    static void Stop()
    {
        if(previewAudio) {
            typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil")?.GetMethod("StopAllPreviewClips",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)?.Invoke(null,null);
            previewAudio=null;
        }
        if(blurSync)blurSync.panels=originalGlassPanels;
        blurSync=null;originalGlassPanels=null;glassPanels=null;
        if(blurCamera)Object.DestroyImmediate(blurCamera.gameObject);
        blurCamera=null;
        if(blurTarget){blurTarget.Release();Object.DestroyImmediate(blurTarget);}blurTarget=null;
        if(audienceRoot) Object.DestroyImmediate(audienceRoot);
        if(canvasRoot) Object.DestroyImmediate(canvasRoot);
        audienceRoot=null; canvasRoot=null; bodies=null; graphs=null;
        foreach(var go in suspended) if(go) go.SetActive(true);
        suspended.Clear();
        foreach(var go in hidden) if(go) SceneVisibilityManager.instance.Show(go,true);
        hidden.Clear(); SceneView.RepaintAll();
    }

    static void Capture()
    {
        var camera=sourceCamera;var previousTarget=camera.targetTexture;var stereo=camera.stereoTargetEye;
        var sources=glassPanels?.Select(p=>p.source).ToArray();
        if(glassPanels!=null)foreach(var panel in glassPanels)panel.source=camera.GetComponent<TranslucentImageSource>();
        camera.stereoTargetEye=StereoTargetEyeMask.None;
        var rt=new RenderTexture(1600,900,24);var old=RenderTexture.active; bool enabled=originalCanvas.enabled;
        try {
            originalCanvas.enabled=false;camera.targetTexture=rt;camera.Render();
            // Camera.Render renders one camera, not the URP stack. Composite the
            // UI pass explicitly for this diagnostic capture only.
            var ui=blurSync?blurSync.uiCamera:null;
            if(ui) {
                var data=ui.GetUniversalAdditionalCameraData();var renderType=data.renderType;var flags=ui.clearFlags;var target=ui.targetTexture;
                try{data.renderType=CameraRenderType.Base;ui.clearFlags=CameraClearFlags.Nothing;ui.targetTexture=rt;ui.Render();}
                finally{data.renderType=renderType;ui.clearFlags=flags;ui.targetTexture=target;}
            }
            RenderTexture.active=rt;
            var tex=new Texture2D(1600,900,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1600,900),0,0);tex.Apply();
            File.WriteAllBytes("Temp/RehearFeedbackPreview.png",tex.EncodeToPNG());Object.DestroyImmediate(tex);
        }finally {originalCanvas.enabled=enabled;RenderTexture.active=old;camera.targetTexture=previousTarget;camera.stereoTargetEye=stereo;rt.Release();Object.DestroyImmediate(rt);
            if(sources!=null)for(int i=0;i<sources.Length;i++)glassPanels[i].source=sources[i];}
    }
}
