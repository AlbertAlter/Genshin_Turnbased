using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>Isolated preview assembly. Reuses public cards, names and orbit graphics; never creates battle data.</summary>
public sealed class StatusEffectsPreview : MonoBehaviour
{
    public ProjectUiCanvas sharedCanvas;
    public UICardFactory cardFactory;
    public UIStatusNamePopup statusNames;
    public UICardOrbitArrows buff, debuff;
    public Font controlsFont;
    public int characterId = 1009;
    public Vector2 cardSize = new Vector2(197, 338);
    public Vector2 buffPosition = new Vector2(-10, -30), debuffPosition = new Vector2(540, -30);
    public bool showNames = true;
    public static Func<IReadOnlyDictionary<string, float>, string> WriteTable;
    private UICardView buffCard, debuffCard;
    private Canvas canvas;
    private UICardOrbitArrowStyle style;
    private Dictionary<string, float> numbers;
    private readonly Dictionary<string, string> edits = new Dictionary<string, string>();
    private string error, note;
    private bool ready, paused, started;
    private int mode;
    private GUIStyle label, button, field, title;
    private static readonly string[] Keys = { "ArrowCount", "ChevronsPerArrow", "TravelSeconds", "SpawnStaggerSeconds", "SpreadWidthRatio",
        "TravelHeightRatio", "ArrowWidthRatio", "ArrowHeightRatio", "ChevronGapRatio", "TrailLengthRatio", "GlowScale",
        "GlowOpacity", "Opacity", "FadeInSeconds", "FadeOutSeconds", "DurationSeconds", "CoreWhiteMix" };
    private static readonly string[] Names = { "箭头组数", "每组箭头数", "单箭头移动时间（秒）", "逐组错开（秒）", "横向分布 / 卡宽",
        "移动距离 / 卡高", "箭头宽 / 卡宽", "箭头高 / 卡高", "双箭头间距 / 卡高", "动线长 / 卡高", "外光倍率",
        "外光不透明度", "整体不透明度", "淡入（秒）", "淡出（秒）", "播放时长（秒）", "白色核心混合" };
    private IEnumerator Start()
    { started = true; yield return Assemble(); }
    private void OnEnable() { if (started) StartCoroutine(Assemble()); }
    private IEnumerator Assemble()
    {
        if (sharedCanvas == null || cardFactory == null || statusNames == null || buff == null || debuff == null || controlsFont == null)
        { error = "状态预览缺少场景显式绑定。"; yield break; }
        canvas = sharedCanvas.CreateContentCanvas("StatusEffectsPreviewCanvas");
        if (!ReloadParameters()) yield break;
        buffCard = cardFactory.Request(Mount("Buff card", buffPosition), BattleSide.Ally, characterId, false);
        debuffCard = cardFactory.Request(Mount("Debuff card", debuffPosition), BattleSide.Ally, characterId, false);
        while (buffCard != null && debuffCard != null && (!buffCard.Ready || !debuffCard.Ready)
            && buffCard.Error == null && debuffCard.Error == null) yield return null;
        if (buffCard == null || debuffCard == null || !buffCard.Ready || !debuffCard.Ready)
        { error = buffCard?.Error ?? debuffCard?.Error ?? "卡牌已释放。"; yield break; }
        statusNames.Bind(canvas, statusNames.font);
        if (!statusNames.Ready) { error = statusNames.Error; yield break; }
        ready = true;
    }
    private RectTransform Mount(string name, Vector2 position)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(canvas.transform, false); rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f;
        rect.sizeDelta = cardSize; rect.anchoredPosition = position; return rect;
    }
    public bool ReloadParameters()
    {
        try
        {
            var loaded = UICardOrbitArrowStyle.Load(); style = loaded; numbers = loaded.Numbers();
            edits.Clear(); foreach (var pair in numbers) edits[pair.Key] = pair.Value.ToString("0.####", CultureInfo.InvariantCulture);
            error = null; note = "已读取运行时表。"; return true;
        }
        catch (Exception e) { error = e.Message; return false; }
    }
    private void Replay()
    {
        if (!ready) return;
        buff.Stop(true); debuff.Stop(true); statusNames.Clear();
        if (!buff.Bind(buffCard.Appearance, style) || !debuff.Bind(debuffCard.Appearance, style))
        { error = buff.Error ?? debuff.Error; return; }
        if (mode != 2) buff.TryPlay(UICardStatusFlow.Buff);
        if (mode != 1) debuff.TryPlay(UICardStatusFlow.Debuff);
        if (showNames)
        {
            if (mode != 2) statusNames.TryShow("攻击力提升", buffCard.Appearance.Root);
            if (mode != 1) statusNames.TryShow("攻击力降低", debuffCard.Appearance.Root);
        }
        paused = false;
    }
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) { Exit(); return; }
    }
    private void Apply()
    {
        try
        {
            var changed = new Dictionary<string, float>();
            foreach (string key in Keys)
            {
                if (!float.TryParse(edits[key], NumberStyles.Float, CultureInfo.InvariantCulture, out float n)) throw new Exception("参数必须为数字：" + key);
                changed[key] = n;
            }
            style = style.WithNumbers(changed); numbers = style.Numbers(); error = null; note = "参数已应用，点击施加状态查看。尚未写回。";
        }
        catch (Exception e) { error = e.Message; }
    }
    private void OnGUI()
    {
        if (controlsFont == null) return;
        if (label == null)
        {
            label = new GUIStyle(GUI.skin.label) { font = controlsFont, fontSize = 20, fontStyle = FontStyle.Normal };
            button = new GUIStyle(GUI.skin.button) { font = controlsFont, fontSize = 20, fontStyle = FontStyle.Normal };
            field = new GUIStyle(GUI.skin.textField) { font = controlsFont, fontSize = 19, fontStyle = FontStyle.Normal };
            title = new GUIStyle(label) { fontSize = 28 };
        }
        var old = GUI.matrix; GUI.matrix = ProjectUiCanvas.GetGuiMatrix(canvas);
        GUI.Box(new Rect(24, 24, 470, 1032), GUIContent.none);
        GUI.Label(new Rect(44, 42, 430, 44), "状态效果预览", title);
        GUI.Label(new Rect(44, 85, 430, 55), "Buff 向上 · Debuff 向下", label);
        GUI.enabled = ready;
        int nextMode = GUI.SelectionGrid(new Rect(44, 139, 430, 38), mode, new[] { "两者", "Buff", "Debuff" }, 3, button);
        if (nextMode != mode) { mode = nextMode; buff.Stop(true); debuff.Stop(true); statusNames.Clear(); }
        if (GUI.Button(new Rect(44, 188, 130, 38), "施加状态", button)) Replay();
        if (GUI.Button(new Rect(184, 188, 140, 38), paused ? "继续" : "暂停箭头", button))
        { paused = !paused; buff.Paused = debuff.Paused = paused; }
        if (GUI.Button(new Rect(334, 188, 140, 38), "停止", button))
        { buff.Stop(); debuff.Stop(); statusNames.Clear(); }
        GUI.Label(new Rect(44, 237, 210, 30), "施加时单次播放", label);
        showNames = GUI.Toggle(new Rect(254, 237, 220, 30), showNames, "显示状态名称", label);
        if (numbers != null)
        {
            for (int i = 0; i < Keys.Length; i++)
            {
                float y = 282 + i * 33;
                GUI.Label(new Rect(44, y, 298, 31), Names[i], label);
                edits[Keys[i]] = GUI.TextField(new Rect(350, y, 124, 29), edits[Keys[i]], field);
            }
            if (GUI.Button(new Rect(44, 862, 205, 38), "应用临时参数", button)) Apply();
            if (GUI.Button(new Rect(264, 862, 210, 38), "重读参数表", button)) ReloadParameters();
            GUI.enabled = ready && WriteTable != null;
            if (GUI.Button(new Rect(44, 911, 430, 38), "写回 Excel 并发布", button))
            { Apply(); if (error == null) try { note = WriteTable(numbers); } catch (Exception e) { error = e.Message; } }
        }
        GUI.enabled = true;
        GUI.Label(new Rect(44, 958, 430, 82), error ?? note ?? "加载中…", new GUIStyle(label) { wordWrap = true, fontSize = 17 });
        GUI.Label(new Rect(780, 260, 350, 40), "Buff · 向上", title);
        GUI.Label(new Rect(1330, 260, 350, 40), "Debuff · 向下", title);
        if (GUI.Button(new Rect(1640, 35, 240, 44), "退出预览 · Esc", button)) Exit();
        GUI.matrix = old;
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
        if (buff != null) buff.Clear(); if (debuff != null) debuff.Clear(); if (statusNames != null) statusNames.Clear();
        if (cardFactory != null) { cardFactory.Release(buffCard); cardFactory.Release(debuffCard); }
        buffCard = debuffCard = null;
        if (canvas != null) Destroy(canvas.gameObject);
    }
}
