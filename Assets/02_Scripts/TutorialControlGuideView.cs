using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Self-paced controller demonstration, using the tutorial's authored glass/TMP style.</summary>
public sealed class TutorialControlGuideView : MonoBehaviour
{
    public TMP_Text title;
    public TMP_Text description;
    public Button continueButton;
    public TMP_Text continueLabel;

    public void Show(string heading, string body)
    {
        title.text = heading;
        description.text = body;
        gameObject.SetActive(true);
        SetReady(false);
    }

    public void SetReady(bool ready)
    {
        continueButton.interactable = ready;
        continueLabel.text = ready ? "직접 해보기" : "동작을 확인해 주세요";
    }

    public void SetWaitingForRelease()
    {
        continueButton.interactable = false;
        continueLabel.text = "버튼과 조이스틱을 놓아 주세요";
    }

    public void Hide() => gameObject.SetActive(false);
}
