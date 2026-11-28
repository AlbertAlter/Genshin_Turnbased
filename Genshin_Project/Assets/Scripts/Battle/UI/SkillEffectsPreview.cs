using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Isolated authoring host. Saved layout, explicit public player, mock targets; no BattleManager or save files.</summary>
public sealed partial class SkillEffectsPreview : MonoBehaviour
{
    [Serializable] public sealed class PreviewCard
    {
        public BattleSide side;
        public int id, slot;
        public Vector2 position;
        [NonSerialized] public RectTransform mount;
        [NonSerialized] public UICardView view;
    }
    public ProjectUiCanvas sharedCanvas;
    public UICardFactory cardFactory;
    public UIBattleEffectPlayer player;
    public UIBattleEffectPlayer burningAreaPlayer;
    public UIBattleEffectAnimationPreview fullPreview;
    public Shader effectShader;
    public Font controlsFont;
    public Vector2 cardSize = new Vector2(197, 338), stagePosition = new Vector2(230, -45);
    public float stageScale = .7f;
    public PreviewCard[] cards;
    public BattleEffectKind initialEffect = BattleEffectKind.Explosion;
#if UNITY_EDITOR
    public static Func<BattleEffectKind, BattleEffectAssets> ResolveAssets;
    public static Func<BattleEffectKind, IReadOnlyDictionary<string, float>, string> WriteParameters;
#endif
    private readonly Dictionary<BattleEffectKind, BattleEffectParameters> tuning = new Dictionary<BattleEffectKind, BattleEffectParameters>();
    private readonly Dictionary<string, string> edits = new Dictionary<string, string>(StringComparer.Ordinal);
    private readonly UIEffectPicker effectPicker = new UIEffectPicker();
    private readonly UIBattlePreviewStage stageView = new UIBattlePreviewStage();
    private UIViewportZoom viewportZoom => stageView.Zoom;
    private bool releasePickerFocus;
    private readonly List<RectTransform> selected = new List<RectTransform>();
    private Canvas canvas;
    private RectTransform stageViewport, stage, behind, above;
    private BattleEffectParameters parameters;
    private BattleEffectParameters burningAreaParameters;
    private BattleEffectKind kind;
    private bool ready, loading, choosing, paused, loop, manual;
    private int targetCount = 3, mainSlot = 3, casterSlot = 3, generation;
    private BattleSide targetSide = BattleSide.Enemy;
    private readonly bool[] occupied = { true, true, true, true, true };
    private float speed = 1;
    private Vector2 scroll;
    private string error, message;
    private GUIStyle label, button, heading, field;
    private bool IsKaeya => tableResource==null&&(kind == BattleEffectKind.KaeyaGlacialWaltz || kind == BattleEffectKind.KaeyaIcicle);
    private UICardAirborneFlip ventiPreviewFlip;
    private bool ventiFlipped;
    private bool fullAnimation;
    private string previewAmount = "1234";
    private string absorbedAmount = "1234";
    private int ventiAbsorption;
    private ElementColorPalette previewPalette;
    private static readonly string[] AbsorptionElements = { null, "Pyro", "Hydro", "Electro", "Cryo" };
    private static readonly string[] AbsorptionLabels = { "无", "火", "水", "雷", "冰" };
    private bool HasVentiAbsorption => tableResource==null&&(kind == BattleEffectKind.VentiStormEye || kind == BattleEffectKind.VentiStormPulse);
    private GUIStyle toggle;
    private BattleEffectKind? CompanionKind => tableResource!=null?null:fullAnimation ? BattleEffectAnimationPreviewPlan.Companion(kind)
        : kind == BattleEffectKind.AmberArrowRain ? BattleEffectKind.AmberBurningArea
        : kind == BattleEffectKind.KaeyaIcicle ? BattleEffectKind.KaeyaGlacialWaltz
        : kind == BattleEffectKind.VentiStormPulse ? BattleEffectKind.VentiStormEye : (BattleEffectKind?)null;

