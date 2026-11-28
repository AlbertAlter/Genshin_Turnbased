using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Shared cropped battle backdrop and cursor-centred zoom for UI/VFX authoring. Owns no effects, cards or battle.</summary>
public sealed class UIBattlePreviewStage : IDisposable
{
    public RectTransform Viewport { get; private set; }
    public RectTransform Root { get; private set; }
    public UIViewportZoom Zoom { get; } = new UIViewportZoom();
    private Sprite background;
    private int generation;
    public IEnumerator Create(Canvas canvas, Vector2 position, float scale, Action<string> fail)
    {
        Dispose(); int token = generation;
        Viewport = Mount("BattleViewport", (RectTransform)canvas.transform, new Vector2(1920, 1080) * scale);
        Viewport.anchoredPosition = position; Viewport.gameObject.AddComponent<RectMask2D>();
        Root = Mount("BattleStage", Viewport, new Vector2(1920, 1080));
        Root.localScale = Vector3.one * scale; Zoom.Bind(Root);
        var image = new GameObject("BattleBackground", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>();
        image.rectTransform.SetParent(Root, false); image.rectTransform.anchorMin = Vector2.zero;
        image.rectTransform.anchorMax = Vector2.one; image.rectTransform.offsetMin = image.rectTransform.offsetMax = Vector2.zero;
        image.raycastTarget = false;
        yield return StreamingAssetSpriteLoader.Load("art_assets/BattleBG/1_BG.png", sprite =>
        {
            if (token != generation || image == null) { Release(sprite); return; }
            background = sprite; image.sprite = sprite;
        }, error => { if (token == generation) fail(error); });
    }
    public static RectTransform Mount(string name, RectTransform parent, Vector2 size)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f;
        rect.sizeDelta = size; return rect;
    }
    public void Dispose()
    {
        generation++; Zoom.Unbind();
        if (Viewport != null) { Viewport.gameObject.SetActive(false); Destroy(Viewport.gameObject); }
        Viewport = Root = null; Release(background); background = null;
    }
    private static void Release(Sprite sprite)
    { if (sprite == null) return; var texture = sprite.texture; Destroy(sprite); if (texture != null) Destroy(texture); }
    private static void Destroy(UnityEngine.Object item)
    { if (item == null) return; if (Application.isPlaying) UnityEngine.Object.Destroy(item); else UnityEngine.Object.DestroyImmediate(item); }
}
