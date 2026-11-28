using System.Collections.Generic;
using TMPro;
using UnityEngine;

// The top roster and card bars share the same health presenters, phase pips and HP animation.
public sealed class BattleBossRosterView : MonoBehaviour
{
    sealed class CardBar
    {
        public BattlefieldHealthBar health;
        public RectTransform group, home;
        public BattleUiCardBinding card;
        public Vector2 anchorMin, anchorMax, pivot, size, offset;
        public int sibling;
        public bool moved;

        public void SetOnCard(bool show)
        {
            if (moved == show) return;
            moved = show;
            if (show)
            {
                group.SetParent(card.root, false);
                group.anchorMin = group.anchorMax = group.pivot = new Vector2(.5f, .5f);
                // PSD fill is 167 x 11, its top is 12 px above EnemyCard1.
                // The existing health presenter has a 4 px horizontal inset and a -8.5 px centre offset.
                group.sizeDelta = new Vector2(171, 28);
                group.position = card.art.TransformPoint(new Vector3(card.art.rect.center.x, card.art.rect.yMax + 26));
            }
            else if (home != null)
            {
                group.SetParent(home, false);
                group.anchorMin = anchorMin;
                group.anchorMax = anchorMax;
                group.pivot = pivot;
                group.sizeDelta = size;
                group.anchoredPosition = offset;
                group.SetSiblingIndex(sibling);
            }
        }
    }

    Transform panel, roster;
    BattleUiCombatHudSource source;
    BattlefieldHealthBar bossHealth;
    BattleEntity displayed;
    TMP_Text nameText, levelText, titleText;
    StreamingSpriteReference avatar;
    int displayedPhase, displayedLevel;
    bool displayedEnglish;
    Dictionary<string, LangEntry> displayedLang;
    bool showStatuses;
    readonly List<CardBar> bars = new List<CardBar>();

    public void Bind(Transform boss, Transform ordinary, BattleUiCombatHudSource hud, BattleUiLayout layout, bool statuses)
    {
        panel = boss;
        roster = ordinary;
        source = hud;
        showStatuses = statuses;
        bossHealth = panel.GetComponentInChildren<BattlefieldHealthBar>(true);
        foreach (var binding in panel.GetComponentsInChildren<PsdUiBinding>(true))
        {
            switch (binding.bindingKey)
            {
                case "EnemyName": nameText = binding.GetComponent<TMP_Text>(); break;
                case "EnemyLevel": levelText = binding.GetComponent<TMP_Text>(); break;
                case "EnemyTitle": titleText = binding.GetComponent<TMP_Text>(); break;
                case "EnemyAvatar":
                    avatar = binding.GetComponent<StreamingSpriteReference>() ?? binding.GetComponentInChildren<StreamingSpriteReference>(true);
                    break;
            }
        }
        if (layout != null)
            foreach (var health in roster.GetComponentsInChildren<BattlefieldHealthBar>(true))
            {
                if (!layout.TryGetCard(BattleSide.Enemy, health.slot, out var card)) continue;
                var group = (RectTransform)health.transform.parent;
                bars.Add(new CardBar { health = health, card = card, group = group, home = (RectTransform)group.parent,
                    anchorMin = group.anchorMin, anchorMax = group.anchorMax, pivot = group.pivot, size = group.sizeDelta,
                    offset = group.anchoredPosition, sibling = group.GetSiblingIndex() });
            }
    }

    void LateUpdate()
    {
        var battle = source != null ? source.battle : null;
        if (battle == null) return;
        if (!battle.IsBattleRunning || battle.IsBattleOver)
        {
            foreach (var bar in bars) if (bar.group != null) bar.SetOnCard(false);
            roster.gameObject.SetActive(false);
            panel.gameObject.SetActive(false);
            displayed = null;
            return;
        }
        BattleEntity boss = null;
        foreach (var controller in battle.Enemies)
        {
            var enemy = controller != null ? controller.Entity : null;
            if (enemy?.IsAlive == true && enemy.IsOnField && EnemyTraits.HasType(enemy, "Boss")
                && (boss == null || enemy.SlotPosition < boss.SlotPosition)) boss = enemy;
        }
        foreach (var bar in bars)
        {
            var entity = battle.GetEntityByPosition(BattleSide.Enemy, bar.health.slot);
            bar.SetOnCard(boss != null && entity?.IsAlive == true && entity.IsOnField && !EnemyTraits.HasType(entity, "Boss"));
        }
        roster.gameObject.SetActive(boss == null);
        panel.gameObject.SetActive(boss != null);
        bool changed = displayed != boss;
        if (boss == null) { displayed = null; return; }
        var dataManager = DataManager.Instance;
        if (dataManager == null) return;
        displayed = boss;
        int phase = boss.EnemyCtrl?.CurrentPhaseData?.Phase ?? 0;
        bool english = ProjectUserSettings.English();
        if (changed || displayedPhase != phase || displayedEnglish != english
            || displayedLevel != boss.Level || displayedLang != dataManager.LangDict)
        {
            string fallbackName = dataManager.EnemyMainDict.TryGetValue(boss.EntityID, out var data)
                ? data.EnemyName : $"敌人 {boss.EntityID}";
            var text = dataManager.GetBossUiText(boss.EntityID, phase, fallbackName, english);
            if (nameText != null) nameText.text = text.Name;
            if (levelText != null) levelText.text = $"Lv.{boss.Level}";
            if (titleText != null)
            {
                titleText.text = text.Subtitle;
                titleText.gameObject.SetActive(!string.IsNullOrWhiteSpace(text.Subtitle));
            }
            displayedPhase = phase;
            displayedEnglish = english;
            displayedLevel = boss.Level;
            displayedLang = dataManager.LangDict;
        }
        if (!changed) return;
        if (avatar != null) avatar.SetResolvedPath($"art_assets/Enemy_Icon/{boss.EntityID}_01.png");
        if (bossHealth != null)
        {
            bossHealth.side = BattleSide.Enemy;
            bossHealth.slot = boss.SlotPosition;
            bossHealth.Bind(source);
        }
        if (showStatuses) BattleRosterStatusBar.Bind(panel.Find("敌人状态区"), BattleSide.Enemy, boss.SlotPosition,
            boss: true, input: source.input);
    }

    void OnDisable()
    {
        foreach (var bar in bars)
            if (bar.group != null) bar.SetOnCard(false);
        if (panel != null) panel.gameObject.SetActive(false);
        if (roster != null && displayed != null)
            roster.gameObject.SetActive(source != null && source.battle != null && source.battle.IsBattleRunning && !source.battle.IsBattleOver);
        displayed = null;
    }
}
