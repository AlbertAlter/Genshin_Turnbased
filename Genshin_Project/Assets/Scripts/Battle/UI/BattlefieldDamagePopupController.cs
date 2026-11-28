using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

/// <summary>展示正式伤害、治疗、元素反应与状态结果；不预测、重新结算或阻塞战斗。</summary>
[DisallowMultipleComponent]
public sealed class BattlefieldDamagePopupController : MonoBehaviour, IBattlePresentationView, IBattleVisualCompletion
{
    public BattleUiLayout layout;
    public TMP_FontAsset numberFont;
    public TMP_FontAsset reactionFont;
    public UIStatusNamePopup statusNames;
    public bool english;
    private sealed class Pending
    {
        public BattleSide Side;
        public int Slot;
        public string Text;
        public Vector2 Position;
        public BattlefieldPopupStyle Style;
        public long PlaybackID;
        public float FontMultiplier = 1;
        public Color? BaseColor;
        public TMP_FontAsset Font;
        public bool Reaction;
    }
    private sealed class Popup
    {
        public TMP_Text Text;
        public Vector2 Origin;
        public float Height, Age;
        public BattlefieldPopupStyle Style;
        public BattleSide Side;
        public int Slot;
        public long PlaybackID;
        public Color BaseColor;
        public bool Reaction;
    }

    private readonly Dictionary<(BattleSide, int), RectTransform> cards = new Dictionary<(BattleSide, int), RectTransform>();
    private readonly List<Popup> active = new List<Popup>();
    public bool HasPendingVisuals => active.Count > 0;
    private readonly Stack<TMP_Text> pool = new Stack<TMP_Text>();
    private BattlefieldDamagePopupStyle style;
    private ElementColorPalette elementColors;
    private bool unknownElementReported;
    private bool unknownReactionReported;
    private RectTransform layer;
    private Canvas rootCanvas;
    private RectTransform presentationParent;
    private TMP_FontAsset font;
    private bool missingAnchorReported;
    public bool AutomaticClock { get; set; } = true;
    public bool Ready => style != null && rootCanvas != null && numberFont != null;
    public float PresentationLifetime => style?.Lifetime ?? 0;

    /// <summary>Explicit display host for tools without a BattleUiLayout; numbers stay in its zoom/clipping space.</summary>
    public void BindPresentationCanvas(Canvas target, RectTransform parent)
    {
        ClearPopups(); cards.Clear(); pool.Clear();
        if (layer != null) { layer.gameObject.SetActive(false); Destroy(layer.gameObject); layer = null; }
        rootCanvas = target; presentationParent = parent;
    }

    /// <summary>Display-only value, shared by authoring tools; never calculates or submits damage.</summary>
    public bool TryShowDamage(BattleSide side, int slot, float amount, string element, bool critical = false,
        int visualSeed = 12345, int hitIndex = 0, long playbackID = 0, int lane = 0)
    {
        if (!isActiveAndEnabled || style == null || !style.Enabled || float.IsNaN(amount) || float.IsInfinity(amount)
            || amount < 0 || (amount == 0 && !style.ShowZero) || !TryGetCard(side, slot, out _) || !EnsureLayer()) return false;
        Vector2 position = BattlefieldDamagePopupLayout.Sample(style, visualSeed, hitIndex, 0, slot);
        position.y += Math.Max(0, lane) * style.FontHeightRatio * style.FontSizeMultiplier(critical) * 1.5f;
        Spawn(new Pending { Side = side, Slot = slot, PlaybackID = playbackID, Style = style,
            Text = BattlefieldDamagePopupLayout.Format(amount, style.Decimals),
            BaseColor = DamageColor(element), FontMultiplier = style.FontSizeMultiplier(critical),
            Position = position });
        return true;
    }

    public static void EnsureOn(GameObject host)
    {
        BattlefieldPresentationPlayer.EnsureOn(host);
    }

    private void OnEnable()
    {
        ReloadConfiguration();
        BindCards();
        BattleEventHub.SessionReset += OnSessionReset;
        GetComponent<BattlefieldPresentationPlayer>()?.RegisterView(this);
    }

    public void ReloadConfiguration()
    {
        unknownElementReported = false;
        unknownReactionReported = false;
        try { elementColors = ElementColorPalette.Load(); }
        catch (Exception error)
        {
            elementColors = null;
            // 兼容尚未发布颜色表的旧运行时数据；不复制一套默认元素色号，暂沿用旧跳字Color。
            Debug.LogWarning("元素颜色表未加载，伤害字体暂用原跳字颜色：" + error.Message, this);
        }
        try { style = BattlefieldDamagePopupStyle.Load(); }
        catch (Exception error)
        {
            style = null;
            Debug.LogError("伤害跳字参数未加载，请同步 UI 配表：" + error.Message, this);
        }
        ClearPopups();
    }

