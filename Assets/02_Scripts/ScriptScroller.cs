using TMPro;
using UnityEngine;

public class ScriptScroller : MonoBehaviour
{
    [SerializeField]
    private TMP_Text scriptText;

    [SerializeField]
    private TMP_Text pageIndicatorText;

    [Header("Optional page direction hints")]
    [SerializeField] private GameObject previousPageHint;
    [SerializeField] private GameObject nextPageHint;

    public int CurrentPage => currentPage;
    public int PageCount => pageCount;
    public TMP_Text ScriptText => scriptText;
    public bool IsAtPageBoundary(bool next) => CanChangePage() && (next ? currentPage >= pageCount : currentPage <= 1);

    [Header("Firebase 대본")]
    [SerializeField]
    private bool useRuntimeSessionScript = true;

    private int currentPage = 1;
    private int pageCount = 1;

    private void Start()
    {
        ApplyRuntimeSessionScript();
        RefreshPagination();
    }

    private void ApplyRuntimeSessionScript()
    {
        if (!useRuntimeSessionScript ||
            scriptText == null)
        {
            return;
        }

        string runtimeScript =
            RuntimeSessionData.PresentationScript;

        if (string.IsNullOrWhiteSpace(
                runtimeScript))
        {
            Debug.Log(
                "[대본] Firebase 대본이 없어 " +
                "기본 대본을 사용합니다."
            );

            return;
        }

        scriptText.text = runtimeScript;

        Debug.Log(
            "[대본] Firebase 대본 적용 완료" +
            "\n글자 수: " + runtimeScript.Length
        );
    }

    public void RefreshPagination()
    {
        if (scriptText == null)
        {
            UpdateDirectionHints();
            return;
        }

        // Keep complete Korean words together and keep glyphs inside the reading plate.
        TMP_Settings.useModernHangulLineBreakingRules = true;
        scriptText.textWrappingMode = TextWrappingModes.Normal;
        // The old inset was compensated by negative text margins. Use the whole
        // reading plate so positive padding does not collapse it to a single line.
        var textRect=scriptText.rectTransform;
        textRect.anchorMin=Vector2.zero;textRect.anchorMax=Vector2.one;
        textRect.offsetMin=Vector2.zero;textRect.offsetMax=Vector2.zero;
        float padding = scriptText.fontSize * .5f;
        scriptText.margin = new Vector4(padding, padding, padding, scriptText.fontSize * 2f);
        EnsurePageIndicator();
        EnsureDirectionCaption(previousPageHint, "대본", scriptText.font);
        EnsureDirectionCaption(nextPageHint, "대본", scriptText.font);

        scriptText.overflowMode =
            TextOverflowModes.Page;

        scriptText.pageToDisplay = 1;

        Canvas.ForceUpdateCanvases();
        scriptText.ForceMeshUpdate(true, true);

        pageCount =
            Mathf.Max(
                1,
                scriptText.textInfo.pageCount
            );

        currentPage =
            Mathf.Clamp(
                currentPage,
                1,
                pageCount
            );

        ApplyPage();
    }

    private void EnsurePageIndicator()
    {
        if (pageIndicatorText) return;
        var go = new GameObject("Script page indicator", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.layer = scriptText.gameObject.layer;
        go.transform.SetParent(scriptText.transform, false);
        pageIndicatorText = go.GetComponent<TMP_Text>();
        var controller = FindFirstObjectByType<PresentationController>();
        var numericStyle = controller && controller.timerText ? controller.timerText : scriptText;
        pageIndicatorText.font = numericStyle.font;
        pageIndicatorText.fontSharedMaterial = numericStyle.fontSharedMaterial;
        pageIndicatorText.fontSize = scriptText.fontSize * .65f;
        pageIndicatorText.color = new Color(1, 1, 1, .8f);
        pageIndicatorText.alignment = TextAlignmentOptions.Center;
        pageIndicatorText.raycastTarget = false;
        var rect = pageIndicatorText.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0);
        rect.pivot = new Vector2(.5f, 0);
        rect.anchoredPosition = new Vector2(0, scriptText.fontSize * .2f);
        rect.sizeDelta = new Vector2(scriptText.rectTransform.rect.width, scriptText.fontSize * 1.3f);
        scriptText.GetComponentInParent<CurvedUI.CurvedUISettings>()?.AddEffectToChildren();
    }

    public static void EnsureDirectionCaption(GameObject hint, string caption, TMP_FontAsset font)
    {
        if (!hint) return;
        var parent = hint.transform as RectTransform;
        if (!parent) return;
        float size = parent.rect.height;
        var existing=parent.Find("Direction caption");
        if(existing){PlaceDirectionCaption((RectTransform)existing,parent,size);return;}
        var go = new GameObject("Direction caption", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.layer = hint.layer;
        go.transform.SetParent(parent, false);
        var text = go.GetComponent<TMP_Text>();
        text.font = font;
        text.text = caption;
        text.fontSize = size * .24f;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        var rect = text.rectTransform;
        PlaceDirectionCaption(rect,parent,size);
        rect.sizeDelta = new Vector2(size * 2.5f, size * .5f);
        hint.GetComponentInParent<CurvedUI.CurvedUISettings>()?.AddEffectToChildren();
    }

    static void PlaceDirectionCaption(RectTransform rect,RectTransform arrow,float size)
    {
        rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);
        rect.localRotation=Quaternion.Inverse(arrow.localRotation);
        rect.anchoredPosition=Quaternion.Inverse(arrow.localRotation)*new Vector3(0,-size*.85f,0);
    }

    // Keep the void methods available to existing UnityEvent bindings.
    public void NextPage() => TryNextPage();
    public void PreviousPage() => TryPreviousPage();

    public bool TryNextPage()
    {
        if (!CanChangePage() ||
            currentPage >= pageCount)
        {
            return false;
        }

        currentPage++;
        ShowPagePress(nextPageHint);
        ApplyPage();
        return true;
    }

    public bool TryPreviousPage()
    {
        if (!CanChangePage() ||
            currentPage <= 1)
        {
            return false;
        }

        currentPage--;
        ShowPagePress(previousPageHint);
        ApplyPage();
        return true;
    }

    public void ResetToFirstPage()
    {
        currentPage = 1;
        RefreshPagination();
    }

    private bool CanChangePage()
    {
        return
            scriptText != null &&
            !string.IsNullOrWhiteSpace(scriptText.text) &&
            scriptText.gameObject.activeInHierarchy;
    }

    private static void ShowPagePress(GameObject hint)
    {
        if (Application.isPlaying && hint != null)
            hint.GetComponent<TutorialDirectionArrow>()?.ShowPressedFeedback();
    }

    private void ApplyPage()
    {
        if (scriptText != null)
        {
            scriptText.pageToDisplay =
                currentPage;
        }

        if (pageIndicatorText != null)
        {
            pageIndicatorText.text =
                currentPage +
                " / " +
                pageCount;
        }

        UpdateDirectionHints();
    }

    private void UpdateDirectionHints()
    {
        bool hasText = scriptText != null && !string.IsNullOrWhiteSpace(scriptText.text);
        if (previousPageHint != null)
            previousPageHint.SetActive(hasText && currentPage > 1);
        if (nextPageHint != null)
            nextPageHint.SetActive(hasText && currentPage < pageCount);
    }
}
