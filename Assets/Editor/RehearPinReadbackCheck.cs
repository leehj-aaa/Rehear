using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Rehear.Evc.Presentation;

// Explicit, one-shot editor verification. Never stores a PIN or session payload in reports.
[InitializeOnLoad]
internal static class RehearPinReadbackCheck
{
    const string Request="Temp/RehearPinReadback.request";
    const string Report="Temp/RehearPinReadback.txt";
    const string PhaseKey="RehearPinReadbackPhase";
    static bool LocalTest => SessionState.GetBool("RehearPinLocalTransitionTest",false);
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static RehearPinReadbackCheck() { EditorApplication.update+=Tick; }
    static bool Flag(PinInputManager m,string name)=>(bool)typeof(PinInputManager).GetField(name,Private).GetValue(m);
    static void Log(string message)=>File.AppendAllText(Report,DateTime.Now.ToString("HH:mm:ss")+" "+message+"\n");
    static void Phase(string phase) { SessionState.SetString(PhaseKey,phase); SessionState.SetFloat("RehearReadbackDeadline",(float)EditorApplication.timeSinceStartup+45); }
    static void Capture(string name)=>ScreenCapture.CaptureScreenshot("Temp/RehearOpening-"+name+".png");
    static void Tick()
    {
        if(EditorApplication.isCompiling || EditorApplication.isUpdating)return;
        try { Run(); }
        catch(Exception e) { Log("BLOCKED "+e.GetType().Name+": "+e.Message); Phase("done"); }
    }
    static void Run()
    {
        string phase=SessionState.GetString(PhaseKey,"");
        if(!EditorApplication.isPlaying)
        {
            if(!File.Exists(Request) || EditorApplication.isPlayingOrWillChangePlaymode)return;
            string command=File.ReadAllText(Request).Trim();
            if(command!="prepare" && command!="prepare-local")return;
            File.Delete(Request);
            var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if(scene.name!="Scene_00" || UnityEngine.SceneManagement.SceneManager.sceneCount!=1)throw new Exception("Only Scene_00 may be open.");
            SessionState.SetBool("RehearPinRuntimeTest",false);SessionState.SetBool("RehearPinWaitFirebase",false);
            SessionState.SetBool("RehearPinLocalTransitionTest",command=="prepare-local");
            Phase("opening"); Log("PREPARE: initialization only; no PIN request.");
            EditorApplication.EnterPlaymode();return;
        }
        if(phase=="" || phase=="done")return;
        if(phase=="transition")
        {
            if(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name=="Scene_00_5_Tutorial")
            {
                if(RuntimeSessionData.Session==null || !PresentationSessionContext.Current.HasPresentation)
                    throw new Exception("Session data was lost during the tutorial transition.");
                Phase("done");Log("SUCCESS: tutorial loaded automatically with cached session; requests="+(LocalTest?0:1)+".");
                RehearLoadingDesignSetup.Lighting();
                if(LocalTest)
                {
                    if(PresentationSessionContext.Current.HasEvcSession)throw new Exception("Local transition test started a server session.");
                    Log("PASS LOCAL FLOW: loading -> tutorial; no completion UI; no PIN query or presentation session.");
                    if (!System.IO.File.Exists("Temp/RehearLightingArrivalTrace.keep-open"))
                        EditorApplication.delayCall+=EditorApplication.ExitPlaymode;
                }
                return;
            }
            if(EditorApplication.timeSinceStartup>SessionState.GetFloat("RehearReadbackDeadline",0))
            { Phase("done");Log("FAILED: tutorial transition timed out; no PIN retry."); }
            return;
        }
        var manager=UnityEngine.Object.FindObjectsByType<PinInputManager>(FindObjectsInactive.Include,FindObjectsSortMode.None).FirstOrDefault(m=>m.gameObject.scene.name=="Scene_00");
        if(!manager)throw new Exception("Opening PIN manager not found.");
        var root=manager.transform;
        var loading=root.Find("LoadingPanel").gameObject;
        if(phase=="opening")
        {
            if (root.Find("PinPanel").gameObject.activeInHierarchy)
            { Phase("firebase"); return; }
            var start=UnityEngine.Object.FindObjectsByType<Button>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single(b=>b.name=="Btn_Scene00_to_Scene01");
            if(!start.isActiveAndEnabled || !start.IsInteractable() || start.GetComponent<CanvasGroup>().alpha<1)return;
            start.onClick.Invoke(); Phase("firebase");return;
        }
        if(phase=="firebase")
        {
            if(Flag(manager,"firebaseReady"))
            {
                if(Flag(manager,"enableDemoFallback"))throw new Exception("Demo fallback must be disabled.");
                Phase("ready"); Log("READY: Firebase initialized; PIN and loading panel connected; no PIN submitted.");return;
            }
            if(EditorApplication.timeSinceStartup>SessionState.GetFloat("RehearReadbackDeadline",0))
            { Phase("done");Log("BLOCKED: Firebase did not initialize. PIN not submitted."); }
            return;
        }
        if(phase=="ready" && LocalTest)
        {
            // Supply a local success result at the presentation boundary. Never query a PIN.
            var session=new SessionData
            {
                status="ready",
                page_1=new Page1 { presentation_title="로컬 씬 전환 검증",presentation_purpose="발표 모드",duration_minutes=1,environment_type="세미나실",qa_count=0,used_language="ko" },
                page_2=new Page2 { presentation_script_content="씬 전환만 확인하는 로컬 테스트 데이터입니다." },
                page_3=new Page3 { audience_scale=6,audience_expertise="중간",audience_interest="중간",audience_type="일반 청중" }
            };
            if(!(bool)typeof(PinInputManager).GetMethod("LoadEvcPresentationContext",Private).Invoke(manager,new object[]{"0000",session,true}))
                throw new Exception("Local fixture validation failed.");
            RuntimeSessionData.Load("0000",session);
            typeof(PinInputManager).GetMethod("ApplySessionInformation",Private).Invoke(manager,new object[]{session});
            if(!loading.activeInHierarchy || !Flag(manager,"isLoading") || root.Find("PinPanel").gameObject.activeSelf || root.Find("NumberKeyboard").gameObject.activeSelf)
                throw new Exception("Successful load did not retain loading-only UI.");
            if(loading.transform.Find("Title").GetComponent<TMP_Text>().text!="세션 불러오는 중")
                throw new Exception("Loading title changed to a completion message.");
            ScreenCapture.CaptureScreenshot("Temp/RehearPin-transition-loading.png");
            Phase("transition");Log("PASS LOCAL: loading title and input lock retained; automatic transition pending; requests=0.");return;
        }
        if(phase=="ready" && File.Exists(Request))
        {
            string pin=File.ReadAllText(Request).Trim();
            if(pin.Length!=4 || pin.Any(c=>c<'0'||c>'9'))throw new Exception("Expected exactly four digits.");
            // Consume the local instruction BEFORE the call. Never retry after an uncertain result.
            File.Delete(Request);Phase("submitted");Log("SUBMIT_ONCE: one UI submit; automatic retry disabled.");
            manager.ResetInput();manager.OpenKeyboard();
            foreach(char digit in pin)root.GetComponentsInChildren<Button>(true).Single(b=>b.name=="Key"+digit).onClick.Invoke();
            if(manager.GetFullPin()!=pin)throw new Exception("Digit callbacks did not match the requested PIN.");
            manager.OnSubmitButtonClicked();
            Log("LOADING_VISIBLE="+loading.activeInHierarchy+" INPUT_LOCKED="+Flag(manager,"isLoading"));
            if(!loading.activeInHierarchy)throw new Exception("Submission did not show loading panel.");
            Capture("pin-readback-loading");return;
        }
        if(phase=="submitted")
        {
            if(RuntimeSessionData.Session!=null && PresentationSessionContext.Current.HasPresentation)
            {
                // Observe the real application flow. Never stop navigation or replace its UI.
                Log("LOADED: server session cached; waiting for automatic tutorial transition; requests=1.");
                Phase("transition");return;
            }
            if(!Flag(manager,"isLoading"))
            {
                string error=root.GetComponentsInChildren<TMP_Text>(true).Single(t=>t.name=="Error").text;
                Phase("done");Log("FAILED: "+error+"; no retry performed.");Capture("pin-readback-error");return;
            }
            if(EditorApplication.timeSinceStartup>SessionState.GetFloat("RehearReadbackDeadline",0))
            { Phase("done");Log("UNKNOWN: deadline exceeded; no retry permitted."); }
        }
    }
}
