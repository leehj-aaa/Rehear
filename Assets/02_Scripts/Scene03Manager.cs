using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Rehear.Evc.Contracts;

public class Scene03Manager : MonoBehaviour
{
    [Header("AI 평가 결과")]
    [SerializeField]
    private TMP_Text scoreText;

    [SerializeField]
    private TMP_Text engagementText;

    [SerializeField]
    private TMP_Text clarityText;

    [SerializeField]
    private TMP_Text credibilityText;

    [Header("평가 상태 배지")]
    [SerializeField]
    private Image engagementBadge;

    [SerializeField]
    private Image clarityBadge;

    [SerializeField]
    private Image credibilityBadge;

    [Header("결과 화면 흐름")]
    [SerializeField]
    private GameObject[] resultObjects;

    [SerializeField]
    private GameObject sessionEndedPanel;

    // 웹 UI와 동일한 의미 색상: 우수(민트), 보통(앰버), 개선(레드)
    private static readonly Color ExcellentTextColor = Hex("31C79A");
    private static readonly Color AverageTextColor = Hex("FFBD21");
    private static readonly Color ImproveTextColor = Hex("FF4C48");

    private void Start()
    {
        ShowResults();
        ApplyReport();
    }

    private void ApplyReport()
    {
        if (!RuntimeReportData.IsLoaded)
        {
            Debug.LogWarning(
                "[피드백] 불러온 AI 리포트가 없습니다."
            );

            ShowFallback();
            return;
        }

        ReportFeedback report =
            RuntimeReportData.Report;

        if (report.score == null ||
            report.score_card == null ||
            report.score_card.scores == null)
        {
            Debug.LogError(
                "[피드백] 리포트에 점수 정보가 없습니다."
            );

            ShowFallback();
            return;
        }

        int overall =
            Mathf.Clamp(
                report.score.overall_score,
                0,
                100
            );

        int engagement =
            Mathf.Clamp(
                report.score_card.scores.engagement,
                0,
                100
            );

        int clarity =
            Mathf.Clamp(
                report.score_card.scores.clarity,
                0,
                100
            );

        int credibility =
            Mathf.Clamp(
                report.score_card.scores.credibility,
                0,
                100
            );

        if (scoreText != null)
        {
            scoreText.text =
                overall.ToString();
        }

        ApplyRating(engagementText, engagementBadge, engagement);
        ApplyRating(clarityText, clarityBadge, clarity);
        ApplyRating(credibilityText, credibilityBadge, credibility);

        Debug.Log(
            "[피드백] AI 리포트 적용 완료" +
            "\n종합 점수: " + overall +
            "\n몰입도: " + engagement +
            " → " + ConvertScoreToLevel(engagement) +
            "\n명확도: " + clarity +
            " → " + ConvertScoreToLevel(clarity) +
            "\n신뢰도: " + credibility +
            " → " + ConvertScoreToLevel(credibility)
        );
    }

    private string ConvertScoreToLevel(int score)
    {
        if (score >= 70)
            return "우수";

        if (score >= 40)
            return "보통";

        return "개선";
    }

    private void ApplyRating(TMP_Text text, Image badge, int score)
    {
        if (text == null)
            return;

        string level = ConvertScoreToLevel(score);
        text.text = level;
        text.fontWeight = FontWeight.Bold;

        Color textColor;
        switch (level)
        {
            case "우수":
                textColor = ExcellentTextColor;
                break;
            case "보통":
                textColor = AverageTextColor;
                break;
            default:
                textColor = ImproveTextColor;
                break;
        }

        text.color = textColor;
        if (badge != null)
            badge.enabled = false;
    }

    private void ShowFallback()
    {
        if (scoreText != null)
            scoreText.text = "--";

        if (engagementText != null)
            engagementText.text = "-";

        if (clarityText != null)
            clarityText.text = "-";

        if (credibilityText != null)
            credibilityText.text = "-";
    }

    public void GoToScene2()
    {
        SceneManager.LoadScene(
            "Scene_02_Presentation"
        );
    }

    public void GoToScene1()
    {
        if (sessionEndedPanel != null)
        {
            if (resultObjects != null)
            {
                foreach (GameObject resultObject in resultObjects)
                {
                    if (resultObject != null)
                        resultObject.SetActive(false);
                }
            }

            sessionEndedPanel.SetActive(true);
            return;
        }

        ReturnToStart();
    }

    public void ReturnToStart()
    {
        RuntimeReportData.Clear();

        SceneManager.LoadScene(
            "Scene_01_Intro"
        );
    }

    private void ShowResults()
    {
        if (resultObjects != null)
        {
            foreach (GameObject resultObject in resultObjects)
            {
                if (resultObject != null)
                    resultObject.SetActive(true);
            }
        }

        if (sessionEndedPanel != null)
            sessionEndedPanel.SetActive(false);
    }

    private static Color Hex(string rgb)
    {
        ColorUtility.TryParseHtmlString("#" + rgb, out Color color);
        return color;
    }
}
