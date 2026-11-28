using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Shared turn counter and full-screen battle banners. Receives text/data; never advances battle or grants rewards.</summary>
[DisallowMultipleComponent]
public sealed class UIBattleFlowView : MonoBehaviour, IBattleVisualCompletion
{
    public TMP_FontAsset font;
    public Vector2 counterPosition = new Vector2(44, -28), counterSize = new Vector2(300, 48);
    public Vector2 waveCounterSize = new Vector2(240, 48), enemyCounterSize = new Vector2(280, 48);
    public float counterGap = 12;
    public Vector2 bannerPosition = new Vector2(0, 15), bannerSize = new Vector2(1120, 172);
    public float counterFontSize = 25, headingFontSize = 54, detailFontSize = 25;
    public float fadeIn = .18f, hold = .8f, fadeOut = .26f;
    [Range(0, 1)] public float dimOpacity = .5f;
    public Color textColor = new Color32(254, 254, 254, 255);
    public Color accentColor = new Color32(220, 194, 146, 255);
    public int sortingOrderOffset = 30;
    public bool ManualClock, Paused, English;
    private sealed class Request
    {
        internal BattleFlowNotice Notice;
        internal CancellationToken Cancellation;
        internal readonly TaskCompletionSource<bool> Done = new TaskCompletionSource<bool>();
    }
    private readonly Queue<Request> pending = new Queue<Request>();
    private Request active;
    private Func<string, bool, object[], string> localize;
    private CanvasGroup banner;
    private RectTransform root, dim, textRoot, counter;
    private TMP_Text heading, detail, roundLabel, waveLabel, enemyLabel;
    private bool coverScreen;
    private float elapsed;
    private int turn, roundLimit;
    private int wave = 1, waves = 1, enemies;
    private bool expired;
    private bool previousEnglish;
    public bool Ready => banner != null && localize != null;
    public bool HasPendingVisuals => active != null || pending.Count > 0;

