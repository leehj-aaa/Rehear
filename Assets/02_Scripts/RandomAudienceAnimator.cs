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
        float playbackSpeed)
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

        yield return BlendWeights(0f, 1f);

        float totalDuration = Mathf.Max(
            0.1f,
            clip.length / Mathf.Max(0.01f, playbackSpeed));

        float holdDuration = Mathf.Max(
            0f,
            totalDuration - blendDuration * 2f);

        if (holdDuration > 0f)
            yield return new WaitForSeconds(holdDuration);

        yield return BlendWeights(1f, 0f);
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
