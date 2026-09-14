using System;
using System.Linq;
using Rehear.Evc.Contracts;
using UnityEngine;

// Owns the audience only in the results scene. No presentation/EVC commands run here.
public sealed class FeedbackAudienceApplause : MonoBehaviour
{
    [SerializeField] private AudienceSeating seating;
    [SerializeField] private AudioClip applauseLoop;
    [SerializeField, Range(0f, 1f)] private float applauseVolume = .09f;
    private AudioSource applauseSource;
    private float audioFade;
    private sealed class Participant
    {
        public string visual, id, row, side;
        public bool laptop;
    }
    private static Participant[] previousAudience;
    public static readonly string[] Variations = {
        "AP_01.polite_applause", "AP_02.enthusiastic_applause", "AP_03.light_applause"
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession() => previousAudience = null;

    public static void Capture(AudienceSeating source)
    {
        previousAudience = source ? source.seats.Where(s => s && s.Occupant).Select(s => new Participant {
            visual = s.Occupant.gameObject.name,
            id = s.Occupant.GetComponent<Rehear.Evc.Audience.AudienceAgent>().AgentId,
            row = s.row, side = s.side, laptop = s.HasLaptop
        }).ToArray() : null;
    }

    public void InitializeAudience()
    {
        if (!seating) seating = GetComponentInChildren<AudienceSeating>();
        if (!seating) throw new InvalidOperationException("Feedback audience seating is missing.");
        seating.Initialize(1234);
        var saved = previousAudience; previousAudience = null;
        if (saved != null && saved.Length == 6)
        {
            foreach (var p in saved)
            {
                var actor = seating.members.Single(a => a.gameObject.name == p.visual);
                actor.Configure(p.id, actor.ActionRegistry);
            }
            seating.ApplyServerProfiles(saved.Select(p => new AudienceDto {
                agent_id = p.id, profile = new AudienceSeatingProfile { row = p.row, seat = p.side, has_laptop = p.laptop }
            }).ToArray());
        }
    }

    public AudienceAnimationPlayer[] OrderedAudience() => seating.members
        .Select(a => a.GetComponent<AudienceAnimationPlayer>())
        .OrderBy(a => a.Gender).ThenBy(a => a.name, StringComparer.Ordinal).ToArray();

    public static string VariationFor(AudienceAnimationPlayer[] audience, int index)
    {
        int sameGenderBefore = audience.Take(index).Count(a => a.Gender == audience[index].Gender);
        return Variations[sameGenderBefore % Variations.Length];
    }

    private void Start()
    {
        InitializeAudience();
        var audience = OrderedAudience();
        for (int i = 0; i < audience.Length; i++)
            Applaud(audience[i], VariationFor(audience, i));
        // The editor preview advances poses manually and must not create scene audio.
        if (Application.isPlaying) StartApplauseAudio();
    }

    private void StartApplauseAudio()
    {
        if (!applauseLoop) return;
        if (!applauseSource) applauseSource = gameObject.AddComponent<AudioSource>();
        applauseSource.playOnAwake = false;
        applauseSource.clip = applauseLoop;
        applauseSource.loop = true;
        applauseSource.spatialBlend = 0f;
        applauseSource.volume = 0f;
        audioFade = 0f;
        applauseSource.Play();
    }

    private void Update()
    {
        if (!applauseSource) return;
        audioFade = Mathf.Min(1f, audioFade + Time.unscaledDeltaTime / .8f);
        applauseSource.volume = applauseVolume * Mathf.SmoothStep(0f, 1f, audioFade);
    }

    private void OnDisable()
    {
        if (applauseSource) applauseSource.Stop();
    }

    private void OnEnable()
    {
        if (Application.isPlaying && applauseSource) StartApplauseAudio();
    }

    private void Applaud(AudienceAnimationPlayer body, string variation)
    {
        // Retain the authored head/hand motion while clapping, without random gaze overrides.
        var gaze = body.GetComponent<AudienceGazeController>();
        if (gaze) gaze.enabled = false;
        body.ReleaseQuestionTurn();
        if (body && !body.PlayServerVariation(variation, float.PositiveInfinity, 1f))
            Debug.LogError("[Feedback] Applause clip could not start: " + body.name + " / " + variation, body);
    }
}
