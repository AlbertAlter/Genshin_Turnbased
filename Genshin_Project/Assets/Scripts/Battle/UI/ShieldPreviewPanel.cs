using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>Shield controls in the existing hit sandbox. No scene layout writes or real shield gameplay.</summary>
public sealed class ShieldPreviewPanel : MonoBehaviour
{
    private static readonly string[] Elements = { "None", "Pyro", "Hydro", "Electro", "Cryo", "Anemo", "Geo", "Dendro" };
    private static readonly string[] Labels = { "无元素", "火", "水", "雷", "冰", "风", "岩", "草", "PSD原稿色" };
    private static readonly string[] Targets = { "敌方全体", "敌1", "敌2", "敌3", "敌4", "敌5", "我方全体", "我1", "我2", "我3", "我4", "全部九人" };
    private RectTransform[] cards;
    private TMP_FontAsset font;
    private UICardShield[] shields;
    private TextMeshProUGUI[] labels;
    private float[] fades;
    private List<Sprite> ownedSprites = new List<Sprite>();
    private readonly List<Sprite> pendingSprites = new List<Sprite>();
    private UIShieldStyle settings;
    private ElementColorPalette palette;
    private bool ready, loading, visible = true, sweep = true, loop = true, paused, compare, sweepOnHit;
    private bool sweeping = true;
    private int element = 1, target = 7;
    private float clock, progress, sweepAlpha;
    private float membraneOpacity, tintOpacity, duration, interval, width, angle, brightness;
    private Vector2 membraneOffset, maskOffset;
    private string error;

    public void Bind(RectTransform[] cardRoots, TMP_FontAsset labelFont) { cards = cardRoots; font = labelFont; }
    private IEnumerator Start() { yield return Reload(); }
    public void RequestReload() { if (!loading) StartCoroutine(Reload()); }

    private IEnumerator Reload()
    {
        loading = true;
        UIShieldStyle next = null; ElementColorPalette colors = null;
        try { next = UIShieldStyle.Load(); colors = ElementColorPalette.Load(); error = null; }
        catch (Exception ex) { error = ex.Message; }
        if (next == null || colors == null) { loading = false; yield break; }
        pendingSprites.Clear();
        foreach (string path in next.ArtPaths)
        {
            yield return StreamingAssetSpriteLoader.Load(path, sprite => pendingSprites.Add(sprite), message => error = message);
            if (error != null) { DisposeSprites(pendingSprites); loading = false; yield break; }
        }
        if (Resources.Load<Shader>("UI/CardShield") == null)
        { error = "缺少 UI/CardShield Shader"; DisposeSprites(pendingSprites); loading = false; yield break; }
        settings = next; palette = colors;
        if (shields == null) Build();
        for (int i = 0; i < shields.Length; i++)
            shields[i].Initialize(pendingSprites[0].texture, pendingSprites[1].texture, pendingSprites[2].texture);
        DisposeSprites(ownedSprites); ownedSprites.AddRange(pendingSprites); pendingSprites.Clear();
        ready = true; loading = false; ResetValues();
    }

