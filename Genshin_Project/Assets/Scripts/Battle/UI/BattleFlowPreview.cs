using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using OfficeOpenXml;
using UnityEngine;

/// <summary>Isolated presentation preview. Explicit stage/cards and the same shared display Prefab; no battle or save creation.</summary>
public sealed class BattleFlowPreview : MonoBehaviour
{
    public ProjectUiCanvas sharedCanvas;
    public UICardFactory cardFactory;
    public UIBattleFlowView viewPrefab;
    public UIRewardPopup rewardPrefab;
    public Font controlsFont;
    public Vector2 cardSize = new Vector2(197, 338), stagePosition = new Vector2(0, -5);
    public float stageScale = .80f;
    public SkillEffectsPreview.PreviewCard[] cards;
    private readonly UIBattlePreviewStage stage = new UIBattlePreviewStage();
    private readonly Dictionary<string, LangEntry> texts = new Dictionary<string, LangEntry>();
    private Canvas canvas;
    private UIBattleFlowView view;
    private UIRewardPopup reward;
    private GUIStyle label, button;
    private bool ready, english, paused, limited;
    private int turn = 1, wave = 1;
    private string error;
    private void OnEnable() { StartCoroutine(Assemble()); }
    private IEnumerator Assemble()
    {
        if (sharedCanvas == null || cardFactory == null || viewPrefab == null || controlsFont == null || cards == null)
        { error = "流程预览缺少显式绑定。"; yield break; }
        ProjectUiCanvas.EnsureBackgroundViewport(GetComponent<Camera>());
        try { LoadText(); }
        catch (Exception e) { error = e.Message; yield break; }
        canvas = sharedCanvas.CreateContentCanvas("BattleFlowPreviewCanvas");
        yield return stage.Create(canvas, stagePosition, stageScale, e => error = e);
        if (error != null) yield break;
        foreach (var entry in cards)
        {
            entry.mount = UIBattlePreviewStage.Mount("Card", stage.Root, cardSize); entry.mount.anchoredPosition = entry.position;
            entry.view = cardFactory.Request(entry.mount, entry.side, entry.id, false);
        }
        foreach (var entry in cards)
        {
            while (entry.view != null && !entry.view.Ready && entry.view.Error == null) yield return null;
            if (entry.view == null || !entry.view.Ready) { error = entry.view?.Error ?? "卡牌未就绪。"; yield break; }
        }
        view = Instantiate(viewPrefab, stage.Root, false); view.Bind(Text, false); view.ManualClock = true; RefreshTurn();
        ready = true;
    }
    private void LoadText()
    {
        texts.Clear();
        using var book = new ExcelPackage(new FileInfo(Path.Combine(Application.streamingAssetsPath, "Data", "lang", "lang.xlsx")));
        foreach (var sheet in book.Workbook.Worksheets)
        {
            if (sheet.Dimension == null || !(sheet.Name == "lang" || sheet.Name.StartsWith("lang_", StringComparison.Ordinal))) continue;
            var h = ExcelTableHeaders.For(sheet); h.Require("key", "chinese", "english");
            for (int row = h.DataStartRow; row <= sheet.Dimension.End.Row; row++)
            {
                string key = h.Cell(row, "key").Text;
                if (key.StartsWith("ui_battle_flow_", StringComparison.Ordinal) || key == "ui_reward_dismiss_hint")
                    texts.Add(key, new LangEntry { Chinese = h.Cell(row, "chinese").Text, English = h.Cell(row, "english").Text });
            }
        }
    }
    private string Text(string key, bool en, object[] args)
    {
        if (!texts.TryGetValue(key, out var entry)) throw new InvalidDataException("流程预览缺少Lang：" + key);
        return LangTextFormatter.Format(en ? entry.English : entry.Chinese, args, context: key);
    }
    private void Play(BattleFlowNoticeKind kind)
    {
        if (!ready) return;
        ClearDisplay(); view.English = english; view.Paused = paused;
        if (kind == BattleFlowNoticeKind.Victory || kind == BattleFlowNoticeKind.Defeat)
        {
            if (rewardPrefab == null) { error = "未绑定公共奖励浮层。"; return; }
            reward = Instantiate(rewardPrefab, stage.Root, false);
            var sample = new[] {
                new UIRewardPopupItem(english ? "Sample reward" : "预览奖励", "art_assets/ItemIcon/680103.png", "12", 4),
                new UIRewardPopupItem(english ? "Sample reward" : "预览奖励", "art_assets/ItemIcon/660103.png", "24", 3) };
            reward.TryShowBattleResult(sample, Text(kind == BattleFlowNoticeKind.Victory ? "ui_battle_flow_victory" : "ui_battle_flow_defeat", english, Array.Empty<object>()),
                Text("ui_battle_flow_turn", english, new object[] { turn }), Text("ui_reward_dismiss_hint", english, Array.Empty<object>()));
            return;
        }
        _ = view.PlayAsync(new BattleFlowNotice(kind, turn, wave, 3));
    }
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) { Exit(); return; }
        if (view != null) { view.English = english; view.Paused = paused; view.Advance(Time.unscaledDeltaTime); }
    }
    private void OnGUI()
    {
        if (controlsFont == null) return;
        label ??= new GUIStyle(GUI.skin.label) { font = controlsFont, fontSize = 22 };
        button ??= new GUIStyle(GUI.skin.button) { font = controlsFont, fontSize = 20 };
        var old = GUI.matrix; GUI.matrix = ProjectUiCanvas.GetGuiMatrix(canvas);
        GUI.Label(new Rect(28, 15, 750, 40), "战斗流程提示 · 独立预览", label);
        if (GUI.Button(new Rect(1490, 15, 180, 40), english ? "中文" : "English", button)) english = !english;
        if (GUI.Button(new Rect(1680, 15, 210, 40), "退出 · Esc", button)) Exit();
        GUI.enabled = ready;
        float x = 28;
        bool Click(string title, float width = 160) { bool clicked = GUI.Button(new Rect(x, 990, width, 46), title, button); x += width + 10; return clicked; }
        if (Click("战斗开始")) { turn = wave = 1; RefreshTurn(); Play(BattleFlowNoticeKind.BattleStart); }
        if (Click("下一波")) { wave = wave % 3 + 1; RefreshTurn(); Play(BattleFlowNoticeKind.WaveStart); }
        if (Click("下一回合")) { turn++; RefreshTurn(); }
        if (Click("胜利")) Play(BattleFlowNoticeKind.Victory);
        if (Click("失败")) Play(BattleFlowNoticeKind.Defeat);
        if (Click(limited ? "取消限时" : "5回合上限")) { limited = !limited; RefreshTurn(); }
        if (Click(paused ? "继续" : "暂停")) paused = !paused;
        if (Click("清空")) ClearDisplay();
        if (Click("开场顺序", 190))
        {
            ClearDisplay(); turn = wave = 1; RefreshTurn();
            _ = view.PlayAsync(new BattleFlowNotice(BattleFlowNoticeKind.BattleStart, turn));
            _ = view.PlayAsync(new BattleFlowNotice(BattleFlowNoticeKind.WaveStart, turn, wave, 3));
        }
        GUI.enabled = true;
        GUI.Label(new Rect(30, 1040, 1700, 34), error ?? $"回合 {turn} · 波次 {wave}/3 · {(ready ? "准备完成" : "加载中…")}；换波按钮保留当前回合。", label);
        GUI.matrix = old;
    }
    private void RefreshTurn()
    {
        view.SetTurn(turn, limited ? 5 : 0, limited && turn > 5);
        view.SetEnemies(wave, 3, (4 - wave) * 5);
    }
    private void ClearDisplay()
    {
        view.Clear();
        if (reward != null) { Destroy(reward.gameObject); reward = null; }
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
        StopAllCoroutines(); ready = false;
        if (view != null) { view.Clear(); Destroy(view.gameObject); view = null; }
        if (reward != null) { Destroy(reward.gameObject); reward = null; }
        if (cards != null && cardFactory != null)
            foreach (var card in cards) { cardFactory.Release(card.view); card.view = null; }
        stage.Dispose();
        if (canvas != null) Destroy(canvas.gameObject); canvas = null;
    }
}
