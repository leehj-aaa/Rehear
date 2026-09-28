using System.Collections;
using Rehear.Evc.Audience;
using UnityEngine;

[DefaultExecutionOrder(100)]
public class AudienceGazeController : MonoBehaviour, IAudienceStateReceiver, IAudienceQuestionWaitReceiver, IAudienceQuestionPhaseReceiver
{
    public Transform SlideTarget => slideTarget;
    public void ConfigureTargets(Transform presenter, Transform slide, Transform[] around)
    {
        presenterTarget=presenter; slideTarget=slide; aroundTargets=around;
    }
    private enum GazeState
    {
        Presenter,
        Slide,
        AnimationPose
    }

    [Header("머리 설정")]

    [Tooltip("실제 캐릭터의 머리 뼈")]
    [SerializeField]
    private Transform headBone;

    [Tooltip(
        "머리의 정면 방향을 나타내는 빈 오브젝트. " +
        "파란색 Z축이 얼굴 정면을 향해야 합니다."
    )]
    [SerializeField]
    private Transform gazeForwardReference;

    [Header("시선 목표")]

    [Tooltip("발표자 또는 Main Camera")]
    [SerializeField]
    private Transform presenterTarget;

    [Tooltip("발표자료 화면 앞에 만든 시선 목표")]
    [SerializeField]
    private Transform slideTarget;

    [Tooltip("허공이나 주변을 보기 위한 목표들")]
    [SerializeField]
    private Transform[] aroundTargets;

    [Header("상태 확률")]

    [SerializeField]
    [Range(0f, 1f)]
    private float presenterProbability = 0.55f;

    [SerializeField]
    [Range(0f, 1f)]
    private float slideProbability = 0.30f;

    [SerializeField]
    [Range(0f, 1f)]
    private float aroundProbability = 0.15f;

    [Header("시간 설정")]

    [SerializeField]
    private Vector2 initialDelayRange =
        new Vector2(0.5f, 4f);

    [SerializeField]
    private Vector2 presenterDurationRange =
        new Vector2(2.5f, 6f);

    [SerializeField]
    private Vector2 slideDurationRange =
        new Vector2(1.5f, 4f);

    [SerializeField]
    private Vector2 aroundDurationRange =
        new Vector2(0.8f, 2.5f);

    [Header("회전 설정")]

    [Tooltip("기존 자세 애니메이션 위에 적용할 시선 가중치")]
    [SerializeField]
    [Range(0f, 1f)]
    private float gazeWeight = 0.65f;

    [Tooltip("머리가 기존 자세에서 벗어날 수 있는 최대 각도")]
    [SerializeField]
    [Range(5f, 80f)]
    private float maximumHeadAngle = 40f;

    [Tooltip("높을수록 목표를 빠르게 바라봅니다")]
    [SerializeField]
    [Range(0.5f, 15f)]
    private float directionSmoothSpeed = 4f;

    [Tooltip("애니메이션 자세에서 시선 목표로 전환되는 속도")]
    [SerializeField]
    [Range(0.1f, 5f)]
    private float gazeBlendInSpeed = 2.2f;

    [Tooltip("시선 목표에서 원래 애니메이션 자세로 복귀하는 속도")]
    [SerializeField]
    [Range(0.1f, 5f)]
    private float gazeReleaseSpeed = 0.65f;

    [Header("개인차")]

    [Tooltip(
        "청중마다 목표 지점이 조금 다르게 보이도록 하는 범위"
    )]
    [SerializeField]
    private Vector3 randomTargetOffsetRange =
        new Vector3(0.12f, 0.08f, 0.05f);

    [Header("디버그")]

    [SerializeField]
    private bool printStateLog;

    private GazeState currentState;
    private Transform currentTarget;
    private Vector3 personalTargetOffset;
    private Vector3 smoothedDirection;

    private Coroutine ruleRoutine;

    private bool hasServerOverride;
    private float serverOverrideEndTime;
    private GazeState serverOverrideState;
    private Transform serverOverrideTarget;

    private float evaluatedPresenterProbability = -1f;
    private float evaluatedSlideProbability = -1f;
    private float evaluatedAroundProbability = -1f;
    // Either low engagement or low comprehension must release the animated head.
    private float attentionGazeWeight = 1f;
    private float blendedGazeWeight;
    private AudienceAnimationPlayer animationPlayer;
    private bool hasEvaluationState;
    private bool nextFocusedTargetIsSlide;
    private bool questionGenerationWaiting;
    private bool questionPhaseActive;
    public void SetQuestionGenerationWaiting(bool waiting) => questionGenerationWaiting = waiting;
    public void SetQuestionPhaseActive(bool active) => questionPhaseActive = active;
    public float AttentionGazeWeight => attentionGazeWeight;
    public float CurrentGazeWeight => blendedGazeWeight;

