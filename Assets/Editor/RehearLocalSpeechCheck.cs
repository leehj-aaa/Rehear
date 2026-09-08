using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Rehear.Evc.Audio;
using Rehear.Evc.Contracts;
using Rehear.Evc.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Object = UnityEngine.Object;

// Runs the real Unity speech client, AudioSource/OVR and Q&A state machine against
// Server/Integration/local_speech_check.py, in a disposable play scene.
[InitializeOnLoad]
internal static class RehearLocalSpeechCheck
{
    const string Root = "Temp/LocalSpeechCheck/";
    const string Active = "RehearLocalSpeechCheck.active";
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static RehearLocalSpeechCheck()
    {
        EditorApplication.update += Poll;
        EditorApplication.playModeStateChanged += Changed;
    }
    static void Poll()
    {
        const string request = "Temp/RehearLocalSpeech.request";
        if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(request);
        if (!File.Exists(Root+"session.json")) { File.WriteAllText(Root+"unity_report.txt","FAIL start the loopback server first"); return; }
        var scene = EditorSceneManager.GetActiveScene();
        if (EditorSceneManager.sceneCount != 1 || string.IsNullOrEmpty(scene.path))
        { File.WriteAllText(Root+"unity_report.txt","FAIL requires one saved scene"); return; }
        if (scene.isDirty && !EditorSceneManager.SaveScene(scene)) return;
        SessionState.SetString(Active+".scene",scene.path);
        SessionState.SetInt(Active+".http",(int)PlayerSettings.insecureHttpOption);
        SessionState.SetBool(Active,true);
        PlayerSettings.insecureHttpOption = InsecureHttpOption.DevelopmentOnly;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }
    static void Changed(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Active,false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode) Run();
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool(Active,false);
            AzureSpeechConfig.EditorLoopbackBaseUrl = null;
            PlayerSettings.insecureHttpOption = (InsecureHttpOption)SessionState.GetInt(Active+".http",0);
            EditorSceneManager.OpenScene(SessionState.GetString(Active+".scene",""));
        }
    }
    static object Field(object target,string name) => target.GetType().GetField(name,Flags).GetValue(target);
    static void Field(object target,string name,object value) => target.GetType().GetField(name,Flags).SetValue(target,value);
    static string State(QuestionAnswerManager qa) => Field(qa,"state").ToString();
    static async Task Wait(Func<bool> condition,float timeout,string description)
    {
        double end = EditorApplication.timeSinceStartup+timeout;
        while (!condition())
        {
            if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup>end) throw new Exception("Timeout: "+description);
            await Task.Yield();
        }
    }
    static async Task Delay(float seconds)
    {
        double end = EditorApplication.timeSinceStartup+seconds;
        await Wait(()=>EditorApplication.timeSinceStartup>=end,seconds+5,"delay");
    }
    static async void Run()
    {
        var report = new StringBuilder();
        var originalMute = EditorUtility.audioMasterMute;
        Application.LogCallback capture = (message,stack,type) => { if (message.Contains("OVRLip") || message.Contains("OvrLip") || message.Contains("[Q&A]") || type==LogType.Exception) report.AppendLine(type+": "+message); };
        Application.logMessageReceived += capture;
        try
        {
            EditorUtility.audioMasterMute = false;
            Application.runInBackground = true;
            AudioListener.pause = false; Time.timeScale = 1;
            var camera = new GameObject("Smoke camera",typeof(Camera),typeof(AudioListener)).GetComponent<Camera>();
            camera.transform.position = new Vector3(0,1.4f,0);
            AzureSpeechConfig.EditorLoopbackBaseUrl = "http://127.0.0.1:8092/xreal_rehear/evc";
            PresentationSessionContext.Current.ApplySmartStart(JsonUtility.FromJson<Rehear.Evc.Contracts.SmartStartResponse>(File.ReadAllText(Root+"session.json")));
            var config = Resources.Load<AzureSpeechConfig>("AzureSpeechConfig");
            var client = new AzureQuestionSpeechClient(config);
            var paths = Directory.GetFiles("Assets/03_Prefabs/Audience/Presentation","Aud_*.prefab").OrderBy(p=>p).ToArray();
            var actors = paths.Select((p,i)=>
            {
                var actor = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(p.Replace('\\','/')));
                actor.transform.SetPositionAndRotation(new Vector3((i%3-1)*1.2f,0,3+i/3),Quaternion.Euler(0,180,0));
                actor.GetComponent<Rehear.Evc.Audience.AudienceAgent>().Configure("audience_0"+(i+1),null);
                return actor;
            }).ToArray();
            await Delay(.3f);
            foreach (var actor in actors)
            {
                var profile = actor.GetComponent<AudienceVoiceProfile>();
                var speaker = actor.GetComponent<AudienceQuestionSpeaker>();
                var clip = await client.SynthesizeAsync(0,profile.VoiceName,actor.GetComponent<Rehear.Evc.Audience.AudienceAgent>().AgentId,CancellationToken.None);
                if (!speaker.Play(clip)) throw new Exception("Speech start failed: "+actor.name+", active="+speaker.isActiveAndEnabled+", clip="+(clip ? clip.length : -1)+", context="+((OVRLipSyncContext)Field(speaker,"lipsync"))?.Context+", shapes="+((System.Collections.ICollection)Field(speaker,"shapes")).Count);
                var mouth = actor.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(m=>m.sharedMesh && Enumerable.Range(0,m.sharedMesh.blendShapeCount).Any(i=>m.sharedMesh.GetBlendShapeName(i).ToLowerInvariant().EndsWith("jawopen")));
                int jaw = Enumerable.Range(0,mouth.sharedMesh.blendShapeCount).First(i=>mouth.sharedMesh.GetBlendShapeName(i).ToLowerInvariant().EndsWith("jawopen"));
                float peak = 0;
                await Wait(()=>
                {
                    peak = Mathf.Max(peak,mouth.GetBlendShapeWeight(jaw));
                    return !speaker.IsSpeaking;
                },clip.length+12,"live audio visemes "+actor.name);
                if (peak<2) throw new Exception("Azure audio did not move mouth: "+actor.name+" peak="+peak);
                await Delay(.1f);
                if (mouth.GetBlendShapeWeight(jaw)>.01f) throw new Exception("Mouth did not restore after speech");
                report.AppendLine($"PASS {actor.name}: {profile.VoiceName}, Azure WAV {clip.length:F2}s -> live OVR jaw peak {peak:F2}; reset after end.");
                File.WriteAllText(Root+"unity_report.txt",report.ToString());
                // Verify pausing does not mark speech complete, and stopping restores the face.
                if (actor==actors[0])
                {
                    if (!speaker.Play(clip)) throw new Exception("Replay failed");
                    await Delay(.4f); speaker.SetPaused(true);
                    int sample = speaker.Voice.timeSamples;
                    await Delay(.3f);
                    if (!speaker.IsSpeaking || Math.Abs(speaker.Voice.timeSamples-sample)>1024) throw new Exception("Pause did not hold playback");
                    speaker.SetPaused(false); await Delay(.2f); speaker.StopSpeech(); await Delay(.1f);
                    if (speaker.IsSpeaking || mouth.GetBlendShapeWeight(jaw)>.01f) throw new Exception("Stop did not restore face");
                    report.AppendLine("PASS voice pause/resume/stop.");
                }
                Object.Destroy(clip);
            }
            var qa = new GameObject("Q&A smoke",typeof(QuestionAnswerManager)).GetComponent<QuestionAnswerManager>();
            var canvas = new GameObject("Smoke canvas",typeof(Canvas));
            var button = new GameObject("Shared presentation action",typeof(RectTransform),typeof(Image),typeof(Button)).GetComponent<Button>();
            button.transform.SetParent(canvas.transform,false);
            var label = new GameObject("Label",typeof(RectTransform),typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            label.transform.SetParent(button.transform,false); label.font = RehearPretendardFontBake.Font("SemiBold");
            button.onClick.AddListener(qa.OnActionButtonClick);
            qa.questionContents = new[]{"발표 내용을 어떻게 검증하셨나요?","교체 전 두 번째 질문입니다."};
            qa.Prepare(button); qa.StartQAPhase(button);
            for (int index=0;index<2;index++)
            {
                await Wait(()=>State(qa)=="ReadyToAnswer" || State(qa)=="QuestionAudioFailed",120,"QS then TTS then answer button");
                if (State(qa)!="ReadyToAnswer" || label.text!="응답 시작하기" || !button.interactable) throw new Exception("Question flow failed: "+label.text+", play="+EditorApplication.isPlaying+", body="+Field(qa,"questionBody")+", actors="+string.Join(";",actors.Select(a=>a ? a.name+":"+a.activeInHierarchy+":"+a.GetComponent<AudienceAnimationPlayer>().enabled : "destroyed")));
                var body = (AudienceAnimationPlayer)Field(qa,"questionBody");
                if (!body.IsAtBaseline) throw new Exception("Question finished outside baseline");
                button.onClick.Invoke();
                await Wait(()=>State(qa)!="StartingAnswer",12,"microphone start");
                if (State(qa)!="Answering" || button.interactable) throw new Exception("Answer did not start with double-click lock: "+label.text);
                var recorder = (QuestionAnswerRecorder)Field(qa,"answerRecorder");
                if (!recorder || !recorder.IsRecording) throw new Exception("Microphone is not recording");
                button.onClick.Invoke();
                if (State(qa)!="Answering") throw new Exception("Double click submitted answer prematurely");
                await Delay(1.2f);
                if (!button.interactable || label.text!="응답 마치기") throw new Exception("Finish-answer did not unlock");
                recorder.Cancel(); // Discard microphone capture; only synthetic audio is uploaded.
                Field(qa,"pendingAnswer",File.ReadAllBytes(Root+"synthetic_answer.wav"));
                button.onClick.Invoke();
                if (index==1) qa.SetPaused(true); // Stop at ready-for-feedback; never enter production report/database paths.
                await Wait(()=>State(qa)!="SavingAnswer",120,"Azure STT and adaptive result");
                if (State(qa)=="AnswerSaveFailed") throw new Exception("Answer upload/STT failed");
                if (index==0 && qa.questionContents[1]=="교체 전 두 번째 질문입니다.") throw new Exception("Next question was not updated");
                if (index==1 && State(qa)!="ReadyToFinish") throw new Exception("Last answer failed to finish");
                report.AppendLine($"PASS Q{index+1}/2: QS -> baseline -> real TTS/lipsync -> start mic -> click lock -> discard mic -> synthetic Azure STT -> {(index==0 ? "mock LLM updated Q2" : "ready for feedback")}; total remains 2.");
                File.WriteAllText(Root+"unity_report.txt",report.ToString());
            }
            report.AppendLine("COMPLETE local speech smoke. Actual microphone start/stop checked, captured audio discarded. LLM mocked; production deployment and Quest not tested.");
        }
        catch (Exception e) { report.AppendLine("FAIL "+e); }
        finally
        {
            Application.logMessageReceived -= capture;
            File.WriteAllText(Root+"unity_report.txt",report.ToString());
            EditorUtility.audioMasterMute = originalMute;
            AzureSpeechConfig.EditorLoopbackBaseUrl = null;
            EditorApplication.ExitPlaymode();
        }
    }
}
