using UnityEngine;
using UnityEngine.UI;

/// <summary>Small UI line icons expressed in the same 24-unit coordinates as their SVG source.</summary>
[RequireComponent(typeof(RectTransform), typeof(CanvasRenderer))]
public sealed class UIStrokeIcon : MaskableGraphic
{
    private Vector2[][] paths = System.Array.Empty<Vector2[]>();
    private float stroke = 1.8f;
    public void SetPaths(Vector2[][] value, float width = 1.8f)
    { paths = value; stroke = width; raycastTarget = false; SetVerticesDirty(); }
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear(); var rect = rectTransform.rect;
        float scale = Mathf.Min(rect.width, rect.height) / 24;
        Vector2 Project(Vector2 point) => rect.center + new Vector2(point.x - 12, 12 - point.y) * scale;
        foreach (var path in paths)
        for (int i = 1; i < path.Length; i++)
        {
            Vector2 a = Project(path[i - 1]), b = Project(path[i]);
            Vector2 normal = new Vector2(a.y - b.y, b.x - a.x).normalized * stroke * scale * .5f;
            int start = mesh.currentVertCount;
            mesh.AddVert(a - normal, color, Vector2.zero); mesh.AddVert(a + normal, color, Vector2.zero);
            mesh.AddVert(b + normal, color, Vector2.zero); mesh.AddVert(b - normal, color, Vector2.zero);
            mesh.AddTriangle(start, start + 1, start + 2); mesh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
