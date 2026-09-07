using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Guides the tracked right trigger only while the opening CTA is waiting for input.</summary>
[DisallowMultipleComponent, RequireComponent(typeof(Button))]
public sealed class OpeningStartCue : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
    IPointerDownHandler, IPointerUpHandler
{
    public Quest3TutorialControllerVisual controllerVisual;
    Button button;
    CanvasGroup[] groups;
    readonly HashSet<int> hovering = new HashSet<int>();
    readonly HashSet<int> pressing = new HashSet<int>();
    Quest3TutorialControllerVisual.Cue lastCue = Quest3TutorialControllerVisual.Cue.Hidden;
    float resumeAt;
    bool completed, paused, introReady;

    void Awake() { button = GetComponent<Button>(); groups = GetComponentsInParent<CanvasGroup>(true); }
    void OnEnable()
    {
        completed = false;
        hovering.Clear(); pressing.Clear();
        resumeAt = Time.unscaledTime + .35f;
        if (!button) button = GetComponent<Button>();
        groups = GetComponentsInParent<CanvasGroup>(true);
        button.onClick.AddListener(Complete);
    }
    void Update()
    {
        bool ready = introReady && button && button.isActiveAndEnabled && button.IsInteractable() && !completed && !paused;
        if (groups != null) foreach (var group in groups) if (group && group.alpha < .95f) ready = false;
        var cue = ready ? Quest3TutorialControllerVisual.Cue.Trigger : Quest3TutorialControllerVisual.Cue.Hidden;
        SetCue(cue);
        // Aiming at the CTA must not hide which physical button to press.
        // Freeze motion but retain a steady blue trigger until the click completes.
        if (controllerVisual) controllerVisual.SetPulsePaused(ready &&
            (hovering.Count > 0 || pressing.Count > 0 || Time.unscaledTime < resumeAt));
#if UNITY_EDITOR
        if (controllerVisual) controllerVisual.RefreshUntrackedEditorPreview();
#endif
    }
    public void SetIntroReady(bool ready)
    {
        introReady = ready;
        groups = GetComponentsInParent<CanvasGroup>(true);
        if (ready) resumeAt = Time.unscaledTime;
        else SetCue(Quest3TutorialControllerVisual.Cue.Hidden);
        if (isActiveAndEnabled) Update();
    }
    void SetCue(Quest3TutorialControllerVisual.Cue cue)
    {
        if (!controllerVisual || cue == lastCue) return;
        controllerVisual.Show(cue);
        lastCue = cue;
    }
    public void Complete() { completed = true; SetCue(Quest3TutorialControllerVisual.Cue.Hidden); }
    public void OnPointerEnter(PointerEventData e) { hovering.Add(e.pointerId); Update(); }
    public void OnPointerExit(PointerEventData e) { hovering.Remove(e.pointerId); resumeAt = Time.unscaledTime + .35f; Update(); }
    public void OnPointerDown(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) { pressing.Add(e.pointerId); Update(); } }
    public void OnPointerUp(PointerEventData e) { pressing.Remove(e.pointerId); resumeAt = Time.unscaledTime + .35f; Update(); }
    void OnApplicationPause(bool value) { paused = value; if (value) SetCue(Quest3TutorialControllerVisual.Cue.Hidden); }
    void OnDisable()
    {
        if (button) button.onClick.RemoveListener(Complete);
        hovering.Clear(); pressing.Clear();
        SetCue(Quest3TutorialControllerVisual.Cue.Hidden);
    }
}
