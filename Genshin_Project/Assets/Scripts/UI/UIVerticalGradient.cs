using UnityEngine;
using UnityEngine.UI;

/// <summary>Shared vertex gradient; respects the Image's alpha, mask and existing material.</summary>
[RequireComponent(typeof(Graphic))]
public sealed class UIVerticalGradient : BaseMeshEffect
{
    private Color top = Color.white, bottom = Color.white;
    public void SetColors(Color upper, Color lower)
    {
        top = upper; bottom = lower; graphic.SetVerticesDirty();
    }
    public override void ModifyMesh(VertexHelper mesh)
    {
        if (!IsActive() || mesh.currentVertCount == 0) return;
        var vertex = new UIVertex();
        float min = float.MaxValue, max = float.MinValue;
        for (int i = 0; i < mesh.currentVertCount; i++)
        {
            mesh.PopulateUIVertex(ref vertex, i);
            min = Mathf.Min(min, vertex.position.y); max = Mathf.Max(max, vertex.position.y);
        }
        for (int i = 0; i < mesh.currentVertCount; i++)
        {
            mesh.PopulateUIVertex(ref vertex, i);
            vertex.color = (Color)vertex.color * Color.Lerp(bottom, top, Mathf.InverseLerp(min, max, vertex.position.y));
            mesh.SetUIVertex(vertex, i);
        }
    }
}
