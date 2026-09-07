using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Native TMP/UGUI views for Figma UI design / 튜토리얼.</summary>
public sealed class TutorialFigmaView : MonoBehaviour
{
    public GameObject[] steps;
    public RectTransform primary;
    public RectTransform secondary;
    public Material primaryMaterial;
    public Material secondaryMaterial;

    [Header("Buttons owned by each tutorial step")]
    public Button[] primaryButtons;
    public Button[] secondaryButtons;
    private bool ownsLegacyMaterials;

    public bool HasStepButtons => steps != null && primaryButtons != null &&
        secondaryButtons != null && primaryButtons.Length == steps.Length &&
        secondaryButtons.Length == steps.Length && primaryButtons.Length > 0 && primaryButtons[0];

    public void Show(int step)
    {
        for (int i = 0; i < steps.Length; i++) if (steps[i]) steps[i].SetActive(i == step);
        // Authored child buttons already have their final position, label and material.
        // Showing a step must not move or restyle another step's controls.
        if (HasStepButtons) return;
        if (!primary || !secondary) return;
        if (step == 0)
        {
            Place(primary, 232.5f, 707, 735, 56);
            Place(secondary, 232.5f, 775, 735, 56);
        }
        else if (step == 8)
        {
            Place(primary, 583, 480.5f, 340, 56);
            Place(secondary, 231, 480.5f, 340, 56);
        }
        else
        {
            Place(primary, 430, step == 1 ? 480 : 552, 340, 56);
            Place(secondary, 430, 620, 340, 56);
        }
        if (primaryMaterial) primaryMaterial.SetVector("_PanelSize", new Vector4(primary.rect.width, 56, 0, 0));
        if (secondaryMaterial)
        {
            secondaryMaterial.SetVector("_PanelSize", new Vector4(secondary.rect.width, 56, 0, 0));
            secondaryMaterial.SetFloat("_BorderWidth", step == 8 ? 2 : 0);
        }
    }

    public bool SetStepButton(int step, bool isPrimary, bool visible, string label)
    {
        if (!HasStepButtons) return false;
        var buttons = isPrimary ? primaryButtons : secondaryButtons;
        if (step < 0 || step >= buttons.Length) return true;
        var button = buttons[step];
        if (!button) return true;
        button.gameObject.SetActive(visible);
        // Only the repeated trigger exercise has a changing label. Keep all other
        // labels authored in the scene so designers can edit them in the Inspector.
        if (isPrimary && step == 2)
        {
            var text = button.GetComponentInChildren<TMP_Text>(true);
            if (text) text.text = label;
        }
        return true;
    }

    public static void Place(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }

    private void Awake()
    {
        if (HasStepButtons) return;
        ownsLegacyMaterials = true;
        // Mutable button sizes use scene-local material instances, never change assets in play mode.
        if (primaryMaterial) primary.GetComponent<Image>().material = primaryMaterial = new Material(primaryMaterial);
        if (secondaryMaterial) secondary.GetComponent<Image>().material = secondaryMaterial = new Material(secondaryMaterial);
    }
    private void OnDestroy()
    {
        if (!ownsLegacyMaterials || !Application.isPlaying) return;
        if (primaryMaterial) Destroy(primaryMaterial);
        if (secondaryMaterial) Destroy(secondaryMaterial);
    }
}
