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
using Object=UnityEngine.Object;

// Example audio only; no microphone, report submission or production session edits.
[InitializeOnLoad]
internal static class RehearQuestionFlowPreview
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static GameObject root,uiRoot;
    static readonly List<GameObject> suspended=new List<GameObject>();
    static AudienceAnimationPlayer[] bodies;
    static AudienceGazeController[] gazes;
    static PlayableGraph[] graphs;
    static AudienceAnimationPlayer questionBody;
    static AudienceQuestionSpeaker speaker;
    static AudioClip clip;
    static readonly List<float[]> visemes=new List<float[]>();
    static float elapsed,stageTime,maxJaw;
    static double last;
    static int stage;
    static string status;
    static TMP_Text actionLabel;
    static bool previousMute;
    static Type AudioUtil=>typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
    static object Audio(string method,params object[] args)=>AudioUtil.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static).Single(m=>m.Name==method&&m.GetParameters().Length==args.Length).Invoke(null,args);

    static RehearQuestionFlowPreview()
    {
        EditorApplication.update+=Tick;
        AssemblyReloadEvents.beforeAssemblyReload+=Stop;
        EditorSceneManager.sceneSaving+=(s,p)=>Stop();
        EditorApplication.playModeStateChanged+=s=>{if(s==PlayModeStateChange.ExitingEditMode)Stop();};
        SceneView.duringSceneGui+=v=>{
            if(!root)return;
            Handles.BeginGUI();GUI.Box(new Rect(15,45,410,56),"예시 Q&A 미리보기 · 베이지색 청중\n"+status);
            if(GUI.Button(new Rect(15,106,100,26),"처음부터"))Restart();
            if(GUI.Button(new Rect(122,106,100,26),"미리보기 종료"))Stop();
            Handles.EndGUI();
        };
    }

    static void Tick()
    {
        if(EditorApplication.isCompiling||EditorApplication.isUpdating)return;
        try {
            const string request="Temp/RehearQuestionFlow.request";
            if(File.Exists(request)){string command=File.ReadAllText(request).Trim();File.Delete(request);if(command=="stop")Stop();else Show();}
            if(!root)return;
            float dt=Mathf.Clamp((float)(EditorApplication.timeSinceStartup-last),0,.1f);last=EditorApplication.timeSinceStartup;elapsed+=dt;stageTime+=dt;
            for(int i=0;i<bodies.Length;i++){
                typeof(AudienceAnimationPlayer).GetMethod("Advance",Private).Invoke(bodies[i],new object[]{dt});
                graphs[i].Evaluate(dt);bodies[i].ApplyPropPoses();
                if(gazes[i]) gazes[i].ApplyGaze(dt,elapsed);
            }
            if(stage==0 && stageTime>=2f){
                questionBody.ReserveQuestionTurn();SetStage(1,"질문 준비 · 기본 자세로 전환");
            }else if(stage==1 && questionBody.IsAtBaseline){
                if(!questionBody.PlayQuestionGesture())throw new Exception("Hand-raising clip failed.");
                SetStage(2,"청중이 손을 들고 있습니다");
            }else if(stage==2 && questionBody.IsReadyForQuestionSpeech){
                File.AppendAllText("Temp/RehearQuestionFlow.txt",$"\nSpeech cue at gesture {stageTime:F2}s; hand motion still active={!questionBody.IsAtBaseline}.");
                Audio("PlayPreviewClip",clip,0,false);
                typeof(AudienceQuestionSpeaker).GetField("driving",Private).SetValue(speaker,true);
                SetStage(3,"청중 질문 중 · 음성 + 립싱크");
            }else if(stage==3){
                bool playing=(bool)Audio("IsPreviewClipPlaying");
                float position=(float)Audio("GetPreviewClipPosition");
                int frame=Mathf.Clamp(Mathf.FloorToInt(position*clip.frequency/1024),0,visemes.Count-1);
                typeof(AudienceQuestionSpeaker).GetMethod("ApplyVisemes",Private).Invoke(speaker,new object[]{visemes[frame],dt});
                maxJaw=Mathf.Max(maxJaw,visemes[frame][10]*100);
                if(!playing && stageTime>.3f){speaker.StopSpeech();questionBody.ReleaseQuestionTurn();SetStage(4,"질문 종료 · 응답 시작 대기 (마이크는 꺼져 있음)");
                    File.AppendAllText("Temp/RehearQuestionFlow.txt",$"\nPASS sequence completed: hand lowering + voice/OVR visemes -> mouth reset; peak jaw={maxJaw:F2}.");}
                else if(stageTime>clip.length+8)throw new Exception("Question preview audio did not finish.");
            }else if(stage==4 && stageTime>6f)Restart();
            SceneView.RepaintAll();
        }catch(Exception e){File.WriteAllText("Temp/RehearQuestionFlow.txt",e.ToString());Stop();}
    }
    static void SetStage(int value,string text){stage=value;stageTime=0;status=text;if(actionLabel)actionLabel.text=value==0?"발표 종료":value<3?"청중 질문 준비 중…":value==3?"청중 질문 중…":"응답 시작하기";}
    static void Restart(){Audio("StopAllPreviewClips");speaker.StopSpeech();questionBody.ReleaseQuestionTurn();elapsed=0;maxJaw=0;last=EditorApplication.timeSinceStartup;SetStage(0,"발표 종료 → 청중 질문으로 전환");}

    static void Show()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Use Edit mode for this preview.");
        Stop();typeof(RehearFeedbackPreview).GetMethod("Stop",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,null);
        typeof(RehearAudienceMotionPreview).GetMethod("StopPreview",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,null);
        var scene=SceneManager.GetSceneByPath("Assets/01_Scene/Scene_02_Presentation.unity");
        if(!scene.isLoaded)scene=EditorSceneManager.OpenScene("Assets/01_Scene/Scene_02_Presentation.unity",OpenSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        for(int i=0;i<SceneManager.sceneCount;i++){var other=SceneManager.GetSceneAt(i);if(other!=scene)foreach(var g in other.GetRootGameObjects())Suspend(g);}
        var roots=scene.GetRootGameObjects();var seating=roots.SelectMany(g=>g.GetComponentsInChildren<AudienceSeating>(true)).Single();
        root=Object.Instantiate(seating.gameObject);root.name="Question Flow Preview (Editor Only)";SceneManager.MoveGameObjectToScene(root,scene);root.hideFlags=HideFlags.DontSave;Suspend(seating.gameObject);
        root.GetComponent<AudienceSeating>().Initialize(1234);
        bodies=root.GetComponentsInChildren<AudienceAnimationPlayer>();
        graphs=bodies.Select(b=>{
            b.enabled=true;
            foreach(var skin in b.GetComponentsInChildren<SkinnedMeshRenderer>()){skin.updateWhenOffscreen=true;skin.forceMatrixRecalculationPerRender=true;}
            foreach(var animator in b.GetComponentsInChildren<Animator>()){animator.enabled=true;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;}
            typeof(AudienceAnimationPlayer).GetMethod("CreateAnimationGraph",Private).Invoke(b,null);
            var g=(PlayableGraph)typeof(AudienceAnimationPlayer).GetField("playableGraph",Private).GetValue(b);g.SetTimeUpdateMode(DirectorUpdateMode.Manual);g.Evaluate(0);return g;
        }).ToArray();
        var bindings=roots.SelectMany(g=>g.GetComponentsInChildren<PresentationAudienceBindings>(true)).Single();
        if(!bindings.presenterTarget) throw new Exception("Presenter gaze target is missing.");
        gazes=bodies.Select(b=>b.GetComponent<AudienceGazeController>()).ToArray();
        foreach(var gaze in gazes) if(gaze){
            gaze.ConfigureTargets(bindings.presenterTarget,bindings.slideTarget,bindings.aroundTargets);
            gaze.ResetManualGaze();
        }
        questionBody=bodies.Single(b=>b.name.StartsWith("Aud_W_03",StringComparison.OrdinalIgnoreCase));
        speaker=questionBody.GetComponent<AudienceQuestionSpeaker>();if(!speaker)speaker=questionBody.gameObject.AddComponent<AudienceQuestionSpeaker>();
        typeof(AudienceQuestionSpeaker).GetMethod("Initialize",Private).Invoke(speaker,null);
        AssetDatabase.ImportAsset("Assets/Editor/QuestionFlowPreview.wav",ImportAssetOptions.ForceSynchronousImport);
        clip=AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Editor/QuestionFlowPreview.wav");if(!clip)throw new Exception("Question example WAV is missing.");
        BuildVisemes();
        var controller=roots.SelectMany(g=>g.GetComponentsInChildren<PresentationController>(true)).Single();
        var canvas=controller.endPresentationButton.GetComponentInParent<Canvas>();
        uiRoot=Object.Instantiate(canvas.gameObject);uiRoot.name="Question UI Preview (Editor Only)";SceneManager.MoveGameObjectToScene(uiRoot,scene);uiRoot.hideFlags=HideFlags.DontSave;Suspend(canvas.gameObject);
        Transform Map(Transform original)=>uiRoot.transform.Find(AnimationUtility.CalculateTransformPath(original,canvas.transform));
        var button=Map(controller.endPresentationButton.transform);button.gameObject.SetActive(true);actionLabel=button.GetComponentInChildren<TMP_Text>(true);
        if(controller.startPresentationButton)Map(controller.startPresentationButton.transform)?.gameObject.SetActive(false);
        if(controller.scriptPanel && controller.scriptPanel.transform.IsChildOf(canvas.transform))Map(controller.scriptPanel.transform)?.gameObject.SetActive(false);
        if(controller.timerText && controller.timerText.transform.IsChildOf(canvas.transform))Map(controller.timerText.transform)?.GetComponent<TMP_Text>().SetText("Q&A");
        foreach(var t in uiRoot.GetComponentsInChildren<TMP_Text>(true))if(t.name=="Question Text"||t.name=="QuestionText")t.gameObject.SetActive(false);
        var camera=roots.SelectMany(g=>g.GetComponentsInChildren<Camera>(true)).First(c=>c.CompareTag("MainCamera"));
        var view=SceneView.lastActiveSceneView;if(view){view.orthographic=false;view.LookAtDirect(camera.transform.position+camera.transform.forward*3,camera.transform.rotation,1.5f);}
        previousMute=EditorUtility.audioMasterMute;EditorUtility.audioMasterMute=false;
        File.WriteAllText("Temp/RehearQuestionFlow.txt",$"RUNNING example Q&A: {questionBody.name}; voice={questionBody.GetComponent<AudienceVoiceProfile>().VoiceName}; audio={clip.length:F2}s; viseme frames={visemes.Count}; production hand gesture and mouth mapping; no microphone/network/session write.");
        Restart();
    }
    static void BuildVisemes()
    {
        clip.LoadAudioData();var data=new float[clip.samples*clip.channels];if(!clip.GetData(data,0))throw new Exception("Could not read question audio.");
        uint context=0;var result=OVRLipSync.CreateContext(ref context,OVRLipSync.ContextProviders.Enhanced,clip.frequency,false);
        if(result!=OVRLipSync.Result.Success)throw new Exception("OVR context: "+result);
        visemes.Clear();try{
            for(int offset=0;offset<clip.samples;offset+=1024){
                var block=new float[1024];for(int f=0;f<1024 && offset+f<clip.samples;f++)for(int c=0;c<clip.channels;c++)block[f]+=data[(offset+f)*clip.channels+c]/clip.channels;
                var frame=new OVRLipSync.Frame();result=OVRLipSync.ProcessFrame(context,block,frame,false);
                if(result!=OVRLipSync.Result.Success)throw new Exception("OVR frame: "+result);
                visemes.Add((float[])frame.Visemes.Clone());
            }
        }finally{OVRLipSync.DestroyContext(context);}
        if(visemes.Max(v=>v[10])<.02f)throw new Exception("No mouth opening detected from question speech.");
    }
    static void Suspend(GameObject go){if(go.activeSelf){suspended.Add(go);go.SetActive(false);}}
    static void Stop()
    {
        if(root){Audio("StopAllPreviewClips");if(speaker)speaker.StopSpeech();EditorUtility.audioMasterMute=previousMute;Object.DestroyImmediate(root);}
        if(uiRoot)Object.DestroyImmediate(uiRoot);root=null;uiRoot=null;speaker=null;actionLabel=null;bodies=null;graphs=null;gazes=null;
        foreach(var go in suspended)if(go)go.SetActive(true);suspended.Clear();visemes.Clear();SceneView.RepaintAll();
    }
}
