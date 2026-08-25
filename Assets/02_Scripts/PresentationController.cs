using System;
using System.Threading;
using Rehear.Evc.Presentation;
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
    [SerializeField] private bool useEvcPipeline;
    [SerializeField] private PresentationFlowController flowController;
    [SerializeField] private PresentationManager presentationManager;
    public QuestionAnswerManager qaManager;

    private float timeRemaining = 60f;
    private bool isRunning = true;
    private bool isTimerFinished;
    private bool isQAPhaseStarted;
    private bool isGeneratingQuestions;
    private CancellationTokenSource lifetimeCancellation;

    public bool IsPaused => !isRunning;

    private async void Start()
    {
        lifetimeCancellation = new CancellationTokenSource();
        if (!useEvcPipeline)
            flowController = null;
        else if (flowController == null)
            flowController = FindFirstObjectByType<PresentationFlowController>();
        if (presentationManager == null)
            presentationManager = FindFirstObjectByType<PresentationManager>();
        if (presentationManager != null)
            presentationManager.SlideChanged += OnSlideChanged;
        var presentation = PresentationSessionContext.Current.Presentation;
        if (presentation?.page_1 != null && presentation.page_1.duration_minutes > 0)
            timeRemaining = presentation.page_1.duration_minutes * 60f;

        SetScriptPanelVisible(false);
        if (pausePanel != null) pausePanel.SetActive(false);
        qaManager?.Prepare(qaButton);

        if (qaButton != null)
            qaButton.gameObject.SetActive(false);

        if (flowController != null)
        {
            isRunning = false;
            try
            {
                await flowController.StartPresentationAsync();
                isRunning = flowController.State == PresentationFlowState.Running;
            }
            catch (Exception)
            {
                isRunning = false;
            }
        }
    }

    public async void OnActionButtonClick()
    {
        if (isGeneratingQuestions) return;

        if (!isQAPhaseStarted && flowController != null &&
            (flowController.State == PresentationFlowState.Running ||
             flowController.State == PresentationFlowState.Paused ||
             flowController.CanRetryQuestions))
        {
            isGeneratingQuestions = true;
            qaManager?.ShowGenerating();
            try
            {
                var questions = await flowController.EndPresentationAndLoadQuestionsAsync(
                    presentationManager != null ? presentationManager.CurrentSlideIndex : 0,
                    lifetimeCancellation.Token);
                qaManager?.SetGeneratedQuestions(questions);
                isQAPhaseStarted = true;
                StartQA();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
                qaManager?.ShowGenerationFailed("질문을 불러오지 못했습니다. 다시 시도해주세요.");
            }
            finally
            {
                isGeneratingQuestions = false;
            }
            return;
        }

        if (!isQAPhaseStarted && flowController != null)
        {
            qaManager?.ShowGenerationFailed("AI 세션을 복구할 수 없습니다. 발표를 다시 시작해주세요.");
            return;
        }

        if (!isQAPhaseStarted) isQAPhaseStarted = true;

        qaManager?.OnActionButtonClick();
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

    public async void PauseGame()
    {
        isRunning = false;

        if (pausePanel != null)
        {
            pausePanel.SetActive(true);
            pausePanel.transform.SetAsLastSibling();
        }

        sessionAudioSource?.Pause();
        if (flowController != null)
        {
            try
            {
                await flowController.PauseAsync(
                    presentationManager != null ? presentationManager.CurrentSlideIndex : 0,
                    lifetimeCancellation.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
                Debug.LogWarning("EVC pause segment update failed.");
            }
        }
    }

    public async void ResumeGame()
    {
        if (flowController != null)
        {
            await flowController.ResumeAsync(lifetimeCancellation.Token);
            if (flowController.State != PresentationFlowState.Running)
                return;
        }

        isRunning = true;

        if (pausePanel != null)
            pausePanel.SetActive(false);

        sessionAudioSource?.UnPause();
    }

    public void RestartSession()
    {
        flowController?.StopFlow();
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void StopSession()
    {
        flowController?.StopFlow();
        PlayerPrefs.SetInt(ShowSessionReadyKey, 1);
        PlayerPrefs.Save();
        SceneManager.LoadScene("Scene_01_Intro");
    }

    public void StartQA()
    {
        qaManager?.StartQAPhase(qaButton);
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

    private async void OnSlideChanged(int slideIndex)
    {
        if (flowController == null || flowController.State != PresentationFlowState.Running)
            return;
        try
        {
            await flowController.FlushSegmentAsync("slide_transition", slideIndex, lifetimeCancellation.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            Debug.LogWarning("EVC slide transition update failed.");
        }
    }

    private void OnDestroy()
    {
        if (presentationManager != null)
            presentationManager.SlideChanged -= OnSlideChanged;
        lifetimeCancellation?.Cancel();
        lifetimeCancellation?.Dispose();
    }
}
