using System;
using System.Threading;
using Rehear.Evc.Presentation;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Rehear.Evc.Contracts;

public class PresentationController : MonoBehaviour
{
    private const string ShowSessionReadyKey =
        "ShowSessionReadyOnLoad";

    [Header("기존 발표 UI")]
    public TextMeshProUGUI timerText;
    public Button qaButton;
    public GameObject pausePanel;
    public GameObject scriptPanel;

    [SerializeField]
    private TextMeshProUGUI scriptButtonText;

    [SerializeField]
    private AudioSource sessionAudioSource;

    public QuestionAnswerManager qaManager;

    [Header("EVC 서버 연동")]
    [SerializeField]
    private bool useEvcPipeline;

    [SerializeField]
    private PresentationFlowController flowController;

    [SerializeField]
    private PresentationManager presentationManager;

    [Header("Timer Warning")]
    [SerializeField]
    private float warningStartSeconds = 10f;

    [Header("EVC 자동 음성 전송")]
    [SerializeField]
    [Min(3f)]
    private float evcSegmentIntervalSeconds = 8f;

    private float nextEvcSegmentTime;

    [SerializeField]
    private Color warningColor = Color.red;

    [SerializeField]
    private float blinkSpeed = 1.5f;

    private Color normalTimerColor;

    private float timeRemaining = 60f;
    private bool isRunning = true;
    private bool isTimerFinished;
    private bool isQAPhaseStarted;
    private bool isGeneratingQuestions;

    private CancellationTokenSource lifetimeCancellation;

    public bool IsPaused => !isRunning;

    private async void Start()
    {
        lifetimeCancellation =
            new CancellationTokenSource();

        if (!useEvcPipeline)
        {
            flowController = null;
        }
        else if (flowController == null)
        {
            flowController =
                FindFirstObjectByType<
                    PresentationFlowController>();
        }

        if (presentationManager == null)
        {
            presentationManager =
                FindFirstObjectByType<
                    PresentationManager>();
        }

        if (presentationManager != null)
        {
            presentationManager.SlideChanged +=
                OnSlideChanged;
        }

        if (RuntimeSessionData.IsLoaded &&
            RuntimeSessionData.DurationMinutes > 0)
        {
            timeRemaining =
                RuntimeSessionData.DurationMinutes *
                60f;

            Debug.Log(
                "[발표 타이머] Firebase 발표 시간 적용: " +
                RuntimeSessionData.DurationMinutes +
                "분"
            );
        }
        else
        {
            timeRemaining = 60f;

            Debug.LogWarning(
                "[발표 타이머] 세션 시간이 없어 " +
                "기본 1분을 사용합니다."
            );
        }

        if (timerText != null)
        {
            normalTimerColor = timerText.color;
            UpdateTimerDisplay();
        }

        SetScriptPanelVisible(false);

        if (pausePanel != null)
            pausePanel.SetActive(false);

        qaManager?.Prepare(qaButton);

        if (qaButton != null)
            qaButton.gameObject.SetActive(false);

        if (flowController == null)
            return;

        // 서버 세션이 준비될 때까지 발표 타이머를 멈춘다.
        isRunning = false;

        try
        {
            await flowController
                .StartPresentationAsync();

            isRunning =
                flowController.State ==
                PresentationFlowState.Running;

           if (isRunning)
            {
                nextEvcSegmentTime =
                    Time.unscaledTime +
                    evcSegmentIntervalSeconds;

                Debug.Log(
                    "[EVC] 발표 서버 세션 시작 완료"
                );
            }
        }
        catch (Exception exception)
        {
            isRunning = false;

            if (exception is EvcApiException apiException)
            {
                Debug.LogError(
                    "[EVC] 발표 서버 세션 시작 실패" +
                    "\nHTTP 상태: " + apiException.HttpStatus +
                    "\n오류 종류: " + apiException.Kind +
                    "\n오류 코드: " + apiException.ErrorCode +
                    "\n메시지: " + apiException.Message
                );
            }
            else
            {
                Debug.LogError(
                    "[EVC] 발표 서버 세션 시작 실패" +
                    "\n예외 타입: " +
                    exception.GetType().Name +
                    "\n메시지: " +
                    exception.Message
                );
            }
        }
    }

