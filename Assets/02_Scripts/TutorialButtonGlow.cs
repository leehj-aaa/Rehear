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
        Update();
    }

    void Update()
    {
        if (!runtimeMaterial) return;
        runtimeMaterial.SetFloat(Strength, EvaluateStrength(Time.unscaledTime));
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
        if (targeted || (button && (!button.isActiveAndEnabled || !button.IsInteractable())))
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
        if (glowImage && runtimeMaterial) glowImage.material = originalMaterial;
        if (runtimeMaterial) Destroy(runtimeMaterial);
        runtimeMaterial = null;
    }
}
