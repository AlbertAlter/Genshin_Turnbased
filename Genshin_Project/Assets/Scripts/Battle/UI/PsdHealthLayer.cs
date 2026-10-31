using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;

/// <summary>One PSD file, one UI graphic. Never renders a flattened health-bar PNG.</summary>
public sealed class PsdHealthLayer : MaskableGraphic
{
    public string relativePath;
    public Vector4 layerBounds;
    public int blendMode;
    public bool clippedToBase;
    public bool normalizeWithBase;
    public bool followsHealth;
    public RoundedHealthFill healthSource;
    public Vector2 canvasSize;
    public Vector4 baseBounds;
    public Vector4 padding;
    public Vector2 caps;
    public bool vectorBase;
    // Exported fractional PSD extent: offset from baseBounds, then original width/height.
    // This is texture-space metadata, never an adjustment to the user's RectTransform.
    public Vector4 sourceBaseExtent;
    public Vector2 contentOffset = new Vector2(1,-1);
    private Sprite layerSprite;
    private Material ownedMaterial;
    private float lastFraction = -1;
    private bool overrideBaseColor;
    private Color baseColor;
    public void SetBaseColor(Color value)
    {
        overrideBaseColor = true; baseColor = value;
        if (ownedMaterial == null) return;
        ownedMaterial.SetFloat("_OverrideBaseColor", 1);
        ownedMaterial.SetColor("_BaseColor", value);
    }
    public override Texture mainTexture => layerSprite != null ? layerSprite.texture : base.mainTexture;

    public void Bind(Sprite source, Texture baseTexture, Shader shader, Sprite underA, Sprite underB, Vector4 underBounds)
    {
        layerSprite = source;
        if (ownedMaterial == null) ownedMaterial = new Material(shader) { hideFlags=HideFlags.HideAndDontSave };
        ownedMaterial.SetTexture("_BaseTex", baseTexture);
        ownedMaterial.SetVector("_LayerBounds", layerBounds);
        ownedMaterial.SetVector("_CanvasSize", new Vector4(canvasSize.x,canvasSize.y,0,0));
        ownedMaterial.SetVector("_BaseBounds", baseBounds);
        Vector4 sourceBounds = baseBounds;
        if (sourceBaseExtent.z > 0 && sourceBaseExtent.w > 0)
            sourceBounds = new Vector4(baseBounds.x + sourceBaseExtent.x,
                baseBounds.y + sourceBaseExtent.y, sourceBaseExtent.z, sourceBaseExtent.w);
        ownedMaterial.SetVector("_PsdBaseBounds", sourceBounds);
        ownedMaterial.SetTexture("_UnderA", underA != null ? underA.texture : Texture2D.whiteTexture);
        ownedMaterial.SetTexture("_UnderB", underB != null ? underB.texture : Texture2D.whiteTexture);
        ownedMaterial.SetFloat("_HasUnder", underA != null && underB != null ? 1 : 0);
        ownedMaterial.SetVector("_UnderBounds", underBounds);
        ownedMaterial.SetFloat("_Mode", blendMode);
        ownedMaterial.SetFloat("_VectorBase", vectorBase ? 1 : 0);
        ownedMaterial.SetFloat("_Clipped", clippedToBase ? 1 : 0);
        ownedMaterial.SetFloat("_NormalizeWithBase", normalizeWithBase ? 1 : 0);
        ownedMaterial.SetInt("_SrcBlend", (int)(blendMode == 1 ? BlendMode.DstColor : BlendMode.SrcAlpha));
        material = ownedMaterial;
        if (overrideBaseColor) SetBaseColor(baseColor);
        SetAllDirty();
    }

    private void LateUpdate()
    {
        float value = followsHealth && healthSource != null ? healthSource.Fraction : 1;
        if (Mathf.Approximately(lastFraction, value)) return;
        lastFraction = value;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        if (layerSprite == null) return;
        Rect rect=rectTransform.rect;
        if (followsHealth)
        {
            // Both PSD templates place the colour shape 1px above/left of the frame centre.
            // Translate the whole colour/effect stack, not its UVs: clipping and Soft Light
            // continue sampling the same PSD coordinates, including at partial HP.
            float pixelScale = rect.height / Mathf.Max(.01f, baseBounds.w);
            rect.position += contentOffset * pixelScale;
            if (healthSource != null) rect.width*=healthSource.Fraction;
        }
        RoundedHealthFill.DrawPsd(mesh, rect, layerSprite, padding,caps,baseBounds.w,color.a,canvasSize.x);
    }

    protected override void OnDestroy()
    {
        if (ownedMaterial != null)
        {
            if (Application.isPlaying) Destroy(ownedMaterial); else DestroyImmediate(ownedMaterial);
        }
        base.OnDestroy();
    }
}
