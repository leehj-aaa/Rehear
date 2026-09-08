using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

public class AudienceAnimationPlayer : MonoBehaviour
{
    [SerializeField] private Animator targetAnimator;
    [SerializeField] private AudienceGender gender = AudienceGender.Male;
    [SerializeField] private AudienceAnimationCatalog catalog;
    [SerializeField] private AnimationClip idleClipOverride;
    [SerializeField] private string defaultIdleVariation = "BL_03.quiet_stable_posture";
    [SerializeField] private bool randomizeIdleStartTime = true;
    [SerializeField, Range(.1f, 2f)] private float blendDuration = .7f;
    [SerializeField] private bool printAnimationLog = true;
    [SerializeField] private GameObject photoPhone;

    private sealed class Voice
    {
        public AnimationClipPlayable playable;
        public AnimationClip clip;
        public string variation;
        public int port;
        public float from;
    }
    private readonly List<Voice> voices = new List<Voice>();
    private PlayableGraph playableGraph;
    private AnimationMixerPlayable mixer;
    private Voice target;
    private float idleFrom, targetWeight, transitionElapsed, transitionDuration, remaining;
    private bool transitioning;
    public AudienceGender Gender => gender;
    public bool IsBusy => target != null || transitioning;

    private void Reset() => targetAnimator = GetComponentInChildren<Animator>();
    private void OnEnable() => CreateAnimationGraph();
    private void OnDisable() => DestroyAnimationGraph();
    private void Update() => Advance(Time.deltaTime);

    private bool CreateAnimationGraph()
    {
        DestroyAnimationGraph();
        if (!targetAnimator) targetAnimator = GetComponentInChildren<Animator>();
        var idle = idleClipOverride;
        if (!idle && catalog) catalog.TryGetClip(defaultIdleVariation, gender, out idle);
        if (!idle && catalog) catalog.TryGetClip("BL_01.neutral_listening", gender, out idle);
        if (!targetAnimator || !idle) return false;
        playableGraph = PlayableGraph.Create(name + "_ServerAudienceGraph");
        playableGraph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        mixer = AnimationMixerPlayable.Create(playableGraph, 1);
        var idlePlayable = AnimationClipPlayable.Create(playableGraph, idle);
        idlePlayable.SetApplyFootIK(false);
        idlePlayable.SetApplyPlayableIK(false);
        if (randomizeIdleStartTime) idlePlayable.SetTime(Random.Range(0, idle.length));
        playableGraph.Connect(idlePlayable, 0, mixer, 0);
        mixer.SetInputWeight(0, 1);
        var output = AnimationPlayableOutput.Create(playableGraph, "AudienceAnimation", targetAnimator);
        output.SetSourcePlayable(mixer);
        playableGraph.Play();
        return true;
    }

    public bool PlayServerVariation(string variationId, float requestedDuration, float intensity)
    {
        var seat = GetComponent<AudienceSeatAssignment>();
        if (seat && !seat.Allows(variationId)) return false;
        if (!isActiveAndEnabled || !playableGraph.IsValid() || !catalog ||
            !catalog.TryGetClip(variationId, gender, out var clip)) return false;
        // Repeated evaluations of the same gesture update its strength/time without rewinding it.
        var next = voices.Find(v => v.variation == variationId && v.clip == clip);
        if (next == null)
        {
            int port = 1;
            while (voices.Exists(v => v.port == port)) port++;
            if (port >= mixer.GetInputCount()) mixer.SetInputCount(port + 1);
            var playable = AnimationClipPlayable.Create(playableGraph, clip);
            playable.SetApplyFootIK(false);
            playable.SetApplyPlayableIK(false);
            playable.SetSpeed(1);
            playableGraph.Connect(playable, 0, mixer, port);
            mixer.SetInputWeight(port, 0);
            next = new Voice { playable = playable, clip = clip, variation = variationId, port = port };
            voices.Add(next);
        }
        remaining = Mathf.Max(.2f, requestedDuration);
        BeginTransition(next, Mathf.Lerp(.7f, 1, Mathf.Clamp01(intensity)));
        if (printAnimationLog) Debug.Log($"[청중 전환] {name}: {variationId}, 요청 {remaining:F1}s, 블렌드 {transitionDuration:F1}s", this);
        return true;
    }

    private void BeginTransition(Voice next, float weight)
    {
        // Snapshot ALL live weights, including an unfinished previous blend.
        // Never destroy the outgoing pose or insert an idle frame at command arrival.
        idleFrom = mixer.GetInputWeight(0);
        foreach (var voice in voices) voice.from = mixer.GetInputWeight(voice.port);
        target = next;
        targetWeight = next != null ? weight : 0;
        transitionElapsed = 0;
        transitionDuration = Mathf.Max(.1f, blendDuration);
        transitioning = true;
    }

    public void StopAction()
    {
        if (mixer.IsValid()) BeginTransition(null, 0);
        else SetPhotoPhoneVisible(false);
    }

    private void Advance(float delta)
    {
        if (!mixer.IsValid()) return;
        // One-shot clips hold their final pose rather than wrapping while fading out.
        foreach (var voice in voices)
        {
            double end = Mathf.Max(0, voice.clip.length - .001f);
            if (voice.playable.GetTime() + delta >= end)
            {
                voice.playable.SetTime(end);
                voice.playable.SetSpeed(0);
            }
        }
        if (transitioning)
        {
            transitionElapsed += delta;
            float t = Mathf.Clamp01(transitionElapsed / transitionDuration);
            t = t * t * (3 - 2 * t);
            mixer.SetInputWeight(0, Mathf.Lerp(idleFrom, 1 - targetWeight, t));
            foreach (var voice in voices)
                mixer.SetInputWeight(voice.port, Mathf.Lerp(voice.from, voice == target ? targetWeight : 0, t));
            if (transitionElapsed >= transitionDuration)
            {
                transitioning = false;
                for (int i = voices.Count - 1; i >= 0; i--)
                {
                    var voice = voices[i];
                    if (voice == target) continue;
                    playableGraph.Disconnect(mixer, voice.port);
                    voice.playable.Destroy();
                    voices.RemoveAt(i);
                }
            }
        }
        if (target != null)
        {
            remaining -= delta;
            if (remaining <= 0) BeginTransition(null, 0);
        }
        bool photoVisible = voices.Exists(v => v.variation == "ACT_02.photoslide" && mixer.GetInputWeight(v.port) > .05f);
        SetPhotoPhoneVisible(photoVisible);
    }

    private void SetPhotoPhoneVisible(bool visible)
    {
        if (photoPhone && photoPhone.activeSelf != visible) photoPhone.SetActive(visible);
    }
    private void DestroyAnimationGraph()
    {
        SetPhotoPhoneVisible(false);
        if (playableGraph.IsValid()) playableGraph.Destroy();
        voices.Clear(); target = null; transitioning = false;
    }
}
