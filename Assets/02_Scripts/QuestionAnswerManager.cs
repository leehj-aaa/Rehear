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


    private enum QAState { ReadyToStart, PlayingQuestion, ReadyToAnswer, ReadyForNext, ReadyToFinish }

    public GameObject qaPanel;
    public TMP_Text questionText;
    public TMP_Text answerGuideText;
    public AudioSource audioSource;
    public AudioClip[] questionAudios;
    [TextArea] public string[] questionContents;

    private int currentIdx;
    private int questionCount;
    private Button actionButton;
    private TMP_Text actionButtonText;
    private QAState state = QAState.ReadyToStart;
    private Coroutine audioWaitCoroutine;

    public void Prepare(Button button)
    {
        actionButton = button;
        actionButtonText = actionButton != null ? actionButton.GetComponentInChildren<TMP_Text>(true) : null;
        currentIdx = 0;
        state = QAState.ReadyToStart;

        if (qaPanel != null) qaPanel.SetActive(false);
        if (answerGuideText != null) answerGuideText.text = string.Empty;
        SetButtonState("질의응답하기", true);
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
        if (qaPanel != null) qaPanel.SetActive(true);
        PlayCurrentQuestion();
    }

    public void OnActionButtonClick()
    {
        switch (state)
        {
            case QAState.ReadyToStart:
                StartQAPhase(actionButton);
                break;
            case QAState.PlayingQuestion:
                break;
            case QAState.ReadyToAnswer:
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
        SetButtonState("질문받는 중...", false);

       AudioClip clip =
            questionAudios != null &&
            currentIdx < questionAudios.Length
                ? questionAudios[currentIdx]
                : null;

        if (audioSource == null || clip == null)
        {
            Debug.LogWarning(
                "[Q&A] 질문 TTS 오디오가 없어 " +
                "질문 텍스트만 표시합니다."
            );

            FinishQuestionAudio();
            return;
        }

        audioSource.Stop();
        audioSource.clip = clip;
        audioSource.Play();

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

    int plannedSeconds =
        Mathf.Max(
            0,
            RuntimeSessionData.DurationMinutes * 60
        );

    int qaSeconds =
        Mathf.Max(
            0,
            Mathf.RoundToInt(
                Time.realtimeSinceStartup - qaStartedTime
            )
        );

    try
    {
        ReportFeedback report =
            await flowController.FinishReportAsync(
                plannedSeconds,
                qaSeconds,
                CancellationToken.None
            );

        RuntimeReportData.Set(report);
        LoadFeedbackScene();
    }
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
        yield return new WaitWhile(() => audioSource != null && audioSource.isPlaying);
        audioWaitCoroutine = null;
        FinishQuestionAudio();
    }

    private void FinishQuestionAudio()
    {
        bool isLastQuestion =
            currentIdx >= questionCount - 1;

        if (isLastQuestion)
        {
            state = QAState.ReadyToFinish;

            if (answerGuideText != null)
            {
                answerGuideText.text =
                    "마지막 질문입니다.\n" +
                    "답변을 마치신 후 발표 종료하기를 눌러주세요.";
            }

            SetButtonState("발표 종료하기", true);
        }
        else
        {
            state = QAState.ReadyForNext;

            if (answerGuideText != null)
            {
                answerGuideText.text =
                    "답변을 마치신 후 질문받기를 눌러\n" +
                    "다음 질문을 진행해 주세요.";
            }

            SetButtonState("질문받기", true);
        }
    }

    private void CompleteAnswerStep()
    {
        bool isLastQuestion = currentIdx >= questionCount - 1;

        if (isLastQuestion)
        {
            if (answerGuideText != null)
                answerGuideText.text = "마지막 답변을 마치셨다면 아래 버튼을 눌러 \n 발표를 종료해주세요.";
            state = QAState.ReadyToFinish;
            SetButtonState("발표 종료하기", true);
        }
        else
        {
            if (answerGuideText != null)
                answerGuideText.text = "답변을 마치셨다면 아래 버튼을 눌러 \n 다음 질문을 받아주세요.";
            state = QAState.ReadyForNext;
            SetButtonState("질문받기", true);
        }
    }

    private void SetButtonState(string label, bool interactable)
    {
        if (actionButton != null) actionButton.interactable = interactable;
        if (actionButtonText != null) actionButtonText.text = label;
    }
    public void SetGeneratedQuestions(
        IReadOnlyList<GeneratedQuestion> questions)
    {
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

        currentIdx = 0;
        state = QAState.ReadyToStart;
    }

    public void ShowGenerating()
    {
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
        if (audioWaitCoroutine == null) return;
        StopCoroutine(audioWaitCoroutine);
        audioWaitCoroutine = null;
    }
}
