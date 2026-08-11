using UnityEngine;
using UnityEngine.UI;

public class PresentationManager : MonoBehaviour
{
    public RawImage deskScreen;
    public RawImage slideScreen;
    public Texture2D[] slides;

    private int currentIndex;

    private void Start()
    {
        if (slides != null && slides.Length > 0)
            UpdateDisplay();
    }

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
