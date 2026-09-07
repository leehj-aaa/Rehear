using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Independent of the script panel so hiding it never hides its toggle.</summary>
public sealed class PodiumScriptToggle : MonoBehaviour
{
    public PresentationController controller;
    public GameObject scriptPanel;
    public TMP_Text label;
    public Image background;
    bool previousVisible;

    void OnEnable() => Refresh();
    void LateUpdate()
    {
        if (scriptPanel && previousVisible != scriptPanel.activeSelf) Refresh();
    }
    public void ToggleScript()
    {
        if (!scriptPanel) return;
        if (controller && controller.scriptPanel == scriptPanel) controller.OpenScriptPanel();
        else scriptPanel.SetActive(!scriptPanel.activeSelf);
        Refresh();
    }
    public void Refresh()
    {
        previousVisible = scriptPanel && scriptPanel.activeSelf;
        if (label) label.text = previousVisible ? "대본 끄기" : "대본 켜기";
        if (background) background.color = previousVisible
            ? new Color32(0, 51, 255, 255) : new Color32(53, 56, 65, 255);
    }
}
