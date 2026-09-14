using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Rehear.Evc.Audience;
using Rehear.Evc.Contracts;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

/// <summary>Editor-only replay of real scheduler output with synthetic evidence.</summary>
[InitializeOnLoad]
public sealed partial class RehearAudienceMotionPreview : EditorWindow
{
    [Serializable] public sealed class Trace { public float duration; public int seed; public AudienceDto[] audiences; public Frame[] frames; }
    [Serializable] public sealed class Frame { public float time; public AudienceReactionResponse response; }
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    const string Request = "Temp/RehearAudienceMotion.request";
    const string TracePath = "Temp/AudienceReactionPreview.json";
    static readonly MethodInfo CreateGraph = typeof(AudienceAnimationPlayer).GetMethod("CreateAnimationGraph", Flags);
    static readonly MethodInfo Advance = typeof(AudienceAnimationPlayer).GetMethod("Advance", Flags);
    static readonly FieldInfo GraphField = typeof(AudienceAnimationPlayer).GetField("playableGraph", Flags);
    static GameObject root;
    static AudienceAgent[] actors;
    static AudienceAnimationPlayer[] bodies;
    static Trace trace;
    static readonly Dictionary<string,string> latest = new Dictionary<string,string>();
    static readonly List<GameObject> hidden = new List<GameObject>();
    static double lastTime;
    static float elapsed;
    static int cursor;
    static bool paused;
    static bool typingDemo;
    static bool conversationDemo, conversationStarted;
    static float LoopDuration => conversationDemo?14f:typingDemo?20f:trace.duration;
    static readonly Dictionary<AudienceAnimationPlayer,float> typingAt = new Dictionary<AudienceAnimationPlayer,float>();
    static double diagnosticAt;
    static AudienceGazeController[] gazes;
    static float[] nextGaze;
    static RehearAudienceMotionPreview window;

    static RehearAudienceMotionPreview()
    {
        EditorApplication.update += Tick;
        SceneView.duringSceneGui += DrawReviewOverlay;
        AssemblyReloadEvents.beforeAssemblyReload += StopPreview;
        EditorApplication.playModeStateChanged += s => { if(s == PlayModeStateChange.ExitingEditMode) StopPreview(); };
        EditorSceneManager.sceneClosing += (scene, removing) => StopPreview();
    }

