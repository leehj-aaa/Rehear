using System.Collections;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

public class AudienceAnimationPlayer :
    MonoBehaviour
{
    [Header("청중 설정")]

    [SerializeField]
    private Animator targetAnimator;

    [SerializeField]
    private AudienceGender gender =
        AudienceGender.Male;

    [SerializeField]
    private AudienceAnimationCatalog catalog;

    [Header("기본 자세")]

    [Tooltip(
        "비워두면 Catalog에서 기본 Idle을 찾습니다."
    )]
    [SerializeField]
    private AnimationClip idleClipOverride;

    [SerializeField]
    private string defaultIdleVariation =
        "BL_03.quiet_stable_posture";

    [SerializeField]
    private bool randomizeIdleStartTime = true;

    [Header("전환")]

    [SerializeField]
    [Range(0.1f, 2f)]
    private float blendDuration = 0.7f;

    [Header("디버그")]

    [SerializeField]
    private bool printAnimationLog = true;

    private PlayableGraph playableGraph;
    private AnimationMixerPlayable mixer;

    private AnimationClipPlayable idlePlayable;
    private AnimationClipPlayable actionPlayable;

    private Coroutine actionRoutine;

    [SerializeField]
    private bool doNotInterruptActiveAction = true;

    public AudienceGender Gender =>
        gender;

    private void Reset()
    {
        targetAnimator =
            GetComponentInChildren<Animator>();
    }

    private void OnEnable()
    {
        CreateAnimationGraph();
    }

    private void OnDisable()
    {
        if (actionRoutine != null)
        {
            StopCoroutine(actionRoutine);
            actionRoutine = null;
        }

        DestroyAnimationGraph();
    }

    public bool PlayServerVariation(
        string variationId,
        float requestedDuration,
        float intensity)
    {
        // 새 서버 명령이 기존 동작의 블렌드 아웃을 끊으면 자세가 순간적으로
        // 튄다. 진행 중인 동작은 끝까지 재생하고 다음 주기 명령을 받는다.
        if (doNotInterruptActiveAction && actionRoutine != null)
            return true;

        if (!playableGraph.IsValid() ||
            !mixer.IsValid())
        {
            Debug.LogWarning(
                gameObject.name +
                ": 서버 애니메이션 그래프가 " +
                "준비되지 않았습니다.",
                this
            );

            return false;
        }

        if (catalog == null)
        {
            Debug.LogWarning(
                gameObject.name +
                ": AudienceAnimationCatalog가 " +
                "연결되지 않았습니다.",
                this
            );

            return false;
        }

        if (!catalog.TryGetClip(
                variationId,
                gender,
                out AnimationClip clip))
        {
            Debug.LogWarning(
                gameObject.name +
                ": Catalog에서 클립을 찾지 못했습니다." +
                "\nVariation ID: " +
                variationId +
                "\nGender: " +
                gender,
                this
            );

            return false;
        }

        if (actionRoutine != null)
        {
            StopCoroutine(actionRoutine);
            actionRoutine = null;
        }

        RemoveActionPlayable();

        actionRoutine =
            StartCoroutine(
                PlayActionRoutine(
                    clip,
                    variationId,
                    requestedDuration,
                    intensity
                )
            );

        return true;
    }

    private bool CreateAnimationGraph()
    {
        if (targetAnimator == null)
        {
            Debug.LogWarning(
                gameObject.name +
                ": Target Animator가 연결되지 않았습니다.",
                this
            );

            return false;
        }

        AnimationClip idleClip =
            GetIdleClip();

        if (idleClip == null)
        {
            Debug.LogWarning(
                gameObject.name +
                ": 기본 Idle Clip을 찾지 못했습니다.",
                this
            );

            return false;
        }

        DestroyAnimationGraph();

        playableGraph =
            PlayableGraph.Create(
                gameObject.name +
                "_ServerAudienceGraph"
            );

        playableGraph.SetTimeUpdateMode(
            DirectorUpdateMode.GameTime
        );

        AnimationPlayableOutput output =
            AnimationPlayableOutput.Create(
                playableGraph,
                "AudienceAnimation",
                targetAnimator
            );

        mixer =
            AnimationMixerPlayable.Create(
                playableGraph,
                2
            );

        idlePlayable =
            AnimationClipPlayable.Create(
                playableGraph,
                idleClip
            );

        idlePlayable.SetApplyFootIK(false);
        idlePlayable.SetApplyPlayableIK(false);

        if (
            randomizeIdleStartTime &&
            idleClip.length > 0.1f)
        {
            idlePlayable.SetTime(
                Random.Range(
                    0f,
                    idleClip.length
                )
            );
        }

        playableGraph.Connect(
            idlePlayable,
            0,
            mixer,
            0
        );

        mixer.SetInputWeight(0, 1f);
        mixer.SetInputWeight(1, 0f);

        output.SetSourcePlayable(mixer);
        playableGraph.Play();

        return true;
    }

    private AnimationClip GetIdleClip()
    {
        if (idleClipOverride != null)
            return idleClipOverride;

        if (catalog != null &&
            catalog.TryGetClip(
                defaultIdleVariation,
                gender,
                out AnimationClip catalogIdle))
        {
            return catalogIdle;
        }

        if (catalog != null &&
            catalog.TryGetClip(
                "BL_01.neutral_listening",
                gender,
                out AnimationClip fallbackIdle))
        {
            return fallbackIdle;
        }

        return null;
    }

    private IEnumerator PlayActionRoutine(
        AnimationClip clip,
        string variationId,
        float requestedDuration,
        float intensity)
    {
        actionPlayable =
            AnimationClipPlayable.Create(
                playableGraph,
                clip
            );

        actionPlayable.SetTime(0);
        actionPlayable.SetApplyFootIK(false);
        actionPlayable.SetApplyPlayableIK(false);

        float safeRequestedDuration =
            Mathf.Max(
                0.2f,
                requestedDuration
            );

        float playbackSpeed =
            clip.length /
            safeRequestedDuration;

        playbackSpeed =
            Mathf.Clamp(
                playbackSpeed,
                0.9f,
                1.1f
            );

        actionPlayable.SetSpeed(
            playbackSpeed
        );

        playableGraph.Connect(
            actionPlayable,
            0,
            mixer,
            1
        );

        mixer.SetInputWeight(1, 0f);

        float playableClipDuration =
            clip.length /
            playbackSpeed;

        // 자연스러운 속도로 재생하되,
        // 서버가 요청한 시간까지만 보여준다.
        float actualDuration =
            Mathf.Min(
                safeRequestedDuration,
                playableClipDuration
            );

        float transitionDuration =
            Mathf.Min(
                blendDuration,
                actualDuration * 0.35f
            );

        transitionDuration =
            Mathf.Max(
                0.05f,
                transitionDuration
            );

        float actionWeight =
            Mathf.Lerp(
                0.7f,
                1f,
                Mathf.Clamp01(intensity)
            );

        if (printAnimationLog)
        {
            Debug.Log(
                "[청중 애니메이션 재생]" +
                "\n청중: " +
                gameObject.name +
                "\nVariation ID: " +
                variationId +
                "\n클립: " +
                clip.name +
                "\n성별: " +
                gender +
                "\n재생 시간: " +
                actualDuration.ToString("F2") +
                "초" +
                "\n전환 시간: " +
                transitionDuration.ToString("F2") +
                "초"
            );
        }

        yield return BlendWeights(
            0f,
            actionWeight,
            transitionDuration
        );

        float holdDuration =
            Mathf.Max(
                0f,
                actualDuration -
                transitionDuration * 2f
            );

        if (holdDuration > 0f)
        {
            yield return new WaitForSeconds(
                holdDuration
            );
        }

        yield return BlendWeights(
            actionWeight,
            0f,
            transitionDuration
        );

        RemoveActionPlayable();
        actionRoutine = null;
    }

    private IEnumerator BlendWeights(
        float startWeight,
        float endWeight,
        float duration)
    {
        float elapsed = 0f;
        float safeDuration =
            Mathf.Max(0.01f, duration);

        while (elapsed < safeDuration)
        {
            elapsed += Time.deltaTime;

            float t =
                Mathf.Clamp01(
                    elapsed / safeDuration
                );

            // Smooth Step
            t =
                t * t *
                (3f - 2f * t);

            float actionWeight =
                Mathf.Lerp(
                    startWeight,
                    endWeight,
                    t
                );

            mixer.SetInputWeight(
                0,
                1f - actionWeight
            );

            mixer.SetInputWeight(
                1,
                actionWeight
            );

            yield return null;
        }

        mixer.SetInputWeight(
            0,
            1f - endWeight
        );

        mixer.SetInputWeight(
            1,
            endWeight
        );
    }

    private void RemoveActionPlayable()
    {
        if (!playableGraph.IsValid() ||
            !mixer.IsValid())
        {
            return;
        }

        mixer.SetInputWeight(0, 1f);
        mixer.SetInputWeight(1, 0f);

        if (actionPlayable.IsValid())
        {
            playableGraph.Disconnect(
                mixer,
                1
            );

            actionPlayable.Destroy();
        }
    }

    private void DestroyAnimationGraph()
    {
        if (playableGraph.IsValid())
            playableGraph.Destroy();
    }
}