    private void OnEnable() { StartCoroutine(Assemble()); }
    private IEnumerator Assemble()
    {
        int token = ++generation; loading = true; ready = false; error = message = null;
        if (sharedCanvas == null || cardFactory == null || player == null || burningAreaPlayer == null || burningAreaPlayer == player
            || fullPreview == null || effectShader == null || controlsFont == null || cards == null || cards.Length == 0)
        { Fail("场景缺少显式公共组件、Shader、字体或卡位绑定。"); yield break; }
        if (!ProjectUiCanvas.EnsureBackgroundViewport(GetComponent<Camera>()))
        { Fail("缺少已启用的背景清屏相机。"); yield break; }
        canvas = sharedCanvas.CreateContentCanvas("SkillEffectsPreviewCanvas");
        yield return stageView.Create(canvas, stagePosition, stageScale, Fail);
        if (error != null || token != generation) yield break;
        stageViewport = stageView.Viewport; stage = stageView.Root;
        behind = Mount("BehindCards", stage, new Vector2(1920, 1080));
        behind.gameObject.AddComponent<RectMask2D>();
        var cardLayer = Mount("Cards", stage, new Vector2(1920, 1080));
        foreach (var entry in cards)
        {
            entry.mount = Mount("CardMount", cardLayer, cardSize); entry.mount.anchoredPosition = entry.position;
            entry.view = cardFactory.Request(entry.mount, entry.side, entry.id, false);
        }
        above = Mount("AboveCards", stage, new Vector2(1920, 1080));
        above.gameObject.AddComponent<RectMask2D>();
        foreach (var entry in cards)
        {
            while (entry.view != null && !entry.view.Ready && entry.view.Error == null) yield return null;
            if (token != generation) yield break;
            if (entry.view == null || !entry.view.Ready) { Fail(entry.view?.Error ?? "卡牌请求已失效。"); yield break; }
        }
        try
        {
            fullPreview.Bind(canvas, stage);
            LoadTablePicker();
            foreach (var entry in cards) fullPreview.RegisterCard(entry.side, entry.slot, entry.view.Appearance.Root);
        }
        catch (Exception failure) { Fail(failure.Message); yield break; }
        kind = initialEffect; SelectEffect(kind); loading = false;
    }
    private static RectTransform Mount(string name, RectTransform parent, Vector2 size)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f; rect.sizeDelta = size; return rect;
    }
    private static void Stretch(RectTransform rect)
    { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
    private void SelectEffect(BattleEffectKind next)
    {
        if(tableResource!=null)tableTuning[tableResource.Key]=tableResource.Copy();
        tableResource=null;
        if (parameters != null) tuning[kind] = parameters.Copy();
        fullPreview?.Stop(true);
        parameters = burningAreaParameters = null;
        ready = false; choosing = false; error = message = null; edits.Clear(); scroll = Vector2.zero;
        player.Stop(true); burningAreaPlayer.Stop(true); kind = next;
        if (kind == BattleEffectKind.DandelionWindOrbit) { targetCount = 1; manual = false; }
        if (IsKaeya)
        {
            targetCount = 1; manual = false;
            foreach (var entry in cards) if (entry.side == BattleSide.Ally && entry.id == 1010) { casterSlot = entry.slot; break; }
        }
        if (BattleEffectParameters.IsVenti(kind))
        {
            targetSide = kind == BattleEffectKind.VentiWindDomain ? BattleSide.Ally : BattleSide.Enemy;
            targetCount = kind == BattleEffectKind.VentiWindDomain ? 4 : kind == BattleEffectKind.VentiStormPulse ? 5 : 3;
            if (ventiPreviewFlip == null) ventiPreviewFlip = GetComponent<UICardAirborneFlip>() ?? gameObject.AddComponent<UICardAirborneFlip>();
            ventiPreviewFlip.AutomaticClock = false;
        }
        int owner = BattleEffectPreviewCatalog.Get(kind).OwnerID;
        foreach (var entry in cards) if (entry.side == BattleSide.Ally && entry.id == owner) { casterSlot = entry.slot; break; }
        try
        {
            parameters = PreviewParameters(kind);
            BindPlayer(player, kind);
            if (CompanionKind.HasValue)
            {
                burningAreaParameters = PreviewParameters(CompanionKind.Value);
                BindPlayer(burningAreaPlayer, CompanionKind.Value);
            }
            SetPaused(false); ready = true; Replay();
        }
        catch (Exception failure) { Fail(failure.Message); }
    }
    private BattleEffectParameters PreviewParameters(BattleEffectKind effect)
        => tuning.TryGetValue(effect, out var cached) ? cached.Copy() : BattleEffectParameters.Load(effect);
    private void BindPlayer(UIBattleEffectPlayer playback, BattleEffectKind effect)
    {
        BattleEffectAssets assets = null;
#if UNITY_EDITOR
        if (ResolveAssets != null) assets = ResolveAssets(effect);
#endif
        if (assets == null) throw new InvalidOperationException("该检查场景通过编辑器解析已发布的原生模型；缺少模型解析绑定。");
        if (!playback.Bind(stage, behind, above, effectShader, assets)) throw new InvalidOperationException(playback.Error);
        playback.automaticClock = false;
    }
    private void SelectTargets()
    {
        selected.Clear();
        if (!fullAnimation && (kind == BattleEffectKind.KaeyaGlacialWaltz || kind == BattleEffectKind.VentiStormEye))
        { if (Caster() != null) selected.Add(Caster()); return; }
        int available = 0;
        foreach (var entry in cards) if (entry.side == targetSide) available = Mathf.Max(available, entry.slot);
        int count = kind == BattleEffectKind.DandelionWindOrbit||tableResource?.Anchor==BattleTableEffectAnchor.Holder ? 1 : Mathf.Clamp(targetCount, 1, available), primary = Mathf.Clamp(mainSlot, 1, available);
        int first = Mathf.Min(available - count + 1, Mathf.Max(1, primary - (count - 1) / 2));
        foreach (var entry in cards)
            if (entry.side == targetSide && entry.view != null && entry.view.Ready &&
                (manual ? occupied[entry.slot - 1] : entry.slot >= first && entry.slot < first + count)) selected.Add(entry.view.Appearance.Root);
    }
    private RectTransform Caster()
    {
        foreach (var entry in cards) if (entry.side == (tableResource!=null&&tableResource.Category=="Enemy"?BattleSide.Enemy:BattleSide.Ally) && entry.slot == casterSlot && entry.view != null && entry.view.Ready) return entry.view.Appearance.Root;
        return null;
    }
    private void Replay(bool restartCompanion = true)
    {
        if (!ready) return;
        ventiPreviewFlip?.Cancel(-1); ventiFlipped = false;
        SelectTargets();
        if(tableResource!=null){ReplayTable();return;}
        if (fullAnimation)
        {
            if (!fullPreview.Play(kind, selected, Caster(), player, burningAreaPlayer, PreviewParameters, BindPlayer))
            { error = fullPreview.Error; loop = false; return; }
            if (!ApplyPreviewAbsorption()) return;
            error = null; SetPaused(paused); return;
        }
        IReadOnlyList<RectTransform> companionTargets = IsKaeya || kind == BattleEffectKind.VentiStormPulse ? new[] { Caster() } : (IReadOnlyList<RectTransform>)selected;
        if (CompanionKind.HasValue && (restartCompanion || !burningAreaPlayer.Playing)
            && !burningAreaPlayer.TryPlay(burningAreaParameters, companionTargets, Caster(), 12345))
        { player.Stop(true); error = burningAreaPlayer.Error; loop = false; return; }
        if (!player.TryPlay(parameters, selected, Caster(), 12345))
        { burningAreaPlayer.Stop(true); error = player.Error; loop = false; return; }
        if (kind == BattleEffectKind.KaeyaIcicle && selected.Count == 1 && burningAreaPlayer.TryCaptureKaeyaDeparture(selected[0], out var departure))
            player.SetKaeyaDeparture(departure);
        if (kind == BattleEffectKind.VentiStormPulse)
            player.SetVentiPhase(burningAreaPlayer.Elapsed * parameters["OrbitSeconds"] / burningAreaParameters["OrbitSeconds"]);
        if (!ApplyPreviewAbsorption()) return;
        error = null; SetPaused(paused);
    }
    private bool ApplyPreviewAbsorption()
    {
        if (!HasVentiAbsorption) return true;
        try
        {
            string element = AbsorptionElements[ventiAbsorption];
            Color? color = element == null ? (Color?)null : (previewPalette ??= ElementColorPalette.Load()).GetColor(element);
            player.SetVentiAbsorbedColor(color);
            if (CompanionKind == BattleEffectKind.VentiStormEye || CompanionKind == BattleEffectKind.VentiStormPulse)
                burningAreaPlayer.SetVentiAbsorbedColor(color);
            fullPreview.SetVentiAbsorption(element, color); return true;
        }
        catch (Exception failure) { StopEffects(true); error = "元素转化预览失败：" + failure.Message; return false; }
    }
    private void SetPaused(bool value)
    {
        paused = value; player.Paused = value; burningAreaPlayer.Paused = value;
        if (ventiPreviewFlip != null) ventiPreviewFlip.Paused = value;
        if (fullPreview != null) fullPreview.Paused = value;
    }
    private void StopEffects(bool immediate = false)
    {
        loop = false; SetPaused(false); player.Stop(immediate); burningAreaPlayer.Stop(immediate);
        ventiPreviewFlip?.Cancel(-1);
        fullPreview?.Stop(immediate);
    }
    private void SeekEffects(float seconds)
    {
        if (fullAnimation) { fullPreview.Seek(seconds); SetPaused(true); return; }
        SetPaused(true); player.Seek(seconds);
        if (CompanionKind.HasValue) burningAreaPlayer.Seek(seconds);
        ventiPreviewFlip?.Cancel(-1); ventiFlipped = false;
        if (kind == BattleEffectKind.VentiUpdraft && seconds >= parameters["ContactSeconds"])
        {
            StartVentiPreviewFlip(); ventiPreviewFlip.Paused = false;
            ventiPreviewFlip.Advance(seconds - parameters["ContactSeconds"]); ventiPreviewFlip.Paused = paused;
        }
    }
    private void AdvanceEffects(float seconds)
    {
        if (fullAnimation)
        {
            fullPreview.Advance(seconds);
            if (fullPreview.Error != null) { error = fullPreview.Error; StopEffects(true); }
            else if (loop && !fullPreview.Playing) Replay();
            return;
        }
        player.Advance(seconds);
        if (CompanionKind.HasValue) burningAreaPlayer.Advance(seconds);
        float flipStep = seconds;
        if (kind == BattleEffectKind.VentiUpdraft && !ventiFlipped && player.Elapsed >= parameters["ContactSeconds"])
        {
            StartVentiPreviewFlip();
            flipStep = Mathf.Min(seconds, player.Elapsed - parameters["ContactSeconds"]);
        }
        ventiPreviewFlip?.Advance(flipStep);
        string failure = player.Error ?? (CompanionKind.HasValue ? burningAreaPlayer.Error : null);
        if (failure != null) { error = failure; StopEffects(true); }
        else if (loop && !player.Playing) Replay(false);
    }
    private void StartVentiPreviewFlip()
    {
        ventiFlipped = true;
        foreach (var entry in cards)
            if (entry.view != null && entry.view.Ready && selected.Contains(entry.view.Appearance.Root))
                ventiPreviewFlip.Play(0, entry.view, parameters["FlipSeconds"], parameters["LiftHeightRatio"], null);
    }
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) { if (choosing) { choosing = false; releasePickerFocus = true; } else Exit(); return; }
        if (!ready) return;
        if (!choosing)
        {
            if (Input.GetKeyDown(KeyCode.Space)) Replay();
            if (Input.GetKeyDown(KeyCode.RightArrow)) { mainSlot = mainSlot % 5 + 1; Replay(); }
            if (Input.GetKeyDown(KeyCode.LeftArrow)) { mainSlot = (mainSlot + 3) % 5 + 1; Replay(); }
        }
        if (!paused)
        {
            AdvanceEffects(BattlePause.DeltaTime * speed);
        }
    }
    public bool ReloadConfiguration()
    {
        if (!ready || loading || !isActiveAndEnabled) return false;
        if(tableResource!=null)return ReloadTable();
        try
        {
            var next = BattleEffectParameters.Load(kind);
            var nextBurningArea = CompanionKind.HasValue ? BattleEffectParameters.Load(CompanionKind.Value) : null;
            parameters = next; burningAreaParameters = nextBurningArea;
            fullPreview.ReloadConfiguration();
            tuning[kind] = parameters.Copy();
            if (burningAreaParameters != null) tuning[CompanionKind.Value] = burningAreaParameters.Copy();
            previewPalette = null;
            edits.Clear(); Replay();
            message = "已重读 Data/UE/" + BattleEffectParameters.FileName(kind)
                + (burningAreaParameters != null ? " 与 " + BattleEffectParameters.FileName(CompanionKind.Value) : "");
            return true;
        }
        catch (Exception failure) { message = "重读失败，保留当前参数：" + failure.Message; return false; }
    }
    private void Save()
    {
        if(tableResource!=null){SaveTable();return;}
        try
        {
            foreach (var field in BattleEffectParameters.Fields(kind))
                if (edits.TryGetValue(field.Key, out string text) &&
                    (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ||
                     float.IsNaN(value) || float.IsInfinity(value) || value < field.Min || value > field.Max ||
                     (field.Unit == "count" && value != Mathf.Round(value))))
                    throw new InvalidOperationException(field.Label + "：请填写范围内的有效数值。");
#if UNITY_EDITOR
            if (WriteParameters == null) throw new InvalidOperationException("缺少公共参数写回入口。");
            message = WriteParameters(kind, parameters.Values());
            parameters = BattleEffectParameters.Load(kind); tuning[kind] = parameters.Copy(); edits.Clear(); player.SetParameters(parameters); Replay();
#endif
        }
        catch (Exception failure) { message = "写回失败：" + failure.Message; }
    }
    private void Fail(string text) { ready = false; loading = false; error = text; Debug.LogError("特效检查：" + text, this); }
    private void DrawParameters()
    {
        if(tableResource!=null){DrawTableParameters();return;}
        var fields = BattleEffectParameters.Fields(kind); var values = parameters.Values(); bool changed = false;
        scroll = GUI.BeginScrollView(new Rect(34, 360, 430, 500), scroll, new Rect(0, 0, 404, fields.Count * 72));
        for (int i = 0; i < fields.Count; i++)
        {
            var p = fields[i]; float y = i * 72, value = values[p.Key];
            GUI.Label(new Rect(5, y, 285, 30), p.Label, label);
            string before = edits.TryGetValue(p.Key, out string editing) ? editing : value.ToString("0.###", CultureInfo.InvariantCulture);
            string after = GUI.TextField(new Rect(292, y, 105, 30), before, field);
            if (before != after)
            {
                edits[p.Key] = after;
                if (float.TryParse(after, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) && !float.IsNaN(parsed) && !float.IsInfinity(parsed))
                { value = Mathf.Clamp(parsed, p.Min, p.Max); if (p.Unit == "count") value = Mathf.Round(value); values[p.Key] = value; changed = true; }
            }
            float slider = GUI.HorizontalSlider(new Rect(5, y + 37, 392, 24), value, p.Min, p.Max);
            if (p.Unit == "count") slider = Mathf.Round(slider);
            if (!Mathf.Approximately(slider, value)) { values[p.Key] = slider; edits.Remove(p.Key); changed = true; }
        }
        GUI.EndScrollView();
        if (changed)
        {
            try
            {
                parameters = BattleEffectParameters.Parse(kind, values); tuning[kind] = parameters.Copy();
                if (fullAnimation)
                {
                    float time = fullPreview.Elapsed; bool hold = paused;
                    Replay(); if (error == null) fullPreview.Seek(time); SetPaused(hold);
                }
                else { player.SetParameters(parameters); player.Seek(player.Elapsed); }
            }
            catch (Exception failure) { message = failure.Message; }
        }
    }
    private void OnGUI()
    {
        if (controlsFont == null) return;
        if (label == null)
        {
            label = new GUIStyle(GUI.skin.label) { font = controlsFont, fontSize = 20, fontStyle = FontStyle.Normal, wordWrap = true };
            button = new GUIStyle(GUI.skin.button) { font = controlsFont, fontSize = 20, fontStyle = FontStyle.Normal };
            heading = new GUIStyle(label) { fontSize = 27 };
            field = new GUIStyle(GUI.skin.textField) { font = controlsFont, fontSize = 18, fontStyle = FontStyle.Normal };
            toggle = new GUIStyle(GUI.skin.toggle) { font = controlsFont, fontSize = 20, fontStyle = FontStyle.Normal };
        }
        Matrix4x4 previous = GUI.matrix; bool enabled = GUI.enabled;
        GUI.matrix = ProjectUiCanvas.GetGuiMatrix(canvas);
        if (releasePickerFocus) { GUI.FocusControl(null); releasePickerFocus = false; }
        GUI.Box(new Rect(24, 24, 460, 1032), GUIContent.none);
        GUI.Label(new Rect(44, 40, 410, 42), "3D 特效检查", heading);
        GUI.enabled = !loading;
        if (GUI.Button(new Rect(44, 96, 410, 44), (tableResource!=null?tableResource.OwnerName+" · "+tableResource.Name:BattleEffectParameters.Label(kind)) + "  ▾", button))
        {
            choosing = !choosing;
            if (choosing) effectPicker.Open(CurrentTableKey); else GUI.FocusControl(null);
        }
        GUI.enabled = ready && !choosing;
        if (GUI.Button(new Rect(44, 154, 195, 40), "播放 / 重播 · 空格", button)) Replay();
        if (GUI.Button(new Rect(254, 154, 200, 40), "停止并淡出", button)) StopEffects();
        if (GUI.Button(new Rect(44, 206, 195, 40), paused ? "继续" : "暂停", button)) SetPaused(!paused);
        if (GUI.Button(new Rect(254, 206, 200, 40), "立即清空", button)) StopEffects(true);
        if (GUI.Button(new Rect(44, 258, 195, 40), "速度 " + speed.ToString("0.##") + "×", button)) speed = speed == 1 ? .25f : 1;
        if (GUI.Button(new Rect(254, 258, 200, 40), loop ? "循环：开" : "循环：关", button)) loop = !loop;
        if (ready)
        {
            float maximum = Mathf.Max(.1f, fullAnimation ? fullPreview.Duration : player.Duration);
            float current = Mathf.Clamp(fullAnimation ? fullPreview.Elapsed : player.Elapsed, 0, maximum);
            float seek = GUI.HorizontalSlider(new Rect(44, 320, 410, 25), current, 0, maximum);
            if (Mathf.Abs(seek - current) > .015f) SeekEffects(seek);
            if (!choosing) DrawParameters();
        }
        GUI.enabled = !loading && !choosing;
        if (GUI.Button(new Rect(44, 880, 410, 40), "重读已发布模型", button))
        {
            try { if(tableResource!=null)SelectTable(tableResource.Key);else{fullPreview.ReloadModels(BindPlayer); SelectEffect(kind);} }
            catch (Exception failure) { message = "模型重读失败：" + failure.Message; }
        }
        GUI.enabled = ready && !choosing;
        if (GUI.Button(new Rect(44, 932, 195, 40), "重读配表", button)) ReloadConfiguration();
#if UNITY_EDITOR
        if (GUI.Button(new Rect(254, 932, 200, 40), "写回表并发布", button)) Save();
#endif
        GUI.enabled = enabled;
        GUI.Label(new Rect(44, 988, 410, 66), error ?? message ?? (ready ? "Charts/UE/" + (tableResource!=null?BattleTableEffectCatalog.FileName:BattleEffectParameters.FileName(kind)) + "\n目标、人数、播放与视图缩放不写回。" : "正在加载卡牌与模型…"), label);
        GUI.Label(new Rect(530, 32, 1300, 38), "目标输入仅供检查；正式调用接收实际技能目标。多人中心取卡牌范围中线。", label);
        GUI.enabled = ready && !choosing;
        if (GUI.Button(new Rect(530, 88, 150, 40), targetSide == BattleSide.Enemy ? "目标：敌方" : "目标：我方", button))
        { targetSide = targetSide == BattleSide.Enemy ? BattleSide.Ally : BattleSide.Enemy; Replay(); }
        GUI.Label(new Rect(700, 92, 100, 35), "人数", label);
        GUI.enabled = ready && !choosing && !IsKaeya && kind != BattleEffectKind.DandelionWindOrbit;
        int nextCount = GUI.SelectionGrid(new Rect(790, 88, 300, 40), targetCount - 1, new[] { "1", "2", "3", "4", "5" }, 5, button) + 1;
        if (nextCount != targetCount) { targetCount = nextCount; manual = false; Replay(); }
        GUI.enabled = ready && !choosing;
        if (GUI.Button(new Rect(1110, 88, 190, 40), "基准卡位 " + mainSlot, button)) { mainSlot = mainSlot % 5 + 1; Replay(); }
        GUI.enabled = ready && !choosing && kind != BattleEffectKind.DandelionWindOrbit;
        if (GUI.Button(new Rect(1320, 88, 190, 40), "施放者 " + casterSlot, button)) { casterSlot = casterSlot % (tableResource?.Category=="Enemy"?5:4) + 1; Replay(); }
        GUI.enabled = ready && !choosing && !IsKaeya && kind != BattleEffectKind.DandelionWindOrbit;
        bool nextManual = GUI.Toggle(new Rect(530, 145, 190, 35), manual, "逐卡指定目标", label);
        if (nextManual != manual) { manual = nextManual; Replay(); }
        for (int i = 0; i < 5; i++)
        {
            bool next = GUI.Toggle(new Rect(750 + i * 135, 145, 125, 35), occupied[i], "卡位 " + (i + 1), label);
            if (next != occupied[i]) { occupied[i] = next; if (manual) Replay(); }
        }
        if (kind == BattleEffectKind.AmberArrowRain)
            GUI.Label(new Rect(530, 195, 1300, 50), "箭雨与持续燃烧区同时显示；箭雨结束后火场保留。火场参数可在下拉菜单中单独调整。", label);
        GUI.enabled = enabled;
        if (kind == BattleEffectKind.DandelionWindOrbit)
            GUI.Label(new Rect(530, 195, 1300, 50), "基准卡位模拟状态持有者，可切敌我卡牌；SeedSize调大小、SeedGlow调微光。正式按状态ID与真实实例绑定。", label);
        if (IsKaeya)
            GUI.Label(new Rect(530, 195, 1300, 50), kind == BattleEffectKind.KaeyaIcicle
                ? "环绕状态持续保留；每次触发沿当前切线发射至一个实际目标，不重置环绕。环绕参数在状态页调整。"
                : "环绕绑定施放者卡牌；数量3为普通爆发、4为六命。停止按钮使用公共淡出。", label);
        GUI.enabled = ready && !choosing && tableResource==null;
        bool nextFull = GUI.Toggle(new Rect(530, 250, 345, 34), fullAnimation,
            new GUIContent("按完整动画展示", "读取攻击表时序，包含动作、受击与预览跳字；不执行战斗结算。"), toggle);
        if (nextFull != fullAnimation)
        { fullPreview.Stop(true); fullAnimation = nextFull; SelectEffect(kind); }
        if(tableResource!=null)DrawTableVariables();
        if (fullAnimation)
        {
            GUI.Label(new Rect(885, 250, 100, 32), "预览伤害", label);
            previewAmount = GUI.TextField(new Rect(990, 250, 115, 32), previewAmount, field);
            if (float.TryParse(previewAmount, NumberStyles.Float, CultureInfo.InvariantCulture, out float amount)
                && !float.IsNaN(amount) && !float.IsInfinity(amount) && amount > 0) fullPreview.Amount = amount;
            fullPreview.Critical = GUI.Toggle(new Rect(1125, 250, 100, 32), fullPreview.Critical, "暴击", toggle);
            GUI.Label(new Rect(1230, 250, 650, 32), fullPreview.EffectID, label);
        }
        if (HasVentiAbsorption)
        {
            GUI.Label(new Rect(530, 292, 110, 32), "元素转化", label);
            int nextAbsorption = GUI.SelectionGrid(new Rect(650, 292, 550, 32), ventiAbsorption, AbsorptionLabels, 5, button);
            if (nextAbsorption != ventiAbsorption) { ventiAbsorption = nextAbsorption; Replay(); }
            if (fullAnimation && ventiAbsorption != 0)
            {
                GUI.Label(new Rect(1230, 292, 110, 32), "转化伤害", label);
                absorbedAmount = GUI.TextField(new Rect(1350, 292, 115, 32), absorbedAmount, field);
                if (float.TryParse(absorbedAmount, NumberStyles.Float, CultureInfo.InvariantCulture, out float amount)
                    && !float.IsNaN(amount) && !float.IsInfinity(amount) && amount > 0) fullPreview.AbsorbedAmount = amount;
            }
        }
        GUI.enabled = enabled;
        // Existing parameter scroll views get first refusal; zoom only consumes unhandled wheel input over the picture.
        if (ready && !choosing && stageViewport != null && Event.current.type == EventType.ScrollWheel)
        {
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            if (RectTransformUtility.RectangleContainsScreenPoint(stageViewport, Input.mousePosition, camera))
            {
                viewportZoom.ZoomAt(Input.mousePosition, camera, -Event.current.delta.y / 3f);
                Event.current.Use();
            }
        }
        if (choosing)
        {
            if (effectPicker.Draw(new Rect(44, 142, 740, 774), controlsFont, CurrentTableKey, out var choice)) SelectTable(choice);
            else if (effectPicker.CloseRequested) choosing = false;
        }
        GUI.enabled = ready && !choosing;
        if (GUI.Button(new Rect(530, 990, 230, 44), "还原视图 · " + Mathf.RoundToInt(viewportZoom.Factor * 100) + "%", button)) viewportZoom.Reset();
        GUI.Label(new Rect(780, 997, 800, 32), "画面内滚轮缩放，以鼠标位置为中心", label);
        GUI.enabled = enabled;
        if (GUI.Button(new Rect(1620, 990, 250, 44), "退出检查 · Esc", button)) Exit();
        GUI.enabled = enabled; GUI.matrix = previous;
    }
    private void Exit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        UnityEngine.SceneManagement.SceneManager.LoadScene(0);
#endif
    }
    private void OnDisable()
    {
        fullPreview?.Stop(true);
        ventiPreviewFlip?.Cancel(-1);
        generation++; StopAllCoroutines(); ready = loading = choosing = false;
        if (player != null) player.Stop(true);
        if (burningAreaPlayer != null) burningAreaPlayer.Stop(true);
        if (cards != null) foreach (var entry in cards)
        { if (entry == null) continue; if (cardFactory != null) cardFactory.Release(entry.view); entry.view = null; entry.mount = null; }
        stageView.Dispose();
        if (canvas != null) Destroy(canvas.gameObject); canvas = null;
        stageViewport = stage = behind = above = null;
        tuning.Clear(); edits.Clear(); parameters = burningAreaParameters = null;
        tableTuning.Clear();tableResource=null;tableCatalog=null;
        previewPalette = null;
    }
}