    private void Awake()
    {
        animationPlayer=GetComponent<AudienceAnimationPlayer>();
        personalTargetOffset =
            new Vector3(
                Random.Range(
                    -randomTargetOffsetRange.x,
                    randomTargetOffsetRange.x
                ),
                Random.Range(
                    -randomTargetOffsetRange.y,
                    randomTargetOffsetRange.y
                ),
                Random.Range(
                    -randomTargetOffsetRange.z,
                    randomTargetOffsetRange.z
                )
            );
    }

    private void OnEnable()
    {
        if (!ValidateReferences())
            return;

        smoothedDirection =
            gazeForwardReference.forward;

        SetState(GazeState.Presenter, presenterTarget);
        blendedGazeWeight = 0f;

        ruleRoutine =
            StartCoroutine(RuleBasedGazeRoutine());
    }

    private void OnDisable()
    {
        if (ruleRoutine != null)
        {
            StopCoroutine(ruleRoutine);
            ruleRoutine = null;
        }

        hasServerOverride = false;
        questionPhaseActive = false;
        questionGenerationWaiting = false;
    }

    private void LateUpdate()
    {
        ApplyGaze(Time.deltaTime, Time.unscaledTime);
    }

#if UNITY_EDITOR
    public float ResetManualGaze()
    {
        hasServerOverride = false;
        questionPhaseActive = false;
        questionGenerationWaiting = false;
        evaluatedPresenterProbability = evaluatedSlideProbability = evaluatedAroundProbability = -1f;
        hasEvaluationState=false;
        nextFocusedTargetIsSlide=false;
        attentionGazeWeight = 1f;
        blendedGazeWeight = 0f;
        smoothedDirection = gazeForwardReference ? gazeForwardReference.forward : transform.forward;
        SetState(GazeState.Presenter, presenterTarget);
        return Random.Range(initialDelayRange.x, initialDelayRange.y);
    }

    public float AdvanceManualGaze()
    {
        SelectNextRuleState();
        return GetCurrentStateDuration();
    }
#endif

    // Shared by runtime LateUpdate and the editor's manual animation preview.
    public void ApplyGaze(float deltaTime, float clock)
    {
        if (headBone == null ||
            gazeForwardReference == null)
        {
            return;
        }

        UpdateServerOverride(clock);

        Transform target =
            hasServerOverride
                ? serverOverrideTarget
                : currentTarget;
        if(!animationPlayer)animationPlayer=GetComponent<AudienceAnimationPlayer>();
        var body=animationPlayer;
        if(body && body.TypingLookTarget) target=body.TypingLookTarget;
        if(body && body.DeviceLookTarget) target=body.DeviceLookTarget;
        if(body && body.IsQuestionTurn && presenterTarget) target=presenterTarget;
        bool takingPhoto=body && body.IsTakingPhoto;
        if(takingPhoto)target=null;

        var state = hasServerOverride ? serverOverrideState : currentState;
        bool actionTarget = body && (body.TypingLookTarget || body.DeviceLookTarget || body.IsQuestionTurn);
        // Keep authored nods readable while retaining enough gaze correction to
        // face the presenter. At full nod weight, roughly half of each system is kept.
        float nodGazeMultiplier = body ? Mathf.Lerp(1f, .55f, body.HeadNodWeight) : 1f;
        float desiredWeight = target && (state != GazeState.AnimationPose || actionTarget)
            ? gazeWeight * attentionGazeWeight * nodGazeMultiplier * (body ? 1f - body.ConversationWeight : 1f)
            : 0f;
        if(takingPhoto)desiredWeight=0f;
        // All listeners face the presenter throughout Q&A, independent of E/C,
        // random gaze and any remaining body-action look target.
        // Keep the evaluation values intact so normal gaze resumes afterwards.
        bool focusPresenter = (questionGenerationWaiting || questionPhaseActive) && presenterTarget;
        if (focusPresenter)
        {
            target = presenterTarget;
            desiredWeight = gazeWeight;
        }
        float gazeBlendSpeed = desiredWeight > blendedGazeWeight
            ? gazeBlendInSpeed
            : gazeReleaseSpeed;
        blendedGazeWeight = Mathf.MoveTowards(
            blendedGazeWeight,
            desiredWeight,
            Mathf.Max(0f, deltaTime) * gazeBlendSpeed
        );
        // When gaze is released (low E/C, photo pose, or AnimationPose), keep
        // applying the last correction while its weight fades out. Returning as
        // soon as target becomes null drops the correction in one frame and makes
        // the head visibly snap back to the authored animation.
        bool activelyTracking = target && desiredWeight > .0001f;
        if (activelyTracking)
        {
            Debug.DrawLine(
                gazeForwardReference.position,
                target.position,
                Color.cyan
            );
            Vector3 targetPosition =
                target.position + (!focusPresenter && body && (body.DeviceLookTarget || body.TypingLookTarget) ? Vector3.zero : personalTargetOffset);

            Vector3 desiredDirection =
                targetPosition -
                gazeForwardReference.position;

            if (desiredDirection.sqrMagnitude > 0.0001f)
            {
                desiredDirection.Normalize();

                float smoothFactor =
                    1f -
                    Mathf.Exp(
                        -directionSmoothSpeed *
                        Mathf.Max(0f, deltaTime)
                    );

                smoothedDirection =
                    Vector3.Slerp(
                        smoothedDirection,
                        desiredDirection,
                        smoothFactor
                    ).normalized;
            }
        }

        if (blendedGazeWeight <= 0f)
        {
            // Only reset after the correction is fully invisible, so the next
            // tracking interval starts from the current authored head direction.
            smoothedDirection = gazeForwardReference.forward;
            return;
        }

        // Animator와 FBX 애니메이션이 먼저 적용한
        // 현재 머리 회전을 기준으로 시선 보정을 계산한다.
        Quaternion animatedHeadRotation =
            headBone.rotation;

        Quaternion directionCorrection =
            Quaternion.FromToRotation(
                gazeForwardReference.forward,
                smoothedDirection
            );

        Quaternion desiredHeadRotation =
            directionCorrection *
            animatedHeadRotation;

        Quaternion limitedHeadRotation =
            Quaternion.RotateTowards(
                animatedHeadRotation,
                desiredHeadRotation,
                maximumHeadAngle
            );

        headBone.rotation =
            Quaternion.Slerp(
                animatedHeadRotation,
                limitedHeadRotation,
                blendedGazeWeight
            );
    }