    [MenuItem("Rehear/Audience/Preview Independent Reactions")]
    static void Open()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play mode before previewing.");
        if(!File.Exists(TracePath)) throw new FileNotFoundException("Generate the trace with Server/Integration/generate_audience_preview.py first.");
        StopPreview();
        var scene = EditorSceneManager.GetActiveScene();
        if(scene.path != "Assets/01_Scene/Scene_02_Presentation.unity")
        {
            if(scene.isDirty) throw new InvalidOperationException("Save your current scene before opening the preview.");
            scene = EditorSceneManager.OpenScene("Assets/01_Scene/Scene_02_Presentation.unity");
        }
        trace = JsonUtility.FromJson<Trace>(File.ReadAllText(TracePath));
        typeof(RehearSessionReadySetup).GetMethod("ShowAudiencePreview", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null,null);
        root = scene.GetRootGameObjects().Single(g=>g.name=="Audience Preview (Editor Only)");
        root.GetComponent<AudienceSeating>().ApplyServerProfiles(trace.audiences);
        actors = root.GetComponentsInChildren<AudienceAgent>();
        bodies = root.GetComponentsInChildren<AudienceAnimationPlayer>();
        foreach(var actor in actors) { actor.Configure(actor.AgentId,actor.ActionRegistry); actor.SetServerMode(true); }
        foreach(var body in bodies)
        {
            body.enabled=true;
            // Manual edit-mode graphs change bone transforms without the usual
            // runtime skinning update. Keep the rendered mesh in sync with them.
            foreach(var renderer in body.GetComponentsInChildren<SkinnedMeshRenderer>())
            { renderer.updateWhenOffscreen=true; renderer.forceMatrixRecalculationPerRender=true; }
            foreach(var animator in body.GetComponentsInChildren<Animator>())
            { animator.enabled=true; animator.cullingMode=AnimatorCullingMode.AlwaysAnimate; }
            typeof(AudienceAnimationPlayer).GetField("printAnimationLog",Flags).SetValue(body,false);
        }
        foreach(var canvas in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Canvas>(true)))
            if(!SceneVisibilityManager.instance.IsHidden(canvas.gameObject))
            { hidden.Add(canvas.gameObject); SceneVisibilityManager.instance.Hide(canvas.gameObject,true); }
        gazes=bodies.Select(b=>b.GetComponent<AudienceGazeController>()).ToArray();
        var bindings=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<PresentationAudienceBindings>(true)).FirstOrDefault();
        foreach(var gaze in gazes) if(gaze && bindings)
            gaze.ConfigureTargets(bindings.presenterTarget,bindings.slideTarget,bindings.aroundTargets);
        Restart();
        if(!focusedReview) {
            window = GetWindow<RehearAudienceMotionPreview>(true,"청중 움직임 미리보기",false);
            window.minSize = new Vector2(360,270);
            window.Show();
        }
        // Reopening after a script reload must preserve the user's Scene view
        // framing, focus and panel layout.
        File.WriteAllText("Temp/RehearAudienceMotion.txt",$"RUNNING: {actors.Length} actors; {trace.frames.Length} evaluations; {trace.duration}s loop. Synthetic evidence, production scheduler and body mixer. No recording/network.\n");
    }

    static void Restart()
    {
        if(!root) return;
        reviewMode=false;typingDemo=false; conversationDemo=false;conversationStarted=false; typingAt.Clear();
        elapsed=0; cursor=0; paused=false; latest.Clear(); lastTime=EditorApplication.timeSinceStartup;
        nextGaze=new float[bodies.Length];
        for(int i=0;i<gazes.Length;i++) if(gazes[i]) nextGaze[i]=gazes[i].ResetManualGaze();
        foreach(var body in bodies)
        {
            if(!(bool)CreateGraph.Invoke(body,null)) throw new Exception("Animation graph unavailable: "+body.name);
            var graph=(PlayableGraph)GraphField.GetValue(body);
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            graph.Evaluate(0);
        }
    }

    static void StartTypingPreview()
    {
        if(!root) Open();
        Restart(); typingDemo=true; cursor=trace.frames.Length;
        int index=0;
        foreach(var body in bodies) {
            var seat=body.GetComponent<AudienceSeatAssignment>();
            if(seat && seat.Seat && seat.Seat.HasLaptop) typingAt[body]=.5f+index++*3f;
        }
    }

    static void StartConversationPreview()
    {
        if(!root)Open();
        Restart();conversationDemo=true;cursor=trace.frames.Length;
    }

    [MenuItem("Rehear/Audience/Stop Reaction Preview")]
    static void StopPreview()
    {
        if(root) {
            if(Selection.activeTransform && Selection.activeTransform.IsChildOf(root.transform)) Selection.activeObject=null;
            Object.DestroyImmediate(root);
        }
        root=null; actors=null; bodies=null;reviewMode=false;
        foreach(var item in focusedSuspended)if(item)item.SetActive(true);
        focusedSuspended.Clear();
        foreach(var item in hidden) if(item) SceneVisibilityManager.instance.Show(item,true);
        hidden.Clear(); latest.Clear();
        SceneView.RepaintAll();
    }

    static void Tick()
    {
        if(EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if(File.Exists(Request))
        {
            try {
                var command=File.ReadAllText(Request).Trim(); File.Delete(Request);
                if(command=="stop") StopPreview();
                else if(command=="typing") StartTypingPreview();
                else if(command=="conversation") StartConversationPreview();
                else if(command=="review") StartReview();
                else if(command=="review-props") StartFocusedPropsReview();
                else if(command=="photo-capture") CaptureFocusedPhoto();
                else if(command=="review-check") CheckReview();
                else if(command=="review-seatadjust") {
                    StartReview();
                    SelectReview(System.Array.FindIndex(reviewEntries,e=>e.variationId=="ACT_05.seatadjust"));
                    reviewAuto=false;
                    for(int frame=0;frame<150;frame++){elapsed+=.02f;EvaluatePreview(.02f);}
                    paused=true;
                }
                else if(command=="restart") Restart();
                else if(command=="resume") { paused=false;lastTime=EditorApplication.timeSinceStartup; }
                else if(command.StartsWith("typing-seek:")) {
                    StartTypingPreview();
                    float destination=Mathf.Clamp(float.Parse(command.Substring(12),System.Globalization.CultureInfo.InvariantCulture),0,19f);
                    while(elapsed<destination) {float step=Mathf.Min(.02f,destination-elapsed);elapsed+=step;EvaluatePreview(step);}
                    paused=true;SceneView.RepaintAll();if(window)window.Repaint();
                }
                else if(command.StartsWith("seek:")) {
                    if(!root) Open();
                    Restart(); float destination=Mathf.Clamp(float.Parse(command.Substring(5),System.Globalization.CultureInfo.InvariantCulture),0,trace.duration);
                    while(elapsed<destination) {float step=Mathf.Min(.02f,destination-elapsed);elapsed+=step;EvaluatePreview(step);}
                    paused=true;SceneView.RepaintAll();if(window)window.Repaint();
                } else Open();
            }
            catch(IOException) { return; } // Request writer may still hold the file.
            catch(Exception e) { StopPreview(); File.WriteAllText("Temp/RehearAudienceMotion.txt",e.ToString()); Debug.LogException(e); }
        }
        if(!root || EditorApplication.isPlayingOrWillChangePlaymode) return;
        double now=EditorApplication.timeSinceStartup;
        float dt=Mathf.Min(.25f,(float)(now-lastTime)); lastTime=now;
        if(paused) return;
        try
        {
            elapsed+=dt;
            EvaluatePreview(dt);
            if(!reviewMode && elapsed>=LoopDuration) {if(conversationDemo)StartConversationPreview();else if(typingDemo)StartTypingPreview();else Restart();}
            if(now-diagnosticAt>=1)
            {
                File.WriteAllText("Temp/RehearAudienceMotion-live.txt",$"elapsed={elapsed:F2} dt={dt:F3} paused={paused} skinMatrices=refreshed gaze=active");
                diagnosticAt=now;
            }
            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll(); if(window) window.Repaint();
        }
        catch(Exception e) { StopPreview(); File.WriteAllText("Temp/RehearAudienceMotion.txt",e.ToString()); Debug.LogException(e); }
    }

    static void EvaluatePreview(float dt)
    {
            if(reviewMode)AdvanceReview();
            if(conversationDemo && !conversationStarted && elapsed>=1f) {
                conversationStarted=true;
                var rear=bodies.Where(b=>b.GetComponent<AudienceSeatAssignment>().Seat.row=="rear").ToArray();
                if(rear.Length!=2 || !rear[0].PlayServerVariation("ACT_08.side_conversation",8f,1f))throw new Exception("Rear conversation pair unavailable.");
                foreach(var body in rear) {
                    body.GetComponent<AudienceSeatAssignment>().TryGetConversationVariation(out var variation);
                    latest[body.GetComponent<AudienceAgent>().AgentId]=variation.EndsWith("_r")?"오른쪽 상대와 대화":"왼쪽 상대와 대화";
                }
            }
            if(typingDemo) foreach(var body in typingAt.Keys.ToArray())
                if(elapsed>=typingAt[body]) {
                    bool accepted=body.PlayServerVariation("ACT_01.laptop_typing",7f,1f);
                    latest[body.GetComponent<AudienceAgent>().AgentId]=accepted?"노트북으로 돌아서 타이핑":"타이핑 거절됨";
                    typingAt[body]=float.PositiveInfinity;
                }
            while(cursor<trace.frames.Length && trace.frames[cursor].time<=elapsed)
            {
                var frame=trace.frames[cursor++];
                foreach(var update in frame.response.audiences ?? Array.Empty<AudienceUpdateDto>()) {
                    var gaze=actors.Single(a=>a.AgentId==update.agent_id).GetComponent<AudienceGazeController>();
                    if(gaze && update.state!=null) gaze.ApplyEvaluationState(update.state.E,update.state.C);
                }
                foreach(var command in frame.response.commands)
                {
                    var actor=actors.Single(a=>a.AgentId==command.agent_id);
                    if(command.layer=="GazeHead") {
                        var gaze=actor.GetComponent<AudienceGazeController>();
                        if(gaze)gaze.ApplyServerGaze(command.action_id,command.duration,elapsed);
                    }
                    if(command.layer!="Body") continue;
                    var body=actor.GetComponent<AudienceAnimationPlayer>();
                    bool accepted=body.PlayServerVariation(command.selected_variation_id,command.duration,command.intensity);
                    latest[actor.AgentId]=$"{frame.time:0.0}s · {command.selected_variation_id}"+(accepted?"":" (거절됨)");
                    File.AppendAllText("Temp/RehearAudienceMotion-events.txt",$"{frame.time:F2} {actor.name} {command.selected_variation_id} accepted={accepted}\n");
                }
            }
            foreach(var body in bodies)
            {
                Advance.Invoke(body,new object[]{dt});
                ((PlayableGraph)GraphField.GetValue(body)).Evaluate(dt);
                body.ApplyPropPoses();
            }
            for(int i=0;i<gazes.Length;i++) if(gazes[i]) {
                if(elapsed>=nextGaze[i]) nextGaze[i]=elapsed+gazes[i].AdvanceManualGaze();
                gazes[i].ApplyGaze(dt,elapsed);
            }
    }

    void OnGUI()
    {
        if(reviewMode) {DrawReview();return;}
        EditorGUILayout.LabelField(conversationDemo?"맨 뒤 두 사람 · 옆 대화 → 듣는 자세 복귀":typingDemo?"타이핑 · 방향 전환 → 입력 → 복귀":"청중별 반응 · 2분 반복",EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("예시 평가 데이터로 실제 서버 선택 로직과 청중 동작을 재생합니다. 실제 발표·음성 연결 테스트는 아닙니다.",MessageType.Info);
        EditorGUILayout.LabelField($"{elapsed:0.0} / {(conversationDemo?14:typingDemo?20:120)}초");
        GUILayout.BeginHorizontal();
        if(GUILayout.Button(paused?"계속 보기":"일시정지")) paused=!paused;
        if(GUILayout.Button("처음부터")) Restart();
        if(GUILayout.Button("미리보기 종료")) StopPreview();
        GUILayout.EndHorizontal();
        if(GUILayout.Button(typingDemo?"전체 반응 보기":"타이핑 보기")) {if(typingDemo)Restart();else StartTypingPreview();}
        if(GUILayout.Button(conversationDemo?"전체 반응 보기":"맨 뒤 두 사람 대화 보기")) {if(conversationDemo)Restart();else StartConversationPreview();}
        if(GUILayout.Button("모든 애니메이션 순서대로 점검"))StartReview();
        if(actors!=null) foreach(var actor in actors.OrderBy(a=>a.AgentId))
            EditorGUILayout.LabelField(actor.name,latest.TryGetValue(actor.AgentId,out var label)?label:"기본 자세");
    }
    void OnDisable() { StopPreview(); }
}
