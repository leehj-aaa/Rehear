using UnityEngine;
#if UNITY_EDITOR || REHEAR_SPEECH_SMOKE
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Rehear.Evc.Audio;
using Rehear.Evc.Presentation;
using UnityEngine.Networking;
#endif

// Included only in the explicit diagnostic scene; no startup hook in the app.
public sealed class RehearDeviceSpeechCheck : MonoBehaviour
{
    public GameObject[] audiencePrefabs;
#if REHEAR_SPEECH_SMOKE
    readonly StringBuilder report = new StringBuilder();
    void Write(string value)
    {
        report.AppendLine(value);
        File.WriteAllText(Path.Combine(Application.persistentDataPath,"speech-report.txt"),report.ToString());
        Debug.Log("[SpeechCheck] "+value);
    }
    async Task Wait(Func<bool> condition,float timeout)
    {
        float end=Time.realtimeSinceStartup+timeout;
        while(!condition()) { if(Time.realtimeSinceStartup>end)throw new Exception("Timeout"); await Task.Yield(); }
    }
    async Task<byte[]> Get(string endpoint)
    {
        using(var request=UnityWebRequest.Get("http://127.0.0.1:8092/test/"+endpoint))
        {
            request.timeout=30; var op=request.SendWebRequest(); await Wait(()=>op.isDone,35);
            if(request.result!=UnityWebRequest.Result.Success)throw new Exception("Fixture connection: "+request.error);
            return request.downloadHandler.data;
        }
    }
    async void Start()
    {
        try
        {
            Application.runInBackground=true;
            Write("START Android ARM64 real Azure/OVR test; synthetic answer; mock LLM.");
            AzureSpeechConfig.EditorLoopbackBaseUrl="http://127.0.0.1:8092/xreal_rehear/evc";
            PresentationSessionContext.Current.ApplySmartStart(JsonUtility.FromJson<Rehear.Evc.Contracts.SmartStartResponse>(Encoding.UTF8.GetString(await Get("session"))));
            var client=new AzureQuestionSpeechClient(Resources.Load<AzureSpeechConfig>("AzureSpeechConfig"));
            for(int n=0;n<audiencePrefabs.Length;n++)
            {
                var actor=Instantiate(audiencePrefabs[n],new Vector3(0,0,2),Quaternion.Euler(0,180,0));
                string id="audience_0"+(n+1);
                actor.GetComponent<Rehear.Evc.Audience.AudienceAgent>().Configure(id,null);
                var speaker=actor.GetComponent<AudienceQuestionSpeaker>();
                var profile=actor.GetComponent<AudienceVoiceProfile>();
                var clip=await client.SynthesizeAsync(0,profile.VoiceName,id,CancellationToken.None);
                if(!speaker.Play(clip))throw new Exception("Speaker failed: "+actor.name);
                var mouth=actor.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(m=>m.sharedMesh&&Enumerable.Range(0,m.sharedMesh.blendShapeCount).Any(i=>m.sharedMesh.GetBlendShapeName(i).ToLowerInvariant().EndsWith("jawopen")));
                int jaw=Enumerable.Range(0,mouth.sharedMesh.blendShapeCount).First(i=>mouth.sharedMesh.GetBlendShapeName(i).ToLowerInvariant().EndsWith("jawopen"));
                float peak=0;
                await Wait(()=>{peak=Mathf.Max(peak,mouth.GetBlendShapeWeight(jaw));return !speaker.IsSpeaking;},clip.length+15);
                if(peak<2)throw new Exception("No actual audio visemes: "+actor.name);
                Write($"PASS {profile.VoiceName}: WAV {clip.length:F2}s, live jaw peak {peak:F2}");
                Destroy(actor); Destroy(clip);
            }
            var recorder=gameObject.AddComponent<QuestionAnswerRecorder>();
            await recorder.BeginAsync(CancellationToken.None);
            float until=Time.realtimeSinceStartup+1.2f;
            await Wait(()=>Time.realtimeSinceStartup>=until,5);
            var captured=recorder.Finish();
            if(captured==null||captured.Length<=44)throw new Exception("Quest microphone captured no samples");
            Write("PASS Quest microphone start/PCM capture/stop; recorded data discarded, not uploaded.");
            captured=null;
            byte[] synthetic=await Get("answer");
            var first=await client.SubmitAnswerAsync(0,synthetic,Guid.NewGuid().ToString("D"),"audience_02",CancellationToken.None);
            if(first==null||first.total!=2||first.next_question==null)throw new Exception("Adaptive answer failed");
            Write("PASS Quest -> Azure STT -> mock LLM -> next question, total=2");
            var next=await client.SynthesizeAsync(1,"ko-KR-BongJinNeural","audience_02",CancellationToken.None);
            if(!next||next.samples==0)throw new Exception("Next question TTS missing");
            Destroy(next);
            var second=await client.SubmitAnswerAsync(1,synthetic,Guid.NewGuid().ToString("D"),null,CancellationToken.None);
            if(second==null||!second.saved||second.next_question!=null)throw new Exception("Final answer failed");
            Write("COMPLETE Quest speech check: six voices/OVR, microphone, synthetic STT, adaptive exchange. Production LLM/deployment not tested.");
        }
        catch(Exception e){Write("FAIL "+e);}
        finally{AzureSpeechConfig.EditorLoopbackBaseUrl=null;}
    }
#endif
}
