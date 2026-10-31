using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

/// <summary>展示正式伤害与状态赋予结果；不预测、重新结算或阻塞战斗。</summary>
[DisallowMultipleComponent]
public sealed class BattlefieldDamagePopupController : MonoBehaviour, IBattlePresentationView
{
    public BattleUiLayout layout;
    public TMP_FontAsset numberFont;
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
    }

    private readonly Dictionary<(BattleSide, int), RectTransform> cards = new Dictionary<(BattleSide, int), RectTransform>();
    private readonly List<Popup> active = new List<Popup>();
    private readonly Stack<TMP_Text> pool = new Stack<TMP_Text>();
    private BattlefieldDamagePopupStyle style;
    private BattlefieldStatusPopupStyle statusStyle;
    private ElementColorPalette elementColors;
    private bool unknownElementReported;
    private RectTransform layer;
    private Canvas rootCanvas;
    private TMP_FontAsset font;
    private bool missingAnchorReported;

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
        try { statusStyle = BattlefieldStatusPopupStyle.Load(); }
        catch (Exception error)
        {
            statusStyle = null;
            Debug.LogError("状态跳字参数未加载，请同步 UI 配表：" + error.Message, this);
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
                if (cue.Result.Data is DamageEventData damage) ShowDamage(playbackID, cue, damage);
                else if (cue.Result.Data is StatusEventData status) ShowStatus(playbackID, cue, status);
            }
        }
        catch (Exception error) { completed(error); return; }
        // 数字已经出生即可继续；上浮、淡出由本组件独立收尾，不占用技能流程。
        completed(null);
    }

    private void ShowDamage(long playbackID, BattlePresentationCue cue, DamageEventData damage)
    {
        if (style == null || !style.Enabled || damage.Subject == null) return;
        if (!style.TryGetAmount(damage, out float amount)) return;
        BattleEventHeader header = cue.Result.Header;
        Vector2 position = BattlefieldDamagePopupLayout.Sample(style, header.SessionId, header.EventId, header.HitId, header.TargetIndex);
        float Scale(int mode) => mode == 0 ? 0 : mode == 1 ? cue.Timing.MediumRandomScale : 1;
        float centerX = (style.XMin + style.XMax) * .5f, centerY = (style.YMin + style.YMax) * .5f;
        position.x = centerX + (position.x - centerX) * Scale(cue.Timing.XRandomMode);
        position.y = centerY + (position.y - centerY) * Scale(cue.Timing.YRandomMode);
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

    private void ShowStatus(long playbackID, BattlePresentationCue cue, StatusEventData fact)
    {
        if (statusStyle == null || !statusStyle.Enabled || !fact.HostSide.HasValue || fact.HostSlot <= 0) return;
        string text = BattlefieldStatusPopupStyle.TextFor(fact);
        if (text.Length == 0) return; // 不用 ID、描述或其他占位文字替代空状态名。
        if (!EnsureLayer()) return;
        Spawn(new Pending
        {
            Side = cue.Result.TargetSide.Value, Slot = cue.Result.TargetSlot, Text = text, Style = statusStyle, PlaybackID = playbackID,
            Position = new Vector2(.5f, 1),
        });
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
        layer.SetParent(rootCanvas.transform, false);
        layer.anchorMin = Vector2.zero; layer.anchorMax = Vector2.one;
        layer.offsetMin = layer.offsetMax = Vector2.zero;
        layer.pivot = new Vector2(.5f, .5f);
        var canvas = go.GetComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingLayerID = rootCanvas.sortingLayerID;
        canvas.sortingOrder = rootCanvas.sortingOrder + 100;
        return true;
    }

    private void LateUpdate()
    {
        if ((style == null || !style.Enabled) && (statusStyle == null || !statusStyle.Enabled)) return;
        float delta = Mathf.Max(0, Time.unscaledDeltaTime);
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

    private void Spawn(Pending request)
    {
        if (!cards.TryGetValue((request.Side, request.Slot), out var card) || card == null)
        {
            BindCards();
            if (!cards.TryGetValue((request.Side, request.Slot), out card) || card == null)
            {
                if (!missingAnchorReported) Debug.LogWarning("跳字未找到目标卡牌，可通过 RegisterCard 绑定阵营与位置。", this);
                missingAnchorReported = true;
                return;
            }
        }
        if (!card.gameObject.activeInHierarchy) return;
        bool statusPopup = request.Style is BattlefieldStatusPopupStyle;
        while (active.Count(popup => (popup.Style is BattlefieldStatusPopupStyle) == statusPopup) >= request.Style.MaxVisible)
        {
            int index = active.FindIndex(popup => (popup.Style is BattlefieldStatusPopupStyle) == statusPopup);
            Recycle(active[index]); active.RemoveAt(index);
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
            text.maskable = false;
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
        text.fontSize = Mathf.Max(1, height * request.Style.FontHeightRatio) * request.FontMultiplier;
        DamagePopupOutline.Apply(text, request.Style as BattlefieldDamagePopupStyle);
        text.text = request.Text;
        Color baseColor = request.BaseColor ?? request.Style.Color;
        Color initialColor = baseColor;
        initialColor.a *= request.Style.Alpha(0);
        text.color = initialColor;
        Vector2 preferred = text.GetPreferredValues(text.text);
        text.rectTransform.sizeDelta = new Vector2(Mathf.Max(text.fontSize, preferred.x), Mathf.Max(text.fontSize * 2, preferred.y));
        if (request.Style is BattlefieldStatusPopupStyle status)
        {
            float? previousTop = null;
            foreach (var popup in active)
                if (popup.Style is BattlefieldStatusPopupStyle && popup.Side == request.Side && popup.Slot == request.Slot)
                {
                    float top = popup.Text.rectTransform.anchoredPosition.y + popup.Text.rectTransform.sizeDelta.y * .5f;
                    previousTop = previousTop.HasValue ? Mathf.Max(previousTop.Value, top) : top;
                }
            origin.y = status.CenterAbove(origin.y, height, text.rectTransform.sizeDelta.y, previousTop);
        }
        text.rectTransform.anchoredPosition = origin;
        text.gameObject.SetActive(true);
        active.Add(new Popup { Text = text, Origin = origin, Height = height, Style = request.Style,
            Side = request.Side, Slot = request.Slot, PlaybackID = request.PlaybackID, BaseColor = baseColor });
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
    }

    private void OnDisable()
    {
        GetComponent<BattlefieldPresentationPlayer>()?.UnregisterView(this);
        BattleEventHub.SessionReset -= OnSessionReset;
        ClearPopups(); pool.Clear();
        if (layer != null) { layer.gameObject.SetActive(false); Destroy(layer.gameObject); layer = null; }
        cards.Clear();
    }

    public void Cancel(long playbackID)
    {
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