    private void OnSessionReset()
    {
        ClearPopups();
    }

    /// <summary>只由独立播放器调用，不再从战斗事件另开一条跳字通道。</summary>
    public void Present(long playbackID, BattlePresentationCue cue, Action<Exception> completed)
    {
        try
        {
            if (isActiveAndEnabled && cue.Result.TargetSide.HasValue && cue.Result.TargetSlot > 0)
            {
                if (cue.ReactionPopups != null)
                    for (int i = 0; i < cue.ReactionPopups.Count; i++) ShowReaction(playbackID, cue.ReactionPopups[i], cue, i);
                if (cue.Result.Data is DamageEventData damage) ShowDamage(playbackID, cue, damage);
                else if (style != null && cue.Result.Data is HealEventData heal && BattlefieldDamagePopupStyle.TryGetHealingAmount(heal, out float restored))
                    ShowHealing(playbackID, cue.Result.TargetSide.Value, cue.Result.TargetSlot, restored,
                        BattlefieldDamagePopupLayout.Sample(style, cue.Result.Header, cue.Timing));
                else if (cue.Result.Data is ReactionEventData && !cue.DefersReactionPopup) ShowReaction(playbackID, cue.Result, cue, 0);
                else if (cue.Result.Data is StatusEventData status) ShowStatus(playbackID, cue, BattlefieldStatusPopupStyle.TextFor(status));
                else if (cue.Result.Data is ShieldResistedStatusEventData)
                    ShowStatus(playbackID, cue, DataManager.Instance?.GetLangText("status_immune"));
            }
        }
        catch (Exception error) { completed(error); return; }
        // 数字已经出生即可继续；上浮、淡出由本组件独立收尾，不占用技能流程。
        completed(null);
    }

    private void ShowReaction(long playbackID, BattlePresentationResult result, BattlePresentationCue anchor, int lane)
    {
        if (style == null || result.Data is not ReactionEventData reaction || reaction.Reaction == ReactionType.None) return;
        string key = BattlefieldReactionPopupLayout.LangKey(reaction.Reaction);
        var data = DataManager.Instance;
        if (data == null || !data.LangDict.ContainsKey(key))
        { Debug.LogWarning("缺少反应名称Lang：" + key, this); return; }
        if (!EnsureLayer()) return;
        Color color = style.Color;
        if (elementColors != null && elementColors.TryGetReactionColor(reaction.Reaction, reaction.TriggerElement, out var configured))
        { color = configured; color.a *= style.Color.a; }
        else if (!unknownReactionReported)
        { unknownReactionReported = true; Debug.LogWarning("反应跳字颜色未配置，暂用跳字默认色：" + reaction.Reaction + "/" + reaction.TriggerElement, this); }
        Spawn(new Pending {
            Side = result.TargetSide.Value, Slot = result.TargetSlot, PlaybackID = playbackID,
            Text = data.GetLangText(key, english), Style = style, Reaction = true, FontMultiplier = BattlefieldReactionPopupLayout.FontScale,
            Font = reactionFont != null ? reactionFont : statusNames != null && statusNames.font != null ? statusNames.font : font,
            BaseColor = color, Position = BattlefieldReactionPopupLayout.Position(style, anchor.Result.Header, anchor.Timing, lane)
        });
    }

    private void ShowDamage(long playbackID, BattlePresentationCue cue, DamageEventData damage)
    {
        if (style == null || !style.Enabled || damage.Subject == null) return;
        if (!style.TryGetAmount(damage, out float amount)) return;
        BattleEventHeader header = cue.Result.Header;
        Vector2 position = BattlefieldDamagePopupLayout.Sample(style, header, cue.Timing);
        if (!EnsureLayer()) return;
        Spawn(new Pending
        {
            Side = cue.Result.TargetSide.Value, Slot = cue.Result.TargetSlot, PlaybackID = playbackID,
            Text = BattlefieldDamagePopupLayout.Format(amount, style.Decimals), Style = style,
            FontMultiplier = style.FontSizeMultiplier(damage.Critical?.IsCritical == true),
            BaseColor = DamageColor(damage.Element),
            Position = position,
        });
    }

