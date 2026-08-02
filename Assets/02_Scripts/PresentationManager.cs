using UnityEngine;
using UnityEngine.UI;

public class PresentationManager : MonoBehaviour
{
    public RawImage deskScreen;
    public RawImage slideScreen;

    // 인스펙터에서 사진들을 드래그해서 순서대로 넣으세요
    public Texture2D[] slides; 
    private int currentIndex = 0;

    void Start()
    {
        if (slides.Length > 0) UpdateDisplay();
    }

    public void NextSlide()
    {
        if (currentIndex < slides.Length - 1)
        {
            currentIndex++;
            UpdateDisplay();
        }
    }

    public void PrevSlide()
    {
        if (currentIndex > 0)
        {
            currentIndex--;
            UpdateDisplay();
        }
    }

    void UpdateDisplay()
    {
        deskScreen.texture = slides[currentIndex];
        slideScreen.texture = slides[currentIndex];
    }
}