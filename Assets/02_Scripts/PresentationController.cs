using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class PresentationController : MonoBehaviour
{
    public TextMeshProUGUI timerText;
    public Button qaButton;
    public GameObject pausePanel;
    public GameObject scriptPanel;
    [SerializeField] private TextMeshProUGUI scriptButtonText;
    public QuestionAnswerManager qaManager;

    private float timeRemaining = 600f;
    private bool isRunning = true;
    private bool isTimerFinished;
    private bool isQAPhaseStarted;

    private void Start()
    {
        SetScriptPanelVisible(false);
        pausePanel.SetActive(false);
        qaManager.Prepare(qaButton);
        if (qaButton != null) qaButton.gameObject.SetActive(false);
    }

    public void OnActionButtonClick()
    {
        if (!isQAPhaseStarted)
        {
            isQAPhaseStarted = true;
        }

        qaManager.OnActionButtonClick();
    }

    private void Update()
    {
        if (isQAPhaseStarted)
            return;

        if (isRunning && !isTimerFinished)
        {
            timeRemaining -= Time.deltaTime;
            UpdateTimerDisplay();
            if (timeRemaining <= 0f) FinishTimer();
        }
        else if (isTimerFinished)
        {
            timeRemaining += Time.deltaTime;
            timerText.text = "+" + FormatTime(timeRemaining);
        }
    }

    private void UpdateTimerDisplay() => timerText.text = FormatTime(timeRemaining);

    private string FormatTime(float time)
    {
        int minutes = Mathf.FloorToInt(Mathf.Abs(time) / 60f);
        int seconds = Mathf.FloorToInt(Mathf.Abs(time) % 60f);
        return string.Format("{0:00}:{1:00}", minutes, seconds);
    }

    private void FinishTimer()
    {
        isTimerFinished = true;
        if (qaButton != null) qaButton.gameObject.SetActive(true);
    }

    public void StartQA()
    {
        qaManager.StartQAPhase(qaButton);
        SetScriptPanelVisible(true);
    }

    public void CloseScriptPanel() => SetScriptPanelVisible(false);

    public void OpenScriptPanel()
    {
        if (scriptPanel != null) SetScriptPanelVisible(!scriptPanel.activeSelf);
    }

    private void SetScriptPanelVisible(bool visible)
    {
        if (scriptPanel != null) scriptPanel.SetActive(visible);

        if (scriptButtonText == null)
        {
            GameObject scriptButton = GameObject.Find("Btn_Script");
            if (scriptButton != null)
                scriptButtonText = scriptButton.GetComponentInChildren<TextMeshProUGUI>(true);
        }

        if (scriptButtonText != null) scriptButtonText.text = visible ? "OFF" : "ON";
    }

    public void SkipPresentation() => timeRemaining = 10f;
    public void PauseGame() { isRunning = false; pausePanel.SetActive(true); }
    public void ResumeGame() { isRunning = true; pausePanel.SetActive(false); }
    public void RestartSession() { timeRemaining = 600f; isTimerFinished = false; ResumeGame(); }
    public void StopSession() => SceneManager.LoadScene("Scene_01_Intro");
}