    private Color DamageColor(string element)
    {
        if (elementColors == null) return style.Color;
        if (elementColors.TryGetColor(element, out var color))
        {
            color.a *= style.Color.a; // RGB来自统一表，保留字体自身不透明度，再由生命周期淡出。
            return color;
        }
        if (!unknownElementReported)
            Debug.LogWarning("伤害元素没有统一展示色，暂用原跳字颜色：" + element, this);
        unknownElementReported = true;
        return style.Color;
    }

    /// <summary>预览与正式结果共用文字生成；这里只接收显示值，不执行治疗或发布业务声明。</summary>
    public bool TryShowHealing(BattleSide side, int slot, float actualAmount, long playbackID = 0)
    {
        if (style == null) return false;
        return ShowHealing(playbackID, side, slot, actualAmount,
            BattlefieldDamagePopupLayout.Sample(style, unchecked((int)playbackID), 1, 0, slot));
    }

    private bool ShowHealing(long playbackID, BattleSide side, int slot, float actualAmount, Vector2 position)
    {
        if (!isActiveAndEnabled || style == null || !style.Enabled || elementColors == null ||
            !BattlefieldDamagePopupStyle.IsPositiveFinite(actualAmount) || !TryGetCard(side, slot, out var card) ||
            !card.gameObject.activeInHierarchy || !EnsureLayer()) return false;
        Color color = elementColors.HealingGreen; color.a *= style.Color.a;
        Spawn(new Pending { Side = side, Slot = slot, PlaybackID = playbackID, Style = style,
            Text = "+" + BattlefieldDamagePopupLayout.Format(actualAmount, style.Decimals),
            BaseColor = color, Position = position });
        return true;
    }

    private void ShowStatus(long playbackID, BattlePresentationCue cue, string text)
    {
        if (statusNames == null || !statusNames.isActiveAndEnabled || string.IsNullOrEmpty(text)) return;
        if (TryGetCard(cue.Result.TargetSide.Value, cue.Result.TargetSlot, out var card))
            statusNames.TryShow(text, card, playbackID);
    }

    private void BindCards()
    {
        if (layout == null) return;
        rootCanvas = layout.Canvas;
        foreach (var card in layout.Cards)
            if (card != null) RegisterCard(card.side, card.slot, card.art);
    }

    public void RegisterCard(BattleSide side, int slot, RectTransform card)
    {
        if (card != null) cards[(side, slot)] = card;
    }

    private bool EnsureLayer()
    {
        if (layer != null) return true;
        if (rootCanvas == null) BindCards();
        if (rootCanvas == null) return false;
        font = numberFont;
        if (font == null) font = rootCanvas.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(text => text.font != null)?.font;
        if (font == null) font = TMP_Settings.defaultFontAsset;
        if (font == null) return false;
        var go = new GameObject("DamagePopupLayer", typeof(RectTransform), typeof(Canvas));
        layer = (RectTransform)go.transform;
        layer.SetParent(presentationParent != null ? presentationParent : rootCanvas.transform, false);
        layer.anchorMin = Vector2.zero; layer.anchorMax = Vector2.one;
        layer.offsetMin = layer.offsetMax = Vector2.zero;
        layer.pivot = new Vector2(.5f, .5f);
        var canvas = go.GetComponent<Canvas>();
        canvas.overrideSorting = presentationParent == null;
        canvas.sortingLayerID = rootCanvas.sortingLayerID;
        canvas.sortingOrder = rootCanvas.sortingOrder + 100;
        return true;
    }

    private void LateUpdate()
    {
        if (AutomaticClock) Advance(BattlePause.DeltaTime);
    }

    public void Advance(float seconds)
    {
        if (style == null) return;
        float delta = Mathf.Max(0, seconds);
        for (int i = active.Count - 1; i >= 0; i--)
        {
            Popup popup = active[i];
            popup.Age += delta;
            if (popup.Age >= popup.Style.Lifetime)
            { Recycle(popup); active.RemoveAt(i); continue; }
            popup.Text.rectTransform.anchoredPosition = popup.Origin + Vector2.up *
                (popup.Height * popup.Style.RiseHeightRatio * popup.Style.RiseProgress(popup.Age));
            Color color = popup.BaseColor;
            color.a *= popup.Style.Alpha(popup.Age);
            popup.Text.color = color;
        }
    }

