using System.Collections;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

public class RandomAudienceAnimator : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Animator targetAnimator;
    [SerializeField] private AnimationClip idleClip;
    [SerializeField] private AnimationClip[] reactionClips;

    [Header("Timing")]
    [SerializeField] private float minInitialDelay = 1f;
    [SerializeField] private float maxInitialDelay = 5f;
    [SerializeField] private float minInterval = 4f;
    [SerializeField] private float maxInterval = 9f;
    [SerializeField, Range(0.05f, 2f)] private float blendDuration = 0.4f;

    [Header("Variation")]
    [SerializeField, Range(0.5f, 1f)] private float reactionChance = 0.75f;
    [SerializeField] private float minPlaybackSpeed = 0.92f;
    [SerializeField] private float maxPlaybackSpeed = 1.08f;

    private PlayableGraph playableGraph;
    private AnimationMixerPlayable mixer;
    private AnimationClipPlayable idlePlayable;
    private AnimationClipPlayable reactionPlayable;
    private Coroutine randomRoutine;
    private Coroutine serverCommandRoutine;
    private int previousClipIndex = -1;

    private void Reset()
    {
        targetAnimator = GetComponentInChildren<Animator>();
    }

    private void OnEnable()
    {
        if (!CreateAnimationGraph())
            return;

        randomRoutine = StartCoroutine(RandomReactionRoutine());
    }

    private void OnDisable()
    {
        if (randomRoutine != null)
        {
            StopCoroutine(randomRoutine);
            randomRoutine = null;
        }

        if (serverCommandRoutine != null)
        {
            StopCoroutine(serverCommandRoutine);
            serverCommandRoutine = null;
        }

        DestroyAnimationGraph();
    }

    private bool CreateAnimationGraph()
    {
        if (targetAnimator == null || idleClip == null)
        {
            Debug.LogWarning(
                gameObject.name +
                ": Target Animator 또는 Idle Clip이 연결되지 않았습니다.",
                this);
            return false;
        }

        DestroyAnimationGraph();

        playableGraph = PlayableGraph.Create(
            gameObject.name + "_AudienceBlendGraph");

        playableGraph.SetTimeUpdateMode(
            DirectorUpdateMode.GameTime);

        AnimationPlayableOutput output =
            AnimationPlayableOutput.Create(
                playableGraph,
                "AudienceAnimation",
                targetAnimator);

        mixer = AnimationMixerPlayable.Create(
            playableGraph,
            2);

        idlePlayable = AnimationClipPlayable.Create(
            playableGraph,
            idleClip);

        idlePlayable.SetApplyFootIK(false);
        idlePlayable.SetApplyPlayableIK(false);

        playableGraph.Connect(
            idlePlayable,
            0,
            mixer,
            0);

        mixer.SetInputWeight(0, 1f);
        mixer.SetInputWeight(1, 0f);

        output.SetSourcePlayable(mixer);
        playableGraph.Play();
        return true;
    }

    private IEnumerator RandomReactionRoutine()
    {
        yield return new WaitForSeconds(
            Random.Range(minInitialDelay, maxInitialDelay));

        while (enabled)
        {
            if (Random.value <= reactionChance)
            {
                AnimationClip selectedClip = GetRandomClip();

                if (selectedClip != null)
                {
                    float playbackSpeed = Random.Range(
                        minPlaybackSpeed,
                        maxPlaybackSpeed);

                    yield return PlayReactionRoutine(
                        selectedClip,
                        playbackSpeed);
                }
            }

            yield return new WaitForSeconds(
                Random.Range(minInterval, maxInterval));
        }
    }

    private IEnumerator PlayReactionRoutine(
    AnimationClip clip,
    float playbackSpeed,
    float maximumWeight = 1f)
    {
        RemoveReactionPlayable();

        reactionPlayable = AnimationClipPlayable.Create(
            playableGraph,
            clip);

        reactionPlayable.SetTime(0);
        reactionPlayable.SetSpeed(
            Mathf.Max(0.01f, playbackSpeed));
        reactionPlayable.SetApplyFootIK(false);
        reactionPlayable.SetApplyPlayableIK(false);

        playableGraph.Connect(
            reactionPlayable,
            0,
            mixer,
            1);

        mixer.SetInputWeight(1, 0f);

        yield return BlendWeights(
            0f,
            maximumWeight
        );

        float totalDuration = Mathf.Max(
            0.1f,
            clip.length / Mathf.Max(0.01f, playbackSpeed));

        float holdDuration = Mathf.Max(
            0f,
            totalDuration - blendDuration * 2f);

        if (holdDuration > 0f)
            yield return new WaitForSeconds(holdDuration);

        yield return BlendWeights(
            0f,
            maximumWeight
        );
        RemoveReactionPlayable();
    }

    private IEnumerator BlendWeights(
        float reactionStart,
        float reactionEnd)
    {
        float elapsed = 0f;
        float safeDuration = Mathf.Max(0.01f, blendDuration);

        while (elapsed < safeDuration)
        {
            elapsed += Time.deltaTime;

            float t = Mathf.Clamp01(elapsed / safeDuration);
            t = t * t * (3f - 2f * t);

            float reactionWeight = Mathf.Lerp(
                reactionStart,
                reactionEnd,
                t);

            mixer.SetInputWeight(0, 1f - reactionWeight);
            mixer.SetInputWeight(1, reactionWeight);

            yield return null;
        }

        mixer.SetInputWeight(0, 1f - reactionEnd);
        mixer.SetInputWeight(1, reactionEnd);
    }
    public bool PlayServerVariation(
    string variationId,
    float requestedDuration,
    float intensity)
{
    if (!playableGraph.IsValid())
    {
        Debug.LogWarning(
            gameObject.name +
            ": 애니메이션 그래프가 준비되지 않았습니다.",
            this
        );

        return false;
    }

    AnimationClip clip =
        FindClipForVariation(
            variationId
        );

    if (clip == null)
    {
        Debug.LogWarning(
            gameObject.name +
            ": 서버 Variation에 대응하는 클립이 없습니다." +
            "\nVariation ID: " +
            variationId,
            this
        );

        return false;
    }

    if (randomRoutine != null)
    {
        StopCoroutine(randomRoutine);
        randomRoutine = null;
    }

    if (serverCommandRoutine != null)
    {
        StopCoroutine(serverCommandRoutine);
        serverCommandRoutine = null;
    }

    RemoveReactionPlayable();

    serverCommandRoutine =
        StartCoroutine(
            PlayServerVariationRoutine(
                clip,
                requestedDuration,
                intensity
            )
        );

    return true;
}

