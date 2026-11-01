using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Reusable card skin assembly for static or video textures. Borrows art; owns only its hierarchy and silhouette.</summary>
[DisallowMultipleComponent]
public sealed class UICardFrameView : MonoBehaviour
{
    public RawImage Surface { get; private set; }
    public RectTransform FrontEffects { get; private set; }
    public RectTransform BackEffects { get; private set; }
    public RectTransform FreezeLayer { get; private set; }
    public RectTransform ShieldLayer { get; private set; }
    public RectTransform Root => (RectTransform)transform;
    public Image OuterShadow { get; private set; }
    public Graphic[] SnapshotLayers => new Graphic[] { Surface, frameImage, innerImage };
    public Graphic[] SnapshotClippedLayers => new Graphic[] { Surface, innerImage };
    public Texture SnapshotClip => Masks.TryGetValue(maskKey, out var lease) ? lease.Sprite.texture : null;
    // Covers both the opening and frame. Only allocated when a freeze view actually binds.
    public Texture FreezeClip
    {
        get
        {
            if (frame == null || !Masks.TryGetValue(maskKey, out var lease)) return null;
            if (lease.FullCardClip == null) lease.FullCardClip = CreateMaskTexture(frame, true);
            return lease.FullCardClip;
        }
    }
    private Image frameImage, glowImage, innerImage;
    private RectTransform visuals;
    private Sprite frame;
    private int maskKey;
    private Texture lastTexture;
    private Vector2 lastSize;
    private sealed class MaskLease { public Sprite Sprite; public Texture2D FullCardClip; public int Users; }
    private static readonly Dictionary<int, MaskLease> Masks = new Dictionary<int, MaskLease>();

