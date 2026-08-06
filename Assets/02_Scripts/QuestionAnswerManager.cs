using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class QuestionAnswerManager : MonoBehaviour
{
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
        if (actionButton == null) Prepare(button);

        questionCount = Mathf.Min(
            questionContents != null ? questionContents.Length : 0,
            questionAudios != null ? questionAudios.Length : 0);

        if (questionCount == 0)
        {
            Debug.LogError("질문 텍스트와 질문 오디오를 한 개 이상 연결해야 합니다.");
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
                SceneManager.LoadScene("Scene_03_Feedback");
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

        if (audioSource == null)
        {
            Debug.LogError("QAManager에 Audio Source가 연결되지 않았습니다.");
            FinishQuestionAudio();
            return;
        }

        audioSource.Stop();
        audioSource.clip = questionAudios[currentIdx];
        audioSource.Play();

        if (audioWaitCoroutine != null) StopCoroutine(audioWaitCoroutine);
        audioWaitCoroutine = StartCoroutine(WaitForQuestionAudio());
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
        state = QAState.ReadyToAnswer;
        SetButtonState("답변하기", true);
    }

    private void CompleteAnswerStep()
    {
        bool isLastQuestion = currentIdx >= questionCount - 1;

        if (isLastQuestion)
        {
            if (answerGuideText != null)
                answerGuideText.text = "마지막 답변을 마치셨다면 아래 버튼을 눌러 발표를 종료해주세요.";
            state = QAState.ReadyToFinish;
            SetButtonState("발표 종료하기", true);
        }
        else
        {
            if (answerGuideText != null)
                answerGuideText.text = "답변을 마치셨다면 아래 버튼을 눌러 다음 질문을 받아주세요.";
            state = QAState.ReadyForNext;
            SetButtonState("질문받기", true);
        }
    }

    private void SetButtonState(string label, bool interactable)
    {
        if (actionButton != null) actionButton.interactable = interactable;
        if (actionButtonText != null) actionButtonText.text = label;
    }

    private void OnDisable()
    {
        if (audioWaitCoroutine == null) return;
        StopCoroutine(audioWaitCoroutine);
        audioWaitCoroutine = null;
    }
}
