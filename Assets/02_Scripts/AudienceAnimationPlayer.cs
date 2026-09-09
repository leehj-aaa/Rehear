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
    [SerializeField] private AudioClip photoShutter;
    [SerializeField, Range(0,1)] private float photoShutterVolume=.10f;
    [SerializeField, Range(0,1)] private float photoShutterNormalizedTime=.30f;
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
    private bool ownsSeatFacing;
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
    public Transform DeviceLookTarget => devicePhone && devicePhone.activeInHierarchy ? devicePhone.transform : null;
    public bool IsTakingPhoto => photoPhone && photoPhone.activeInHierarchy;
    private AudiencePhotoPose photoPose;
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
        ApplyDeviceGrip();
        if(IsTakingPhoto && mixer.IsValid()) {
            float photoWeight=0;
            foreach(var voice in voices)if(voice.variation=="ACT_02.photoslide")photoWeight+=mixer.GetInputWeight(voice.port);
            var gaze=GetComponent<AudienceGazeController>();
            if(gaze && gaze.SlideTarget){
                if(photoPose==null)photoPose=new AudiencePhotoPose(transform);
                photoPose.Apply(photoPhone.transform,gaze.SlideTarget,photoWeight);
            }
        }
        UpdatePhotoShutter();
        var assignment=GetComponent<AudienceSeatAssignment>();
        if(!mixer.IsValid() || !assignment || !assignment.Seat || !assignment.Seat.HasLaptop || !assignment.Seat.laptopAnchor) return;
        float weight=0;
        foreach(var voice in voices) if(IsTyping(voice.variation)) weight+=mixer.GetInputWeight(voice.port);
        if(weight<=0) return;
        if(typingPose==null)typingPose=new AudienceTypingPose(transform);
        typingPose.Apply(assignment.Seat.laptopAnchor,weight);
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
        pendingTypingClip=null;
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
            // Stable looping listening clips need not all begin at the same frame.
            // One-shot actions always start at their authored beginning (hands/props).
            if (isCore && clip.isLooping && randomizeIdleStartTime && !variationId.StartsWith("AP_", System.StringComparison.Ordinal))
                playable.SetTime(Random.Range(0, clip.length));
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
        ReleaseConversationPartner();
        pendingTypingClip=null;
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
            t = t * t * (3 - 2 * t);
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
        ReleaseConversationPartner();
        SetPhotoPhoneVisible(false);
        if (playableGraph.IsValid()) playableGraph.Destroy();
        voices.Clear(); target = core = null; transitioning = false;
        pendingTypingClip=null; ownsSeatFacing=false;
        questionTurn = questionPaused = false;
    }
}
