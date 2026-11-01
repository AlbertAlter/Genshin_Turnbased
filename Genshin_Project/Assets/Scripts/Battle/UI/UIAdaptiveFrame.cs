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
    public bool Ready => frameImage != null && frameImage.sprite != null;

    private RectTransform visuals;
    private Image fillImage, frameImage;
    private Sprite loadedSprite, slicedFrame, slicedFill;
    private Texture2D fillTexture;
    private Coroutine request;
    private int version;

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
        var alpha = new byte[pixels.Length];
        for (int i = 0; i < pixels.Length; i++) alpha[i] = pixels[i].a;
        // 与角色卡面使用同一闭合开口及两像素内侧搭接，底色不会漏到框外。
        Color32[] mask = UICardFrameView.BuildOpeningMask(alpha, width, height);
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
        RefreshAppearance();
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
        if (frameImage != null) frameImage.pixelsPerUnitMultiplier = multiplier;
        if (fillImage == null) return;
        fillImage.pixelsPerUnitMultiplier = multiplier;
        fillImage.color = fillColor;
        fillImage.enabled = showFill;
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
        visuals = null; frameImage = fillImage = null;
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
