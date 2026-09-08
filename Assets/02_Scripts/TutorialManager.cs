using System.Collections;
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
    [SerializeField] private TutorialFigmaView figmaView;
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

    [Header("Quest 3 Controller Tutorial")]
    [SerializeField] private Quest3TutorialControllerVisual controllerVisual;
    [SerializeField] private TutorialControlGuideView controlGuide;
    [SerializeField, Min(1.35f)] private float demonstrationDuration = 2.7f;

    [Header("Practice Feedback Sound")]
    [SerializeField] private AudioSource practiceAudioSource;
    [SerializeField] private AudioClip stickInputSound;
    [SerializeField] private AudioClip gripInputSound;
    [SerializeField] private AudioClip stepSuccessSound;
    [SerializeField] private AudioClip triggerPromptSound;
    [SerializeField] private AudioClip triggerInputSound;
    [SerializeField] private AudioClip scriptBoundarySound;
    [SerializeField] private AudioClip slideBoundarySound;
    [SerializeField] private TMP_Text scriptRemainingText;
    [SerializeField, Range(0f, 1f)] private float triggerPromptVolume = 0.35f;
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
    [UnityEngine.Serialization.FormerlySerializedAs("pinSceneName")]
    [SerializeField] private string presentationSceneName = "Scene_02_Presentation";

    private TutorialStep currentStep;
    private InputAction stickAction;
    private InputAction gripAction;
    private bool stickLatched;
    private int practiceCount;
    private bool scriptCompletionPending;
    private bool slideCompletionPending;
    private float slideCompletionAt;
    private float scriptCompletionAt;
    private float lastButtonTime = -10f;
    private Transform tutorialRig;
    private Vector3 authoredRigPosition;
    private float authoredRigYaw;
    private bool demonstrating;
    private bool waitingForGuideRelease;
    private float demonstrationReadyAt;
    private InputAction triggerAction;

    private void Awake()
    {
        FindAndRememberTutorialRig();
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
        triggerAction = new InputAction("Tutorial Trigger Release", InputActionType.Button,
            "<XRController>{RightHand}/triggerButton");
    }

    private void Start()
    {
        Time.timeScale = 1f;

        StartCoroutine(StabilizeTutorialRig());

        if (timerStopPanel != null)
            timerStopPanel.SetActive(false);

        if (tutorial5_1Object != null)
            tutorial5_1Object.SetActive(false);

        if (scriptPanel != null)
            scriptPanel.SetActive(false);

        ShowControllerGuide();
    }

    private void FindAndRememberTutorialRig()
    {
        if (xrCamera == null && Camera.main != null)
            xrCamera = Camera.main.transform;

        Transform candidate = xrCamera;
        while (candidate != null)
        {
            if (candidate.name.Contains("XR Origin"))
            {
                tutorialRig = candidate;
                break;
            }

            candidate = candidate.parent;
        }

        if (tutorialRig == null)
            return;

        authoredRigPosition = tutorialRig.position;
        authoredRigYaw = tutorialRig.eulerAngles.y;

        CharacterController controller =
            tutorialRig.GetComponent<CharacterController>();
        if (controller != null)
            controller.enabled = false;

        foreach (MonoBehaviour behaviour in
            tutorialRig.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour == null)
                continue;

            string typeName = behaviour.GetType().Name;
            if (typeName.Contains("MoveProvider") ||
                typeName.Contains("CharacterControllerDriver") ||
                typeName.Contains("GravityProvider"))
            {
                behaviour.enabled = false;
            }
        }
    }

    private IEnumerator StabilizeTutorialRig()
    {
        // Quest 추적 원점이 적용된 뒤 실제 HMD 위치를 씬의 시작점에 맞춘다.
        yield return null;
        yield return null;
        yield return null;

        if (tutorialRig == null || xrCamera == null)
            yield break;

        float yawDelta = Mathf.DeltaAngle(
            xrCamera.eulerAngles.y,
            authoredRigYaw
        );
        tutorialRig.RotateAround(
            xrCamera.position,
            Vector3.up,
            yawDelta
        );

        Vector3 planarOffset =
            xrCamera.position - tutorialRig.position;
        planarOffset.y = 0f;

        tutorialRig.position = new Vector3(
            authoredRigPosition.x - planarOffset.x,
            authoredRigPosition.y,
            authoredRigPosition.z - planarOffset.z
        );

        Debug.Log("[튜토리얼] 플레이어 시작 위치와 방향을 자동 보정했습니다.");
    }

    private void OnEnable()
    {
        stickAction?.Enable();
        gripAction?.Enable();
        triggerAction?.Enable();
    }

    private void OnDisable()
    {
        stickAction?.Disable();
        gripAction?.Disable();
        triggerAction?.Disable();
        controllerVisual?.Show(Quest3TutorialControllerVisual.Cue.Hidden);
    }

    private void OnDestroy()
    {
        if (gripAction != null)
            gripAction.performed -= OnGripPerformed;

        stickAction?.Dispose();
        gripAction?.Dispose();
        triggerAction?.Dispose();
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
        if (ProcessDemonstration(Time.unscaledTime)) return;
        if (ProcessSlideCompletion(Time.unscaledTime)) return;
        if (ProcessScriptCompletion(Time.unscaledTime)) return;
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
            HandleSlidePracticeInput(stick.x);
            return;
        }

        if (Mathf.Abs(stick.y) <= Mathf.Abs(stick.x))
            return;

        stickLatched = true;
        HandleScriptPracticeInput(stick.y);
    }

    private bool HandleSlidePracticeInput(float direction)
    {
        if (demonstrating || slideCompletionPending || !presentationManager || direction == 0f) return false;
        var display = currentStep == TutorialStep.RearSlidePractice ? presentationManager.slideScreen : presentationManager.deskScreen;
        if (!display || !display.isActiveAndEnabled) return false;
        bool changed = direction > 0f ? presentationManager.TryNextSlide() : presentationManager.TryPrevSlide();
        if (!changed)
        {
            if (presentationManager.IsAtSlideBoundary(direction > 0f) && practiceAudioSource && slideBoundarySound)
                practiceAudioSource.PlayOneShot(slideBoundarySound, .35f);
            return false;
        }
        PlayPracticeSound(stickInputSound);
        practiceCount++;
        if (practiceCount >= requiredPracticeCount)
        {
            // Leave the final pressed state visible before hiding this step's hints.
            slideCompletionPending = true;
            slideCompletionAt = Time.unscaledTime + .2f;
        }
        return true;
    }

    private bool ProcessSlideCompletion(float now)
    {
        if (!slideCompletionPending) return false;
        if (now >= slideCompletionAt)
        {
            slideCompletionPending = false;
            if (currentStep == TutorialStep.SlidePractice) BeginDemonstration(TutorialStep.RearSlidePractice);
            else if (currentStep == TutorialStep.RearSlidePractice) BeginDemonstration(TutorialStep.ScriptPractice);
        }
        return true;
    }

    private bool HandleScriptPracticeInput(float direction)
    {
        if (demonstrating || scriptCompletionPending || scriptScroller == null || direction == 0f) return false;
        bool changed = direction < 0f ? scriptScroller.TryNextPage() : scriptScroller.TryPreviousPage();
        if (!changed)
        {
            if (scriptScroller.IsAtPageBoundary(direction < 0f) && practiceAudioSource && scriptBoundarySound)
                practiceAudioSource.PlayOneShot(scriptBoundarySound, .35f);
            return false;
        }

        // Count and acknowledge actual page changes, never blocked boundary inputs.
        PlayPracticeSound(stickInputSound);
        practiceCount++;
        UpdateScriptRemaining();
        if (practiceCount >= requiredPracticeCount)
        {
            scriptCompletionPending = true;
            scriptCompletionAt = Time.unscaledTime + .65f;
        }
        return true;
    }

    private void UpdateScriptRemaining()
    {
        if (scriptRemainingText)
        {
            int remaining = Mathf.Max(0, requiredPracticeCount - practiceCount);
            scriptRemainingText.text = remaining > 0 ? remaining + "회 남음" : "완료!";
        }
    }

    private bool ProcessScriptCompletion(float now)
    {
        if (!scriptCompletionPending) return false;
        if (now >= scriptCompletionAt)
        {
            scriptCompletionPending = false;
            BeginDemonstration(TutorialStep.PausePractice);
        }
        return true;
    }

    // Connect both Button.OnClick and XR Simple Interactable.Select Entered here.
    // The short guard prevents one physical click from being counted twice.
    public void OnPrimaryButtonPressed()
    {
        if (demonstrating)
        {
            if (Time.unscaledTime >= demonstrationReadyAt && !waitingForGuideRelease)
            {
                waitingForGuideRelease = true;
                controlGuide?.SetWaitingForRelease();
            }
            return;
        }
        if (!AcceptButtonEvent())
            return;

        switch (currentStep)
        {
            case TutorialStep.ControllerGuide:
                ShowPracticeIntro();
                break;

            case TutorialStep.PracticeIntro:
                BeginDemonstration(TutorialStep.TriggerPractice);
                break;

            case TutorialStep.TriggerPractice:
                PlayPracticeSound(triggerInputSound);
                CountTriggerPractice();
                break;

            case TutorialStep.Complete:
                LoadPinScene();
                break;
        }
    }

    public void OnSecondaryButtonPressed()
    {
        if (demonstrating) return;
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
        var practiceButton = figmaView && figmaView.HasStepButtons
            ? figmaView.primaryButtons[(int)TutorialStep.TriggerPractice] : primaryButton;
        if (practiceButton) practiceButton.GetComponent<TutorialButtonGlow>()?.NotifyAcceptedClick();
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
            BeginDemonstration(TutorialStep.SlidePractice);
        }
    }

    private void OnGripPerformed(InputAction.CallbackContext context)
    {
        if (demonstrating) return;
        // Grip is deliberately ignored until the pause practice step.
        if (currentStep == TutorialStep.PausePractice)
        {
            currentStep = TutorialStep.PauseResumePractice;
            figmaView?.Show((int)currentStep);

            if (stageImage != null)
                stageImage.gameObject.SetActive(false);

            if (progressImage != null)
                progressImage.gameObject.SetActive(false);

            if (timerStopPanel != null && figmaView == null)
            {
                timerStopPanel.SetActive(true);
                timerStopPanel.transform.SetAsLastSibling();
            }

            if (tutorial5_1Object != null && figmaView == null)
            {
                tutorial5_1Object.SetActive(true);
                tutorial5_1Object.transform.SetAsLastSibling();
            }
            controllerVisual?.Show(Quest3TutorialControllerVisual.Cue.Hidden);
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
        controllerVisual?.Show(Quest3TutorialControllerVisual.Cue.Hidden);
        SetStage(controllerGuideSprite, false, null,controllerGuideTts);
        SetPrimaryButton(true, "튜토리얼 시작하기");
        SetSecondaryButton(true, "건너뛰기");
    }

    private void ShowPracticeIntro()
    {
        currentStep = TutorialStep.PracticeIntro;
        controllerVisual?.Show(Quest3TutorialControllerVisual.Cue.Hidden);
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
        controllerVisual?.Show(Quest3TutorialControllerVisual.Cue.Hidden);
        SetStage(tutorial2Sprite, true, progress0Sprite, tutorial2Tts);
        SetPrimaryButton(true, "눌러보기");
        SetSecondaryButton(false, string.Empty);
        var practiceButton = figmaView && figmaView.HasStepButtons
            ? figmaView.primaryButtons[(int)TutorialStep.TriggerPractice] : primaryButton;
        var glow = practiceButton ? practiceButton.GetComponent<TutorialButtonGlow>() : null;
        if (glow) glow.ConfigurePrompt(triggerPromptSound, triggerPromptVolume);
        else if (practiceAudioSource && triggerPromptSound)
            practiceAudioSource.PlayOneShot(triggerPromptSound, triggerPromptVolume);
    }

    private void ShowSlidePractice()
    {
        PlayStepSuccessSound();

        currentStep = TutorialStep.SlidePractice;
        practiceCount = 0;
        stickLatched = true;
        controllerVisual?.Show(Quest3TutorialControllerVisual.Cue.Hidden);
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

        controllerVisual?.Show(Quest3TutorialControllerVisual.Cue.Hidden);

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
        scriptCompletionPending = false;
        UpdateScriptRemaining();
        stickLatched = true;
        controllerVisual?.Show(Quest3TutorialControllerVisual.Cue.Hidden);
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

        controllerVisual?.Show(Quest3TutorialControllerVisual.Cue.Hidden);

        if (scriptPanel != null)
            scriptPanel.SetActive(false);

        SetStage(tutorial5Sprite, true, progress3Sprite, tutorial5Tts);
        HideButtons();
    }

    private void ShowComplete()
    {
        PlayStepSuccessSound();

        currentStep = TutorialStep.Complete;
        controllerVisual?.Show(Quest3TutorialControllerVisual.Cue.Hidden);
        SetStage(tutorial6Sprite, true, progress4Sprite, tutorial6Tts);
        SetPrimaryButton(true, "세션 시작하기");
        SetSecondaryButton(true, "처음으로 돌아가기");
    }

    private void BeginDemonstration(TutorialStep practice)
    {
        // Keep the nine authored Figma step indices stable. Demonstrations are a
        // separate, input-blocking phase before each practice, never a counted action.
        if (!controlGuide) { EnterPractice(practice); return; }
        currentStep = practice;
        practiceCount = 0;
        scriptCompletionPending = slideCompletionPending = false;
        demonstrating = true;
        waitingForGuideRelease = false;
        demonstrationReadyAt = Time.unscaledTime + demonstrationDuration;
        stickLatched = true;
        tutorialTtsAudioSource?.Stop();
        if (scriptPanel) scriptPanel.SetActive(false);
        if (stageImage) stageImage.gameObject.SetActive(false);
        if (progressImage) progressImage.gameObject.SetActive(false);
        HideButtons();
        figmaView?.Show(-1);
        var cue = Quest3TutorialControllerVisual.Cue.Trigger;
        string title = "검지로 트리거를 눌러요";
        string body = "오른손 컨트롤러 앞쪽의 파란 버튼을 확인해 주세요.\n버튼을 가리킨 뒤 검지로 트리거를 눌러 선택해요.";
        if (practice == TutorialStep.SlidePractice || practice == TutorialStep.RearSlidePractice)
        {
            cue = Quest3TutorialControllerVisual.Cue.StickHorizontal;
            title = "조이스틱을 좌우로 움직여요";
            body = practice == TutorialStep.RearSlidePractice
                ? "이번에는 뒤쪽 화면을 보며 슬라이드를 넘겨요.\n오른손 조이스틱을 좌우로 기울인 뒤 가운데로 놓아 주세요."
                : "오른손 조이스틱을 왼쪽·오른쪽으로 기울여요.\n한 번 넘긴 뒤 가운데로 놓으면 다시 넘길 수 있어요.";
        }
        else if (practice == TutorialStep.ScriptPractice)
        {
            cue = Quest3TutorialControllerVisual.Cue.StickVertical;
            title = "조이스틱을 위아래로 움직여요";
            body = "오른손 조이스틱을 위·아래로 기울여 대본을 넘겨요.\n한 번 넘긴 뒤 가운데로 놓으면 다시 넘길 수 있어요.";
        }
        else if (practice == TutorialStep.PausePractice)
        {
            cue = Quest3TutorialControllerVisual.Cue.Grip;
            title = "중지로 그립을 눌러요";
            body = "오른손 컨트롤러 옆면의 파란 버튼을 확인해 주세요.\n그립을 쥐면 일시정지하고, 놓았다 다시 쥐면 재개해요.";
        }
        controlGuide.Show(title, body);
        controllerVisual?.Show(cue);
    }

    private bool ProcessDemonstration(float now)
    {
        return AdvanceDemonstration(now, triggerAction?.IsPressed() ?? false,
            gripAction?.IsPressed() ?? false, stickAction?.ReadValue<Vector2>() ?? Vector2.zero);
    }

    private bool AdvanceDemonstration(float now, bool triggerHeld, bool gripHeld, Vector2 stick)
    {
        if (!demonstrating) return false;
        if (!waitingForGuideRelease)
        {
            controlGuide?.SetReady(now >= demonstrationReadyAt);
            return true;
        }
        // The click used to leave the guide must never become the first exercise
        // input. Also require the stick and grip to be released before arming.
        if (triggerHeld || gripHeld || stick.sqrMagnitude > releaseThreshold * releaseThreshold) return true;
        demonstrating = false;
        waitingForGuideRelease = false;
        controlGuide?.Hide();
        controllerVisual?.Show(Quest3TutorialControllerVisual.Cue.Hidden);
        lastButtonTime = now;
        EnterPractice(currentStep);
        return true;
    }

    private void EnterPractice(TutorialStep practice)
    {
        switch (practice)
        {
            case TutorialStep.TriggerPractice: ShowTriggerPractice(); break;
            case TutorialStep.SlidePractice: ShowSlidePractice(); break;
            case TutorialStep.RearSlidePractice: ShowRearSlidePractice(); break;
            case TutorialStep.ScriptPractice: ShowScriptPractice(); break;
            case TutorialStep.PausePractice: ShowPausePractice(); break;
        }
    }

    private void SetStage(Sprite sprite, bool showProgress, Sprite progressSprite, AudioClip ttsClip)
    {
        controlGuide?.Hide();
        figmaView?.Show((int)currentStep);
        if (stageImage != null)
        {
            stageImage.gameObject.SetActive(true);
            stageImage.sprite = sprite;
            if (figmaView != null) stageImage.enabled = false;
        }

        if (progressImage != null)
        {
            progressImage.gameObject.SetActive(showProgress && figmaView == null);
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
        if (figmaView && figmaView.SetStepButton((int)currentStep, true, visible, label))
            return;
        if (primaryButton != null)
            primaryButton.gameObject.SetActive(visible);

        if (primaryButtonText != null)
            primaryButtonText.text = label;
    }

    private void SetSecondaryButton(bool visible, string label)
    {
        if (figmaView && figmaView.SetStepButton((int)currentStep, false, visible, label))
            return;
        if (secondaryButton != null)
            secondaryButton.gameObject.SetActive(visible);

        if (secondaryButtonText != null)
            secondaryButtonText.text = label;
    }

    private void LoadPinScene()
    {
        PlayerPrefs.DeleteKey("ShowSessionReadyOnLoad");
        PlayerPrefs.Save();

        SceneManager.LoadScene(presentationSceneName);
    }
}
