using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Shared card requests. Scenes supply a mount, identity and animation preference, never skin layers.</summary>
[DisallowMultipleComponent]
public sealed class UICardFactory : MonoBehaviour
{
    public const string FramePath = "art_assets/Character_Bare/Frame/01.png";
    public const string InnerShadowPath = "art_assets/UI/BattleField1/UI_components/card/CardInnerShadow.png";
    public const string OuterShadowPath = "art_assets/UI/BattleField1/UI_components/card/CardShadow.png";
    public const string GlowPath = "art_assets/UI/BattleField1/UI_components/card/CardOverGlow.png";
    public const float Aspect = 420f / 720f;
    private readonly Dictionary<string, Sprite> assets = new Dictionary<string, Sprite>();
    private readonly HashSet<string> pending = new HashSet<string>(), failed = new HashSet<string>();
    private readonly HashSet<int> videos = new HashSet<int>();
    private sealed class RequestState
    { public UICardView View; public BattleSide Side; public int Id; public bool Animated, Poster, Active = true; }
    private readonly List<RequestState> cards = new List<RequestState>();
    private bool catalogStarted, catalogReady;

    public UICardView Request(RectTransform mount, BattleSide side, int id, bool animated = true, bool useVideoPoster = false)
    {
        if (mount == null || id <= 0) throw new ArgumentException("卡牌请求需要有效的挂载位置和身份编号。");
        if (!isActiveAndEnabled) throw new InvalidOperationException("卡牌服务未启用。");
        var root = new GameObject("CardView", typeof(RectTransform));
        var rect = root.GetComponent<RectTransform>(); rect.SetParent(mount, false); rect.SetAsFirstSibling();
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        var view = root.AddComponent<UICardView>();
        var request = new RequestState { View = view, Side = side, Id = id, Animated = animated, Poster = useVideoPoster };
        cards.Add(request); StartCoroutine(Prepare(request));
        return view;
    }
    private IEnumerator Catalog()
    {
        catalogStarted = true;
        yield return UICardVideoCatalog.Load(videos,
            error => Debug.LogWarning("卡牌视频清单读取失败，使用静态卡面：" + error, this));
        catalogReady = true;
    }
    private IEnumerator Prepare(RequestState request)
    {
        var view = request.View; var side = request.Side; int id = request.Id; bool animated = request.Animated;
        if (animated && side == BattleSide.Ally && !catalogStarted) StartCoroutine(Catalog());
        string art = side == BattleSide.Ally ? request.Poster ? UICardVideoPlayer.PosterPathForCharacter(id)
            : $"art_assets/Character_Bare/{id}_03.png" : $"art_assets/Enemy/{id}_01.png";
        foreach (string path in new[] { FramePath, InnerShadowPath, OuterShadowPath, GlowPath, art })
        {
            if (!assets.ContainsKey(path) && !pending.Contains(path) && !failed.Contains(path))
            { pending.Add(path); StartCoroutine(LoadAsset(path)); }
            while (pending.Contains(path)) yield return null;
            if (view == null || view.Released) yield break;
            if (!assets.ContainsKey(path)) { view.Fail("卡牌资源缺失：" + path); yield break; }
        }
        try
        {
            if (!view.Ready) view.Initialize(assets[FramePath], assets[InnerShadowPath], assets[OuterShadowPath], assets[GlowPath], assets[art].texture);
        }
        catch (Exception error) { view.Fail(error.Message); }
        if (view.Error != null) yield break;
        if (side != BattleSide.Ally || !request.Animated) { view.MediaResolved = true; yield break; }
        while (!catalogReady) { if (view == null || view.Released) yield break; yield return null; }
        if (view != null && !view.Released)
        {
            if (request.Animated && videos.Contains(id) && view.Video == null) view.PlayVideo(UICardVideoPlayer.VideoPathForCharacter(id));
            view.MediaResolved = true;
        }
    }
    private IEnumerator LoadAsset(string path)
    {
        yield return StreamingAssetSpriteLoader.Load(path, sprite => assets[path] = sprite,
            error => { failed.Add(path); Debug.LogWarning(error, this); });
        pending.Remove(path);
    }
    private RequestState Find(UICardView view) => cards.Find(c => c.View == view);
    private static string VariantPath(RequestState request, int variant) =>
        $"art_assets/{(request.Side == BattleSide.Ally ? "Character_Bare" : "Enemy")}/{request.Id}_{variant:D2}.png";

    /// <summary>Preload a static variant into this factory's shared cache, without changing the visible card.</summary>
    public IEnumerator PreloadVariant(UICardView view, int variant)
    {
        var request = Find(view);
        if (!isActiveAndEnabled || request == null || view == null || view.Released || variant <= 0) yield break;
        string path = VariantPath(request, variant);
        if (!assets.ContainsKey(path) && !pending.Contains(path))
        { failed.Remove(path); pending.Add(path); StartCoroutine(LoadAsset(path)); }
        while (isActiveAndEnabled && pending.Contains(path) && view != null && !view.Released) yield return null;
    }
    public bool HasVariant(UICardView view, int variant)
    {
        var request = Find(view);
        return isActiveAndEnabled && request != null && view != null && !view.Released && view.Ready &&
            view.isActiveAndEnabled && variant > 0 && assets.ContainsKey(VariantPath(request, variant));
    }
    internal Texture GetVariantTexture(UICardView view, int variant) => HasVariant(view, variant)
        ? assets[VariantPath(Find(view), variant)].texture : null;
    /// <summary>Atomic texture-only change. Keeps mask, frame, tint and effect mounts; an explicit static variant stops video.</summary>
    public bool TrySetVariant(UICardView view, int variant)
    {
        if (!HasVariant(view, variant)) return false;
        var request = Find(view);
        request.Animated = false; view.MediaResolved = true;
        view.SetStaticVariant(assets[VariantPath(request, variant)].texture);
        return true;
    }
    public void Release(UICardView view)
    {
        cards.RemoveAll(c => c.View == view);
        if (view == null) return;
        view.Clear(); view.gameObject.SetActive(false); Destroy(view.gameObject);
    }
    private void OnDisable()
    {
        StopAllCoroutines();
        pending.Clear(); if (!catalogReady) catalogStarted = false;
        foreach (var request in cards)
            if (request.View != null)
            { request.Active = request.View.gameObject.activeSelf; request.View.gameObject.SetActive(false); }
    }
    private void OnEnable()
    {
        foreach (var request in cards)
            if (request.View != null && !request.View.Released)
            {
                request.View.gameObject.SetActive(request.Active);
                if (!request.View.Ready || !request.View.MediaResolved) StartCoroutine(Prepare(request));
            }
    }
    private void OnDestroy()
    {
        StopAllCoroutines();
        foreach (var request in cards.ToArray()) Release(request.View);
        foreach (var sprite in assets.Values)
            if (sprite != null) { Destroy(sprite.texture); Destroy(sprite); }
        assets.Clear(); pending.Clear(); failed.Clear(); videos.Clear();
        catalogStarted = catalogReady = false;
    }
}
