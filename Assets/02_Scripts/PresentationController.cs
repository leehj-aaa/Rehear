using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class PresentationController : MonoBehaviour
{
    private const string ShowSessionReadyKey = "ShowSessionReadyOnLoad";

    public TextMeshProUGUI timerText;
    public Button qaButton;
    public GameObject pausePanel;
    public GameObject scriptPanel;
    [SerializeField] private TextMeshProUGUI scriptButtonText;
    [SerializeField] private AudioSource sessionAudioSource;
    public QuestionAnswerManager qaManager;

    private float timeRemaining = 600f;
    private bool isRunning = true;
    private bool isTimerFinished;
    private bool isQAPhaseStarted;

    public bool IsPaused => !isRunning;

    private void Start()
    {
        SetScriptPanelVisible(false);
        pausePanel.SetActive(false);
        qaManager.Prepare(qaButton);

        if (qaButton != null)
            qaButton.gameObject.SetActive(false);
    }

    public void OnActionButtonClick()
    {
        if (!isQAPhaseStarted)
            isQAPhaseStarted = true;

        qaManager.OnActionButtonClick();
    }

    private void Update()
    {
        if (!isRunning || isQAPhaseStarted)
            return;

        if (!isTimerFinished)
        {
            timeRemaining -= Time.deltaTime;
            UpdateTimerDisplay();

            if (timeRemaining <= 0f)
                FinishTimer();
        }
        else
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
        if (qaButton != null)
            qaButton.gameObject.SetActive(true);
    }

    public void TogglePause()
    {
        if (IsPaused)
            ResumeGame();
        else
            PauseGame();
    }

    public void PauseGame()
    {
        isRunning = false;

        if (pausePanel != null)
        {
            pausePanel.SetActive(true);
            pausePanel.transform.SetAsLastSibling();
        }

        sessionAudioSource?.Pause();
    }

    public void ResumeGame()
    {
        isRunning = true;

        if (pausePanel != null)
            pausePanel.SetActive(false);

        sessionAudioSource?.UnPause();
    }

    public void RestartSession()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void StopSession()
    {
        PlayerPrefs.SetInt(ShowSessionReadyKey, 1);
        PlayerPrefs.Save();
        SceneManager.LoadScene("Scene_01_Intro");
    }

    public void StartQA()
    {
        qaManager.StartQAPhase(qaButton);
        SetScriptPanelVisible(true);
    }

    public void CloseScriptPanel() => SetScriptPanelVisible(false);

    public void OpenScriptPanel()
    {
        if (scriptPanel != null)
            SetScriptPanelVisible(!scriptPanel.activeSelf);
    }

    private void SetScriptPanelVisible(bool visible)
    {
        if (scriptPanel != null)
            scriptPanel.SetActive(visible);

        if (scriptButtonText == null)
        {
            GameObject scriptButton = GameObject.Find("Btn_Script");
            if (scriptButton != null)
                scriptButtonText = scriptButton.GetComponentInChildren<TextMeshProUGUI>(true);
        }

        if (scriptButtonText != null)
            scriptButtonText.text = visible ? "OFF" : "ON";
    }

    public void SkipPresentation() => timeRemaining = 10f;
}
