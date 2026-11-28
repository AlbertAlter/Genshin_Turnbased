using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

/// <summary>公共治疗/复苏光芒。消费真实治疗或Revive生命变化；不补HP、不发布Heal或复活声明。</summary>
[DisallowMultipleComponent]
public sealed class BattleUiHealEffects : MonoBehaviour, IBattlePresentationView, IBattleVisualCompletion
{
    public BattlefieldPresentationPlayer player;
    public BattleUiLayout layout;
    public BattleUiAnimatedCards cards;
    public bool automaticClock = true;
    public bool Paused { get; set; }
    public float Lifetime => style?.Lifetime ?? 0;
    private HealEffectStyle style;
    private Color healingColor;
    private sealed class Burst
    {
        public UIHealRiseGraphic Graphic;
        public long Playback;
        public float Age;
        public HealEffectStyle Style;
    }
    private readonly List<Burst> active = new List<Burst>();
    private sealed class Revival
    {
        public BattleEntity Entity;
        public BattleSide Side;
        public int Slot;
        public long Playback;
        public uint Seed;
    }
    private readonly List<Revival> revivals = new List<Revival>();
    public bool HasPendingVisuals => active.Count > 0 || revivals.Count > 0;
    private readonly Stack<UIHealRiseGraphic> pool = new Stack<UIHealRiseGraphic>();

    private void OnEnable()
    {
        ReloadConfiguration();
        if (player != null) player.RegisterView(this);
        BattleEventHub.SessionReset += ResetSession;
    }
    public void ReloadConfiguration()
    {
        try
        {
            var next = HealEffectStyle.Load(); var palette = ElementColorPalette.Load();
            style = next; healingColor = palette.HealingGreen;
        }
        catch (Exception e) { Debug.LogError("治疗光芒参数加载失败：" + e.Message, this); }
    }
    public void Present(long playbackID, BattlePresentationCue cue, Action<Exception> completed)
    {
        try
        {
            if (cue.Result.Data is HealEventData heal && CanPlay(heal) && cue.Result.TargetSide.HasValue)
                TryPlay(cue.Result.TargetSide.Value, cue.Result.TargetSlot, playbackID, unchecked((uint)cue.Result.Header.EventId));
            // ReviveEffect already commits one HPChanged(Reason=Revive). Reuse that fact rather
            // than inventing a Heal event, which would incorrectly run healing talents/equipment.
            else if (cue.Result.Data is HPChangedEventData hp && CanPlayRevival(hp) && cue.Result.TargetSide.HasValue
                && cards != null && layout != null && style != null && style.Enabled)
                revivals.Add(new Revival { Entity = hp.Subject, Side = cue.Result.TargetSide.Value,
                    Slot = cue.Result.TargetSlot, Playback = playbackID, Seed = unchecked((uint)cue.Result.Header.EventId) });
        }
        catch (Exception e) { completed(e); return; }
        // 光芒与跳字同刻开始，独立收尾，不延长技能/群体治疗的结算等待。
        completed(null);
    }
    public static bool CanPlay(HealEventData heal) => heal?.Subject != null && BattlefieldDamagePopupStyle.IsPositiveFinite(heal.Requested);
    public static bool CanPlayRevival(HPChangedEventData hp) => hp?.Subject != null && hp.Reason == "Revive"
        && hp.Before <= 0 && !float.IsNaN(hp.Before) && !float.IsInfinity(hp.Before)
        && BattlefieldDamagePopupStyle.IsPositiveFinite(hp.After);

