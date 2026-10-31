using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One visual death operation: compose card/frame/inner shadow together (optionally retaining live video), dissolve,
/// emit one batched fragment mesh, fade outer shadows separately, then signal after the particle tail.
/// No battle instance, damage events, source-transform edits, or automatic scene binding.
/// </summary>
[DisallowMultipleComponent]
public sealed class UIGoldenDissolveCard : MonoBehaviour
{
    public RectTransform cardRoot;
    [Tooltip("Bottom-to-top Image order. Do not include external shadows or HUD.")]
    public Image[] bodyLayers = Array.Empty<Image>();
    public Image[] outerShadows = Array.Empty<Image>();
    public UICardFrameView framedView;
    [Tooltip("Refresh the composed card every frame; the owning video source must keep decoding while hidden.")]
    public bool keepSourceAnimated;
    public bool Paused { get; set; }
    public bool IsPlaying { get; private set; }
    public float Elapsed { get; private set; }
    public float BodyProgress => style == null ? 0 : style.DurationSeconds <= 0 ? 1 : Mathf.Clamp01(Elapsed / style.DurationSeconds);

    private UIGoldenDissolveStyle style;
    private RenderTexture snapshot;
    private UICardDissolveSnapshot.CaptureSession liveCapture;
    private Graphic[] activeLayers;
    private GameObject output;
    private UIGoldenDissolve dissolve;
    private UIGoldenDissolveFragments fragments;
    private bool[] originalEnabled;
    private Color[] originalShadowColors;
    private Action completed;
    private int seed;
    private static uint nextVisualSeed;

    public bool Play(Action onCompleted = null)
        => Play(unchecked((int)GoldenDissolveMath.Mix(++nextVisualSeed ^ (uint)GetInstanceID())), onCompleted);

    public bool Play(int visualSeed, Action onCompleted = null)
    {
        UIGoldenDissolveStyle settings;
        try { settings = UIGoldenDissolveStyle.Load(); }
        catch (Exception error) { Debug.LogError("Card dissolve settings failed: " + error.Message, this); return false; }
        return Play(settings, visualSeed, onCompleted);
    }

