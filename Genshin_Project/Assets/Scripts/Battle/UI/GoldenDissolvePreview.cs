using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Combined entrance/exit preview: four individually animated allies and five static enemies. No combat initialization.</summary>
public sealed class GoldenDissolvePreview : MonoBehaviour
{
    public Font controlsFont;
    public int[] enemyIds = { 2001, 2002, 2003, 2004, 2005 };
    public int[] characterIds = { 1009, 1010, 1011, 1012 };
    public bool autoReplay = true;
    public bool newSeedEachPlay = true;
    public int fixedSeed = 12345;
    private sealed class Card
    {
        public bool Ally;
        public UICardFrameView View;
        public UICardEntrance Entrance;
        public UIGoldenDissolveCard Exit;
    }
    private readonly List<Card> cards = new List<Card>();
    private UICardFactory cardFactory;
    private readonly List<UICardView> requestedCards = new List<UICardView>();
    private Canvas previewCanvas;
    private UIGoldenDissolveStyle exitStyle;
    private UICardEntranceStyle entranceStyle;
    private readonly List<UICardVideoPlayer> videos = new List<UICardVideoPlayer>();
    private Camera previewCamera;
    private Coroutine sequence;
    private bool started, built, ready, paused;
    private int selection;
    private uint playIndex;
    private string error, phase = "加载中";
    private float frameMs;

