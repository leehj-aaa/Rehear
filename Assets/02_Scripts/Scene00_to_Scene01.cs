using UnityEngine;
using Rehear.Evc.Presentation;
using UnityEngine.UI;

public class Scene00_to_Scene01 : MonoBehaviour
{
    [SerializeField] private GameObject openingPinRoot;
    [SerializeField] private GameObject openingLogo;
    [SerializeField] private Button startButton;
    private bool opened;
    // [게임 시작하기] 버튼에 연결할 함수
    public void ClickGameStart()
    {
        if (opened || (startButton && !startButton.IsInteractable())) return;
        if (!openingPinRoot)
        {
            Debug.LogError("Opening PIN screen is not connected.", this);
            return;
        }
        opened = true;
        RuntimeSessionData.Clear();
        PresentationSessionContext.Current.ClearAll();
        var camera = Camera.main;
        if (camera)
        {
            Vector3 forward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < .01f) forward = Vector3.forward;
            openingPinRoot.transform.SetPositionAndRotation(camera.transform.position + forward * 3f,
                Quaternion.LookRotation(forward, Vector3.up));
        }
        if (openingLogo) openingLogo.SetActive(false);
        if (startButton) startButton.gameObject.SetActive(false);
        // Hiding only the graphics leaves CurvedUI's generated canvas colliders
        // in front of the PIN keyboard, intercepting its physics raycasts.
        var previousCanvas = startButton ? startButton.GetComponentInParent<Canvas>(true)?.rootCanvas : null;
        if (previousCanvas && !openingPinRoot.transform.IsChildOf(previousCanvas.transform))
            previousCanvas.gameObject.SetActive(false);
        // The last keyboard row extends below the original canvas collider.
        var pinRect = openingPinRoot.transform as RectTransform;
        var keyboard = openingPinRoot.transform.Find("NumberKeyboard") as RectTransform;
        if (pinRect && keyboard)
        {
            var corners = new Vector3[4];
            keyboard.GetWorldCorners(corners);
            float halfHeight = pinRect.rect.height * 0.5f;
            foreach (var corner in corners)
                halfHeight = Mathf.Max(halfHeight, Mathf.Abs(pinRect.InverseTransformPoint(corner).y) + 20f);
            pinRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, halfHeight * 2f);
        }
        openingPinRoot.SetActive(true);
#if REHEAR_QUEST_DIAGNOSTICS
        StartCoroutine(CheckKeyboardTargets());
#endif
    }

#if REHEAR_QUEST_DIAGNOSTICS
    private System.Collections.IEnumerator CheckKeyboardTargets()
    {
        yield return null;
        yield return null;
        Canvas.ForceUpdateCanvases();
        Physics.SyncTransforms();
        var settings = openingPinRoot.GetComponent<CurvedUI.CurvedUISettings>();
        var module = UnityEngine.EventSystems.EventSystem.current?.currentInputModule as CurvedUI.Core.CurvedUIInputModule;
        if (!settings || !module || !Camera.main) yield break;
        var probe = new GameObject("Keyboard ray validation");
        var previousPointer = module.PointerTransformOverride;
        int passed = 0, failed = 0;
        try
        {
            module.PointerTransformOverride = probe.transform;
            var results = new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
            foreach (var button in openingPinRoot.GetComponentsInChildren<Button>())
            {
                if (!button.name.StartsWith("Key") && button.name != "ConfirmPin") continue;
                var rect = (RectTransform)button.transform;
                foreach (float handX in new[] { -0.25f, 0f, 0.25f })
                foreach (var fraction in new[] { Vector2.zero, new Vector2(-0.3f, 0), new Vector2(0.3f, 0), new Vector2(0, -0.3f), new Vector2(0, 0.3f) })
                {
                    Vector3 local = rect.rect.center + Vector2.Scale(rect.rect.size, fraction);
                    Vector3 flatWorld = rect.TransformPoint(local);
                    Vector3 target = settings.CanvasToCurvedCanvas(settings.transform.InverseTransformPoint(flatWorld));
                    probe.transform.position = Camera.main.transform.position + Camera.main.transform.right * handX - Vector3.up * 0.2f;
                    probe.transform.rotation = Quaternion.LookRotation(target - probe.transform.position);
                    results.Clear();
                    var eventSystem = UnityEngine.EventSystems.EventSystem.current;
                    eventSystem.RaycastAll(new UnityEngine.EventSystems.PointerEventData(eventSystem), results);
                    var hitButton = results.Count > 0 ? results[0].gameObject.GetComponentInParent<Button>() : null;
                    if (hitButton == button) passed++;
                    else { failed++; Debug.LogWarning($"[KeyboardRayCheck] {button.name} missed at {fraction}; hit={(results.Count > 0 ? results[0].gameObject.name : "none")}"); }
                }
            }
        }
        finally { module.PointerTransformOverride = previousPointer; Destroy(probe); }
        Debug.Log($"[KeyboardRayCheck] PASS={passed} FAIL={failed}");
    }
#endif
}
