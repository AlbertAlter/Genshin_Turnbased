using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Shared card requests. Scenes supply a mount, identity and animation preference, never skin layers.</summary>
[DisallowMultipleComponent]
public sealed class UICardFactory : MonoBehaviour
{
    public const string FramePath = "art_assets/Character_Bare/Frame/01.png";
    public const string DefaultCardBackPath = UICardBack.DefaultPath;
    public const string InnerShadowPath = "art_assets/UI/BattleField1/UI_components/card/CardInnerShadow.png";
    public const string OuterShadowPath = "art_assets/UI/BattleField1/UI_components/card/CardShadow.png";
    public const string GlowPath = "art_assets/UI/BattleField1/UI_components/card/CardSelectionBorder.png";
    public const string WeaponArtFolder = "art_assets/Weapon_Bare/";
    public const float Aspect = 420f / 720f;
    public string[] characterFramePaths = { FramePath, "art_assets/Character_Bare/Frame/02.png", "art_assets/Character_Bare/Frame/03.png" };
    private ICardAppearanceSource appearanceSource;
    public string SelectedCardBackPath => appearanceSource?.SelectedCardBackPath ?? "";
    public int GetWeaponFrameLevel(int weaponId) => appearanceSource?.GetWeaponFrameLevel(weaponId) ?? 0;
    /// <summary>Explicit application binding. No save reads or global cosmetic service installation inside a scene component.</summary>
    public void BindAppearance(ICardAppearanceSource source)
    {
        if (source != null && !source.IsReady) throw new InvalidOperationException("绑定外观前请先成功读取外观存档。");
        if (appearanceSource != null) appearanceSource.Changed -= AppearanceChanged;
        appearanceSource = source;
        if (appearanceSource != null && isActiveAndEnabled) appearanceSource.Changed += AppearanceChanged;
        if (isActiveAndEnabled) AppearanceChanged();
    }
    private readonly Dictionary<string, Sprite> assets = new Dictionary<string, Sprite>();
    private readonly HashSet<string> pending = new HashSet<string>(), failed = new HashSet<string>();
    private readonly HashSet<string> videos = new HashSet<string>(StringComparer.Ordinal);
    private readonly HashSet<string> enemyVideos = new HashSet<string>(StringComparer.Ordinal);
    private sealed class RequestState
    {
        public UICardView View; public BattleSide Side; public int Id, Variant; public bool Animated, Poster, Weapon, Shadows, Active = true;
        public string FrameAsset; public int FrameRequest;
        public readonly Dictionary<int, int> CharacterVideos = new Dictionary<int, int> { { 1, 1 } };
    }
    private readonly List<RequestState> cards = new List<RequestState>();
    private bool catalogStarted, catalogReady;
    private bool enemyCatalogStarted, enemyCatalogReady;

    public UICardView Request(RectTransform mount, BattleSide side, int id, bool animated = true, bool useVideoPoster = false,
        bool showShadows = true)
        => RequestCard(mount, side, id, animated, useVideoPoster, false, showShadows);

    public UICardView RequestWeapon(RectTransform mount, int weaponId)
        => RequestCard(mount, BattleSide.Ally, weaponId, false, false, true, true);