    public bool Play(UIGoldenDissolveStyle settings, int visualSeed, Action onCompleted = null)
    {
        ResetEffect();
        try
        {
            if (settings == null || cardRoot == null) throw new ArgumentException("Card dissolve requires settings and cardRoot.");
            foreach (var shadow in outerShadows)
                if (Array.IndexOf(bodyLayers, shadow) >= 0) throw new ArgumentException("External shadows cannot also be body layers.");
            style = settings; seed = visualSeed;
            activeLayers = framedView != null ? framedView.SnapshotLayers : bodyLayers;
            foreach (var shadow in outerShadows)
                if (Array.IndexOf(activeLayers, shadow) >= 0) throw new ArgumentException("External shadows cannot also be body layers.");
            liveCapture = UICardDissolveSnapshot.BeginCapture(cardRoot, activeLayers, settings.CaptureHeightPixels,
                framedView != null ? framedView.SnapshotClip : null, framedView != null ? framedView.SnapshotClippedLayers : null);
            snapshot = liveCapture.Texture;
            if (!keepSourceAnimated) { liveCapture.Detach(); liveCapture.Dispose(); liveCapture = null; }
            originalEnabled = new bool[activeLayers.Length];
            for (int i = 0; i < activeLayers.Length; i++) originalEnabled[i] = activeLayers[i].enabled;
            originalShadowColors = new Color[outerShadows.Length];
            for (int i = 0; i < outerShadows.Length; i++)
                if (outerShadows[i] != null) originalShadowColors[i] = outerShadows[i].color;
            var size = cardRoot.rect.size;
            output = new GameObject("DeathVisual_Snapshot", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage), typeof(LayoutElement));
            output.GetComponent<LayoutElement>().ignoreLayout = true;
            var rect = output.GetComponent<RectTransform>();
            rect.SetParent(cardRoot, false); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = Vector2.zero; rect.sizeDelta = size;
            var image = output.GetComponent<RawImage>();
            image.texture = snapshot; image.raycastTarget = false;
            dissolve = output.AddComponent<UIGoldenDissolve>();
            dissolve.Configure(settings); dissolve.InputIsPremultiplied = true;
            dissolve.SetSourceBounds(new Rect(-size.x * .5f, -size.y * .5f, size.x, size.y));
            if (settings.FragmentCount > 0)
            {
                var particles = new GameObject("DeathVisual_GoldFragments", typeof(RectTransform), typeof(CanvasRenderer));
                var particleRect = particles.GetComponent<RectTransform>();
                particleRect.SetParent(rect, false); particleRect.anchorMin = particleRect.anchorMax = particleRect.pivot = new Vector2(.5f, .5f);
                particleRect.sizeDelta = size + Vector2.one * size.y *
                    (settings.FragmentDriftHeightRatio + settings.FragmentSpreadHeightRatio + settings.FragmentSizeMaxHeightRatio) * 2;
                fragments = particles.AddComponent<UIGoldenDissolveFragments>();
                fragments.Bind(snapshot, size, settings, visualSeed);
            }
            foreach (var layer in activeLayers) layer.enabled = false;
            Elapsed = 0; Paused = false; completed = onCompleted; IsPlaying = true;
            ApplyFrame();
            if (settings.TotalDuration == 0) Finish();
            return true;
        }
        catch (Exception error)
        {
            ResetEffect();
            Debug.LogError("Card dissolve could not start: " + error.Message, this);
            return false;
        }
    }

    private void Update()
    {
        if (!IsPlaying || Paused) return;
        Elapsed = Mathf.Min(style.TotalDuration, Elapsed + Mathf.Max(0, style.UseUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime));
        ApplyFrame();
        if (Elapsed >= style.TotalDuration) Finish();
    }
    private void LateUpdate()
    {
        if (IsPlaying && !Paused && liveCapture != null) liveCapture.Render();
    }

    private void ApplyFrame()
    {
        if (!dissolve.SetProgress(BodyProgress, seed)) throw new InvalidOperationException("Snapshot dissolve is unavailable.");
        if (fragments != null) fragments.SetElapsed(Elapsed);
        float opacity = GoldenDissolveMath.ShadowAlpha(BodyProgress, style.ShadowFadePower);
        for (int i = 0; i < outerShadows.Length; i++)
        {
            if (outerShadows[i] == null) continue;
            Color value = originalShadowColors[i]; value.a *= opacity;
            outerShadows[i].color = value;
        }
    }

    private void Finish()
    {
        IsPlaying = false;
        var callback = completed; completed = null;
        callback?.Invoke();
    }

    /// <summary>Cancel/recycle, restoring source enabled flags and shadow colours exactly. Does not signal completion.</summary>
    public void ResetEffect()
    {
        IsPlaying = false; completed = null; Paused = false; Elapsed = 0;
        if (originalEnabled != null)
            for (int i = 0; i < Mathf.Min(activeLayers.Length, originalEnabled.Length); i++)
                if (activeLayers[i] != null) activeLayers[i].enabled = originalEnabled[i];
        if (originalShadowColors != null)
            for (int i = 0; i < Mathf.Min(outerShadows.Length, originalShadowColors.Length); i++)
                if (outerShadows[i] != null) outerShadows[i].color = originalShadowColors[i];
        originalEnabled = null; originalShadowColors = null;
        if (output != null) { output.SetActive(false); UICardDissolveSnapshot.Dispose(output); }
        output = null; dissolve = null; fragments = null;
        if (liveCapture != null) { liveCapture.Dispose(); liveCapture = null; }
        else if (snapshot != null) { snapshot.Release(); UICardDissolveSnapshot.Dispose(snapshot); }
        snapshot = null; style = null; activeLayers = null;
    }

    private void OnDisable() => ResetEffect();
    private void OnDestroy() => ResetEffect();
}
