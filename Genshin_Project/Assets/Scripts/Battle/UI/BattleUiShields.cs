using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Optional shield visuals. Caller supplies presence/element; this module owns only drawing and animation.</summary>
[DisallowMultipleComponent]
[AddComponentMenu("Battle UI/Shields")]
public sealed class BattleUiShields : MonoBehaviour, IBattleUiShields
{
    public BattleUiLayout layout;
    public bool loadOnStart = true;
    public bool automaticClock = true;
    public bool paused;
    public UIShieldStyle Settings { get; private set; }
    public ElementColorPalette Palette { get; private set; }
    public bool Ready { get; private set; }
    public bool Loading { get; private set; }
    public string Error { get; private set; }
    private readonly List<Sprite> sprites = new List<Sprite>();
    private readonly List<Sprite> pending = new List<Sprite>();
    private bool started;
    private int loadVersion;
    private sealed class View
    {
        public UICardShield Graphic;
        public bool Visible, Sweep, Loop;
        public string Element = "None";
        public float Alpha, Clock;
    }
    private readonly Dictionary<(BattleSide, int), View> views = new Dictionary<(BattleSide, int), View>();

    private IEnumerator Start() { started = true; if (loadOnStart) yield return Reload(); }
    private void OnEnable() { if (started && loadOnStart && !Ready && !Loading) StartCoroutine(Reload()); }
    public IEnumerator Reload()
    {
        if (Loading) yield break;
        Loading = true; Error = null;
        int version = ++loadVersion;
        UIShieldStyle next = null; ElementColorPalette palette = null;
        try
        {
            if (layout == null) throw new InvalidOperationException("护盾模块需要显式绑定 BattleUiLayout。");
            next = UIShieldStyle.Load(); palette = ElementColorPalette.Load();
            if (Resources.Load<Shader>("UI/CardShield") == null) throw new InvalidOperationException("缺少 UI/CardShield Shader。");
        }
        catch (Exception ex) { Error = ex.Message; }
        if (Error != null) { Debug.LogError(Error, this); Loading = false; yield break; }
        Release(pending);
        foreach (string path in next.ArtPaths)
        {
            yield return StreamingAssetSpriteLoader.Load(path, sprite =>
            {
                if (version == loadVersion && isActiveAndEnabled) pending.Add(sprite);
                else { Destroy(sprite.texture); Destroy(sprite); }
            }, error => { if (version == loadVersion) Error = error; });
            if (version != loadVersion || !isActiveAndEnabled) yield break;
            if (Error != null) { Debug.LogError(Error, this); Release(pending); Loading = false; yield break; }
        }
        foreach (var card in layout.Cards)
        {
            if (card?.art == null) continue;
            var key = (card.side, card.slot);
            if (!views.TryGetValue(key, out var view))
            {
                var rect = new GameObject("Shield_Membrane_Mask_Tint_Sweep", typeof(RectTransform)).GetComponent<RectTransform>();
                rect.SetParent(card.art, false); rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                // Child of art: naturally below the scene's sibling frame and inner shadow.
                view = new View { Graphic = rect.gameObject.AddComponent<UICardShield>() }; views.Add(key, view);
            }
            view.Graphic.Initialize(pending[0].texture, pending[1].texture, pending[2].texture);
            view.Graphic.gameObject.SetActive(false);
        }
        Settings = next; Palette = palette; Release(sprites); sprites.AddRange(pending); pending.Clear();
        Ready = true; Loading = false;
    }

    public bool SetShield(BattleSide side, int slot, bool visible, string element = "None")
    {
        if (!isActiveAndEnabled || !Ready || !views.TryGetValue((side, slot), out var view)) return false;
        if (element != "None" && !Palette.TryGetColor(element, out _)) throw new ArgumentException("未知护盾元素：" + element);
        view.Visible = visible; view.Element = element;
        return true;
    }
    public bool PlaySweep(BattleSide side, int slot, bool loop = false)
    {
        if (!isActiveAndEnabled || !Ready || !views.TryGetValue((side, slot), out var view)) return false;
        view.Clock = 0; view.Sweep = true; view.Loop = loop;
        return true;
    }
    public void ApplyCard(BattleSide side, int slot, Color tint, float membraneOpacity, float tintOpacity,
        Vector2 membraneOffset, Vector2 maskOffset, float progress, float sweepOpacity, float width, float angle, float alpha)
    {
        if (!Ready || !views.TryGetValue((side, slot), out var view)) return;
        view.Graphic.gameObject.SetActive(isActiveAndEnabled && alpha > 0);
        view.Graphic.Apply(Settings, tint, membraneOpacity, tintOpacity, membraneOffset, maskOffset, progress, sweepOpacity, width, angle, alpha);
    }
    private void Update() { if (automaticClock && !paused) Advance(Time.unscaledDeltaTime); }
    public void Advance(float deltaTime)
    {
        if (!Ready) return;
        foreach (var pair in views)
        {
            var v = pair.Value; float fade = Settings.Number(v.Visible ? "FadeInSeconds" : "FadeOutSeconds");
            v.Alpha = fade <= 0 ? (v.Visible ? 1 : 0) : Mathf.MoveTowards(v.Alpha, v.Visible ? 1 : 0, Mathf.Max(0, deltaTime) / fade);
            float duration = Settings.Number("SweepDuration"), progress = 0, alpha = 0;
            if (v.Sweep)
            {
                v.Clock += Mathf.Max(0, deltaTime); progress = Mathf.Clamp01(v.Clock / duration);
                float fraction = Settings.Number("SweepFadeFraction");
                alpha = v.Clock >= duration ? 0 : fraction <= 0 ? 1 :
                    Mathf.SmoothStep(0, 1, Mathf.Clamp01(progress / fraction)) * Mathf.SmoothStep(0, 1, Mathf.Clamp01((1 - progress) / fraction));
                if (v.Clock >= duration + Settings.Number("SweepInterval"))
                { v.Clock = 0; v.Sweep = v.Loop; }
            }
            bool neutral = v.Element == "None";
            ApplyCard(pair.Key.Item1, pair.Key.Item2, neutral ? Color.white : Palette.GetColor(v.Element),
                Settings.Number("MembraneOpacity"), Settings.Number(neutral ? "NeutralTintOpacity" : "TintOpacity"),
                Vector2.zero, Vector2.zero, progress, Settings.Number("SweepOpacity") * alpha,
                Settings.Number("SweepWidth"), Settings.Number("SweepAngle"), v.Alpha);
        }
    }
    private void OnDisable()
    {
        ++loadVersion; StopAllCoroutines(); Release(pending); Loading = false;
        foreach (var view in views.Values) if (view.Graphic != null) view.Graphic.gameObject.SetActive(false);
    }
    private static void Release(List<Sprite> list)
    {
        foreach (var sprite in list) if (sprite != null) { Destroy(sprite.texture); Destroy(sprite); }
        list.Clear();
    }
    private void OnDestroy()
    {
        foreach (var view in views.Values) if (view.Graphic != null) Destroy(view.Graphic.gameObject);
        Release(sprites); Release(pending);
    }
}
