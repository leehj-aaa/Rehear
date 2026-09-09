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
    [SerializeField] private GameObject retryConfirmationPanel;
    [SerializeField] private TMP_Text engagementDescription, credibilityDescription, clarityDescription;
    [SerializeField] private TMP_Text practiceTitle, practiceDescription, completionSign;
    [SerializeField] private FeedbackScoreRing engagementRing, credibilityRing, clarityRing;
    [SerializeField] private FeedbackAudienceApplause feedbackAudience;

    private void SetAudienceVisible(bool visible)
    {
        if (!feedbackAudience)
            foreach (var root in gameObject.scene.GetRootGameObjects())
            {
                feedbackAudience = root.GetComponentInChildren<FeedbackAudienceApplause>(true);
                if (feedbackAudience) break;
            }
        // Disabling the owner also stops its looping AudioSource via OnDisable.
        if (feedbackAudience) feedbackAudience.gameObject.SetActive(visible);
    }

    // 웹 UI와 동일한 의미 색상: 우수(민트), 보통(앰버), 개선(레드)
    private static readonly Color ExcellentTextColor = Hex("31C79A");
    private static readonly Color AverageTextColor = Hex("FFBD21");
    private static readonly Color ImproveTextColor = Hex("FF4C48");

    private void Start()
    {
        if (completionSign) completionSign.text = "수고하셨습니다";
        ShowResults();
        ApplyReport();
    }

    private void ApplyReport()
    {
        DisplayReport(RuntimeReportData.Report);
    }

    public void DisplayReport(ReportFeedback report)
    {
        if (report == null)
        {
            Debug.LogWarning(
                "[피드백] 불러온 AI 리포트가 없습니다."
            );

            ShowFallback();
            return;
        }

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
        if (engagementRing) engagementRing.Value = engagement / 100f;
        if (credibilityRing) credibilityRing.Value = credibility / 100f;
        if (clarityRing) clarityRing.Value = clarity / 100f;
        var descriptions = report.score_card.descriptions;
        SetDescription(engagementDescription, descriptions?.engagement, engagement >= 70 ? "청중의 관심을\n유지했어요" : "청중의 관심을 끌도록\n핵심을 강조해보세요");
        SetDescription(credibilityDescription, descriptions?.credibility, credibility >= 70 ? "내용을 믿을 만하게\n전달했어요" : "구체적인 근거를 더해\n전달해보세요");
        SetDescription(clarityDescription, descriptions?.clarity, clarity >= 70 ? "핵심을 분명하게\n전달했어요" : "핵심을 더 분명하게\n전달해보세요");
        string metric = clarity <= engagement && clarity <= credibility ? "명확도" : engagement <= credibility ? "몰입도" : "신뢰도";
        if (practiceTitle) practiceTitle.text = "다음 연습 · " + metric;
        SetDescription(practiceDescription, report.ai_insight?.description, metric == "명확도" ? "핵심 문장을 짧게 말하고, 잠깐 쉬어보세요." : metric == "몰입도" ? "핵심 메시지를 강조하고, 청중에게 질문해보세요." : "주장을 뒷받침하는 구체적인 근거를 덧붙여보세요.");

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

    private static void SetDescription(TMP_Text target, string supplied, string fallback)
    {
        if (target) target.text = string.IsNullOrWhiteSpace(supplied) ? fallback : supplied;
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

        // The Figma metric color identifies the metric, not the rating tier.
        text.color = text == engagementText ? Hex("0033FF") : text == credibilityText ? Hex("4522C4") : Hex("9FE300");
        if (badge != null)
            badge.enabled = false;
    }

    private void ShowFallback()
    {
        foreach(var ring in new[]{engagementRing,credibilityRing,clarityRing}) if(ring) ring.Value=0;
        foreach(var text in new[]{engagementDescription,credibilityDescription,clarityDescription}) if(text) text.text="결과를 불러오지 못했어요";
        if(practiceTitle)practiceTitle.text="결과 확인 필요";
        if(practiceDescription)practiceDescription.text="웹 리포트에서 세션 결과를 확인해주세요.";
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

    public void ShowRetryConfirmation()
    {
        SetAudienceVisible(false);
        foreach(var item in resultObjects) if(item)item.SetActive(false);
        if(sessionEndedPanel)sessionEndedPanel.SetActive(false);
        if(retryConfirmationPanel)retryConfirmationPanel.SetActive(true);
    }

    public void BackToResults() => ShowResults();

    public void GoToScene1()
    {
        if(retryConfirmationPanel)retryConfirmationPanel.SetActive(false);
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
        SetAudienceVisible(true);
        if(retryConfirmationPanel)retryConfirmationPanel.SetActive(false);
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
