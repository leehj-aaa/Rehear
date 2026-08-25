using TMPro;
using Rehear.Evc.Presentation;
using UnityEngine;

// 기존 컴포넌트 연결을 보존하기 위해 클래스 이름은 ScriptScroller를 유지한다.
public class ScriptScroller : MonoBehaviour
{
    [SerializeField] private TMP_Text scriptText;
    [SerializeField] private TMP_Text pageIndicatorText;

    private int currentPage = 1;
    private int pageCount = 1;

    private void Start()
    {
        var contextScript = PresentationSessionContext.Current.Presentation?.page_2?.presentation_script_content;
        if (scriptText != null && !string.IsNullOrWhiteSpace(contextScript))
            scriptText.text = contextScript;
        RefreshPagination();
    }

    public void RefreshPagination()
    {
        if (scriptText == null)
            return;

        scriptText.overflowMode = TextOverflowModes.Page;
        scriptText.pageToDisplay = 1;

        Canvas.ForceUpdateCanvases();
        scriptText.ForceMeshUpdate(true, true);

        pageCount = Mathf.Max(1, scriptText.textInfo.pageCount);
        currentPage = Mathf.Clamp(currentPage, 1, pageCount);
        ApplyPage();
    }

    public void NextPage()
    {
        if (!CanChangePage() || currentPage >= pageCount)
            return;

        currentPage++;
        ApplyPage();
    }

    public void PreviousPage()
    {
        if (!CanChangePage() || currentPage <= 1)
            return;

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
        return scriptText != null && scriptText.gameObject.activeInHierarchy;
    }

    private void ApplyPage()
    {
        if (scriptText != null)
            scriptText.pageToDisplay = currentPage;

        if (pageIndicatorText != null)
            pageIndicatorText.text = currentPage + " / " + pageCount;
    }
}
