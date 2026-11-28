using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 公共按钮按下／松开表现。只通过共享网格偏移层缩放图形，不修改布局、点击区、颜色或业务状态。
/// 可挂在原Button或UIBattlePressArea上；键盘入口可调用Pulse，同一次操作不重复提交命令。
/// </summary>
public sealed class UIButtonClickFeedback : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    public RectTransform visualRoot, center;
    [Range(.8f, 1)] public float pressedScale = .94f;
    [Min(.01f)] public float pressSeconds = .055f, releaseSeconds = .16f;
    [Range(0, .1f)] public float rebound = .025f;
    private readonly List<UIShakeMeshOffset> meshes = new();
    private Func<bool> available;
    private bool held, animating, releasing;
    private float age, from = 1, scale = 1;
    private int? pointer;
    public void Bind(RectTransform root, RectTransform pivot = null, Func<bool> canUse = null)
    { Clear(); visualRoot = root; center = pivot != null ? pivot : root; available = canUse; }
    private bool CanUse() => isActiveAndEnabled && visualRoot != null && visualRoot.gameObject.activeInHierarchy
        && available?.Invoke() != false && (GetComponent<Selectable>() is not { } button || button.IsInteractable());
    private void Collect()
    {
        ClearMeshes();
        foreach (var graphic in visualRoot.GetComponentsInChildren<Graphic>(true))
        {
            // 命中区为透明图形，不参加绘制；保留原Graphic上的其他所有者（如震屏）的变形。
            if (graphic.gameObject == gameObject && graphic.color.a == 0) continue;
            var mesh = graphic.GetComponent<UIShakeMeshOffset>() ?? graphic.gameObject.AddComponent<UIShakeMeshOffset>();
            meshes.Add(mesh);
        }
    }
    public void OnPointerDown(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Left || pointer.HasValue || !CanUse()) return;
        pointer = e.pointerId; Press(true);
    }
    public void OnPointerUp(PointerEventData e) { if (pointer == e.pointerId) Release(); }
    public void OnPointerExit(PointerEventData e) { if (pointer == e.pointerId) Release(); }
    private void Press(bool hold)
    { Collect(); held = hold; releasing = false; animating = true; age = 0; from = scale; }
    private void Release()
    { pointer = null; held = false; if (!animating) return; releasing = true; age = 0; from = scale; }
    public void Pulse() { if (!CanUse()) return; Press(false); }
    public static float Sample(float start, float pressed, float bounce, float t, bool release)
    {
        t = Mathf.Clamp01(t);
        if (!release) return Mathf.Lerp(start, pressed, t * t * (3 - 2 * t));
        return Mathf.Lerp(start, 1, 1 - (1 - t) * (1 - t)) + Mathf.Sin(t * Mathf.PI) * bounce;
    }
    private void LateUpdate()
    {
        if (!animating) return;
        if (visualRoot == null || !visualRoot.gameObject.activeInHierarchy) { Clear(); return; }
        age += Time.unscaledDeltaTime;
        float duration = releasing ? releaseSeconds : pressSeconds;
        float progress = Mathf.Clamp01(age / Mathf.Max(.01f, duration));
        scale = Sample(from, pressedScale, rebound, progress, releasing);
        var pivot = center != null ? center : visualRoot;
        var local = pivot.rect.center;
        Vector3 point = pivot.TransformPoint(new Vector3(local.x, local.y, 0));
        var delta = Matrix4x4.Translate(point) * Matrix4x4.Scale(Vector3.one * scale) * Matrix4x4.Translate(-point);
        foreach (var mesh in meshes) if (mesh != null) mesh.SetWorldDelta(this, delta);
        if (progress < 1) return;
        if (releasing) Clear(); else if (!held) Release();
    }
    private void ClearMeshes() { foreach (var mesh in meshes) if (mesh != null) mesh.ClearWorldDelta(this); meshes.Clear(); }
    private void Clear() { ClearMeshes(); held = animating = releasing = false; pointer = null; scale = from = 1; age = 0; }
    private void OnDisable() => Clear();
    private void OnApplicationFocus(bool focused) { if (!focused) Clear(); }
}
