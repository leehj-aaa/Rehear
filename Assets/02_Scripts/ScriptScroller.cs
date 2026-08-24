using TMPro;
using UnityEngine;

public class ScriptScroller : MonoBehaviour
{
    [SerializeField]
    private TMP_Text scriptText;

    [SerializeField]
    private TMP_Text pageIndicatorText;

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
            return;

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

    public void NextPage()
    {
        if (!CanChangePage() ||
            currentPage >= pageCount)
        {
            return;
        }

        currentPage++;
        ApplyPage();
    }

    public void PreviousPage()
    {
        if (!CanChangePage() ||
            currentPage <= 1)
        {
            return;
        }

        currentPage--;
        ApplyPage();
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
            scriptText.gameObject.activeInHierarchy;
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
    }
}