using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
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

    private void Start()
    {
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

        if (engagementText != null)
        {
            engagementText.text =
                ConvertScoreToLevel(engagement);
        }

        if (clarityText != null)
        {
            clarityText.text =
                ConvertScoreToLevel(clarity);
        }

        if (credibilityText != null)
        {
            credibilityText.text =
                ConvertScoreToLevel(credibility);
        }

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
            return "높음";

        if (score >= 40)
            return "보통";

        return "낮음";
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
        RuntimeReportData.Clear();

        SceneManager.LoadScene(
            "Scene_01_Intro"
        );
    }
}