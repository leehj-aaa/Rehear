using UnityEngine;
using UnityEngine.UI;

/// <summary>Sprite-backed direction hint. Keeps existing scene references and rotations.</summary>
[AddComponentMenu("Rehear/Tutorial Direction Arrow")]
public sealed class TutorialDirectionArrow : Image
{
    private const float PressDuration = .18f;
    private float pressUntil;
    private bool pressed;

    public void ShowPressedFeedback()
    {
        if (!isActiveAndEnabled) return;
        pressed = true;
        pressUntil = Time.realtimeSinceStartup + PressDuration;
        SetVerticesDirty();
    }

    private void Update()
    {
        if (!pressed || Time.realtimeSinceStartup < pressUntil) return;
        pressed = false;
        SetVerticesDirty();
    }

    protected override void OnDisable()
    {
        pressed = false;
        base.OnDisable();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        base.OnPopulateMesh(mesh);
        if (!pressed) return;
        // Change only rendered vertices, never authored transforms, sprite, or tint.
        Vector3 center = rectTransform.rect.center;
        UIVertex vertex = default;
        for (int i = 0; i < mesh.currentVertCount; i++)
        {
            mesh.PopulateUIVertex(ref vertex, i);
            vertex.position = center + (vertex.position - center) * .9f;
            Color tint = vertex.color;
            vertex.color = new Color(tint.r * .65f, tint.g * .65f, tint.b * .65f, tint.a);
            mesh.SetUIVertex(vertex, i);
        }
    }
}
