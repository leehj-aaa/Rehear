using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Collections.Generic;
using Rehear.Evc.Contracts;
using System;
using System.Threading;
using Rehear.Evc.Presentation;

public class QuestionAnswerManager : MonoBehaviour
{
    [Header("AI 세션")]
    [Header("EVC 리포트")]
    [SerializeField]
    private PresentationFlowController flowController;

    private float qaStartedTime;

    private bool isFinishingPresentation;
    private bool finishWithoutQuestions;
    private int? finishPlannedSeconds;
    private int? finishQaSeconds;


    private enum QAState { ReadyToStart, PlayingQuestion, QuestionAudioFailed, ReadyToAnswer, Answering, ReadyForNext, ReadyToFinish }

    public GameObject qaPanel;
    public TMP_Text questionText;
    public TMP_Text answerGuideText;
    public AudioSource audioSource;
    public AudioClip[] questionAudios;
    [TextArea] public string[] questionContents;

    private AudienceQuestionSpeaker currentSpeaker;
    private readonly Dictionary<int,string> questionSpeakerIds = new Dictionary<int,string>();
    private int currentIdx;
    private int questionCount;
    private Button actionButton;
    private TMP_Text actionButtonText;
    private TMP_Text questionProgressText;
    private QAState state = QAState.ReadyToStart;
    private Coroutine audioWaitCoroutine;
        [SerializeField] private Sprite questionOutlineSprite;
    [SerializeField] private Sprite answerFillSprite;
    [SerializeField, Min(0.2f)] private float answerClickLockSeconds = 1f;
    private float answerUnlockTime;
    private bool isPaused;

    public void SetPaused(bool paused)
    {
        if (isPaused == paused) return;
        isPaused = paused;
        currentSpeaker?.SetPaused(paused);
        if (actionButton) actionButton.interactable = !paused &&
            state != QAState.PlayingQuestion && !isFinishingPresentation &&
            (state != QAState.Answering || Time.unscaledTime >= answerUnlockTime);
    }

    public bool IsQAPhaseActive { get; private set; }

    public void Prepare(Button button)
    {
        IsQAPhaseActive = false;
        actionButton = button;
        actionButtonText = actionButton != null ? actionButton.GetComponentInChildren<TMP_Text>(true) : null;
        currentIdx = 0;
        state = QAState.ReadyToStart;
        finishWithoutQuestions = false;
        isFinishingPresentation = false;
        finishPlannedSeconds = finishQaSeconds = null;

        if (qaPanel != null) qaPanel.SetActive(false);
        if (answerGuideText != null) answerGuideText.text = string.Empty;
        SetButtonState("질의응답하기", true);
    }

    public void PrepareFinishWithoutQuestions(Button button)
    {
        IsQAPhaseActive = false;
        actionButton = button;
        actionButtonText = actionButton != null
            ? actionButton.GetComponentInChildren<TMP_Text>(true)
            : null;
        qaStartedTime = Time.realtimeSinceStartup;
        finishWithoutQuestions = true;
        questionCount = 0;
        currentIdx = 0;
        state = QAState.ReadyToFinish;

        if (qaPanel != null)
            qaPanel.SetActive(false);

        if (questionText != null)
            questionText.text = string.Empty;

        if (answerGuideText != null)
            answerGuideText.text = string.Empty;

        SetButtonState("발표 종료하기", true);
    }

    public void StartQAPhase(Button button)
    {
        qaStartedTime = Time.realtimeSinceStartup;

        if (actionButton == null) Prepare(button);

        questionCount =
            questionContents != null
                ? questionContents.Length
                : 0;

        if (questionCount == 0)
        {
           Debug.LogError("질문 텍스트를 한 개 이상 연결해야 합니다.");
            return;
        }

        currentIdx = 0;
        IsQAPhaseActive = true;
        if (qaPanel != null) qaPanel.SetActive(true);
        PlayCurrentQuestion();
    }

    public void OnActionButtonClick()
    {
        if (isPaused || isFinishingPresentation) return;
        switch (state)
        {
            case QAState.ReadyToStart:
                StartQAPhase(actionButton);
                break;
            case QAState.PlayingQuestion:
                break;
            case QAState.QuestionAudioFailed:
                PlayCurrentQuestion();
                break;
            case QAState.ReadyToAnswer:
                BeginAnswer();
                break;
            case QAState.Answering:
                if (Time.unscaledTime < answerUnlockTime) return;
                CompleteAnswerStep();
                break;
            case QAState.ReadyForNext:
                currentIdx++;
                PlayCurrentQuestion();
                break;
            case QAState.ReadyToFinish:
                FinishPresentation();
                break;
        }
    }

