using System;
using UnityEngine;

/// <summary>Explicitly bound authoring viewport zoom. Owns temporary transforms, no input polling or scene search.</summary>
public sealed class UIViewportZoom
{
    private RectTransform content;
    private Vector2 initialPosition;
    private Vector3 initialScale;
    public float Factor { get; private set; } = 1;
    public void Bind(RectTransform target)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));
        Unbind(); content = target; initialPosition = target.anchoredPosition; initialScale = target.localScale;
    }
    public bool ZoomAt(Vector2 screenPoint, Camera camera, float wheelSteps)
    {
        if (content == null || !(content.parent is RectTransform parent)) return false;
        float next = UIViewportZoomMath.NextFactor(Factor, wheelSteps);
        if (Mathf.Approximately(Factor, next) || !RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenPoint, camera, out var anchor)) return false;
        var pivot = content.localPosition;
        var moved = UIViewportZoomMath.KeepAnchor(anchor, pivot, Factor, next);
        content.localScale = initialScale * next;
        content.localPosition = new Vector3(moved.x, moved.y, pivot.z);
        Factor = next; return true;
    }
    public void Reset()
    {
        if (content != null) { content.localScale = initialScale; content.anchoredPosition = initialPosition; }
        Factor = 1;
    }
    public void Unbind() { Reset(); content = null; }
}

public static class UIViewportZoomMath
{
    public const float MinimumFactor = .4f, MaximumFactor = 3;
    public static float NextFactor(float current, float wheelSteps)
    {
        if (!(current > 0) || float.IsInfinity(current)) throw new ArgumentOutOfRangeException(nameof(current));
        if (float.IsNaN(wheelSteps) || float.IsInfinity(wheelSteps)) return current;
        double next = current * Math.Pow(1.12, wheelSteps);
        return (float)Math.Max(MinimumFactor, Math.Min(MaximumFactor, next));
    }
    public static Vector2 KeepAnchor(Vector2 anchor, Vector2 oldPivot, float oldFactor, float newFactor)
        => anchor + (oldPivot - anchor) * (newFactor / oldFactor);
}