    public async void OnActionButtonClick()
    {
        if (isGeneratingQuestions)
            return;

        // 새 EVC 파이프라인을 사용하는 경우
        if (!isQAPhaseStarted &&
            flowController != null &&
            (
                flowController.State ==
                PresentationFlowState.Running ||
                flowController.State ==
                PresentationFlowState.Paused ||
                flowController.CanRetryQuestions
            ))
        {
            isGeneratingQuestions = true;
            isRunning = false;

            qaManager?.ShowGenerating();

            try
            {
                int slideIndex =
                    presentationManager != null
                        ? presentationManager
                            .CurrentSlideIndex
                        : 0;

                var questions =
                    await flowController
                        .EndPresentationAndLoadQuestionsAsync(
                            slideIndex,
                            lifetimeCancellation.Token
                        );

                qaManager?.SetGeneratedQuestions(
                    questions
                );

                isQAPhaseStarted = true;
                StartQA();

                Debug.Log(
                    "[EVC] 서버 질문 적용 완료: " +
                    questions.Count + "개"
                );
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "[EVC] 질문 생성 실패: " +
                    exception.Message
                );

                qaManager?.ShowGenerationFailed(
                    "질문을 불러오지 못했습니다. " +
                    "다시 시도해주세요."
                );
            }
            finally
            {
                isGeneratingQuestions = false;
            }

            return;
        }

        // EVC 질문 생성 실패 후 복구할 수 없는 상태
        if (!isQAPhaseStarted &&
            flowController != null)
        {
            qaManager?.ShowGenerationFailed(
                "AI 세션을 복구할 수 없습니다. " +
                "발표를 다시 시작해주세요."
            );

            return;
        }

        // 기존 로컬 질문 흐름
        if (!isQAPhaseStarted)
            isQAPhaseStarted = true;