    private void PlayCurrentQuestion()
    {
        if (currentIdx < 0 || currentIdx >= questionCount) return;

        state = QAState.PlayingQuestion;
        if (answerGuideText != null) answerGuideText.text = string.Empty;
        if (questionText != null) questionText.text = questionContents[currentIdx];
        SetButtonState("질문하는 중...", false);

       AudioClip clip =
            questionAudios != null &&
            currentIdx < questionAudios.Length
                ? questionAudios[currentIdx]
                : null;

        if (currentSpeaker) currentSpeaker.StopSpeech();
        if (audioSource) audioSource.Stop();
        var agents = FindObjectsByType<Rehear.Evc.Audience.AudienceAgent>(FindObjectsSortMode.None);
        Array.Sort(agents, (a,b) => string.CompareOrdinal(a.AgentId,b.AgentId));
        Rehear.Evc.Audience.AudienceAgent selected = null;
        if (questionSpeakerIds.TryGetValue(currentIdx,out var speakerId))
            selected = Array.Find(agents, a => a.AgentId == speakerId);
        else if (agents.Length > 0) selected = agents[currentIdx % agents.Length];
        currentSpeaker = selected ? selected.GetComponent<AudienceQuestionSpeaker>() : null;
        if (selected && !currentSpeaker) currentSpeaker = selected.gameObject.AddComponent<AudienceQuestionSpeaker>();
        if (!clip || !currentSpeaker || !currentSpeaker.Play(clip))
        {
            state = QAState.QuestionAudioFailed;
            SetButtonState("질문 음성 다시 시도", true);
            if (questionText) { questionText.gameObject.SetActive(true); questionText.text = "질문 음성을 준비하지 못했습니다."; }
            Debug.LogWarning("[Q&A] 질문 음성/청중 연결을 확인하세요. 발화 완료로 처리하지 않습니다.");
            return;
        }
        if (questionText) questionText.gameObject.SetActive(false);
        if (audioWaitCoroutine != null) StopCoroutine(audioWaitCoroutine);
        audioWaitCoroutine = StartCoroutine(WaitForQuestionAudio());
    }

    private async void FinishPresentation()
{
    if (isFinishingPresentation)
        return;

    isFinishingPresentation = true;
    SetButtonState("리포트 생성 중...", false);

    if (audioSource != null)
        audioSource.Stop();

    if (flowController == null)
    {
        flowController =
            FindFirstObjectByType<PresentationFlowController>();
    }

    if (flowController == null)
    {
        Debug.LogError(
            "[EVC] PresentationFlowController를 찾을 수 없습니다."
        );

        isFinishingPresentation = false;
        SetButtonState("발표 종료하기", true);
        return;
    }

    int plannedSeconds = finishPlannedSeconds ?? (
        Mathf.Max(
            0,
            RuntimeSessionData.DurationMinutes * 60
        ));

    int qaSeconds = finishQaSeconds ?? (finishWithoutQuestions
        ? 0
        : Mathf.Max(
            0,
            Mathf.RoundToInt(
                Time.realtimeSinceStartup - qaStartedTime
            )
        ));
    finishPlannedSeconds = plannedSeconds;
    finishQaSeconds = qaSeconds;

    try
    {
        ReportFeedback report =
            await flowController.FinishReportAsync(
                plannedSeconds,
                qaSeconds,
                destroyCancellationToken
            );

        if (!this) return;
        RuntimeReportData.Set(report);
        LoadFeedbackScene();
    }
    catch (OperationCanceledException) { }
    catch (Exception exception)
    {
        Debug.LogError(
            "[EVC] AI 리포트 생성 실패: " +
            exception.Message
        );

        isFinishingPresentation = false;
        SetButtonState("발표 종료 다시 시도", true);
    }
}

private void LoadFeedbackScene()
{
    SceneManager.LoadScene(
        "Scene_03_Feedback"
    );
}

    private IEnumerator WaitForQuestionAudio()
    {
        yield return null;
        yield return new WaitWhile(() => AudioListener.pause || Time.timeScale == 0 || (currentSpeaker != null && currentSpeaker.IsSpeaking));
        audioWaitCoroutine = null;
        FinishQuestionAudio();
    }

    private void FinishQuestionAudio()
    {
        if (state != QAState.PlayingQuestion) return;
        state = QAState.ReadyToAnswer;
        SetButtonState("응답 시작하기", true);
    }

    private void BeginAnswer()
    {
        state = QAState.Answering;
        answerUnlockTime = Time.unscaledTime + Mathf.Max(.2f, answerClickLockSeconds);
        SetButtonState("응답 완료하기", false);

    }

