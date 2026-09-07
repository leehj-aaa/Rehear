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

    public void Show(int step)
    {
        for (int i = 0; i < steps.Length; i++) if (steps[i]) steps[i].SetActive(i == step);
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
        // Mutable button sizes use scene-local material instances, never change assets in play mode.
        if (primaryMaterial) primary.GetComponent<Image>().material = primaryMaterial = new Material(primaryMaterial);
        if (secondaryMaterial) secondary.GetComponent<Image>().material = secondaryMaterial = new Material(secondaryMaterial);
    }
    private void OnDestroy()
    {
        if (!Application.isPlaying) return;
        if (primaryMaterial) Destroy(primaryMaterial);
        if (secondaryMaterial) Destroy(secondaryMaterial);
    }
}
