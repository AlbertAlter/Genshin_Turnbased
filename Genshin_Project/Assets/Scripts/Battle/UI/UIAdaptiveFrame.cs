using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

/// <summary>九宫格装饰框；借用公共卡框开口算法，固定四角并保留镂空外缘。</summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class UIAdaptiveFrame : MonoBehaviour
{
    [Tooltip("相对于 StreamingAssets 的已发布图片路径。")]
    public string framePath = UICardFactory.FramePath;
    [Tooltip("原图像素：左、下、右、上。切线必须越过全部角部装饰。")]
    public Vector4 borderPixels = new Vector4(64, 64, 64, 64);
    [Min(0.01f), Tooltip("越大则边框和角部越小；1 表示原始像素比例。")]
    public float pixelsPerUnitMultiplier = 1f;
    public Color fillColor = new Color32(187, 177, 164, 255);
    public bool showFill = true;
    [SerializeField, Tooltip("可选的背景 Image，与边框等大，使用边框开口作为遮罩。")]
    private Image[] openingMasks = Array.Empty<Image>();
    public bool Ready => frameImage != null && frameImage.sprite != null;

    private RectTransform visuals;
    private Image fillImage, frameImage;
    private Image decorationMask;
    private Action<RectTransform> buildOpeningDecoration;
    private Sprite loadedSprite, slicedFrame, slicedFill;
    private Texture2D fillTexture;
    // Separate instances load their own textures. Reuse the last opening only while its source alpha matches.
    private static byte[] cachedAlpha;
    private static int cachedWidth;
    private static Color32[] cachedOpening;
    private Coroutine request;
    private int version;

    /// <summary>Build optional decoration inside the opening, between the fill and the border.</summary>
    public void BindOpeningDecoration(Action<RectTransform> build)
    {
        buildOpeningDecoration = build;
        if (!Ready) return;
        RebuildOpeningDecoration();
        RefreshAppearance();
    }

    private void OnEnable() => Reload();

    [ContextMenu("重新加载边框")]
    public void Reload()
    {
        Clear();
        if (!isActiveAndEnabled || !gameObject.scene.IsValid()) return;
        int expectedVersion = version;
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            if (!StreamingAssetSpriteLoader.TryBuildRequestUrl(framePath, out string url))
            {
                Debug.LogWarning("自适应边框的 StreamingAssets 路径无效：" + framePath, this);
                return;
            }
            var uri = new Uri(url);
            if (!uri.IsFile || !File.Exists(uri.LocalPath))
            {
                Debug.LogWarning("自适应边框未发布：" + framePath, this);
                return;
            }
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            texture.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                if (!texture.LoadImage(File.ReadAllBytes(uri.LocalPath))) throw new InvalidOperationException("PNG 解码失败。");
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = FilterMode.Bilinear;
                loadedSprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                    new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
                loadedSprite.hideFlags = HideFlags.HideAndDontSave;
                Build();
            }
            catch (Exception error)
            {
                if (loadedSprite == null) Release(texture);
                Clear();
                Debug.LogWarning("自适应边框加载失败：" + error.Message, this);
            }
            return;
        }