    private IEnumerator RuleBasedGazeRoutine()
    {
        float initialDelay =
            Random.Range(
                initialDelayRange.x,
                initialDelayRange.y
            );

        yield return new WaitForSeconds(
            initialDelay
        );

        while (enabled)
        {
            SelectNextRuleState();

            float duration =
                GetCurrentStateDuration();

            yield return new WaitForSeconds(
                duration
            );
        }
    }

    private void SelectNextRuleState()
    {
        // Once E/C data is available, focused listeners use only the two useful
        // presentation targets. Alternation avoids repeated random selections of
        // the same target while keeping each actor's randomized dwell time.
        if(hasEvaluationState)
        {
            if(attentionGazeWeight<=0f)
            {
                SetState(GazeState.AnimationPose,null);
                return;
            }
            bool lookAtSlide=nextFocusedTargetIsSlide && slideTarget;
            if(!presenterTarget)lookAtSlide=true;
            SetState(lookAtSlide?GazeState.Slide:GazeState.Presenter,
                lookAtSlide?slideTarget:presenterTarget);
            nextFocusedTargetIsSlide=!nextFocusedTargetIsSlide;
            return;
        }

        float activePresenterProbability = evaluatedPresenterProbability >= 0f
            ? evaluatedPresenterProbability
            : presenterProbability;
        float activeSlideProbability = evaluatedSlideProbability >= 0f
            ? evaluatedSlideProbability
            : slideProbability;
        float activeAroundProbability = evaluatedAroundProbability >= 0f
            ? evaluatedAroundProbability
            : aroundProbability;

        float totalProbability =
            activePresenterProbability +
            activeSlideProbability +
            activeAroundProbability;

        if (totalProbability <= 0f)
        {
            SetState(
                GazeState.Presenter,
                presenterTarget
            );

            return;
        }

        float randomValue =
            Random.Range(
                0f,
                totalProbability
            );

        if (randomValue <
            activePresenterProbability)
        {
            SetState(
                GazeState.Presenter,
                presenterTarget
            );
        }
        else if (
            randomValue <
            activePresenterProbability +
            activeSlideProbability)
        {
            SetState(
                GazeState.Slide,
                slideTarget
            );
        }
        else
        {
            Transform aroundTarget =
                GetRandomAroundTarget();

            SetState(GazeState.AnimationPose, aroundTarget);
        }
    }

