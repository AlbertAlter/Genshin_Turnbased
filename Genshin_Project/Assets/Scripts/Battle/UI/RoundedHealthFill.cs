using UnityEngine;
using UnityEngine.UI;

/// <summary>A variable-width rounded rectangle, not a cropped full-width capsule.</summary>
public sealed class RoundedHealthFill : MaskableGraphic
{
    // Optional PSD composite, including clipped effects. Empty keeps the procedural ally fill.
    public string psdTexturePath;
    public Vector4 psdPadding = new Vector4(3, 7, 5, 1); // left, bottom, right, top
    public Vector2 psdCaps = new Vector2(10, 12);
    public float psdBodyHeight = 14;
    private Sprite psdSprite;
    private Coroutine textureRequest;
    public override Texture mainTexture => psdSprite != null ? psdSprite.texture : base.mainTexture;

    protected override void OnEnable()
    {
        base.OnEnable();
        ReloadPsdTexture();
    }

    public void ReloadPsdTexture()
    {
        if (!isActiveAndEnabled) return;
        if (textureRequest != null) StopCoroutine(textureRequest);
        textureRequest = null;
        ReleasePsdTexture();
        if (string.IsNullOrWhiteSpace(psdTexturePath)) { SetAllDirty(); return; }
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            // Preview uses the same published asset as Play Mode, never art_assets/Charts sources.
            if (!StreamingAssetSpriteLoader.TryBuildRequestUrl(psdTexturePath, out string url)) return;
            var uri = new System.Uri(url);
            if (!uri.IsFile || !System.IO.File.Exists(uri.LocalPath)) return;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            texture.hideFlags = HideFlags.HideAndDontSave;
            if (!texture.LoadImage(System.IO.File.ReadAllBytes(uri.LocalPath)))
            {
                DestroyImmediate(texture);
                return;
            }
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            psdSprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f));
            psdSprite.hideFlags = HideFlags.HideAndDontSave;
            SetAllDirty();
            return;
        }
#endif
        if (Application.isPlaying)
            textureRequest = StartCoroutine(StreamingAssetSpriteLoader.Load(psdTexturePath, sprite =>
            {
                psdSprite = sprite;
                SetMaterialDirty();
                SetVerticesDirty();
            }, error => Debug.LogError("Enemy HP PSD texture unavailable. Import UI art before Play. " + error, this)));
    }

    protected override void OnDisable()
    {
        if (textureRequest != null) StopCoroutine(textureRequest);
        textureRequest = null;
        base.OnDisable();
    }

    protected override void OnDestroy()
    {
        ReleasePsdTexture();
        base.OnDestroy();
    }

    private void ReleasePsdTexture()
    {
        if (psdSprite == null) return;
        if (Application.isPlaying) { Destroy(psdSprite.texture); Destroy(psdSprite); }
        else { DestroyImmediate(psdSprite.texture); DestroyImmediate(psdSprite); }
        psdSprite = null;
    }

    [SerializeField, Range(0, 1)] private float fraction = 1;
    public float Fraction
    {
        get => fraction;
        set
        {
            float next = float.IsNaN(value) || float.IsInfinity(value) ? 0 : Mathf.Clamp01(value);
            if (Mathf.Approximately(fraction, next)) return;
            fraction = next;
            SetVerticesDirty();
        }
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect rect = rectTransform.rect;
        rect.width *= fraction;
        if (psdSprite != null)
        {
            DrawPsd(mesh, rect, psdSprite, psdPadding, psdCaps, psdBodyHeight, color.a);
            return;
        }
        DrawCapsule(mesh, rect, color, canvas != null ? canvas.scaleFactor : 1);
    }

    public static void DrawPsd(VertexHelper mesh, Rect body, Sprite psdSprite, Vector4 psdPadding,
        Vector2 psdCaps, float psdBodyHeight, float alpha, float referenceWidth = 0)
    {
        if (body.width <= 0 || body.height <= 0) return;
        float scale = body.height / Mathf.Max(.01f, psdBodyHeight);
        // At very low HP compress both caps, rather than inventing a minimum HP width.
        float capBody = Mathf.Max(.01f, psdCaps.x + psdCaps.y - psdPadding.x - psdPadding.z);
        float capScale = Mathf.Min(scale, body.width / capBody);
        float left = body.xMin - psdPadding.x * capScale;
        float right = body.xMax + psdPadding.z * capScale;
        float bottom = body.yMin - psdPadding.y * scale;
        float top = body.yMax + psdPadding.w * scale;
        float width = referenceWidth > 0 ? referenceWidth : psdSprite.texture.width;
        Color tint = new Color(1, 1, 1, alpha); // PSD already contains its actual RGB and blend modes.
        AddPsdQuad(mesh, left, left + psdCaps.x * capScale, bottom, top, 0, psdCaps.x / width, tint);
        AddPsdQuad(mesh, left + psdCaps.x * capScale, right - psdCaps.y * capScale, bottom, top,
            psdCaps.x / width, 1 - psdCaps.y / width, tint);
        AddPsdQuad(mesh, right - psdCaps.y * capScale, right, bottom, top, 1 - psdCaps.y / width, 1, tint);
    }

    private static void AddPsdQuad(VertexHelper mesh, float left, float right, float bottom, float top,
        float u0, float u1, Color tint)
    {
        if (right <= left) return;
        int start = mesh.currentVertCount;
        mesh.AddVert(new Vector3(left, bottom), tint, new Vector2(u0, 0));
        mesh.AddVert(new Vector3(left, top), tint, new Vector2(u0, 1));
        mesh.AddVert(new Vector3(right, top), tint, new Vector2(u1, 1));
        mesh.AddVert(new Vector3(right, bottom), tint, new Vector2(u1, 0));
        mesh.AddTriangle(start, start+1, start+2);
        mesh.AddTriangle(start, start+2, start+3);
    }

    public static void DrawCapsule(VertexHelper mesh, Rect rect, Color color, float scale)
    {
        float width = rect.width;
        if (width <= 0 || rect.height <= 0) return;
        float radius = Mathf.Min(width, rect.height) * .5f;
        const int steps = 12;
        const int count = 4 * (steps + 1);
        // One screen pixel of feathering; preserve the configured fill dimensions.
        float feather = Mathf.Min(.5f / Mathf.Max(.01f, scale), radius);
        int start = mesh.currentVertCount;
        mesh.AddVert(rect.center, color, Vector2.zero);
        for (int corner = 0; corner < 4; corner++)
        {
            Vector2 center = new Vector2(corner == 0 || corner == 3 ? rect.xMax-radius : rect.xMin+radius,
                                        corner < 2 ? rect.yMax-radius : rect.yMin+radius);
            for (int step = 0; step <= steps; step++)
            {
                float angle = (corner * 90f + step * 90f/steps) * Mathf.Deg2Rad;
                Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                mesh.AddVert(center + direction * (radius-feather), color, Vector2.zero);
                Color transparent = color; transparent.a = 0;
                mesh.AddVert(center + direction * (radius+feather), transparent, Vector2.zero);
            }
        }
        for (int i = 0; i < count; i++)
        {
            int a = start + 1 + i*2, b = start + 1 + ((i+1)%count)*2;
            mesh.AddTriangle(start, a, b);
            mesh.AddTriangle(a, a+1, b+1);
            mesh.AddTriangle(a, b+1, b);
        }
    }
}
