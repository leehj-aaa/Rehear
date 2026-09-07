using UnityEngine;

/// <summary>
/// Displays a camera-relative Meta Quest Touch Plus controller and drives the
/// official Meta controller animator with a looping tutorial cue.
/// </summary>
public sealed class Quest3TutorialControllerVisual : MonoBehaviour
{
    public enum Cue
    {
        Hidden,
        Idle,
        Trigger,
        StickHorizontal,
        StickVertical,
        Grip
    }

    private static readonly int TriggerParameter = Animator.StringToHash("Trigger");
    private static readonly int GripParameter = Animator.StringToHash("Grip");
    private static readonly int JoyXParameter = Animator.StringToHash("Joy X");
    private static readonly int JoyYParameter = Animator.StringToHash("Joy Y");

    [Header("Quest 3 Controller")]
    [SerializeField] private GameObject controllerModelPrefab;
    [SerializeField] private RuntimeAnimatorController controllerAnimator;

    [Header("Placement")]
    [SerializeField] private Transform followTarget;
    [SerializeField] private Vector3 cameraOffset = new(0.3f, -0.16f, 0.62f);
    [SerializeField] private Vector3 modelEulerAngles = new(18f, 165f, -10f);
    [SerializeField, Min(0.1f)] private float modelScale = 1.35f;
    [SerializeField, Min(0f)] private float followSharpness = 14f;

    [Header("Animation")]
    [SerializeField, Min(0.2f)] private float cycleDuration = 1.35f;
    [SerializeField, Range(0f, 0.05f)] private float floatAmount = 0.012f;
    [SerializeField, Range(0f, 10f)] private float idleRockDegrees = 2.5f;

    private Transform visualRoot;
    private Animator animator;
    private Cue currentCue = Cue.Hidden;
    private float cueStartedAt;

    private void Awake()
    {
        if (followTarget == null && Camera.main != null)
            followTarget = Camera.main.transform;

        BuildVisual();
        ApplyVisibility();
    }

    private void OnDisable()
    {
        ResetAnimatorParameters();
        if (visualRoot != null)
            visualRoot.gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        ApplyVisibility();
    }

    private void LateUpdate()
    {
        if (visualRoot == null || followTarget == null || currentCue == Cue.Hidden)
            return;

        float followT = followSharpness <= 0f
            ? 1f
            : 1f - Mathf.Exp(-followSharpness * Time.unscaledDeltaTime);

        Vector3 targetPosition = followTarget.TransformPoint(cameraOffset);
        Quaternion targetRotation = followTarget.rotation;
        visualRoot.position = Vector3.Lerp(visualRoot.position, targetPosition, followT);
        visualRoot.rotation = Quaternion.Slerp(visualRoot.rotation, targetRotation, followT);

        float phase = Mathf.Repeat(
            (Time.unscaledTime - cueStartedAt) / Mathf.Max(0.2f, cycleDuration),
            1f);

        float bob = Mathf.Sin(phase * Mathf.PI * 2f) * floatAmount;
        float rock = Mathf.Sin(phase * Mathf.PI * 2f) * idleRockDegrees;

        Transform modelTransform = visualRoot.childCount > 0
            ? visualRoot.GetChild(0)
            : null;

        if (modelTransform != null)
        {
            modelTransform.localPosition = new Vector3(0f, bob, 0f);
            modelTransform.localRotation = Quaternion.Euler(
                modelEulerAngles + new Vector3(0f, rock, 0f));
        }

        AnimateCue(phase);
    }

    public void Show(Cue cue)
    {
        if (currentCue == cue)
            return;

        currentCue = cue;
        cueStartedAt = Time.unscaledTime;
        ResetAnimatorParameters();
        ApplyVisibility();

        if (visualRoot != null && followTarget != null && cue != Cue.Hidden)
        {
            visualRoot.position = followTarget.TransformPoint(cameraOffset);
            visualRoot.rotation = followTarget.rotation;
        }
    }

    private void BuildVisual()
    {
        if (controllerModelPrefab == null)
        {
            Debug.LogWarning("[튜토리얼] Quest 3 컨트롤러 모델이 연결되지 않았습니다.", this);
            return;
        }

        GameObject rootObject = new("Quest 3 Controller Tutorial Visual");
        visualRoot = rootObject.transform;
        visualRoot.SetParent(transform, false);

        GameObject modelObject = Instantiate(controllerModelPrefab, visualRoot);
        modelObject.name = "Meta Quest Touch Plus - Right";
        modelObject.transform.SetLocalPositionAndRotation(
            Vector3.zero,
            Quaternion.Euler(modelEulerAngles));
        modelObject.transform.localScale = Vector3.one * modelScale;

        animator = modelObject.GetComponentInChildren<Animator>(true);
        if (animator == null)
        {
            Debug.LogWarning("[튜토리얼] Quest 3 컨트롤러 Animator를 찾지 못했습니다.", this);
            return;
        }

        if (controllerAnimator != null)
            animator.runtimeAnimatorController = controllerAnimator;

        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.updateMode = AnimatorUpdateMode.UnscaledTime;
        animator.Rebind();
        animator.Update(0f);
    }

    private void AnimateCue(float phase)
    {
        if (animator == null)
            return;

        // Leave a neutral beat in every loop so each action reads clearly.
        float action = TutorialPulse(phase);

        switch (currentCue)
        {
            case Cue.Trigger:
                animator.SetFloat(TriggerParameter, action);
                break;

            case Cue.Grip:
                animator.SetFloat(GripParameter, action);
                break;

            case Cue.StickHorizontal:
                animator.SetFloat(JoyXParameter, Mathf.Sin(phase * Mathf.PI * 2f) * action);
                break;

            case Cue.StickVertical:
                animator.SetFloat(JoyYParameter, Mathf.Sin(phase * Mathf.PI * 2f) * action);
                break;
        }
    }

    private static float TutorialPulse(float phase)
    {
        if (phase < 0.15f || phase > 0.85f)
            return 0f;

        float normalized = Mathf.InverseLerp(0.15f, 0.5f, phase);
        if (phase > 0.5f)
            normalized = Mathf.InverseLerp(0.85f, 0.5f, phase);

        return Mathf.SmoothStep(0f, 1f, normalized);
    }

    private void ResetAnimatorParameters()
    {
        if (animator == null)
            return;

        animator.SetFloat(TriggerParameter, 0f);
        animator.SetFloat(GripParameter, 0f);
        animator.SetFloat(JoyXParameter, 0f);
        animator.SetFloat(JoyYParameter, 0f);
    }

    private void ApplyVisibility()
    {
        if (visualRoot != null)
            visualRoot.gameObject.SetActive(isActiveAndEnabled && currentCue != Cue.Hidden);
    }
}