        qaManager?.OnActionButtonClick();
    }

    private void Update()
    {

                #if UNITY_EDITOR
            var keyboard =
                UnityEngine.InputSystem.Keyboard.current;

            if (keyboard != null)
            {
                if (keyboard.sKey.wasPressedThisFrame)
                {
                    SkipPresentation();
                }

                if (keyboard.qKey.wasPressedThisFrame &&
                    qaButton != null &&
                    qaButton.gameObject.activeInHierarchy)
                {
                    OnActionButtonClick();
                }
            }
        #endif
       
        UpdateTimerWarning();

        if (!isRunning ||
            isQAPhaseStarted)
        {
            return;
        }

        if (flowController != null &&
            flowController.State ==
                PresentationFlowState.Running &&
            Time.unscaledTime >=
                nextEvcSegmentTime)
        {
            nextEvcSegmentTime =
                Time.unscaledTime +
                evcSegmentIntervalSeconds;

            FlushPeriodicEvcSegment();
        }

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

            if (timerText != null)
            {
                timerText.text =
                    "+" + FormatTime(timeRemaining);
            }
        }
    }


    private async void FlushPeriodicEvcSegment()
{
    if (flowController == null ||
        flowController.State !=
            PresentationFlowState.Running)
    {
        return;
    }

    int slideIndex =
        presentationManager != null
            ? presentationManager
                .CurrentSlideIndex
            : 0;

    try
    {
        await flowController.FlushSegmentAsync(
            "utterance_boundary",
            slideIndex,
            lifetimeCancellation.Token
        );

        Debug.Log(
            "[EVC] 발표 음성 주기 전송" +
            "\n슬라이드: " + slideIndex
        );
    }
    catch (OperationCanceledException)
    {
    }
    catch (Exception exception)
    {
        Debug.LogWarning(
            "[EVC] 발표 음성 주기 전송 실패: " +
            exception.Message
        );
    }
}


    private void UpdateTimerDisplay()
    {
        if (timerText != null)
            timerText.text =
                FormatTime(timeRemaining);
    }

    private string FormatTime(float time)
    {
        int minutes =
            Mathf.FloorToInt(
                Mathf.Abs(time) / 60f
            );

        int seconds =
            Mathf.FloorToInt(
                Mathf.Abs(time) % 60f
            );

        return string.Format(
            "{0:00}:{1:00}",
            minutes,
            seconds
        );
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

        if (flowController == null)
            return;

        try
        {
            int slideIndex =
                presentationManager != null
                    ? presentationManager
                        .CurrentSlideIndex
                    : 0;

            await flowController.PauseAsync(
                slideIndex,
                lifetimeCancellation.Token
            );
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                "[EVC] 일시정지 음성 전송 실패: " +
                exception.Message
            );
        }
    }

    public async void ResumeGame()
    {
        if (flowController != null)
        {
            try
            {
                await flowController.ResumeAsync(
                    lifetimeCancellation.Token
                );
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[EVC] 발표 재개 실패: " +
                    exception.Message
                );

                return;
            }

            if (flowController.State !=
                PresentationFlowState.Running)
            {
                return;
            }
        }

        isRunning = true;

        if (pausePanel != null)
            pausePanel.SetActive(false);

        sessionAudioSource?.UnPause();
    }

    public void RestartSession()
    {
        flowController?.StopFlow();

        SceneManager.LoadScene(
            SceneManager.GetActiveScene().name
        );
    }

    public void StopSession()
    {
        flowController?.StopFlow();

        PlayerPrefs.SetInt(
            ShowSessionReadyKey,
            1
        );

        PlayerPrefs.Save();

        SceneManager.LoadScene(
            "Scene_01_Intro"
        );
    }

    public void StartQA()
    {
        qaManager?.StartQAPhase(qaButton);
        SetScriptPanelVisible(true);
    }

    public void CloseScriptPanel()
    {
        SetScriptPanelVisible(false);
    }

    public void OpenScriptPanel()
    {
        if (scriptPanel != null)
        {
            SetScriptPanelVisible(
                !scriptPanel.activeSelf
            );
        }
    }

    private void SetScriptPanelVisible(
        bool visible)
    {
        if (scriptPanel != null)
            scriptPanel.SetActive(visible);

        if (scriptButtonText == null)
        {
            GameObject scriptButton =
                GameObject.Find("Btn_Script");

            if (scriptButton != null)
            {
                scriptButtonText =
                    scriptButton
                        .GetComponentInChildren<
                            TextMeshProUGUI>(true);
            }
        }

        if (scriptButtonText != null)
        {
            scriptButtonText.text =
                visible ? "OFF" : "ON";
        }
    }

    private void UpdateTimerWarning()
    {
        if (timerText == null)
            return;

        bool shouldBlink =
            !isQAPhaseStarted &&
            (
                isTimerFinished ||
                timeRemaining <=
                warningStartSeconds
            );

        if (!shouldBlink)
        {
            timerText.color =
                normalTimerColor;

            return;
        }

        float blink =
            (
                Mathf.Sin(
                    Time.unscaledTime *
                    blinkSpeed *
                    Mathf.PI *
                    2f
                ) + 1f
            ) * 0.5f;

        timerText.color =
            Color.Lerp(
                normalTimerColor,
                warningColor,
                blink
            );
    }

    public void SkipPresentation()
    {
        timeRemaining = 10f;
    }

    private async void OnSlideChanged(
        int slideIndex)
    {
        if (flowController == null ||
            flowController.State !=
            PresentationFlowState.Running)
        {
            return;
        }

        try
        {
            await flowController.FlushSegmentAsync(
                "slide_transition",
                slideIndex,
                lifetimeCancellation.Token
            );
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                "[EVC] 슬라이드 전환 음성 전송 실패: " +
                exception.Message
            );
        }
    }

    private void OnDestroy()
    {
        if (presentationManager != null)
        {
            presentationManager.SlideChanged -=
                OnSlideChanged;
        }

        lifetimeCancellation?.Cancel();
        lifetimeCancellation?.Dispose();
    }
}