    private void Build()
    {
        shields = new UICardShield[cards.Length]; labels = new TextMeshProUGUI[cards.Length]; fades = new float[cards.Length];
        for (int i = 0; i < cards.Length; i++)
        {
            var rect = new GameObject("Shield_Membrane_Mask_Tint_Sweep", typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(cards[i], false); rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            // Keep the existing card frame, inner shadow and front impacts above the shield.
            var inner = cards[i].Find("InnerShadow");
            if (inner != null) rect.SetSiblingIndex(inner.GetSiblingIndex());
            shields[i] = rect.gameObject.AddComponent<UICardShield>();
            var labelRect = new GameObject("ShieldElementLabel", typeof(RectTransform)).GetComponent<RectTransform>();
            labelRect.SetParent(cards[i], false); labelRect.anchorMin = labelRect.anchorMax = new Vector2(.5f, 0);
            labelRect.sizeDelta = new Vector2(cards[i].rect.width, 30); labelRect.anchoredPosition = new Vector2(0, -22);
            labels[i] = labelRect.gameObject.AddComponent<TextMeshProUGUI>();
            labels[i].font = font != null ? font : TMP_Settings.defaultFontAsset;
            labels[i].fontSize = 22; labels[i].alignment = TextAlignmentOptions.Center;
            labels[i].raycastTarget = false; labels[i].enableWordWrapping = false;
        }
    }

    private void ResetValues()
    {
        membraneOpacity = settings.Number("MembraneOpacity"); tintOpacity = settings.Number("TintOpacity");
        duration = settings.Number("SweepDuration"); interval = settings.Number("SweepInterval");
        width = settings.Number("SweepWidth"); angle = settings.Number("SweepAngle"); brightness = settings.Number("SweepOpacity");
        membraneOffset = maskOffset = Vector2.zero; ReplaySweep();
    }
    private void ReplaySweep() { clock = progress = sweepAlpha = 0; sweeping = true; }
    public void NotifyHit(int index)
    {
        if (ready && visible && sweepOnHit && Selected(index)) ReplaySweep();
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
        sweepAlpha = 0;
        if (sweep && sweeping)
        {
            clock += dt; progress = Mathf.Clamp01(clock / duration);
            float fade = settings.Number("SweepFadeFraction");
            sweepAlpha = fade > 0 ? Mathf.SmoothStep(0, 1, Mathf.Clamp01(progress / fade)) *
                Mathf.SmoothStep(0, 1, Mathf.Clamp01((1 - progress) / fade)) : 1;
            if (clock >= duration) { sweeping = false; clock = 0; sweepAlpha = 0; }
        }
        else if (sweep && loop)
        {
            clock += dt;
            if (clock >= interval) ReplaySweep();
        }
        for (int i = 0; i < shields.Length; i++)
        {
            bool selected = Selected(i), show = visible && selected;
            float fadeTime = settings.Number(show ? "FadeInSeconds" : "FadeOutSeconds");
            fades[i] = fadeTime <= 0 ? (show ? 1 : 0) : Mathf.MoveTowards(fades[i], show ? 1 : 0, dt / fadeTime);
            int e = compare ? i % Elements.Length : element;
            Color tint = e == 8 ? settings.PsdTint : e == 0 ? Color.white : palette.GetColor(Elements[e]);
            float tintAlpha = e == 0 ? settings.Number("NeutralTintOpacity") : tintOpacity;
            shields[i].Apply(settings, tint, membraneOpacity, tintAlpha, membraneOffset, maskOffset,
                progress, sweep ? brightness * sweepAlpha : 0, width, angle, fades[i]);
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
        sweep = GUILayout.Toggle(sweep, "扫光", toggle); loop = GUILayout.Toggle(loop, "循环扫光", toggle);
        sweepOnHit = GUILayout.Toggle(sweepOnHit, "命中时重新扫光", toggle);
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("扫光一次", button)) { sweep = true; ReplaySweep(); }
        if (GUILayout.Button(paused ? "继续护盾动画" : "暂停护盾动画", button)) paused = !paused;
        if (GUILayout.Button("恢复表内参数", button)) ResetValues();
        GUI.enabled = !loading;
        if (GUILayout.Button(loading ? "加载中…" : "重读护盾运行时表和贴图", button)) RequestReload();
        GUI.enabled = true; GUILayout.EndHorizontal();
        GUILayout.Label(error ?? "膜面和蒙版居中；显隐/扫光为预览，不改变真实护盾。切回命中页可在护盾上播放攻击。", label);
    }

    public void DrawAdjustments(GUIStyle label)
    {
        if (!ready) return;
        GUILayout.BeginArea(new Rect(24, 826, 1872, 244), GUI.skin.box);
        GUILayout.Label("临时调节（退出不保存）；长期配置：Charts/UI/ShieldEffects.xlsx → 同步 → 重读。位置单位：设计像素", label);
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
        GUILayout.Label($"扫光进度 {progress:P0}｜中心偏移基准来自表", label, GUILayout.Width(580)); GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }
    private static void Slider(string title, ref float value, float min, float max, GUIStyle label)
    {
        GUILayout.Label($"{title} {value:F2}", label, GUILayout.Width(265));
        value = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(290)); GUILayout.Space(35);
    }
    private static void DisposeSprites(List<Sprite> list)
    {
        foreach (var sprite in list)
        { if (sprite == null) continue; Destroy(sprite.texture); Destroy(sprite); }
        list.Clear();
    }
    private void OnDestroy()
    {
        StopAllCoroutines();
        if (shields != null) foreach (var view in shields) if (view != null) Destroy(view.gameObject);
        if (labels != null) foreach (var text in labels) if (text != null) Destroy(text.gameObject);
        DisposeSprites(ownedSprites); DisposeSprites(pendingSprites);
    }
}
