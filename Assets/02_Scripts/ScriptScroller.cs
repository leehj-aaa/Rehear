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
