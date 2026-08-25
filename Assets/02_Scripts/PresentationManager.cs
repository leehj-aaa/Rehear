using System;
using System.Collections.Generic;
using System.Threading;
using Rehear.Evc.Data;
using Rehear.Evc.Presentation;
using UnityEngine;
using UnityEngine.UI;

public class PresentationManager : MonoBehaviour
{
    public RawImage deskScreen;
    public RawImage slideScreen;
    public Texture2D[] slides;

    private int currentIndex;
    private CancellationTokenSource slideLoadCancellation;

    public event Action<int> SlideChanged;
    public int CurrentSlideIndex => currentIndex;
    public int SlideCount => slides != null ? slides.Length : 0;

    private async void Start()
    {
        slideLoadCancellation = new CancellationTokenSource();
        var urls = PresentationSessionContext.Current.Presentation?.page_2?.slide_image?.image_urls;
        if (urls != null && urls.Length > 0)
        {
            var originalSlides = slides;
            var loaded = new List<Texture2D>(urls.Length);
            var loader = new RemoteSlideLoader();
            for (var index = 0; index < urls.Length; index++)
            {
                try
                {
                    loaded.Add(await loader.LoadAsync(urls[index], 15, slideLoadCancellation.Token));
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception)
                {
                    var placeholder = originalSlides != null && index < originalSlides.Length
                        ? originalSlides[index]
                        : Texture2D.grayTexture;
                    loaded.Add(placeholder);
                    Debug.LogWarning("Remote slide download failed index=" + index + ". A placeholder is used.");
                }
            }
            slides = loaded.ToArray();
        }

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

        SlideChanged?.Invoke(currentIndex);
    }

    private void OnDestroy()
    {
        slideLoadCancellation?.Cancel();
        slideLoadCancellation?.Dispose();
    }
}
