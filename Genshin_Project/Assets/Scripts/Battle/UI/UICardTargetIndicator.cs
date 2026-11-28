using System;
using UnityEngine;
using UnityEngine.UI;

[Serializable]
public sealed class CardTargetIndicatorStyle
{
    public Vector2 referenceSize = new Vector2(156, 159);
    public Vector2 centerOffset = new Vector2(2.5f, -6.5f);
    [Min(.01f)] public float referenceCardWidth = 197;
    [Range(.25f, 1.5f)] public float sizeMultiplier = 1;
    [Min(0)] public float enterSeconds = .12f, settleSeconds = .06f, exitSeconds = .12f;
    [Min(0)] public float tipTravel = 6, tipOvershoot = 1;
    [Range(1, 1.2f)] public float ringStartScale = 1.03f;
    public bool idleMotion = true;
    [Min(.1f)] public float idleCycleSeconds = 2.8f;
    [Range(0, .1f)] public float idleScaleAmount = .015f;
    [Range(0, 1)] public float idleGlowDip = .2f;
    [Tooltip("整组准心绕中心旋转的速度；字段名保留以兼容已有场景参数。")]
    public float ringDegreesPerSecond = 8;
    public bool outerGlow = true;
    [Min(0)] public float glowSize = 81;
    [Range(0, 100)] public float glowSpread;
    public Color glowColor = new Color(227f / 255, 144.214f / 255, 144.214f / 255, .74f);
    public CardTargetIndicatorStyle Copy() => (CardTargetIndicatorStyle)MemberwiseClone();

    public void SampleIdle(double seconds, out float scale, out float glowOpacity, out float angle)
    {
        scale = glowOpacity = 1; angle = 0;
        if (!idleMotion) return;
        double wave = .5 - .5 * Math.Cos(seconds / Math.Max(.1, idleCycleSeconds) * 2 * Math.PI);
        scale += (float)wave * idleScaleAmount;
        glowOpacity -= (float)wave * idleGlowDip;
        angle = (float)(seconds * ringDegreesPerSecond % 360);
    }
}

/// <summary>Reusable targeting animation. Owns only overlay graphics, never card layout or battle state.</summary>
[DisallowMultipleComponent]
public sealed class UICardTargetIndicator : MonoBehaviour, IUICardFaceEffect
{
    public CardTargetIndicatorStyle style = new CardTargetIndicatorStyle();
    public bool automaticClock = true;
    public bool Paused { get; set; }
    public bool Ready => isActiveAndEnabled && resources != null && resources.Ready && root != null && cardArea != null;
    public bool Visible => phase != Phase.Hidden;
    public string Error { get; private set; }
    private enum Phase { Hidden, Enter, Settle, Hold, Exit }
    private Phase phase;
    private RectTransform cardArea;
    private UICardTargetAssets resources;
    private RectTransform root, ring, glow;
    private CanvasGroup alpha;
    private RawImage glowImage;
    private UICardTargetAssets.GlowHandle glowHandle;
    private int glowSizeKey = -1, glowSpreadKey = -1;
    private readonly RectTransform[] tips = new RectTransform[4];
    private readonly Vector2[] startTips = new Vector2[4];
    private static readonly Vector2[] Directions = { Vector2.up, Vector2.right, Vector2.down, Vector2.left };
    private float time, ratio, enterAlpha, enterRingScale, enterRootScale, exitAlpha, exitScale;
    private double idleTime;

    public bool Bind(UICardFrameView target, UICardTargetAssets art)
        => Bind(target, art, target != null ? target.FrontEffects : null);

    // An independent card-face host may provide its own layer container within this card's foreground.
    public bool Bind(UICardFrameView target, UICardTargetAssets art, RectTransform mount)
        => Bind(target != null ? target.Root : null, art, mount);

    // A field-hosted marker uses the fixed card-slot area, even when no card occupies it.
    public bool Bind(RectTransform area, UICardTargetAssets art, RectTransform mount)
    {
        Clear(); Error = null;
        if (!isActiveAndEnabled || area == null || mount == null || art == null || !art.Ready)
        { Error = "准心需要显式绑定卡位范围、显示挂点和已就绪的共享素材。"; return false; }
        cardArea = area; resources = art; resources.Invalidated += Clear;
        resources.ParametersChanged += ApplyConfiguredStyle;
        root = Rect("CardTargetIndicator", mount);
        alpha = root.gameObject.AddComponent<CanvasGroup>();
        alpha.interactable = false; alpha.blocksRaycasts = false;
        glow = Rect("OuterGlow", root);
        glowImage = glow.gameObject.AddComponent<RawImage>();
        glowImage.raycastTarget = false;
        ring = Graphic("Ring", root, art.GetSprite(0));
        for (int i = 0; i < tips.Length; i++) tips[i] = Graphic(UICardTargetAssets.TextureFiles[i + 1], root, art.GetSprite(i + 1));
        ApplyConfiguredStyle();
        Hide(true);
        return true;
    }

