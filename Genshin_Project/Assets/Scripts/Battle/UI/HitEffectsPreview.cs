using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Display-only sandbox: copies BattleField1 card layout, never creates battle actors or publishes damage.</summary>
public sealed class HitEffectsPreview : MonoBehaviour
{
    public Font controlsFont;
    public TMP_FontAsset numberFont;
    public int[] characterIds = { 1009, 1010, 1011, 1012 };
    public int enemyId = 2001;
    // Same anchors, reference resolution, card bounds and sibling ordering as BattleField1. No battle HUD is instantiated.
    public Vector2 allyGroupOffset = new Vector2(-482.5f, 33.5f);
    public Vector2 enemyGroupOffset = new Vector2(509.5f, 33);
    public Vector2[] allyPositions = { new Vector2(-270, -101.5f), new Vector2(-107, 101.5f), new Vector2(59, -101.5f), new Vector2(222, 101.5f) };
    public Vector2[] enemyPositions = { new Vector2(329, -102), new Vector2(174, 102), new Vector2(0, -102), new Vector2(-155, 102), new Vector2(-329, -101) };
    public Vector2 cardSize = new Vector2(197, 338);
    public int fixedSeed = 12345;
    public bool newSeedEachPlay = true, autoReplay;
    public bool startWithShields = true;
    public bool animatedFaces = true;
    private UICardFactory cardFactory;
    private readonly UICardView[] cardViews = new UICardView[9];
    private ShieldPreviewPanel shieldPanel;
    private BattleUiLayout sceneLayout;
    private BattleUiHitEffects hitModule;
    private int previewTab;

    private static readonly string[] Elements = { "Physical", "Pyro", "Hydro", "Electro", "Cryo", "Anemo", "Geo", "Dendro" };
    private static readonly string[] ElementLabels = { "物理", "火", "水", "雷", "冰", "风", "岩", "草" };
    private static readonly string[] WeaponLabels = { "单手剑", "双手剑", "弓", "长柄", "法器" };
    private static readonly string[] TargetLabels = { "敌方全体", "敌1", "敌2", "敌3", "敌4", "敌5", "我方全体", "我1", "我2", "我3", "我4", "全部九人", "敌2～4" };
    private readonly RectTransform[] cards = new RectTransform[9], front = new RectTransform[9], behind = new RectTransform[9];
    private readonly List<Cue> cues = new List<Cue>();
    private readonly List<VisualCue> visualCues = new List<VisualCue>();
    private readonly List<Flight> flights = new List<Flight>();
    private readonly List<Popup> popups = new List<Popup>();
    private UIHitEffectStyle effects;
    private HitEffectTextureStyle textures;
    private HitEffectTextureStyle.Profile playingTextures;
    private HitTextureLibrary textureLibrary;
    private HitTextureLibrary pendingTextureLibrary;
    private ElementColorPalette palette;
    private BattlefieldDamagePopupStyle popupStyle;
    private BattlePresentationConfiguration timing;
    private BattleSkillPresentationTemplates templates;
    private bool useGenericTemplate = true;
    private float playingFlightDuration;
    private HitShakeStyle shakeStyle;
    private UIHitShakeController shake;
    private HitShakeStyle.Selection playingShake;
    private UIHitEffectStyle.Profile playingProfile;
    private UIHitEffectStyle.Particles playingParticles;
    private BattlefieldDamagePopupStyle playingPopup;
    private Color playingColor;
    private RectTransform popupLayer, flightLayer, groupBehind, groupFront;
    private bool ready, running, paused, playingNumbers, showNumbers = true, fullTimeRandom = true;
    private int weapon, attack, element, target, nextCue, nextFlight, playIndex, playingSeed, source, playingSource;
    private int playbackMode, nextVisual;
    private bool playingGrouped;
    private float elapsed, endTime;
    private string hitsText = "3", critText = "50", amountText = "1234", error, inputError;
    private int playingHits;
    private bool cardShake = true, screenShake;
    private struct Cue { public float Time, DirectionDegrees; public int Hit, Target; public bool Critical; public uint Seed; public float Amount; }
    private sealed class VisualCue { public Cue Cue; public bool Grouped; public Vector2 Center, Span; }
    private sealed class Flight { public UIHitProjectileEffect Graphic; public float Start; }
    private sealed class Popup { public TextMeshProUGUI Text; public Vector2 Origin; public float Start; public Color Color; }