private IEnumerator PlayServerVariationRoutine(
    AnimationClip clip,
    float requestedDuration,
    float intensity)
{
    float safeDuration =
        Mathf.Max(
            0.2f,
            requestedDuration
        );

    float playbackSpeed =
        clip.length /
        safeDuration;

    playbackSpeed =
        Mathf.Clamp(
            playbackSpeed,
            0.5f,
            2f
        );

    float maximumWeight =
        Mathf.Lerp(
            0.55f,
            1f,
            Mathf.Clamp01(intensity)
        );

    Debug.Log(
        "[청중 애니메이션 재생]" +
        "\n청중: " + gameObject.name +
        "\n클립: " + clip.name +
        "\n요청 시간: " +
        safeDuration.ToString("F2") +
        "초" +
        "\n재생 속도: " +
        playbackSpeed.ToString("F2") +
        "\n가중치: " +
        maximumWeight.ToString("F2")
    );

        yield return PlayReactionRoutine(
            clip,
            playbackSpeed,
            maximumWeight
        );

        serverCommandRoutine = null;

        if (enabled &&
            gameObject.activeInHierarchy)
        {
            randomRoutine =
                StartCoroutine(
                    RandomReactionRoutine()
                );
        }
    }

    private AnimationClip FindClipForVariation(
        string variationId)
    {
        if (string.IsNullOrWhiteSpace(
                variationId))
        {
            return null;
        }

        if (reactionClips == null ||
            reactionClips.Length == 0)
        {
            return null;
        }

        string expectedName =
            NormalizeVariationName(
                variationId
            );

        foreach (AnimationClip clip
                in reactionClips)
        {
            if (clip == null)
                continue;

            string clipName =
                NormalizeVariationName(
                    clip.name
                );

            // 예:
            // BL_01.neutral_listening
            // → bl_01_neutral_listening
            //
            // BL_01_neutral_listening_M
            // → bl_01_neutral_listening_m
            if (
                clipName == expectedName ||
                clipName.StartsWith(
                    expectedName + "_"
                ))
            {
                return clip;
            }
        }

        return null;
    }

    private string NormalizeVariationName(
        string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        return value
            .Trim()
            .ToLowerInvariant()
            .Replace(".", "_")
            .Replace(" ", "_")
            .Replace("-", "_");
    }

    private AnimationClip GetRandomClip()
    {
        if (reactionClips == null || reactionClips.Length == 0)
            return null;

        if (reactionClips.Length == 1)
        {
            previousClipIndex = 0;
            return reactionClips[0];
        }

        int selectedIndex = previousClipIndex;

        while (selectedIndex == previousClipIndex)
            selectedIndex = Random.Range(0, reactionClips.Length);

        previousClipIndex = selectedIndex;
        return reactionClips[selectedIndex];
    }

    private void RemoveReactionPlayable()
    {
        if (!playableGraph.IsValid() || !mixer.IsValid())
            return;

        mixer.SetInputWeight(0, 1f);
        mixer.SetInputWeight(1, 0f);

        if (reactionPlayable.IsValid())
        {
            playableGraph.Disconnect(mixer, 1);
            reactionPlayable.Destroy();
        }
    }

    private void DestroyAnimationGraph()
    {
        if (playableGraph.IsValid())
            playableGraph.Destroy();
    }
}
