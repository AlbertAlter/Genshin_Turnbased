using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>A release inside the same hit area clicks once. Holding consumes that click.</summary>
public sealed class UIBattlePressArea : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler, IPointerClickHandler
{
    private Action click, hold, release;
    private float threshold;
    private int? pointer;
    private double pressedAt;
    private bool held;

    public void Bind(float seconds, Action onClick, Action onHold, Action onRelease)
    {
        threshold = Mathf.Max(.05f, seconds);
        click = onClick;
        hold = onHold;
        release = onRelease;
    }
    public void OnPointerDown(PointerEventData e)
    {
        if (pointer.HasValue || e.button != PointerEventData.InputButton.Left) return;
        pointer = e.pointerId;
        pressedAt = Time.unscaledTimeAsDouble;
    }
    private void Update()
    {
        if (pointer.HasValue) TryBeginHold();
    }
    private void TryBeginHold()
    {
        if (held || Time.unscaledTimeAsDouble - pressedAt < threshold) return;
        held = true;
        hold?.Invoke();
    }
    public void OnPointerUp(PointerEventData e)
    {
        if (pointer != e.pointerId || e.button != PointerEventData.InputButton.Left) return;
        bool inside = RectTransformUtility.RectangleContainsScreenPoint((RectTransform)transform, e.position, e.pressEventCamera);
        // A low-frame-rate release can arrive before this component's Update.
        if (inside) TryBeginHold();
        bool tap = inside && !held && pointer.HasValue;
        ResetPress();
        if (tap) click?.Invoke();
    }
    public void OnPointerExit(PointerEventData e) { if (pointer == e.pointerId) ResetPress(); }
    // UGUI resolves click independently of pointer-up. Consume it here so a parent Button
    // cannot also fire after a tap, a hold or a cancelled gesture.
    public void OnPointerClick(PointerEventData e) { }
    private void OnDisable() => ResetPress();
    private void OnApplicationFocus(bool focus) { if (!focus) ResetPress(); }
    private void ResetPress()
    {
        bool dismiss = held;
        pointer = null;
        held = false;
        if (dismiss) release?.Invoke();
    }
}