    private IEnumerator Start()
    {
        if (!ReadSettings()) yield break;
        if (characterIds == null || characterIds.Length != 4 || allyPositions.Length != 4 || enemyPositions.Length != 5 ||
            Array.Exists(characterIds, id => id <= 0) || enemyId <= 0)
        { Fail("预览必须配置四名我方、五名敌方。"); yield break; }
        textureLibrary = new HitTextureLibrary();
        yield return textureLibrary.Load(textures, Fail);
        if (error != null) yield break;
        cardFactory = gameObject.AddComponent<UICardFactory>();
        yield return Build();
        if (error != null) yield break;
        ready = true; previewTab = startWithShields ? 1 : 0;
        if (!startWithShields) Replay();
    }
    private bool ReadSettings()
    {
        try
        {
            // Parse all first: reloading an invalid table must not partially replace the active configuration.
            var newEffects = UIHitEffectStyle.Load(); var newPalette = ElementColorPalette.Load();
            var newShake = HitShakeStyle.Load();
            var newTextures = HitEffectTextureStyle.Load();
            var newPopup = BattlefieldDamagePopupStyle.Load();
            var newTemplates = BattleSkillPresentationTemplates.Load();
            var newTiming = BattlePresentationConfiguration.Parse(BattlefieldDamagePopupStyle.ReadParameters(
                Path.Combine(Application.streamingAssetsPath, "Data", "UI", BattlefieldDamagePopupStyle.FileName)));
            effects = newEffects; textures = newTextures; palette = newPalette; popupStyle = newPopup; timing = newTiming; templates = newTemplates; shakeStyle = newShake; error = null; return true;
        }
        catch (Exception exception) { Fail(exception.Message); return false; }
    }
    private IEnumerator Build()
    {
        var cameraObject = new GameObject("HitPreviewCamera", typeof(Camera)); cameraObject.transform.SetParent(transform, false);
        var camera = cameraObject.GetComponent<Camera>(); camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.16f, .15f, .14f); camera.cullingMask = 0;
        var canvasObject = new GameObject("HitPreviewCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvasObject.transform.SetParent(transform, false); canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasObject.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
        groupBehind = Rect("GroupImpactsBehind", canvasObject.transform, new Vector2(1920, 1080));
        var allies = Rect("character", canvasObject.transform, new Vector2(737, 541));
        allies.anchorMin = allies.anchorMax = new Vector2(1, .5f); allies.anchoredPosition = allyGroupOffset;
        var enemies = Rect("Enemy", canvasObject.transform, new Vector2(855, 542));
        enemies.anchorMin = enemies.anchorMax = new Vector2(0, .5f); enemies.anchoredPosition = enemyGroupOffset;
        foreach (int index in new[] { 3, 1, 2, 0 }) BuildCard(index, allies, allyPositions[index], characterIds[index]);
        foreach (int index in new[] { 1, 3, 4, 2, 0 }) BuildCard(index + 4, enemies, enemyPositions[index], enemyId);
        for (int i = 0; i < cardViews.Length; i++)
        {
            var view = cardViews[i];
            while (!view.Ready && view.Error == null) yield return null;
            if (view.Error != null) { Fail(view.Error); yield break; }
            behind[i] = view.Appearance.BackEffects; front[i] = view.Appearance.FrontEffects;
        }
        groupFront = Rect("GroupImpactsFront", canvasObject.transform, new Vector2(1920, 1080));
        flightLayer = Rect("ProjectileLayer", canvasObject.transform, new Vector2(1920, 1080));
        popupLayer = Rect("IndependentDamagePopups", canvasObject.transform, new Vector2(1920, 1080));
        sceneLayout = gameObject.AddComponent<BattleUiLayout>();
        sceneLayout.canvas = canvasObject.GetComponent<Canvas>();
        sceneLayout.groupHitsBehind = groupBehind; sceneLayout.groupHitsFront = groupFront; sceneLayout.projectiles = flightLayer;
        sceneLayout.cards = new BattleUiCardBinding[cards.Length];
        for (int i = 0; i < cards.Length; i++)
            sceneLayout.cards[i] = new BattleUiCardBinding { side = i < 4 ? BattleSide.Ally : BattleSide.Enemy,
                slot = i < 4 ? i + 1 : i - 3, root = cards[i], art = cards[i],
                hitsBehind = behind[i], hitsFront = front[i], shieldLayer = cardViews[i].Appearance.ShieldLayer };
        hitModule = gameObject.AddComponent<BattleUiHitEffects>();
        hitModule.layout = sceneLayout; hitModule.loadOnStart = false; hitModule.automaticClock = false;
        shake = gameObject.AddComponent<UIHitShakeController>(); shake.AutomaticClock = false;
        shake.BindScreen(canvasObject.transform as RectTransform);
        foreach (var card in cards) shake.RegisterCard(card);
        var shields = gameObject.AddComponent<BattleUiShields>();
        shields.layout = sceneLayout; shields.loadOnStart = false; shields.automaticClock = false;
        shieldPanel = gameObject.AddComponent<ShieldPreviewPanel>();
        shieldPanel.Bind(shields, numberFont);
    }
    private void BuildCard(int index, RectTransform parent, Vector2 position, int id)
    {
        var card = Rect((index < 4 ? "CharacterCard" + (index + 1) : "EnemyCard" + (index - 3)), parent, cardSize);
        card.anchoredPosition = position; cards[index] = card;
        cardViews[index] = cardFactory.Request(card, index < 4 ? BattleSide.Ally : BattleSide.Enemy, id, animatedFaces);
    }
    private static RectTransform Rect(string name, Transform parent, Vector2 size)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f; rect.sizeDelta = size; return rect;
    }
    private bool TryInputs(out int hits, out float rate, out float amount)
    {
        hits = 0; rate = amount = 0;
        if (!int.TryParse(hitsText, out hits) || hits < 1 || hits > 100) inputError = "hit 数请输入 1～100 的整数。";
        else if (!float.TryParse(critText, NumberStyles.Float, CultureInfo.InvariantCulture, out rate) || float.IsNaN(rate) || rate < 0 || rate > 100)
            inputError = "暴击率请输入 0～100（百分比）。";
        else if (!float.TryParse(amountText, NumberStyles.Float, CultureInfo.InvariantCulture, out amount) || float.IsNaN(amount) || float.IsInfinity(amount) || amount < 0)
            inputError = "示例伤害请输入非负有限数字。";
        else { inputError = null; rate /= 100; return true; }
        return false;
    }
    private IEnumerable<int> Targets()
    {
        if (target == 0) { for (int i = 4; i < 9; i++) yield return i; }
        else if (target >= 1 && target <= 5) yield return target + 3;
        else if (target == 6) { for (int i = 0; i < 4; i++) yield return i; }
        else if (target >= 7 && target <= 10) yield return target - 7;
        else if (target == 12) { for (int i = 5; i <= 7; i++) yield return i; }
        else { for (int i = 0; i < 9; i++) yield return i; }
    }
    private void Replay()
    {
        if (!ready || error != null || !TryInputs(out int count, out float rate, out float amount)) return;
        Stop();
        playingHits = count; playingNumbers = showNumbers; playingProfile = effects.Get((HitEffectWeapon)weapon, attack == 1);
        playingTextures = textures.Get((HitEffectWeapon)weapon, attack == 1); playingSource = source;
        playingGrouped = playbackMode == 2 || (playbackMode == 0 && playingTextures.Mode == HitEffectTextureStyle.PlaybackMode.PerHitGroup);
        playingShake = shakeStyle.Resolve(attack == 1 ? "Heavy" : "Normal", "", -1);
        if (!cardShake) playingShake.Card = null;
        playingShake.Screen = screenShake ? shakeStyle.Get("ScreenDefault") : null;
        playingColor = palette.GetColor(Elements[element]); playingParticles = effects.GetParticles(Elements[element]); playingPopup = popupStyle;
        playingSeed = newSeedEachPlay ? unchecked((int)HitEffectMath.Mix((uint)fixedSeed ^ (uint)++playIndex)) : fixedSeed;
        float jitter = fullTimeRandom ? (float)timing.FullJitter : 0;
        var template = useGenericTemplate ? templates.Get(weapon, attack == 1 ? "Heavy" : "Normal") : null;
        playingFlightDuration = template != null ? (float)template.ProjectileSeconds : playingTextures.Number("FlightSeconds");
        var targetTimes = new float[9];
        var selectedTargets = new List<int>(Targets());
        for (int hit = 0; hit < count; hit++)
            foreach (int index in selectedTargets)
            {
                uint seed = HitEffectMath.CueSeed(playingSeed, hit, index);
                bool critical = HitEffectMath.Critical(seed, rate);
                float time = template != null ? (float)template.HitTime(hit)
                    : playingFlightDuration + HitEffectMath.CueTime(seed, hit, (float)timing.HitInterval, jitter, targetTimes[index]);
                targetTimes[index] = time - playingFlightDuration;
                cues.Add(new Cue { Hit = hit, Target = index, Seed = seed, Critical = critical,
                    Amount = amount * (critical ? 2 : 1),
                    Time = time });
            }
        cues.Sort((a, b) => { int time = a.Time.CompareTo(b.Time); if (time != 0) return time;
            int hit = a.Hit.CompareTo(b.Hit); return hit != 0 ? hit : a.Target.CompareTo(b.Target); });
        // Replay is one action. Deduplicate only by hit within this action, never across subsequent replays.
        // Visuals use the earliest target cue; independent per-target timing and damage numbers stay intact.
        var seenHits = new HashSet<int>();
        Vector2 min = Vector2.positiveInfinity, max = Vector2.negativeInfinity;
        var corners = new Vector3[4];
        foreach (int index in selectedTargets)
        {
            cards[index].GetWorldCorners(corners);
            foreach (Vector3 corner in corners)
            {
                Vector2 point = groupFront.InverseTransformPoint(corner);
                min = Vector2.Min(min, point); max = Vector2.Max(max, point);
            }
        }
        foreach (var cue in cues)
        {
            if (playingGrouped && !seenHits.Add(cue.Hit)) continue;
            var visual = new VisualCue { Cue = cue, Grouped = playingGrouped && selectedTargets.Count > 1,
                Center = (min + max) * .5f, Span = max - min };
            if (visual.Grouped)
            {
                var sample = visual.Cue; sample.Seed = HitEffectMath.CueSeed(playingSeed, cue.Hit, -1); visual.Cue = sample;
            }
            visualCues.Add(visual);
        }
        float tail = Mathf.Max(playingTextures.Duration(playingProfile), playingNumbers ? playingPopup.Lifetime : 0);
        tail = Mathf.Max(tail, Mathf.Max(playingShake.Card?.Duration ?? 0, playingShake.Screen?.Duration ?? 0));
        endTime = cues[cues.Count - 1].Time + tail;
        running = true; paused = false;
    }
    private void Stop()
    {
        running = paused = false; elapsed = 0; nextCue = nextFlight = nextVisual = 0; cues.Clear(); visualCues.Clear();
        if (shake != null) shake.Cancel(-1);
        hitModule?.Clear();
        foreach (var flight in flights) if (flight.Graphic != null) Destroy(flight.Graphic.gameObject);
        foreach (var popup in popups) if (popup.Text != null) Destroy(popup.Text.gameObject);
        flights.Clear(); popups.Clear();
    }
    private void Spawn(Cue cue)
    {
        if (shieldPanel != null) shieldPanel.NotifyHit(cue.Target);
        shake.PulseCard(playIndex, cards[cue.Target], playingShake.Card, cue.Seed);
        shake.PulseScreen(playIndex, 0, playIndex, "Preview", cue.Hit, playingShake.Screen,
            HitEffectMath.CueSeed(playingSeed, cue.Hit, 0));
        if (!playingNumbers || !playingPopup.Enabled || (cue.Amount == 0 && !playingPopup.ShowZero)) return;
        if (popups.Count >= playingPopup.MaxVisible) { Destroy(popups[0].Text.gameObject); popups.RemoveAt(0); }
        Vector2 point = BattlefieldDamagePopupLayout.Sample(playingPopup, playingSeed, 1, cue.Hit, cue.Target);
        Vector3 world = cards[cue.Target].TransformPoint(Vector2.Scale(point - Vector2.one * .5f, cardSize));
        Vector2 origin = popupLayer.InverseTransformPoint(world);
        var rect = Rect("Damage_H" + (cue.Hit + 1), popupLayer, new Vector2(cardSize.x * 3, cardSize.y * .5f));
        rect.anchoredPosition = origin; rect.gameObject.AddComponent<CanvasRenderer>();
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>(); text.raycastTarget = false;
        text.font = numberFont != null ? numberFont : TMP_Settings.defaultFontAsset;
        text.text = BattlefieldDamagePopupLayout.Format(cue.Amount, playingPopup.Decimals);
        text.fontSize = cardSize.y * playingPopup.FontHeightRatio * playingPopup.FontSizeMultiplier(cue.Critical);
        DamagePopupOutline.Apply(text, playingPopup);
        text.alignment = TextAlignmentOptions.Center; text.enableWordWrapping = false; text.overflowMode = TextOverflowModes.Overflow;
        Color color = playingColor; color.a = playingPopup.Color.a; text.color = color;
        popups.Add(new Popup { Text = text, Origin = origin, Start = cue.Time, Color = color });
    }
    private void SpawnVisual(VisualCue visual)
    {
        if (!playingProfile.Enabled) return;
        Cue cue = visual.Cue;
        bool drawBehind = playingProfile.Text("DrawLayer") == "Behind";
        var parent = visual.Grouped ? (drawBehind ? groupBehind : groupFront) : (drawBehind ? behind[cue.Target] : front[cue.Target]);
        hitModule.PlayResolved(parent, textureLibrary, playingProfile, playingParticles, playingTextures,
            playingColor, cardSize, cue.Seed, cue.Hit, playingHits, cue.DirectionDegrees, cue.Time,
            visual.Grouped ? (Vector2?)visual.Center : null, visual.Grouped ? (Vector2?)visual.Span : null);
    }
    private void Update()
    {
        if (!ready) return;
        if (Input.GetKeyDown(KeyCode.R)) Replay();
        if (Input.GetKeyDown(KeyCode.Space) && running) paused = !paused;
        if (!running || paused) return;
        elapsed += Time.unscaledDeltaTime;
        float flightDuration = playingFlightDuration;
        while (nextFlight < visualCues.Count && visualCues[nextFlight].Cue.Time - flightDuration <= elapsed)
        {
            var visual = visualCues[nextFlight++]; var cue = visual.Cue;
            if (!playingProfile.Enabled || flightDuration <= 0 || elapsed >= cue.Time) continue;
            var graphic = visual.Grouped
                ? UIHitProjectileEffect.Create(flightLayer, cards[playingSource], visual.Center, playingTextures, textureLibrary, playingColor, flightDuration)
                : UIHitProjectileEffect.Create(flightLayer, cards[playingSource], cards[cue.Target],
                    HitEffectMath.Center(playingProfile, cue.Seed), playingTextures, textureLibrary, playingColor, flightDuration);
            if (graphic != null)
            {
                cue.DirectionDegrees = graphic.HeadingDegrees; visual.Cue = cue;
                flights.Add(new Flight { Graphic = graphic, Start = cue.Time - flightDuration });
            }
        }
        while (nextVisual < visualCues.Count && visualCues[nextVisual].Cue.Time <= elapsed) SpawnVisual(visualCues[nextVisual++]);
        while (nextCue < cues.Count && cues[nextCue].Time <= elapsed) Spawn(cues[nextCue++]);
        shake.Advance(Time.unscaledDeltaTime);
        for (int i = flights.Count - 1; i >= 0; i--)
        {
            var flight = flights[i]; flight.Graphic.SetElapsed(elapsed - flight.Start);
            if (flight.Graphic.Completed) { Destroy(flight.Graphic.gameObject); flights.RemoveAt(i); }
        }
        hitModule.SetTime(elapsed);
        for (int i = popups.Count - 1; i >= 0; i--)
        {
            var popup = popups[i]; float age = elapsed - popup.Start;
            if (age >= playingPopup.Lifetime) { Destroy(popup.Text.gameObject); popups.RemoveAt(i); continue; }
            popup.Text.rectTransform.anchoredPosition = popup.Origin + Vector2.up * (cardSize.y * playingPopup.RiseHeightRatio * playingPopup.RiseProgress(age));
            Color color = popup.Color; color.a *= playingPopup.Alpha(age); popup.Text.color = color;
        }
        if (elapsed >= endTime + .4f) { running = false; if (autoReplay) Replay(); }
    }
    private void OnGUI()
    {
        var oldMatrix = GUI.matrix;
        float scale = Mathf.Min(Screen.width / 1920f, Screen.height / 1080f);
        GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - 1920 * scale) * .5f, 0), Quaternion.identity, Vector3.one * scale);
        var skin = GUI.skin; var savedFont = skin.font; if (controlsFont != null) skin.font = controlsFont;
        var label = new GUIStyle(skin.label) { fontSize = 21 };
        var button = new GUIStyle(skin.button) { fontSize = 21 };
        var toggle = new GUIStyle(skin.toggle) { fontSize = 21 };
        var field = new GUIStyle(skin.textField) { fontSize = 21 };
        GUILayout.BeginArea(new UnityEngine.Rect(24, 12, 1872, previewTab == 0 ? 290 : 252), GUI.skin.box);
        previewTab = GUILayout.Toolbar(previewTab, new[] { "命中特效预览", "护盾与扫光预览" }, button);
        if (previewTab == 1)
        {
            if (shieldPanel != null) shieldPanel.DrawMain(label, button, toggle);
            else GUILayout.Label(error ?? "加载卡面中…", label);
            GUILayout.EndArea();
            if (shieldPanel != null) shieldPanel.DrawAdjustments(label);
            skin.font = savedFont; GUI.matrix = oldMatrix; return;
        }
        GUILayout.BeginHorizontal(); weapon = GUILayout.SelectionGrid(weapon, WeaponLabels, 5, button, GUILayout.Width(650));
        attack = GUILayout.SelectionGrid(attack, new[] { "普攻", "重击" }, 2, button, GUILayout.Width(230));
        GUILayout.Label("实际伤害元素 / 附魔色：", label, GUILayout.Width(265));
        element = GUILayout.SelectionGrid(element, ElementLabels, 8, button); GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal(); GUILayout.Label("攻击者 / 发射点", label, GUILayout.Width(220));
        source = GUILayout.SelectionGrid(source, new[] { "我1", "我2", "我3", "我4", "敌1", "敌2", "敌3", "敌4", "敌5" }, 9, button);
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        GUILayout.Label("hit数", label); hitsText = GUILayout.TextField(hitsText, 3, field, GUILayout.Width(70));
        GUILayout.Label("暴击率%", label); critText = GUILayout.TextField(critText, 8, field, GUILayout.Width(95));
        GUILayout.Label("示例伤害", label); amountText = GUILayout.TextField(amountText, 12, field, GUILayout.Width(110));
        showNumbers = GUILayout.Toggle(showNumbers, "跳字（完整空间范围）", toggle);
        useGenericTemplate = GUILayout.Toggle(useGenericTemplate, "通用命中模板", toggle);
        if (!useGenericTemplate) fullTimeRandom = GUILayout.Toggle(fullTimeRandom, "旧节奏：完整时间偏移", toggle);
        cardShake = GUILayout.Toggle(cardShake, "卡牌微抖", toggle); screenShake = GUILayout.Toggle(screenShake, "全屏抖动", toggle);
        autoReplay = GUILayout.Toggle(autoReplay, "循环", toggle); newSeedEachPlay = GUILayout.Toggle(newSeedEachPlay, "新随机样式", toggle);
        GUILayout.EndHorizontal();
        target = GUILayout.SelectionGrid(target, TargetLabels, TargetLabels.Length, button);
        playbackMode = GUILayout.Toolbar(playbackMode, new[] { "按配表模式", "每个目标分别播放", "每个总 hit 共用一次（含法器）" }, button);
        GUILayout.BeginHorizontal(); GUI.enabled = ready;
        if (GUILayout.Button("播放 / 重播 [R]", button)) Replay();
        if (GUILayout.Button(paused ? "继续 [空格]" : "暂停 [空格]", button) && running) paused = !paused;
        if (GUILayout.Button("停止", button)) Stop();
        GUI.enabled = true;
        if (GUILayout.Button("重读运行时配表与贴图", button) && ready) StartCoroutine(ReloadTextures());
        GUILayout.Label(!ready ? "加载卡面中…" : $"{elapsed:F2}s / {endTime:F2}s  暴击示例×2（只用于预览）", label);
        GUILayout.EndHorizontal(); GUILayout.EndArea();
        if (error != null || inputError != null)
            GUI.Label(new UnityEngine.Rect(24, 960, 1850, 100), error ?? inputError, label);
        else GUI.Label(new UnityEngine.Rect(24, 998, 1850, 60),
            $"当前播放：{(playingGrouped ? "总hit共用" : "逐目标")}，{visualCues.Count}组特效 / {cues.Count}次目标跳字。群体取该hit最早受击时点；配置变更下次播放生效。", label);
        skin.font = savedFont; GUI.matrix = oldMatrix;
    }
    private void Fail(string message) { error = message; Debug.LogError("HitEffectsPreview: " + message, this); }
    private IEnumerator ReloadTextures()
    {
        Stop(); ready = false;
        if (!ReadSettings()) { ready = true; yield break; }
        pendingTextureLibrary = new HitTextureLibrary(); yield return pendingTextureLibrary.Load(textures, Fail);
        if (error != null) { pendingTextureLibrary.Dispose(); pendingTextureLibrary = null; ready = true; yield break; }
        textureLibrary?.Dispose(); textureLibrary = pendingTextureLibrary; pendingTextureLibrary = null; ready = true; Replay();
    }
    private void OnDestroy()
    {
        Stop();
        textureLibrary?.Dispose();
        pendingTextureLibrary?.Dispose();
    }
}
