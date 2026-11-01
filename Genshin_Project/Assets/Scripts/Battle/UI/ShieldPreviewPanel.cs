using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>Shield controls in the existing hit sandbox. No scene layout writes or real shield gameplay.</summary>
public sealed class ShieldPreviewPanel : MonoBehaviour
{
#if UNITY_EDITOR
    public static Func<IReadOnlyDictionary<string, float>, string> WriteTable;
#endif
    private static readonly string[] Elements = { "None", "Pyro", "Hydro", "Electro", "Cryo", "Anemo", "Geo", "Dendro" };
    private static readonly string[] Labels = { "无元素", "火", "水", "雷", "冰", "风", "岩", "草", "PSD原稿色" };
    private static readonly string[] Targets = { "敌方全体", "敌1", "敌2", "敌3", "敌4", "敌5", "我方全体", "我1", "我2", "我3", "我4", "全部九人" };
    private RectTransform[] cards;
    private TMP_FontAsset font;
    private BattleUiShields module;
    private IReadOnlyList<BattleUiCardBinding> bindings;
    private TextMeshProUGUI[] labels;
    private float[] fades;
    private UIShieldStyle settings;
    private ElementColorPalette palette;
    private bool ready, loading, visible = true, sweep = true, loop = true, paused, compare, sweepOnHit;
    private ShieldSweepClock[] clocks;
    private bool[] shown, replaying;
    private double[] revealStarts;
    private float[] revealProgress;
    public bool revealEnabled = true;
    public bool loopAppearance;
    private double lastAppearanceReplay;
    private float revealDuration, revealSoftness, revealBrightness, revealEdgeWidth, fadeInSeconds, fadeOutSeconds;
    private int element = 6, target = 6; // Geo on all four allies, for the party shield preview.
    private double clock;
    private float membraneOpacity, tintOpacity, duration, interval, jitter, width, angle, brightness;
    private Vector2 membraneOffset, maskOffset;
    private string error, message;

    public void Bind(BattleUiShields shieldModule, TMP_FontAsset labelFont)
    {
        module = shieldModule; bindings = module.layout.Cards; font = labelFont;
        cards = new RectTransform[bindings.Count];
        clocks = new ShieldSweepClock[cards.Length]; shown = new bool[cards.Length]; replaying = new bool[cards.Length];
        revealStarts = new double[cards.Length]; revealProgress = new float[cards.Length];
        for (int i = 0; i < cards.Length; i++)
        {
            cards[i] = bindings[i].art;
            clocks[i] = new ShieldSweepClock();
        }
    }
    private IEnumerator Start() { yield return Reload(); }
    public void RequestReload() { if (!loading) StartCoroutine(Reload()); }

    private IEnumerator Reload()
    {
        loading = true;
        if (module == null) { error = "未绑定独立护盾模块。"; loading = false; yield break; }
        yield return module.Reload();
        error = module.Error;
        if (!module.Ready || error != null) { loading = false; yield break; }
        settings = module.Settings; palette = module.Palette;
        if (labels == null) BuildLabels();
        ready = true; loading = false; ResetValues();
    }
    private void BuildLabels()
    {
        labels = new TextMeshProUGUI[cards.Length]; fades = new float[cards.Length];
        for (int i = 0; i < cards.Length; i++)
        {
            var rect = new GameObject("ShieldElementLabel", typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(cards[i], false); rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0);
            rect.sizeDelta = new Vector2(cards[i].rect.width, 30); rect.anchoredPosition = new Vector2(0, -22);
            labels[i] = rect.gameObject.AddComponent<TextMeshProUGUI>();
            labels[i].font = font != null ? font : TMP_Settings.defaultFontAsset;
            labels[i].fontSize = 22; labels[i].alignment = TextAlignmentOptions.Center;
            labels[i].raycastTarget = false; labels[i].enableWordWrapping = false;
        }
    }

