using UnityEngine;
using UnityEngine.UI;

/// <summary>A non-rendered diamond stencil used by battle avatar portraits.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class DiamondMaskGraphic : MaskableGraphic
{
    protected override void OnPopulateMesh(VertexHelper vertexHelper)
    {
        vertexHelper.Clear();
        Rect area = rectTransform.rect;
        Color32 vertexColor = Color.white;
        vertexHelper.AddVert(new Vector3(area.center.x, area.yMax), vertexColor, new Vector2(0.5f, 1f));
        vertexHelper.AddVert(new Vector3(area.xMax, area.center.y), vertexColor, new Vector2(1f, 0.5f));
        vertexHelper.AddVert(new Vector3(area.center.x, area.yMin), vertexColor, new Vector2(0.5f, 0f));
        vertexHelper.AddVert(new Vector3(area.xMin, area.center.y), vertexColor, new Vector2(0f, 0.5f));
        vertexHelper.AddTriangle(0, 1, 2);
        vertexHelper.AddTriangle(0, 2, 3);
    }
}
