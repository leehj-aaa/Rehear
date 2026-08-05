using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class PresentationManager : MonoBehaviour
{
    public RawImage deskScreen;
    public RawImage slideScreen;
    public Texture2D[] slides;

    private int currentIndex;
    private InputAction nextSlideAction;
    private InputAction previousSlideAction;

    private void Awake()
    {
        // Meta Quest 오른쪽 컨트롤러: A = Primary, B = Secondary.
        nextSlideAction = new InputAction("Next Slide", InputActionType.Button);
        nextSlideAction.AddBinding("<XRController>{RightHand}/primaryButton");
        nextSlideAction.AddBinding("<Keyboard>/rightArrow");

        previousSlideAction = new InputAction("Previous Slide", InputActionType.Button);
        previousSlideAction.AddBinding("<XRController>{RightHand}/secondaryButton");
        previousSlideAction.AddBinding("<Keyboard>/leftArrow");

        nextSlideAction.performed += OnNextSlidePerformed;
        previousSlideAction.performed += OnPreviousSlidePerformed;
    }

    private void Start()
    {
        if (slides != null && slides.Length > 0)
            UpdateDisplay();
    }

    private void OnEnable()
    {
        nextSlideAction?.Enable();
        previousSlideAction?.Enable();
    }

    private void OnDisable()
    {
        nextSlideAction?.Disable();
        previousSlideAction?.Disable();
    }

    private void OnDestroy()
    {
        if (nextSlideAction != null)
        {
            nextSlideAction.performed -= OnNextSlidePerformed;
            nextSlideAction.Dispose();
        }

        if (previousSlideAction != null)
        {
            previousSlideAction.performed -= OnPreviousSlidePerformed;
            previousSlideAction.Dispose();
        }
    }

    private void OnNextSlidePerformed(InputAction.CallbackContext context) => NextSlide();
    private void OnPreviousSlidePerformed(InputAction.CallbackContext context) => PrevSlide();

    public void NextSlide()
    {
        if (slides == null || currentIndex >= slides.Length - 1)
            return;

        currentIndex++;
        UpdateDisplay();
    }

    public void PrevSlide()
    {
        if (slides == null || currentIndex <= 0)
            return;

        currentIndex--;
        UpdateDisplay();
    }

    private void UpdateDisplay()
    {
        if (slides == null || slides.Length == 0)
            return;

        if (deskScreen != null)
            deskScreen.texture = slides[currentIndex];

        if (slideScreen != null)
            slideScreen.texture = slides[currentIndex];
    }
}
