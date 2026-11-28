using UnityEngine;
using UnityEngine.UI;

/// <summary>池化的纯表现图标。留白由网格扩展，发光不裁在原图边缘；不接管射线。</summary>
public sealed class ElementAuraMotionIcon : MaskableGraphic
{
    private Sprite sprite;
    private Material ownedMaterial;
    public override Texture mainTexture => sprite != null ? sprite.texture : Texture2D.whiteTexture;
    public void Set(Sprite value, Color glowColor, float glow, float opacity, float brightness)
    {
        if (sprite != value) { sprite = value; SetVerticesDirty(); SetMaterialDirty(); }
        if (ownedMaterial == null)
        {
            var shader = Resources.Load<Shader>("UI/ElementAuraIcon");
            if (shader == null) { Debug.LogError("缺少元素栏图标Shader。", this); enabled = false; return; }
            ownedMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave }; material = ownedMaterial;
        }
        raycastTarget = false;
        color = new Color(1, 1, 1, opacity);
        Vector4 uv = sprite != null ? UnityEngine.Sprites.DataUtility.GetOuterUV(sprite) : new Vector4(0, 0, 1, 1);
        Rect rect = GetPixelAdjustedRect();
        float aspect = sprite != null ? sprite.rect.width / sprite.rect.height : 1;
        float width = Mathf.Min(rect.width, rect.height * aspect), height = Mathf.Min(rect.height, rect.width / aspect);
        var pixelUV = new Vector4((uv.z - uv.x) / Mathf.Max(1, width), (uv.w - uv.y) / Mathf.Max(1, height), 0, 0);
        void Apply(Material m)
        {
            m.SetVector("_SpriteUV", uv); m.SetColor("_GlowColor", glowColor);
            m.SetVector("_PixelUV", pixelUV);
            m.SetFloat("_Glow", glow); m.SetFloat("_BrightnessAmount", brightness);
        }
        Apply(ownedMaterial);
        var rendered = materialForRendering; if (rendered != ownedMaterial) Apply(rendered);
    }
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear(); if (sprite == null) return;
        Rect r = GetPixelAdjustedRect(); var uv = UnityEngine.Sprites.DataUtility.GetOuterUV(sprite);
        float aspect = sprite.rect.width / sprite.rect.height;
        if (r.width / r.height > aspect) { float w = r.height * aspect; r.x += (r.width - w) / 2; r.width = w; }
        else { float h = r.width / aspect; r.y += (r.height - h) / 2; r.height = h; }
        if (r.width <= 0 || r.height <= 0) return;
        const float padding = 12;
        float x = padding / r.width * (uv.z - uv.x), y = padding / r.height * (uv.w - uv.y);
        vh.AddVert(new Vector3(r.xMin - padding, r.yMin - padding), color, new Vector2(uv.x - x, uv.y - y));
        vh.AddVert(new Vector3(r.xMin - padding, r.yMax + padding), color, new Vector2(uv.x - x, uv.w + y));
        vh.AddVert(new Vector3(r.xMax + padding, r.yMax + padding), color, new Vector2(uv.z + x, uv.w + y));
        vh.AddVert(new Vector3(r.xMax + padding, r.yMin - padding), color, new Vector2(uv.z + x, uv.y - y));
        vh.AddTriangle(0, 1, 2); vh.AddTriangle(2, 3, 0);
    }
    protected override void OnDestroy()
    { base.OnDestroy(); if (ownedMaterial != null) Destroy(ownedMaterial); }
}
