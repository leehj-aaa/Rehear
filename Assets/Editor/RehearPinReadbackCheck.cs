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
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static RehearPinReadbackCheck() { EditorApplication.update+=Tick; }
    static bool Flag(PinInputManager m,string name)=>(bool)typeof(PinInputManager).GetField(name,Private).GetValue(m);
    static void Log(string message)=>File.AppendAllText(Report,DateTime.Now.ToString("HH:mm:ss")+" "+message+"\n");
    static void Phase(string phase) { SessionState.SetString(PhaseKey,phase); SessionState.SetFloat("RehearReadbackDeadline",(float)EditorApplication.timeSinceStartup+45); }
    static void Capture(string name)=>typeof(RehearOpeningStartSetup).GetMethod("Capture",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{name,-1f});
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
            if(command!="prepare")return;
            File.Delete(Request);
            var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if(scene.name!="Scene_00" || UnityEngine.SceneManagement.SceneManager.sceneCount!=1)throw new Exception("Only Scene_00 may be open.");
            SessionState.SetBool("RehearPinRuntimeTest",false);SessionState.SetBool("RehearPinWaitFirebase",false);
            Phase("opening"); Log("PREPARE: initialization only; no PIN request.");
            EditorApplication.EnterPlaymode();return;
        }
        if(phase=="" || phase=="done")return;
        var manager=UnityEngine.Object.FindObjectsByType<PinInputManager>(FindObjectsInactive.Include,FindObjectsSortMode.None).FirstOrDefault(m=>m.gameObject.scene.name=="Scene_00");
        if(!manager)throw new Exception("Opening PIN manager not found.");
        var root=manager.transform;
        var loading=root.Find("LoadingPanel").gameObject;
        if(phase=="opening")
        {
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
                // Keep the loaded session in memory; do not start tutorial, presentation, or recording.
                manager.StopAllCoroutines();typeof(PinInputManager).GetField("isLoading",Private).SetValue(manager,false);
                bool nativeContext=PresentationSessionContext.Current.HasPresentation;
                Log("SUCCESS: server session parsed and cached; EVC context="+nativeContext+"; requests=1; presentation/recording not started.");
                Phase("done");
                loading.transform.Find("Title").GetComponent<TMP_Text>().text="세션 불러오기 완료";
                loading.transform.Find("Description").GetComponent<TMP_Text>().text="웹에서 설정한 세션을 불러왔습니다.\n발표와 녹음은 시작하지 않았습니다.";
                loading.SetActive(true);Capture("pin-readback-success");return;
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
