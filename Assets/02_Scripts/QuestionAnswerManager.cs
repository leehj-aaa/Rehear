using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class QuestionAnswerManager : MonoBehaviour
{
    public GameObject qaPanel;
    public TMP_Text questionText;
    public AudioSource audioSource;
    public AudioClip[] questionAudios;
    [TextArea] public string[] questionContents;

    private int currentIdx = 0;
    private Button actionButton;

    public void StartQAPhase(Button btn)
    {
        actionButton = btn;
        qaPanel.SetActive(true);
        PlayQA();
    }

    void PlayQA()
    {
        questionText.text = questionContents[currentIdx];
        audioSource.clip = questionAudios[currentIdx];
        audioSource.Play();

        if (currentIdx < questionContents.Length - 1)
            actionButton.GetComponentInChildren<TMP_Text>().text = "다음 질문";
        else
            actionButton.GetComponentInChildren<TMP_Text>().text = "발표 종료하기";
    }

    public void OnNextButtonClick()
    {
        currentIdx++;
        if (currentIdx < questionContents.Length)
        {
            PlayQA();
        }
        else
        {
            SceneManager.LoadScene("Scene_03_Feedback");
        }
    }
}