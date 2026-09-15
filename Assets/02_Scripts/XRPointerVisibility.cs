using UnityEngine;

/// <summary>
/// Keeps XR interaction enabled while showing or hiding only the controller ray visuals.
/// </summary>
public static class XRPointerVisibility
{
    private const string LineVisualName = "LineVisual";

    public static void SetVisible(bool visible)
    {
        var transforms = Object.FindObjectsByType<Transform>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        foreach (var target in transforms)
        {
            if (target.name == LineVisualName && target.gameObject.activeSelf != visible)
                target.gameObject.SetActive(visible);
        }
    }
}
