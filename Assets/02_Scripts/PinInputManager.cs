using Firebase;
using Firebase.Database;
using Firebase.Extensions;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using Rehear.Evc.Data;
using Rehear.Evc.Presentation;
using System.Collections;
using UnityEngine.SceneManagement;

public class PinInputManager : MonoBehaviour
{
    [Header("PIN 입력")]
    [SerializeField] private TMP_Text[] pinTextSlots;
    [SerializeField] private GameObject numberKeyboardPanel;
    [SerializeField] private GameObject panel_PinInput;
    [SerializeField] private GameObject panel_SessionReady;
    [SerializeField] private TMP_Text errorText;
    [Header("Opening scene flow")]
    [SerializeField] private bool continueToTutorial;
    [SerializeField] private GameObject panel_Loading;
    private int requestVersion;
    private float loadingStarted;
    private const float LoadTimeoutSeconds = 25f;
    private bool InputVisible => isActiveAndEnabled && panel_PinInput && panel_PinInput.activeInHierarchy;

    [Header("세션 정보 표시")]
    [SerializeField] private TMP_Text sessionTypeValueText;
    [SerializeField] private TMP_Text durationValueText;
    [SerializeField] private TMP_Text qaCountValueText;
    [SerializeField] private TMP_Text audienceScaleValueText;
    [SerializeField] private TMP_Text environmentValueText;
    [SerializeField] private TMP_Text expertiseValueText;
    [SerializeField] private TMP_Text interestValueText;

    [Header("시연용 Fallback")]
    [SerializeField] private bool enableDemoFallback = true;
    [SerializeField] private string demoPin = "1234";
    [SerializeField] private string demoExpertise = "중간";
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
    private void Awake()
    {
        // A loaded session must not flash the old PIN panel after the tutorial.
        if (!continueToTutorial && RuntimeSessionData.Session != null &&
            PresentationSessionContext.Current.HasPresentation)
            ApplySessionInformation(RuntimeSessionData.Session);
        else
            ShowPinInputPanel();
    }