    private void Update()
    {
        if (!isPaused && state == QAState.Answering && actionButton != null && !actionButton.interactable
            && Time.unscaledTime >= answerUnlockTime)
            SetButtonState("응답 완료하기", true);
    }
    private void CompleteAnswerStep()
    {
        // Completing an answer advances directly, without another confirmation button.
        if (currentIdx >= questionCount - 1)
        {
            state = QAState.ReadyToFinish;
            FinishPresentation();
        }
        else
        {
            currentIdx++;
            PlayCurrentQuestion();
        }
    }
    private void SetButtonState(string label, bool interactable)
    {
        UpdateQuestionProgress();
        if (actionButton != null) actionButton.interactable = interactable && !isPaused;
        if (actionButtonText != null) { actionButtonText.text = label; actionButtonText.color = Color.white; }
        if (actionButton != null && actionButton.targetGraphic is Image image)
        {
            bool speaking = state == QAState.PlayingQuestion;
            var sprite = speaking ? questionOutlineSprite : answerFillSprite;
            if (sprite) { image.sprite = sprite; image.overrideSprite = null; }
            image.material = null;
            image.type = Image.Type.Sliced;
            image.color = speaking ? Color.white : new Color32(0, 51, 255, 255);
            image.pixelsPerUnitMultiplier = speaking ? 1f : 128f / Mathf.Max(1f, image.rectTransform.rect.height);
            var colors = actionButton.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = Color.white;
            colors.selectedColor = Color.white;
            colors.pressedColor = new Color(.8f, .8f, .8f, 1);
            colors.disabledColor = Color.white;
            actionButton.colors = colors;
        }
    }
    private void UpdateQuestionProgress()
    {
        bool visible = IsQAPhaseActive && questionCount > 0 &&
            (state == QAState.PlayingQuestion || state == QAState.ReadyToAnswer ||
             state == QAState.Answering || state == QAState.ReadyForNext);
        if (visible && questionProgressText == null && actionButtonText != null)
        {
            // Copy the existing font/curved text setup so this follows the same canvas.
            questionProgressText = Instantiate(actionButtonText, actionButton.transform);
            questionProgressText.name = "Question Progress";
            questionProgressText.raycastTarget = false;
            questionProgressText.alignment = TextAlignmentOptions.Center;
            questionProgressText.enableAutoSizing = true;
            questionProgressText.fontSizeMax = actionButtonText.fontSize * .55f;
            questionProgressText.fontSizeMin = questionProgressText.fontSizeMax * .6f;
            questionProgressText.color = Color.white;
            var rect = questionProgressText.rectTransform;
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(.5f, 0);
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            rect.sizeDelta = new Vector2(0, ((RectTransform)actionButton.transform).rect.height * .5f);
            rect.anchoredPosition3D = new Vector3(0, 12, 0);
        }
        if (questionProgressText == null) return;
        questionProgressText.gameObject.SetActive(visible);
        if (visible)
            questionProgressText.text = $"질문 {currentIdx + 1} / {questionCount} · 남은 질문 {Mathf.Max(0, questionCount - currentIdx - 1)}개";
    }
    // The TTS provider supplies the clip and, when known, the evaluation's speaker ID.
    public void SetQuestionSpeech(int questionIndex, AudioClip clip, string audienceId = null)
    {
        if (questionContents == null || questionIndex < 0 || questionIndex >= questionContents.Length)
            throw new ArgumentOutOfRangeException(nameof(questionIndex));
        if (questionAudios == null || questionAudios.Length != questionContents.Length)
            Array.Resize(ref questionAudios, questionContents.Length);
        questionAudios[questionIndex] = clip;
        if (!string.IsNullOrWhiteSpace(audienceId)) questionSpeakerIds[questionIndex] = audienceId;
        else questionSpeakerIds.Remove(questionIndex);
        if (IsQAPhaseActive && currentIdx == questionIndex && state == QAState.QuestionAudioFailed && clip)
            PlayCurrentQuestion();
    }

    public void SetGeneratedQuestions(
        IReadOnlyList<GeneratedQuestion> questions)
    {
        IsQAPhaseActive = false;
        if (questions == null)
        {
            questionContents = new string[0];
        }
        else
        {
            questionContents =
                new string[questions.Count];

            for (int index = 0;
                index < questions.Count;
                index++)
            {
                questionContents[index] =
                    questions[index]?.question ??
                    string.Empty;
            }
        }

        // 서버 응답에는 현재 TTS 오디오가 포함되지 않는다.
        // Inspector에 있던 기존 오디오가 잘못 재생되지 않도록 비운다.
        questionAudios = new AudioClip[0];
        questionSpeakerIds.Clear();

        currentIdx = 0;
        state = QAState.ReadyToStart;
        UpdateQuestionProgress();
    }

    public void ShowGenerating()
    {
        if (questionText) questionText.gameObject.SetActive(true);
        if (qaPanel != null)
            qaPanel.SetActive(true);

        if (questionText != null)
            questionText.text =
                "질문을 생성하고 있습니다.";

        if (answerGuideText != null)
            answerGuideText.text =
                string.Empty;

        SetButtonState(
            "질문 생성 중...",
            false
        );
    }

    public void ShowGenerationFailed(string message)
    {
        if (questionText) questionText.gameObject.SetActive(true);
        if (qaPanel != null)
            qaPanel.SetActive(true);

        if (questionText != null)
            questionText.text = message;

        if (answerGuideText != null)
            answerGuideText.text =
                string.Empty;

        state = QAState.ReadyToStart;

        SetButtonState(
            "다시 시도",
            true
        );
    }
    private void OnDisable()
    {
        if (currentSpeaker) currentSpeaker.StopSpeech();


        if (audioWaitCoroutine == null) return;
        StopCoroutine(audioWaitCoroutine);
        audioWaitCoroutine = null;
    }
}