    /// <summary>预览也走同一入口。每个目标/每次请求独立播放；未挂模块的场景不会自动安装。</summary>
    public bool TryPlay(BattleSide side, int slot, long playbackID = 0, uint seed = 1)
    {
        if (!isActiveAndEnabled || style == null || !style.Enabled || layout == null ||
            !layout.TryGetCard(side, slot, out var card) || !card.art.gameObject.activeInHierarchy) return false;
        var parent = layout.HitLayer(card, false);
        if (parent == null) return false;
        while (active.Count >= style.MaxVisible) Recycle(0);
        var graphic = pool.Count > 0 ? pool.Pop() :
            new GameObject("HealingLight", typeof(RectTransform), typeof(CanvasRenderer)).AddComponent<UIHealRiseGraphic>();
        var rect = graphic.rectTransform; rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        graphic.raycastTarget = false; graphic.color = healingColor;
        graphic.Style = style; graphic.Seed = seed; graphic.SetProgress(0); graphic.gameObject.SetActive(true);
        active.Add(new Burst { Graphic = graphic, Playback = playbackID, Style = style });
        return true;
    }
    private void Update() { if (automaticClock) Advance(BattlePause.DeltaTime); }
    public void Advance(float seconds)
    {
        if (Paused || float.IsNaN(seconds) || float.IsInfinity(seconds)) return;
        for (int i = revivals.Count - 1; i >= 0; i--)
        {
            var r = revivals[i];
            if (cards == null || !cards.isActiveAndEnabled || cards.factory == null || !cards.factory.isActiveAndEnabled
                || layout == null || r.Entity == null || !r.Entity.IsAlive || !r.Entity.IsOnField
                || r.Entity.Side != r.Side || r.Entity.SlotPosition != r.Slot || cards.battle == null
                || cards.battle.GetEntityByPosition(r.Side, r.Slot) != r.Entity
                || !layout.TryGetCard(r.Side, r.Slot, out var binding))
            { revivals.RemoveAt(i); continue; }
            if (!cards.TryGetCard(binding, r.Entity, out var view)) continue;
            if (view.Error != null) { revivals.RemoveAt(i); continue; }
            if (!view.Ready || !view.gameObject.activeInHierarchy) continue;
            TryPlay(r.Side, r.Slot, r.Playback, r.Seed); revivals.RemoveAt(i);
        }
        for (int i = active.Count - 1; i >= 0; i--)
        {
            var burst = active[i]; burst.Age += Mathf.Max(0, seconds);
            if (burst.Graphic == null || burst.Age >= burst.Style.Lifetime) { Recycle(i); continue; }
            burst.Graphic.SetProgress(burst.Age / burst.Style.Lifetime);
        }
    }
    private void Recycle(int index)
    {
        var burst = active[index]; active.RemoveAt(index);
        if (burst.Graphic == null) return;
        burst.Graphic.gameObject.SetActive(false); burst.Graphic.transform.SetParent(transform, false); pool.Push(burst.Graphic);
    }
    public void Cancel(long playbackID)
    {
        revivals.RemoveAll(r => playbackID < 0 || r.Playback == playbackID);
        for (int i = active.Count - 1; i >= 0; i--)
            if (playbackID < 0 || active[i].Playback == playbackID) Recycle(i);
    }
    private void ResetSession() => Cancel(-1);
    private void OnDisable()
    {
        if (player != null) player.UnregisterView(this);
        BattleEventHub.SessionReset -= ResetSession; Cancel(-1); Paused = false;
    }
    private void OnDestroy() { Cancel(-1); foreach (var g in pool) if (g != null) Destroy(g.gameObject); pool.Clear(); }
}