    private void ResetValues()
    {
        membraneOpacity = settings.Number("MembraneOpacity"); tintOpacity = settings.Number("TintOpacity");
        duration = settings.Number("SweepDuration"); interval = settings.Number("SweepInterval");
        jitter = settings.Number("SweepIntervalJitter");
        width = settings.Number("SweepWidth"); angle = settings.Number("SweepAngle"); brightness = settings.Number("SweepOpacity");
        revealDuration = settings.Number("RevealDuration"); revealSoftness = settings.Number("RevealSoftness");
        revealBrightness = settings.Number("RevealEdgeOpacity");
        revealEdgeWidth = settings.Number("RevealEdgeWidth");
        fadeInSeconds = settings.Number("FadeInSeconds"); fadeOutSeconds = settings.Number("FadeOutSeconds");
        membraneOffset = maskOffset = Vector2.zero; ReplayAppearance();
    }
    private void ReplaySweep()
    {
        for (int i = 0; i < clocks.Length; i++)
            clocks[i].Restart(Math.Max(clock, revealStarts[i] + (revealEnabled ? revealDuration : 0)));
    }
    private void BeginAppearance(int index)
    {
        revealStarts[index] = clock;
        revealProgress[index] = UIShieldStyle.SampleReveal(0, revealEnabled ? revealDuration : 0);
        clocks[index].Restart(clock + (revealEnabled ? revealDuration : 0));
    }
    public void ReplayAppearance()
    {
        if (!ready || clocks == null || fades == null) return;
        lastAppearanceReplay = clock;
        for (int i = 0; i < clocks.Length; i++)
        {
            if (!Selected(i)) continue;
            // 已显示的盾先完整淡出，再重新展开；不能直接把Alpha清零。
            replaying[i] = true;
        }
    }
    public void NotifyHit(int index)
    {
        if (ready && visible && sweepOnHit && index >= 0 && index < clocks.Length && Selected(index))
            clocks[index].Restart(Math.Max(clock, revealStarts[index] + (revealEnabled ? revealDuration : 0)));
    }
    private bool Selected(int i)
    {
        if (compare || target == 11) return true;
        if (target == 0) return i >= 4;
        if (target == 6) return i < 4;
        return target <= 5 ? i == target + 3 : i == target - 7;
    }
    private void Update()
    {
        if (!ready) return;
        float dt = paused ? 0 : Time.unscaledDeltaTime;
        clock += dt;
        if (visible && loopAppearance && clock - lastAppearanceReplay >= Math.Max(2.5, fadeOutSeconds + revealDuration + duration + .5))
            ReplayAppearance();
        for (int i = 0; i < cards.Length; i++)
        {
            bool selected = Selected(i), requested = visible && selected;
            if (!requested) replaying[i] = false;
            if (replaying[i] && fades[i] <= 0) { replaying[i] = false; shown[i] = false; }
            bool show = requested && !replaying[i];
            if (show && !shown[i]) BeginAppearance(i);
            shown[i] = show;
            if (show) revealProgress[i] = UIShieldStyle.SampleReveal(clock - revealStarts[i], revealEnabled ? revealDuration : 0);
            float progress = 0;
            float sweepAlpha = show && sweep ? clocks[i].Sample(clock, duration, interval, jitter,
                settings.Number("SweepFadeFraction"), loop, out progress) : 0;
            fades[i] = UIShieldStyle.StepAlpha(fades[i], show, dt, show ? fadeInSeconds : fadeOutSeconds);
            int e = compare ? i % Elements.Length : element;
            Color tint = e == 8 ? settings.PsdTint : palette.GetColor(Elements[e]);
            float tintAlpha = e == 0 ? settings.Number("NeutralTintOpacity") : tintOpacity;
            module.ApplyCard(bindings[i].side, bindings[i].slot, tint, membraneOpacity, tintAlpha, membraneOffset, maskOffset,
                progress, sweep ? brightness * sweepAlpha : 0, width, angle, fades[i], revealProgress[i], revealSoftness, revealBrightness, revealEdgeWidth);
            labels[i].text = selected ? Labels[e] + (visible ? "护盾" : "（原卡面）") : "";
        }
    }

    public void DrawMain(GUIStyle label, GUIStyle button, GUIStyle toggle)
    {
        if (!ready)
        {
            GUILayout.Label(error ?? "正在读取护盾表和独立PSD图层…", label);
            GUI.enabled = !loading;
            if (GUILayout.Button("重新加载护盾", button)) RequestReload();
            GUI.enabled = true; return;
        }
        GUILayout.BeginHorizontal(); GUILayout.Label("护盾元素（独立于攻击元素）", label, GUILayout.Width(340));
        element = GUILayout.SelectionGrid(element, Labels, Labels.Length, button); GUILayout.EndHorizontal();
        target = GUILayout.SelectionGrid(target, Targets, Targets.Length, button);
        GUILayout.BeginHorizontal();
        visible = GUILayout.Toggle(visible, "显示护盾 / 原卡面对照", toggle);
        compare = GUILayout.Toggle(compare, "八种护盾同时对照", toggle);
        sweep = GUILayout.Toggle(sweep, "填充矩形扫光", toggle); loop = GUILayout.Toggle(loop, "循环扫光", toggle);
        sweepOnHit = GUILayout.Toggle(sweepOnHit, "命中时重新扫光", toggle);
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("重新施加 / 左右展开", button)) { visible = true; ReplayAppearance(); }
        if (GUILayout.Button("移除 / 淡出", button)) visible = false;
        bool nextReveal = GUILayout.Toggle(revealEnabled, "中心向两侧展开", toggle);
        if (nextReveal != revealEnabled) { revealEnabled = nextReveal; ReplayAppearance(); }
        loopAppearance = GUILayout.Toggle(loopAppearance, "循环施加（展示形成过程）", toggle);
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("扫光一次", button)) { sweep = true; ReplaySweep(); }
        if (GUILayout.Button(paused ? "继续护盾动画" : "暂停护盾动画", button)) paused = !paused;
        if (GUILayout.Button("恢复表内参数", button)) ResetValues();
        GUI.enabled = !loading;
        if (GUILayout.Button(loading ? "加载中…" : "重读护盾运行时表和贴图", button)) RequestReload();
