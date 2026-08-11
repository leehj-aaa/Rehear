using Firebase;
using Firebase.Database;
using Firebase.Extensions;
using TMPro;
using UnityEngine;

public class PinInputManager : MonoBehaviour
{
    [Header("PIN 입력")]
    [SerializeField] private TMP_Text[] pinTextSlots;
    [SerializeField] private GameObject numberKeyboardPanel;
    [SerializeField] private GameObject panel_PinInput;
    [SerializeField] private GameObject panel_SessionReady;
    [SerializeField] private TMP_Text errorText;

    [Header("세션 정보 표시")]
    [SerializeField] private TMP_Text expertiseValueText;
    [SerializeField] private TMP_Text interestValueText;

    [Header("시연용 Fallback")]
    [SerializeField] private bool enableDemoFallback = true;
    [SerializeField] private string demoPin = "1234";
    [SerializeField] private string demoExpertise = "보통";
    [SerializeField] private string demoInterest = "높음";

    private const string ShowSessionReadyKey =
        "ShowSessionReadyOnLoad";

    private const string DatabaseRootPath =
        "presentation_data";

    private int currentIndex;
    private string currentPin = "";

    private bool firebaseReady;
    private bool firebaseInitializing;
    private bool isLoading;

    private void Start()
    {
        InitializeFirebase();

        bool returnFromPresentation =
            PlayerPrefs.GetInt(ShowSessionReadyKey, 0) == 1;

        if (returnFromPresentation)
        {
            PlayerPrefs.DeleteKey(ShowSessionReadyKey);
            PlayerPrefs.Save();

            ShowSessionReadyPanel();
            return;
        }

        ShowPinInputPanel();
    }

    private void InitializeFirebase()
    {
        if (firebaseInitializing)
            return;

        firebaseInitializing = true;

        FirebaseApp.CheckAndFixDependenciesAsync()
            .ContinueWithOnMainThread(task =>
            {
                firebaseInitializing = false;

                if (task.IsCanceled)
                {
                    firebaseReady = false;
                    Debug.LogWarning(
                        "Firebase 초기화가 취소되었습니다."
                    );
                    return;
                }

                if (task.IsFaulted)
                {
                    firebaseReady = false;

                    Debug.LogError(
                        "Firebase 초기화 중 오류가 발생했습니다."
                    );

                    Debug.LogException(task.Exception);
                    return;
                }

                DependencyStatus dependencyStatus =
                    task.Result;

                if (dependencyStatus ==
                    DependencyStatus.Available)
                {
                    firebaseReady = true;
                    Debug.Log("Firebase 초기화 완료");
                }
                else
                {
                    firebaseReady = false;

                    Debug.LogError(
                        "Firebase 초기화 실패: " +
                        dependencyStatus
                    );
                }
            });
    }

    // PIN 입력 영역을 누르면 숫자 키보드를 엽니다.
    public void OpenKeyboard()
    {
        if (numberKeyboardPanel != null)
            numberKeyboardPanel.SetActive(true);
    }

    // 숫자 키보드 버튼에서 문자열 숫자를 전달합니다.
    public void AddNumber(string number)
    {
        if (isLoading)
            return;

        if (currentIndex >= 4)
            return;

        if (string.IsNullOrEmpty(number))
            return;

        pinTextSlots[currentIndex].text = number;
        currentPin += number;
        currentIndex++;

        ClearError();

        if (currentIndex >= 4 &&
            numberKeyboardPanel != null)
        {
            numberKeyboardPanel.SetActive(false);
        }
    }

    // PIN 번호를 처음부터 다시 입력합니다.
    public void ResetInput()
    {
        if (isLoading)
            return;

        currentPin = "";
        currentIndex = 0;

        if (pinTextSlots != null)
        {
            foreach (TMP_Text slot in pinTextSlots)
            {
                if (slot != null)
                    slot.text = "";
            }
        }

        ClearError();

        if (numberKeyboardPanel != null)
            numberKeyboardPanel.SetActive(true);
    }

    public string GetFullPin()
    {
        return currentPin;
    }

    // 세션 불러오기 버튼에 연결합니다.
    public void OnSubmitButtonClicked()
    {
        if (isLoading)
            return;

        string pin = GetFullPin();

        if (pin.Length != 4)
        {
            ShowError(
                "PIN 번호 4자리를 모두 입력해주세요."
            );
            return;
        }

        if (!firebaseReady)
        {
            if (TryLoadDemoFallback(pin))
                return;

            ShowError(
                "서버에 연결 중입니다. 잠시 후 다시 시도해주세요."
            );

            InitializeFirebase();
            return;
        }

        LoadSessionFromFirebase(pin);
    }

