using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Explicit video, shield and hit-effect assembly for an isolated practice scene.</summary>
public sealed class CharacterVideoPreview : MonoBehaviour
{
    public Font controlsFont;
    public int characterId = 1009;
    private static readonly string[] Elements = { "None", "Pyro", "Hydro", "Electro", "Cryo", "Anemo", "Geo", "Dendro" };
    private static readonly string[] ElementNames = { "无", "火", "水", "雷", "冰", "风", "岩", "草" };
    private UICardFactory cardFactory;
    private readonly List<UICardView> requestedCards = new List<UICardView>();
    private Canvas previewCanvas;
    private Camera previewCamera;
    private readonly List<RawImage> surfaces = new List<RawImage>();
    private readonly List<UICardVideoPlayer> players = new List<UICardVideoPlayer>();
    private readonly List<UICardFrameView> cardViews = new List<UICardFrameView>();
    private BattleUiLayout layout;
    private BattleUiShields shields;
    private BattleUiHitEffects hits;
    private CanvasGroup cardsGroup;
    private int copies = 1, element;
    private bool started, initialized, paused, independent, showShield, showFrame = true, showGlow, dim;
    private float speed = 1, scale = 1, frameMs;
    private uint hitSeed;
    private string error;

    private IEnumerator Start()
    {
        started = true;
        error = null;
        if (characterId <= 0) { Fail("动态卡面预览需要有效角色编号。"); yield break; }
        cardFactory = GetComponent<UICardFactory>() ?? gameObject.AddComponent<UICardFactory>();
        yield return Build();
        if (error != null) yield break;
        initialized = true;
        ConfigurePlayers();
        yield return shields.Reload();
        yield return hits.Reload();
    }
    private IEnumerator Build()
    {
        var camera = new GameObject("PreviewCamera", typeof(Camera)).GetComponent<Camera>();
        previewCamera = camera;
        camera.transform.SetParent(transform, false); camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.13f, .15f, .15f); camera.cullingMask = 0;
        var canvas = new GameObject("VideoPreviewCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler)).GetComponent<Canvas>();
        previewCanvas = canvas;
        canvas.transform.SetParent(transform, false); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1440, 900); scaler.matchWidthOrHeight = .5f;
        var cards = Rect("Cards", canvas.transform, Vector2.zero, Vector2.zero);
        cardsGroup = cards.gameObject.AddComponent<CanvasGroup>();
        layout = gameObject.AddComponent<BattleUiLayout>(); layout.canvas = canvas;
        var bindings = new List<BattleUiCardBinding>();
        for (int i = 0; i < 10; i++)
        {
            bool primary = i < 2;
            var size = primary ? new Vector2(239, 403) : new Vector2(105, 105 * 806f / 478);
            var position = primary ? new Vector2(-470 + i * 340, -65)
                : new Vector2(190 + (i - 2) % 4 * 125, 40 - (i - 2) / 4 * 225);
            var root = Rect(i == 0 ? "StaticReference" : "VideoCard" + i, cards, size, position);
            root.sizeDelta = new Vector2(size.x, size.x / UICardFactory.Aspect);
            var request = cardFactory.Request(root, BattleSide.Ally, characterId, false, true);
            requestedCards.Add(request);
            while (!request.Ready && request.Error == null) yield return null;
            if (request.Error != null) { Fail(request.Error); yield break; }
            var view = request.Appearance;
            view.SetFrameVisible(showFrame); cardViews.Add(view);
            RawImage surface = view.Surface;
            if (i > 0)
            {
                surfaces.Add(surface);
                // Decoder lifetime is independent of individual card visibility, so shared playback remains valid.
                var source = new GameObject("VideoSource" + i); source.transform.SetParent(transform, false);
                var player = source.AddComponent<UICardVideoPlayer>(); player.playOnStart = false;
                player.videoPath = UICardVideoPlayer.VideoPathForCharacter(characterId); players.Add(player);
                root.gameObject.SetActive(i == 1);
            }
            bindings.Add(new BattleUiCardBinding { side = BattleSide.Ally, slot = i + 1, root = root, art = surface.rectTransform,
                hitsBehind = view.BackEffects, hitsFront = view.FrontEffects });
        }
        layout.cards = bindings.ToArray();
        shields = gameObject.AddComponent<BattleUiShields>(); shields.loadOnStart = false; shields.layout = layout;
        hits = gameObject.AddComponent<BattleUiHitEffects>(); hits.loadOnStart = false; hits.layout = layout;
    }
    private void ConfigurePlayers()
    {
        foreach (var player in players) player.Unload();
        for (int i = 0; i < players.Count; i++)
        {
            layout.cards[i + 1].root.gameObject.SetActive(i < copies);
            players[i].paused = paused; players[i].speed = speed;
            if (independent && i < copies)
            { players[i].targets = new[] { surfaces[i] }; players[i].Load(); }
        }
        if (!independent) { players[0].targets = surfaces.ToArray(); players[0].Load(); }
    }
    private void Update()
    {
        frameMs = Mathf.Lerp(frameMs, Time.unscaledDeltaTime * 1000, .05f);
        if (!initialized) return;
        foreach (var player in players) { player.paused = paused; player.speed = speed; }
        for (int i = 0; i < layout.cards.Length; i++)
        {
            layout.cards[i].root.localScale = Vector3.one * (i < 2 ? scale : 1);
            cardViews[i].SetFrameVisible(showFrame);
            cardViews[i].SetHighlightVisible(showGlow);
            shields.SetShield(BattleSide.Ally, i + 1, showShield, Elements[element]);
        }
        cardsGroup.alpha = dim ? .4f : 1;
    }
    private void Hit(HitEffectWeapon weapon)
    {
        var targets = new List<BattleUiCardBinding>();
        foreach (var card in layout.Cards) if (card.root.gameObject.activeInHierarchy) targets.Add(card);
        hits.PlayHit(weapon, true, element == 0 ? "Physical" : Elements[element], null, targets, 0, 1, ++hitSeed,
            HitEffectTextureStyle.PlaybackMode.PerTarget);
    }
    private void Fail(string message) { error = message; Debug.LogError("CharacterVideoPreview: " + message, this); }
    private void OnGUI()
    {
        var matrix = GUI.matrix; var font = GUI.skin.font; bool wasEnabled = GUI.enabled;
        GUI.matrix = Matrix4x4.Scale(new Vector3(Screen.width / 1440f, Screen.height / 900f, 1));
        if (controlsFont != null) GUI.skin.font = controlsFont;
        var label = new GUIStyle(GUI.skin.label) { fontSize = 19 };
        var button = new GUIStyle(GUI.skin.button) { fontSize = 18 };
        var toggle = new GUIStyle(GUI.skin.toggle) { fontSize = 18 };
        GUILayout.BeginArea(new Rect(20, 12, 1400, 250), GUI.skin.box);
        GUILayout.Label("安柏 60 fps 无框视频：左侧静态首帧，中央视频，右侧多卡对照。", label);
        GUI.enabled = initialized && isActiveAndEnabled;
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(paused ? "继续" : "暂停 / 定格", button)) paused = !paused;
        if (GUILayout.Button("下一帧", button)) { paused = true; foreach (var p in players) p.Step(); }
        if (GUILayout.Button("从头播放", button)) { paused = false; foreach (var p in players) p.Restart(); }
        if (GUILayout.Button("重新加载视频", button)) ConfigurePlayers();
        GUILayout.Label("速度 " + speed.ToString("F2"), label, GUILayout.Width(110));
        speed = GUILayout.HorizontalSlider(speed, .25f, 2, GUILayout.Width(140));
        GUILayout.Label("缩放 " + scale.ToString("F2"), label, GUILayout.Width(110));
        scale = GUILayout.HorizontalSlider(scale, .65f, 1.3f, GUILayout.Width(140));
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        foreach (int n in new[] { 1, 4, 9 })
            if (GUILayout.Button(n + " 张视频卡", button) && copies != n) { copies = n; ConfigurePlayers(); }
        bool next = GUILayout.Toggle(independent, "每张卡独立解码（性能对照）", toggle);
        if (next != independent) { independent = next; ConfigurePlayers(); }
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        showFrame = GUILayout.Toggle(showFrame, "卡框", toggle); showGlow = GUILayout.Toggle(showGlow, "选中高光", toggle);
        dim = GUILayout.Toggle(dim, "半透明模拟", toggle); showShield = GUILayout.Toggle(showShield, "护盾", toggle);
        element = GUILayout.SelectionGrid(element, ElementNames, 8, button);
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        GUI.enabled = initialized && shields.Ready && isActiveAndEnabled;
        if (GUILayout.Button("护盾扫光", button))
        {
            showShield = true;
            foreach (var card in layout.Cards) if (card.root.gameObject.activeInHierarchy)
            { shields.SetShield(card.side, card.slot, true, Elements[element]); shields.PlaySweep(card.side, card.slot); }
        }
        GUI.enabled = initialized && hits.Ready && isActiveAndEnabled;
        if (GUILayout.Button("单手剑命中", button)) Hit(HitEffectWeapon.Sword);
        if (GUILayout.Button("双手剑重击", button)) Hit(HitEffectWeapon.Claymore);
        if (GUILayout.Button("法器命中", button)) Hit(HitEffectWeapon.Catalyst);
        if (GUILayout.Button("清除命中特效", button)) hits.Clear();
        GUILayout.EndHorizontal(); GUI.enabled = true;
        string message = error ?? shields?.Error ?? hits?.Error;
        foreach (var p in players) if (p.Error != null) { message = p.Error; break; }
        GUILayout.Label(message ?? (initialized ? "" : "正在加载运行时资源…"), label);
        GUILayout.EndArea();
        GUILayout.BeginArea(new Rect(25, 760, 1390, 130));
        if (initialized)
        {
            var p = players[0]; long bytes = 0; int ready = 0;
            foreach (var source in players) { bytes += source.TextureBytes; if (source.Ready) ready++; }
            GUILayout.Label(p.Ready ? $"H.264 · {p.FrameRate:F0} fps · {p.FrameCount} 帧 / {p.Duration:F2} 秒 · 当前 {p.Frame + 1} 帧 · 循环 {p.Loops} 次" : "视频正在准备首帧…", label);
            GUILayout.Label($"{copies} 张视频卡 · {ready} 个解码器就绪 · 输出纹理 {bytes / 1048576f:F2} MiB（不含解码缓存）· 屏幕帧时 {frameMs:F1} ms", label);
        }
        GUILayout.Label("静态背景随卡面一起播放；不加载骨骼文件。可观察循环衔接和护盾 / 命中叠加。", label);
        GUILayout.EndArea(); GUI.enabled = wasEnabled; GUI.skin.font = font; GUI.matrix = matrix;
    }
    private static RectTransform Rect(string name, Transform parent, Vector2 size, Vector2 position)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.sizeDelta = size; rect.anchoredPosition = position; return rect;
    }
    private void OnDisable()
    {
        StopAllCoroutines();
        if (!initialized)
        {
            foreach (var request in requestedCards) if (cardFactory != null) cardFactory.Release(request);
            foreach (var player in players) if (player != null) { player.Unload(); Destroy(player.gameObject); }
            requestedCards.Clear(); players.Clear(); cardViews.Clear(); surfaces.Clear();
            if (previewCanvas != null) Destroy(previewCanvas.gameObject);
            if (previewCamera != null) Destroy(previewCamera.gameObject);
            if (layout != null) Destroy(layout);
        }
        if (shields != null) shields.enabled = false;
        if (hits != null) hits.enabled = false;
        foreach (var player in players) player.enabled = false;
    }
    private void OnEnable()
    {
        if (shields != null) shields.enabled = true;
        if (hits != null) hits.enabled = true;
        foreach (var player in players) player.enabled = true;
        if (started && !initialized) StartCoroutine(Start());
        else if (initialized) StartCoroutine(ReloadEffects());
    }
    private IEnumerator ReloadEffects()
    {
        if (!shields.Ready) yield return shields.Reload();
        if (!hits.Ready) yield return hits.Reload();
    }
    private void OnDestroy()
    {
        foreach (var player in players) if (player != null) player.Unload();
        foreach (var view in cardViews) if (view != null) view.Clear();
    }
}
