using System.Collections;                                       
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Native four-layer recreation of Logo-Animation.mp4.
/// The four layers remain at their final size and fade in with reference timing.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
public sealed class RehearLogoIntro : MonoBehaviour
{
    public const float Duration = 4.4f;
    private const float LogoCanvasSize = 650f;

    [SerializeField] private bool playOnEnable = true;
    [SerializeField] private bool useUnscaledTime = true;
    [SerializeField] private float buttonFadeDuration = 1f;
    [SerializeField] private float logoStartDelay = 1.5f;

    private SpriteRenderer source;
    private Image symbol;
    private Image re;
    private Image colon;
    private Image hear;
    private GameObject startButton;
    private Color sourceColor;
    private float elapsed;
    private bool isPlaying;
    private Coroutine delayedPlay;

    private void Awake()
    {
        source = GetComponent<SpriteRenderer>();
        sourceColor = source.color;

        colon = CreatePart("LogoPart_Colon", "RehearLogoParts/Colon", 0);
        re = CreatePart("LogoPart_Re", "RehearLogoParts/Re", 1);
        hear = CreatePart("LogoPart_Hear", "RehearLogoParts/Hear", 2);
        symbol = CreatePart("LogoPart_Symbol", "RehearLogoParts/Symbol", 3);

        source.enabled = false;
        startButton = GameObject.Find("Btn_Scene00_to_Scene01");
        SetAllPartsHidden();
        if (startButton != null)
            startButton.SetActive(false);
    }

    private void OnEnable()
    {
        if (playOnEnable)
            delayedPlay = StartCoroutine(PlayAfterVisibleFrames());
    }

    [ContextMenu("Replay Logo Intro")]
    public void Play()
    {
        if (delayedPlay != null)
        {
            StopCoroutine(delayedPlay);
            delayedPlay = null;
        }

        elapsed = 0f;
        isPlaying = true;
        if (startButton != null)
            StartCoroutine(FadeInStartButton());    
        Evaluate(0f);
        Debug.Log("[RehearLogoIntro] Visible 4.4 second playback started.", this);
    }

    private IEnumerator FadeInStartButton()
    {
        startButton.SetActive(true);

        CanvasGroup canvasGroup = startButton.GetComponent<CanvasGroup>();

        if (canvasGroup == null)
            canvasGroup = startButton.AddComponent<CanvasGroup>();

        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;

        float elapsedTime = 0f;

        while (elapsedTime < buttonFadeDuration)
        {
            elapsedTime += useUnscaledTime
                ? Time.unscaledDeltaTime
                : Time.deltaTime;

            canvasGroup.alpha =
                Mathf.Clamp01(elapsedTime / buttonFadeDuration);

            yield return null;
        }

        canvasGroup.alpha = 1f;
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;
    }

    private void Update()
    {
        if (!isPlaying)
            return;

        elapsed = Mathf.Min(elapsed + (useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime), Duration);
        Evaluate(elapsed);

        if (elapsed >= Duration)
        {
            isPlaying = false;
            if (startButton != null)
                startButton.SetActive(true);
            Debug.Log("[RehearLogoIntro] Playback completed at 4.4 seconds.", this);
        }
    }

    private IEnumerator PlayAfterVisibleFrames()
    {
        // XR initialization and domain reload can consume several seconds before the
        // Game view presents anything. Count completed renders rather than wall time.
        for (int i = 0; i < 30; i++)
            yield return new WaitForEndOfFrame();

            yield return new WaitForSecondsRealtime(logoStartDelay);

        delayedPlay = null;
        Play();
    }

    private void SetAllPartsHidden()
    {
        SetPart(colon, 0f, 0f);
        SetPart(re, 0f, 0.10f);
        SetPart(hear, 0f, -0.10f);
        SetPart(symbol, 0f, 0.065f);
    }

    private Image CreatePart(string objectName, string resourcePath, int orderOffset)
    {
        Sprite sprite = Resources.Load<Sprite>(resourcePath);
        if (sprite == null)
        {
            Debug.LogError($"Re:hear logo part is missing: Resources/{resourcePath}", this);
            enabled = false;
            return null;
        }

        GameObject partObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer));
        partObject.transform.SetParent(transform, false);
        RectTransform rect = (RectTransform)partObject.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        // Bake the visible size into the UI geometry itself. CurvedUI decides how
        // many segments to create from RectTransform size and does not include a
        // parent Transform's scale in that tessellation calculation.
        rect.sizeDelta = new Vector2(sprite.bounds.size.x, sprite.bounds.size.y) * LogoCanvasSize;

        Image part = partObject.AddComponent<Image>();
        part.sprite = sprite;
        part.color = sourceColor;
        part.preserveAspect = true;
        part.raycastTarget = false;
        
        rect.SetSiblingIndex(orderOffset);
        return part;
    }

    private void Evaluate(float time)
    {
        if (symbol == null || re == null || colon == null || hear == null)
            return;

        // Timings measured from the supplied 133-frame / 30 fps reference.
        float colonT = SmootherStep(Mathf.InverseLerp(0.30f, 0.72f, time));
        float reT = SmootherStep(Mathf.InverseLerp(0.50f, 1.24f, time));
        float hearT = SmootherStep(Mathf.InverseLerp(0.62f, 1.64f, time));
        float symbolT = SmootherStep(Mathf.InverseLerp(1.05f, 2.45f, time));

        // Re and hear begin slightly nearer the colon, then glide outward.
        // The symbol enters from the right by an even smaller amount.
        SetPart(colon, colonT, 0f);
        SetPart(re, reT, 0.10f);
        SetPart(hear, hearT, -0.10f);
        SetPart(symbol, symbolT, 0.065f);
    }

    private void SetPart(Image part, float progress, float startOffsetX)
    {
        Transform partTransform = part.transform;
        partTransform.localPosition = Vector3.right *
                                      Mathf.LerpUnclamped(startOffsetX * LogoCanvasSize, 0f, progress);
        partTransform.localRotation = Quaternion.identity;
        partTransform.localScale = Vector3.one;

        Color color = sourceColor;
        color.a *= progress;
        part.color = color;
    }

    private static float SmootherStep(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * t * (t * (t * 6f - 15f) + 10f);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AttachToOpeningLogo()
    {
        if (SceneManager.GetActiveScene().name != "Scene_00")
            return;

        GameObject logoObject = GameObject.Find("Rehear Logo Intro");
        if (logoObject != null && logoObject.TryGetComponent(out SpriteRenderer _) &&
            logoObject.GetComponent<RehearLogoIntro>() == null)
        {
            logoObject.AddComponent<RehearLogoIntro>();
        }
    }
}
