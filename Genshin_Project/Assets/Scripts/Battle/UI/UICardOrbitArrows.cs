using System;
using UnityEngine;

/// <summary>Short, single-pass vertical arrows. Call only for an applied status, never for its ongoing presence.</summary>
[DisallowMultipleComponent]
public sealed class UICardOrbitArrows : MonoBehaviour
{
    public bool Paused { get; set; }
    public bool IsPlaying { get; private set; }
    public string Error { get; private set; }
    public float Elapsed => elapsed;
    public bool Ready => isActiveAndEnabled && style != null && target != null && backRoot != null && frontRoot != null;
    private RectTransform target, backRoot, frontRoot;
    private UICardOrbitArrowGraphic back, front;
    private UICardOrbitArrowStyle style;
    private UICardStatusFlow flow;
    private Color tint;
    private float elapsed, duration, stopAge, stopAlpha;
    private bool stopping;

    public bool Bind(UICardFrameView card, UICardOrbitArrowStyle parameters = null)
        => Bind(card == null ? null : card.Root, card == null ? null : card.BackEffects,
            card == null ? null : card.FrontEffects, parameters);
    public bool Bind(RectTransform card, RectTransform behind, RectTransform ahead, UICardOrbitArrowStyle parameters = null)
    {
        Release(); target = card; backRoot = behind; frontRoot = ahead;
        if (card == null || behind == null || ahead == null || behind == ahead)
        { Error = "状态箭头需要卡牌与独立的前后特效挂点。"; return false; }
        try { style = parameters ?? UICardOrbitArrowStyle.Load(); Error = null; return true; }
        catch (Exception e) { style = null; Error = e.Message; return false; }
    }
    public bool TryPlay(UICardStatusFlow direction, float seconds = -1, Color? color = null)
    {
        if (!Ready || !target.gameObject.activeInHierarchy || !backRoot.gameObject.activeInHierarchy || !frontRoot.gameObject.activeInHierarchy
            || !Enum.IsDefined(typeof(UICardStatusFlow), direction)
            || float.IsNaN(seconds) || float.IsInfinity(seconds)) return false;
        if (back == null) back = Create("Status arrows behind", backRoot, false);
        if (front == null) front = Create("Status arrows front", frontRoot, true);
        flow = direction; tint = color ?? (flow == UICardStatusFlow.Buff ? style.BuffColor : style.DebuffColor);
        duration = seconds <= 0 ? style.Duration : seconds;
        elapsed = stopAge = 0; stopping = Paused = false; IsPlaying = true;
        back.gameObject.SetActive(true); front.gameObject.SetActive(true); Render(); return true;
    }
    public void Stop(bool immediate = false)
    {
        if (!IsPlaying) return;
        if (immediate || style == null || style.FadeOut <= 0) { Hide(); return; }
        if (!stopping) { stopAlpha = Alpha(); stopping = true; stopAge = 0; }
    }
    private float Alpha() => stopping ? stopAlpha * Mathf.Clamp01(1 - stopAge / Mathf.Max(.001f, style.FadeOut))
        : style.FadeIn > 0 ? Mathf.Clamp01(elapsed / style.FadeIn) : 1;
    private void Update()
    {
        if (!IsPlaying) return;
        if (!Ready || back == null || front == null || !target.gameObject.activeInHierarchy) { Hide(); return; }
        if (!Paused || stopping)
        {
            elapsed += BattlePause.DeltaTime;
            if (stopping) stopAge += BattlePause.DeltaTime;
            else if (duration > 0 && elapsed >= duration) { Stop(); if (stopping) stopAge = elapsed - duration; }
        }
        if (stopping && stopAge >= style.FadeOut) Hide(); else Render();
    }
    private void Render()
    {
        float alpha = Alpha();
        back.Present(target, style, flow, tint, elapsed, alpha);
        front.Present(target, style, flow, tint, elapsed, alpha);
    }
    private static UICardOrbitArrowGraphic Create(string name, RectTransform parent, bool ahead)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(UICardOrbitArrowGraphic));
        go.layer = parent.gameObject.layer;
        var rect = (RectTransform)go.transform; rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        var graphic = go.GetComponent<UICardOrbitArrowGraphic>(); graphic.Front = ahead;
        graphic.raycastTarget = false; graphic.maskable = false; return graphic;
    }
    private void Hide()
    {
        IsPlaying = stopping = false;
        if (back != null) back.gameObject.SetActive(false);
        if (front != null) front.gameObject.SetActive(false);
    }
    public void Clear() => Release();
    private void Release()
    {
        Hide();
        if (back != null) Destroy(back.gameObject);
        if (front != null) Destroy(front.gameObject);
        back = front = null;
    }
    private void OnDisable() => Release();
    private void OnDestroy() => Release();
}
