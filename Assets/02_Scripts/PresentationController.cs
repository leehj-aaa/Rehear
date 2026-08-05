using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class PresentationController : MonoBehaviour
{
    public TextMeshProUGUI timerText;
    public Button qaButton;           // 타이머 종료 후 나타날 질의응답 버튼
    public GameObject pausePanel;
    public GameObject scriptPanel;
    [SerializeField] private TextMeshProUGUI scriptButtonText;
    public QuestionAnswerManager qaManager;

    private float timeRemaining = 600f;
    private bool isRunning = true;
    private bool isTimerFinished = false;

    void Start()
    {
        SetScriptPanelVisible(true);
        pausePanel.SetActive(false);
        qaManager.qaPanel.SetActive(false);
        
        // 처음에는 질의응답 버튼을 숨김
        if (qaButton != null) qaButton.gameObject.SetActive(false);
    }

    public void OnActionButtonClick()
{
    // 현재 버튼의 텍스트를 가져와서 기능을 결정합니다[cite: 3]
    string btnText = qaButton.GetComponentInChildren<TextMeshProUGUI>().text;

    if (btnText == "질의응답하기") 
    {
        StartQA(); // 질의응답을 시작합니다[cite: 3]
    }
    else 
    {
        // 텍스트가 "다음 질문"이거나 "발표 종료하기"일 때는 QA 관리자의 함수를 호출합니다[cite: 3]
        qaManager.OnNextButtonClick(); 
    }
}

    void Update()
    {
        if (isRunning && !isTimerFinished)
        {
            timeRemaining -= Time.deltaTime;
            UpdateTimerDisplay();
            if (timeRemaining <= 0) FinishTimer();
        }
        else if (isTimerFinished)
        {
            timeRemaining += Time.deltaTime;
            timerText.text = "+" + FormatTime(timeRemaining);
        }
    }

    void UpdateTimerDisplay() => timerText.text = FormatTime(timeRemaining);

    string FormatTime(float t)
    {
        int m = Mathf.FloorToInt(Mathf.Abs(t) / 60);
        int s = Mathf.FloorToInt(Mathf.Abs(t) % 60);
        return string.Format("{0:00}:{1:00}", m, s);
    }

    void FinishTimer()
    {
        isTimerFinished = true;
        // 타이머 종료 시 질의응답 버튼 활성화
        if (qaButton != null) qaButton.gameObject.SetActive(true);
    }

    // 질의응답 버튼 클릭 시 실행
    public void StartQA()
    {
        qaManager.StartQAPhase(qaButton); 
        SetScriptPanelVisible(true);
    }

    public void CloseScriptPanel() => SetScriptPanelVisible(false);

    // 현재 Btn_Script의 기존 Inspector 이벤트가 이 메서드를 호출하므로
    // 별도의 씬 재연결 없이 토글로 동작하게 한다.
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

        // 버튼 텍스트는 누르면 수행될 동작을 표시한다.
        if (scriptButtonText != null)
            scriptButtonText.text = visible ? "OFF" : "ON";
    }
    
    public void SkipPresentation() => timeRemaining = 10f;
    public void PauseGame() { isRunning = false; pausePanel.SetActive(true); }
    public void ResumeGame() { isRunning = true; pausePanel.SetActive(false); }
    public void RestartSession() { timeRemaining = 600f; isTimerFinished = false; ResumeGame(); }
    public void StopSession() { SceneManager.LoadScene("Scene_01_Intro"); }
}
