using UnityEngine;
using UnityEngine.UI;

/// <summary>Textured quad with vertex-driven reveal/erase. Different hits share two cached materials.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class UIHitTextureGraphic : MaskableGraphic
{
    private Sprite sprite;
    private float reveal = 1, erase, softness = .06f, whiteMix = .35f, blur;
    public override Texture mainTexture => sprite != null ? sprite.texture : base.mainTexture;
    public void Initialize(Sprite image, Material sharedMaterial)
    {
        sprite = image; material = sharedMaterial; raycastTarget = false;
        var canvas = GetComponentInParent<Canvas>();
        if (canvas != null) canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2;
        SetAllDirty();
    }
    public void Appearance(float visible, float removed, float edge, float coreWhite, float blurPixels = 0)
    {
        reveal = visible; erase = removed; softness = edge; whiteMix = coreWhite; blur = blurPixels; SetVerticesDirty();
    }
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear(); if (sprite == null) return;
        Rect rect = GetPixelAdjustedRect(), uv = sprite.textureRect;
        float tw = sprite.texture.width, th = sprite.texture.height;
        Vector2[] points = { new Vector2(rect.xMin, rect.yMin), new Vector2(rect.xMin, rect.yMax),
            new Vector2(rect.xMax, rect.yMax), new Vector2(rect.xMax, rect.yMin) };
        Vector2[] coords = { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
        for (int i = 0; i < 4; i++)
        {
            var vertex = UIVertex.simpleVert; vertex.position = points[i]; vertex.color = color;
            vertex.uv0 = new Vector2((uv.x + uv.width * coords[i].x) / tw, (uv.y + uv.height * coords[i].y) / th);
            vertex.uv1 = new Vector4(coords[i].x, reveal, erase, softness);
            vertex.uv2 = new Vector4(blur / tw, blur / th, whiteMix, 0); vh.AddVert(vertex);
        }
        vh.AddTriangle(0, 1, 2); vh.AddTriangle(0, 2, 3);
    }
}