/// <summary>纯显示参数；治疗绿始终读ElementColors/Healing，数字仍读原DamagePopup。</summary>
public sealed class HealEffectStyle
{
    public const string FileName = "HealEffects.xlsx";
    public bool Enabled;
    public int ParticleCount, MaxVisible;
    public float Lifetime, Rise, Width, Height, Spread, Stagger, Opacity;
    public static HealEffectStyle Load() => CampaignUiTables.Read(() => ReadFile(BattleTimelineSettings.RuntimePath(FileName)));
    public static HealEffectStyle ReadFile(string path) => Parse(BattlefieldDamagePopupStyle.ReadParameters(path));
    public static HealEffectStyle Parse(IReadOnlyDictionary<string, string> values)
    {
        string[] keys = { "Enabled", "LifetimeSeconds", "ParticleCount", "RiseHeightRatio", "LightWidthRatio",
            "LightHeightRatio", "SpreadWidthRatio", "SpawnStaggerRatio", "Opacity", "MaxVisible" };
        foreach (var key in values.Keys) if (Array.IndexOf(keys, key) < 0) throw new InvalidDataException("未知治疗光芒参数：" + key);
        float Number(string key, float min, float max, bool integer = false)
        {
            if (!values.TryGetValue(key, out string raw) || !float.TryParse(raw, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float n) || float.IsNaN(n) || float.IsInfinity(n) ||
                n < min || n > max || (integer && n != (int)n)) throw new InvalidDataException("治疗光芒参数无效：" + key);
            return n;
        }
        return new HealEffectStyle { Enabled = Number("Enabled", 0, 1, true) == 1,
            Lifetime = Number("LifetimeSeconds", .01f, 10), ParticleCount = (int)Number("ParticleCount", 1, 64, true),
            Rise = Number("RiseHeightRatio", 0, 2), Width = Number("LightWidthRatio", .001f, .5f),
            Height = Number("LightHeightRatio", .001f, 1), Spread = Number("SpreadWidthRatio", 0, 1),
            Stagger = Number("SpawnStaggerRatio", 0, .8f), Opacity = Number("Opacity", 0, 1),
            MaxVisible = (int)Number("MaxVisible", 1, 256, true) };
    }
}

/// <summary>一张卡一次网格绘制：柔光竖芒和小光点上浮，参数按卡宽高缩放，不移动布局或分配逐帧纹理。</summary>
internal sealed class UIHealRiseGraphic : MaskableGraphic
{
    internal HealEffectStyle Style;
    internal uint Seed;
    private float progress;
    internal void SetProgress(float value) { progress = Mathf.Clamp01(value); SetVerticesDirty(); }
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear(); if (Style == null) return;
        Rect rect = rectTransform.rect;
        for (int i = 0; i < Style.ParticleCount; i++)
        {
            uint bits = HitEffectMath.Mix(Seed + (uint)i * 7919u);
            float t = Mathf.Clamp01((progress - Style.Stagger * ((bits & 255) / 255f)) / (1 - Style.Stagger));
            float alpha = Mathf.Sin(Mathf.PI * t) * Style.Opacity;
            if (alpha < .001f) continue;
            float x = rect.center.x + rect.width * Style.Spread * (((bits >> 8 & 255) / 255f) - .5f);
            float y = rect.yMin + rect.height * (.04f + .34f * ((bits >> 16 & 255) / 255f) + Style.Rise * (1 - Mathf.Pow(1 - t, 1.4f)));
            float w = rect.width * Style.Width * (.6f + .4f * ((bits >> 24) / 255f));
            float h = rect.height * Style.Height;
            // 多层透明顶点形成软边；沿用UGUI标准混合，不把柔光误当成模糊贴图。
            Glow(mesh, new Vector2(x, y), w * 2.5f, h, color, alpha * .18f);
            Glow(mesh, new Vector2(x, y), w, h * .8f, color, alpha * .55f);
            Color core = Color.Lerp(color, Color.white, .42f);
            Glow(mesh, new Vector2(x, y + h * .12f), w * .2f, h * .45f, core, alpha);
            Glow(mesh, new Vector2(x, y + h * .2f), w * .8f, w * .8f, core, alpha * .7f);
        }
    }
    private static void Glow(VertexHelper mesh, Vector2 p, float width, float height, Color c, float alpha)
    {
        int v = mesh.currentVertCount;
        Color edge = c; edge.a = 0; c.a *= alpha;
        mesh.AddVert(p, c, Vector2.zero);
        mesh.AddVert(p + Vector2.left * width, edge, Vector2.zero);
        mesh.AddVert(p + Vector2.up * height, edge, Vector2.zero);
        mesh.AddVert(p + Vector2.right * width, edge, Vector2.zero);
        mesh.AddVert(p + Vector2.down * height, edge, Vector2.zero);
        for (int i = 0; i < 4; i++) mesh.AddTriangle(v, v + i + 1, v + (i + 1) % 4 + 1);
    }
}