    private void LoadSessionFromFirebase(string pin)
    {
        isLoading = true;

        if (errorText != null)
        {
            errorText.text =
                "세션 정보를 불러오는 중입니다.";
        }

        DatabaseReference sessionReference =
            FirebaseDatabase.DefaultInstance
                .GetReference(DatabaseRootPath)
                .Child(pin);

        sessionReference.GetValueAsync()
            .ContinueWithOnMainThread(task =>
            {
                isLoading = false;

                if (task.IsCanceled)
                {
                    if (!TryLoadDemoFallback(pin))
                    {
                        ShowError(
                            "세션 불러오기가 취소되었습니다."
                        );
                    }

                    return;
                }

                if (task.IsFaulted)
                {
                    Debug.LogError(
                        "Firebase 데이터 조회 실패"
                    );

                    Debug.LogException(task.Exception);

                    if (!TryLoadDemoFallback(pin))
                    {
                        ShowError(
                            "서버 연결에 실패했습니다."
                        );
                    }

                    return;
                }

                DataSnapshot sessionSnapshot =
                    task.Result;

                if (sessionSnapshot == null ||
                    !sessionSnapshot.Exists)
                {
                    if (!TryLoadDemoFallback(pin))
                    {
                        ShowError(
                            "존재하지 않는 PIN 번호입니다."
                        );
                    }

                    return;
                }

                ReadAudienceInformation(
                    sessionSnapshot,
                    pin
                );
            });
    }

    private void ReadAudienceInformation(
        DataSnapshot sessionSnapshot,
        string pin)
    {
        DataSnapshot page3Snapshot =
            sessionSnapshot.Child("page_3");

        if (!page3Snapshot.Exists)
        {
            if (!TryLoadDemoFallback(pin))
            {
                ShowError(
                    "청중 정보가 없는 세션입니다."
                );
            }

            return;
        }

        string expertise = GetSnapshotString(
            page3Snapshot,
            "audience_expertise"
        );

        string interest = GetSnapshotString(
            page3Snapshot,
            "audience_interest"
        );

        if (string.IsNullOrWhiteSpace(expertise))
            expertise = "미설정";

        if (string.IsNullOrWhiteSpace(interest))
            interest = "미설정";

        ApplyAudienceInformation(
            expertise,
            interest
        );

        Debug.Log(
            "세션 불러오기 완료" +
            "\nPIN: " + pin +
            "\n청중 전문성: " + expertise +
            "\n청중 관심도: " + interest
        );
    }

    private string GetSnapshotString(
        DataSnapshot parent,
        string childName)
    {
        if (parent == null)
            return "";

        DataSnapshot child =
            parent.Child(childName);

        if (!child.Exists ||
            child.Value == null)
        {
            return "";
        }

        return child.Value.ToString();
    }

    private void ApplyAudienceInformation(
        string expertise,
        string interest)
    {
        if (expertiseValueText != null)
            expertiseValueText.text = expertise;

        if (interestValueText != null)
            interestValueText.text = interest;

        ClearError();
        ShowSessionReadyPanel();
    }

    private bool TryLoadDemoFallback(string pin)
    {
        if (!enableDemoFallback)
            return false;

        if (pin != demoPin)
            return false;

        Debug.LogWarning(
            "Firebase 대신 시연용 로컬 데이터를 사용합니다."
        );

        ApplyAudienceInformation(
            demoExpertise,
            demoInterest
        );

        return true;
    }

    private void ShowPinInputPanel()
    {
        if (panel_PinInput != null)
            panel_PinInput.SetActive(true);

        if (numberKeyboardPanel != null)
            numberKeyboardPanel.SetActive(false);

        if (panel_SessionReady != null)
            panel_SessionReady.SetActive(false);
    }

    private void ShowSessionReadyPanel()
    {
        if (panel_PinInput != null)
            panel_PinInput.SetActive(false);

        if (numberKeyboardPanel != null)
            numberKeyboardPanel.SetActive(false);

        if (panel_SessionReady != null)
        {
            panel_SessionReady.SetActive(true);
            panel_SessionReady.transform.SetAsLastSibling();
        }
    }

    private void ShowError(string message)
    {
        Debug.LogWarning(message);

        if (errorText != null)
            errorText.text = message;
    }

    private void ClearError()
    {
        if (errorText != null)
            errorText.text = "";
    }
}