    public void ApplyEvaluationState(float engagement, float clarity)
    {
        // A high value in one channel must not mask a low value in the other.
        // Below .35, use the original animation; from .35 to .70, blend gaze in.
        float attention = Mathf.Clamp01(Mathf.Min(engagement, clarity));
        hasEvaluationState=true;
        attentionGazeWeight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.35f, .70f, attention));
        evaluatedPresenterProbability = Mathf.Lerp(0.20f, 0.65f, attention);
        evaluatedSlideProbability = Mathf.Lerp(0.15f, 0.30f, attention);
        evaluatedAroundProbability = Mathf.Max(
            0f,
            1f - evaluatedPresenterProbability - evaluatedSlideProbability
        );

        if (printStateLog)
        {
            Debug.Log(
                "[청중 시선 E/C 반영] " + gameObject.name +
                "\nE: " + engagement.ToString("F3") +
                "\nC: " + clarity.ToString("F3") +
                "\n발표자 응시 확률: " + evaluatedPresenterProbability.ToString("P0")
            );
        }
    }

    public void ApplyAudienceState(float engagement, float clarity)
    {
        ApplyEvaluationState(engagement, clarity);
    }

    private void SetState(
        GazeState state,
        Transform target)
    {
        currentState = state;
        currentTarget = target;

        personalTargetOffset =
            new Vector3(
                Random.Range(
                    -randomTargetOffsetRange.x,
                    randomTargetOffsetRange.x
                ),
                Random.Range(
                    -randomTargetOffsetRange.y,
                    randomTargetOffsetRange.y
                ),
                Random.Range(
                    -randomTargetOffsetRange.z,
                    randomTargetOffsetRange.z
                )
            );

        if (printStateLog)
        {
            Debug.Log(
                "[청중 시선] " +
                gameObject.name +
                " → " +
                state
            );
        }
    }

    private float GetCurrentStateDuration()
    {
        switch (currentState)
        {
            case GazeState.Slide:
                return Random.Range(
                    slideDurationRange.x,
                    slideDurationRange.y
                );

            case GazeState.AnimationPose:
                return Random.Range(
                    aroundDurationRange.x,
                    aroundDurationRange.y
                );

            default:
                return Random.Range(
                    presenterDurationRange.x,
                    presenterDurationRange.y
                );
        }
    }

    private Transform GetRandomAroundTarget()
    {
        if (aroundTargets == null ||
            aroundTargets.Length == 0)
        {
            return null;
        }

        int startIndex =
            Random.Range(
                0,
                aroundTargets.Length
            );

        for (int offset = 0;
             offset < aroundTargets.Length;
             offset++)
        {
            int index =
                (
                    startIndex + offset
                ) %
                aroundTargets.Length;

            if (aroundTargets[index] != null)
                return aroundTargets[index];
        }

        return null;
    }

    public void ApplyServerGaze(
        string actionId,
        float duration,
        float clock = -1f)
    {
        if (string.IsNullOrWhiteSpace(
                actionId))
        {
            return;
        }

        string normalized =
            actionId.ToLowerInvariant();

        if (
            normalized.Contains("slide") ||
            normalized.Contains("screen") ||
            normalized.Contains("tracking"))
        {
            serverOverrideState =
                GazeState.Slide;

            serverOverrideTarget =
                slideTarget;
        }
        else if (
            normalized.Contains("off_target") ||
            normalized.Contains("withdrawal") ||
            normalized.Contains("disengaged") ||
            normalized.Contains("device") ||
            normalized.Contains("laptop"))
        {
            serverOverrideState =
                GazeState.AnimationPose;

            serverOverrideTarget =
                GetRandomAroundTarget();
        }
        else
        {
            serverOverrideState =
                GazeState.Presenter;

            serverOverrideTarget =
                presenterTarget;
        }

        if (serverOverrideTarget == null)
            serverOverrideTarget = presenterTarget;

        hasServerOverride = true;

        serverOverrideEndTime =
            (clock < 0 ? Time.unscaledTime : clock) +
            Mathf.Max(0.2f, duration);

        if (printStateLog)
        {
            Debug.Log(
                "[청중 시선 AI Override] " +
                gameObject.name +
                " → " +
                serverOverrideState +
                "\nAction ID: " +
                actionId
            );
        }
    }

    private void UpdateServerOverride(float clock)
    {
        if (!hasServerOverride)
            return;

        if (clock >=
            serverOverrideEndTime)
        {
            hasServerOverride = false;
            serverOverrideTarget = null;
        }
    }

    private bool ValidateReferences()
    {
        if (headBone == null)
        {
            Debug.LogWarning(
                gameObject.name +
                ": Head Bone이 연결되지 않았습니다.",
                this
            );

            return false;
        }

        if (gazeForwardReference == null)
        {
            Debug.LogWarning(
                gameObject.name +
                ": Gaze Forward Reference가 " +
                "연결되지 않았습니다.",
                this
            );

            return false;
        }

        if (presenterTarget == null)
        {
            Debug.LogWarning(
                gameObject.name +
                ": Presenter Target이 연결되지 않았습니다.",
                this
            );

            return false;
        }

        return true;
    }
}
