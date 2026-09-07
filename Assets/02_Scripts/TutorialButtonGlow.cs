using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>Local UI glow; no full-screen pass and no changes to shared materials.</summary>
[DisallowMultipleComponent]
public sealed class TutorialButtonGlow : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    public Image glowImage;
    [Min(0.5f)] public float pulsePeriod = 1.6f;
    [Range(0f, 1f)] public float minimumStrength = 0.35f;
    [Range(0f, 1f)] public float maximumStrength = 1f;
    Material originalMaterial;
    Material runtimeMaterial;
    float startTime;
    float resumeAt;
    bool wasSuppressed;
    Button button;
    XRBaseInteractable xrInteractable;
    AudioClip promptClip;
    AudioSource promptSource;
    float promptVolume;
    bool applicationPaused;
    float[] promptEnvelope;
    const float EnvelopeStep = 0.01f;
    readonly HashSet<int> hoveringPointers = new HashSet<int>();
    readonly HashSet<int> pressedPointers = new HashSet<int>();
    static readonly int Strength = Shader.PropertyToID("_GlowStrength");
    static readonly int ButtonSize = Shader.PropertyToID("_ButtonSize");

    public static float EvaluatePulse(float elapsed, float period, float minimum, float maximum)
    {
        float wave = 0.5f + 0.5f * Mathf.Cos(elapsed * Mathf.PI * 2f / Mathf.Max(0.5f, period));
        return Mathf.Lerp(minimum, maximum, wave);
    }

    void OnEnable()
    {
        button = GetComponent<Button>();
        xrInteractable = GetComponent<XRBaseInteractable>();
        ResetInteraction();
        if (!Application.isPlaying || !glowImage || !glowImage.material) return;
        originalMaterial = glowImage.material;
        runtimeMaterial = new Material(originalMaterial) { name = originalMaterial.name + " (Runtime)" };
        glowImage.material = runtimeMaterial;
        startTime = Time.unscaledTime;
        EnsurePromptSource();
        Update();
    }

    void Update()
    {
        if (!runtimeMaterial) return;
        float strength = EvaluateStrength(Time.unscaledTime);
        if (promptSource)
        {
            if (strength <= 0)
            {
                if (promptSource.isPlaying) promptSource.Stop();
            }
            else
            {
                if (!promptSource.isPlaying) promptSource.Play();
                // Follow the sound's actual loudness/rhythm, using the playback
                // cursor rather than an independent animation timer.
                strength = SampleEnvelope(promptEnvelope,
                    promptSource.timeSamples / (float)promptClip.frequency) * maximumStrength;
            }
        }
        runtimeMaterial.SetFloat(Strength, strength);
        var size = ((RectTransform)transform).rect.size;
        runtimeMaterial.SetVector(ButtonSize, new Vector4(size.x, size.y, 0, 0));
    }

    float EvaluateStrength(float now)
    {
        // CurvedUI/XR UI rays use pointer events; the optional 3D interactable
        // route is also respected. Multiple rays must all leave before resuming.
        bool targeted = hoveringPointers.Count > 0 || pressedPointers.Count > 0 ||
            (xrInteractable && xrInteractable.isActiveAndEnabled &&
                (xrInteractable.isHovered || xrInteractable.isSelected));
        if (applicationPaused || targeted || (button && (!button.isActiveAndEnabled || !button.IsInteractable())))
        {
            wasSuppressed = true;
            return 0;
        }
        if (wasSuppressed)
        {
            resumeAt = Mathf.Max(resumeAt, now + 0.35f);
            startTime = resumeAt + pulsePeriod * 0.5f;
            wasSuppressed = false;
        }
        if (now < resumeAt) return 0;
        return EvaluatePulse(now - startTime, pulsePeriod, minimumStrength, maximumStrength);
    }

    public void ConfigurePrompt(AudioClip clip, float volume)
    {
        if (clip != promptClip || promptEnvelope == null)
        {
            promptEnvelope = null;
            if (clip && clip.LoadAudioData())
            {
                var samples = new float[clip.samples * clip.channels];
                if (clip.GetData(samples, 0)) promptEnvelope = BuildEnvelope(samples, clip.channels, clip.frequency);
            }
        }
        promptClip = clip;
        promptVolume = Mathf.Clamp01(volume);
        EnsurePromptSource();
        Update();
    }

    public static float[] BuildEnvelope(float[] samples, int channels, int frequency)
    {
        int stride = Mathf.Max(1, Mathf.RoundToInt(frequency * EnvelopeStep)) * channels;
        var envelope = new float[Mathf.CeilToInt(samples.Length / (float)stride)];
        float peak = 0;
        for (int block = 0; block < envelope.Length; block++)
        {
            int begin = block * stride, end = Mathf.Min(samples.Length, begin + stride);
            double energy = 0;
            for (int i = begin; i < end; i++) energy += samples[i] * samples[i];
            envelope[block] = Mathf.Sqrt((float)(energy / (end - begin)));
            peak = Mathf.Max(peak, envelope[block]);
        }
        if (peak < 0.0001f) return envelope;
        for (int i = 0; i < envelope.Length; i++)
        {
            // Reject the quiet tail/noise floor; keep each distinct hit and decay.
            float level = Mathf.InverseLerp(0.035f, 1, envelope[i] / peak);
            envelope[i] = Mathf.SmoothStep(0, 1, level);
        }
        return envelope;
    }

    public static float SampleEnvelope(float[] envelope, float seconds)
    {
        if (envelope == null || envelope.Length == 0) return 0;
        float index = Mathf.Clamp(seconds / EnvelopeStep, 0, envelope.Length - 1);
        int left = Mathf.FloorToInt(index);
        return Mathf.Lerp(envelope[left], envelope[Mathf.Min(left + 1, envelope.Length - 1)], index - left);
    }

    void EnsurePromptSource()
    {
        if (!Application.isPlaying || !isActiveAndEnabled || !promptClip) return;
        if (!promptSource)
        {
            var go = new GameObject("Tutorial Prompt Audio (Runtime)");
            go.transform.SetParent(transform, false);
            promptSource = go.AddComponent<AudioSource>();
            promptSource.playOnAwake = false;
            promptSource.loop = true;
            promptSource.spatialBlend = 0;
            promptSource.spatialize = false;
            promptSource.dopplerLevel = 0;
            promptSource.bypassReverbZones = true;
        }
        if (promptSource.clip != promptClip)
        {
            promptSource.Stop();
            promptSource.clip = promptClip;
        }
        promptSource.volume = promptVolume;
    }

    void OnApplicationPause(bool paused)
    {
        applicationPaused = paused;
        Update();
    }

    // Called only after the manager accepts a click (including the XR route).
    public void NotifyAcceptedClick()
    {
        resumeAt = Time.unscaledTime + 0.45f;
        startTime = resumeAt + pulsePeriod * 0.5f;
        Update();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        hoveringPointers.Add(eventData.pointerId);
        Update();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        hoveringPointers.Remove(eventData.pointerId);
        Update();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        pressedPointers.Add(eventData.pointerId);
        Update();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        pressedPointers.Remove(eventData.pointerId);
        Update();
    }

    void ResetInteraction()
    {
        hoveringPointers.Clear();
        pressedPointers.Clear();
        resumeAt = 0;
        wasSuppressed = false;
    }

    void OnDisable()
    {
        ResetInteraction();
        if (promptSource)
        {
            promptSource.Stop();
            Destroy(promptSource.gameObject);
            promptSource = null;
        }
        if (glowImage && runtimeMaterial) glowImage.material = originalMaterial;
        if (runtimeMaterial) Destroy(runtimeMaterial);
        runtimeMaterial = null;
    }
}
