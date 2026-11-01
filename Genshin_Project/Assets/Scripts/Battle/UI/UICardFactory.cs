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
    public const string GlowPath = "art_assets/UI/BattleField1/UI_components/card/CardSelectionBorder.png";
    public const float Aspect = 420f / 720f;
    private readonly Dictionary<string, Sprite> assets = new Dictionary<string, Sprite>();
    private readonly HashSet<string> pending = new HashSet<string>(), failed = new HashSet<string>();
    private readonly HashSet<int> videos = new HashSet<int>();
    private readonly HashSet<string> enemyVideos = new HashSet<string>(StringComparer.Ordinal);
    private sealed class RequestState
    { public UICardView View; public BattleSide Side; public int Id, Variant; public bool Animated, Poster, Active = true; }
    private readonly List<RequestState> cards = new List<RequestState>();
    private bool catalogStarted, catalogReady;
    private bool enemyCatalogStarted, enemyCatalogReady;

    public UICardView Request(RectTransform mount, BattleSide side, int id, bool animated = true, bool useVideoPoster = false)
    {
        if (mount == null || id <= 0) throw new ArgumentException("卡牌请求需要有效的挂载位置和身份编号。");
        if (!isActiveAndEnabled) throw new InvalidOperationException("卡牌服务未启用。");
        var root = new GameObject("CardView", typeof(RectTransform));
        var rect = root.GetComponent<RectTransform>(); rect.SetParent(mount, false); rect.SetAsFirstSibling();
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        var view = root.AddComponent<UICardView>();
        var request = new RequestState { View = view, Side = side, Id = id,
            Variant = side == BattleSide.Ally ? 3 : 1, Animated = animated, Poster = useVideoPoster };
        cards.Add(request); StartCoroutine(Prepare(request));
        return view;
    }
    private IEnumerator Catalog(BattleSide side)
    {
        if (side == BattleSide.Enemy)
        {
            enemyCatalogStarted = true;
            yield return UICardVideoCatalog.LoadEnemies(enemyVideos,
                error => Debug.LogWarning("敌人视频清单读取失败，使用静态卡面：" + error, this));
            enemyCatalogReady = true;
        }
        else
        {
            catalogStarted = true;
            yield return UICardVideoCatalog.Load(videos,
                error => Debug.LogWarning("卡牌视频清单读取失败，使用静态卡面：" + error, this));
            catalogReady = true;
        }
    }
    private IEnumerator EnsureCatalog(RequestState request)
    {
        if (!request.Animated) yield break;
        bool enemy = request.Side == BattleSide.Enemy;
        if (!(enemy ? enemyCatalogStarted : catalogStarted)) StartCoroutine(Catalog(request.Side));
        while (!(enemy ? enemyCatalogReady : catalogReady))
        {
            if (request.View == null || request.View.Released) yield break;
            yield return null;
        }
    }
    private IEnumerator Prepare(RequestState request)
    {
        var view = request.View;
        yield return EnsureCatalog(request);
        if (view == null || view.Released) yield break;
        foreach (string path in new[] { FramePath, InnerShadowPath, OuterShadowPath, GlowPath })
        {
            yield return EnsureAsset(path);
            if (view == null || view.Released) yield break;
            if (!assets.ContainsKey(path)) { view.Fail("卡牌资源缺失：" + path); yield break; }
        }
        yield return LoadVariant(request, request.Variant);
        if (view == null || view.Released) yield break;
        if (!TryGetVariantSprite(request, request.Variant, out var sprite))
        { view.Fail("卡牌资源缺失：" + VariantPath(request, request.Variant)); yield break; }
        try
        {
            if (!view.Ready) view.Initialize(assets[FramePath], assets[InnerShadowPath], assets[OuterShadowPath], assets[GlowPath], sprite.texture);
            ApplyVariant(request, request.Variant, sprite);
        }
        catch (Exception error) { view.Fail(error.Message); }
        view.MediaResolved = true;
    }
    private IEnumerator EnsureAsset(string path, bool retry = false)
    {
        if (retry) failed.Remove(path);
        if (!assets.ContainsKey(path) && !pending.Contains(path) && !failed.Contains(path))
        { pending.Add(path); StartCoroutine(LoadAsset(path)); }
        while (pending.Contains(path)) yield return null;
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

    private string VideoPath(RequestState request, int variant)
    {
        if (!request.Animated) return null;
        if (request.Side == BattleSide.Enemy)
        {
            string path = UICardVideoPlayer.VideoPathForEnemy(request.Id, variant);
            return enemyVideos.Contains(path) ? path : null;
        }
        return videos.Contains(request.Id) ? UICardVideoPlayer.VideoPathForCharacter(request.Id) : null;
    }
    private string PreferredVariantPath(RequestState request, int variant) =>
        request.Side == BattleSide.Enemy && VideoPath(request, variant) != null
            ? UICardVideoPlayer.PosterPathForEnemy(request.Id, variant)
            : request.Side == BattleSide.Ally && request.Poster && variant == 3
                ? UICardVideoPlayer.PosterPathForCharacter(request.Id) : VariantPath(request, variant);

    private bool TryGetVariantSprite(RequestState request, int variant, out Sprite sprite) =>
        assets.TryGetValue(PreferredVariantPath(request, variant), out sprite) ||
        assets.TryGetValue(VariantPath(request, variant), out sprite);

    private IEnumerator LoadVariant(RequestState request, int variant, bool retry = false)
    {
        yield return EnsureCatalog(request);
        if (request.View == null || request.View.Released) yield break;
        string preferred = PreferredVariantPath(request, variant);
        yield return EnsureAsset(preferred, retry);
        if (!assets.ContainsKey(preferred) && preferred != VariantPath(request, variant))
            yield return EnsureAsset(VariantPath(request, variant), retry);
    }
    private void ApplyVariant(RequestState request, int variant, Sprite sprite)
    {
        var view = request.View;
        string video = VideoPath(request, variant);
        request.Variant = variant;
        // Initial mode resolution must not restart an already preparing/playing video.
        if (video != null && view.Video != null && view.Video.enabled && view.Video.videoPath == video) return;
        view.SetStaticVariant(sprite.texture);
        if (video != null) view.PlayVideo(video);
    }

    /// <summary>Preload the preferred poster or static variant without changing the visible card.</summary>
    public IEnumerator PreloadVariant(UICardView view, int variant)
    {
        var request = Find(view);
        if (!isActiveAndEnabled || request == null || view == null || view.Released || variant <= 0) yield break;
        yield return LoadVariant(request, variant, true);
    }
    public bool HasVariant(UICardView view, int variant)
    {
        var request = Find(view);
        return isActiveAndEnabled && request != null && view != null && !view.Released && view.Ready &&
            view.isActiveAndEnabled && variant > 0 && TryGetVariantSprite(request, variant, out _);
    }
    internal Texture GetVariantTexture(UICardView view, int variant) => HasVariant(view, variant)
        ? VariantTexture(Find(view), variant) : null;
    private Texture VariantTexture(RequestState request, int variant) =>
        TryGetVariantSprite(request, variant, out var sprite) ? sprite.texture : null;
    /// <summary>Keep card layers; animated enemy requests prefer the exact form's video, otherwise use static art.</summary>
    public bool TrySetVariant(UICardView view, int variant)
    {
        if (!HasVariant(view, variant)) return false;
        var request = Find(view);
        if (request.Side == BattleSide.Ally) request.Animated = false;
        view.MediaResolved = true;
        TryGetVariantSprite(request, variant, out var sprite);
        ApplyVariant(request, variant, sprite);
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
        if (!enemyCatalogReady) enemyCatalogStarted = false;
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
        assets.Clear(); pending.Clear(); failed.Clear(); videos.Clear(); enemyVideos.Clear();
        catalogStarted = catalogReady = false;
        enemyCatalogStarted = enemyCatalogReady = false;
    }
}
