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
    [SerializeField] private GameObject devicePhone;

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
    private AnimationClip pendingTypingClip;
    private string pendingTypingVariation;
    private float pendingTypingDuration, pendingTypingIntensity;
    private bool ownsSeatFacing;
    private bool questionTurn, questionPaused;
    public bool IsQuestionTurn => questionTurn;
    public bool IsAtBaseline => !IsBusy && !ownsSeatFacing;
    public const string QuestionGesture = "QS_01.raise_hand_question";
    public Transform TypingLookTarget
    {
        get
        {
            bool typing = pendingTypingClip || voices.Exists(v => IsTyping(v.variation) && mixer.IsValid() && mixer.GetInputWeight(v.port) > .05f);
            var assignment = GetComponent<AudienceSeatAssignment>();
            return typing && assignment && assignment.Seat && assignment.Seat.HasLaptop ? assignment.Seat.laptopAnchor : null;
        }
    }
    private static bool IsTyping(string variation) => variation == "ACT_01.laptoptyping";
    public float ConversationWeight
    {
        get
        {
            if (!mixer.IsValid()) return 0;
            float weight = 0;
            foreach (var voice in voices)
                if (AudienceSeatAssignment.IsSideConversation(voice.variation)) weight += mixer.GetInputWeight(voice.port);
            return Mathf.Clamp01(weight);
        }
    }
    public AudienceGender Gender => gender;
    public bool IsBusy => target != null || transitioning || pendingTypingClip;

    private void Reset() => targetAnimator = GetComponentInChildren<Animator>();
    private void OnEnable() => CreateAnimationGraph();
    private void OnDisable() => DestroyAnimationGraph();
    private void OnDestroy() => DestroyAnimationGraph();
    private void Update() { if (!questionPaused) Advance(Time.deltaTime); }

    public bool HasQuestionGesture => catalog && catalog.TryGetClip(QuestionGesture, gender, out var clip) && clip.length > 0;

    public void ReserveQuestionTurn()
    {
        questionTurn = true;
        StopAction();
    }

    public bool PlayQuestionGesture()
    {
        if (!questionTurn || !IsAtBaseline || !playableGraph.IsValid() || !catalog ||
            !catalog.TryGetClip(QuestionGesture, gender, out var clip)) return false;
        PlayClip(QuestionGesture, clip, clip.length, 1);
        return true;
    }

    public void SetQuestionPaused(bool paused)
    {
        questionPaused = paused;
        if (mixer.IsValid()) mixer.SetSpeed(paused ? 0 : 1);
    }

    public void ReleaseQuestionTurn()
    {
        SetQuestionPaused(false);
        questionTurn = false;
    }

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
        // Presentation reactions can interrupt each other; the explicit Q&A speaking turn is reserved.
        if (questionTurn) return false;
        var seat = GetComponent<AudienceSeatAssignment>();
        if (seat && !seat.Allows(variationId)) return false;
        if (AudienceSeatAssignment.IsSideConversation(variationId))
        {
            // Never trust a character ID or an incoming L/R suffix to choose the side.
            if (!seat || !seat.TryGetConversationVariation(out variationId)) return false;
        }
        if (!isActiveAndEnabled || !playableGraph.IsValid() || !catalog ||
            !catalog.TryGetClip(variationId, gender, out var clip)) return false;
        if(IsTyping(variationId))
        {
            if(!seat || !seat.Seat || !seat.Seat.HasLaptop || !seat.Seat.laptopAnchor) return false;
            ownsSeatFacing=true;
            if(target == null || !IsTyping(target.variation))
            {
                if(!pendingTypingClip) BeginTransition(null,0);
                pendingTypingClip=clip; pendingTypingVariation=variationId;
                pendingTypingDuration=requestedDuration; pendingTypingIntensity=intensity;
                return true;
            }
        }
        pendingTypingClip=null;
        PlayClip(variationId,clip,requestedDuration,intensity);
        return true;
    }

    private void PlayClip(string variationId, AnimationClip clip, float requestedDuration, float intensity)
    {
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
        pendingTypingClip=null;
        if (mixer.IsValid()) BeginTransition(null, 0);
        else SetPhotoPhoneVisible(false);
    }

    private void Advance(float delta)
    {
        if (!mixer.IsValid()) return;
        if (target != null && AudienceSeatAssignment.IsSideConversation(target.variation))
        {
            var assignment = GetComponent<AudienceSeatAssignment>();
            if (!assignment || !assignment.TryGetConversationVariation(out var direction) || direction != target.variation)
                StopAction();
        }
        UpdateSeatFacing(delta);
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
        float photoWeight = 0, deviceWeight = 0;
        foreach (var voice in voices)
        {
            if (voice.variation == "ACT_02.photoslide") photoWeight += mixer.GetInputWeight(voice.port);
            if (voice.variation == "ACT_03.devicechecking") deviceWeight += mixer.GetInputWeight(voice.port);
        }
        // Photo uses both hands; device checking holds the phone in the right hand.
        // During a crossfade show only the dominant action's grip, never two phones.
        SetPhotoPhoneVisible(photoWeight > .05f && photoWeight >= deviceWeight,
            deviceWeight > .05f && deviceWeight > photoWeight);
    }

    private void UpdateSeatFacing(float delta)
    {
        if(!ownsSeatFacing) return;
        var assignment=GetComponent<AudienceSeatAssignment>();
        var seat=assignment ? assignment.Seat : null;
        if(!seat) { pendingTypingClip=null; ownsSeatFacing=false; return; }
        if((pendingTypingClip || (target != null && IsTyping(target.variation))) && (!seat.HasLaptop || !seat.laptopAnchor))
        {
            pendingTypingClip=null;
            BeginTransition(null,0);
        }
        bool faceLaptop=pendingTypingClip || (target != null && IsTyping(target.variation));
        var pose=GetComponent<AudienceSeatedPose>();
        var correction=pose ? pose.facingCorrection : Quaternion.identity;
        var facing=seat.transform.rotation;
        if(faceLaptop)
        {
            var direction=seat.laptopAnchor.position-seat.transform.position;
            direction.y=0;
            if(direction.sqrMagnitude>.0001f) facing=Quaternion.LookRotation(direction,Vector3.up);
        }
        var desired=facing*correction;
        transform.rotation=Quaternion.Slerp(transform.rotation,desired,1f-Mathf.Exp(-6f*Mathf.Max(0,delta)));
        // Turn around the seated hip, not the FBX's offset root pivot.
        if(pose) transform.position=seat.transform.position-transform.rotation*Vector3.Scale(pose.localHip,transform.localScale);
        float angle=Quaternion.Angle(transform.rotation,desired);
        if(pendingTypingClip && angle<4f)
        {
            var clip=pendingTypingClip;
            pendingTypingClip=null;
            PlayClip(pendingTypingVariation,clip,pendingTypingDuration,pendingTypingIntensity);
        }
        if(!faceLaptop && angle<.1f) {transform.rotation=desired; ownsSeatFacing=false;}
    }

    private void SetPhotoPhoneVisible(bool visible, bool deviceVisible = false)
    {
        if (photoPhone && photoPhone.activeSelf != visible) photoPhone.SetActive(visible);
        if (devicePhone && devicePhone.activeSelf != deviceVisible) devicePhone.SetActive(deviceVisible);
    }
    private void DestroyAnimationGraph()
    {
        SetPhotoPhoneVisible(false);
        if (playableGraph.IsValid()) playableGraph.Destroy();
        voices.Clear(); target = null; transitioning = false;
        pendingTypingClip=null; ownsSeatFacing=false;
        questionTurn = questionPaused = false;
    }
}
