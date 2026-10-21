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
    [SerializeField, Range(.3f, 2.5f)] private float blendDuration = 1.4f;
    [SerializeField] private bool printAnimationLog = true;
    [SerializeField] private GameObject photoPhone;
    [SerializeField] private GameObject devicePhone;
    [SerializeField] private AudioClip photoShutter;
    [SerializeField, Range(0,1)] private float photoShutterVolume=.10f;
    [SerializeField, Range(0,1)] private float photoShutterNormalizedTime=.30f;
    [SerializeField, Range(.2f, 1.5f)] private float photoTurnSmoothTime=.55f;
    [SerializeField, Range(.2f, 2f)] private float returnTurnSmoothTime=.75f;
    [SerializeField, Range(15f, 120f)] private float photoTurnMaxSpeed=55f;
    [SerializeField, Range(15f, 120f)] private float returnTurnMaxSpeed=40f;
    [SerializeField, Range(.15f, .65f)] private float photoRootTurnFraction=.35f;
    private AudioSource photoAudio;
#if UNITY_EDITOR
    public static System.Action<AudienceAnimationPlayer,AudioClip,float> PreviewPhotoShutter;
#endif

    private sealed class Voice
    {
        public AnimationClipPlayable playable;
        public AnimationClip clip;
        public string variation;
        public int port;
        public float from;
        public bool shutterPlayed;
    }
    private readonly List<Voice> voices = new List<Voice>();
    private PlayableGraph playableGraph;
    private AnimationMixerPlayable mixer;
    private Voice target;
    // Keep the evaluated listening pose alive underneath temporary actions.
    private Voice core;
    private float coreWeight = 1f;
    private float idleFrom, targetWeight, transitionElapsed, transitionDuration, remaining;
    private bool transitioning;
    private AnimationClip pendingTypingClip;
    private string pendingTypingVariation;
    private float pendingTypingDuration, pendingTypingIntensity;
    private AnimationClip pendingPhotoClip;
    private string pendingPhotoVariation;
    private float pendingPhotoDuration, pendingPhotoIntensity;
    private bool ownsSeatFacing;
    private float seatFacingYawVelocity;
    private float seatFacingTurnVariation;
    private float photoTorsoYaw, photoTorsoYawTarget, photoTorsoYawVelocity;
    private Transform photoFacingSpine;
    private bool questionTurn, questionPaused;
    private bool questionGestureStarted, questionSpeechReady;
    private Transform[] questionHands;
    private readonly float[] questionHandStart = new float[2], questionHandPeak = new float[2];
    private AudienceAnimationPlayer conversationPartner;
    public bool IsQuestionTurn => questionTurn;
    public bool IsAtBaseline => !IsBusy && !ownsSeatFacing;
    public bool IsReadyForQuestionSpeech => questionTurn && questionGestureStarted &&
        (questionSpeechReady || IsAtBaseline);
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
    private static bool IsPhoto(string variation) => variation == "ACT_02.photoslide";
    public Transform DeviceLookTarget => devicePhone && devicePhone.activeInHierarchy ? devicePhone.transform : null;
    public bool IsTakingPhoto => photoPhone && photoPhone.activeInHierarchy;
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
    public float HeadNodWeight
    {
        get
        {
            if (!mixer.IsValid()) return 0;
            float weight = 0;
            foreach (var voice in voices)
                if (IsHeadNod(voice.variation)) weight += mixer.GetInputWeight(voice.port);
            return Mathf.Clamp01(weight);
        }
    }
    private static bool IsHeadNod(string variation) =>
        !string.IsNullOrEmpty(variation) &&
        variation.IndexOf("nod", System.StringComparison.OrdinalIgnoreCase) >= 0;
    public AudienceGender Gender => gender;
    public bool IsBusy => target != null || transitioning || pendingTypingClip || pendingPhotoClip;

    private void Reset() => targetAnimator = GetComponentInChildren<Animator>();
    private void OnEnable()
    {
        EnsureSeatFacingTiming();
        seatFacingYawVelocity=0;
        CreateAnimationGraph();
    }
    private void EnsureSeatFacingTiming()
    {
        if(seatFacingTurnVariation>0) return;
        // Editor previews build playable graphs manually, so OnEnable is not
        // guaranteed to run. Keep the per-audience timing deterministic there.
        uint hash=(uint)Animator.StringToHash(name);
        seatFacingTurnVariation=Mathf.Lerp(.9f,1.12f,(hash&1023)/1023f);
    }
    private void OnDisable() => DestroyAnimationGraph();
    private void OnDestroy() => DestroyAnimationGraph();
    private void Update() { if (!questionPaused) Advance(Time.deltaTime); }
    private void LateUpdate() { if (!questionPaused) ApplyPropPoses(); }

    private AudienceTypingPose typingPose;
    private Transform seatAdjustPelvis, seatAdjustSpine, seatAdjustNeck;
    private sealed class SeatAdjustArm
    {
        public Transform upper, lower, hand;
        public Vector3 position;
        public Quaternion rotation;
    }
    private SeatAdjustArm[] seatAdjustArms;

    // SeatAdjust was captured on a different chair. Preserve its lift and lateral
    // shuffle, but keep the pelvis and upper back in front of our fixed backrest.
    private void ApplySeatAdjustPose()
    {
        if (!mixer.IsValid()) return;
        float weight = 0;
        foreach (var voice in voices)
            if (voice.variation == "ACT_05.seatadjust") weight += mixer.GetInputWeight(voice.port);
        if (weight <= 0) return;
        var pose = GetComponent<AudienceSeatedPose>();
        if (!pose) return;
        if (!seatAdjustPelvis || !seatAdjustSpine || !seatAdjustNeck)
        {
            seatAdjustArms = new[] { new SeatAdjustArm(), new SeatAdjustArm() };
            foreach (var bone in GetComponentsInChildren<Transform>())
            {
                if (bone.name == "pelvis") seatAdjustPelvis = bone;
                else if (bone.name == "spine_01") seatAdjustSpine = bone;
                else if (bone.name == "neck_01") seatAdjustNeck = bone;
                for (int i = 0; i < 2; i++)
                {
                    string side = i == 0 ? "l" : "r";
                    if (bone.name == "upperarm_" + side) seatAdjustArms[i].upper = bone;
                    else if (bone.name == "lowerarm_" + side) seatAdjustArms[i].lower = bone;
                    else if (bone.name == "hand_" + side) seatAdjustArms[i].hand = bone;
                }
            }
        }
        if (!seatAdjustPelvis || !seatAdjustSpine || !seatAdjustNeck) return;
        foreach (var arm in seatAdjustArms)
            if (arm.hand) { arm.position = arm.hand.position; arm.rotation = arm.hand.rotation; }
        var hip = transform.InverseTransformPoint(seatAdjustPelvis.position);
        hip.z = Mathf.Lerp(hip.z, Mathf.Max(hip.z, pose.localHip.z), Mathf.Clamp01(weight));
        seatAdjustPelvis.position = transform.TransformPoint(hip);
        var torso = seatAdjustNeck.position - seatAdjustSpine.position;
        float pitch = Mathf.Atan2(Vector3.Dot(torso, transform.forward),
            Vector3.Dot(torso, transform.up)) * Mathf.Rad2Deg;
        float correction = Mathf.Max(0, -4f - pitch) * Mathf.Clamp01(weight);
        seatAdjustSpine.rotation = Quaternion.AngleAxis(correction, transform.right) * seatAdjustSpine.rotation;
        // Moving the torso must not push the authored hands through the thighs.
        foreach (var arm in seatAdjustArms)
        {
            if (!arm.upper || !arm.lower || !arm.hand) continue;
            var shoulder = arm.upper.position;
            float a = Vector3.Distance(shoulder, arm.lower.position);
            float b = Vector3.Distance(arm.lower.position, arm.hand.position);
            var delta = arm.position - shoulder;
            if (delta.sqrMagnitude < .000001f || a < .001f || b < .001f) continue;
            var direction = delta.normalized;
            float distance = Mathf.Clamp(delta.magnitude, Mathf.Abs(a - b) + .001f, (a + b) * .999f);
            var bend = Vector3.ProjectOnPlane(arm.lower.position - shoulder, direction);
            if (bend.sqrMagnitude < .000001f) bend = Vector3.ProjectOnPlane(-transform.up, direction);
            float along = (a * a - b * b + distance * distance) / (2 * distance);
            var elbow = shoulder + direction * along + bend.normalized * Mathf.Sqrt(Mathf.Max(0, a * a - along * along));
            arm.upper.rotation = Quaternion.FromToRotation(arm.lower.position - shoulder, elbow - shoulder) * arm.upper.rotation;
            arm.lower.rotation = Quaternion.FromToRotation(arm.hand.position - arm.lower.position,
                shoulder + direction * distance - arm.lower.position) * arm.lower.rotation;
            arm.hand.rotation = arm.rotation;
        }
    }

    public void ApplyPropPoses()
    {
        UpdateQuestionSpeechCue();
        ApplySeatAdjustPose();
        ApplyPhotoFacingPose();
        ApplyDeviceGrip();
        UpdatePhotoShutter();
        var assignment=GetComponent<AudienceSeatAssignment>();
        if(!mixer.IsValid() || !assignment || !assignment.Seat || !assignment.Seat.HasLaptop || !assignment.Seat.laptopAnchor) return;
        float weight=0;
        foreach(var voice in voices) if(IsTyping(voice.variation)) weight+=mixer.GetInputWeight(voice.port);
        if(weight<=0) return;
        if(typingPose==null)typingPose=new AudienceTypingPose(transform);
        typingPose.Apply(assignment.Seat.laptopAnchor,weight);
    }

    private void ApplyPhotoFacingPose()
    {
        if(Mathf.Abs(photoTorsoYaw)<.001f) return;
        if(!photoFacingSpine)
            foreach(var bone in GetComponentsInChildren<Transform>(true))
                if(bone.name=="spine_01") { photoFacingSpine=bone; break; }
        if(photoFacingSpine)
            photoFacingSpine.rotation=Quaternion.AngleAxis(photoTorsoYaw,Vector3.up)*photoFacingSpine.rotation;
    }

    private Transform[] gripJoints;
    private void UpdatePhotoShutter()
    {
        if(!IsTakingPhoto || !mixer.IsValid() || target==null || target.variation!="ACT_02.photoslide")return;
        // One shutter at the held shooting pose, never during entry/exit or per frame.
        if(target.shutterPlayed || target.playable.GetTime()<target.clip.length*photoShutterNormalizedTime || mixer.GetInputWeight(target.port)<.5f)return;
        target.shutterPlayed=true;
        if(!photoShutter)return;
#if UNITY_EDITOR
        if(!Application.isPlaying){PreviewPhotoShutter?.Invoke(this,photoShutter,photoShutterVolume);return;}
#endif
        if(!photoAudio){
            photoAudio=gameObject.AddComponent<AudioSource>();
            photoAudio.playOnAwake=false;photoAudio.loop=false;photoAudio.spatialBlend=1;
            photoAudio.minDistance=1;photoAudio.maxDistance=12;
            photoAudio.rolloffMode=AudioRolloffMode.Linear;
        }
        photoAudio.PlayOneShot(photoShutter,photoShutterVolume);
    }
    // Apply after animation evaluation, including in the manual editor preview.
    // The source clip leaves the distal fingers straight; curl them round the
    // handset while preserving its animated wrist and palm pose.
    public void ApplyDeviceGrip()
    {
        if (!DeviceLookTarget || !mixer.IsValid()) return;
        float weight = 0;
        foreach (var voice in voices)
            if (voice.variation == "ACT_03.devicechecking") weight += mixer.GetInputWeight(voice.port);
        // Rotate the wrist together with the prop so the palm never separates
        // from it. The handset's physical top is its local -Y axis.
        var hand = devicePhone.transform.parent;
        var upright = Quaternion.LookRotation(devicePhone.transform.forward, -transform.up);
        var correction = upright * Quaternion.Inverse(devicePhone.transform.rotation);
        hand.rotation = Quaternion.RotateTowards(hand.rotation, correction * hand.rotation, 25f * Mathf.Clamp01(weight));
        if (gripJoints == null)
        {
            var found = new List<Transform>();
            foreach (var bone in GetComponentsInChildren<Transform>())
                if (bone.name.EndsWith("_02_r") || bone.name.EndsWith("_03_r"))
                    if (bone.name.StartsWith("index_") || bone.name.StartsWith("middle_") ||
                        bone.name.StartsWith("ring_") || bone.name.StartsWith("pinky_")) found.Add(bone);
            gripJoints = found.ToArray();
        }
        foreach (var joint in gripJoints)
        {
            if (!joint || joint.childCount == 0) continue;
            var direction = joint.GetChild(0).position - joint.position;
            var axis = Vector3.Cross(direction, devicePhone.transform.forward);
            if (axis.sqrMagnitude < 0.000001f) continue;
            float curl = joint.name.Contains("_02_") ? 45f : 35f;
            joint.rotation = Quaternion.AngleAxis(curl * Mathf.Clamp01(weight), axis.normalized) * joint.rotation;
        }
    }

    public bool HasQuestionGesture => catalog && catalog.TryGetClip(QuestionGesture, gender, out var clip) && clip.length > 0;

    public void ReserveQuestionTurn()
    {
        questionTurn = true;
        questionGestureStarted = questionSpeechReady = false;
        StopAction();
    }

    public bool PlayQuestionGesture()
    {
        if (!questionTurn || !IsAtBaseline || !playableGraph.IsValid() || !catalog ||
            !catalog.TryGetClip(QuestionGesture, gender, out var clip)) return false;
        questionHands = new Transform[2];
        foreach (var bone in GetComponentsInChildren<Transform>())
        {
            if (bone.name == "hand_l") questionHands[0] = bone;
            if (bone.name == "hand_r") questionHands[1] = bone;
        }
        for (int i = 0; i < 2; i++)
            questionHandStart[i] = questionHandPeak[i] = questionHands[i] ?
                Vector3.Dot(questionHands[i].position - transform.position, transform.up) : 0;
        questionSpeechReady = false;
        questionGestureStarted = true;
        PlayClip(QuestionGesture, clip, clip.length, 1);
        return true;
    }

    // Read the evaluated pose: begin speaking as the raised hand starts descending,
    // independently of the male/female clip length and its final baseline blend.
    private void UpdateQuestionSpeechCue()
    {
        if (!questionTurn || !questionGestureStarted || questionSpeechReady || questionPaused) return;
        for (int i = 0; i < 2; i++)
        {
            if (!questionHands[i]) continue;
            float height = Vector3.Dot(questionHands[i].position - transform.position, transform.up);
            questionHandPeak[i] = Mathf.Max(questionHandPeak[i], height);
            if (questionHandPeak[i] - questionHandStart[i] >= .15f &&
                questionHandPeak[i] - height >= .025f)
                questionSpeechReady = true;
        }
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
        questionGestureStarted = questionSpeechReady = false;
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
            return TryStartConversationPair(seat, requestedDuration, intensity);
        }
        if (!isActiveAndEnabled || !playableGraph.IsValid() || !catalog ||
            !catalog.TryResolveClip(variationId, gender, out var clip, out var canonicalId)) return false;
        // The wire format contains aliases such as device_checking/laptop_typing.
        // Props, seat-facing and interruption deduplication must use the resolved ID.
        variationId = canonicalId;
        ReleaseConversationPartner();
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
        if(IsPhoto(variationId))
        {
            if(!seat || !seat.Seat) return false;
            ownsSeatFacing=true;
            if(target == null || !IsPhoto(target.variation))
            {
                if(!pendingPhotoClip) BeginTransition(core,core != null ? coreWeight : 0);
                pendingPhotoClip=clip; pendingPhotoVariation=variationId;
                pendingPhotoDuration=requestedDuration; pendingPhotoIntensity=intensity;
                return true;
            }
        }
        pendingTypingClip=null; pendingPhotoClip=null;
        PlayClip(variationId,clip,requestedDuration,intensity);
        return true;
    }

    private bool TryStartConversationPair(AudienceSeatAssignment seat, float duration, float intensity)
    {
        if(!seat || !seat.TryGetConversationVariation(out var ownVariation))return false;
        var otherSeat=seat.Seat.conversationPartner.Occupant;
        var other=otherSeat.GetComponent<AudienceAnimationPlayer>();
        // Validate both participants before changing either pose. A Q&A turn wins.
        if(!isActiveAndEnabled || !playableGraph.IsValid() || !catalog ||
           !other || !other.isActiveAndEnabled || other.questionTurn ||
           !other.playableGraph.IsValid() || !other.catalog ||
           !otherSeat.TryGetConversationVariation(out var otherVariation) ||
           !catalog.TryResolveClip(ownVariation,gender,out var ownClip,out var ownId) ||
           !other.catalog.TryResolveClip(otherVariation,other.gender,out var otherClip,out var otherId))return false;
        if(conversationPartner!=other)ReleaseConversationPartner();
        if(other.conversationPartner!=this)other.ReleaseConversationPartner();
        conversationPartner=other;other.conversationPartner=this;
        pendingTypingClip=null;other.pendingTypingClip=null;
        PlayClip(ownId,ownClip,duration,intensity);
        other.PlayClip(otherId,otherClip,duration,intensity);
        return true;
    }

    private void ReleaseConversationPartner()
    {
        var other=conversationPartner;
        conversationPartner=null;
        if(!other || other.conversationPartner!=this)return;
        other.conversationPartner=null;
        if(other.mixer.IsValid() && other.target!=null && AudienceSeatAssignment.IsSideConversation(other.target.variation)) {
            other.remaining=float.PositiveInfinity;
            other.BeginTransition(other.core,other.core!=null?other.coreWeight:0);
        }
    }

    private void PlayClip(string variationId, AnimationClip clip, float requestedDuration, float intensity)
    {
        // Repeated evaluations of the same gesture update its strength/time without rewinding it.
        // A completed one-shot needs a NEW input to crossfade into, never a time reset
        // on an input which is still contributing to the visible pose.
        bool isCore = !variationId.StartsWith("ACT_", System.StringComparison.Ordinal) && variationId != QuestionGesture;
        var next = voices.Find(v => v.variation == variationId && v.clip == clip &&
            (clip.isLooping || v.playable.GetTime() < clip.length - .01f));
        if (next == null)
        {
            int port = 1;
            while (voices.Exists(v => v.port == port)) port++;
            if (port >= mixer.GetInputCount()) mixer.SetInputCount(port + 1);
            var playable = AnimationClipPlayable.Create(playableGraph, clip);
            playable.SetApplyFootIK(false);
            playable.SetApplyPlayableIK(false);
            playable.SetSpeed(1);
            // Enter looping listening clips close to their authored neutral start.
            // Starting at an arbitrary point can crossfade into a completely different
            // limb pose and produces a visible pop. A small deterministic offset keeps
            // listeners from moving in lockstep without skipping into mid-gesture.
            if (isCore && clip.isLooping && randomizeIdleStartTime && !variationId.StartsWith("AP_", System.StringComparison.Ordinal))
            {
                uint phaseHash = (uint)Animator.StringToHash(name + "|" + variationId);
                float normalizedOffset = Mathf.Lerp(0f, .08f, (phaseHash & 1023) / 1023f);
                playable.SetTime(clip.length * normalizedOffset);
            }
            playableGraph.Connect(playable, 0, mixer, port);
            mixer.SetInputWeight(port, 0);
            next = new Voice { playable = playable, clip = clip, variation = variationId, port = port };
            voices.Add(next);
        }
        float weight = Mathf.Clamp01(intensity);
        if (isCore) { core = next; coreWeight = weight; }
        remaining = isCore ? float.PositiveInfinity : Mathf.Max(.2f, requestedDuration);
        // Same target refreshes must not restart the easing curve every evaluation.
        if (target != next || !Mathf.Approximately(targetWeight, weight))
            BeginTransition(next, weight);
        if (printAnimationLog) Debug.Log($"[청중 전환] {name}: {variationId}, 요청 {remaining:F1}s, 블렌드 {transitionDuration:F1}s", this);
    }

    private void BeginTransition(Voice next, float weight)
    {
        EnsureSeatFacingTiming();
        // Snapshot ALL live weights, including an unfinished previous blend.
        // Never destroy the outgoing pose or insert an idle frame at command arrival.
        idleFrom = mixer.GetInputWeight(0);
        foreach (var voice in voices) voice.from = mixer.GetInputWeight(voice.port);
        target = next;
        targetWeight = next != null ? weight : 0;
        transitionElapsed = 0;
        transitionDuration = Mathf.Max(.1f, blendDuration*seatFacingTurnVariation);
        transitioning = true;
    }

    public void StopAction()
    {
        ReleaseConversationPartner();
        pendingTypingClip=null; pendingPhotoClip=null;
        core = null;
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
            if (voice.clip.isLooping) continue;
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
            // Cosine easing has gentler mid-blend acceleration than smootherstep.
            // This avoids the visually abrupt weight hand-off around 50 percent.
            t = .5f - .5f * Mathf.Cos(Mathf.PI * t);
            float backgroundWeight = core != null && target != core ? (1 - targetWeight) * coreWeight : 0;
            mixer.SetInputWeight(0, Mathf.Lerp(idleFrom, 1 - targetWeight - backgroundWeight, t));
            foreach (var voice in voices)
                mixer.SetInputWeight(voice.port, Mathf.Lerp(voice.from,
                    voice == target ? targetWeight : voice == core ? backgroundWeight : 0, t));
            if (transitionElapsed >= transitionDuration)
            {
                transitioning = false;
                for (int i = voices.Count - 1; i >= 0; i--)
                {
                    var voice = voices[i];
                    if (voice == target || voice == core) continue;
                    playableGraph.Disconnect(mixer, voice.port);
                    voice.playable.Destroy();
                    voices.RemoveAt(i);
                }
            }
        }
        if (target != null)
        {
            remaining -= delta;
            if (remaining <= 0)
            {
                ReleaseConversationPartner();
                remaining = float.PositiveInfinity;
                BeginTransition(core, core != null ? coreWeight : 0);
            }
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
        EnsureSeatFacingTiming();
        var assignment=GetComponent<AudienceSeatAssignment>();
        var seat=assignment ? assignment.Seat : null;
        if(!seat) { pendingTypingClip=null; pendingPhotoClip=null; ownsSeatFacing=false; return; }
        if((pendingTypingClip || (target != null && IsTyping(target.variation))) && (!seat.HasLaptop || !seat.laptopAnchor))
        {
            pendingTypingClip=null;
            BeginTransition(null,0);
        }
        bool faceLaptop=pendingTypingClip || (target != null && IsTyping(target.variation));
        float photoWeight=0;
        if(mixer.IsValid())
            foreach(var voice in voices)
                if(IsPhoto(voice.variation)) photoWeight+=mixer.GetInputWeight(voice.port);
        // Keep the root facing the screen until the outgoing photo pose has
        // completely faded. This prevents the torso blend and root return from
        // pulling in opposite directions.
        bool faceSlide=pendingPhotoClip || (target != null && IsPhoto(target.variation)) || photoWeight>.01f;
        var pose=GetComponent<AudienceSeatedPose>();
        var correction=pose ? pose.facingCorrection : Quaternion.identity;
        var facing=seat.transform.rotation;
        if(faceLaptop)
        {
            var direction=seat.laptopAnchor.position-seat.transform.position;
            direction.y=0;
            if(direction.sqrMagnitude>.0001f) facing=Quaternion.LookRotation(direction,Vector3.up);
        }
        else if(faceSlide)
        {
            var gaze=GetComponent<AudienceGazeController>();
            var slide=gaze ? gaze.SlideTarget : null;
            if(slide)
            {
                var direction=slide.position-seat.transform.position;
                direction.y=0;
                if(direction.sqrMagnitude>.0001f) facing=Quaternion.LookRotation(direction,Vector3.up);
            }
        }
        var desired=facing*correction;
        photoTorsoYawTarget=0;
        if(faceSlide)
        {
            // A seated person turns mostly from the waist. Rotating the entire
            // character makes the feet and knees skate around the chair.
            var seated=seat.transform.rotation*correction;
            float totalYaw=Mathf.DeltaAngle(seated.eulerAngles.y,desired.eulerAngles.y);
            photoTorsoYawTarget=totalYaw*(1f-photoRootTurnFraction);
            var euler=desired.eulerAngles;
            desired=Quaternion.Euler(euler.x,seated.eulerAngles.y+totalYaw*photoRootTurnFraction,euler.z);
        }
        // Ease the seated root into and out of the slide-facing pose. A bounded
        // angular speed prevents a large target change from snapping in one frame.
        bool turningToAction=faceLaptop||faceSlide;
        float smoothTime=(turningToAction?photoTurnSmoothTime:returnTurnSmoothTime)*seatFacingTurnVariation;
        float maxSpeed=(turningToAction?photoTurnMaxSpeed:returnTurnMaxSpeed)/seatFacingTurnVariation;
        float turnDelta=Mathf.Min(.05f,Mathf.Max(0,delta));
        float yaw=Mathf.SmoothDampAngle(transform.eulerAngles.y,desired.eulerAngles.y,
            ref seatFacingYawVelocity,smoothTime,maxSpeed,turnDelta);
        photoTorsoYaw=Mathf.SmoothDampAngle(photoTorsoYaw,photoTorsoYawTarget,
            ref photoTorsoYawVelocity,smoothTime,maxSpeed,turnDelta);
        var desiredEuler=desired.eulerAngles;
        transform.rotation=Quaternion.Euler(desiredEuler.x,yaw,desiredEuler.z);
        // Turn around the seated hip, not the FBX's offset root pivot.
        if(pose) transform.position=seat.transform.position-transform.rotation*Vector3.Scale(pose.localHip,transform.localScale);
        float angle=Quaternion.Angle(transform.rotation,desired);
        if(pendingTypingClip && angle<4f)
        {
            var clip=pendingTypingClip;
            pendingTypingClip=null;
            PlayClip(pendingTypingVariation,clip,pendingTypingDuration,pendingTypingIntensity);
        }
        float torsoAngle=Mathf.Abs(Mathf.DeltaAngle(photoTorsoYaw,photoTorsoYawTarget));
        if(pendingPhotoClip && Mathf.Max(angle,torsoAngle)<3f)
        {
            var clip=pendingPhotoClip;
            pendingPhotoClip=null;
            PlayClip(pendingPhotoVariation,clip,pendingPhotoDuration,pendingPhotoIntensity);
        }
        if(!faceLaptop && !faceSlide && angle<.1f && torsoAngle<.1f)
        {
            transform.rotation=desired;
            seatFacingYawVelocity=0; photoTorsoYaw=photoTorsoYawTarget=photoTorsoYawVelocity=0;
            ownsSeatFacing=false;
        }
    }

    private void SetPhotoPhoneVisible(bool visible, bool deviceVisible = false)
    {
        if (photoPhone && photoPhone.activeSelf != visible) photoPhone.SetActive(visible);
        if (devicePhone && devicePhone.activeSelf != deviceVisible) devicePhone.SetActive(deviceVisible);
    }
    private void DestroyAnimationGraph()
    {
        ReleaseConversationPartner();
        SetPhotoPhoneVisible(false);
        if (playableGraph.IsValid()) playableGraph.Destroy();
        voices.Clear(); target = core = null; transitioning = false;
        pendingTypingClip=null; pendingPhotoClip=null; ownsSeatFacing=false; seatFacingYawVelocity=0;
        photoTorsoYaw=photoTorsoYawTarget=photoTorsoYawVelocity=0; photoFacingSpine=null;
        questionTurn = questionPaused = false;
    }
}
