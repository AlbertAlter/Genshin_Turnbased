using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Reproduces Photoshop's stacked outside strokes without rasterizing text.
/// Widths are expressed in the 1920x1080 UI design coordinate system.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class PsdDoubleOutline : BaseMeshEffect
{
    [SerializeField] private Color32 outerColor = new Color32(113, 103, 88, 255);
    [SerializeField] [Min(0f)] private float outerWidth = 7f;
    [SerializeField] private Color32 innerColor = new Color32(153, 143, 129, 255);
    [SerializeField] [Min(0f)] private float innerWidth = 3f;

    public override void ModifyMesh(VertexHelper vertexHelper)
    {
        if (!IsActive() || vertexHelper.currentVertCount == 0) return;

        var source = new List<UIVertex>();
        vertexHelper.GetUIVertexStream(source);
        var output = new List<UIVertex>(source.Count * 220);
        AppendOutsideStroke(source, output, outerColor, outerWidth);
        AppendOutsideStroke(source, output, innerColor, innerWidth);
        output.AddRange(source);
        vertexHelper.Clear();
        vertexHelper.AddUIVertexTriangleStream(output);
    }

    private static void AppendOutsideStroke(
        IReadOnlyList<UIVertex> source,
        ICollection<UIVertex> output,
        Color32 color,
        float width)
    {
        int radius = Mathf.CeilToInt(width);
        float squaredWidth = width * width;
        for (int y = -radius; y <= radius; y++)
        {
            for (int x = -radius; x <= radius; x++)
            {
                if (x * x + y * y > squaredWidth) continue;
                for (int index = 0; index < source.Count; index++)
                {
                    UIVertex vertex = source[index];
                    vertex.position += new Vector3(x, y, 0f);
                    byte alpha = (byte)(color.a * vertex.color.a / 255);
                    vertex.color = new Color32(color.r, color.g, color.b, alpha);
                    output.Add(vertex);
                }
            }
        }
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        if (graphic != null) graphic.SetVerticesDirty();
    }
#endif
}
