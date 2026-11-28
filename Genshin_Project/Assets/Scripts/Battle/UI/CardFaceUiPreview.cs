using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>Shared preview host: explicit effect selection, saved card layout and one tuning/playback UI.</summary>
public sealed class CardFaceUiPreview : MonoBehaviour
{
    [Serializable]
    public sealed class PreviewCard
    {
        public BattleSide side;
        public int id, slot;
        public Vector2 position;
        [NonSerialized] public UICardView view;
        [NonSerialized] public RectTransform mount;
    }
    public ProjectUiCanvas sharedCanvas;
    public UICardFactory cardFactory;
    public CardFacePreviewEffect[] effects;
    public string initialEffect = "Targeted";
    public Font controlsFont;
    public Vector2 cardSize = new Vector2(197, 338), stagePosition = new Vector2(230, -45);
    public float stageScale = .7f;
    public PreviewCard[] cards;
    private Canvas canvas;
    private readonly UIBattlePreviewStage stageView = new UIBattlePreviewStage();
    private readonly UIEffectPicker picker = new UIEffectPicker();
    private BattleSide targetSide = BattleSide.Enemy;
    private int targetCount = 1;
    private bool started, ready, paused, loop, showAll, reloading, assembling, choosing;
    private bool editingInput, releasePickerFocus;
    private int selected, effectIndex;
    private float speed = 1, elapsed;
    private string error, tableMessage;
    private GUIStyle label, button, title, field;
    private Vector2 parameterScroll, choiceScroll;
    private string[] folders = Array.Empty<string>();
    private readonly Dictionary<string, CardFacePreviewEffect> registered = new Dictionary<string, CardFacePreviewEffect>(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, float>> tuning = new Dictionary<string, Dictionary<string, float>>(StringComparer.Ordinal);
    private readonly Dictionary<string, string> edits = new Dictionary<string, string>(StringComparer.Ordinal);
    private readonly List<UICardFrameView> framedCards = new List<UICardFrameView>();
    private CardFacePreviewEffect active;

    private IEnumerator Start() { started = true; yield return Assemble(); }
    private void OnEnable() { if (started) StartCoroutine(Assemble()); }
    private IEnumerator Assemble()
    {
        assembling = true; yield return AssembleCore(); assembling = false;
    }
    private IEnumerator AssembleCore()
    {
        paused = false;
        if (sharedCanvas == null || cardFactory == null || controlsFont == null || cards == null || cards.Length == 0)
        { Fail("缺少显式绑定的公共 Canvas、卡牌服务、字体或卡位。"); yield break; }
        if (!ProjectUiCanvas.EnsureBackgroundViewport(GetComponent<Camera>()))
        { Fail("卡面 UI 测试需要同一服务物体上已启用的背景相机。"); yield break; }
        if (!ReadChoices()) yield break;
        canvas = sharedCanvas.CreateContentCanvas("CardFaceUiCanvas");
        yield return stageView.Create(canvas, stagePosition, stageScale, Fail);
        if (error != null || stageView.Root == null) yield break;
        var cardLayer = UIBattlePreviewStage.Mount("Cards", stageView.Root, new Vector2(1920, 1080));
        foreach (var entry in cards)
        {
            if (entry == null) { Fail("预览卡位为空。"); yield break; }
            entry.mount = new GameObject("PreviewCard_" + entry.id, typeof(RectTransform)).GetComponent<RectTransform>();
            entry.mount.SetParent(cardLayer, false);
            entry.mount.anchorMin = entry.mount.anchorMax = entry.mount.pivot = Vector2.one * .5f;
            entry.mount.anchoredPosition = entry.position; entry.mount.sizeDelta = cardSize;
            entry.view = cardFactory.Request(entry.mount, entry.side, entry.id, false);
        }
        framedCards.Clear();
        foreach (var entry in cards)
        {
            while (entry.view != null && !entry.view.Ready && entry.view.Error == null) yield return null;
            if (entry.view == null || !entry.view.Ready) { Fail(entry.view?.Error ?? "卡牌已释放。"); yield break; }
            framedCards.Add(entry.view.Appearance);
        }
        effectIndex = Math.Max(0, Array.IndexOf(folders, initialEffect));
        yield return ChangeEffect(effectIndex, false);
    }
    private bool ReadChoices()
    {
        registered.Clear();
        var choices = new SortedSet<string>(StringComparer.Ordinal);
        if (effects != null) foreach (var effect in effects)
        {
            if (effect == null || !effect.isActiveAndEnabled) continue;
            if (string.IsNullOrWhiteSpace(effect.FolderName) || registered.ContainsKey(effect.FolderName))
            { Fail("效果预览的文件夹标识为空或重复。"); return false; }
            registered.Add(effect.FolderName, effect); choices.Add(effect.FolderName);
        }
        // Source art folders are published by the existing importer. Runtime only enumerates that published directory.
        string published = Path.Combine(Application.streamingAssetsPath, "art_assets", "UI", "CardFaceUI");
        try
        {
            if (Directory.Exists(published)) foreach (string folder in Directory.GetDirectories(published)) choices.Add(Path.GetFileName(folder));
        }
        catch (Exception failure) { Fail("读取卡面效果目录失败：" + failure.Message); return false; }
        folders = new List<string>(choices).ToArray();
        if (folders.Length == 0) { Fail("没有可选择的卡面效果。请显式配置公共效果预览组件并发布素材。"); return false; }
        var items = new List<UIEffectPicker.Entry>();
        foreach (string folder in folders)
        {
            registered.TryGetValue(folder, out var effect);
            string category = effect == null ? "未接入" : effect.SourceCategory;
            string ownerName = effect == null ? "未接入" : effect.OwnerName;
            string ownerID = effect == null || effect.OwnerID == 0 ? "" : effect.OwnerID.ToString(CultureInfo.InvariantCulture);
            string effectType = effect == null ? "其他" : effect.EffectType;
            string name = effect == null ? folder + " · 未绑定" : effect.DisplayName;
            items.Add(new UIEffectPicker.Entry { Key = folder, Label = name, Detail = folder + " · " + effectType,
                Category = category, Type = effectType, OwnerKey = category + ownerID, OwnerLabel = ownerName + " " + ownerID,
                SearchText = name + " " + folder + " " + ownerName + " " + ownerID + " " + category + " " + effectType });
        }
        picker.SetEntries(items, new[] { "全部", "角色", "敌人", "公共", "未接入" },
            new[] { "全部类型", "目标标记", "层数", "符印", "其他" });
        return true;
    }
    private IEnumerator ChangeEffect(int index, bool reloadArt)
    {
        if (index < 0 || index >= folders.Length) yield break;
        reloading = true;
        if (ready && active != null) tuning[active.FolderName] = active.ReadParameters();
        ready = false; active?.Clear(); active = null; error = tableMessage = null;
        edits.Clear(); parameterScroll = Vector2.zero; effectIndex = index;
        if (!registered.TryGetValue(folders[index], out active))
        { Fail("该素材文件夹尚未绑定公共效果预览组件：" + folders[index]); reloading = false; yield break; }
        yield return active.Load(reloadArt);
        if (!active.Bind(framedCards))
        { Fail(active.Error ?? "效果尚未就绪。"); reloading = false; yield break; }
        if (tuning.TryGetValue(active.FolderName, out var parameters)) active.ApplyParameters(parameters);
        ready = true; reloading = false; Replay();
    }
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        { if (choosing) { choosing = false; releasePickerFocus = true; } else Exit(); return; }
        if (!ready || choosing) return;
        if (Input.GetMouseButtonDown(0) && stageView.Viewport != null)
        {
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            if (RectTransformUtility.RectangleContainsScreenPoint(stageView.Viewport, Input.mousePosition, camera))
                for (int i = 0; i < cards.Length; i++)
                    if (RectTransformUtility.RectangleContainsScreenPoint(cards[i].mount, Input.mousePosition, camera))
                    { Select(i); releasePickerFocus = true; break; }
        }
        if (!editingInput)
        {
            if (Input.GetKeyDown(KeyCode.Space)) Replay();
            if (Input.GetKeyDown(KeyCode.RightArrow)) Select((selected + 1) % cards.Length);
            if (Input.GetKeyDown(KeyCode.LeftArrow)) Select((selected + cards.Length - 1) % cards.Length);
            for (int i = 0; i < cards.Length && i < 9; i++)
                if (Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + i))) Select(i);
        }
        if (paused) return;
        float delta = Time.unscaledDeltaTime * speed; active.Advance(delta);
        elapsed = Mathf.Min(active.PreviewDuration, elapsed + delta);
        if (loop && elapsed >= active.PreviewDuration) Replay();
    }
    private void Select(int index)
    {
        if (!ready || index < 0 || index >= cards.Length) return;
        selected = index; elapsed = 0;
        targetSide = cards[selected].side; targetCount = Mathf.Clamp(targetCount, 1, SideCount);
        ShowTargets();
    }
    private int SideCount { get { int n = 0; foreach (var card in cards) if (card.side == targetSide) n++; return n; } }
    private void ShowTargets()
    {
        int count = Mathf.Max(1, SideCount), baseSlot = cards[selected].slot;
        for (int i = 0; i < cards.Length; i++)
            if (showAll || cards[i].side == targetSide && (cards[i].slot - baseSlot + count) % count < targetCount)
                active.Show(i); else active.Hide(i, false);
    }
    private void Replay()
    {
        if (!ready) return;
        for (int i = 0; i < cards.Length; i++) active.Hide(i, true);
        Select(selected);
    }
    private void Hide(bool immediate = false)
    {
        loop = false;
        for (int i = 0; i < cards.Length; i++) active.Hide(i, immediate);
    }
    private void Seek(float time)
    {
        Replay(); active.Advance(time); elapsed = time; paused = true;
    }
    public bool ReloadConfiguration()
    {
        if (active == null || reloading || assembling || !ready) return false;
        bool loaded = active.ReloadConfiguration();
        if (loaded) { tuning[active.FolderName] = active.ReadParameters(); edits.Clear(); }
        tableMessage = loaded ? "已从运行时表重读参数。" : active.Error;
        return loaded;
    }
    private void SaveTable()
    {
        try
        {
            tableMessage = active.WriteParameters();
            if (!active.ReloadConfiguration()) tableMessage += "\n" + active.Error;
            tuning[active.FolderName] = active.ReadParameters(); edits.Clear();
        }
        catch (Exception failure) { tableMessage = "写回失败：" + failure.Message; Debug.LogError(tableMessage, this); }
    }
    private void Fail(string message) { ready = false; error = message; Debug.LogError("CardFaceUI：" + message, this); }
    private void DrawParameters()
    {
        var values = active.ReadParameters(); bool changed = false;
        parameterScroll = GUI.BeginScrollView(new Rect(34, 360, 430, 500), parameterScroll, new Rect(0, 0, 404, active.Parameters.Count * 74));
        for (int i = 0; i < active.Parameters.Count; i++)
        {
            var p = active.Parameters[i]; float y = i * 74, value = values[p.Key];
            if (p.Toggle)
            {
                bool next = GUI.Toggle(new Rect(10, y + 10, 400, 38), value == 1, p.Label, label);
                if (next != (value == 1)) { values[p.Key] = next ? 1 : 0; changed = true; }
                continue;
            }
            GUI.Label(new Rect(10, y, 296, 31), p.Label, label);
            string formatted = (value * p.DisplayScale).ToString("0.###", CultureInfo.InvariantCulture);
            string before = edits.TryGetValue(p.Key, out var editing) ? editing : formatted;
            GUI.SetNextControlName("CardFaceParameter." + p.Key);
            string text = GUI.TextField(new Rect(292, y, 105, 31), before, field);
            if (text != before)
            {
                edits[p.Key] = text;
                if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float number)
                    && !float.IsNaN(number) && !float.IsInfinity(number))
                {
                    float requested = number / p.DisplayScale;
                    value = Mathf.Clamp(requested, p.Min, p.Max); values[p.Key] = value; changed = true;
                    if (!Mathf.Approximately(requested, value)) edits[p.Key] = (value * p.DisplayScale).ToString("0.###", CultureInfo.InvariantCulture);
                }
            }
            float slider = GUI.HorizontalSlider(new Rect(10, y + 41, 387, 23), value, p.Min, p.Max);
            if (!Mathf.Approximately(value, slider))
            { values[p.Key] = slider; changed = true; edits[p.Key] = (slider * p.DisplayScale).ToString("0.###", CultureInfo.InvariantCulture); }
        }
        GUI.EndScrollView();
        if (changed) active.ApplyParameters(values);
    }
    private void OnGUI()
    {
        if (controlsFont == null) return;
        if (releasePickerFocus) { GUI.FocusControl(null); releasePickerFocus = false; editingInput = false; }
        if (label == null)
        {
            label = new GUIStyle(GUI.skin.label) { font = controlsFont, fontSize = 20, fontStyle = FontStyle.Normal, wordWrap = true };
            button = new GUIStyle(GUI.skin.button) { font = controlsFont, fontSize = 20, fontStyle = FontStyle.Normal };
            title = new GUIStyle(label) { fontSize = 28 };
            field = new GUIStyle(GUI.skin.textField) { font = controlsFont, fontSize = 18 };
        }
        var matrix = GUI.matrix; bool enabledBefore = GUI.enabled;
        GUI.matrix = ProjectUiCanvas.GetGuiMatrix(canvas);
        GUI.Box(new Rect(24, 24, 460, 1032), GUIContent.none);
        GUI.Label(new Rect(44, 42, 430, 42), "卡面状态效果预览", title);
        GUI.enabled = !assembling && !reloading && framedCards.Count == (cards?.Length ?? 0) && folders.Length > 0;
        string current = folders.Length == 0 ? "没有效果" : folders[effectIndex];
        string caption = active != null ? current + " · " + active.DisplayName : current;
        if (GUI.Button(new Rect(44, 96, 410, 44), caption + "  ▾", button))
        { choosing = !choosing; if (choosing) picker.Open(current); else GUI.FocusControl(null); }
        GUI.enabled = ready && !choosing;
        if (GUI.Button(new Rect(44, 154, 195, 40), "播放 / 重播 · 空格", button)) Replay();
        if (GUI.Button(new Rect(254, 154, 200, 40), "停止并淡出", button)) Hide();
        if (GUI.Button(new Rect(44, 206, 195, 40), paused ? "继续" : "暂停", button)) paused = !paused;
        if (GUI.Button(new Rect(254, 206, 200, 40), "立即清空", button)) Hide(true);
        if (GUI.Button(new Rect(44, 258, 195, 40), "速度 " + speed.ToString("0.##") + "×", button)) speed = speed == 1 ? .25f : 1;
        if (GUI.Button(new Rect(254, 258, 200, 40), loop ? "循环：开" : "循环：关", button)) { loop = !loop; elapsed = 0; }
        if (ready && !choosing)
        {
            float seek = GUI.HorizontalSlider(new Rect(44, 320, 410, 25), elapsed, 0, active.PreviewDuration);
            if (Mathf.Abs(seek - elapsed) > .015f) Seek(seek);
            DrawParameters();
        }
        if (GUI.Button(new Rect(44, 880, 410, 40), "重读已发布素材", button)) StartCoroutine(ChangeEffect(effectIndex, true));
        if (GUI.Button(new Rect(44, 932, 195, 40), "重读配表", button)) ReloadConfiguration();
#if UNITY_EDITOR
        if (GUI.Button(new Rect(254, 932, 200, 40), "写回表并发布", button)) SaveTable();
#else
        GUI.enabled = false; GUI.Button(new Rect(254, 934, 200, 40), "写回（仅编辑器）", button);
#endif
        GUI.enabled = enabledBefore;
        string file = active != null ? active.ParameterFile : "";
        GUI.Label(new Rect(44, 988, 410, 68), error ?? tableMessage ?? (ready ? "未写回的调整退出丢弃。\n配表：UI/CardFaceUI/" + file : "正在加载卡牌与效果…"), label);
        GUI.Label(new Rect(530, 32, 1300, 38), "卡面状态效果检查；预览输入不施加状态、不启动战斗或读取存档。", label);
        GUI.enabled = ready && !choosing;
        if (ready)
        {
            if (GUI.Button(new Rect(530, 88, 150, 40), targetSide == BattleSide.Enemy ? "目标：敌方" : "目标：我方", button))
            {
                var side = targetSide == BattleSide.Enemy ? BattleSide.Ally : BattleSide.Enemy;
                for (int i = 0; i < cards.Length; i++) if (cards[i].side == side) { Select(i); break; }
            }
            GUI.Label(new Rect(700, 92, 90, 35), "人数", label);
            string[] counts = SideCount > 4 ? new[] { "1", "2", "3", "4", "5" } : new[] { "1", "2", "3", "4" };
            int next = GUI.SelectionGrid(new Rect(790, 88, 300, 40), targetCount - 1, counts, counts.Length, button) + 1;
            if (next != targetCount) { targetCount = next; Replay(); }
            if (GUI.Button(new Rect(1110, 88, 230, 40), "基准卡位 " + cards[selected].slot, button))
                Select((selected + 1) % cards.Length);
            bool all = GUI.Toggle(new Rect(1360, 92, 400, 35), showAll, "全部卡面同时显示", label);
            if (all != showAll) { showAll = all; Replay(); }
            GUI.Label(new Rect(530, 145, 1300, 34), "点击卡面或按1～9、左右键切换；当前基准 " + cards[selected].id, label);
            active.DrawSampleControls(new Rect(530, 194, 1300, 110), label, button);
        }
        if (choosing)
        {
            GUI.enabled = !assembling && !reloading;
            if (picker.Draw(new Rect(44, 142, 740, 774), controlsFont, current, out string key))
            { choosing = false; StartCoroutine(ChangeEffect(Array.IndexOf(folders, key), false)); }
            else if (picker.CloseRequested) choosing = false;
        }
        GUI.enabled = ready && !choosing;
        if (ready && !choosing && stageView.Viewport != null && Event.current.type == EventType.ScrollWheel)
        {
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            if (RectTransformUtility.RectangleContainsScreenPoint(stageView.Viewport, Input.mousePosition, camera))
            { stageView.Zoom.ZoomAt(Input.mousePosition, camera, -Event.current.delta.y / 3f); Event.current.Use(); }
        }
        if (GUI.Button(new Rect(530, 990, 230, 44), "还原视图 · " + Mathf.RoundToInt(stageView.Zoom.Factor * 100) + "%", button)) stageView.Zoom.Reset();
        GUI.Label(new Rect(780, 997, 800, 32), "画面内滚轮缩放，以鼠标位置为中心", label);
        GUI.enabled = enabledBefore;
        if (GUI.Button(new Rect(1620, 985, 250, 44), "退出测试 · Esc", button)) Exit();
        editingInput = !string.IsNullOrEmpty(GUI.GetNameOfFocusedControl());
        GUI.enabled = enabledBefore; GUI.matrix = matrix;
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
        StopAllCoroutines(); ready = false; reloading = false; assembling = false; choosing = false;
        if (effects != null) foreach (var effect in effects) if (effect != null) effect.Clear();
        active = null; tuning.Clear(); edits.Clear(); framedCards.Clear();
        if (cards != null) foreach (var entry in cards)
        {
            if (entry == null) continue;
            if (cardFactory != null) cardFactory.Release(entry.view);
            entry.view = null; entry.mount = null;
        }
        if (canvas != null) Destroy(canvas.gameObject);
        stageView.Dispose();
        canvas = null;
    }
}
