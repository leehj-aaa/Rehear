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
using Rehear.Evc.Audio;

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


    private enum QAState { ReadyToStart, PlayingQuestion, QuestionAudioFailed, ReadyToAnswer, StartingAnswer, Answering, SavingAnswer, AnswerSaveFailed, ReadyForNext, ReadyToFinish }

    [SerializeField] private AzureSpeechConfig azureSpeechConfig;
    private AzureQuestionSpeechClient speechClient;
    private QuestionAnswerRecorder answerRecorder;
    private CancellationTokenSource speechLifetime;
    private readonly List<AudioClip> generatedSpeechClips = new List<AudioClip>();
    private byte[] pendingAnswer;
    private string answerRequestId;

    private CancellationToken SpeechToken
    {
        get
        {
            if (speechLifetime == null) speechLifetime = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
            return speechLifetime.Token;
        }
    }

    private AzureQuestionSpeechClient SpeechClient
    {
        get
        {
            if (!azureSpeechConfig) azureSpeechConfig = Resources.Load<AzureSpeechConfig>("AzureSpeechConfig");
            return speechClient ?? (speechClient = new AzureQuestionSpeechClient(azureSpeechConfig));
        }
    }

    public GameObject qaPanel;
    public TMP_Text questionText;
    public TMP_Text answerGuideText;
    public AudioSource audioSource;
    public AudioClip[] questionAudios;
    [TextArea] public string[] questionContents;

    private AudienceQuestionSpeaker currentSpeaker;
    private AudienceAnimationPlayer questionBody;
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
        if (questionBody) questionBody.SetQuestionPaused(paused);
        if (answerRecorder) answerRecorder.SetPaused(paused);
        if (actionButton) actionButton.interactable = !paused &&
            state != QAState.PlayingQuestion && state != QAState.StartingAnswer && state != QAState.SavingAnswer && !isFinishingPresentation &&
            (state != QAState.Answering || Time.unscaledTime >= answerUnlockTime);
    }

    public bool IsQAPhaseActive { get; private set; }

    public void Prepare(Button button)
    {
        IsQAPhaseActive = false;
        if (actionButton != button && questionProgressText != null)
        {
            questionProgressText.gameObject.SetActive(false);
            questionProgressText = null;
        }
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
        AssignQuestionSpeakers();
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
                SaveAnswer();
                break;
            case QAState.AnswerSaveFailed:
                SaveAnswer();
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

    private async void PlayCurrentQuestion()
    {
        if (currentIdx < 0 || currentIdx >= questionCount) return;

        state = QAState.PlayingQuestion;
        if (answerGuideText != null) answerGuideText.text = string.Empty;
        if (questionText != null) questionText.text = questionContents[currentIdx];
        SetButtonState("청중 질문 중…", false);

       AudioClip clip =
            questionAudios != null &&
            currentIdx < questionAudios.Length
                ? questionAudios[currentIdx]
                : null;

        if (currentSpeaker) currentSpeaker.StopSpeech();
        if (questionBody) questionBody.ReleaseQuestionTurn();
        if (audioSource) audioSource.Stop();
        var agents = GetActiveQuestionAgents();
        Array.Sort(agents, (a,b) => string.CompareOrdinal(a.AgentId,b.AgentId));
        Rehear.Evc.Audience.AudienceAgent selected = null;
        if (questionSpeakerIds.TryGetValue(currentIdx,out var speakerId))
            selected = Array.Find(agents, a => a.AgentId == speakerId);
        else if (agents.Length > 0) selected = agents[currentIdx % agents.Length];
        currentSpeaker = selected ? selected.GetComponent<AudienceQuestionSpeaker>() : null;
        if (selected && !currentSpeaker) currentSpeaker = selected.gameObject.AddComponent<AudienceQuestionSpeaker>();
        questionBody = selected ? selected.GetComponent<AudienceAnimationPlayer>() : null;

        if (!clip && Application.isPlaying)
        {
            SetButtonState("질문 음성 준비 중…", false);
            try
            {
                var profile = selected ? selected.GetComponent<AudienceVoiceProfile>() : null;
                if (!profile) throw new InvalidOperationException("청중 음성 프로필이 없습니다.");
                clip = await SpeechClient.SynthesizeAsync(currentIdx, profile.VoiceName, selected.AgentId, SpeechToken);
                if (!this || !isActiveAndEnabled) { if (clip) Destroy(clip); return; }
                generatedSpeechClips.Add(clip);
                if (questionAudios == null || questionAudios.Length != questionCount) Array.Resize(ref questionAudios, questionCount);
                questionAudios[currentIdx] = clip;
            }
            catch (OperationCanceledException) { return; }
            catch (Exception e)
            {
                state = QAState.QuestionAudioFailed;
                SetButtonState("질문 음성 다시 시도", true);
                Debug.LogWarning("[Q&A] " + e.Message);
                return;
            }
        }
        SetButtonState("청중 질문 중…", false);

        if (clip && currentSpeaker && Application.isPlaying)
        {
            try
            {
                if (!questionBody || !questionBody.HasQuestionGesture)
                    throw new InvalidOperationException("질문 청중의 QS 애니메이션이 연결되지 않았습니다.");
                SetButtonState("청중 질문 준비 중…", false);
                questionBody.ReserveQuestionTurn();
                questionBody.SetQuestionPaused(isPaused);
                var token = SpeechToken;
                await WaitForQuestionBaseline(token);
                if (!questionBody.PlayQuestionGesture()) throw new InvalidOperationException("QS 애니메이션을 재생하지 못했습니다.");
                await WaitForQuestionBaseline(token);
                // The hand has lowered and the blend to the baseline has finished.
                SetButtonState("청중 질문 중…", false);
            }
            catch (OperationCanceledException) { if (questionBody) questionBody.ReleaseQuestionTurn(); return; }
            catch (Exception e)
            {
                if (questionBody) questionBody.ReleaseQuestionTurn();
                state = QAState.QuestionAudioFailed;
                SetButtonState("질문 다시 시도", true);
                Debug.LogWarning("[Q&A] " + e.Message);
                return;
            }
        }
        if (!clip || !currentSpeaker || !currentSpeaker.Play(clip))
        {
            if (questionBody) questionBody.ReleaseQuestionTurn();
            state = QAState.QuestionAudioFailed;
            SetButtonState("질문 음성 다시 시도", true);
            if (questionText) { questionText.gameObject.SetActive(true); questionText.text = "질문 음성을 준비하지 못했습니다."; }
            Debug.LogWarning("[Q&A] 질문 음성/청중 연결을 확인하세요. 발화 완료로 처리하지 않습니다.");
            return;
        }
        currentSpeaker.SetPaused(isPaused);
        if (questionText) questionText.gameObject.SetActive(false);
        if (audioWaitCoroutine != null) StopCoroutine(audioWaitCoroutine);
        audioWaitCoroutine = StartCoroutine(WaitForQuestionAudio());
    }

    private void AssignQuestionSpeakers()
    {
        var agents = GetActiveQuestionAgents();
        var ids = new List<string>();
        foreach (var agent in agents)
        {
            var body = agent.GetComponent<AudienceAnimationPlayer>();
            if (body && body.HasQuestionGesture && !string.IsNullOrEmpty(agent.AgentId)) ids.Add(agent.AgentId);
        }
        if (ids.Count == 0) return; // Playback shows an actionable failure instead of pretending speech ended.
        var assignments = QuestionSpeakerAssignment.Create(ids, questionCount, PresentationSessionContext.Current.Seed);
        for (int i = 0; i < assignments.Length; i++)
            if (!questionSpeakerIds.ContainsKey(i)) questionSpeakerIds[i] = assignments[i];
    }

    private static Rehear.Evc.Audience.AudienceAgent[] GetActiveQuestionAgents()
    {
        // Editor prefab/preview objects can share an ID with a spawned attendee.
        // Select only enabled attendees in a loaded runtime scene for both assignment and playback.
        return Array.FindAll(FindObjectsByType<Rehear.Evc.Audience.AudienceAgent>(FindObjectsSortMode.None), agent =>
        {
            var scene = agent.gameObject.scene;
            var body = agent.GetComponent<AudienceAnimationPlayer>();
            return scene.IsValid() && scene.isLoaded && agent.isActiveAndEnabled && body &&
                body.isActiveAndEnabled && body.HasQuestionGesture && !string.IsNullOrEmpty(agent.AgentId);
        });
    }

    private async System.Threading.Tasks.Task WaitForQuestionBaseline(CancellationToken token)
    {
        do
        {
            token.ThrowIfCancellationRequested();
            if (!questionBody || !questionBody.isActiveAndEnabled)
                throw new InvalidOperationException("질문 청중이 비활성화되었습니다.");
            await System.Threading.Tasks.Task.Yield();
        } while (isPaused || !questionBody.IsAtBaseline);
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
        if (questionBody) questionBody.ReleaseQuestionTurn();
        state = QAState.ReadyToAnswer;
        SetButtonState("응답 시작하기", true);
    }

    private async void BeginAnswer()
    {
        // Editor previews exercise layout only; they never request microphone access.
        if (Application.isPlaying)
        {
            state = QAState.StartingAnswer;
            SetButtonState("마이크 준비 중…", false);
            try
            {
                SpeechClient.Validate();
                if (!answerRecorder) answerRecorder = gameObject.AddComponent<QuestionAnswerRecorder>();
                await answerRecorder.BeginAsync(SpeechToken);
                if (!this || !isActiveAndEnabled) return;
                answerRecorder.SetPaused(isPaused);
                pendingAnswer = null;
                answerRequestId = Guid.NewGuid().ToString("D");
            }
            catch (OperationCanceledException) { return; }
            catch (Exception e)
            {
                state = QAState.ReadyToAnswer;
                SetButtonState("마이크 다시 시도", true);
                Debug.LogWarning("[Q&A] " + e.Message);
                return;
            }
        }
        state = QAState.Answering;
        answerUnlockTime = Time.unscaledTime + Mathf.Max(.2f, answerClickLockSeconds);
        SetButtonState("응답 마치기", false);

    }

    private void Update()
    {
        if (!isPaused && state == QAState.Answering && answerRecorder && answerRecorder.ReachedLimit)
        { SaveAnswer(); return; }
        if (!isPaused && state == QAState.Answering && actionButton != null && !actionButton.interactable
            && Time.unscaledTime >= answerUnlockTime)
            SetButtonState("응답 마치기", true);
    }
    private async void SaveAnswer()
    {
        if (state != QAState.Answering && state != QAState.AnswerSaveFailed) return;
        state = QAState.SavingAnswer;
        SetButtonState("응답 저장 중…", false);
        try
        {
            if (pendingAnswer == null) pendingAnswer = answerRecorder ? answerRecorder.Finish() : null;
            if (pendingAnswer == null || pendingAnswer.Length <= 44)
            {
                pendingAnswer = null;
                state = QAState.ReadyToAnswer;
                SetButtonState("응답 다시 녹음하기", true);
                return;
            }
            questionSpeakerIds.TryGetValue(currentIdx + 1, out var nextAudienceId);
            var result = await SpeechClient.SubmitAnswerAsync(currentIdx, pendingAnswer, answerRequestId, nextAudienceId, SpeechToken);
            if (!this || !isActiveAndEnabled) return;
            if (result == null)
            {
                pendingAnswer = null;
                state = QAState.ReadyToAnswer;
                SetButtonState("응답 다시 녹음하기", true);
                return;
            }
            ApplyAdaptiveAnswer(result);
            pendingAnswer = null;
            if (isPaused)
            {
                state = currentIdx >= questionCount - 1 ? QAState.ReadyToFinish : QAState.ReadyForNext;
                SetButtonState(state == QAState.ReadyToFinish ? "피드백 보기" : "다음 질문 듣기", true);
            }
            else CompleteAnswerStep();
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            state = QAState.AnswerSaveFailed;
            SetButtonState("응답 저장 다시 시도", true);
            Debug.LogWarning("[Q&A] " + e.Message);
        }
    }
    private void ApplyAdaptiveAnswer(AzureQuestionSpeechClient.AnswerResult result)
    {
            if (result.total != questionCount)
                throw new InvalidOperationException("서버 질문 개수가 세션 설정과 다릅니다.");
            if (currentIdx + 1 < questionCount)
            {
                var next = result.next_question;
                if (next == null || next.order != currentIdx + 2 || next.id != "q" + (currentIdx + 2) || string.IsNullOrWhiteSpace(next.question))
                    throw new InvalidOperationException("답변 기반 다음 질문을 준비하지 못했습니다.");
                questionContents[currentIdx + 1] = next.question;
                if (questionAudios != null && currentIdx + 1 < questionAudios.Length)
                    questionAudios[currentIdx + 1] = null;
            }
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
        if (questionProgressText != null && actionButton != null && questionProgressText.transform.parent != actionButton.transform)
        {
            questionProgressText.gameObject.SetActive(false);
            questionProgressText = null;
        }
        if (questionProgressText == null && actionButton != null)
        {
            var existing = actionButton.transform.Find("Question Progress");
            if (existing) questionProgressText = existing.GetComponent<TMP_Text>();
        }
        bool visible = IsQAPhaseActive && questionCount > 0 &&
            (state == QAState.PlayingQuestion || state == QAState.ReadyToAnswer ||
             state == QAState.StartingAnswer || state == QAState.Answering || state == QAState.SavingAnswer ||
             state == QAState.AnswerSaveFailed || state == QAState.QuestionAudioFailed || state == QAState.ReadyForNext);
        if (visible && questionProgressText == null && actionButtonText != null)
        {
            // Copy the existing font/curved text setup so this follows the same canvas.
            questionProgressText = Instantiate(actionButtonText, actionButton.transform);
            questionProgressText.name = "Question Progress";
            questionProgressText.raycastTarget = false;
            questionProgressText.alignment = TextAlignmentOptions.Center;
            questionProgressText.color = Color.white;
            var rect = questionProgressText.rectTransform;
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(.5f, 0);
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            rect.sizeDelta = new Vector2(0, ((RectTransform)actionButton.transform).rect.height * .85f);
            rect.anchoredPosition3D = new Vector3(0, 12, 0);
        }
        if (questionProgressText == null) return;
        questionProgressText.enableAutoSizing = false;
        questionProgressText.fontSize = actionButtonText.fontSize;
        questionProgressText.rectTransform.sizeDelta = new Vector2(0, ((RectTransform)actionButton.transform).rect.height * .85f);
        questionProgressText.gameObject.SetActive(visible);
        if (visible)
            questionProgressText.text = $"{currentIdx + 1} / {questionCount}번째 질문 · 이후 {Mathf.Max(0, questionCount - currentIdx - 1)}개 남음";
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
        speechLifetime?.Cancel();
        speechLifetime?.Dispose();
        speechLifetime = null;
        if (answerRecorder) answerRecorder.Cancel();
        if (currentSpeaker) currentSpeaker.StopSpeech();
        if (questionBody) questionBody.ReleaseQuestionTurn();


        if (audioWaitCoroutine == null) return;
        StopCoroutine(audioWaitCoroutine);
        audioWaitCoroutine = null;
    }
    private void OnDestroy()
    {
        foreach (var clip in generatedSpeechClips) if (clip) Destroy(clip);
    }
}