    private UICardView RequestCard(RectTransform mount, BattleSide side, int id, bool animated, bool useVideoPoster,
        bool weapon, bool shadows)
    {
        if (mount == null || id <= 0) throw new ArgumentException("卡牌请求需要有效的挂载位置和身份编号。");
        if (!isActiveAndEnabled) throw new InvalidOperationException("卡牌服务未启用。");
        var root = new GameObject("CardView", typeof(RectTransform));
        var rect = root.GetComponent<RectTransform>(); rect.SetParent(mount, false); rect.SetAsFirstSibling();
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        var view = root.AddComponent<UICardView>();
        var request = new RequestState { View = view, Side = side, Id = id,
            Variant = 1, Animated = animated, Poster = useVideoPoster, Weapon = weapon, Shadows = shadows };
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
            yield return UICardVideoCatalog.LoadCharacters(videos,
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
            if (!request.Shadows && (path == InnerShadowPath || path == OuterShadowPath)) continue;
            yield return EnsureAsset(path);
            if (view == null || view.Released) yield break;
            if (!assets.ContainsKey(path)) { view.Fail("卡牌资源缺失：" + path); yield break; }
        }
        yield return LoadVariant(request, request.Variant);
        if (view == null || view.Released) yield break;
        if (!TryGetVariantSprite(request, request.Variant, out var sprite))
        { view.Fail("卡牌资源缺失：" + VariantPath(request, request.Variant)); yield break; }
        string selectedFrame = SelectedFrame(request);
        yield return EnsureAsset(selectedFrame);
        if (view == null || view.Released) yield break;
        if (!assets.ContainsKey(selectedFrame)) selectedFrame = FramePath;
        try
        {
            if (!view.Ready)
                view.Initialize(assets[selectedFrame], request.Shadows ? assets[InnerShadowPath] : null,
                    request.Shadows ? assets[OuterShadowPath] : null, assets[GlowPath], sprite.texture);
            request.FrameAsset = selectedFrame;
            ApplyVariant(request, request.Variant, sprite);
        }
        catch (Exception error) { view.Fail(error.Message); }
        view.MediaResolved = true;
        yield return RefreshFrame(request);
        yield return RefreshBack(request);
    }
    private string SelectedFrame(RequestState request)
    {
        return FramePathAtLevel(FrameLevel(request), characterFramePaths);
    }
    private int FrameLevel(RequestState request) => request.Weapon ? GetWeaponFrameLevel(request.Id) :
        request.Side == BattleSide.Ally ? appearanceSource?.GetCharacterFrameLevel(request.Id) ?? 0 : 0;
    public static string FramePathAtLevel(int level, IReadOnlyList<string> paths)
        => paths == null || paths.Count == 0 ? FramePath : paths[Math.Min(Math.Max(0, level), paths.Count - 1)] ?? FramePath;
    private void AppearanceChanged()
    {
        foreach (var request in cards.ToArray())
            if (request.View != null && request.View.Ready && !request.View.Released)
            { StartCoroutine(RefreshFrame(request)); StartCoroutine(RefreshBack(request)); }
    }
    private IEnumerator RefreshBack(RequestState request)
    {
        string desired = string.IsNullOrEmpty(SelectedCardBackPath) ? DefaultCardBackPath : SelectedCardBackPath;
        yield return EnsureAsset(desired);
        if (!assets.ContainsKey(desired) && desired != DefaultCardBackPath)
        { desired = DefaultCardBackPath; yield return EnsureAsset(desired); }
        var view = request.View;
        if (view == null || view.Released || !view.Ready) yield break;
        string current = string.IsNullOrEmpty(SelectedCardBackPath) ? DefaultCardBackPath : SelectedCardBackPath;
        if (desired != current && !failed.Contains(current)) { yield return RefreshBack(request); yield break; }
        if (assets.TryGetValue(desired, out var sprite)) view.SetCardBack(sprite);
    }
    private IEnumerator RefreshFrame(RequestState request)
    {
        int generation = ++request.FrameRequest;
        string desired = SelectedFrame(request);
        yield return EnsureAsset(desired);
        var view = request.View;
        if (view == null || view.Released || generation != request.FrameRequest || !view.Ready) yield break;
        // A selection may have changed while its image was loading. Never commit a late frame over the new one.
        if (desired != SelectedFrame(request)) { yield return RefreshFrame(request); yield break; }
        view.FrameLevel = FrameLevel(request);
        if (!assets.TryGetValue(desired, out var frame)) yield break; // Keep the current frame when a future tier has no published art.
        if (request.FrameAsset == desired) yield break;
        try { view.Appearance.ReplaceFrame(frame); request.FrameAsset = desired; }
        catch (Exception error) { Debug.LogWarning("保留当前卡框：" + error.Message, this); }
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
    private static string VariantPath(RequestState request, int variant) => request.Weapon
        ? $"{WeaponArtFolder}{request.Id}_{variant:D2}.png"
        : $"art_assets/{(request.Side == BattleSide.Ally ? "Character_Bare" : "Enemy")}/{request.Id}_{variant:D2}.png";

    private string VideoPath(RequestState request, int variant)
    {
        if (!request.Animated) return null;
        if (request.Side == BattleSide.Enemy)
        {
            string path = UICardVideoPlayer.VideoPathForEnemy(request.Id, variant);
            return enemyVideos.Contains(path) ? path : null;
        }
        if (!request.CharacterVideos.TryGetValue(variant, out int videoVariant)) return null;
        string characterPath = UICardVideoPlayer.VideoPathForCharacter(request.Id, videoVariant);
        return videos.Contains(characterPath) ? characterPath : null;
    }
    private string PreferredVariantPath(RequestState request, int variant) =>
        request.Side == BattleSide.Enemy && VideoPath(request, variant) != null
            ? UICardVideoPlayer.PosterPathForEnemy(request.Id, variant)
            : request.Side == BattleSide.Ally && request.Poster && variant == 1
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

    /// <summary>Register an explicit character form's movie; unmapped variants remain static.</summary>
    public bool MapCharacterVideo(UICardView view, int variant, int videoVariant)
    {
        var request = Find(view);
        if (request == null || request.Weapon || request.Side != BattleSide.Ally || view == null || view.Released ||
            variant <= 0 || videoVariant <= 0) return false;
        request.CharacterVideos[variant] = videoVariant;
        return true;
    }
    /// <summary>Keep card layers; animated enemy requests prefer the exact form's video, otherwise use static art.</summary>
    public bool TrySetVariant(UICardView view, int variant)
    {
        if (!HasVariant(view, variant)) return false;
        var request = Find(view);
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
        if (appearanceSource != null) appearanceSource.Changed -= AppearanceChanged;
        StopAllCoroutines();
        pending.Clear(); if (!catalogReady) catalogStarted = false;
        if (!enemyCatalogReady) enemyCatalogStarted = false;
        foreach (var request in cards)
            if (request.View != null)
            { request.Active = request.View.gameObject.activeSelf; request.View.gameObject.SetActive(false); }
    }
    private void OnEnable()
    {
        if (appearanceSource != null) { appearanceSource.Changed -= AppearanceChanged; appearanceSource.Changed += AppearanceChanged; }
        foreach (var request in cards)
            if (request.View != null && !request.View.Released)
            {
                request.View.gameObject.SetActive(request.Active);
                if (!request.View.Ready || !request.View.MediaResolved) StartCoroutine(Prepare(request));
                else { StartCoroutine(RefreshFrame(request)); StartCoroutine(RefreshBack(request)); }
            }
    }
    private void OnDestroy()
    {
        if (appearanceSource != null) appearanceSource.Changed -= AppearanceChanged;
        StopAllCoroutines();
        foreach (var request in cards.ToArray()) Release(request.View);
        foreach (var sprite in assets.Values)
            if (sprite != null) { Destroy(sprite.texture); Destroy(sprite); }
        assets.Clear(); pending.Clear(); failed.Clear(); videos.Clear(); enemyVideos.Clear();
        catalogStarted = catalogReady = false;
        enemyCatalogStarted = enemyCatalogReady = false;
    }
}
