using UnityEngine;
using UnityEngine.UI;

/// <summary>Local UI glow; no full-screen pass and no changes to shared materials.</summary>
[DisallowMultipleComponent]
public sealed class TutorialButtonGlow : MonoBehaviour
{
    public Image glowImage;
    [Min(0.5f)] public float pulsePeriod = 1.6f;
    [Range(0f, 1f)] public float minimumStrength = 0.35f;
    [Range(0f, 1f)] public float maximumStrength = 1f;
    Material originalMaterial;
    Material runtimeMaterial;
    float startTime;
    static readonly int Strength = Shader.PropertyToID("_GlowStrength");
    static readonly int ButtonSize = Shader.PropertyToID("_ButtonSize");

    public static float EvaluatePulse(float elapsed, float period, float minimum, float maximum)
    {
        float wave = 0.5f + 0.5f * Mathf.Cos(elapsed * Mathf.PI * 2f / Mathf.Max(0.5f, period));
        return Mathf.Lerp(minimum, maximum, wave);
    }

    void OnEnable()
    {
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
        runtimeMaterial.SetFloat(Strength, EvaluatePulse(Time.unscaledTime - startTime,
            pulsePeriod, minimumStrength, maximumStrength));
        var size = ((RectTransform)transform).rect.size;
        runtimeMaterial.SetVector(ButtonSize, new Vector4(size.x, size.y, 0, 0));
    }

    void OnDisable()
    {
        if (glowImage && runtimeMaterial) glowImage.material = originalMaterial;
        if (runtimeMaterial) Destroy(runtimeMaterial);
        runtimeMaterial = null;
    }
}
