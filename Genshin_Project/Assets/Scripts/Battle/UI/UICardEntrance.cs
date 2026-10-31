using System;
using UnityEngine;

/// <summary>Optional entrance on an explicitly bound motion container, leaving scene layout transforms alone.</summary>
[DisallowMultipleComponent]
public sealed class UICardEntrance : MonoBehaviour
{
    public RectTransform motionRoot;
    public CanvasGroup opacity;
    public bool Paused { get; set; }
    public bool IsPlaying { get; private set; }
    private UICardEntranceStyle style;
    private Vector2 origin, offset;
    private Vector3 scale;
    private float originalAlpha, elapsed, delay;
    private bool captured;
    private Action completed;
    public void Play(UICardEntranceStyle settings, float directionX, float delaySeconds = 0, Action onCompleted = null)
    {
        Restore();
        if (settings == null || motionRoot == null || opacity == null) throw new InvalidOperationException("入场需要参数、动画容器和 CanvasGroup。");
        style = settings; origin = motionRoot.anchoredPosition; scale = motionRoot.localScale; originalAlpha = opacity.alpha;
        captured = true; elapsed = 0; delay = Mathf.Max(0, delaySeconds);
        offset = Vector2.right * directionX * motionRoot.rect.width * style.OffsetWidthRatio;
        completed = onCompleted; IsPlaying = true; Paused = false; Apply(0);
    }
    private void Update()
    {
        if (!IsPlaying || Paused) return;
        elapsed += Mathf.Max(0, style.Unscaled ? Time.unscaledDeltaTime : Time.deltaTime);
        if (elapsed < delay) return;
        float progress = style.Duration <= 0 ? 1 : Mathf.Clamp01((elapsed - delay) / style.Duration);
        Apply(progress);
        if (progress >= 1)
        {
            var callback = completed;
            Restore(); callback?.Invoke();
        }
    }
    private void Apply(float progress)
    {
        float eased = 1 - Mathf.Pow(1 - progress, style.EasePower);
        motionRoot.anchoredPosition = origin + offset * (1 - eased);
        motionRoot.localScale = scale * Mathf.Lerp(style.StartScale, 1, eased);
        opacity.alpha = originalAlpha * Mathf.Lerp(style.StartAlpha, 1, eased);
    }
    public void Restore()
    {
        IsPlaying = false; completed = null; Paused = false;
        if (captured)
        {
            if (motionRoot != null) { motionRoot.anchoredPosition = origin; motionRoot.localScale = scale; }
            if (opacity != null) opacity.alpha = originalAlpha;
        }
        captured = false;
    }
    private void OnDisable() => Restore();
    private void OnDestroy() => Restore();
}
