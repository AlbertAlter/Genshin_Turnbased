using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class BattleUiCharacterDetails
{
    private readonly struct Detail
    {
        public readonly string Name, Summary, Body, Icon, Element;
        public readonly bool StatusIcon;
        public Detail(string name, string summary, string body, string icon, string element = null, bool statusIcon = false)
        {
            Name = name; Summary = summary; Body = body; Icon = icon; Element = element; StatusIcon = statusIcon;
        }
    }

    private static readonly (string Id, string Chinese)[] Elements = {
        ("Pyro", "火元素"), ("Hydro", "水元素"), ("Dendro", "草元素"), ("Electro", "雷元素"),
        ("Anemo", "风元素"), ("Cryo", "冰元素"), ("Geo", "岩元素"), ("Physical", "物理")
    };
    private static readonly string[] SkillKeys = { "normal", "heavy", "skill", "burst" };
    private static readonly string[] WeaponChinese = { "单手剑", "双手剑", "弓", "长柄武器", "法器" };
    private static readonly string[] WeaponEnglish = { "Sword", "Claymore", "Bow", "Polearm", "Catalyst" };
    private static readonly string[] SlotKeys = { "flower", "plume", "sands", "goblet", "circlet" };
    private static readonly string[] SlotChinese = { "生之花", "死之羽", "时之沙", "空之杯", "理之冠" };
    private static readonly string[] SlotEnglish = { "Flower of Life", "Plume of Death", "Sands of Eon", "Goblet of Eonothem", "Circlet of Logos" };

    private void RenderTab()
    {
        Clear(body);
        SetSelectedTab(mainTabs, tab);
        var area = Region("Current tab", body, 0, 0, 1, 1);
        if (tab == 1)
        {
            var subTabs = Region("Skill tabs", area, 0, 1, 1, 1);
            subTabs.offsetMin = new Vector2(0, -40);
            RowLayout(subTabs, 9).childForceExpandHeight = true;
            string[] titles = { Ui("ui_character_tab_skill"), Ui("ui_character_tab_talent"), Ui("ui_character_tab_constellation") };
            for (int i = 0; i < titles.Length; i++)
            {
                int index = i;
                var rect = Node(titles[i], subTabs);
                bool selected = i == skillTab;
                TextButton(rect, titles[i], () =>
                {
                    if (skillTab == index) return;
                    skillTab = index;
                    entry = 0;
                    RenderTab();
                }, selected ? Deep : Light, 22, selected ? Color.white : Ink);
                var text = rect.GetComponentInChildren<TMP_Text>();
                rect.gameObject.AddComponent<LayoutElement>().preferredWidth =
                    Mathf.Max(120, text.GetPreferredValues(titles[i], Mathf.Infinity, Mathf.Infinity).x + 34);
            }
            var scrollArea = Region("Skill content", area, 0, 0, 1, 1);
            scrollArea.offsetMax = new Vector2(0, -58);
            RenderEntries(ScrollColumn(scrollArea), Skills());
            return;
        }

        var scroll = ScrollColumn(area);
        switch (tab)
        {
            case 0: RenderAttributes(scroll); break;
            case 2: RenderEquipment(scroll); break;
            case 3: RenderEntries(scroll, Statuses()); break;
        }
    }

    private void RenderAttributes(RectTransform content)
    {
        var entity = actor.Entity;
        entity.ReadAttributesForDisplay(() =>
        {
            float maximum = entity.GetFinalHP();
            var resources = Node("Current resources", content);
            Column(resources, 16);
            Fill(resources, Paper);
            FlowText(resources, Ui("ui_battle_detail_current_resources"), 29);
            var resourceRow = Node("Energy and health", resources);
            RowLayout(resourceRow, 24);
            var energy = Node("Energy", resourceRow);
            Column(energy, 2, 3);
            energy.gameObject.AddComponent<LayoutElement>().preferredWidth = 151;
            var disc = Node("Burst gauge", energy);
            disc.gameObject.AddComponent<LayoutElement>().preferredHeight = 104;
            var gauge = Square(disc, 104);
            DrawDisc(gauge, actor.GetBoundSkillIcon(3), actor.Element, entity.CurrentEnergy, entity.MaxEnergy);
            FlowText(energy, $"{entity.CurrentEnergy:0.##} / {entity.MaxEnergy}", 23).alignment = TextAlignmentOptions.Center;
            FlowText(energy, Ui("ui_battle_detail_energy"), 19).alignment = TextAlignmentOptions.Center;
            var health = Node("Health and shield", resourceRow);
            health.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            Column(health, 10, 8);
            Stat(health, Ui("ui_stat_hp"), $"{Number(entity.CurrentHP)} / {Number(maximum)}");
            var bar = Node("Health bar", health);
            bar.gameObject.AddComponent<LayoutElement>().preferredHeight = 8;
            Fill(bar, Deep);
            Fill(Region("HP fill", bar, 0, 0, maximum > 0 ? Mathf.Clamp01(entity.CurrentHP / maximum) : 0, 1),
                new Color32(82, 113, 63, 255));
            Stat(health, Ui("ui_battle_detail_shield"), Number(entity.GetTotalShieldHP()));

            FlowText(content, Ui("ui_battle_detail_detailed_attributes"), 29);
            FlowText(content, Ui("ui_battle_detail_base_attributes"), 23);
            Stat(content, Ui("ui_stat_max_hp"), Number(maximum));
            Stat(content, Ui("ui_stat_atk"), Number(entity.GetFinalATK("", "", -1, "")));
            Stat(content, Ui("ui_stat_def"), Number(entity.GetFinalDEF(skillType: -1)));
            Stat(content, Ui("ui_stat_elemental_mastery"), Number(entity.GetFinalEM()));

            FlowText(content, Ui("ui_battle_detail_advanced_attributes"), 23);
            Stat(content, Ui("ui_stat_crit_rate"),
                Percent(entity.CritRate + entity.WeaponCritRate + entity.GetStatusCritRate("", "", -1, "")));
            Stat(content, Ui("ui_stat_crit_dmg"),
                Percent(entity.CritDMG + entity.WeaponCritDMG + entity.GetStatusCritDMG("", "", -1, "", null)));
            Stat(content, Ui("ui_battle_detail_healing_bonus"),
                Percent(entity.HealBonus + entity.GetStatusHealingBonus("HealBonus", "", "", -1, "", null)));
            Stat(content, Ui("ui_battle_detail_incoming_healing_bonus"),
                Percent(entity.BeHealedBonus + entity.GetStatusHealingBonus("BeHealedBonus", "", "", -1, "", null)));
            Stat(content, Ui("ui_stat_energy_recharge"), Percent(entity.GetFinalRechargeRate()));
            Stat(content, Ui("ui_battle_detail_shield_strength"), Percent(entity.GetShieldStrengthMultiplier() - 1f));

            FlowText(content, Ui("ui_battle_detail_elemental_attributes"), 23);
            foreach (var element in Elements)
            {
                string key = element.Id.ToLowerInvariant();
                Stat(content, Ui($"ui_stat_{key}_dmg_bonus"),
                    Percent(entity.DMGBonus + actor.GetElementBonus(element.Id) + entity.GetStatusDMGBonus("", "", -1, element.Id, null)));
                Stat(content, Ui($"ui_stat_{key}_res"),
                    Percent(ReactionDamageCalculator.GetResistance(entity, element.Id)));
            }
        });
    }

    private void Stat(RectTransform parent, string name, string value)
    {
        var row = Node(name, parent);
        row.gameObject.AddComponent<LayoutElement>().preferredHeight = 38;
        FixedText(Region("Attribute", row, 0, 0, .70f, 1), name, 22, TextAlignmentOptions.MidlineLeft);
        FixedText(Region("Value", row, .70f, 0, 1, 1), value, 23, TextAlignmentOptions.MidlineRight);
        var rule = Region("Separator", row, 0, 0, 1, 0);
        rule.offsetMax = new Vector2(0, 1);
        Fill(rule, Rule);
    }

    private List<Detail> Skills()
    {
        int id = actor.CharacterID;
        var details = new List<Detail>();
        if (skillTab == 0)
        {
            string[] labels = { Ui("ui_battle_detail_normal_attack"), Ui("ui_skill_heavy"), Ui("ui_battle_detail_elemental_skill"), Ui("ui_battle_detail_elemental_burst") };
            for (int i = 0; i < SkillKeys.Length; i++)
            {
                var skill = actor.GetBoundSkillData(i);
                if (skill == null) continue;
                string summary = $"{labels[i]} · Lv. {actor.GetEffectiveSkillLevel(i)}";
                int remaining = actor.GetCooldownRemaining(i);
                if (remaining > 0) summary += Ui("ui_battle_detail_cooldown", remaining);
                details.Add(new Detail(actor.GetUiSkillName(i, english, skill), summary,
                    CharacterDescription($"character_{id}_{SkillKeys[i]}_desc", skill.Description), skill.Icon, actor.Element));
            }
        }
        else if (skillTab == 1 && data.TalentDict.TryGetValue(id, out var talents))
        {
            foreach (var talent in talents.OrderBy(value => value.TalentIndex))
            {
                int index = talent.TalentIndex;
                bool active = CharacterAscensionRules.IsTalentUnlocked(talent.UnlockAfter, actor.Level, actor.Ascension);
                details.Add(new Detail(data.GetUiName(UiNameKind.Talent, id, talent.TalentName, english, index),
                    active ? Ui("ui_talent_unlocked") : Ui("ui_battle_detail_talent_unlock", talent.UnlockAfter),
                    CharacterDescription($"character_{id}_talent_{index}_desc", talent.Description), $"{id}_T{index:00}", active ? actor.Element : null));
            }
        }
        else if (skillTab == 2 && data.ConstellationDict.TryGetValue(id, out var constellations))
        {
            foreach (var constellation in constellations.OrderBy(value => value.ConstellationIndex))
            {
                int index = constellation.ConstellationIndex;
                bool active = actor.Entity.ConstellationLevel >= index;
                string name = data.GetUiName(UiNameKind.Constellation, id, constellation.ConstellationName, english, index);
                string summary = Ui("ui_battle_detail_constellation", index) + " · "
                    + (active ? Ui("ui_constellation_activated") : Ui("ui_battle_detail_inactive"));
                details.Add(new Detail(name, summary, CharacterDescription($"character_{id}_constellation_{index}_desc", constellation.Description),
                    $"{id}_C{index:00}", active ? actor.Element : null));
            }
        }
        return details;
    }

    private List<Detail> Statuses()
    {
        var entity = actor.Entity;
        var details = new List<Detail>();
        foreach (var status in entity.GetStatusList().Where(value => value.IsActive && value.MainData?.Display == 1).Reverse())
        {
            string summary = status.IsPermanent ? Ui("ui_battle_detail_turns_remaining_unlimited")
                : Ui("ui_battle_detail_turns", status.RemainingPhaseCount);
            if (BattleStatusDescriptionView.ShowsCount(status))
            {
                string count = status.HasIntegerRuntimeValue ? status.RuntimeIntValue.ToString(CultureInfo.InvariantCulture)
                    : status.RuntimeValue.ToString("0.##", CultureInfo.InvariantCulture);
                summary += Ui("ui_battle_detail_stacks", count);
            }
            string icon = status.MainData.Icon;
            if (string.IsNullOrWhiteSpace(icon)) icon = status.MainData.StatusType == "Debuff" ? "GeneralDebuff" : "GeneralBuff";
            string description = data.GetStatusIconDescriptionText(status, entity, english) ?? status.GetDescription(entity);
            if (status.Caster != null && status.Caster.Type == BattleEntity.EntityType.Character)
                description += "\n\n" + Ui("ui_battle_detail_source") + CharacterName(status.Caster.EntityID);
            details.Add(new Detail(status.MainData.StatusName, summary, description, BattleRosterStatusBar.IconPath(icon), statusIcon: true));
        }
        return details;
    }

    private void RenderEntries(RectTransform content, IReadOnlyList<Detail> details)
    {
        if (details.Count == 0)
        {
            FlowText(content, tab == 3 ? Ui("ui_battle_detail_no_active_status_effects") : Ui("ui_battle_detail_no_details_available"), 24);
            return;
        }
        entry = Mathf.Clamp(entry, 0, details.Count - 1);
        var row = Node("Entries and description", content);
        row.gameObject.AddComponent<BattleDetailColumns>();
        var list = Node("Entry list", row);
        Column(list, 0, 3);
        var buttons = new Button[details.Count];
        var reading = Node("Reading pane", row);
        Fill(reading, Paper);
        Column(reading, 0, 15);
        reading.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(22, 22, 18, 25);
        var heading = Node("Entry heading", reading);
        var headLayout = RowLayout(heading, 13);
        headLayout.childAlignment = TextAnchor.MiddleLeft;
        headLayout.padding.bottom = 13;
        var headIcon = Node("Entry icon", heading);
        SizeIcon(headIcon, 53);
        var headText = EntryTextColumn(heading, 5);
        var title = FlowText(headText, "", 30);
        var meta = FlowText(headText, "", 20);
        var description = FlowText(reading, "", 21, true);

        void ShowEntry(int index)
        {
            if (buttons[entry] != null) buttons[entry].image.color = Color.clear;
            entry = index;
            var selected = details[index];
            buttons[index].image.color = Light;
            Clear(headIcon);
            EntryIcon(headIcon, selected);
            title.text = selected.Name;
            meta.text = selected.Summary;
            description.text = selected.Body;
        }

        for (int i = 0; i < details.Count; i++)
        {
            int index = i;
            var item = details[i];
            var button = Node(item.Name, list);
            button.gameObject.AddComponent<LayoutElement>().minHeight = 72;
            buttons[i] = Click(button, () => { if (entry != index) ShowEntry(index); }, Color.clear);
            var line = RowLayout(button, 11);
            line.padding = new RectOffset(10, 10, 10, 10);
            line.childAlignment = TextAnchor.MiddleLeft;
            var icon = Node("Icon", button);
            SizeIcon(icon, 48);
            EntryIcon(icon, item);
            var labels = EntryTextColumn(button, 4);
            FlowText(labels, item.Name, 21);
            FlowText(labels, item.Summary, 16);
        }
        ShowEntry(entry);
    }

    private static void SizeIcon(RectTransform rect, int size)
    {
        var element = rect.gameObject.AddComponent<LayoutElement>();
        element.minWidth = element.preferredWidth = element.minHeight = element.preferredHeight = size;
    }

    private static RectTransform EntryTextColumn(RectTransform parent, float gap)
    {
        var labels = Node("Title and summary", parent);
        var width = labels.gameObject.AddComponent<LayoutElement>();
        width.minWidth = width.preferredWidth = 0;
        width.flexibleWidth = 1;
        Column(labels, 0, gap);
        return labels;
    }

    private void RenderEquipment(RectTransform parent)
    {
        var weapon = Node("Weapon", parent);
        Fill(weapon, Paper);
        Column(weapon, 18);
        FlowText(weapon, Ui("btn_tab_weapon"), 27);
        var equipped = actor.EquippedWeapon;
        if (equipped == null || !data.WeaponAttributesDict.TryGetValue(equipped.WeaponID, out var definition))
            FlowText(weapon, Ui("ui_weapon_empty"), 23);
        else
        {
            var row = Node("Weapon and refinement", weapon);
            RowLayout(row, 18);
            GearIcon(row, $"art_assets/Weapon_Icon/{equipped.WeaponID}_01.png", definition.Star, 96);
            var text = Node("Weapon name", row);
            text.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            Column(text, 0, 5);
            FlowText(text, data.GetUiName(UiNameKind.Weapon, equipped.WeaponID, definition.WeaponName, english), 29);
            FlowText(text, $"Lv. {equipped.Level} · " + Ui("ui_battle_detail_refinement", equipped.Refinement), 21);
            string titleKey = $"weapon_{equipped.WeaponID}_effect_title";
            if (data.LangDict.ContainsKey(titleKey)) FlowText(weapon, data.GetLangText(titleKey, english), 25);
            FlowText(weapon, descriptions.WeaponDescription(definition, equipped.Refinement, equipped.Refinement, english), 23, true);
        }

        var artifacts = Node("Artifacts", parent);
        Fill(artifacts, Paper);
        Column(artifacts, 18, 12);
        FlowText(artifacts, Ui("btn_tab_artifact"), 27);
        var slots = Node("Artifact slots", artifacts);
        RowLayout(slots, 12).childForceExpandWidth = true;
        var items = equippedArtifacts?.Invoke(actor) ?? Array.Empty<RoguelikeArtifactInstanceData>();
        for (int i = 0; i < SlotKeys.Length; i++)
        {
            int position = i + 1;
            var item = items.FirstOrDefault(value => value?.Artifact != null && (int)value.Artifact.Slot == position);
            var column = Node(SlotKeys[i], slots);
            column.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            Column(column, 2, 4);
            GearIcon(column, item == null || item.ArtifactSetID <= 0 ? null : $"art_assets/Artifact_Icon/{item.ArtifactSetID}{position}.png",
                item?.Artifact.Star ?? 0, 65);
            FlowText(column, Ui("ui_artifact_slot_" + SlotKeys[i]), 18).alignment = TextAlignmentOptions.Center;
        }
        var setCounts = items.Count == 0 ? actor.EquippedArtifactSetCounts
            : items.Where(value => value != null && value.ArtifactSetID > 0).GroupBy(value => value.ArtifactSetID)
                .ToDictionary(group => group.Key, group => group.Count());
        foreach (var set in setCounts.Where(value => value.Value >= 2).OrderBy(value => value.Key))
        {
            FlowText(artifacts, descriptions.SetName(set.Key, english), 25);
            for (int pieces = 2; pieces <= 4 && pieces <= set.Value; pieces += 2)
            {
                var space = Node("Set paragraph gap", artifacts);
                space.gameObject.AddComponent<LayoutElement>().preferredHeight = 10;
                FlowText(artifacts, Ui("ui_battle_detail_set_pieces", pieces), 23);
                FlowText(artifacts, descriptions.SetBonus(set.Key, pieces, english), 23, true);
            }
        }
    }

    private static void GearIcon(RectTransform parent, string iconPath, int rarity, float size)
    {
        var cell = Node("Equipment icon", parent);
        var layout = cell.gameObject.AddComponent<LayoutElement>();
        layout.preferredWidth = layout.preferredHeight = size;
        var square = Square(cell, size);
        Fill(square, Rule);
        Art(Region("Rarity", square, 0, 0, 1, 1), UIItemListIcon.GetRarityBackdropPath(rarity));
        Art(Region("Equipment art", square, .08f, .08f, .92f, .92f), iconPath);
    }

    private static RectTransform Square(RectTransform parent, float size)
    {
        var square = Region("Square", parent, .5f, .5f, .5f, .5f);
        square.sizeDelta = new Vector2(size, size);
        return square;
    }

    private static void EntryIcon(RectTransform parent, Detail detail)
    {
        var square = Region("Icon square", parent, 0, 0, 1, 1);
        var aspect = square.gameObject.AddComponent<AspectRatioFitter>();
        aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent; aspect.aspectRatio = 1;
        parent = square;
        if (!detail.StatusIcon) { DrawDisc(parent, detail.Icon, detail.Element); return; }
        Art(Region("Status circle", parent, 0, 0, 1, 1), "art_assets/" + BattleRosterStatusBar.Background);
        Art(Region("Status symbol", parent, .16f, .16f, .84f, .84f), detail.Icon);
    }

    private static void DrawDisc(RectTransform parent, string icon, string element, float energy = 1, float maximum = 1)
    {
        // The four authored battlefield rings are 104 / 96 / 92 / 88 pixels.
        Art(DiscLayer(parent, "Outer dark ring", 104), DarkDisc, true);
        Art(DiscLayer(parent, "Light ring", 96), LightDisc, true);
        Art(DiscLayer(parent, "Inner dark ring", 92), DarkDisc, true);
        Art(DiscLayer(parent, "Unfilled center", 88), DarkDisc, true);
        if (Elements.Any(value => value.Id == element && value.Id != "Physical"))
        {
            var fill = Art(DiscLayer(parent, "Element fill", 88), $"art_assets/Skill_Icon/#Others/{element}_BG.png");
            BattleUiEnergyFill.Apply(fill, energy, maximum);
        }
        if (CharacterSkillIconResolver.TryGetRelativePath(icon, out string path))
            Art(DiscLayer(parent, "Skill symbol", 79.065f), path);
    }

    private static RectTransform DiscLayer(RectTransform parent, string name, float size)
    {
        float inset = (1f - size / 104f) * .5f;
        return Region(name, parent, inset, inset, 1f - inset, 1f - inset);
    }

    private string CharacterDescription(string key, string fallback)
        => descriptions.BattleCharacterDescription(key, fallback, actor.GetEffectiveSkillLevel, english);
    private string CharacterName(int id) => data.GetUiName(UiNameKind.Character, id,
        data.CharacterAttributesDict.TryGetValue(id, out var attributes) ? attributes.Name : id.ToString(), english);
    private string ElementLabel(string element) => Ui("ui_element_" + element.ToLowerInvariant());
    private string WeaponLabel(int type) => type < 0 || type >= WeaponTypes.Length ? ""
        : " · " + Ui("ui_weapon_type_" + WeaponTypes[type]);
    private string Ui(string key, params object[] arguments) => data.GetLangText(key, english, arguments);
    private static string Number(float value) => value.ToString("N0", CultureInfo.InvariantCulture);
    private static string Percent(float value) => (value * 100f).ToString("0.0", CultureInfo.InvariantCulture) + "%";
}
