#if REHEAR_QUEST_DIAGNOSTICS
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Inputs;

public sealed class RehearQuestDiagnostics : MonoBehaviour
{
    string checkedUIInScene;
    readonly System.Collections.Generic.HashSet<int> checkedButtons = new();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        var obj = new GameObject("Quest input diagnostics");
        DontDestroyOnLoad(obj);
        var diagnostics = obj.AddComponent<RehearQuestDiagnostics>();
#if UNITY_ANDROID && !UNITY_EDITOR
        using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
        using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
        using var intent = activity.Call<AndroidJavaObject>("getIntent");
        if (intent.Call<bool>("getBooleanExtra", "rehear_blur_transition_test", false))
            diagnostics.StartCoroutine(diagnostics.CheckBlurTransitions());
#endif
    }

    IEnumerator CheckBlurTransitions()
    {
        Debug.Log("[BlurTransitionTest] Waiting for headset focus.");
        while (!Application.isFocused) yield return null;
        yield return new WaitForSecondsRealtime(5);
        for (int i = 0; i < 6; i++)
        {
            string scene = i % 2 == 0 ? "Scene_00_5_Tutorial" : "Scene_00";
            yield return SceneManager.LoadSceneAsync(scene);
            yield return new WaitForSecondsRealtime(5);
            var sources = FindObjectsByType<LeTai.Asset.TranslucentImage.TranslucentImageSource>(FindObjectsSortMode.None);
            bool ready = sources.Length > 0;
            foreach (var source in sources)
                ready &= source.isActiveAndEnabled && source.BlurredScreen && source.BlurredScreen.IsCreated();
            Debug.Log($"[BlurTransitionTest] step={i + 1}/6 scene={scene} liveBlurReady={ready}");
            if (!ready) yield break;
        }
        Debug.Log("[BlurTransitionTest] PASS: six scene transitions with live blur.");
    }

    IEnumerator Start()
    {
        while (true)
        {
            yield return new WaitForSecondsRealtime(5);
            Debug.Log($"[QuestInput] scene={SceneManager.GetActiveScene().name} focus={Application.isFocused}");
            if (SceneManager.GetActiveScene().name == "Scene_02_Presentation")
            {
                LogPresentationPointer();
                checkedUIInScene = "Scene_02_Presentation";
                CheckPresentationTargets();
            }
            else { checkedUIInScene = null; checkedButtons.Clear(); }
            foreach (var device in InputSystem.devices)
            {
                if (device is TrackedDevice tracked)
                    Debug.Log($"[QuestInput] device={device.name} layout={device.layout} enabled={device.enabled} usages={device.usages} tracked={tracked.isTracked.ReadValue()} state={tracked.trackingState.ReadValue()} position={tracked.devicePosition.ReadValue()}");
            }
            foreach (var manager in FindObjectsByType<InputActionManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Debug.Log($"[QuestInput] manager={manager.name} active={manager.isActiveAndEnabled}");
                if (manager.actionAssets == null) continue;
                foreach (var asset in manager.actionAssets)
                {
                    if (!asset) continue;
                    foreach (var map in asset.actionMaps)
                    {
                        if (!map.name.Contains("Tracking") && !map.name.Contains("Interaction")) continue;
                        foreach (var action in map.actions)
                            if (action.name == "Position" || action.name == "Is Tracked" || action.name == "UI Press")
                                Debug.Log($"[QuestInput] action={map.name}/{action.name} enabled={action.enabled} controls={action.controls.Count} value={action.ReadValueAsObject()}");
                    }
                }
            }
            foreach (var manager in FindObjectsByType<XRInputModalityManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Debug.Log($"[QuestInput] modality={manager.name} active={manager.isActiveAndEnabled} left={manager.leftController?.activeInHierarchy} right={manager.rightController?.activeInHierarchy}");
            }
        }
    }

    void LogPresentationPointer()
    {
        var module = UnityEngine.EventSystems.EventSystem.current?.currentInputModule as CurvedUI.Core.CurvedUIInputModule;
        if (!module || !module.PointerTransform) { Debug.Log("[PresentationUI] Missing active CurvedUI pointer"); return; }
        var ray = module.GetEventRay();
        var collider = Physics.Raycast(ray, out var hit, 20, module.RaycastLayerMask) ? Path(hit.transform) : "none";
        Debug.Log($"[PresentationUI] pointer={Path(module.PointerTransform)} hit={collider} ui={Path(module.CurrentRaycast.gameObject ? module.CurrentRaycast.gameObject.transform : null)}");
    }

    void CheckPresentationTargets()
    {
        var system = UnityEngine.EventSystems.EventSystem.current;
        var module = system?.currentInputModule as CurvedUI.Core.CurvedUIInputModule;
        if (!module || !Camera.main) return;
        Canvas.ForceUpdateCanvases();
        Physics.SyncTransforms();
        var previous = module.PointerTransformOverride;
        var probe = new GameObject("Presentation UI ray probe");
        var results = new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
        try
        {
            module.PointerTransformOverride = probe.transform;
            foreach (var button in FindObjectsByType<UnityEngine.UI.Button>(FindObjectsSortMode.None))
            {
                var settings = button.GetComponentInParent<CurvedUI.CurvedUISettings>();
                if (!settings) continue;
                if (!checkedButtons.Add(button.GetInstanceID())) continue;
                var rect = (RectTransform)button.transform;
                var world = rect.TransformPoint(rect.rect.center);
                var target = settings.CanvasToCurvedCanvas(settings.transform.InverseTransformPoint(world));
                probe.transform.position = Camera.main.transform.position - Vector3.up * .2f;
                probe.transform.rotation = Quaternion.LookRotation(target - probe.transform.position);
                results.Clear();
                system.RaycastAll(new UnityEngine.EventSystems.PointerEventData(system), results);
                var first = results.Count > 0 ? results[0].gameObject.GetComponentInParent<UnityEngine.UI.Button>() : null;
                var ray = new Ray(probe.transform.position, probe.transform.forward);
                var collider = Physics.Raycast(ray, out var hit, 20, module.RaycastLayerMask) ? Path(hit.transform) : "none";
                Debug.Log($"[PresentationUITest] target={Path(button.transform)} enabled={button.IsInteractable()} match={first == button} collider={collider} ui={(results.Count > 0 ? Path(results[0].gameObject.transform) : "none")}");
            }
        }
        finally { module.PointerTransformOverride = previous; Destroy(probe); }
    }

    static string Path(Transform item)
    {
        if (!item) return "none";
        return item.parent ? Path(item.parent) + "/" + item.name : item.name;
    }
}
#endif
