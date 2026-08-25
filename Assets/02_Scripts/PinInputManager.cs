using System;
using System.Threading;
using System.Threading.Tasks;
using Firebase;
using Firebase.Extensions;
using Rehear.Evc.Data;
using Rehear.Evc.Presentation;
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

    [Header("개발 빌드 전용 Fallback")]
    [SerializeField] private bool enableDemoFallback;
    [SerializeField] private string demoPin = "1234";
    [SerializeField] private string demoExpertise = "보통";
    [SerializeField] private string demoInterest = "높음";

    private const string ShowSessionReadyKey = "ShowSessionReadyOnLoad";
    private readonly PresentationSessionContext context = PresentationSessionContext.Current;
    private CancellationTokenSource lifetimeCancellation;
    private IPresentationRepository repository;
    private int currentIndex;
    private string currentPin = string.Empty;
    private bool firebaseReady;
    private bool firebaseInitializing;
    private bool isLoading;

    private void Awake()
    {
        lifetimeCancellation = new CancellationTokenSource();
    }

    private void Start()
    {
        InitializeFirebase();
        var returnFromPresentation = PlayerPrefs.GetInt(ShowSessionReadyKey, 0) == 1;
        PlayerPrefs.DeleteKey(ShowSessionReadyKey);

        if (returnFromPresentation && context.HasPresentation)
        {
            ApplyAudienceInformation(context.Presentation);
            return;
        }

        ShowPinInputPanel();
    }

    private void InitializeFirebase()
    {
        if (firebaseInitializing) return;
        firebaseInitializing = true;
        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task =>
        {
            firebaseInitializing = false;
            if (task.IsCanceled || task.IsFaulted || task.Result != DependencyStatus.Available)
            {
                firebaseReady = false;
                Debug.LogWarning("Firebase initialization failed.");
                return;
            }

            firebaseReady = true;
            repository = new FirebasePresentationRepository();
            Debug.Log("Firebase initialized.");
        });
    }

    public void OpenKeyboard()
    {
        if (numberKeyboardPanel != null) numberKeyboardPanel.SetActive(true);
    }

    public void AddNumber(string number)
    {
        if (isLoading || currentIndex >= 4 || number == null || number.Length != 1 ||
            number[0] < '0' || number[0] > '9' ||
            pinTextSlots == null || currentIndex >= pinTextSlots.Length)
            return;

        if (pinTextSlots[currentIndex] != null) pinTextSlots[currentIndex].text = number;
        currentPin += number;
        currentIndex++;
        ClearError();
        if (currentIndex >= 4 && numberKeyboardPanel != null) numberKeyboardPanel.SetActive(false);
    }

    public void ResetInput()
    {
        if (isLoading) return;
        currentPin = string.Empty;
        currentIndex = 0;
        context.ClearAll();
        if (pinTextSlots != null)
        {
            foreach (var slot in pinTextSlots)
            {
                if (slot != null) slot.text = string.Empty;
            }
        }
        ClearError();
        if (numberKeyboardPanel != null) numberKeyboardPanel.SetActive(true);
    }

    public string GetFullPin() => currentPin;

    public void OnSubmitButtonClicked()
    {
        _ = SubmitAsync();
    }

    private async Task SubmitAsync()
    {
        if (isLoading) return;
        var pin = GetFullPin();
        if (!PresentationDataValidator.IsFourDigitPin(pin))
        {
            ShowError("PIN 번호 4자리를 모두 입력해주세요.");
            return;
        }

        if (!firebaseReady || repository == null)
        {
            if (TryLoadDemoFallback(pin)) return;
            ShowError("서버에 연결 중입니다. 잠시 후 다시 시도해주세요.");
            InitializeFirebase();
            return;
        }

        isLoading = true;
        ShowError("세션 정보를 불러오는 중입니다.");
        try
        {
            var result = await repository.LoadAsync(pin, lifetimeCancellation.Token);
            if (!result.IsSuccess)
            {
                Debug.LogWarning("Presentation load failed: " + result.Status);
                if (!TryLoadDemoFallback(pin)) ShowError(result.UserMessage);
                return;
            }
            ApplyPresentation(result.Data);
        }
        catch (OperationCanceledException)
        {
            ShowError("세션 불러오기가 취소되었습니다.");
        }
        finally
        {
            isLoading = false;
        }
    }

    private void ApplyPresentation(PresentationDataDto data)
    {
        var validation = context.BeginPresentation(data);
        if (!validation.IsValid)
        {
            Debug.LogWarning("Presentation data validation failed: " + string.Join(" | ", validation.Errors));
            ShowError("세션 정보가 올바르지 않습니다. 관리자에게 문의해주세요.");
            return;
        }

        ApplyAudienceInformation(data);
        Debug.Log(data.is_demo_fallback ? "Demo presentation context is ready." : "Firebase presentation context is ready.");
    }

    private bool TryLoadDemoFallback(string pin)
    {
        if (!enableDemoFallback || !Debug.isDebugBuild || pin != demoPin) return false;
        ApplyPresentation(new PresentationDataDto
        {
            pin = demoPin,
            presentation_title = "Rehear Demo Presentation",
            page_1 = new PresentationPage1Dto
            {
                duration_minutes = 1,
                environment_type = "demo",
                qa_count = 3
            },
            page_2 = new PresentationPage2Dto
            {
                presentation_script_content = "데모 발표 대본",
                slide_image = new SlideImageDto { image_urls = Array.Empty<string>() }
            },
            page_3 = new PresentationPage3Dto
            {
                audience_expertise = demoExpertise,
                audience_interest = demoInterest,
                audience_scale = 6
            },
            is_demo_fallback = true
        });
        return true;
    }

    private void ApplyAudienceInformation(PresentationDataDto data)
    {
        if (expertiseValueText != null) expertiseValueText.text = data.page_3.audience_expertise;
        if (interestValueText != null) interestValueText.text = data.page_3.audience_interest;
        ClearError();
        ShowSessionReadyPanel();
    }

    private void ShowPinInputPanel()
    {
        if (panel_PinInput != null) panel_PinInput.SetActive(true);
        if (numberKeyboardPanel != null) numberKeyboardPanel.SetActive(false);
        if (panel_SessionReady != null) panel_SessionReady.SetActive(false);
    }

    private void ShowSessionReadyPanel()
    {
        if (panel_PinInput != null) panel_PinInput.SetActive(false);
        if (numberKeyboardPanel != null) numberKeyboardPanel.SetActive(false);
        if (panel_SessionReady != null)
        {
            panel_SessionReady.SetActive(true);
            panel_SessionReady.transform.SetAsLastSibling();
        }
    }

    private void ShowError(string message)
    {
        if (errorText != null) errorText.text = message;
    }

    private void ClearError()
    {
        if (errorText != null) errorText.text = string.Empty;
    }

    private void OnDestroy()
    {
        lifetimeCancellation?.Cancel();
        lifetimeCancellation?.Dispose();
    }
}