    private void Start()
    {
        PlayerPrefs.DeleteKey(ShowSessionReadyKey);
        PlayerPrefs.Save();

        if (!continueToTutorial && RuntimeSessionData.Session != null &&
            PresentationSessionContext.Current.HasPresentation)
        {
            ApplySessionInformation(RuntimeSessionData.Session);
        }
        else
        {
            ShowPinInputPanel();
            InitializeFirebase();
        }
    }
    private void Update()
{
    if (isLoading && Time.unscaledTime - loadingStarted > LoadTimeoutSeconds)
    {
        requestVersion++;
        isLoading = false;
        ShowError("서버 응답이 늦어지고 있습니다. 연결을 확인하고 다시 시도해 주세요.");
    }
#if UNITY_EDITOR || UNITY_STANDALONE
    Keyboard keyboard = Keyboard.current;

    if (keyboard == null || isLoading || !InputVisible)
        return;

    for (int number = 0; number <= 9; number++)
    {
        if (WasNumberPressed(
                keyboard,
                number))
        {
            AddNumber(number.ToString());
            break;
        }
    }

    if (keyboard.enterKey.wasPressedThisFrame)
    {
        OnSubmitButtonClicked();
    }

    if (keyboard.backspaceKey.wasPressedThisFrame)
    {
        ResetInput();
    }
#endif
}

private bool WasNumberPressed(
    Keyboard keyboard,
    int number)
{
    switch (number)
    {
        case 0:
            return keyboard.digit0Key.wasPressedThisFrame ||
                   keyboard.numpad0Key.wasPressedThisFrame;

        case 1:
            return keyboard.digit1Key.wasPressedThisFrame ||
                   keyboard.numpad1Key.wasPressedThisFrame;

        case 2:
            return keyboard.digit2Key.wasPressedThisFrame ||
                   keyboard.numpad2Key.wasPressedThisFrame;

        case 3:
            return keyboard.digit3Key.wasPressedThisFrame ||
                   keyboard.numpad3Key.wasPressedThisFrame;

        case 4:
            return keyboard.digit4Key.wasPressedThisFrame ||
                   keyboard.numpad4Key.wasPressedThisFrame;

        case 5:
            return keyboard.digit5Key.wasPressedThisFrame ||
                   keyboard.numpad5Key.wasPressedThisFrame;

        case 6:
            return keyboard.digit6Key.wasPressedThisFrame ||
                   keyboard.numpad6Key.wasPressedThisFrame;

        case 7:
            return keyboard.digit7Key.wasPressedThisFrame ||
                   keyboard.numpad7Key.wasPressedThisFrame;

        case 8:
            return keyboard.digit8Key.wasPressedThisFrame ||
                   keyboard.numpad8Key.wasPressedThisFrame;

        case 9:
            return keyboard.digit9Key.wasPressedThisFrame ||
                   keyboard.numpad9Key.wasPressedThisFrame;

        default:
            return false;
    }
}
    private void InitializeFirebase()
{
    if (firebaseInitializing || firebaseReady)
        return;

    firebaseInitializing = true;

#if UNITY_ANDROID && !UNITY_EDITOR
    try
    {
        FirebaseApp app = FirebaseApp.DefaultInstance;
        FirebaseDatabase database =
            FirebaseDatabase.DefaultInstance;

        firebaseReady = true;
        firebaseInitializing = false;

        Debug.Log(
            "Firebase Android 직접 초기화 완료"
        );
    }
    catch (System.Exception exception)
    {
        firebaseReady = false;
        firebaseInitializing = false;

        Debug.LogError(
            "Firebase Android 직접 초기화 실패"
        );
        Debug.LogException(exception);
    }

    return;
#endif

    try
    {
    FirebaseApp.CheckAndFixDependenciesAsync()
        .ContinueWithOnMainThread(task =>
        {
            if (!this) return;
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
    catch (System.Exception exception)
    {
        firebaseInitializing = false;
        firebaseReady = false;
        ShowError("서버 연결 모듈을 초기화하지 못했습니다. Firebase 설치 상태를 확인해 주세요.");
        Debug.LogException(exception);
    }
}

    // PIN 입력 영역을 누르면 숫자 키보드를 엽니다.
    public void OpenKeyboard()
    {
        if (isLoading || !InputVisible) return;
        if (numberKeyboardPanel != null)
            numberKeyboardPanel.SetActive(true);
    }

    // 숫자 키보드 버튼에서 문자열 숫자를 전달합니다.
    public void AddNumber(string number)
    {
        if (isLoading || !InputVisible)
            return;

        if (currentIndex >= 4)
            return;

        if (string.IsNullOrEmpty(number) || number.Length != 1 || number[0] < '0' || number[0] > '9' ||
            pinTextSlots == null || currentIndex >= pinTextSlots.Length || !pinTextSlots[currentIndex])
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
        if (isLoading || !InputVisible)
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
        if (isLoading || !InputVisible)
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
        loadingStarted = Time.unscaledTime;
        int version = ++requestVersion;
        if (panel_Loading)
        {
            panel_PinInput.SetActive(false);
            if (numberKeyboardPanel) numberKeyboardPanel.SetActive(false);
            panel_Loading.SetActive(true);
        }

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
                if (!this || !isActiveAndEnabled || version != requestVersion) return;
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

                ReadSessionInformation(
                    sessionSnapshot,
                    pin
                );
            });
    }

    private void ReadSessionInformation(
    DataSnapshot sessionSnapshot,
    string pin)
{
    string json =
        sessionSnapshot.GetRawJsonValue();

    if (string.IsNullOrWhiteSpace(json))
    {
        if (!TryLoadDemoFallback(pin))
            ShowError("세션 데이터가 비어 있습니다.");

        return;
    }

    SessionData session;

    try
    {
        session =
            JsonUtility.FromJson<SessionData>(json);
    }
    catch (System.Exception exception)
    {
        Debug.LogException(exception);

        if (!TryLoadDemoFallback(pin))
            ShowError("세션 데이터 형식이 올바르지 않습니다.");

        return;
    }

    if (session == null ||
        session.page_1 == null ||
        session.page_3 == null)
    {
        if (!TryLoadDemoFallback(pin))
            ShowError("필수 세션정보가 없습니다.");

        return;
    }

    NormalizeSessionData(session);

    if (!LoadEvcPresentationContext(pin, session))
    {
        ShowError("세션 정보가 올바르지 않습니다. 웹에서 대본과 세션 설정을 확인해 주세요.");
        return;
    }

    RuntimeSessionData.Load(
        pin,
        session
    );

    ApplySessionInformation(session);

    Debug.Log(
        "세션 불러오기 완료" +
        "\n발표 제목: " +
        RuntimeSessionData.PresentationTitle +
        "\n발표 시간: " +
        RuntimeSessionData.DurationMinutes + "분" +
        "\n발표 환경: " +
        RuntimeSessionData.EnvironmentType +
        "\nQ&A 개수: " +
        RuntimeSessionData.QaCount +
        "\n청중 규모: " +
        RuntimeSessionData.AudienceScale + "명" +
        "\n청중 전문성: " +
        RuntimeSessionData.AudienceExpertise +
        "\n청중 관심도: " +
        RuntimeSessionData.AudienceInterest
    );
}

private void NormalizeSessionData(
    SessionData session)
{
    if (session.page_1 == null)
        session.page_1 = new Page1();

    if (session.page_2 == null)
        session.page_2 = new Page2();

    if (session.page_3 == null)
        session.page_3 = new Page3();

    if (string.IsNullOrWhiteSpace(
            session.page_1.presentation_title))
    {
        session.page_1.presentation_title =
            "Re:hear 발표";
    }

    if (string.IsNullOrWhiteSpace(
            session.page_1.presentation_purpose))
    {
        session.page_1.presentation_purpose =
            "발표 모드";
    }

    if (session.page_1.duration_minutes <= 0)
        session.page_1.duration_minutes = 1;

    if (string.IsNullOrWhiteSpace(
            session.page_1.environment_type))
    {
        session.page_1.environment_type =
            "세미나실";
    }

    // 0은 "질의응답 없음"이라는 유효한 설정이다.
    session.page_1.qa_count =
        Mathf.Clamp(session.page_1.qa_count, 0, 5);

    if (string.IsNullOrWhiteSpace(
            session.page_1.used_language))
    {
        session.page_1.used_language = "ko";
    }

    if (string.IsNullOrWhiteSpace(
            session.page_3.audience_expertise))
    {
        session.page_3.audience_expertise =
            "중간";
    }

    if (string.IsNullOrWhiteSpace(
            session.page_3.audience_interest))
    {
        session.page_3.audience_interest =
            "중간";
    }

    if (session.page_3.audience_scale <= 0)
        session.page_3.audience_scale = 6;

    if (string.IsNullOrWhiteSpace(
            session.page_3.audience_type))
    {
        session.page_3.audience_type =
            "일반 청중";
    }
}

private void ApplySessionInformation(
    SessionData session)
{
    if (sessionTypeValueText != null)
{
    sessionTypeValueText.text = "발표 모드";
}

    if (durationValueText != null)
    {
        durationValueText.text =
            session.page_1.duration_minutes + "분";
    }

    if (qaCountValueText != null)
    {
        qaCountValueText.text =
            session.page_1.qa_count + "개";
    }

    if (audienceScaleValueText != null)
    {
        audienceScaleValueText.text =
            session.page_3.audience_scale + "명";
    }

    if (environmentValueText != null)
    {
        environmentValueText.text =
            session.page_1.environment_type;
    }

    if (expertiseValueText != null)
    {
        expertiseValueText.text =
            session.page_3.audience_expertise;
    }

    if (interestValueText != null)
    {
        interestValueText.text =
            session.page_3.audience_interest;
    }

    ClearError();
    if (continueToTutorial)
    {
        isLoading = true;
        loadingStarted = Time.unscaledTime;
        if (panel_PinInput) panel_PinInput.SetActive(false);
        if (numberKeyboardPanel) numberKeyboardPanel.SetActive(false);
        if (panel_Loading) panel_Loading.SetActive(true);
        StartCoroutine(ContinueAfterLoading());
        return;
    }
    ShowSessionReadyPanel();
}

    private IEnumerator ContinueAfterLoading()
    {
        yield return new WaitForSecondsRealtime(.5f);
        SceneManager.LoadScene("Scene_00_5_Tutorial");
    }

    private void OnDisable()
    {
        requestVersion++;
        isLoading = false;
        StopAllCoroutines();
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

    SessionData demoSession =
        new SessionData
        {
            status = "ready",

            page_1 = new Page1
            {
                presentation_title =
                    "Re:hear 시연 발표",

                presentation_purpose =
                    "발표 모드",

                duration_minutes = 1,
                environment_type = "세미나실",
                qa_count = 1,
                used_language = "ko"
            },

            page_2 = new Page2
            {
                presentation_script_content = "안녕하세요. Re:hear 시연용 발표입니다."
            },

            page_3 = new Page3
            {
                audience_expertise =
                    demoExpertise,

                audience_interest =
                    demoInterest,

                audience_scale = 6,
                audience_type = "일반 청중"
            }
        };

    if (!LoadEvcPresentationContext(pin, demoSession, true)) return false;
    RuntimeSessionData.Load(pin, demoSession);

    ApplySessionInformation(
        demoSession
    );

    return true;
}

    private void ShowPinInputPanel()
    {
        if (panel_Loading) panel_Loading.SetActive(false);
        if (panel_PinInput != null)
            panel_PinInput.SetActive(true);

        if (numberKeyboardPanel != null)
            numberKeyboardPanel.SetActive(continueToTutorial);

        if (panel_SessionReady != null)
            panel_SessionReady.SetActive(false);
    }

    private void ShowSessionReadyPanel()
    {
        if (panel_Loading) panel_Loading.SetActive(false);
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
        if (panel_Loading && panel_Loading.activeSelf) ShowPinInputPanel();
        Debug.LogWarning(message);

        if (errorText != null)
            errorText.text = message;
    }

    private void ClearError()
    {
        if (errorText != null)
            errorText.text = "";
    }

    private bool LoadEvcPresentationContext(
    string pin,
    SessionData session, bool isDemo = false)
    {
        if (session == null)
            return false;

        PresentationDataDto evcData =
            new PresentationDataDto
            {
                pin = pin,

                presentation_title =
                    session.page_1?.presentation_title ?? "Re:hear 발표",

                page_1 =
                    new PresentationPage1Dto
                    {
                        duration_minutes =
                            session.page_1?.duration_minutes ?? 1,

                        environment_type =
                            session.page_1?.environment_type ?? "세미나실",

                        qa_count =
                            session.page_1?.qa_count ?? 1
                    },

                page_2 =
                    new PresentationPage2Dto
                    {
                        presentation_script_content =
                            session.page_2?.presentation_script_content ?? "",

                        slide_image =
                            new SlideImageDto
                            {
                                image_urls =
                                    session.page_2?.slide_image?.image_urls
                            }
                    },

                page_3 =
                    new PresentationPage3Dto
                    {
                        audience_expertise =
                            session.page_3?.audience_expertise ?? "중간",

                        audience_interest =
                            session.page_3?.audience_interest ?? "중간",

                        audience_scale =
                            session.page_3?.audience_scale ?? 6
                    },

                is_demo_fallback =
                    isDemo
            };

        PresentationValidationResult validation =
            PresentationSessionContext.Current
                .BeginPresentation(evcData);

        if (!validation.IsValid)
        {
            Debug.LogError(
                "[EVC] 발표 세션 데이터 연결 실패:\n" +
                string.Join("\n", validation.Errors)
            );

            return false;
        }

        Debug.Log(
            "[EVC] 발표 세션 데이터 연결 완료" +
            "\n제목: " + evcData.presentation_title +
            "\n발표 시간: " +
            evcData.page_1.duration_minutes + "분" +
            "\n질문 개수: " +
            evcData.page_1.qa_count + "개"
        );
        return true;
    }

}