#if UNITY_EDITOR
        GUI.enabled = !loading && WriteTable != null;
        if (GUILayout.Button("写回表并发布", button)) SaveTable();
#else
        GUI.enabled = false;
        GUILayout.Button("写回表（仅编辑器）", button);
#endif
        GUI.enabled = true; GUILayout.EndHorizontal();
        GUILayout.Label(error ?? message ?? "先左右展开，再接斜向扫光；移除时淡出。此页只预览显示，不改变真实护盾。", label);
    }

    public void DrawAdjustments(GUIStyle label)
    {
        if (!ready) return;
        GUILayout.BeginArea(new Rect(24, 826, 1872, 244), GUI.skin.box);
        GUILayout.Label("调节即时预览；点击“写回表并发布”保存，未写回的调整退出丢弃。位置单位：设计像素", label);
        GUILayout.BeginHorizontal();
        Slider("膜面透明度", ref membraneOpacity, 0, 1, label); Slider("元素色透明度", ref tintOpacity, 0, 1, label);
        Slider("扫光亮度", ref brightness, 0, 1, label); GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        Slider("扫过秒数", ref duration, .1f, 3, label); Slider("间隔秒数", ref interval, 0, 10, label);
        Slider("光带宽/卡高", ref width, .02f, .6f, label); GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        Slider("光带角度", ref angle, -75, 75, label); Slider("膜面额外 X", ref membraneOffset.x, -30, 30, label);
        Slider("膜面额外 Y", ref membraneOffset.y, -30, 30, label); GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        Slider("蒙版额外 X", ref maskOffset.x, -30, 30, label); Slider("蒙版额外 Y", ref maskOffset.y, -30, 30, label);
        Slider("间隔浮动 ±秒", ref jitter, 0, 2, label); GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        Slider("展开时长（0立即）", ref revealDuration, 0, 1, label);
        Slider("展开柔边 / 半宽", ref revealSoftness, 0, .3f, label);
        Slider("展开前沿亮度", ref revealBrightness, 0, 1, label); GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        Slider("淡入秒数（0立即）", ref fadeInSeconds, 0, 2, label);
        Slider("淡出秒数（0立即）", ref fadeOutSeconds, 0, 2, label);
        Slider("展开前沿宽 / 半宽", ref revealEdgeWidth, 0, .3f, label); GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }
#if UNITY_EDITOR
    private void SaveTable()
    {
        var values = new Dictionary<string, float> {
            ["MembraneOpacity"] = membraneOpacity, ["TintOpacity"] = tintOpacity, ["SweepOpacity"] = brightness,
            ["SweepDuration"] = duration, ["SweepInterval"] = interval, ["SweepIntervalJitter"] = jitter,
            ["SweepWidth"] = width, ["SweepAngle"] = angle,
            ["MembraneOffsetX"] = settings.Number("MembraneOffsetX") + membraneOffset.x,
            ["MembraneOffsetY"] = settings.Number("MembraneOffsetY") + membraneOffset.y,
            ["MaskOffsetX"] = settings.Number("MaskOffsetX") + maskOffset.x,
            ["MaskOffsetY"] = settings.Number("MaskOffsetY") + maskOffset.y,
            ["RevealDuration"] = revealDuration, ["RevealSoftness"] = revealSoftness,
            ["RevealEdgeWidth"] = revealEdgeWidth, ["RevealEdgeOpacity"] = revealBrightness,
            ["FadeInSeconds"] = fadeInSeconds, ["FadeOutSeconds"] = fadeOutSeconds
        };
        try
        {
            settings.WithNumbers(values); // 写入前校验；预览开关、元素与目标不属于长期参数。
            message = WriteTable(values);
            RequestReload(); // 使用已发布的绝对偏移，额外偏移恢复0，避免连续保存累计两次。
        }
        catch (Exception ex) { message = "写回失败：" + ex.Message; Debug.LogError(message, this); }
    }
#endif
    private static void Slider(string title, ref float value, float min, float max, GUIStyle label)
    {
        GUILayout.Label($"{title} {value:F2}", label, GUILayout.Width(265));
        value = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(290)); GUILayout.Space(35);
    }
    private void OnDestroy()
    {
        StopAllCoroutines();
        if (labels != null) foreach (var text in labels) if (text != null) Destroy(text.gameObject);
    }
    private void OnDisable()
    {
        if (bindings == null || module == null) return;
        for (int i = 0; i < bindings.Count; i++)
        {
            shown[i] = replaying[i] = false;
            if (fades != null) fades[i] = 0;
            module.ApplyCard(bindings[i].side, bindings[i].slot, Color.white, 0, 0, Vector2.zero, Vector2.zero, 0, 0, 1, 0, 0);
            if (labels != null && labels[i] != null) labels[i].text = "";
        }
    }
}