#endif
        request = StartCoroutine(Load(expectedVersion));
    }

    private IEnumerator Load(int expectedVersion)
    {
        yield return StreamingAssetSpriteLoader.Load(framePath, sprite =>
        {
            if (!isActiveAndEnabled || version != expectedVersion)
            {
                Release(sprite.texture); Release(sprite);
                return;
            }
            loadedSprite = sprite;
            loadedSprite.hideFlags = HideFlags.HideAndDontSave;
            try { Build(); }
            catch (Exception error)
            {
                Clear(false);
                Debug.LogWarning("自适应边框创建失败：" + error.Message, this);
            }
        }, error => Debug.LogWarning(error, this));
        request = null;
    }

    private void Build()
    {
        Texture2D texture = loadedSprite.texture;
        int width = texture.width, height = texture.height;
        for (int i = 0; i < 4; i++)
            if (float.IsNaN(borderPixels[i]) || float.IsInfinity(borderPixels[i]) || borderPixels[i] < 0)
                throw new InvalidOperationException("九宫格边距必须是有限非负数。");
        if (borderPixels.x + borderPixels.z >= width || borderPixels.y + borderPixels.w >= height)
            throw new InvalidOperationException("九宫格必须保留非零的中央区域。");

        Color32[] pixels = texture.GetPixels32();
        // 与角色卡面使用同一闭合开口及两像素内侧搭接，底色不会漏到框外。
        Color32[] mask = OpeningPixels(pixels, width, height);
        fillTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = "AdaptiveFrameOpening", hideFlags = HideFlags.HideAndDontSave,
            wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
        };
        fillTexture.SetPixels32(mask);
        fillTexture.Apply(false, true);
        slicedFrame = CreateSliced(texture);
        slicedFill = CreateSliced(fillTexture);

        visuals = new GameObject("AdaptiveFrameVisuals", typeof(RectTransform)).GetComponent<RectTransform>();
        visuals.gameObject.hideFlags = HideFlags.HideAndDontSave;
        Stretch(visuals, transform);
        fillImage = AddImage("Fill", slicedFill, true);
        frameImage = AddImage("Frame", slicedFrame, false);
        RebuildOpeningDecoration();
        foreach (var openingMask in openingMasks)
        {
            if (openingMask == null) continue;
            openingMask.sprite = slicedFill;
            openingMask.type = Image.Type.Sliced;
        }
        RefreshAppearance();
    }

    private static Color32[] OpeningPixels(Color32[] pixels, int width, int height)
    {
        if (cachedWidth == width && cachedAlpha?.Length == pixels.Length)
        {
            int i = 0;
            while (i < pixels.Length && pixels[i].a == cachedAlpha[i]) i++;
            if (i == pixels.Length) return cachedOpening;
        }
        var alpha = new byte[pixels.Length];
        for (int i = 0; i < pixels.Length; i++) alpha[i] = pixels[i].a;
        var opening = UICardFrameView.BuildOpeningMask(alpha, width, height);
        cachedAlpha = alpha;
        cachedWidth = width;
        return cachedOpening = opening;
    }

    private void RebuildOpeningDecoration()
    {
        if (decorationMask != null)
        {
            decorationMask.gameObject.SetActive(false);
            Release(decorationMask.gameObject);
        }
        decorationMask = null;
        if (buildOpeningDecoration == null) return;

        decorationMask = AddImage("OpeningDecoration", slicedFill, true);
        decorationMask.transform.SetSiblingIndex(frameImage.transform.GetSiblingIndex());
        decorationMask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
        buildOpeningDecoration(decorationMask.rectTransform);
    }

    private Sprite CreateSliced(Texture2D texture)
    {
        var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
            new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, borderPixels);
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }

    private Image AddImage(string name, Sprite sprite, bool fillCenter)
    {
        var image = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>();
        image.gameObject.hideFlags = HideFlags.HideAndDontSave;
        Stretch(image.rectTransform, visuals);
        image.sprite = sprite;
        image.type = Image.Type.Sliced;
        image.fillCenter = fillCenter;
        image.raycastTarget = false;
        return image;
    }

    private static void Stretch(RectTransform rect, Transform parent)
    {
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private void LateUpdate() => RefreshAppearance();
    private void RefreshAppearance()
    {
        float multiplier = float.IsNaN(pixelsPerUnitMultiplier) || float.IsInfinity(pixelsPerUnitMultiplier)
            ? 1f : Mathf.Max(.01f, pixelsPerUnitMultiplier);
        UpdateMultiplier(frameImage, multiplier);
        UpdateMultiplier(decorationMask, multiplier);
        foreach (var openingMask in openingMasks) UpdateMultiplier(openingMask, multiplier);
        if (fillImage == null) return;
        UpdateMultiplier(fillImage, multiplier);
        fillImage.color = fillColor;
        fillImage.enabled = showFill;
    }
    private static void UpdateMultiplier(Image image, float multiplier)
    {
        if (image != null && image.pixelsPerUnitMultiplier != multiplier)
            image.pixelsPerUnitMultiplier = multiplier;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        UnityEditor.EditorApplication.delayCall -= RefreshEditor;
        UnityEditor.EditorApplication.delayCall += RefreshEditor;
    }
    private void RefreshEditor()
    {
        if (this != null && isActiveAndEnabled && !Application.isPlaying) Reload();
    }
#endif

    private void OnDisable() => Clear();
    private void OnDestroy() => Clear();
    private void Clear(bool stopRequest = true)
    {
        version++;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.delayCall -= RefreshEditor;
#endif
        if (stopRequest && request != null) StopCoroutine(request);
        request = null;
        if (visuals != null) { visuals.gameObject.SetActive(false); Release(visuals.gameObject); }
        visuals = null; frameImage = fillImage = decorationMask = null;
        if (slicedFill != null)
            foreach (var openingMask in openingMasks)
                if (openingMask != null && openingMask.sprite == slicedFill) openingMask.sprite = null;
        Release(slicedFrame); Release(slicedFill); Release(fillTexture);
        if (loadedSprite != null) { Release(loadedSprite.texture); Release(loadedSprite); }
        slicedFrame = slicedFill = loadedSprite = null; fillTexture = null;
    }
    private static void Release(UnityEngine.Object value)
    {
        if (value == null) return;
        if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
    }
}
