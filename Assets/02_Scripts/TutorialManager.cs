using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class TutorialManager : MonoBehaviour
{
    private enum TutorialStep
    {
        ControllerGuide,
        PracticeIntro,
        TriggerPractice,
        SlidePractice,
        RearSlidePractice,
        ScriptPractice,
        PausePractice,
        PauseResumePractice,
        Complete
    }

    [Header("Common UI")]
    [SerializeField] private Image stageImage;
    [SerializeField] private Image progressImage;
    [SerializeField] private Button primaryButton;
    [SerializeField] private TMP_Text primaryButtonText;
    [SerializeField] private Button secondaryButton;
    [SerializeField] private TMP_Text secondaryButtonText;
    

    [Header("Stage Sprites")]
    [SerializeField] private Sprite controllerGuideSprite;
    [SerializeField] private Sprite tutorial1Sprite;
    [SerializeField] private Sprite tutorial2Sprite;
    [SerializeField] private Sprite tutorial3Sprite;
    [SerializeField] private Sprite tutorial3RearSprite;
    [SerializeField] private Sprite tutorial4Sprite;
    [SerializeField] private Sprite tutorial5Sprite;
    [SerializeField] private Sprite tutorial6Sprite;

    [Header("Tutorial TTS")]
    [SerializeField] private AudioSource tutorialTtsAudioSource;

    [SerializeField] private AudioClip controllerGuideTts;
    [SerializeField] private AudioClip tutorial1Tts;
    [SerializeField] private AudioClip tutorial2Tts;
    [SerializeField] private AudioClip tutorial3Tts;
    [SerializeField] private AudioClip tutorial3RearTts;
    [SerializeField] private AudioClip tutorial4Tts;
    [SerializeField] private AudioClip tutorial5Tts;
    [SerializeField] private AudioClip tutorial5_1Tts;
    [SerializeField] private AudioClip tutorial6Tts;

    [Header("Progress Sprites")]
    [SerializeField] private Sprite progress0Sprite;
    [SerializeField] private Sprite progress1Sprite;
    [SerializeField] private Sprite progress2Sprite;
    [SerializeField] private Sprite progress3Sprite;
    [SerializeField] private Sprite progress4Sprite;

    [Header("Practice Objects")]
    [SerializeField] private GameObject tutorial5_1Object;
    [SerializeField] private GameObject timerStopPanel;
    [SerializeField] private GameObject scriptPanel;
    [SerializeField] private PresentationManager presentationManager;
    [SerializeField] private ScriptScroller scriptScroller;

    [Header("Practice Feedback Sound")]
    [SerializeField] private AudioSource practiceAudioSource;
    [SerializeField] private AudioClip stickInputSound;
    [SerializeField] private AudioClip gripInputSound;
    [SerializeField] private AudioClip stepSuccessSound;
    [SerializeField, Range(0f, 1f)] private float inputSoundVolume = 0.7f;
    [SerializeField, Range(0f, 1f)] private float successSoundVolume = 0.8f;


    [Header("Rear View Detection")]
    [SerializeField] private Transform xrCamera;
    [SerializeField] private Transform rearSlideTarget;
    [SerializeField, Range(10f, 90f)] private float rearFacingAngle = 45f;

    

    [Header("Input")]
    [SerializeField, Range(0.5f, 0.95f)] private float pressThreshold = 0.7f;
    [SerializeField, Range(0.05f, 0.5f)] private float releaseThreshold = 0.3f;
    [SerializeField] private int requiredPracticeCount = 3;

    [Header("Scenes")]
    [SerializeField] private string pinSceneName = "Scene_01_Intro";

    private TutorialStep currentStep;
    private InputAction stickAction;
    private InputAction gripAction;
    private bool stickLatched;
    private int practiceCount;
    private float lastButtonTime = -10f;

    private void Awake()
    {
        stickAction = new InputAction(
            "Tutorial Stick",
            InputActionType.Value,
            expectedControlType: "Vector2");

        stickAction.AddBinding("<XRController>{RightHand}/primary2DAxis");
        stickAction.AddBinding("<Gamepad>/rightStick");
        stickAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/upArrow")
            .With("Down", "<Keyboard>/downArrow")
            .With("Left", "<Keyboard>/leftArrow")
            .With("Right", "<Keyboard>/rightArrow");

        gripAction = new InputAction("Tutorial Grip", InputActionType.Button);
        gripAction.AddBinding("<XRController>{RightHand}/gripButton");
        gripAction.AddBinding("<Keyboard>/p");
        gripAction.performed += OnGripPerformed;
    }

    private void Start()
    {
        Time.timeScale = 1f;

        if (timerStopPanel != null)
            timerStopPanel.SetActive(false);

        if (tutorial5_1Object != null)
            tutorial5_1Object.SetActive(false);

        if (scriptPanel != null)
            scriptPanel.SetActive(false);

        ShowControllerGuide();
    }

    private void OnEnable()
    {
        stickAction?.Enable();
        gripAction?.Enable();
    }

    private void OnDisable()
    {
        stickAction?.Disable();
        gripAction?.Disable();
    }

    private void OnDestroy()
    {
        if (gripAction != null)
            gripAction.performed -= OnGripPerformed;

        stickAction?.Dispose();
        gripAction?.Dispose();
    }

    private void PlayTutorialTts(AudioClip clip)
    {
        if (tutorialTtsAudioSource == null || clip == null)
            return;

        // 이전 단계 TTS가 아직 재생 중이라면 중단
        tutorialTtsAudioSource.Stop();

        tutorialTtsAudioSource.clip = clip;
        tutorialTtsAudioSource.Play();
    }

    private void Update()
    {
        // Stick input is deliberately ignored outside its own practice steps.
        if (currentStep != TutorialStep.SlidePractice &&
            currentStep != TutorialStep.RearSlidePractice &&
            currentStep != TutorialStep.ScriptPractice)
        {
            stickLatched = false;
            return;
        }
    // 뒤쪽 슬라이드 연습은 실제로 뒤 화면을 보고 있을 때만 입력을 받는다.
if (currentStep == TutorialStep.RearSlidePractice &&
    !IsLookingAtRearSlide())
    {
        // 앞을 보면서 미리 스틱을 기울인 입력이
        // 뒤를 봤을 때 바로 계산되지 않도록 잠근다.
        stickLatched = true;
        return;
    }
        Vector2 stick = stickAction.ReadValue<Vector2>();
        float strongestAxis = Mathf.Max(Mathf.Abs(stick.x), Mathf.Abs(stick.y));

        if (stickLatched)
        {
            if (strongestAxis <= releaseThreshold)
                stickLatched = false;

            return;
        }

        if (strongestAxis < pressThreshold)
            return;

       if (currentStep == TutorialStep.SlidePractice ||
            currentStep == TutorialStep.RearSlidePractice)
        {
            if (Mathf.Abs(stick.x) <= Mathf.Abs(stick.y))
                return;

            stickLatched = true;
            PlayPracticeSound(stickInputSound);

            if (stick.x > 0f)
                presentationManager?.NextSlide();
            else
                presentationManager?.PrevSlide();

             if (currentStep == TutorialStep.SlidePractice)
            {
                // 앞 슬라이드 3회 후 뒤 디스플레이 연습으로 이동
                CountStickPractice(TutorialStep.RearSlidePractice);
            }
            else
            {
                // 뒤 디스플레이에서도 3회 후 대본 연습으로 이동
                CountStickPractice(TutorialStep.ScriptPractice);
            }
            return;
        }

        if (Mathf.Abs(stick.y) <= Mathf.Abs(stick.x))
            return;

        stickLatched = true;
        PlayPracticeSound(stickInputSound);

        if (stick.y < 0f)
            scriptScroller?.NextPage();
        else
            scriptScroller?.PreviousPage();

        CountStickPractice(TutorialStep.PausePractice);
    }

    // Connect both Button.OnClick and XR Simple Interactable.Select Entered here.
    // The short guard prevents one physical click from being counted twice.
    public void OnPrimaryButtonPressed()
    {
        if (!AcceptButtonEvent())
            return;

        switch (currentStep)
        {
            case TutorialStep.ControllerGuide:
                ShowPracticeIntro();
                break;

            case TutorialStep.PracticeIntro:
                ShowTriggerPractice();
                break;

            case TutorialStep.TriggerPractice:
                CountTriggerPractice();
                break;

            case TutorialStep.Complete:
                LoadPinScene();
                break;
        }
    }

    public void OnSecondaryButtonPressed()
    {
        if (!AcceptButtonEvent())
            return;

        if (currentStep == TutorialStep.ControllerGuide)
        {
            LoadPinScene();
        }
        else if (currentStep == TutorialStep.Complete)
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }
    }

    private bool AcceptButtonEvent()
    {
        if (Time.unscaledTime - lastButtonTime < 0.15f)
            return false;

        lastButtonTime = Time.unscaledTime;
        return true;
    }

    private bool IsLookingAtRearSlide()
    {
        if (xrCamera == null || rearSlideTarget == null)
            return false;

        Vector3 cameraForward = xrCamera.forward;
        Vector3 directionToTarget =
            rearSlideTarget.position - xrCamera.position;

        // 고개를 위아래로 기울인 것은 제외하고 좌우 회전만 검사
        cameraForward.y = 0f;
        directionToTarget.y = 0f;

        if (cameraForward.sqrMagnitude < 0.001f ||
            directionToTarget.sqrMagnitude < 0.001f)
        {
            return false;
        }

        float angle = Vector3.Angle(
            cameraForward.normalized,
            directionToTarget.normalized
        );

        return angle <= rearFacingAngle;
    }

    private void CountTriggerPractice()
    {
        practiceCount++;

        if (practiceCount == 1)
        {
            SetPrimaryButton(true, "한 번 더!");
        }
        else if (practiceCount == 2)
        {
            SetPrimaryButton(true, "마지막으로!");
        }
        else if (practiceCount >= requiredPracticeCount)
        {
            ShowSlidePractice();
        }
    }

    private void CountStickPractice(TutorialStep nextStep)
    {
        practiceCount++;

        if (practiceCount < requiredPracticeCount)
            return;

        if (nextStep == TutorialStep.RearSlidePractice)
        {
            ShowRearSlidePractice();
        }
        else if (nextStep == TutorialStep.ScriptPractice)
        {
            ShowScriptPractice();
        }
        else
        {
            ShowPausePractice();
        }
    }

    private void OnGripPerformed(InputAction.CallbackContext context)
    {
        // Grip is deliberately ignored until the pause practice step.
        if (currentStep == TutorialStep.PausePractice)
        {
            currentStep = TutorialStep.PauseResumePractice;

            if (stageImage != null)
                stageImage.gameObject.SetActive(false);

            if (progressImage != null)
                progressImage.gameObject.SetActive(false);

            if (timerStopPanel != null)
            {
                timerStopPanel.SetActive(true);
                timerStopPanel.transform.SetAsLastSibling();
            }

            if (tutorial5_1Object != null)
            {
                tutorial5_1Object.SetActive(true);
                tutorial5_1Object.transform.SetAsLastSibling();
            }
            PlayTutorialTts(tutorial5_1Tts);

            return;
        }

        if (currentStep != TutorialStep.PauseResumePractice)
            return;

        PlayPracticeSound(gripInputSound);

        if (timerStopPanel != null)
            timerStopPanel.SetActive(false);

        if (tutorial5_1Object != null)
            tutorial5_1Object.SetActive(false);

        ShowComplete();
    }

    private void ShowControllerGuide()
    {
        currentStep = TutorialStep.ControllerGuide;
        SetStage(controllerGuideSprite, false, null,controllerGuideTts);
        SetPrimaryButton(true, "튜토리얼 시작하기");
        SetSecondaryButton(true, "건너뛰기");
    }

    private void ShowPracticeIntro()
    {
        currentStep = TutorialStep.PracticeIntro;
        SetStage(tutorial1Sprite, false, null, tutorial1Tts);
        SetPrimaryButton(true, "시작하기");
        SetSecondaryButton(false, string.Empty);
    }

    private void PlayPracticeSound(AudioClip clip)
    {
        if (practiceAudioSource == null || clip == null)
            return;

        practiceAudioSource.PlayOneShot(clip, inputSoundVolume);
    }
    private void PlayStepSuccessSound()
    {
        if (practiceAudioSource == null || stepSuccessSound == null)
            return;

        practiceAudioSource.PlayOneShot(
            stepSuccessSound,
            successSoundVolume
        );
    }
    private void ShowTriggerPractice()
    {
        currentStep = TutorialStep.TriggerPractice;
        practiceCount = 0;
        SetStage(tutorial2Sprite, true, progress0Sprite, tutorial2Tts);
        SetPrimaryButton(true, "눌러보기");
        SetSecondaryButton(false, string.Empty);
    }

    private void ShowSlidePractice()
    {
        PlayStepSuccessSound();

        currentStep = TutorialStep.SlidePractice;
        practiceCount = 0;
        stickLatched = true;
        SetStage(tutorial3Sprite, true, progress1Sprite, tutorial3Tts);
        HideButtons();
    }

    private void ShowRearSlidePractice()
    {
        PlayStepSuccessSound();

        currentStep = TutorialStep.RearSlidePractice;

        // 앞 슬라이드 연습에서 셌던 횟수를 초기화
        practiceCount = 0;

        // 스틱을 가운데로 되돌린 뒤부터 다시 입력받기
        stickLatched = true;
        

        SetStage(
            tutorial3RearSprite,
            true,
            progress1Sprite,
            tutorial3RearTts
        );

        HideButtons();
    }

    private void ShowScriptPractice()
    {
        PlayStepSuccessSound();

        currentStep = TutorialStep.ScriptPractice;
        practiceCount = 0;
        stickLatched = true;
        SetStage(tutorial4Sprite, true, progress2Sprite, tutorial4Tts);
        HideButtons();

        if (scriptPanel != null)
            scriptPanel.SetActive(true);

        scriptScroller?.ResetToFirstPage();
    }

    private void ShowPausePractice()
    {
        PlayStepSuccessSound();

        currentStep = TutorialStep.PausePractice;
        practiceCount = 0;
        stickLatched = false;

        if (scriptPanel != null)
            scriptPanel.SetActive(false);

        SetStage(tutorial5Sprite, true, progress3Sprite, tutorial5Tts);
        HideButtons();
    }

    private void ShowComplete()
    {
        PlayStepSuccessSound();

        currentStep = TutorialStep.Complete;
        SetStage(tutorial6Sprite, true, progress4Sprite, tutorial6Tts);
        SetPrimaryButton(true, "세션 시작하기");
        SetSecondaryButton(true, "처음으로 돌아가기");
    }

    private void SetStage(Sprite sprite, bool showProgress, Sprite progressSprite, AudioClip ttsClip)
    {
        if (stageImage != null)
        {
            stageImage.gameObject.SetActive(true);
            stageImage.sprite = sprite;
        }

        if (progressImage != null)
        {
            progressImage.gameObject.SetActive(showProgress);
            if (showProgress)
                progressImage.sprite = progressSprite;
        }
         PlayTutorialTts(ttsClip);
    }

    private void HideButtons()
    {
        SetPrimaryButton(false, string.Empty);
        SetSecondaryButton(false, string.Empty);
    }

    private void SetPrimaryButton(bool visible, string label)
    {
        if (primaryButton != null)
            primaryButton.gameObject.SetActive(visible);

        if (primaryButtonText != null)
            primaryButtonText.text = label;
    }

    private void SetSecondaryButton(bool visible, string label)
    {
        if (secondaryButton != null)
            secondaryButton.gameObject.SetActive(visible);

        if (secondaryButtonText != null)
            secondaryButtonText.text = label;
    }

    private void LoadPinScene()
    {
        PlayerPrefs.DeleteKey("ShowSessionReadyOnLoad");
        PlayerPrefs.Save();

        SceneManager.LoadScene(pinSceneName);
    }
}