    public void Bind(Func<string, bool, object[], string> text, bool fullScreen)
    {
        localize = text ?? throw new ArgumentNullException(nameof(text)); coverScreen = fullScreen;
        if (banner != null) return;
        if (font == null) throw new InvalidOperationException("战斗流程显示缺少公共字体。");
        root = (RectTransform)transform;
        var canvas = gameObject.AddComponent<Canvas>(); canvas.overrideSorting = true;
        canvas.sortingOrder = GetComponentInParent<Canvas>().rootCanvas.sortingOrder + sortingOrderOffset;
        gameObject.AddComponent<GraphicRaycaster>();
        counter = Mount("Turn counter", root, counterSize);
        counter.anchorMin = counter.anchorMax = new Vector2(0, 1); counter.pivot = new Vector2(0, 1);
        counter.anchoredPosition = counterPosition;
        var counterBack = counter.gameObject.AddComponent<Image>(); counterBack.color = new Color(0.22f, .18f, .14f, .52f); counterBack.raycastTarget = false;
        roundLabel = Label("Turn", counter, counterSize, counterFontSize); roundLabel.alignment = TextAlignmentOptions.MidlineLeft;
        roundLabel.margin = new Vector4(18, 0, 12, 0); counter.gameObject.SetActive(false);
        waveLabel = Counter("Wave counter", waveCounterSize, counterSize.x + counterGap);
        enemyLabel = Counter("Enemy counter", enemyCounterSize, counterSize.x + waveCounterSize.x + counterGap * 2);
        var layer = Mount("Announcements", root, Vector2.zero); Stretch(layer);
        banner = layer.gameObject.AddComponent<CanvasGroup>(); banner.interactable = false;
        dim = Mount("Full screen dim", layer, Vector2.zero);
        var shade = dim.gameObject.AddComponent<Image>(); shade.color = new Color(0, 0, 0, dimOpacity); shade.raycastTarget = true;
        textRoot = Mount("Banner content", layer, bannerSize); textRoot.anchoredPosition = bannerPosition;
        var band = textRoot.gameObject.AddComponent<Image>(); band.color = new Color(.20f, .16f, .12f, .48f); band.raycastTarget = false;
        foreach (float y in new[] { -bannerSize.y * .5f, bannerSize.y * .5f })
        {
            var line = Mount("Gold edge", textRoot, new Vector2(bannerSize.x, 1.5f)); line.anchoredPosition = new Vector2(0, y);
            var image = line.gameObject.AddComponent<Image>(); image.color = accentColor; image.raycastTarget = false;
        }
        heading = Label("Heading", textRoot, new Vector2(bannerSize.x - 60, 82), headingFontSize);
        heading.rectTransform.anchoredPosition = new Vector2(0, 20);
        detail = Label("Details", textRoot, new Vector2(bannerSize.x - 60, 38), detailFontSize);
        detail.rectTransform.anchoredPosition = new Vector2(0, -45); detail.color = accentColor;
        HideBanner();
    }
    private static RectTransform Mount(string name, RectTransform parent, Vector2 size)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.gameObject.layer = parent.gameObject.layer; rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f; rect.sizeDelta = size; return rect;
    }
    private TMP_Text Label(string name, RectTransform parent, Vector2 size, float fontSize)
    {
        var node = Mount(name, parent, size); var label = node.gameObject.AddComponent<TextMeshProUGUI>();
        label.font = font; label.fontSize = fontSize; label.enableAutoSizing = false; label.richText = false;
        label.color = textColor; label.alignment = TextAlignmentOptions.Center; label.raycastTarget = false;
        label.enableWordWrapping = false; label.overflowMode = TextOverflowModes.Overflow; return label;
    }
    private static void Stretch(RectTransform rect)
    { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
    public void SetTurn(int number, int maxRound = 0, bool limitReached = false)
    {
        turn = Math.Max(0, number);
        roundLimit = Math.Max(0, maxRound); expired = limitReached;
        if (counter == null) return;
        counter.gameObject.SetActive(turn > 0);
        if (turn > 0) roundLabel.text = roundLimit > 0
            ? Text("ui_battle_flow_remaining", expired ? 0 : Math.Max(0, roundLimit - turn + 1)) : Text("ui_battle_flow_turn", turn);
        RefreshCounters();
    }
    private string Text(string key, params object[] args) => localize(key, English, args);
    private TMP_Text Counter(string name, Vector2 size, float offset)
    {
        var node = Mount(name, root, size);
        node.anchorMin = node.anchorMax = node.pivot = new Vector2(0, 1);
        node.anchoredPosition = counterPosition + Vector2.right * offset;
        var background = node.gameObject.AddComponent<Image>();
        background.color = new Color(.22f, .18f, .14f, .52f); background.raycastTarget = false;
        var label = Label(name, node, size, counterFontSize); label.alignment = TextAlignmentOptions.MidlineLeft;
        label.margin = new Vector4(18, 0, 12, 0); node.gameObject.SetActive(false);
        return label;
    }
    public void SetEnemies(int currentWave, int waveCount, int remaining)
    {
        wave = Math.Max(1, currentWave); waves = Math.Max(wave, waveCount); enemies = Math.Max(0, remaining);
        RefreshCounters();
    }
    private void RefreshCounters()
    {
        if (waveLabel == null) return;
        waveLabel.transform.parent.gameObject.SetActive(turn > 0);
        enemyLabel.transform.parent.gameObject.SetActive(turn > 0);
        waveLabel.text = Text("ui_battle_flow_wave_progress", wave, waves);
        enemyLabel.text = Text("ui_battle_flow_enemies_remaining", enemies);
    }
    public Task PlayAsync(BattleFlowNotice notice, CancellationToken cancellation = default)
    {
        if (!Ready || !isActiveAndEnabled || cancellation.IsCancellationRequested || notice.Kind == BattleFlowNoticeKind.EnemyRefresh)
            return Task.CompletedTask;
        var request = new Request { Notice = notice, Cancellation = cancellation }; pending.Enqueue(request);
        Advance(0); return request.Done.Task;
    }
    private void RefreshText()
    {
        if (active == null) return;
        var n = active.Notice;
        string key = n.Kind switch {
            BattleFlowNoticeKind.BattleStart => "ui_battle_flow_start",
            BattleFlowNoticeKind.WaveStart => "ui_battle_flow_wave_start",
            BattleFlowNoticeKind.Victory => "ui_battle_flow_victory",
            _ => "ui_battle_flow_defeat" };
        heading.text = n.Kind == BattleFlowNoticeKind.WaveStart ? Text(key, n.Wave) : Text(key);
        detail.text = n.Kind == BattleFlowNoticeKind.WaveStart ? Text("ui_battle_flow_wave_progress", n.Wave, n.WaveCount)
            : n.Kind == BattleFlowNoticeKind.BattleStart ? Text("ui_battle_flow_ready") : Text("ui_battle_flow_turn", n.Turn);
    }
    public void Advance(float seconds)
    {
        if (!Ready || seconds < 0 || float.IsNaN(seconds) || float.IsInfinity(seconds)) return;
        if (active == null)
        {
            while (pending.Count > 0 && pending.Peek().Cancellation.IsCancellationRequested) pending.Dequeue().Done.TrySetCanceled();
            if (pending.Count == 0) return;
            active = pending.Dequeue(); elapsed = 0; RefreshText(); seconds = 0;
            banner.gameObject.SetActive(true); banner.blocksRaycasts = true;
        }
        if (active.Cancellation.IsCancellationRequested) { Finish(true); return; }
        if (!Paused) elapsed += seconds;
        if (elapsed >= Mathf.Max(0, fadeIn) + Mathf.Max(0, hold) + Mathf.Max(0, fadeOut)) { Finish(false); return; }
        var frame = BattleFlowBannerFrame.Sample(elapsed, Mathf.Max(0, fadeIn), Mathf.Max(0, hold), Mathf.Max(0, fadeOut));
        banner.alpha = frame.Alpha;
        textRoot.anchoredPosition = bannerPosition + Vector2.up * frame.Offset; textRoot.localScale = Vector3.one * frame.Scale;
        var shade = dim.GetComponent<Image>(); shade.color = new Color(0, 0, 0, Mathf.Clamp01(dimOpacity));
        if (coverScreen)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, Vector2.zero, null, out var low);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, new Vector2(Screen.width, Screen.height), null, out var high);
            dim.sizeDelta = high - low; dim.anchoredPosition = (high + low) * .5f;
        }
        else { dim.sizeDelta = root.rect.size; dim.anchoredPosition = Vector2.zero; }
    }
    private void Finish(bool cancelled)
    {
        var done = active; active = null; HideBanner();
        if (cancelled) done.Done.TrySetCanceled(); else done.Done.TrySetResult(true);
    }
    private void HideBanner() { if (banner != null) { banner.alpha = 0; banner.blocksRaycasts = false; banner.gameObject.SetActive(false); } }
    public void Clear()
    {
        var old = active; active = null; var queued = pending.ToArray(); pending.Clear(); HideBanner();
        old?.Done.TrySetCanceled(); foreach (var request in queued) request.Done.TrySetCanceled();
    }
    private void Update()
    {
        if (English != previousEnglish) { previousEnglish = English; SetTurn(turn, roundLimit, expired); RefreshCounters(); RefreshText(); }
        if (!ManualClock) Advance(BattlePause.DeltaTime);
    }
    private void OnDisable() { Clear(); SetTurn(0); }
}
