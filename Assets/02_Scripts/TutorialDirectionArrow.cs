using UnityEngine;
using UnityEngine.UI;

/// <summary>Small, unlit UGUI direction badge; no texture, font, or input dependency.</summary>
[AddComponentMenu("Rehear/Tutorial Direction Arrow")]
public sealed class TutorialDirectionArrow : MaskableGraphic
{
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect rect = GetPixelAdjustedRect();
        Vector2 center = rect.center;
        float radius = Mathf.Min(rect.width, rect.height) * .5f;
        Disk(mesh, center, radius, new Color(1, 1, 1, color.a * .9f));
        Disk(mesh, center, radius * .93f, color);
        // A right-facing chevron. Rotate the RectTransform for the other directions.
        Segment(mesh, center + new Vector2(-.16f, .36f) * radius,
            center + new Vector2(.20f, 0) * radius, radius * .12f);
        Segment(mesh, center + new Vector2(.20f, 0) * radius,
            center + new Vector2(-.16f, -.36f) * radius, radius * .12f);
    }

    static void Disk(VertexHelper mesh, Vector2 center, float radius, Color tint)
    {
        const int segments = 48;
        int start = mesh.currentVertCount;
        mesh.AddVert(center, tint, Vector2.zero);
        for (int i = 0; i < segments; i++)
        {
            float angle = i * Mathf.PI * 2 / segments;
            mesh.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, tint, Vector2.zero);
        }
        for (int i = 0; i < segments; i++)
            mesh.AddTriangle(start, start + 1 + (i + 1) % segments, start + 1 + i);
    }

    void Segment(VertexHelper mesh, Vector2 from, Vector2 to, float thickness)
    {
        Vector2 offset = new Vector2(-(to - from).y, (to - from).x).normalized * thickness * .5f;
        int start = mesh.currentVertCount;
        Color tint = new Color(1, 1, 1, color.a);
        mesh.AddVert(from - offset, tint, Vector2.zero);
        mesh.AddVert(from + offset, tint, Vector2.zero);
        mesh.AddVert(to + offset, tint, Vector2.zero);
        mesh.AddVert(to - offset, tint, Vector2.zero);
        mesh.AddTriangle(start, start + 1, start + 2);
        mesh.AddTriangle(start + 2, start + 3, start);
    }
}
