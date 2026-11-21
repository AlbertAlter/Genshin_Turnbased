using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class RoguelikeRosterView
{
    private static readonly string[] AttributeElementOrder = { "Pyro", "Hydro", "Dendro", "Electro", "Anemo", "Cryo", "Geo", "Physical" };
    private static readonly string[] ArtifactSlotArt = { "flower", "plume", "sands", "goblet", "circlet" };
    private string StatName(string key) => RoguelikeRosterStats.Name(key, english);
    private static string StatValue(string key, float value) => RoguelikeRosterStats.FormatValue(key, value);

    private void RenderAttributes(RectTransform body, RectTransform footer, RoguelikeCharacterProgressData c)
    {
        if (!session.Data.CharacterAttributesDict.TryGetValue(c.CharacterID, out var info))
        {
            Label(body, T("这个角色没有可用的养成配置。", "This character has no progression configuration."));
            return;
        }
        var plan = LevelPreview(LevelUpTarget.Character);
        int level = plan?.Result.Level ?? c.Level;
        int ascension = plan?.TargetAscension ?? c.Ascension;
        string element = ElementLabel(info.Element);
        string constellation = T($"{c.ConstellationLevel} 命", $"C{c.ConstellationLevel}");
        Heading(body, CharacterName(c.CharacterID),
            $"{element}  ·  {info.Nation}  ·  {WeaponType(info.WeaponType)}  ·  {constellation}  ·  {Range(Lv(c.Level), Lv(level))} / {session.Limits.CharacterAndWeaponLevel}",
            CharacterListPortraitPath(c.CharacterID), info.Star, UIItemListIcon.IconKind.Character, 76, info.Element);
        var current = RoguelikeRosterStats.Read(session, c, c.Level, c.Ascension);
        var target = level == c.Level && ascension == c.Ascension ? current : RoguelikeRosterStats.Read(session, c, level, ascension);
        void Group(IEnumerable<string> keys)
        {
            var rows = keys.Where(current.ContainsKey).ToArray();
            for (int i = 0; i < rows.Length; i++)
            {
                string key = rows[i];
                StatRow(body, key == "HP" ? T("生命值上限", "Max HP") : StatName(key),
                    Range(StatValue(key, current[key]), StatValue(key, target[key])), i < rows.Length - 1);
            }
        }
        Group(new[] { "HP", "ATK", "DEF", "EMBonus" });
        SectionSpace(body);
        Group(new[] { "CritRate", "CritDMG", "HealBonus", "BeHealedBonus", "RechargeBonus", "ShieldStrength" });
        SectionSpace(body);
        Group(AttributeElementOrder.SelectMany(key => new[] { key + "DMGBonus", key + "RES" }));
        var upgradeTarget = RoguelikeUpgradeTarget.Character(c.CharacterID);
        Block(Button(footer, Ui("btn_upgrade_character", "角色升级", "Level Up Character"),
            () => OpenTraining(upgradeTarget), 216), UpgradeBlock(upgradeTarget));
    }

    private string ElementLabel(string element) => T(element switch
    {
        "Pyro" => "火元素", "Hydro" => "水元素", "Dendro" => "草元素", "Electro" => "雷元素",
        "Anemo" => "风元素", "Cryo" => "冰元素", "Geo" => "岩元素", _ => element
    }, element);

    private static void SectionSpace(RectTransform parent)
        => Rect("Section spacing", parent).gameObject.AddComponent<LayoutElement>().minHeight = 12;

    private (RectTransform Row, TMP_Text Label, TMP_Text Value) StatRow(RectTransform parent, string title, string value, bool rule = true)
    {
        var row = Rect("Attribute row", parent);
        row.gameObject.AddComponent<LayoutElement>().minHeight = 44;
        var left = Rect("Name", row); left.anchorMin = Vector2.zero; left.anchorMax = new Vector2(.55f, 1);
        left.offsetMin = Vector2.zero;
        left.offsetMax = new Vector2(-6, 0);
        var right = Rect("Value", row); right.anchorMin = new Vector2(.55f, 0); right.anchorMax = Vector2.one;
        right.offsetMin = new Vector2(6, 0);
        right.offsetMax = new Vector2(-2, 0);
        var nameText = FitLine(Label(left, title, 24), 16);
        var valueText = FitLine(Label(right, value, 24, TextAlignmentOptions.Right), 16);
        if (rule) BottomRule(row, Rule);
        return (row, nameText, valueText);
    }

    private void RenderCharacterWeapon(RectTransform body, RectTransform footer, RoguelikeCharacterProgressData c)
    {
        var equipped = session.Weapon(c.EquippedWeaponInstanceId);
        var viewed = picking ? pickerRemove ? null : session.Weapon(pickerSelection) ?? equipped : equipped;
        var preview = PreviewPicker(body, c, equipped?.InstanceId);
        if (viewed == null) Heading(body, T("未装备武器", "No weapon equipped"),
            WeaponType(session.Data.CharacterAttributesDict[c.CharacterID].WeaponType), "");
        else ShowWeapon(body, viewed, preview != null && viewed != equipped ? equipped : null);
        RenderGearChanges(body, c, preview);
        GearActions(footer, equipped?.InstanceId, true, false);
        Block(Button(footer, equipped != null ? Ui("btn_change_weapon", "更换武器", "Change Weapon") :
            Ui("btn_equip_weapon", "装备武器", "Equip Weapon"), OpenPicker, 200, true), ReadOnlyReason);
    }

    private RoguelikeArtifactInstanceData EquippedArtifact(RoguelikeCharacterProgressData c)
        => session.Artifact(c.EquippedArtifacts.Find(a => a.Slot == artifactSlot)?.ArtifactInstanceId);

    private void RenderCharacterArtifact(RectTransform body, RectTransform footer, RoguelikeCharacterProgressData c)
    {
        bool wide = !picking && training == null;
        float column = (detailPanelWidth - 74f - ScrollTextGutter - 4f * 8f) / 5f;
        float iconSize = Mathf.Min(112, column - (wide ? 50 : 0));
        var slots = Row(body, iconSize + 44);
        foreach (ArtifactSlot slot in Enum.GetValues(typeof(ArtifactSlot)))
        {
            var item = session.Artifact(c.EquippedArtifacts.Find(a => a.Slot == slot)?.ArtifactInstanceId);
            void Select()
            {
                if (artifactSlot == slot) return;
                artifactSlot = slot;
                pickerSelection = null;
                pickerRemove = false;
                if (training != null) RetargetTraining(c, CharacterTab.Artifact);
                if (picking || training != null || side.gameObject.activeSelf) Render();
                else RefreshDetail();
            }
            var cell = Rect("Artifact slot", slots);
            cell.sizeDelta = new Vector2(wide ? column : iconSize, iconSize + 26);
            float offset = 0;
            if (wide)
            {
                var glyph = Image(DetailArtPath + ArtifactSlotArt[(int)slot - 1] + ".png", cell);
                Place((RectTransform)glyph.transform, 0, (iconSize - 45) / 2f, 45, 45);
                glyph.color = RosterDeep;
                offset = 50;
            }
            var mount = Box("Slot tile", cell, offset, 0, iconSize, iconSize + 26);
            if (item != null) ArtifactTile(mount, item, Select, iconSize, slot == artifactSlot);
            else
            {
                var empty = Button(mount, "＋\n" + Slot(slot), Select, iconSize, slot == artifactSlot, iconSize + 26, 20);
                empty.GetComponentInChildren<TMP_Text>().enableWordWrapping = true;
            }
        }
        BottomRule(slots, Rule);
        var equipped = EquippedArtifact(c);
        var viewed = picking ? pickerRemove ? null : session.Artifact(pickerSelection) ?? equipped : equipped;
        var preview = PreviewPicker(body, c, equipped?.InstanceId);
        var sets = EquippedSetCounts(preview ?? c);
        if (viewed == null) Heading(body, T("未装备圣遗物", "No artifact equipped"), Slot(artifactSlot), "");
        else
        {
            sets.TryGetValue(viewed.ArtifactSetID, out int setCount);
            ShowArtifact(body, viewed, setCount, viewed != equipped ? equipped : null);
        }
        if (sets.Count != 0)
        {
            const int gap = 6;
            float available = detailPanelWidth - 74 - ScrollTextGutter;
            float used = 0;
            RectTransform row = null;
            SectionSpace(body);
            foreach (var set in sets)
            {
                int count = set.Value;
                string name = texts.SetName(set.Key, english) + T($" {count} 件", $" {count} pcs");
                var chip = Rect("Equipped set", body);
                var face = Rect("Fill", chip);
                var text = Label(face, name, 18, TextAlignmentOptions.Center);
                float width = Mathf.Min(available, text.GetPreferredValues(name).x + 16);
                if (row == null || used + width > available)
                {
                    row = Row(body, 30);
                    row.GetComponent<HorizontalLayoutGroup>().spacing = gap;
                    used = 0;
                }
                chip.SetParent(row, false);
                chip.sizeDelta = new Vector2(width, 30);
                var border = Fill(chip, Rule);
                SetRounded(border, Rule);
                Stretch(face, 1);
                SetRounded(Fill(face, RosterPaper), RosterPaper);
                text.enableWordWrapping = false;
                text.overflowMode = TextOverflowModes.Ellipsis;
                text.color = count >= 2 ? Ink : Muted;
                used += width + gap;
            }
        }
        RenderGearChanges(body, c, preview);
        GearActions(footer, equipped?.InstanceId, false, false);
        Block(Button(footer, equipped != null ? Ui("btn_change_artifact", "更换圣遗物", "Change Artifact") :
            Ui("btn_equip_artifact", "装备圣遗物", "Equip Artifact"), OpenPicker, 200, true), ReadOnlyReason);
    }

    private void RenderSkills(RectTransform body, RectTransform footer, RoguelikeCharacterProgressData c)
    {
        var info = session.Data.CharacterAttributesDict[c.CharacterID];
        string[] ids = { info.NormalAttackID, info.HeavyAttackID, info.ElementalSkillID, info.ElementalBurstID };
        session.Data.CharacterSkillDict.TryGetValue(c.CharacterID, out var skills);
        var options = ids.Select(id => skills?.Find(s => s.SkillID2 == id)).ToArray();
        var row = Row(body, 68);
        row.GetComponent<HorizontalLayoutGroup>().spacing = 4;
        float width = (detailPanelWidth - 74f - ScrollTextGutter - 12f) / 4f;
        for (int i = 0; i < 4; i++)
        {
            int choice = i;
            var button = Button(row, (english ? SkillNamesEn : SkillNames)[i], () =>
            {
                if (skillIndex == choice) return;
                skillIndex = choice;
                RefreshDetail();
            }, width, skillIndex == i, 65, 20);
            StyleButton(button, skillIndex == i ? Selected : Color.clear, Color.clear);
            var mount = (RectTransform)button.transform;
            var text = button.GetComponentInChildren<TMP_Text>();
            float labelWidth = Mathf.Min(86, text.preferredWidth + 2);
            float left = (width - labelWidth - 63) * .5f;
            Place(text.rectTransform, left, 4, labelWidth, 57);
            IconDisc(Box("Skill icon", mount, left + labelWidth + 12, 7, 51, 51), options[i]?.Icon, info.Element);
            if (i > 0) Fill(Box("Separator", mount, -2, 14, 1, 37), Rule);
            if (skillIndex == i) Fill(Box("Active skill", mount, (width - 36) / 2, 62, 36, 3), RosterDeep);
        }
        var skill = options[skillIndex];
        var levels = c.GetSkillLevelInfo(session.Data);
        string blocked = ReadOnlyReason;
        if (blocked != null) skillMode = false;
        bool atCap = c.SkillLevels[skillIndex] >= session.Limits.SkillBaseLevel;
        RoguelikeSkillUpgradePreview preview = null;
        CharacterSkillLevelInfo target = null;
        if (skillMode && !atCap)
        {
            int next = c.SkillLevels[skillIndex] + 1;
            preview = session.PreviewSkillUpgrade(c.CharacterID, skillIndex, next);
            var projected = RoguelikeSaveJson.DeepClone(c);
            projected.SkillLevels[skillIndex] = next;
            target = projected.GetSkillLevelInfo(session.Data);
        }
        string skillName = skill == null ? (english ? SkillNamesEn : SkillNames)[skillIndex] :
            session.Data.GetUiName(UiNameKind.Skill, skill.SkillID, skill.SkillName, english);
        Heading(body, skillName, Range(Lv(levels.GetOwnLevel(skillIndex)), Lv((target ?? levels).GetOwnLevel(skillIndex))));
        Label(body, texts.CharacterDescription($"character_{c.CharacterID}_{SkillKeys[skillIndex]}_desc",
            skill?.Description, levels, english, target));
        if (!skillMode)
        {
            Block(Button(footer, Ui("btn_upgrade_skill", "技能升级", "Level Up Skill"),
                () => { skillMode = true; RefreshDetail(); }, 216), blocked);
            return;
        }
        footer.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleRight;
        Button(footer, Ui("btn_cancel", "取消", "Cancel"), () => { skillMode = false; RefreshDetail(); }, 118);
        if (atCap)
        {
            if (c.SkillLevels[skillIndex] < RoguelikeUpgradeLimits.MaximumSkillBaseLevel)
                Label(footer, ChapterLevelCap, 24, TextAlignmentOptions.Center).color = Muted;
            return;
        }
        foreach (var cost in preview.Costs)
            if (session.Items.TryGetItem(cost.ItemId, out var material))
                MaterialCostTile(footer, material, cost.Quantity, 78);
        string shortage = null;
        if (!preview.CanAfford)
        {
            var cost = preview.Costs.FirstOrDefault(entry => session.Count(entry.ItemId) < entry.Quantity);
            shortage = cost == null ? NoMaterialsReason : MaterialShortage(cost.ItemId, cost.Quantity, session.Count(cost.ItemId));
        }
        Block(Button(footer, Ui("btn_confirm_upgrade", "确认升级", "Confirm Upgrade"), () =>
        {
            int index = skillIndex;
            var result = session.Upgrade(tx => tx.UpgradeSkill(c.CharacterID, index, preview.TargetBaseLevel));
            if (!result.Succeeded) { SetMessage(result.Message); return; }
            RefreshDetail();
        }, 216), shortage);
    }

    private void RenderTalents(RectTransform body, RoguelikeCharacterProgressData c)
    {
        if (!session.Data.TalentDict.TryGetValue(c.CharacterID, out var items)) return;
        var levels = c.GetSkillLevelInfo(session.Data);
        string element = session.Data.CharacterAttributesDict[c.CharacterID].Element;
        Heading(body, T("角色天赋", "Passive Talents"), T("角色突破进度", "Character Ascension"));
        foreach (var talent in items.OrderBy(t => t.TalentIndex))
        {
            bool active = CharacterAscensionRules.IsTalentUnlocked(talent.UnlockAfter, c.Level, c.Ascension);
            string name = session.Data.GetUiName(UiNameKind.Talent, c.CharacterID, talent.TalentName, english, talent.TalentIndex);
            var row = Rect("Talent", body);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 12;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.padding = new RectOffset(0, 0, 6, 14);
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            var disc = Rect("Talent icon", row);
            disc.sizeDelta = new Vector2(51, 51);
            var discSize = disc.gameObject.AddComponent<LayoutElement>();
            discSize.minWidth = discSize.minHeight = 51;
            IconDisc(disc, $"{c.CharacterID}_T{talent.TalentIndex:00}", active ? element : null);
            var details = Rect("Talent text", row);
            details.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            ColumnLayout(details);
            Label(details, name, 24);
            Label(details, talent.UnlockAfter == 0 ? T("固有天赋", "Innate Talent") : active ? T("已解锁", "Unlocked") :
                T($"达到 Lv. {talent.UnlockAfter} 并突破后解锁", $"Unlocks after ascending at Lv. {talent.UnlockAfter}"), 20).color = Muted;
            Label(details, texts.CharacterDescription($"character_{c.CharacterID}_talent_{talent.TalentIndex}_desc",
                talent.Description, levels, english));
        }
    }

    private void RenderConstellations(RectTransform parent, RoguelikeCharacterProgressData c)
    {
        if (!session.Data.ConstellationDict.TryGetValue(c.CharacterID, out var rows)) return;
        var items = rows.OrderBy(item => item.ConstellationIndex).ToArray();
        if (items.Length == 0) return;
        constellationIndex = Mathf.Clamp(constellationIndex, 0, items.Length - 1);
        const float listWidth = 276;
        var list = Rect("Constellation list", parent);
        list.anchorMin = Vector2.zero;
        list.anchorMax = new Vector2(0, 1);
        list.offsetMin = Vector2.zero;
        list.offsetMax = new Vector2(listWidth, -24);
        string Name(ConstellationData entry) => session.Data.GetUiName(UiNameKind.Constellation, c.CharacterID,
            entry.ConstellationName, english, entry.ConstellationIndex);
        for (int i = 0; i < items.Length; i++)
        {
            int index = i;
            var entry = items[i];
            bool active = entry.ConstellationIndex <= c.ConstellationLevel;
            bool selected = constellationIndex == i;
            var button = Button(list, Name(entry), () => { constellationIndex = index; RefreshDetail(); }, listWidth, selected,
                fontSize: 27);
            var row = (RectTransform)button.transform;
            row.anchorMin = new Vector2(0, 1f - (i + 1f) / items.Length);
            row.anchorMax = new Vector2(1, 1f - (float)i / items.Length);
            row.offsetMin = new Vector2(0, 2);
            row.offsetMax = new Vector2(0, -2);
            StyleButton(button, selected ? Selected : Color.clear, Color.clear);

            var label = button.GetComponentInChildren<TMP_Text>();
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.enableWordWrapping = true;
            label.fontSizeMin = 22;
            ((RectTransform)label.transform).offsetMin = new Vector2(62, 6);
            var badge = Rect("Constellation icon", row);
            badge.anchorMin = badge.anchorMax = new Vector2(0, .5f);
            badge.anchoredPosition = new Vector2(33.5f, 0);
            badge.sizeDelta = new Vector2(43, 43);
            IconDisc(badge, $"{c.CharacterID}_C{entry.ConstellationIndex:00}",
                active ? session.Data.CharacterAttributesDict[c.CharacterID].Element : null);
            if (selected)
            {
                var marker = Rect("Active constellation", row);
                marker.anchorMin = marker.anchorMax = new Vector2(0, .5f);
                marker.sizeDelta = new Vector2(3, 32);
                Fill(marker, RosterDeep);
            }
            if (i < items.Length - 1) BottomRule(row, Rule, 10);
        }
        var item = items[constellationIndex];
        var details = Rect("Constellation details", parent);
        Stretch(details);
        details.offsetMin = new Vector2(listWidth + constellationColumnGap, 0);
        var body = DetailScroll(details, $"constellation:{c.CharacterID}:{item.ConstellationIndex}", out var footer, 116);
        footer.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleRight;
        string status = item.ConstellationIndex <= c.ConstellationLevel ? T("已激活", "Active") :
            item.ConstellationIndex == c.ConstellationLevel + 1 ? T("可按顺序激活", "Ready to activate") :
            T("需先激活前一层", "Activate the previous constellation first");
        Heading(body, Name(item), status);
        var levels = c.GetSkillLevelInfo(session.Data);
        Label(body, texts.CharacterDescription($"character_{c.CharacterID}_constellation_{item.ConstellationIndex}_desc",
            item.Description, levels, english));
        string id = RoguelikeItemIds.Constellation(c.CharacterID);
        int owned = session.Count(id);
        if (session.Items.TryGetItem(id, out var definition)) MaterialCostTile(footer, definition, 1, 84);
        string blocked = ReadOnlyReason;
        if (blocked == null && c.ConstellationLevel >= items.Length)
            blocked = Ui("tip_upgrade_constellation_max", "角色命座已全部激活。", "All constellations are active.");
        if (blocked == null && item.ConstellationIndex <= c.ConstellationLevel)
            blocked = Ui("tip_upgrade_constellation_already_active", "这层命座已激活。", "This constellation is already active.");
        if (blocked == null && item.ConstellationIndex != c.ConstellationLevel + 1)
            blocked = Ui("tip_upgrade_constellation_order", "请先激活前一层命座。", "Activate the previous constellation first.");
        if (blocked == null && owned < 1)
            blocked = Ui("tip_upgrade_constellation_missing_item", $"缺少{CharacterName(c.CharacterID)}的命之座。",
                $"A Stella Fortuna for {CharacterName(c.CharacterID)} is required.", CharacterName(c.CharacterID));
        Block(Button(footer, Ui("btn_upgrade_constellation", "激活命座", "Activate Constellation"),
            () => Commit(session.Upgrade(tx => tx.ActivateConstellation(c.CharacterID))), 270), blocked);
    }
}
