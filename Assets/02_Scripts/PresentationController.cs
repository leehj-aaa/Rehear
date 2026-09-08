using System;
using System.Threading;
using Rehear.Evc.Presentation;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
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
    public Button startPresentationButton;
    public Button endPresentationButton;

    [SerializeField]
    private TextMeshProUGUI scriptButtonText;

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
    [Min(0f)]
    private float warningStartSeconds = 30f;

    [SerializeField]
    [FormerlySerializedAs("sessionAudioSource")]
    private AudioSource timerAudioSource;

    [SerializeField]
    private AudioClip timeWarningClip;

    [SerializeField]
    private AudioClip timeUpClip;

    [SerializeField]
    [Range(0f, 1f)]
    private float warningSoundVolume = 0.35f;

    [SerializeField]
    [Range(0f, 1f)]
    private float timeUpSoundVolume = 1f;

    [Header("EVC 자동 음성 전송")]
    [SerializeField]
    [Min(3f)]
    private float evcSegmentIntervalSeconds = 8f;

    private float nextEvcSegmentTime;

    [SerializeField]
    private Color warningColor = Color.red;

    [SerializeField]
    private float blinkSpeed = 1f;

    private Color normalTimerColor;

    private float timeRemaining = 60f;
    private bool isRunning;
    private bool hasStarted;
    private bool hasEnded;
    private bool isStarting;
    private bool isPaused;
    private bool wasRunningBeforePause;
    private bool isTimerFinished;
    private bool isQAPhaseStarted;
    private bool isGeneratingQuestions;
    private bool periodicFlushInProgress;
    private bool isWarningSoundPlaying;

    private CancellationTokenSource lifetimeCancellation;

    public bool IsPaused => isPaused;
    public PresentationSessionReady sessionReady;
    public bool IsConfirmingSession => sessionReady && sessionReady.IsOpen;

    public void SetSessionConfirmationVisible(bool visible) => RefreshSessionButtons();

    private void Start()
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

        if (sessionReady && RuntimeSessionData.IsLoaded)
            sessionReady.gameObject.SetActive(true);

        RefreshSessionButtons();
    }

    private void RefreshSessionButtons()
    {
        if (startPresentationButton)
        {
            startPresentationButton.gameObject.SetActive(!hasStarted);
            startPresentationButton.interactable = !isStarting && !isPaused && !IsConfirmingSession;
            var label = startPresentationButton.GetComponentInChildren<TMP_Text>(true);
            if (label) label.text = isStarting ? "발표 준비 중…" : "발표 시작하기";
        }
        if (endPresentationButton)
        {
            endPresentationButton.gameObject.SetActive(hasStarted && !hasEnded);
            endPresentationButton.interactable = !isStarting && !isPaused;
        }
    }

    public async void BeginPresentation()
    {
        if (hasStarted || isStarting || isPaused || IsConfirmingSession || lifetimeCancellation == null) return;
        isStarting = true;
        RefreshSessionButtons();
        if (flowController == null)
        {
            hasStarted = isRunning = true;
            isStarting = false;
            RefreshSessionButtons();
            return;
        }

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
                hasStarted = true;
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
        finally
        {
            isStarting = false;
            if (this) RefreshSessionButtons();
        }
    }

    public async void EndPresentation()
    {
        if (!hasStarted || hasEnded || isStarting || isPaused) return;
        hasEnded = true;
        isRunning = false;
        isPaused = false;
        StopTimerAudio();
        if (pausePanel) pausePanel.SetActive(false);
        RefreshSessionButtons();
        try
        {
            if (flowController != null)
                await flowController.PauseAsync(presentationManager ? presentationManager.CurrentSlideIndex : 0,
                    lifetimeCancellation.Token);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception exception) { Debug.LogWarning("발표 종료 처리: " + exception.Message); }
        if (!this) return;
        if (qaButton) qaButton.gameObject.SetActive(true);
        if (RuntimeSessionData.QaCount <= 0)
        {
            isQAPhaseStarted = true;
            flowController?.PrepareFinishWithoutQuestions();
            qaManager?.PrepareFinishWithoutQuestions(qaButton);
            // The end button also confirms completion when no Q&A was selected.
            qaManager?.OnActionButtonClick();
        }
        else OnActionButtonClick();
    }

    public async void OnActionButtonClick()
    {
        if (!hasStarted || isPaused) return;
        if (isGeneratingQuestions)
            return;
        hasEnded = true;
        isRunning = false;
        RefreshSessionButtons();

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
            StopTimerAudio();

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

        StopTimerAudio();
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

        if (qaManager != null && qaManager.IsQAPhaseActive)
        {
            UpdateTimerDisplay();
            return;
        }

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
            UpdateTimerAudio();

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
            PresentationFlowState.Running ||
        periodicFlushInProgress)
    {
        return;
    }

    periodicFlushInProgress = true;

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
    finally
    {
        periodicFlushInProgress = false;
    }
}


    private void UpdateTimerDisplay()
    {
        if (timerText != null)
            timerText.text =
                qaManager != null && qaManager.IsQAPhaseActive ? "Q&A" : FormatTime(timeRemaining);
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
        timeRemaining = 0f;
        isTimerFinished = true;
        StopWarningSound();
        PlayTimeUpSound();

        if (qaButton != null)
            qaButton.gameObject.SetActive(true);

        if (RuntimeSessionData.QaCount <= 0)
        {
            isRunning = false;
            isQAPhaseStarted = true;
            flowController?.PrepareFinishWithoutQuestions();
            qaManager?.PrepareFinishWithoutQuestions(qaButton);

            Debug.Log(
                "[Q&A] 설정된 질문이 없어 발표 종료 버튼으로 전환합니다."
            );
        }
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
        if (isStarting || isPaused || IsConfirmingSession) return;
        wasRunningBeforePause = isRunning;
        isPaused = true;
        qaManager?.SetPaused(true);
        isRunning = false;
        RefreshSessionButtons();

        if (pausePanel != null)
        {
            pausePanel.SetActive(true);
            pausePanel.transform.SetAsLastSibling();
        }

        timerAudioSource?.Pause();

        if (flowController == null || !hasStarted || hasEnded)
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
        if (isStarting || !isPaused) return;
        if (flowController != null && hasStarted && !hasEnded)
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

        isRunning = wasRunningBeforePause && !hasEnded;
        isPaused = false;
        qaManager?.SetPaused(false);
        RefreshSessionButtons();

        if (pausePanel != null)
            pausePanel.SetActive(false);

        timerAudioSource?.UnPause();
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
            "Scene_00"
        );
    }

    public void StartQA()
    {
        StopTimerAudio();
        qaManager?.StartQAPhase(qaButton);
        UpdateTimerDisplay();
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
            hasStarted && !hasEnded && !isQAPhaseStarted && !(qaManager != null && qaManager.IsQAPhaseActive) &&
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

        float blinkTime =
            isWarningSoundPlaying &&
            timerAudioSource != null
                ? timerAudioSource.time
                : Time.unscaledTime;

        float blink =
            (
                Mathf.Cos(
                    blinkTime *
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

    private void UpdateTimerAudio()
    {
        if (isWarningSoundPlaying ||
            timerAudioSource == null ||
            timeWarningClip == null ||
            isTimerFinished ||
            timeRemaining <= 0f ||
            timeRemaining > warningStartSeconds)
        {
            return;
        }

        timerAudioSource.Stop();
        timerAudioSource.clip = timeWarningClip;
        timerAudioSource.loop = true;
        timerAudioSource.volume = warningSoundVolume;
        timerAudioSource.Play();
        isWarningSoundPlaying = true;
    }

    private void StopWarningSound()
    {
        if (timerAudioSource == null)
            return;

        timerAudioSource.Stop();
        timerAudioSource.loop = false;
        timerAudioSource.clip = null;
        isWarningSoundPlaying = false;
    }

    private void PlayTimeUpSound()
    {
        if (timerAudioSource == null ||
            timeUpClip == null)
        {
            return;
        }

        timerAudioSource.volume = 1f;
        timerAudioSource.PlayOneShot(
            timeUpClip,
            timeUpSoundVolume
        );
    }

    private void StopTimerAudio()
    {
        if (timerAudioSource != null)
        {
            timerAudioSource.Stop();
            timerAudioSource.loop = false;
            timerAudioSource.clip = null;
        }

        isWarningSoundPlaying = false;
    }

    public void SkipPresentation()
    {
        timeRemaining = 10f;
    }

    private void OnSlideChanged(
        int slideIndex)
    {
        // 슬라이드 전환은 현재 슬라이드 상태만 바꾼다.
        // 음성은 8초 주기 전송 한 경로에서만 분할한다.
    }

    private void OnDestroy()
    {
        StopTimerAudio();

        if (presentationManager != null)
        {
            presentationManager.SlideChanged -=
                OnSlideChanged;
        }

        lifetimeCancellation?.Cancel();
        lifetimeCancellation?.Dispose();
    }
}