    /// <summary>Initial assembly only. Keeps the scene's position and width; optional height fitting uses the selected frame.</summary>
    public void Initialize(Sprite frame, Sprite innerShadow, Sprite outerShadow, Sprite glow, Texture texture,
        bool fitHeightToFrame = true)
    {
        if (frame == null) throw new ArgumentNullException(nameof(frame));
        if (!(transform is RectTransform)) throw new InvalidOperationException("卡牌外观组件需要 RectTransform。");
        Clear(); this.frame = frame;
        Sprite silhouette = AcquireMask(frame); maskKey = frame.GetInstanceID();
        if (fitHeightToFrame) FitHeightToFrame();
        visuals = Stretch("CardVisuals", Root);
        // Padding is measured relative to the exported inner-shadow canvas, not an arbitrary uniform multiplier.
        Vector2 basis = innerShadow != null ? innerShadow.rect.size : frame.rect.size;
        OuterShadow = AddPaddedImage("Shadow", visuals, outerShadow, basis);
        glowImage = AddPaddedImage("SelectionGlow", visuals, glow, basis); glowImage.enabled = false;
        BackEffects = Stretch("EffectsBehind", visuals);
        RectTransform content = AddMask("CardContentMask", visuals, silhouette);
        Surface = Stretch("CardSurface", content).gameObject.AddComponent<RawImage>();
        Surface.raycastTarget = false; Surface.texture = texture;
        frameImage = AddImage("Frame", visuals, frame);
        // Keep the frame's original antialiased outside edge, then put the clipped inner shadow above it.
        RectTransform shade = AddMask("CardInnerShadowMask", visuals, silhouette);
        innerImage = AddImage("InnerShadow", shade, innerShadow);
        FreezeLayer = Stretch("FreezeOverlay", visuals);
        ShieldLayer = Stretch("ShieldOverlay", visuals);
        FrontEffects = Stretch("EffectsFront", visuals);
        RefreshCrop();
    }
    public void FitHeightToFrame()
    {
        if (frame != null) Root.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
            Root.rect.width * frame.rect.height / frame.rect.width);
    }
    public void SetFrameVisible(bool visible) { if (frameImage != null) frameImage.enabled = visible; }
    public void SetSurfaceTexture(Texture texture)
    {
        if (Surface == null) return;
        Surface.texture = texture; RefreshCrop();
    }
    public void SetTint(Color tint)
    {
        if (Surface != null) Surface.color = tint;
        if (frameImage != null) frameImage.color = tint;
        if (innerImage != null) innerImage.color = tint;
        if (OuterShadow != null) OuterShadow.color = tint;
    }
    public void SetHighlightVisible(bool visible) { if (glowImage != null) glowImage.enabled = visible && glowImage.sprite != null; }
    private void LateUpdate()
    {
        if (Surface != null && (Surface.texture != lastTexture || Surface.rectTransform.rect.size != lastSize)) RefreshCrop();
    }
    private void RefreshCrop()
    {
        if (Surface == null) return;
        lastTexture = Surface.texture; lastSize = Surface.rectTransform.rect.size;
        Surface.uvRect = CoverUV(lastSize, lastTexture == null ? Vector2.one : new Vector2(lastTexture.width, lastTexture.height));
    }
    public static Rect CoverUV(Vector2 viewport, Vector2 textureSize)
    {
        if (viewport.x <= 0 || viewport.y <= 0 || textureSize.x <= 0 || textureSize.y <= 0) return new Rect(0, 0, 1, 1);
        float targetAspect = viewport.x / viewport.y, sourceAspect = textureSize.x / textureSize.y;
        Vector2 uvSize = sourceAspect > targetAspect ? new Vector2(targetAspect / sourceAspect, 1)
            : new Vector2(1, sourceAspect / targetAspect);
        return new Rect((Vector2.one - uvSize) * .5f, uvSize);
    }
    private static Sprite AcquireMask(Sprite frame)
    {
        int key = frame.GetInstanceID();
        if (Masks.TryGetValue(key, out var cached)) { cached.Users++; return cached.Sprite; }
        var texture = CreateMaskTexture(frame, false);
        var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
        Masks.Add(key, new MaskLease { Sprite = sprite, Users = 1 }); return sprite;
    }
    private static Texture2D CreateMaskTexture(Sprite frame, bool includeFrame)
    {
        // Card content uses only the enclosed opening so it cannot leak through the outer bevel.
        // Freeze explicitly adds the frame's alpha afterwards to cover the complete card.
        var rect = frame.rect; int width = Mathf.RoundToInt(rect.width), height = Mathf.RoundToInt(rect.height);
        var pixels = frame.texture.GetPixels32(); int sourceWidth = frame.texture.width;
        var alpha = new byte[width * height];
        int originX = Mathf.RoundToInt(rect.x), originY = Mathf.RoundToInt(rect.y);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++) alpha[y * width + x] = pixels[(originY + y) * sourceWidth + originX + x].a;
        var mask = BuildOpeningMask(alpha, width, height);
        if (includeFrame)
            for (int p = 0; p < mask.Length; p++)
                mask[p] = new Color32(255, 255, 255, Math.Max(mask[p].a, alpha[p]));
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        { name = frame.name + (includeFrame ? "_FullCard" : "_CardOpening"), filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        texture.SetPixels32(mask); texture.Apply(false, true);
        return texture;
    }
    internal static Color32[] BuildOpeningMask(byte[] alpha, int width, int height)
    {
        if (width < 3 || height < 3 || alpha == null || alpha.Length != width * height)
            throw new ArgumentException("卡框遮罩尺寸无效。");
        int center = height / 2 * width + width / 2;
        if (alpha[center] >= 128) throw new InvalidOperationException("卡框中心需要透明镂空区域。");
        var opening = new bool[alpha.Length];
        var queue = new Queue<int>(); opening[center] = true; queue.Enqueue(center);
        while (queue.Count > 0)
        {
            int p = queue.Dequeue(), x = p % width, y = p / width;
            if (x == 0 || y == 0 || x == width - 1 || y == height - 1)
                throw new InvalidOperationException("卡框镂空区域未封闭，不能安全生成卡面遮罩。");
            Visit(p - 1); Visit(p + 1); Visit(p - width); Visit(p + width);
        }
        void Visit(int p)
        {
            if (!opening[p] && alpha[p] < 128) { opening[p] = true; queue.Enqueue(p); }
        }
        // Two source texels of overlap hide filtering seams underneath the opaque frame.
        // Never extend into its translucent outside fringe or across the source texture boundary.
        var expanded = (bool[])opening.Clone();
        for (int pass = 0; pass < 2; pass++)
        {
            var next = (bool[])expanded.Clone();
            for (int y = 1; y < height - 1; y++)
                for (int x = 1; x < width - 1; x++)
                {
                    int p = y * width + x;
                    if (!expanded[p] && alpha[p] >= 128 &&
                        (expanded[p - 1] || expanded[p + 1] || expanded[p - width] || expanded[p + width])) next[p] = true;
                }
            expanded = next;
        }
        var result = new Color32[alpha.Length];
        for (int p = 0; p < result.Length; p++) if (expanded[p]) result[p] = new Color32(255, 255, 255, 255);
        return result;
    }
    private static RectTransform AddMask(string name, Transform parent, Sprite sprite)
    {
        var image = AddImage(name, parent, sprite);
        image.gameObject.AddComponent<Mask>().showMaskGraphic = false;
        return image.rectTransform;
    }
    private static Image AddPaddedImage(string name, Transform parent, Sprite sprite, Vector2 basis)
    {
        Image image = AddImage(name, parent, sprite);
        if (sprite != null)
        {
            Vector2 ratio = new Vector2(sprite.rect.width / basis.x, sprite.rect.height / basis.y);
            image.rectTransform.anchorMin = (Vector2.one - ratio) * .5f;
            image.rectTransform.anchorMax = (Vector2.one + ratio) * .5f;
        }
        return image;
    }
    private static Image AddImage(string name, Transform parent, Sprite sprite)
    {
        var image = Stretch(name, parent).gameObject.AddComponent<Image>();
        image.sprite = sprite; image.enabled = sprite != null; image.raycastTarget = false; return image;
    }
    private static RectTransform Stretch(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false); rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero; return rect;
    }
    public void Clear()
    {
        if (visuals != null) { visuals.gameObject.SetActive(false); Destroy(visuals.gameObject); visuals = null; }
        Surface = null; frameImage = glowImage = innerImage = OuterShadow = null;
        FrontEffects = BackEffects = FreezeLayer = ShieldLayer = null; frame = null;
        if (maskKey != 0 && Masks.TryGetValue(maskKey, out var mask) && --mask.Users == 0)
        {
            Destroy(mask.Sprite.texture); Destroy(mask.Sprite);
            if (mask.FullCardClip != null) Destroy(mask.FullCardClip);
            Masks.Remove(maskKey);
        }
        maskKey = 0;
    }
    private void OnDisable() { if (visuals != null) visuals.gameObject.SetActive(false); }
    private void OnEnable() { if (visuals != null) visuals.gameObject.SetActive(true); }
    private void OnDestroy() => Clear();
}
