using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class PresentationManager : MonoBehaviour
{
    [Header("화면")]
    public RawImage deskScreen;
    public RawImage slideScreen;

    [Header("Optional slide direction feedback")]
    [SerializeField] private TutorialDirectionArrow previousSlideHint;
    [SerializeField] private TutorialDirectionArrow nextSlideHint;

    [Header("기본 발표자료")]
    public Texture2D[] slides;

    [Header("Firebase 자료")]
    [SerializeField]
    private bool useRuntimeSessionSlides = true;

    [SerializeField]
    [Min(5)]
    private int downloadTimeoutSeconds = 20;

    private int currentIndex;
    public int CurrentSlideIndex => currentIndex;

    public event Action<int> SlideChanged;

    private readonly List<Texture2D>
        downloadedSlides = new();

    private void Start()
    {
        currentIndex = 0;

        // Firebase 다운로드 중에도 기존 발표자료를 바로 표시한다.
        UpdateDisplay();

        if (useRuntimeSessionSlides)
        {
            StartCoroutine(
                LoadRuntimeSessionSlides()
            );
        }
    }

    private IEnumerator LoadRuntimeSessionSlides()
    {
        string[] urls =
            RuntimeSessionData.SlideImageUrls;

        if (urls == null || urls.Length == 0)
        {
            Debug.Log(
                "[발표자료] Firebase 이미지가 없어 " +
                "기본 발표자료를 사용합니다."
            );

            yield break;
        }

        List<Texture2D> loadedTextures =
            new List<Texture2D>();

        for (int index = 0;
             index < urls.Length;
             index++)
        {
            string url = urls[index];

            if (string.IsNullOrWhiteSpace(url))
            {
                DestroyTextures(loadedTextures);

                Debug.LogWarning(
                    "[발표자료] 비어 있는 이미지 URL이 있어 " +
                    "기본 발표자료를 사용합니다."
                );

                yield break;
            }

            using UnityWebRequest request =
                UnityWebRequestTexture.GetTexture(
                    url,
                    true
                );

            request.timeout =
                downloadTimeoutSeconds;

            yield return request.SendWebRequest();

            if (request.result !=
                UnityWebRequest.Result.Success)
            {
                Debug.LogWarning(
                    "[발표자료] Firebase 이미지 다운로드 실패" +
                    "\n순서: " + (index + 1) +
                    "\n오류: " + request.error +
                    "\n기본 발표자료를 사용합니다."
                );

                DestroyTextures(loadedTextures);
                yield break;
            }

            Texture2D texture =
                DownloadHandlerTexture.GetContent(
                    request
                );

            if (texture == null)
            {
                DestroyTextures(loadedTextures);

                Debug.LogWarning(
                    "[발표자료] 다운로드한 이미지를 " +
                    "Texture로 변환하지 못했습니다."
                );

                yield break;
            }

            texture.name =
                "FirebaseSlide_" + (index + 1);

            loadedTextures.Add(texture);
        }

        if (loadedTextures.Count == 0)
            yield break;

        downloadedSlides.AddRange(
            loadedTextures
        );

        slides =
            downloadedSlides.ToArray();

        currentIndex = 0;
        UpdateDisplay();

        Debug.Log(
            "[발표자료] Firebase 이미지 적용 완료" +
            "\n슬라이드 수: " + slides.Length
        );
    }

    public bool IsAtSlideBoundary(bool next) => HasCurrentSlide() &&
        (next ? currentIndex >= slides.Length - 1 : currentIndex <= 0);

    private bool HasCurrentSlide() => slides != null && currentIndex >= 0 &&
        currentIndex < slides.Length && slides[currentIndex];

    // Preserve the void UnityEvent API used by other presentation scenes.
    public void NextSlide() => TryNextSlide();
    public void PrevSlide() => TryPrevSlide();

    public bool TryNextSlide()
    {
        if (!HasCurrentSlide()) return false;
        nextSlideHint?.ShowPressedFeedback();
        if (currentIndex >= slides.Length - 1 || !slides[currentIndex + 1])
        {
            return false;
        }

        currentIndex++;
        UpdateDisplay();
        SlideChanged?.Invoke(currentIndex);
        return true;
    }

    public bool TryPrevSlide()
    {
        if (!HasCurrentSlide()) return false;
        previousSlideHint?.ShowPressedFeedback();
        if (currentIndex <= 0 || !slides[currentIndex - 1])
        {
            return false;
        }

        currentIndex--;
        UpdateDisplay();
        SlideChanged?.Invoke(currentIndex);
        return true;
    }

    private void UpdateDisplay()
    {
        if (slides == null ||
            slides.Length == 0)
        {
            return;
        }

        currentIndex =
            Mathf.Clamp(
                currentIndex,
                0,
                slides.Length - 1
            );

        Texture2D currentSlide =
            slides[currentIndex];

        if (deskScreen != null)
            deskScreen.texture = currentSlide;

        if (slideScreen != null)
            slideScreen.texture = currentSlide;
    }

    private void DestroyTextures(
        List<Texture2D> textures)
    {
        foreach (Texture2D texture in textures)
        {
            if (texture != null)
                Destroy(texture);
        }

        textures.Clear();
    }

    private void OnDestroy()
    {
        DestroyTextures(downloadedSlides);
    }
}