    private IEnumerator Start()
    {
        started = true;
        if (!ReadSettings()) yield break;
        if (characterIds == null || characterIds.Length != 4 || Array.Exists(characterIds, id => id <= 0))
        { Fail("入退场预览需要四个有效角色编号。"); yield break; }
        if (enemyIds == null || enemyIds.Length != 5 || Array.Exists(enemyIds, id => id <= 0))
        { Fail("入退场预览需要五个有效敌人编号。"); yield break; }
        cardFactory = GetComponent<UICardFactory>() ?? gameObject.AddComponent<UICardFactory>();
        yield return Build();
        if (error != null) yield break;
        built = true;
        yield return WaitForVideo();
    }
    private IEnumerator WaitForVideo()
    {
        foreach (var video in videos)
        {
            while (!video.Ready && video.Error == null) yield return null;
            if (video.Error != null) { Fail(video.Error); yield break; }
        }
        ready = true;
        if (autoReplay) BeginSequence(); else EntranceOnly();
    }
    private bool ReadSettings()
    {
        try { exitStyle = UIGoldenDissolveStyle.Load(); entranceStyle = UICardEntranceStyle.Load(); error = null; return true; }
        catch (Exception ex) { Fail(ex.Message); return false; }
    }
    private IEnumerator Build()
    {
        previewCamera = new GameObject("PreviewCamera", typeof(Camera)).GetComponent<Camera>();
        previewCamera.transform.SetParent(transform, false); previewCamera.clearFlags = CameraClearFlags.SolidColor;
        previewCamera.backgroundColor = exitStyle.PreviewBackgroundColor; previewCamera.cullingMask = 0;
        var canvas = new GameObject("EntryExitCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler)).GetComponent<Canvas>();
        previewCanvas = canvas;
        canvas.transform.SetParent(transform, false); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
        for (int i = 0; i < 9; i++)
        {
            bool ally = i >= 5; int slot = ally ? i - 5 : i;
            var layout = Rect((ally ? "Ally" : "Enemy") + (slot + 1), canvas.transform);
            layout.sizeDelta = new Vector2(160, 160 / UICardFactory.Aspect);
            layout.anchoredPosition = new Vector2(ally ? 210 + slot * 170 : -810 + slot * 170, slot % 2 == 0 ? -130 : 80);
            var motion = Rect("EntranceMotion", layout); motion.sizeDelta = layout.sizeDelta;
            var opacity = motion.gameObject.AddComponent<CanvasGroup>();
            var request = cardFactory.Request(motion, ally ? BattleSide.Ally : BattleSide.Enemy,
                ally ? characterIds[slot] : enemyIds[slot]);
            requestedCards.Add(request);
            while ((!request.Ready || !request.MediaResolved) && request.Error == null) yield return null;
            if (request.Error != null) { Fail(request.Error); yield break; }
            var view = request.Appearance;
            var entrance = motion.gameObject.AddComponent<UICardEntrance>(); entrance.motionRoot = motion; entrance.opacity = opacity;
            var exit = motion.gameObject.AddComponent<UIGoldenDissolveCard>();
            exit.cardRoot = motion; exit.framedView = view; exit.outerShadows = new[] { view.OuterShadow };
            exit.keepSourceAnimated = ally;
            cards.Add(new Card { Ally = ally, View = view, Entrance = entrance, Exit = exit });
            if (request.Video != null) videos.Add(request.Video);
            opacity.alpha = 0;
        }
    }
    private static RectTransform Rect(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f); return rect;
    }
    private bool Selected(Card card) => selection == 0 || (selection == 1 ? card.Ally : !card.Ally);
    private void RestoreCards()
    {
        paused = false;
        foreach (var card in cards)
        { card.Entrance.Restore(); card.Exit.ResetEffect(); card.Entrance.opacity.alpha = 1; }
        foreach (var video in videos) { video.paused = false; video.keepDecodingWhenHidden = false; }
    }
    private void Cancel()
    {
        if (sequence != null) StopCoroutine(sequence);
        sequence = null; RestoreCards(); phase = "正常显示";
    }
    private void StartEntrance()
    {
        RestoreCards(); phase = "入场"; int allies = 0, enemies = 0;
        foreach (var card in cards)
            if (Selected(card))
            {
                int index = card.Ally ? allies++ : enemies++;
                card.Entrance.Play(entranceStyle, card.Ally ? 1 : -1, index * entranceStyle.Stagger);
            }
    }
    private bool StartExit()
    {
        foreach (var card in cards) card.Entrance.Restore();
        phase = "动态退场"; foreach (var video in videos) video.keepDecodingWhenHidden = true;
        int seed = newSeedEachPlay ? unchecked((int)GoldenDissolveMath.Mix((uint)fixedSeed ^ ++playIndex)) : fixedSeed;
        Canvas.ForceUpdateCanvases();
        for (int index = 0; index < cards.Count; index++)
        {
            var card = cards[index];
            int cardSeed = unchecked((int)GoldenDissolveMath.Mix((uint)seed ^ (uint)(index + 1)));
            if (Selected(card) && !card.Exit.Play(exitStyle, cardSeed))
            { RestoreCards(); Fail("退场未能开始，请查看 Console。"); return false; }
        }
        return true;
    }
    private bool Entering() { foreach (var c in cards) if (c.Entrance.IsPlaying) return true; return false; }
    private bool Exiting() { foreach (var c in cards) if (c.Exit.IsPlaying) return true; return false; }
    private IEnumerator Delay(float seconds)
    {
        float elapsed = 0;
        while (elapsed < seconds)
        { if (!paused) elapsed += entranceStyle.Unscaled ? Time.unscaledDeltaTime : Time.deltaTime; yield return null; }
    }
    private IEnumerator RunSequence()
    {
        do
        {
            StartEntrance();
            while (Entering()) yield return null;
            phase = "停留"; yield return Delay(entranceStyle.PreviewHold);
            if (!StartExit()) yield break;
            while (Exiting()) yield return null;
            phase = "退场完成"; foreach (var video in videos) video.keepDecodingWhenHidden = false;
            if (!autoReplay) break;
            yield return Delay(exitStyle.PreviewLoopPauseSeconds);
        } while (autoReplay);
        sequence = null;
    }
    private void BeginSequence() { if (!ready) return; Cancel(); sequence = StartCoroutine(RunSequence()); }
    private void EntranceOnly() { if (!ready) return; Cancel(); StartEntrance(); }
    private void ExitOnly() { if (!ready) return; Cancel(); StartExit(); }
    private void Update()
    {
        frameMs = Mathf.Lerp(frameMs, Time.unscaledDeltaTime * 1000, .05f);
        if (!ready) return;
        if (Input.GetKeyDown(KeyCode.R)) BeginSequence();
        if (Input.GetKeyDown(KeyCode.Space)) paused = !paused;
        foreach (var video in videos) video.paused = paused;
        foreach (var card in cards) { card.Entrance.Paused = paused; card.Exit.Paused = paused; }
        if (phase == "动态退场" && !Exiting()) { phase = "退场完成"; foreach (var video in videos) video.keepDecodingWhenHidden = false; }
        if (sequence == null && phase == "入场" && !Entering()) phase = "正常显示";
    }
    private void OnGUI()
    {
        var matrix = GUI.matrix; var font = GUI.skin.font; bool enabled = GUI.enabled;
        GUI.matrix = Matrix4x4.Scale(new Vector3(Screen.width / 1920f, Screen.height / 1080f, 1));
        if (controlsFont != null) GUI.skin.font = controlsFont;
        var label = new GUIStyle(GUI.skin.label) { fontSize = 24 };
        var button = new GUIStyle(GUI.skin.button) { fontSize = 22 };
        var toggle = new GUIStyle(GUI.skin.toggle) { fontSize = 22 };
        GUILayout.BeginArea(new Rect(24, 20, 1872, 210), GUI.skin.box);
        GUILayout.Label("卡牌入场 / 退场：左侧 5 个敌人，右侧 4 张角色各自动画；退场时视频继续播放。", label);
        GUI.enabled = ready && isActiveAndEnabled;
        GUILayout.BeginHorizontal();
        int next = GUILayout.SelectionGrid(selection, new[] { "双方", "只看我方", "只看敌方" }, 3, button, GUILayout.Width(510));
        if (next != selection) { Cancel(); selection = next; }
        if (GUILayout.Button("只播放入场", button)) EntranceOnly();
        if (GUILayout.Button("只播放退场", button)) ExitOnly();
        if (GUILayout.Button("入场 → 停留 → 退场 [R]", button)) BeginSequence();
        if (GUILayout.Button("恢复", button)) Cancel();
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(paused ? "继续 [Space]" : "暂停 [Space]", button)) paused = !paused;
        autoReplay = GUILayout.Toggle(autoReplay, "连续模式循环", toggle);
        newSeedEachPlay = GUILayout.Toggle(newSeedEachPlay, "更换消散种子", toggle);
        if (GUILayout.Button("重读运行时动画参数", button))
        { Cancel(); if (ReadSettings()) { previewCamera.backgroundColor = exitStyle.PreviewBackgroundColor; BeginSequence(); } }
        GUILayout.EndHorizontal(); GUI.enabled = true;
        GUILayout.Label(error ?? VideoError() ?? (ready ? "阶段：" + phase : "加载素材 / 准备视频首帧…"), label);
        GUILayout.EndArea();
        if (ready)
        {
            GUILayout.BeginArea(new Rect(30, 925, 1850, 125));
            GUILayout.Label($"入场 {entranceStyle.Duration:F2}s · 间隔 {entranceStyle.Stagger:F2}s · 偏移 {entranceStyle.OffsetWidthRatio:P0} 卡宽 · 缩放 {entranceStyle.StartScale:F2} → 1 · 退场 {exitStyle.TotalDuration:F2}s（含碎屑）", label);
            GUILayout.Label(videos.Count > 0
                ? $"{videos.Count} 个独立视频源，首张当前 {videos[0].Frame + 1}/{videos[0].FrameCount} 帧；屏幕帧时 {frameMs:F1} ms。场景位置不由动画参数表重置。"
                : "当前角色使用公共静态卡面。", label);
            GUILayout.EndArea();
        }
        GUI.enabled = enabled; GUI.skin.font = font; GUI.matrix = matrix;
    }
    private string VideoError() { foreach (var video in videos) if (video.Error != null) return video.Error; return null; }
    private void Fail(string message) { error = message; Debug.LogError("EntryExitPreview: " + message, this); }
    private void OnDisable()
    {
        StopAllCoroutines(); sequence = null; ready = false; RestoreCards();
        foreach (var video in videos) video.enabled = false;
        if (!built)
        {
            foreach (var request in requestedCards) if (cardFactory != null) cardFactory.Release(request);
            requestedCards.Clear(); cards.Clear(); videos.Clear();
            if (previewCanvas != null) Destroy(previewCanvas.gameObject);
            if (previewCamera != null) Destroy(previewCamera.gameObject);
        }
    }
    private void OnEnable()
    {
        if (!started) return;
        if (built) { foreach (var video in videos) video.enabled = true; StartCoroutine(WaitForVideo()); }
        else StartCoroutine(Start());
    }
    private void OnDestroy()
    {
        foreach (var video in videos) if (video != null) video.Unload();
        foreach (var card in cards) { if (card.Exit != null) card.Exit.ResetEffect(); if (card.View != null) card.View.Clear(); }
    }
}