    private bool TryGetCard(BattleSide side, int slot, out RectTransform card)
    {
        if (!cards.TryGetValue((side, slot), out card) || card == null)
        {
            BindCards();
            if (!cards.TryGetValue((side, slot), out card) || card == null)
            {
                if (!missingAnchorReported) Debug.LogWarning("跳字未找到目标卡牌，可通过 RegisterCard 绑定阵营与位置。", this);
                missingAnchorReported = true;
                return false;
            }
        }
        return true;
    }
    private void Spawn(Pending request)
    {
        if (!TryGetCard(request.Side, request.Slot, out var card)) return;
        if (!card.gameObject.activeInHierarchy) return;
        while (active.Count >= request.Style.MaxVisible)
        {
            Recycle(active[0]); active.RemoveAt(0);
        }
        TMP_Text text;
        if (pool.Count > 0) text = pool.Pop();
        else
        {
            var go = new GameObject("DamageNumber", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.transform.SetParent(layer, false);
            text = go.GetComponent<TextMeshProUGUI>();
            text.font = font;
            text.raycastTarget = false;
            text.maskable = presentationParent != null;
            text.enableAutoSizing = false;
            text.enableWordWrapping = false;
            text.richText = false; // 状态名按原文字显示，不把尖括号解析为 TMP 标记。
            text.fontStyle = FontStyles.Normal;
            text.alignment = TextAlignmentOptions.Center;
            text.overflowMode = TextOverflowModes.Overflow;
            text.rectTransform.anchorMin = text.rectTransform.anchorMax = new Vector2(.5f, .5f);
            text.rectTransform.pivot = new Vector2(.5f, .5f);
        }
        Rect rect = card.rect;
        Vector3 local = new Vector3(rect.xMin + rect.width * request.Position.x, rect.yMin + rect.height * request.Position.y, 0);
        Vector2 origin = layer.InverseTransformPoint(card.TransformPoint(local));
        float height = layer.InverseTransformVector(card.TransformVector(Vector3.up * rect.height)).magnitude;
        text.font = request.Font != null ? request.Font : font;
        text.fontSize = Mathf.Max(1, height * request.Style.FontHeightRatio) * request.FontMultiplier;
        DamagePopupOutline.Apply(text, request.Style as BattlefieldDamagePopupStyle);
        text.text = request.Text;
        Color baseColor = request.BaseColor ?? request.Style.Color;
        Color initialColor = baseColor;
        initialColor.a *= request.Style.Alpha(0);
        text.color = initialColor;
        Vector2 preferred = text.GetPreferredValues(text.text);
        if (request.Reaction)
        {
            // Reserve room for a critical number below even when that hit did no HP damage.
            float gap = height * BattlefieldReactionPopupLayout.SeparationRatio(style);
            origin.y += gap;
            foreach (var other in active)
            {
                if (!other.Reaction || other.Side != request.Side || other.Slot != request.Slot || other.Style.Alpha(other.Age) <= .1f) continue;
                origin.y = Mathf.Max(origin.y, other.Text.rectTransform.anchoredPosition.y + other.Text.fontSize * 1.25f);
            }
        }
        text.rectTransform.sizeDelta = new Vector2(Mathf.Max(text.fontSize, preferred.x), Mathf.Max(text.fontSize * 2, preferred.y));
        text.rectTransform.anchoredPosition = origin;
        text.gameObject.SetActive(true);
        active.Add(new Popup { Text = text, Origin = origin, Height = height, Style = request.Style,
            Side = request.Side, Slot = request.Slot, PlaybackID = request.PlaybackID, BaseColor = baseColor, Reaction = request.Reaction });
    }

    private void Recycle(Popup popup)
    {
        popup.Text.gameObject.SetActive(false);
        pool.Push(popup.Text);
    }

    private void ClearPopups()
    {
        foreach (var popup in active) Recycle(popup);
        active.Clear();
        if (statusNames != null) statusNames.Clear();
    }

    private void OnDisable()
    {
        GetComponent<BattlefieldPresentationPlayer>()?.UnregisterView(this);
        BattleEventHub.SessionReset -= OnSessionReset;
        ClearPopups(); pool.Clear();
        if (layer != null) { layer.gameObject.SetActive(false); Destroy(layer.gameObject); layer = null; }
        cards.Clear();
        rootCanvas = null; presentationParent = null;
    }

    public void Cancel(long playbackID)
    {
        if (statusNames != null) statusNames.Cancel(playbackID);
        for (int i = active.Count - 1; i >= 0; i--)
            if (playbackID < 0 || active[i].PlaybackID == playbackID)
            { Recycle(active[i]); active.RemoveAt(i); }
    }

    private static int FirstNumber(string value)
    {
        int number = 0; bool found = false;
        foreach (char c in value)
            if (c >= '0' && c <= '9') { found = true; number = number * 10 + c - '0'; }
            else if (found) break;
        return number;
    }
}
