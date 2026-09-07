using UnityEngine;
using UnityEngine.XR;

/// <summary>Life-size tracked right-hand demonstration. Does not move XR inputs or rays.</summary>
public sealed class Quest3TutorialControllerVisual : MonoBehaviour
{
    public enum Cue { Hidden, Idle, Trigger, StickHorizontal, StickVertical, Grip }
    static readonly int TriggerParameter = Animator.StringToHash("Trigger");
    static readonly int GripParameter = Animator.StringToHash("Grip");
    static readonly int JoyXParameter = Animator.StringToHash("Joy X");
    static readonly int JoyYParameter = Animator.StringToHash("Joy Y");
    [SerializeField] GameObject controllerModelPrefab;
    [SerializeField] RuntimeAnimatorController controllerAnimator;
    [Header("Tracked right hand grip pose, not camera or ray aim")]
    [SerializeField] Transform rightHandTarget;
    [SerializeField] Renderer[] originalControllerRenderers;
    [SerializeField] Vector3 gripPoseOffset;
    [SerializeField] Vector3 gripPoseEuler;
    [Header("Button highlights")]
    [SerializeField] Mesh triggerHighlight;
    [SerializeField] Mesh stickHighlight;
    [SerializeField] Mesh gripHighlight;
    [SerializeField] Material highlightMaterial;
    [SerializeField, Min(.2f)] float cycleDuration = 1.35f;
    Transform visualRoot;
    Animator animator;
    SkinnedMeshRenderer highlight;
    Material runtimeHighlight;
    bool[] previousForceOff;
    bool suppressingOriginal;
    Cue currentCue = Cue.Hidden;
    float cueStartedAt;

    void Awake() { BuildVisual(); ApplyVisibility(); }
    void OnEnable() { ApplyVisibility(); }
    void OnDisable()
    {
        if (visualRoot) visualRoot.gameObject.SetActive(false);
        RestoreOriginal();
    }
    void OnDestroy()
    {
        RestoreOriginal();
        if (visualRoot) Destroy(visualRoot.gameObject);
        if (runtimeHighlight) Destroy(runtimeHighlight);
    }
    void LateUpdate()
    {
        ApplyVisibility();
        if (!visualRoot || !visualRoot.gameObject.activeSelf) return;
        AnimateCue(Mathf.Repeat((Time.unscaledTime - cueStartedAt) / Mathf.Max(.2f, cycleDuration), 1f));
    }
    public void Show(Cue cue)
    {
        currentCue = cue;
        cueStartedAt = Time.unscaledTime;
        if (highlight)
        {
            highlight.sharedMesh = cue == Cue.Trigger ? triggerHighlight : cue == Cue.Grip ? gripHighlight : stickHighlight;
            highlight.enabled = cue != Cue.Hidden && cue != Cue.Idle;
        }
        ApplyVisibility();
        if (animator && animator.isActiveAndEnabled && animator.runtimeAnimatorController)
        {
            if (!animator.isInitialized) { animator.Rebind(); animator.Update(0f); }
            ResetAnimatorParameters();
        }
    }
    void BuildVisual()
    {
        if (!controllerModelPrefab || !rightHandTarget) return;
        visualRoot = new GameObject("Right Hand Tutorial Overlay").transform;
        // Parenting propagates before-render tracking without smoothing or bobbing.
        visualRoot.SetParent(rightHandTarget, false);
        visualRoot.SetLocalPositionAndRotation(gripPoseOffset, Quaternion.Euler(gripPoseEuler));
        var model = Instantiate(controllerModelPrefab, visualRoot);
        model.name = "Meta Quest Touch Plus Right Demonstration";
        model.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        model.transform.localScale = controllerModelPrefab.transform.localScale;
        animator = model.GetComponentInChildren<Animator>(true);
        if (animator)
        {
            animator.runtimeAnimatorController = controllerAnimator;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.updateMode = AnimatorUpdateMode.UnscaledTime;
            if (animator.isActiveAndEnabled && animator.runtimeAnimatorController)
            {
                animator.Rebind();
                animator.Update(0f);
            }
        }
        foreach (var r in model.GetComponentsInChildren<Renderer>(true))
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            if (r.name.Contains("batteryIndicator")) r.gameObject.SetActive(false);
        }
        var body = model.transform.Find("oculus_controller_r_MeshX")?.GetComponent<SkinnedMeshRenderer>();
        if (body && highlightMaterial)
        {
            var go = new GameObject("Active Button Highlight");
            go.transform.SetParent(body.transform, false);
            highlight = go.AddComponent<SkinnedMeshRenderer>();
            highlight.bones = body.bones;
            highlight.rootBone = body.rootBone;
            highlight.localBounds = body.localBounds;
            highlight.updateWhenOffscreen = true;
            highlight.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            highlight.receiveShadows = false;
            runtimeHighlight = new Material(highlightMaterial);
            highlight.sharedMaterial = runtimeHighlight;
        }
    }
    void AnimateCue(float phase)
    {
        if (!animator || !animator.isActiveAndEnabled || !animator.isInitialized) return;
        float action = TutorialPulse(phase);
        switch (currentCue)
        {
            case Cue.Trigger: animator.SetFloat(TriggerParameter, action); break;
            case Cue.Grip: animator.SetFloat(GripParameter, action); break;
            case Cue.StickHorizontal: animator.SetFloat(JoyXParameter, Mathf.Sin(phase * Mathf.PI * 2f)); break;
            case Cue.StickVertical: animator.SetFloat(JoyYParameter, Mathf.Sin(phase * Mathf.PI * 2f)); break;
        }
        if (runtimeHighlight) runtimeHighlight.SetColor("_BaseColor", Color.Lerp(new Color(0, .2f, 1), new Color(.15f, .6f, 1), action));
    }
    static float TutorialPulse(float phase)
    {
        if (phase < .15f || phase > .85f) return 0;
        return Mathf.SmoothStep(0, 1, phase <= .5f ? Mathf.InverseLerp(.15f, .5f, phase) : Mathf.InverseLerp(.85f, .5f, phase));
    }
    void ResetAnimatorParameters()
    {
        if (!animator || !animator.isActiveAndEnabled || !animator.isInitialized) return;
        animator.SetFloat(TriggerParameter, 0);
        animator.SetFloat(GripParameter, 0);
        animator.SetFloat(JoyXParameter, 0);
        animator.SetFloat(JoyYParameter, 0);
    }
    void ApplyVisibility()
    {
        bool tracked = true;
        if (XRSettings.enabled)
        {
            var device = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            tracked = device.isValid && device.TryGetFeatureValue(CommonUsages.isTracked, out bool value) && value;
        }
        bool show = isActiveAndEnabled && currentCue != Cue.Hidden && rightHandTarget && rightHandTarget.gameObject.activeInHierarchy && tracked;
        if (visualRoot) visualRoot.gameObject.SetActive(show);
        if (show && visualRoot && !suppressingOriginal)
        {
            previousForceOff = new bool[originalControllerRenderers?.Length ?? 0];
            for (int i = 0; i < previousForceOff.Length; i++)
            {
                var r = originalControllerRenderers[i];
                if (!r) continue;
                previousForceOff[i] = r.forceRenderingOff;
                r.forceRenderingOff = true;
            }
            suppressingOriginal = true;
        }
        else if (!show) RestoreOriginal();
    }
    void RestoreOriginal()
    {
        if (!suppressingOriginal) return;
        for (int i = 0; i < previousForceOff.Length; i++)
            if (originalControllerRenderers[i]) originalControllerRenderers[i].forceRenderingOff = previousForceOff[i];
        suppressingOriginal = false;
    }
}
