using Rehear.Evc.Presentation;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Confirms the cached PIN session before the separate presentation/recording start.</summary>
[DefaultExecutionOrder(-50)]
public sealed class PresentationSessionReady : MonoBehaviour
{
    public PresentationController controller;
    public TMP_Text presentationTitle;
    public TMP_Text sessionType, duration, questionCount, audienceCount, environment, expertise, interest;
    public Button continueButton, returnButton;
    bool leaving;
    public bool IsOpen => gameObject.activeInHierarchy && RuntimeSessionData.IsLoaded && RuntimeSessionData.Session != null;

    void Start()
    {
        if (!RuntimeSessionData.IsLoaded || RuntimeSessionData.Session == null)
        {
            gameObject.SetActive(false);
            return;
        }

        Populate();
        if (continueButton) continueButton.interactable = true;
    }

    public void Populate()
    {
        SetText(presentationTitle, Value(RuntimeSessionData.PresentationTitle, "발표 제목 없음"));
        SetText(sessionType, Value(RuntimeSessionData.PresentationPurpose, "발표 모드"));
        SetText(duration, RuntimeSessionData.DurationMinutes + "분");
        SetText(questionCount, RuntimeSessionData.QaCount + "개");
        SetText(audienceCount, RuntimeSessionData.AudienceScale + "명");
        SetText(environment, Value(RuntimeSessionData.EnvironmentType, "미설정"));
        SetText(expertise, Value(RuntimeSessionData.AudienceExpertise, "미설정"));
        SetText(interest, Value(RuntimeSessionData.AudienceInterest, "미설정"));
    }

    static void SetText(TMP_Text target, string value)
    {
        if (target) target.text = value;
    }

    static string Value(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;

    public void Confirm()
    {
        if (leaving || !RuntimeSessionData.IsLoaded || RuntimeSessionData.Session == null) return;
        gameObject.SetActive(false);
        if (controller) controller.SetSessionConfirmationVisible(false);
    }

    public void ReturnToPin()
    {
        if (leaving || !Application.CanStreamedLevelBeLoaded("Scene_00")) return;
        leaving = true;
        if (continueButton) continueButton.interactable = false;
        if (returnButton) returnButton.interactable = false;
        RuntimeSessionData.Clear();
        PresentationSessionContext.Current.ClearAll();
        SceneManager.LoadSceneAsync("Scene_00");
    }
}
