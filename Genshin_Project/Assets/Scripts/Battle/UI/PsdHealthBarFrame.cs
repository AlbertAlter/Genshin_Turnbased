using UnityEngine;
using UnityEngine.UI;

/// <summary>PSD capsule silhouette and two strokes. Keeps the existing Image references and tint.</summary>
public sealed class PsdHealthBarFrame : Image
{
    public bool outsideStroke = true;
    public bool psdCompositeFill;
    public string psdFillPath;
    public Color outerStrokeColor = new Color32(113,103,88,255);
    public Color innerStrokeColor = new Color32(198,188,171,255);
    private Sprite background;
    private Coroutine backgroundRequest;
    public override Texture mainTexture => background != null ? background.texture : base.mainTexture;

    protected override void OnEnable() { base.OnEnable(); ReloadBackground(); }

    public void ReloadBackground()
    {
        if (!isActiveAndEnabled) return;
        if (backgroundRequest != null) StopCoroutine(backgroundRequest);
        backgroundRequest = null;
        ReleaseBackground();
        SetAllDirty();
        if (!psdCompositeFill || string.IsNullOrEmpty(psdFillPath)) return;
        string path = psdFillPath.Replace("_Composite.png", "_Background.png").Replace("_Fill.png", "_Background.png");
        if (path == psdFillPath) return;
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            if (!StreamingAssetSpriteLoader.TryBuildRequestUrl(path, out string url)) return;
            var uri = new System.Uri(url);
            if (!uri.IsFile || !System.IO.File.Exists(uri.LocalPath)) return;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            texture.hideFlags = HideFlags.HideAndDontSave;
            if (!texture.LoadImage(System.IO.File.ReadAllBytes(uri.LocalPath))) { DestroyImmediate(texture); return; }
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            background = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f));
            background.hideFlags = HideFlags.HideAndDontSave;
            SetAllDirty();
            return;
        }
#endif
        backgroundRequest = StartCoroutine(StreamingAssetSpriteLoader.Load(path, value =>
        { background = value; SetAllDirty(); }, error => Debug.LogError(error, this)));
    }

    protected override void OnDisable()
    {
        if (backgroundRequest != null) StopCoroutine(backgroundRequest);
        backgroundRequest = null;
        base.OnDisable();
    }
    protected override void OnDestroy() { ReleaseBackground(); base.OnDestroy(); }
    private void ReleaseBackground()
    {
        if (background == null) return;
        if (Application.isPlaying) { Destroy(background.texture); Destroy(background); }
        else { DestroyImmediate(background.texture); DestroyImmediate(background); }
        background = null;
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect rect = rectTransform.rect;
        float scale = canvas != null ? canvas.scaleFactor : 1;
        Color outer=outerStrokeColor, inner=innerStrokeColor;
        outer.a *= color.a; inner.a *= color.a;
        if (psdCompositeFill)
        {
            // Full-width frame and shadow stay still; only the child colour fill responds to HP.
            if (background != null)
                RoundedHealthFill.DrawPsd(mesh, rect, background,
                    new Vector4(3, 7, 5, outsideStroke ? 2 : 1),
                    outsideStroke ? new Vector2(10, 12) : new Vector2(8.5f, 10.5f),
                    outsideStroke ? 14 : 11, color.a);
            else
                RoundedHealthFill.DrawCapsule(mesh,Expand(rect,2),new Color(113/255f,103/255f,88/255f,color.a),scale);
            return;
        }
        if(outsideStroke)
        {
            RoundedHealthFill.DrawCapsule(mesh,Expand(rect,3),outer,scale);
            RoundedHealthFill.DrawCapsule(mesh,Expand(rect,1),inner,scale);
            RoundedHealthFill.DrawCapsule(mesh,rect,color,scale);
        }
        else
        {
            RoundedHealthFill.DrawCapsule(mesh,rect,inner,scale);
            RoundedHealthFill.DrawCapsule(mesh,Expand(rect,-1),outer,scale);
            RoundedHealthFill.DrawCapsule(mesh,Expand(rect,-3),color,scale);
        }
    }

    private static Rect Expand(Rect rect,float amount) =>
        Rect.MinMaxRect(rect.xMin-amount,rect.yMin-amount,rect.xMax+amount,rect.yMax+amount);
}
