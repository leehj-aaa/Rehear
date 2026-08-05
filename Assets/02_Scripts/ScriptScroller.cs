using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class ScriptScroller : MonoBehaviour
{
    [SerializeField] private ScrollRect scrollRect;
    [SerializeField, Min(0.01f)] private float scrollSpeed = 0.6f;
    [SerializeField, Range(0f, 0.95f)] private float deadZone = 0.2f;

    private InputAction scrollAction;

    private void Awake()
    {
        // Quest 오른쪽 스틱과 에디터 확인용 방향키를 같은 Vector2 액션으로 묶는다.
        scrollAction = new InputAction("Script Scroll", InputActionType.Value, expectedControlType: "Vector2");
        scrollAction.AddBinding("<XRController>{RightHand}/primary2DAxis");
        scrollAction.AddBinding("<Gamepad>/rightStick");

        scrollAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/upArrow")
            .With("Down", "<Keyboard>/downArrow");
    }

    private void OnEnable()
    {
        scrollAction?.Enable();
    }

    private void OnDisable()
    {
        scrollAction?.Disable();
    }

    private void OnDestroy()
    {
        scrollAction?.Dispose();
    }

    private void Update()
    {
        if (scrollRect == null || !scrollRect.gameObject.activeInHierarchy)
            return;

        float y = scrollAction.ReadValue<Vector2>().y;
        if (Mathf.Abs(y) < deadZone)
            return;

        scrollRect.verticalNormalizedPosition = Mathf.Clamp01(
            scrollRect.verticalNormalizedPosition + y * scrollSpeed * Time.unscaledDeltaTime);
    }
}