    private void ApplyConfiguredStyle() => ApplyStyle(resources.Parameters.ApplyTo(style));

    public void ApplyStyle(CardTargetIndicatorStyle value)
    {
        if (value == null) throw new ArgumentNullException(nameof(value));
        style = value.Copy();
        style.referenceCardWidth = Mathf.Max(.01f, style.referenceCardWidth);
        style.enterSeconds = Mathf.Max(0, style.enterSeconds); style.settleSeconds = Mathf.Max(0, style.settleSeconds);
        style.exitSeconds = Mathf.Max(0, style.exitSeconds);
        style.idleCycleSeconds = Mathf.Max(.1f, style.idleCycleSeconds);
        style.idleScaleAmount = Mathf.Clamp(style.idleScaleAmount, 0, .1f);
        style.idleGlowDip = Mathf.Clamp01(style.idleGlowDip);
        RefreshSize();
        if (root != null) Sample();
    }
    private void RefreshSize()
    {
        if (root == null || cardArea == null) return;
        ratio = cardArea.rect.width / style.referenceCardWidth * Mathf.Max(.01f, style.sizeMultiplier);
        root.sizeDelta = style.referenceSize * ratio;
        root.anchoredPosition = style.centerOffset * (cardArea.rect.width / style.referenceCardWidth);
        RefreshGlow();
    }
    private void RefreshGlow()
    {
        glowImage.enabled = false;
        if (!style.outerGlow)
        {
            glowImage.texture = null; glowImage.material = null;
            glowHandle?.Dispose(); glowHandle = null;
            glowSizeKey = glowSpreadKey = -1; return;
        }
        Sprite silhouette = resources.GetSprite(5);
        int size = Mathf.Max(0, Mathf.RoundToInt(style.glowSize * silhouette.rect.width / Mathf.Max(.01f, style.referenceSize.x)));
        int spread = Mathf.Clamp(Mathf.RoundToInt(style.glowSpread), 0, 100);
        if (glowHandle == null || size != glowSizeKey || spread != glowSpreadKey)
        {
            var replacement = resources.AcquireGlow(size, spread);
            glowImage.texture = replacement.Texture;
            glowImage.material = replacement.Material;
            glowHandle?.Dispose(); glowHandle = replacement;
            glowSizeKey = size; glowSpreadKey = spread;
        }
        Texture2D texture = glowHandle.Texture;
        glow.sizeDelta = new Vector2(root.sizeDelta.x * texture.width / silhouette.rect.width,
            root.sizeDelta.y * texture.height / silhouette.rect.height);
        glowImage.color = style.glowColor;
        glowImage.enabled = true;
    }
    public bool Show()
    {
        if (!Ready) return false;
        if (phase == Phase.Enter || phase == Phase.Settle || phase == Phase.Hold) return true;
        bool hidden = phase == Phase.Hidden;
        if (hidden) idleTime = 0;
        root.gameObject.SetActive(true);
        enterRootScale = hidden ? 1 : root.localScale.x;
        enterAlpha = hidden ? 0 : alpha.alpha;
        enterRingScale = hidden ? style.ringStartScale : ring.localScale.x;
        for (int i = 0; i < tips.Length; i++)
            startTips[i] = hidden ? Directions[i] * style.tipTravel * ratio : tips[i].anchoredPosition;
        time = 0; phase = Phase.Enter; Sample();
        if (style.enterSeconds <= 0) Advance(0);
        return true;
    }
    public void Hide(bool immediate = false)
    {
        if (root == null) { phase = Phase.Hidden; return; }
        if (immediate || style.exitSeconds <= 0)
        {
            phase = Phase.Hidden; time = 0; idleTime = 0; alpha.alpha = 0;
            root.localScale = Vector3.one; root.gameObject.SetActive(false); return;
        }
        if (phase == Phase.Hidden || phase == Phase.Exit) return;
        exitAlpha = alpha.alpha; exitScale = root.localScale.x;
        time = 0; phase = Phase.Exit;
    }
    private void Update()
    {
        if (automaticClock && !Paused) Advance(BattlePause.DeltaTime);
    }
    public void Advance(float seconds)
    {
        if (!Ready || Paused || seconds < 0 || float.IsNaN(seconds) || float.IsInfinity(seconds)) return;
        float width = cardArea.rect.width / style.referenceCardWidth * Mathf.Max(.01f, style.sizeMultiplier);
        if (!Mathf.Approximately(width, ratio)) RefreshSize();
        float remaining = seconds;
        for (int transitions = 0; transitions < 4; transitions++)
        {
            if (phase == Phase.Hidden) break;
            if (phase == Phase.Hold) { idleTime += remaining; break; }
            float duration = phase == Phase.Enter ? style.enterSeconds : phase == Phase.Settle ? style.settleSeconds : style.exitSeconds;
            float step = Mathf.Min(remaining, Mathf.Max(0, duration - time));
            time += step; remaining -= step;
            if (time < duration) break;
            if (phase == Phase.Exit) { Hide(true); return; }
            phase = phase == Phase.Enter ? Phase.Settle : Phase.Hold; time = 0;
            if (remaining <= 0 && duration > 0) break;
        }
        Sample();
    }
    private void Sample()
    {
        if (root == null || phase == Phase.Hidden) return;
        if (phase == Phase.Exit)
        {
            float t = Ease(time, style.exitSeconds);
            alpha.alpha = Mathf.Lerp(exitAlpha, 0, t);
            root.localScale = Vector3.one * Mathf.Lerp(exitScale, .96f, t);
            return;
        }
        style.SampleIdle(idleTime, out float idleScale, out float glowOpacity, out float angle);
        // Rotate the complete silhouette: ring, tips and their glow share one center.
        root.localRotation = Quaternion.Euler(0, 0, -angle);
        ring.localRotation = Quaternion.identity;
        Color glowColor = style.glowColor; glowColor.a *= glowOpacity;
        glowImage.color = glowColor;
        if (phase == Phase.Enter)
        {
            float t = Ease(time, style.enterSeconds);
            root.localScale = Vector3.one * Mathf.Lerp(enterRootScale, 1, t);
            alpha.alpha = Mathf.Lerp(enterAlpha, 1, Ease(time, Mathf.Min(.06f, style.enterSeconds)));
            ring.localScale = glow.localScale = Vector3.one * Mathf.Lerp(enterRingScale, idleScale, t);
            for (int i = 0; i < tips.Length; i++)
                tips[i].anchoredPosition = Vector2.Lerp(startTips[i], -Directions[i] * style.tipOvershoot * ratio, t);
        }
        else
        {
            root.localScale = Vector3.one;
            alpha.alpha = 1; ring.localScale = glow.localScale = Vector3.one * idleScale;
            float t = phase == Phase.Hold ? 1 : Ease(time, style.settleSeconds);
            for (int i = 0; i < tips.Length; i++) tips[i].anchoredPosition = -Directions[i] * style.tipOvershoot * ratio * (1 - t);
        }
    }
    private static float Ease(float value, float duration) => duration <= 0 ? 1 : Mathf.SmoothStep(0, 1, Mathf.Clamp01(value / duration));
    private static RectTransform Rect(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f;
        return rect;
    }
    private static RectTransform Graphic(string name, RectTransform parent, Sprite sprite)
    {
        RectTransform rect = Rect(name, parent);
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        var image = rect.gameObject.AddComponent<Image>(); image.sprite = sprite; image.raycastTarget = false;
        return rect;
    }
    public void Clear()
    {
        if (resources != null) resources.Invalidated -= Clear;
        if (resources != null) resources.ParametersChanged -= ApplyConfiguredStyle;
        if (glowImage != null) { glowImage.texture = null; glowImage.material = null; }
        if (root != null)
        {
            root.gameObject.SetActive(false);
            if (Application.isPlaying) Destroy(root.gameObject); else DestroyImmediate(root.gameObject);
        }
        glowHandle?.Dispose(); glowHandle = null; glowSizeKey = glowSpreadKey = -1;
        resources = null; cardArea = null; root = ring = glow = null; alpha = null; glowImage = null;
        for (int i = 0; i < tips.Length; i++) tips[i] = null;
        phase = Phase.Hidden; time = 0; idleTime = 0;
    }
    private void OnDisable() => Clear();
}
