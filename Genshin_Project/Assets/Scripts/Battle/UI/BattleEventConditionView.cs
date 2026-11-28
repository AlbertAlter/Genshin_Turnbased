using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>非模态的战斗条件说明。只显示绑定文案，不计数、不判断挑战成败、不发奖励。</summary>
[DefaultExecutionOrder(-250)]
[DisallowMultipleComponent]
public sealed class BattleEventConditionView : MonoBehaviour
{
    [SerializeField] private TMP_FontAsset font;
    [SerializeField] private UIAdaptiveFrame framePrefab;
    private RectTransform panel;
    private TMP_Text heading, condition, indicator;
    private Func<bool> canUse;
    private bool expanded;
    private Func<string> refreshDescription;

    // 位置和尺寸由Prefab保存。模块只创建自己内部的标题、滚动正文和项目边框。
    public void Bind(string title, string description, Func<bool> available = null, Func<string> refresh = null)
    {
        if (string.IsNullOrWhiteSpace(description)) { Clear(); return; }
        Build(); canUse = available; refreshDescription = refresh;
        heading.text = title; condition.text = description.Replace("\\n", "\n");
        gameObject.SetActive(true); SetExpanded(true);
    }

    public bool TryNavigateBack()
    {
        if (!isActiveAndEnabled || !expanded) return false;
        SetExpanded(false); return true;
    }

    public void Clear() { canUse = null; refreshDescription = null; gameObject.SetActive(false); }

    private void Update()
    {
        if (refreshDescription != null)
        {
            string next = refreshDescription()?.Replace("\\n", "\n") ?? "";
            if (condition.text != next) condition.text = next;
        }
        // 在暂停菜单之前处理自己的Esc；已有预选/详情/暂停优先，由宿主传入可用判断。
        if (expanded && canUse?.Invoke() != false && UIPageNavigation.TryConsumeEscape()) TryNavigateBack();
    }

    private void SetExpanded(bool value)
    {
        expanded = value; panel.gameObject.SetActive(value); indicator.text = value ? "－" : "+";
    }

    private void Build()
    {
        if (heading != null) return;
        if (font == null || framePrefab == null) throw new InvalidOperationException("事件条件窗缺少项目字体或边框。");
        var root = (RectTransform)transform;
        var header = Region(root, "Condition header", 0, 1, 1, 1);
        header.pivot = Vector2.up; header.sizeDelta = new Vector2(0, 48); header.anchoredPosition = Vector2.zero;
        var face = header.gameObject.AddComponent<Image>(); face.color = RoguelikeCampaignScreen.Deep;
        var button = header.gameObject.AddComponent<Button>(); button.targetGraphic = face;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.onClick.AddListener(() =>
        {
            if (canUse?.Invoke() == false) return;
            SetExpanded(!expanded); EventSystem.current?.SetSelectedGameObject(null);
        });
        header.gameObject.AddComponent<UIButtonClickFeedback>().Bind(header, canUse: () => canUse?.Invoke() != false);
        heading = Text(Region(header, "Heading", .05f, .05f, .85f, .95f), 25, TextAlignmentOptions.MidlineLeft);
        indicator = Text(Region(header, "Toggle", .86f, .05f, .97f, .95f), 25, TextAlignmentOptions.Center);
        panel = Region(root, "Condition panel", 0, 0, 1, 1); panel.offsetMax = new Vector2(0, -50);
        var backdrop = panel.gameObject.AddComponent<Image>(); backdrop.color = RoguelikeCampaignScreen.Deep;
        var border = Instantiate(framePrefab, panel, false);
        RoguelikeCampaignScreen.Stretch((RectTransform)border.transform); border.fillColor = RoguelikeCampaignScreen.Deep;
        var viewport = Region(panel, "Text viewport", .06f, .08f, .94f, .92f);
        viewport.gameObject.AddComponent<RectMask2D>();
        var hitArea = viewport.gameObject.AddComponent<Image>(); hitArea.color = Color.clear;
        var content = Region(viewport, "Condition text", 0, 1, 1, 1); content.pivot = Vector2.up;
        condition = Text(content, 25, TextAlignmentOptions.TopLeft); condition.lineSpacing = 6;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.viewport = viewport; scroll.content = content;
        scroll.horizontal = false; scroll.scrollSensitivity = 28; scroll.movementType = ScrollRect.MovementType.Clamped;
    }

    private TMP_Text Text(RectTransform target, float size, TextAlignmentOptions alignment)
    {
        var label = target.gameObject.AddComponent<TextMeshProUGUI>(); label.font = font;
        label.fontSize = size; label.color = RoguelikeCampaignScreen.Cream; label.alignment = alignment;
        label.enableWordWrapping = true; label.raycastTarget = false; return label;
    }

    private static RectTransform Region(Transform parent, string name, float x0, float y0, float x1, float y1)
    {
        var child = RoguelikeCampaignScreen.Rect(name, parent); child.anchorMin = new Vector2(x0, y0);
        child.anchorMax = new Vector2(x1, y1); child.offsetMin = child.offsetMax = Vector2.zero; return child;
    }
}

/// <summary>只读的条件文案适配器。占位符约定见Lang表处理；仅计数或语言变化时重新格式化。</summary>
public static class BattleEventConditionText
{
    public static Func<string> Provider(DataManager data, string key, Func<bool> english,
        StageSetupData setup = null, BattleManager battle = null)
    {
        int initialEnemies = setup?.Waves?.Count > 0 ? setup.Waves.Sum(w => w.Enemies.Count) : setup?.Enemies?.Count ?? 0;
        int initialWaves = Math.Max(1, setup?.Waves?.Count ?? 1);
        string cached = null;
        var previous = (-1, -1, -1, -1, -1, -1, -1, -1, -1, false);
        return () =>
        {
            int turn = battle != null ? battle.TurnCount : 0;
            int max = battle != null ? battle.MaxRounds : setup?.MaxRound ?? 0;
            int remaining = battle != null ? battle.RemainingRounds : max;
            int kills = battle != null ? battle.EventEnemyKills : 0;
            int klee = battle != null ? battle.EventCharacterKills(1016) : 0;
            int escapes = battle != null ? battle.EventEscapedEnemies : 0;
            int enemies = battle != null ? battle.RemainingEnemyCount : initialEnemies;
            int wave = battle != null ? battle.CurrentWave : 1;
            int waves = battle != null ? battle.WaveCount : initialWaves;
            bool en = english();
            var state = (turn, remaining, kills, klee, escapes, enemies, wave, waves, max, en);
            if (cached == null || state != previous)
            {
                previous = state;
                object[] arguments = key switch
                {
                    "event_re101_condition" => new object[] { max, remaining, enemies },
                    "event_re102_condition" => new object[] { 5, klee },
                    "event_re103_condition" => new object[] { remaining, kills, kills / 10 },
                    "event_re105_condition" => new object[] { 5, escapes },
                    _ => new object[] { turn, remaining, kills, klee, escapes, enemies, wave, waves, kills / 10, max },
                };
                cached = data.GetLangText(key, en, arguments);
            }
            return cached;
        };
    }
}
