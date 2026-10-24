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
    [Header("뒤쪽 상단 문구 크기")]
    [Tooltip("발표 시작 전 문구의 최대 글자 크기입니다. 공간이 부족하면 자동으로 줄어듭니다.")]
    [SerializeField, Min(1f)] private float startPromptFontSize = 220f;
    [SerializeField, Min(1f)] private float timerFontSize = 400f;
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
    private Color warningColor = new Color(0.72f, 0f, 0f, 1f);


    private Color normalTimerColor = Color.white;

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
    private float warningStopAt = -1f;

    private CancellationTokenSource lifetimeCancellation;

    public bool IsPaused => isPaused;
    public PresentationSessionReady sessionReady;
    private GameObject actionConfirmation;
    private Action confirmedAction;
    private bool actionConfirmed;
    private string startError;
    private GameObject startErrorPanel;
    private bool warningAnnounced;
    public bool IsConfirmingSession => (sessionReady && sessionReady.IsOpen) ||
        (actionConfirmation && actionConfirmation.activeSelf) ||
        (startErrorPanel && startErrorPanel.activeSelf);

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
            UpdateTimerDisplay();
        }

        SetScriptPanelVisible(false);

        if (pausePanel != null)
            pausePanel.SetActive(false);

        // The podium end button continues into Q&A; retire the old wall button.
        if (endPresentationButton)
        {
            if (qaButton && qaButton != endPresentationButton) qaButton.gameObject.SetActive(false);
            qaButton = endPresentationButton;
        }
        qaManager?.Prepare(qaButton);

        if (qaButton != null)
            qaButton.gameObject.SetActive(false);

        if (sessionReady && RuntimeSessionData.IsLoaded)
            sessionReady.gameObject.SetActive(true);

        RefreshSessionButtons();
    }

    private void RefreshSessionButtons()
    {
        if (!hasStarted) UpdateTimerDisplay();
        if (startPresentationButton)
        {
            startPresentationButton.gameObject.SetActive(!hasStarted && startError == null);
            startPresentationButton.interactable = !isStarting && !isPaused && !IsConfirmingSession;
            var label = startPresentationButton.GetComponentInChildren<TMP_Text>(true);
            if (label) label.text = isStarting ? "발표 준비 중…" : "발표 시작하기";
            RefreshStartError(label);
        }
        RefreshPauseFinishAction();
        if (endPresentationButton)
        {
            ConfigurePodiumQaButton();
            // Keep the primary finish action on the podium throughout the talk.
            endPresentationButton.gameObject.SetActive(hasStarted && (!hasEnded || qaButton == endPresentationButton));
            if (!hasEnded)
            {
                endPresentationButton.interactable = !isStarting && !isPaused && !IsConfirmingSession;
                var label = endPresentationButton.GetComponentInChildren<TMP_Text>(true);
                if (label) label.text = "발표 마치기";
            }
        }
    }

    private void RefreshStartError(TMP_Text unused)
    {
        bool visible = startError != null && !isStarting && !hasStarted;
        if (!visible)
        {
            if (startErrorPanel) startErrorPanel.SetActive(false);
            return;
        }
        if (!pausePanel) return;
        if (!startErrorPanel)
        {
            // Reuse the authored translucent VR canvas, including its material,
            // curvature and raycasters. Keep the action inside that same panel.
            startErrorPanel = Instantiate(pausePanel, pausePanel.transform.parent);
            RegisterModalGlass(startErrorPanel);
            ResizeModalGlass(startErrorPanel, 300f);
            foreach (var child in startErrorPanel.GetComponentsInChildren<Transform>(true))
                if (child.name == "Resume helper glass") child.gameObject.SetActive(false);
            startErrorPanel.name = "Connection error UI";
            var buttons = startErrorPanel.GetComponentsInChildren<Button>(true);
            TMP_Text heading = null, explanation = null;
            foreach (var text in startErrorPanel.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text.GetComponentInParent<Button>()) continue;
                if (RehearUiLocalization.Source(text).Contains("일시정지")) heading = text;
                else if (text.name == "Hint" && text.transform.parent.name == "Pause glass") explanation = text;
                else text.text = "";
            }
            if (heading)
            {
                heading.text = "시작 실패";
                PlaceConnectionElement(heading.rectTransform, -40f, 465f, 54f);
                heading.fontSize = 36f;
            }
            if (explanation)
            {
                explanation.text = startError;
                PlaceConnectionElement(explanation.rectTransform, -114f, 449f, 44f);
                explanation.fontSize = 24f;
                explanation.color = new Color32(29,36,48,255);
            }
            foreach (var image in startErrorPanel.GetComponentsInChildren<Image>(true))
                if (image.name == "pause-large") image.gameObject.SetActive(false);
            for (int i = 0; i < buttons.Length; i++)
            {
                var button = buttons[i];
                button.onClick = new Button.ButtonClickedEvent();
                button.gameObject.SetActive(i == 0);
                if (i != 0) continue;
                button.interactable = true;
                PlaceConnectionElement((RectTransform)button.transform, -200f, 449f, 56f);
                var material = new Material(button.targetGraphic.material);
                modalMaterials.Add(material);
                material.SetVector("_PanelSize",new Vector4(449,56,0,0));
                material.SetFloat("_UseBlur",0);material.SetFloat("_FillAlphaOffset",0);material.SetFloat("_BorderWidth",0);
                button.targetGraphic.material=material;button.targetGraphic.color=new Color32(0,51,255,255);
                var label = button.GetComponentInChildren<TMP_Text>(true);
                if (label) { label.text = "다시 시도"; CenterModalLabel(label); }
                foreach (var image in button.GetComponentsInChildren<Image>(true))
                    if (image != button.targetGraphic) image.gameObject.SetActive(false);
                button.onClick.AddListener(() =>
                {
                    startErrorPanel.SetActive(false);
                    GlobalButtonSound.Instance?.PlayClickSound();
                    BeginPresentation();
                });
            }
        }
        foreach (var text in startErrorPanel.GetComponentsInChildren<TMP_Text>(true))
            if (text.name == "Hint" && text.transform.parent.name == "Pause glass") text.text = startError;
        startErrorPanel.SetActive(true);
        RefreshModalGeometry(startErrorPanel);
        startErrorPanel.transform.SetAsLastSibling();
    }

    private static void PlaceConnectionElement(RectTransform rect, float top, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1f);
        rect.pivot = new Vector2(.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, top);
        rect.sizeDelta = new Vector2(width, height);
    }

    private static string DescribeStartFailure(Exception exception)
    {
        if (exception is EvcApiException api)
        {
            switch (api.Kind)
            {
                case EvcErrorKind.Network: return "연결을 확인해주세요.";
                case EvcErrorKind.Timeout: return "응답이 늦어요.";
                case EvcErrorKind.Unauthorized: return "세션 인증을 확인해주세요.";
                default: return "잠시 후 다시 시도해주세요.";
            }
        }
        if (exception.Message.Contains("마이크")) return "마이크 권한을 확인해주세요.";
        return "발표 설정을 확인해주세요.";
    }

    public async void BeginPresentation()
    {
        if (hasStarted || isStarting || isPaused || IsConfirmingSession || lifetimeCancellation == null) return;
        isStarting = true;
        startError = null;
        RefreshSessionButtons();
        if (flowController == null)
        {
            hasStarted = isRunning = true;
            SetScriptPanelVisible(true);
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
                SetScriptPanelVisible(true);
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
            startError = DescribeStartFailure(exception);

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
            if (!hasStarted) startError ??= "잠시 후 다시 시도해주세요.";
            if (this) RefreshSessionButtons();
        }
    }

    public async void EndPresentation()
    {
        if (!hasStarted || isStarting || isPaused || IsConfirmingSession) return;
        bool hasQuestions = RuntimeSessionData.QaCount > 0;
        if (!hasEnded && !ConfirmAction(hasQuestions ? "Q&A로 넘어갈까요?" : "발표를 마칠까요?", "계속 발표", hasQuestions ? "Q&A 시작" : "발표 마치기", EndPresentation)) return;
        if (hasEnded)
        {
            OnActionButtonClick();
            return;
        }
        isStarting = true;
        hasEnded = true;
        isRunning = false;
        isPaused = false;
        qaManager?.SetPaused(false);
        SetScriptPanelVisible(false);
        StopTimerAudio();
        if (pausePanel) pausePanel.SetActive(false);
        RefreshSessionButtons();
        if (qaButton)
        {
            qaButton.interactable = false;
            var label = qaButton.GetComponentInChildren<TMP_Text>(true);
            if (label) label.text = "발표 종료 중…";
        }
        try
        {
            if (flowController != null)
                await flowController.PauseAsync(presentationManager ? presentationManager.CurrentSlideIndex : 0,
                    lifetimeCancellation.Token);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception exception) { Debug.LogWarning("발표 종료 처리: " + exception.Message); }
        finally { isStarting = false; }
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
        if (!hasStarted || isPaused || isStarting) return;
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
        if (warningStopAt >= 0f && Time.unscaledTime >= warningStopAt)
            StopWarningSound();

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
            null, // Periodic capture is not necessarily a sentence boundary.
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
        {
            timerText.enableAutoSizing = true;
            timerText.fontSizeMax = !hasStarted ? startPromptFontSize : timerFontSize;
            timerText.text =
                !hasStarted ? "발표 시작하기" :
                qaManager != null && qaManager.IsQAPhaseActive ? "Q&A" :
                isTimerFinished ? "+" + FormatTime(timeRemaining) : FormatTime(timeRemaining);
            UpdateTimerWarning();
        }
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

        // Reaching the planned duration starts overtime. Only the explicit end
        // action stops recording and selects Q&A or feedback, including zero Q&A.
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
        if (isStarting || !isPaused || IsConfirmingSession) return;
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
                ShowResumeError();

                return;
            }

            if (flowController.State !=
                PresentationFlowState.Running)
            {
                ShowResumeError();
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

    // Reuse the pause canvas so the confirmation retains the scene's VR raycasters,
    // font, panel materials and world-space placement without Inspector setup.
    private bool ConfirmAction(string title, string cancelLabel, string confirmLabel, Action action, Action onCancel = null)
    {
        if (actionConfirmed) { actionConfirmed = false; return true; }
        if (IsConfirmingSession || !pausePanel) return false;
        if (actionConfirmation) { if (Application.isPlaying) Destroy(actionConfirmation); else DestroyImmediate(actionConfirmation); }
        actionConfirmation = Instantiate(pausePanel, pausePanel.transform.parent);
        ResizeModalGlass(actionConfirmation, 320f);
        RegisterModalGlass(actionConfirmation);
        actionConfirmation.name = "Action confirmation";

        foreach (var child in actionConfirmation.GetComponentsInChildren<Transform>(true))
            if (child.name == "Resume helper glass" || child.name == "pause-large") child.gameObject.SetActive(false);
        var buttons = actionConfirmation.GetComponentsInChildren<Button>(true);
        if (buttons.Length < 2)
        {
            Destroy(actionConfirmation);
            Debug.LogError("Confirmation requires two pause-panel buttons.");
            return false;
        }
        foreach (var text in actionConfirmation.GetComponentsInChildren<TMP_Text>(true))
        {
            if (text.GetComponentInParent<Button>()) continue;
            text.text = RehearUiLocalization.Source(text).Contains("일시정지") ? title : "";
            if (text.text == title)
            {
                PlaceConnectionElement(text.rectTransform, -44f, 465f, 60f);
                text.fontSize = 34f;
            }
        }
        for (int i = 0; i < buttons.Length; i++)
        {
            var button = buttons[i];
            button.onClick = new Button.ButtonClickedEvent();
            button.gameObject.SetActive(i < 2);
            if (i >= 2) continue;
            PlaceConnectionElement((RectTransform)button.transform, i == 0 ? -128f : -208f, 449f, 60f);
            button.interactable = true;
            var label = button.GetComponentInChildren<TMP_Text>(true);
            if (label) label.text = i == 0 ? cancelLabel : confirmLabel;
            if (label) CenterModalLabel(label);
            var material = new Material(button.targetGraphic.material);
            modalMaterials.Add(material);
            material.SetVector("_PanelSize",new Vector4(449,60,0,0));
            material.SetFloat("_UseBlur",0);
            material.SetFloat("_FillAlphaOffset",0);
            material.SetFloat("_BorderWidth",0);
            material.SetColor("_BorderColor",Color.white);
            button.targetGraphic.material=material;
            button.targetGraphic.color=i==0?new Color32(0,51,255,255):new Color32(92,96,105,255);
            var foreground=Color.white;
            if(label)label.color=foreground;
            foreach(var icon in button.GetComponentsInChildren<Image>(true))
                if(icon!=button.targetGraphic)icon.gameObject.SetActive(false);
            button.onClick.AddListener(() => GlobalButtonSound.Instance?.PlayClickSound());
        }
        confirmedAction = action;
        buttons[0].onClick.AddListener(() => { CancelActionConfirmation(); onCancel?.Invoke(); });
        buttons[1].onClick.AddListener(() =>
        {
            var pending = confirmedAction;
            CancelActionConfirmation();
            actionConfirmed = true;
            pending?.Invoke();
            actionConfirmed = false;
        });
        if (pausePanel.activeSelf) pausePanel.SetActive(false);
        actionConfirmation.SetActive(true);
        RefreshModalGeometry(actionConfirmation);
        actionConfirmation.transform.SetAsLastSibling();
        RefreshSessionButtons();
        return false;
    }

    private readonly System.Collections.Generic.List<Material> modalMaterials = new();

    private static void CenterModalLabel(TMP_Text label)
    {
        label.rectTransform.anchorMin=Vector2.zero;label.rectTransform.anchorMax=Vector2.one;
        label.rectTransform.offsetMin=label.rectTransform.offsetMax=Vector2.zero;
        label.alignment=TextAlignmentOptions.Center;label.color=Color.white;
    }

    private static void RefreshModalGeometry(GameObject panel)
    {
        foreach (var rect in panel.GetComponentsInChildren<RectTransform>(true)) rect.ForceUpdateRectTransforms();
        foreach (var graphic in panel.GetComponentsInChildren<Graphic>(true)) graphic.SetAllDirty();
        foreach (var text in panel.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate(true, true);
        foreach (var effect in panel.GetComponentsInChildren<CurvedUI.CurvedUIVertexEffect>(true)) effect.SetDirty();
        foreach (var text in panel.GetComponentsInChildren<CurvedUI.Core.Integrations.CurvedUITMP>(true)) text.Dirty = true;
        Canvas.ForceUpdateCanvases();
        foreach (var graphic in panel.GetComponentsInChildren<Graphic>())
        {
            graphic.canvasRenderer.cull = false;
            graphic.canvasRenderer.Clear();
            graphic.SetAllDirty();
            graphic.Rebuild(CanvasUpdate.PreRender);
        }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
        UnityEditor.SceneView.RepaintAll();
#endif
    }

    private Button pauseFinishButton;
    private Material pauseFinishMaterial;
    private Material pauseRestartMaterial;

    private void RefreshPauseFinishAction()
    {
        if (!pausePanel) return;
        if (!pauseFinishButton)
        {
            foreach (var button in pausePanel.GetComponentsInChildren<Button>(true))
                if (button.name == "Stop") { pauseFinishButton = button; break; }
            if (!pauseFinishButton) return;
            pauseFinishButton.onClick = new Button.ButtonClickedEvent();
            pauseFinishButton.onClick.AddListener(StopSession);
        }
        var label = pauseFinishButton.GetComponentInChildren<TMP_Text>(true);
        if (label) { label.text = "세션 종료"; CenterModalLabel(label); }
        foreach (var icon in pauseFinishButton.GetComponentsInChildren<Image>(true))
            if (icon != pauseFinishButton.targetGraphic) icon.gameObject.SetActive(false);
        if (pauseFinishButton.targetGraphic)
        {
            if (!pauseFinishMaterial)
            {
                pauseFinishMaterial = new Material(endPresentationButton ? endPresentationButton.targetGraphic.material : pauseFinishButton.targetGraphic.material);
                modalMaterials.Add(pauseFinishMaterial);
                var size = ((RectTransform)pauseFinishButton.transform).rect.size;
                pauseFinishMaterial.SetVector("_PanelSize", new Vector4(size.x,size.y,0,0));
                pauseFinishMaterial.SetFloat("_Radius",size.y*.5f);
                pauseFinishMaterial.SetFloat("_UseBlur",0);pauseFinishMaterial.SetFloat("_BorderWidth",0);
                pauseFinishMaterial.SetFloat("_FillAlphaOffset",0);
            }
            pauseFinishButton.targetGraphic.material = pauseFinishMaterial;
            pauseFinishButton.targetGraphic.color = new Color32(92,96,105,255);
            if (pauseFinishButton.targetGraphic is Image face) { face.sprite=null;face.type=Image.Type.Simple; }
        }
        // Separate the occasional reset from finishing, using outline vs fill and a larger gap.
        var finishRect = (RectTransform)pauseFinishButton.transform;
        finishRect.anchoredPosition = new Vector2(finishRect.anchoredPosition.x, -351f);
        foreach (var restart in pausePanel.GetComponentsInChildren<Button>(true))
        {
            if (restart.name != "Restart" || !restart.targetGraphic) continue;
            if (!pauseRestartMaterial)
            {
                pauseRestartMaterial = new Material(pauseFinishMaterial);
                modalMaterials.Add(pauseRestartMaterial);
                var size = ((RectTransform)restart.transform).rect.size;
                pauseRestartMaterial.SetVector("_PanelSize", new Vector4(size.x,size.y,0,0));
                pauseRestartMaterial.SetFloat("_FillAlphaOffset",1f);
                pauseRestartMaterial.SetFloat("_BorderWidth",2f);
                pauseRestartMaterial.SetColor("_BorderColor",((Color)new Color32(53,56,65,255)).linear);
            }
            restart.targetGraphic.material=pauseRestartMaterial;
            restart.targetGraphic.color=Color.white;
            if (restart.targetGraphic is Image restartFace) { restartFace.sprite=null;restartFace.type=Image.Type.Simple; }
            foreach (var text in restart.GetComponentsInChildren<TMP_Text>(true))text.color=new Color32(53,56,65,255);
            foreach (var icon in restart.GetComponentsInChildren<Image>(true))
                if(icon!=restart.targetGraphic)icon.color=new Color32(53,56,65,255);
        }
        foreach (var text in pausePanel.GetComponentsInChildren<TMP_Text>(true))
            if (!text.GetComponentInParent<Button>() && RehearUiLocalization.Source(text).StartsWith("그립 버튼"))
                text.text = "그립 버튼으로 계속 발표";
        foreach (var child in pausePanel.GetComponentsInChildren<Transform>(true))
            if (child.name=="Resume helper glass")child.gameObject.SetActive(false);
        pauseFinishButton.interactable = !isStarting && !IsConfirmingSession;
    }

    public void FinishPresentationOrSession()
    {
        // Retain existing scene UnityEvents; this pause-menu route exits the session.
        StopSession();
    }

    private void ConfigurePodiumQaButton()
    {

        if (!presentationManager || !presentationManager.deskScreen) return;
        var toggle = FindFirstObjectByType<PodiumScriptToggle>();
        if (!toggle || !toggle.label) return;
        var toggleButton = toggle.label.GetComponentInParent<Button>();
        if (!toggleButton) return;
        var toggleRect = (RectTransform)toggleButton.transform;
        var rect = (RectTransform)endPresentationButton.transform;
        if (rect.parent != toggleRect.parent) rect.SetParent(toggleRect.parent, false);
        rect.rotation = presentationManager.deskScreen.transform.rotation;
        rect.localScale = toggleRect.localScale;
        var actionSize = toggleRect.rect.size;
        rect.sizeDelta = actionSize;
        var slide = presentationManager.deskScreen.rectTransform;
        var slideCorners = new Vector3[4]; slide.GetWorldCorners(slideCorners);
        // Keep finish above the slide's upper-left edge, away from script visibility.
        var buttonCorners = new Vector3[4]; rect.GetWorldCorners(buttonCorners);
        rect.position += slideCorners[1] - buttonCorners[0] + slide.up * .028f;
        var legacyDock = toggleRect.Find("Action dock extension");
        if (legacyDock) legacyDock.gameObject.SetActive(false);
        var canvas = rect.GetComponent<Canvas>();
        if(!canvas)canvas=rect.gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = toggleRect.GetComponent<Canvas>().worldCamera;
        var curve = rect.GetComponent<CurvedUI.CurvedUISettings>();
        if (!curve)
        {
            curve = rect.gameObject.AddComponent<CurvedUI.CurvedUISettings>();
            curve.Angle = 1; curve.Interactable = curve.BlocksRaycasts = true;
            rect.gameObject.AddComponent<CurvedUI.CurvedUIRaycaster>();
            curve.AddEffectToChildren();
        }
        var label = endPresentationButton.GetComponentInChildren<TMP_Text>(true);
        if (label) {
            label.font = toggle.label.font;
            label.fontSharedMaterial = toggle.label.fontSharedMaterial;
            label.fontStyle = toggle.label.fontStyle;
            label.fontWeight = toggle.label.fontWeight;
            label.fontSize = toggle.label.fontSize;
            label.enableAutoSizing=false;
            label.fontSizeMin=label.fontSizeMax=toggle.label.fontSize;
            label.textWrappingMode=TextWrappingModes.NoWrap;CenterModalLabel(label);
        }
        if (endPresentationButton.targetGraphic)
        {
            if (endPresentationButton.targetGraphic is Image endImage)
            {
                endImage.sprite = null;
                endImage.type = Image.Type.Simple;
                endImage.pixelsPerUnitMultiplier = 1f;
            }
            endPresentationButton.targetGraphic.color = new Color32(92,96,105,255);
            if (!podiumEndMaterial)
            {
                podiumEndMaterial = new Material(toggle.background ? toggle.background.material : toggleButton.targetGraphic.material);
                modalMaterials.Add(podiumEndMaterial);
                podiumEndMaterial.SetVector("_PanelSize", new Vector4(actionSize.x,actionSize.y,0,0));
                podiumEndMaterial.SetFloat("_Radius",actionSize.y*.5f);
                podiumEndMaterial.SetFloat("_UseBlur",0);podiumEndMaterial.SetFloat("_BorderWidth",0);
                podiumEndMaterial.SetFloat("_FillAlphaOffset",0);
            }
            endPresentationButton.targetGraphic.material=podiumEndMaterial;
        }
        // The floating finish action needs no opaque 3D backing.
        var finishMount = rect.Find("Finish action mount");
        if (finishMount) finishMount.gameObject.SetActive(false);
    }
    private Material podiumEndMaterial;

    private void ResizeModalGlass(GameObject panel, float height)
    {
        foreach (var image in panel.GetComponentsInChildren<Image>(true))
        {
            if (image.name != "Pause glass") continue;
            var rect=image.rectTransform;
            rect.anchoredPosition-=Vector2.up*(rect.rect.height-height)*.5f;
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,height);
            var material=new Material(image.material);
            modalMaterials.Add(material);
            material.SetVector("_PanelSize",new Vector4(rect.rect.width,height,0,0));
            image.material=material;
            image.SetAllDirty();
            break;
        }
    }

    private void RegisterModalGlass(GameObject panel)
    {
        foreach (var glass in panel.GetComponentsInChildren<LeTai.Asset.TranslucentImage.TranslucentImage>(true))
        {
            if (!glass.source) continue;
            var sync = glass.source.GetComponent<TutorialBlurCameraSync>();
            if (sync) sync.RegisterPanels(panel);
            break;
        }
    }

    private void CancelActionConfirmation()
    {
        if (actionConfirmation) actionConfirmation.SetActive(false);
        confirmedAction = null;
        if (isPaused && pausePanel) pausePanel.SetActive(true);
        RefreshSessionButtons();
    }

    private void ShowResumeError()
    {
        if (!pausePanel) return;
        foreach (var text in pausePanel.GetComponentsInChildren<TMP_Text>(true))
        {
            if (!text.GetComponentInParent<Button>() && (RehearUiLocalization.Source(text).Contains("여기에서") || RehearUiLocalization.Source(text).Contains("그립 버튼")))
                text.text = "연결 실패\n그립 버튼으로 다시 시도";
        }
    }

    public void RestartSession()
    {
        if (!ConfirmAction("처음부터 다시 할까요?", "취소", "다시 시작", RestartSession)) return;
        flowController?.StopFlow();

        SceneManager.LoadScene(
            SceneManager.GetActiveScene().name
        );
    }

    public void StopSession()
    {
        if (!ConfirmAction("세션을 종료할까요?", "취소", "세션 종료", StopSession)) return;
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
        SetScriptPanelVisible(false);
        qaManager?.StartQAPhase(qaButton);
        UpdateTimerDisplay();
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
                visible ? "대본 숨기기" : "대본 보기";
        }
    }

    private void UpdateTimerWarning()
    {
        if (timerText == null)
            return;

        bool shouldWarn =
            hasStarted && !hasEnded && !isQAPhaseStarted && !(qaManager != null && qaManager.IsQAPhaseActive) &&
            (
                isTimerFinished ||
                timeRemaining <=
                warningStartSeconds
            );

        if (!shouldWarn)
        {
            timerText.color =
                normalTimerColor;

            return;
        }

        var color = warningColor;
        if (isTimerFinished && Mathf.Repeat(Time.unscaledTime, 1f) >= 0.65f)
            color.a = 0f;
        timerText.color = color;
    }

    private void UpdateTimerAudio()
    {
        if (warningAnnounced ||
            timerAudioSource == null ||
            timeWarningClip == null ||
            isTimerFinished ||
            timeRemaining <= 0f ||
            timeRemaining > warningStartSeconds)
        {
            return;
        }

        timerAudioSource.Stop();
        timerAudioSource.clip = null;
        timerAudioSource.loop = false;
        timerAudioSource.volume = 1f;
        timerAudioSource.PlayOneShot(timeWarningClip, warningSoundVolume);
        warningAnnounced = true;
        warningStopAt = Time.unscaledTime + .8f;
    }

    private void StopWarningSound()
    {
        if (timerAudioSource == null)
            return;

        timerAudioSource.Stop();
        timerAudioSource.loop = false;
        timerAudioSource.clip = null;
        warningStopAt = -1f;
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

        warningStopAt = -1f;
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
        foreach (var material in modalMaterials)
            if (material) { if (Application.isPlaying) Destroy(material); else DestroyImmediate(material); }
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
