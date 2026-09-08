using UnityEngine;
using UnityEngine.InputSystem;

public class PresentationInputController : MonoBehaviour
{
    [Header("기능 연결")]
    [SerializeField] private PresentationManager presentationManager;
    [SerializeField] private ScriptScroller scriptPaginator;
    [SerializeField] private PresentationController presentationController;

    [Header("스틱 판정")]
    [SerializeField, Range(0.5f, 0.95f)] private float pressThreshold = 0.7f;
    [SerializeField, Range(0.05f, 0.5f)] private float releaseThreshold = 0.3f;

    private InputAction stickAction;
    private InputAction gripAction;
    private bool stickLatched;

    private void Awake()
    {
        stickAction = new InputAction(
            "Presentation Stick",
            InputActionType.Value,
            expectedControlType: "Vector2");

        stickAction.AddBinding("<XRController>{RightHand}/primary2DAxis");
        stickAction.AddBinding("<Gamepad>/rightStick");
        stickAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/upArrow")
            .With("Down", "<Keyboard>/downArrow")
            .With("Left", "<Keyboard>/leftArrow")
            .With("Right", "<Keyboard>/rightArrow");

        gripAction = new InputAction("Pause Grip", InputActionType.Button);
        gripAction.AddBinding("<XRController>{RightHand}/gripButton");
        gripAction.AddBinding("<Keyboard>/p");
        gripAction.performed += OnGripPerformed;
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

    private void Update()
    {
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

        stickLatched = true;

        if (presentationController != null && (presentationController.IsPaused || presentationController.IsConfirmingSession))
            return;

        // 대각선 입력은 절댓값이 더 큰 축 하나만 실행한다.
        if (Mathf.Abs(stick.x) > Mathf.Abs(stick.y))
        {
            if (stick.x > 0f)
                presentationManager?.NextSlide();
            else
                presentationManager?.PrevSlide();
        }
        else
        {
            // Quest 스틱: 아래(-Y)는 다음 페이지, 위(+Y)는 이전 페이지.
            if (stick.y < 0f)
                scriptPaginator?.NextPage();
            else
                scriptPaginator?.PreviousPage();
        }
    }

    private void OnGripPerformed(InputAction.CallbackContext context)
    {
        presentationController?.TogglePause();
    }
}
