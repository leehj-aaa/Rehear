using UnityEngine;
using TMPro;
using Firebase.Database;

public class PinInputManager : MonoBehaviour
{
    public TMP_Text[] pinTextSlots;
    public GameObject numberKeyboardPanel; // 키보드 패널 오브젝트 연결
    private int currentIndex = 0;
    private string currentPin = "";
    public GameObject panel_PinInput;
    public GameObject panel_SessionReady;
    public TMP_Text errorText;
    private const string ShowSessionReadyKey = "ShowSessionReadyOnLoad";

    private void Start()
    {
        bool returnFromPresentation =
            PlayerPrefs.GetInt(ShowSessionReadyKey, 0) == 1;

        if (returnFromPresentation)
        {
            PlayerPrefs.DeleteKey(ShowSessionReadyKey);
            PlayerPrefs.Save();

            panel_PinInput.SetActive(false);
            numberKeyboardPanel.SetActive(false);
            panel_SessionReady.SetActive(true);
            return;
        }

    panel_PinInput.SetActive(true);
    numberKeyboardPanel.SetActive(false);
    panel_SessionReady.SetActive(false);
}

    // 칸을 눌렀을 때 키보드를 호출하는 함수
    public void OpenKeyboard()
    {
        if (numberKeyboardPanel != null)
        {
            numberKeyboardPanel.SetActive(true); // 키보드 패널 활성화
        }
    }

    public void AddNumber(string number)
    {
        if (currentIndex < 4)
        {
            pinTextSlots[currentIndex].text = number;
            currentPin += number;
            currentIndex++;
        }
        
        // 4자리가 다 입력되면 키보드를 닫는 로직도 추가 가능
        if (currentIndex >= 4)
        {
            numberKeyboardPanel.SetActive(false);
        }
    }

    
    // "다시 입력하기" 버튼을 눌렀을 때 초기화
    public void ResetInput()
    {
        currentPin = "";
        currentIndex = 0;
        
        // 입력 칸 비우기
        foreach (var slot in pinTextSlots)
        {
            slot.text = ""; 
        }

        // 에러 메시지도 함께 지우기
        if (errorText != null)
        {
            errorText.text = ""; 
        }
        
        // 키보드 패널도 다시 열어주면 사용자가 바로 이어서 입력하기 편합니다
        if (numberKeyboardPanel != null)
        {
            numberKeyboardPanel.SetActive(true);
        }
    }
    public string GetFullPin()
    {
        return currentPin;
    }
// ==================나중에 Firebase 서버 연결 시 쓸 것=============
    // public void OnSubmitButtonClicked()   
    // {
    //     string pin = GetFullPin(); // 현재 입력된 4자리 숫자 가져오기
        
    //     if (pin.Length < 4)
    //     {
    //         Debug.Log("PIN 번호를 4자리 모두 입력해주세요.");
    //         return;
    //     }

    //     string path = "presentation_data/" + pin;

    //     // Firebase 데이터 조회
    //     FirebaseDatabase.DefaultInstance.GetReference(path).GetValueAsync().ContinueWith(task =>
    //     {
    //         if (task.IsCompleted && task.Result.Exists)
    //         {
    //             // 성공: PIN 번호가 존재함
    //             string json = task.Result.GetRawJsonValue();
                
    //             // 메인 스레드에서 UI 업데이트 및 다음 단계 처리
    //            // 성공 로직 부분 수정
    //             MainThreadDispatcher.Enqueue(() => {
    //                 Debug.Log("성공! 세션 데이터 로드 시작: " + json);
                    
    //                 // 1. 현재 입력 패널 끄기
    //                 panel_PinInput.SetActive(false);
    //                 // 2. 키보드도 확실히 끄기
    //                 if (numberKeyboardPanel != null) numberKeyboardPanel.SetActive(false);
    //                 // 3. 세션 준비 패널 켜기
    //                 panel_SessionReady.SetActive(true);
    //             });
    //         }
    //         else
    //         {
    //             // 실패: 잘못된 PIN 번호
    //             MainThreadDispatcher.Enqueue(() => {
    //                 Debug.Log("잘못된 PIN 번호입니다.");
    //                 // 인스펙터에서 연결한 텍스트 컴포넌트의 내용을 변경
    //                 if (errorText != null) 
    //                 {
    //                     errorText.text = "잘못된 PIN 번호입니다.";
    //                 }
    //             });
    //         }
    //     });
    // }

    // ========시연영상을 위해 고정값============
    public void OnSubmitButtonClicked()
    {
        string pin = GetFullPin(); // 현재 입력된 4자리 숫자 가져오기
        
        // 1. PIN 번호가 4자리인지 확인
        if (pin.Length < 4)
        {
            Debug.Log("PIN 번호를 4자리 모두 입력해주세요.");
            return;
        }

        // 2. 1234일 때만 성공 처리 (영상 시연용)
        if (pin == "1234")
        {
            Debug.Log("성공! 세션 데이터 로드 시작");
            
            // UI 판넬 전환
            panel_PinInput.SetActive(false);
            if (numberKeyboardPanel != null) numberKeyboardPanel.SetActive(false);
            panel_SessionReady.SetActive(true);
        }
        else
        {
            // 3. 그 외 번호는 실패 처리
            Debug.Log("잘못된 PIN 번호입니다.");
            
            // 안내 텍스트가 있다면 변경 (인스펙터에서 연결 필수)
            if (errorText != null) 
            {
                errorText.text = "잘못된 PIN 번호입니다.";
            }
        }
    }
}      

