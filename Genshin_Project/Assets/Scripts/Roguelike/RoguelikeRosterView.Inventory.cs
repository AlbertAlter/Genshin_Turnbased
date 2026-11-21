using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class RoguelikeRosterView
{
    private Action refreshPickerResults;
    private Action refreshBagGear;
    private GearWindow bagGearWindow;
    private Action refreshBagPanels;
    private readonly struct GearEntry
    {
        public readonly string Id, Art, Value;
        public readonly int Rarity, Rank, Owner;
        public readonly bool Locked;
        public readonly UIItemListIcon.IconKind Kind;

        public bool SameAppearance(GearEntry other) => Art == other.Art && Value == other.Value &&
            Rarity == other.Rarity && Rank == other.Rank && Owner == other.Owner && Locked == other.Locked && Kind == other.Kind;

        public GearEntry(string id, string art, string value, int rarity, int rank, int owner, bool locked,
            UIItemListIcon.IconKind kind)
        {
            Id = id;
            Art = art;
            Value = value;
            Rarity = rarity;
            Rank = rank;
            Owner = owner;
            Locked = locked;
            Kind = kind;
        }
    }

    private sealed class GearWindow
    {
        private sealed class Cell
        {
            public UIItemListIcon Tile;
            public int Revision;
            public float Size;
            public GearEntry Entry;
        }

        private readonly RoguelikeRosterView view;
        private readonly ScrollRect scroll;
        private int columns;
        private float size, step;
        private readonly Func<string, bool> selected;
        private readonly Action<string> select;
        private readonly Dictionary<string, Cell> visible = new(StringComparer.Ordinal);
        private readonly Stack<Cell> pool = new();
        private readonly HashSet<string> wanted = new(StringComparer.Ordinal);
        private readonly List<string> offscreen = new();
        private int firstVisible = -1, endVisible;
        private bool entriesChanged;
        private GearEntry[] entries = Array.Empty<GearEntry>();

        public GearWindow(RoguelikeRosterView view, ScrollRect scroll, float width, float minimum,
            Func<string, bool> selected, Action<string> select)
        {
            this.view = view;
            this.scroll = scroll;
            SetWidth(width, minimum);
            this.selected = selected;
            this.select = select;
            scroll.content.GetComponent<ContentSizeFitter>().enabled = false;
            scroll.onValueChanged.AddListener(_ => ShowVisible());
        }

        private void SetWidth(float width, float minimum)
        {
            columns = Mathf.Max(1, Mathf.FloorToInt(width / (minimum + 8)));
            size = (width - 8 - 8 * columns) / columns;
            step = UIItemListIcon.GetCellSize(size, true).y + 8;
        }

        public void ResizeForSelection(string id, float width, float minimum)
        {
            int index = Array.FindIndex(entries, entry => entry.Id == id);
            var offset = scroll.content.anchoredPosition;
            if (index < 0) index = Mathf.Max(0, Mathf.FloorToInt(offset.y / step)) * columns;
            float screenY = index / columns * step - offset.y;
            SetWidth(width, minimum);
            offset.y = index / columns * step - screenY;
            scroll.content.anchoredPosition = offset;
            SetEntries(entries, false);
        }

        public void SetEntries(GearEntry[] values, bool reset)
        {
            entries = values;
            int rows = (entries.Length + columns - 1) / columns;
            scroll.content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, rows * step);
            var offset = scroll.content.anchoredPosition;
            offset.y = reset ? 0 : Mathf.Clamp(offset.y, 0, Mathf.Max(0, rows * step - scroll.viewport.rect.height));
            scroll.StopMovement();
            scroll.content.anchoredPosition = offset;
            entriesChanged = true;
            ShowVisible();
        }

        public void UpdateSelection()
        {
            foreach (var pair in visible) pair.Value.Tile.SetSelected(selected(pair.Key));
        }

        private void Recycle(Cell cell)
        {
            cell.Revision++;
            cell.Tile.gameObject.SetActive(false);
            pool.Push(cell);
        }

        private Cell TakeCell()
        {
            // 优先复用本次刚移出视口的格子，避免同一帧先停用再启用整棵 UI。
            if (offscreen.Count > 0)
            {
                int last = offscreen.Count - 1;
                string oldId = offscreen[last];
                offscreen.RemoveAt(last);
                var cell = visible[oldId];
                visible.Remove(oldId);
                return cell;
            }
            if (pool.Count == 0)
                return new Cell { Tile = view.CreateTile(scroll.content, size, true), Size = size };
            var spare = pool.Pop();
            spare.Tile.gameObject.SetActive(true);
            return spare;
        }

        private void ShowVisible()
        {
            if (!scroll.gameObject.activeInHierarchy) return;
            float top = Mathf.Max(0, scroll.content.anchoredPosition.y);
            int start = Mathf.Max(0, Mathf.FloorToInt(top / step) - 1) * columns;
            int end = Mathf.Min(entries.Length, (Mathf.CeilToInt((top + scroll.viewport.rect.height) / step) + 1) * columns);
            bool refresh = entriesChanged;
            if (!refresh && start == firstVisible && end == endVisible) return;
            entriesChanged = false;
            firstVisible = start;
            endVisible = end;
            wanted.Clear();
            for (int index = start; index < end; index++) wanted.Add(entries[index].Id);
            offscreen.Clear();
            foreach (string id in visible.Keys)
                if (!wanted.Contains(id)) offscreen.Add(id);
            for (int index = start; index < end; index++)
            {
                var entry = entries[index];
                bool retained = visible.TryGetValue(entry.Id, out var cell);
                if (retained && !refresh) continue;
                if (!retained)
                {
                    cell = TakeCell();
                    visible.Add(entry.Id, cell);
                }
                bool rebind = !retained || !cell.Entry.SameAppearance(entry) || (entry.Locked && cell.Size != size);
                if (cell.Size != size)
                {
                    cell.Tile.ResizeCompact(size);
                    cell.Size = size;
                }
                Place((RectTransform)cell.Tile.transform, 4 + index % columns * (size + 8), 4 + index / columns * step,
                    size, step - 8);
                if (!rebind)
                {
                    cell.Tile.SetSelected(selected(entry.Id));
                    continue;
                }
                cell.Entry = entry;
                int revision = ++cell.Revision;
                view.BindTile(cell.Tile, entry.Art, entry.Rarity, entry.Value, entry.Kind, () => select(entry.Id),
                    size, () => selected(entry.Id), entry.Rank, entry.Owner, entry.Locked, () => cell.Revision == revision);
            }
            foreach (string id in offscreen)
            {
                Recycle(visible[id]);
                visible.Remove(id);
            }
        }
    }

    private GearWindow GearGrid(RectTransform parent, string key, float width, float minimum, float top,
        Func<string, bool> selected, Action<string> select)
    {
        var host = Rect("Gear grid", parent);
        Stretch(host);
        host.offsetMax = new Vector2(0, -top);
        const int scrollbarSpace = 8;
        Scroll(host, key, scrollbarSpace);
        var scroll = scrolls[key];
        // 虚拟网格自己计算总高度。第一次绑定就从记住的行开始，避免先绑定顶部再跳回原位。
        if (scrollPositions.TryGetValue(key, out float offset))
        {
            scroll.content.anchoredPosition = new Vector2(scroll.content.anchoredPosition.x, offset);
            scrollPositions.Remove(key);
        }
        return new GearWindow(this, scroll, width, minimum, selected, select);
    }

    private GearEntry WeaponEntry(RoguelikeWeaponInstanceData item)
        => new GearEntry(item.InstanceId, WeaponArt(item.Weapon.WeaponID), Lv(item.Weapon.Level),
            session.Data.WeaponAttributesDict[item.Weapon.WeaponID].Star, item.Weapon.Refinement,
            session.Owner(item.InstanceId)?.CharacterID ?? 0, item.IsLocked, UIItemListIcon.IconKind.Weapon);

    private GearEntry ArtifactEntry(RoguelikeArtifactInstanceData item)
        => new GearEntry(item.InstanceId, ArtifactArt(item), "+" + item.Artifact.Level, item.Artifact.Star,
            0, session.Owner(item.InstanceId)?.CharacterID ?? 0, item.IsLocked, UIItemListIcon.IconKind.Artifact);

    private static string ArtifactArt(RoguelikeArtifactInstanceData a) =>
        a.ArtifactSetID <= 0 ? "" : $"art_assets/Artifact_Icon/{a.ArtifactSetID}{(int)a.Artifact.Slot}.png";

    private UIItemListIcon WeaponTile(RectTransform parent, RoguelikeWeaponInstanceData item, Action click, float size = 106, bool selected = false)
    {
        if (!session.Data.WeaponAttributesDict.TryGetValue(item.Weapon.WeaponID, out var w)) return null;
        return Tile(parent, WeaponArt(w.WeaponID), w.Star, Lv(item.Weapon.Level), UIItemListIcon.IconKind.Weapon, click,
            size, selected, item.Weapon.Refinement, session.Owner(item.InstanceId)?.CharacterID ?? 0, item.IsLocked, compact: true);
    }

    private void ArtifactTile(RectTransform parent, RoguelikeArtifactInstanceData item, Action click, float size = 106, bool selected = false)
        => Tile(parent, ArtifactArt(item), item.Artifact.Star, "+" + item.Artifact.Level, UIItemListIcon.IconKind.Artifact, click,
            size, selected, owner: session.Owner(item.InstanceId)?.CharacterID ?? 0, locked: item.IsLocked, compact: true);

    private static string MaterialArt(RoguelikeItemDefinition item)
    {
        if (!string.IsNullOrWhiteSpace(item.Icon))
            return item.Icon.StartsWith("art_assets/", StringComparison.Ordinal) ? item.Icon : "art_assets/" + item.Icon;
        return item.ItemType == RoguelikeItemType.Stack && item.ItemId.All(char.IsDigit)
            ? $"art_assets/ItemIcon/{item.ItemId}.png" : "";
    }

    private UIItemListIcon MaterialTile(RectTransform parent, RoguelikeItemDefinition item, string value, Action click, float size = 106,
        bool selected = false, Func<bool> selection = null, bool compact = true)
    {
        int owner = item.Category == "Constellation" ? item.ReferenceID : 0;
        int rarity = item.Rarity;
        if (owner > 0 && rarity == 0)
            rarity = item.Icon?.EndsWith("Stella5.png", StringComparison.OrdinalIgnoreCase) == true ? 5 : 4;
        return Tile(parent, MaterialArt(item), rarity, value, UIItemListIcon.IconKind.Material, click, size, selected, owner: owner,
            compact: compact, selection: selection);
    }

    private void MaterialCostTile(RectTransform parent, RoguelikeItemDefinition item, int required, float size)
    {
        var tile = MaterialTile(parent, item, $"{required}/{session.Count(item.ItemId)}", null, size, compact: false);
        tile.FitValueText(19, 13);
    }

    private void GearHeading(RectTransform body, string title, string subtitle, string art, int rarity, string id, bool locked,
        UIItemListIcon.IconKind kind)
    {
        var row = Heading(body, title, subtitle, art, rarity, kind, trailing: 46);
        var lockMount = Rect("Equipment lock", row);
        lockMount.anchorMin = lockMount.anchorMax = lockMount.pivot = Vector2.one;
        lockMount.sizeDelta = new Vector2(40, 40);
        var lockButton = Button(lockMount, "", () => Commit(session.Equipment(tx => tx.SetEquipmentLocked(id, !locked))),
            40, locked, 40);
        Block(lockButton, ReadOnlyReason);
        var lockColor = new Color32(127, 45, 38, 255);
        StyleButton(lockButton, locked ? new Color32(245, 187, 173, 69) : Color.clear, locked ? lockColor : Color.clear);
        var icon = Image(DetailArtPath + (locked ? "Lock.png" : "Unlock.png"), (RectTransform)lockButton.transform);
        Stretch((RectTransform)icon.transform, 6);
        icon.color = locked ? lockColor : Ink;
    }

    private void ShowWeapon(RectTransform body, RoguelikeWeaponInstanceData item, RoguelikeWeaponInstanceData previous = null)
    {
        var w = item.Weapon;
        if (!session.Data.WeaponAttributesDict.TryGetValue(w.WeaponID, out var def)) return;
        var level = LevelPreview(LevelUpTarget.Weapon);
        bool applies = training?.Target.InstanceId == item.InstanceId;
        int targetLevel = applies ? level?.Result.Level ?? w.Level : w.Level;
        int targetAscension = applies ? level?.TargetAscension ?? w.Ascension : w.Ascension;
        int targetRank = applies && training.IsRefinement ? RefinementPreview(w.Refinement) : w.Refinement;
        GearHeading(body, WeaponName(def), $"{WeaponType(def.WeaponType)}  ·  {T($"{def.Star} 星", $"{def.Star}★")}  ·  {Range(Lv(w.Level), Lv(targetLevel))}",
            WeaponArt(w.WeaponID), def.Star, item.InstanceId, item.IsLocked, UIItemListIcon.IconKind.Weapon);
        var before = previous?.Weapon ?? w;
        var current = session.Data.GetWeaponPanel(before.WeaponID, before.Level, before.Ascension);
        var projected = session.Data.GetWeaponPanel(w.WeaponID, targetLevel, targetAscension);
        StatRow(body, T("武器基础攻击力", "Base Weapon ATK"), Range(StatValue("ATK", current.BaseATK), StatValue("ATK", projected.BaseATK)));
        if (!string.IsNullOrEmpty(current.SecondaryAttribute) && current.SecondaryAttribute != projected.SecondaryAttribute)
            StatRow(body, StatName(current.SecondaryAttribute),
                Range(StatValue(current.SecondaryAttribute, current.SecondaryValue), StatValue(current.SecondaryAttribute, 0)));
        if (!string.IsNullOrEmpty(projected.SecondaryAttribute))
        {
            string k = projected.SecondaryAttribute;
            StatRow(body, StatName(k), Range(StatValue(k, current.SecondaryAttribute == k ? current.SecondaryValue : 0),
                StatValue(k, projected.SecondaryValue)));
        }
        SectionSpace(body);
        Label(body, Range(T($"精炼 {w.Refinement} 阶", $"Refinement Rank {w.Refinement}"),
            T($"精炼 {targetRank} 阶", $"Refinement Rank {targetRank}")), 20).color = Muted;
        string key = $"weapon_{w.WeaponID}_effect_title";
        if (session.Data.LangDict.ContainsKey(key)) Label(body, "• " + session.Data.GetLangText(key, english) + " •", 26);
        Label(body, texts.WeaponDescription(def, w.Refinement, targetRank, english));
    }

    private void ShowArtifact(RectTransform body, RoguelikeArtifactInstanceData item, int? equippedCount,
        RoguelikeArtifactInstanceData previous = null)
    {
        var a = item.Artifact;
        var plan = LevelPreview(LevelUpTarget.Artifact);
        int level = training?.Target.InstanceId == item.InstanceId ? plan?.Result.Level ?? a.Level : a.Level;
        GearHeading(body, texts.SetName(item.ArtifactSetID, english),
            $"{Slot(a.Slot)}  ·  {T($"{a.Star} 星", $"{a.Star}★")}  ·  {Range("+" + a.Level, "+" + level)} / +{a.MaxLevel}",
            ArtifactArt(item), a.Star, item.InstanceId, item.IsLocked, UIItemListIcon.IconKind.Artifact);
        float target = a.MainAttributeValue;
        if (level != a.Level)
        {
            var growth = session.Artifacts.GetMainGrowth(a.MainAttributeType, a.Star);
            target = growth.InitialStat + growth.LevelBonus * level;
        }
        var before = previous?.Artifact ?? a;
        if (before.MainAttributeType != a.MainAttributeType)
            StatRow(body, StatName(before.MainAttributeType), Range(StatValue(before.MainAttributeType, before.MainAttributeValue),
                StatValue(before.MainAttributeType, 0)));
        StatRow(body, StatName(a.MainAttributeType), Range(StatValue(a.MainAttributeType,
            before.MainAttributeType == a.MainAttributeType ? before.MainAttributeValue : 0), StatValue(a.MainAttributeType, target)));
        Label(body, T("副词条", "Substats"), 24);
        foreach (var stat in a.SecondaryAttributes)
            Label(body, StatName(stat.AttributeType) + " +" + StatValue(stat.AttributeType, stat.Value), 22);
        if (level > a.Level)
        {
            int pending = Math.Min(level / 4 - a.Level / 4, Math.Max(0, 4 - a.SecondaryAttributes.Count));
            for (int i = 0; i < pending; i++)
                Label(body, Ui("ui_artifact_add_substat_preview", "强化后新增一条副词条", "A new substat unlocks after enhancement."), 20).color = Muted;
        }
        if (item.ArtifactSetID <= 0) return;
        SectionSpace(body);
        Label(body, T("套装效果", "Set Bonuses") + (equippedCount.HasValue ? T($" · {equippedCount} 件", $" · {equippedCount} Pieces") : ""), 24);
        foreach (int pieces in new[] { 2, 4 })
        {
            string bonus = texts.SetBonus(item.ArtifactSetID, pieces, english);
            if (string.IsNullOrWhiteSpace(bonus)) continue;
            var text = Label(body, T($"{pieces} 件套  ", $"{pieces}-Piece  ") + bonus);
            if (equippedCount.HasValue && equippedCount.Value < pieces) text.alpha = .55f;
        }
    }

    private void ApplyPickerChoice(RoguelikeInventoryTransaction draft, int character)
    {
        if (characterTab == CharacterTab.Weapon)
        {
            if (pickerRemove) draft.UnequipWeapon(character);
            else draft.TransferWeapon(character, pickerSelection);
        }
        else if (pickerRemove) draft.UnequipArtifact(character, artifactSlot);
        else draft.TransferArtifact(character, pickerSelection);
    }

    private bool CompatiblePickerChoice(RoguelikeCharacterProgressData c)
        => characterTab != CharacterTab.Weapon || session.Weapon(pickerSelection) is RoguelikeWeaponInstanceData w &&
            session.Data.WeaponAttributesDict[w.Weapon.WeaponID].WeaponType == session.Data.CharacterAttributesDict[c.CharacterID].WeaponType;

    private RoguelikeCharacterProgressData PreviewPicker(RectTransform body, RoguelikeCharacterProgressData c, string current)
    {
        if (!picking || (pickerRemove ? current == null :
                pickerSelection == null || pickerSelection == current || !CompatiblePickerChoice(c))) return null;
        var next = session.PreviewEquipment(c.CharacterID, tx => ApplyPickerChoice(tx, c.CharacterID));
        string empty = T("未装备", "Unequipped");
        Label(body, T("装备预览", "Equipment preview") + " · " +
            Range(current == null ? empty : GearName(current), pickerRemove ? empty : GearName(pickerSelection)), 20).color = Muted;
        return next;
    }

    private string GearName(string id)
    {
        if (characterTab == CharacterTab.Weapon && session.Weapon(id) is RoguelikeWeaponInstanceData w)
            return WeaponName(session.Data.WeaponAttributesDict[w.Weapon.WeaponID]);
        if (session.Artifact(id) is RoguelikeArtifactInstanceData a)
            return texts.SetName(a.ArtifactSetID, english) + " · " + Slot(a.Artifact.Slot);
        return "";
    }

    private Dictionary<int, int> EquippedSetCounts(RoguelikeCharacterProgressData c)
        => c.EquippedArtifacts.Select(a => session.Artifact(a.ArtifactInstanceId))
            .Where(a => a != null && a.ArtifactSetID > 0).GroupBy(a => a.ArtifactSetID)
            .ToDictionary(group => group.Key, group => group.Count());

    private void RenderGearChanges(RectTransform body, RoguelikeCharacterProgressData current, RoguelikeCharacterProgressData next)
    {
        if (next == null) return;
        var before = RoguelikeRosterStats.Read(session, current, current.Level, current.Ascension);
        var after = RoguelikeRosterStats.Read(session, next, next.Level, next.Ascension);
        var changes = before.Keys.Select(key => (Name: StatName(key), From: StatValue(key, before[key]),
            To: StatValue(key, after[key]))).Where(change => change.From != change.To).ToList();
        var oldSets = EquippedSetCounts(current);
        var newSets = EquippedSetCounts(next);
        foreach (int id in oldSets.Keys.Union(newSets.Keys))
        {
            oldSets.TryGetValue(id, out int oldCount);
            newSets.TryGetValue(id, out int newCount);
            if (oldCount != newCount) changes.Add((texts.SetName(id, english), oldCount.ToString(),
                T($"{newCount} 件", $"{newCount} pcs")));
        }
        if (changes.Count == 0) return;
        SectionSpace(body);
        Label(body, T("属性与套装变化", "Stat and set changes"), 24);
        foreach (var change in changes) StatRow(body, change.Name, Range(change.From, change.To));
    }

    private void GearActions(RectTransform footer, string id, bool weapon, bool showUnequip)
    {
        var w = weapon ? session.Weapon(id) : null;
        var artifact = weapon ? null : session.Artifact(id);
        var target = w != null ? RoguelikeUpgradeTarget.Weapon(id) : artifact != null ? RoguelikeUpgradeTarget.Artifact(id) : null;
        string upgradeReason = target != null ? UpgradeBlock(target) : ReadOnlyReason ??
            (weapon ? Ui("tip_upgrade_weapon_not_equipped", "请先装备武器。", "Equip a weapon first.") :
                Ui("tip_upgrade_artifact_not_equipped", "请先装备圣遗物。", "Equip an artifact first."));
        Block(Button(footer, weapon ? T("武器升级", "Level Up Weapon") : T("圣遗物强化", "Enhance Artifact"),
            () => OpenTraining(target), 184), upgradeReason);
        if (weapon)
            Block(Button(footer, T("精炼", "Refine"), () => OpenTraining(target, true), 106), RefinementBlock(w));
        var owner = showUnequip ? session.Owner(id) : null;
        if (owner != null)
            Block(Button(footer, Ui("btn_unequip", "卸下", "Unequip"), () => Commit(session.Equipment(tx =>
            {
                if (weapon) tx.UnequipWeapon(owner.CharacterID);
                else tx.UnequipArtifact(owner.CharacterID, artifact.Artifact.Slot);
            })), 110), ReadOnlyReason);
    }

    private void RenderBag()
    {
        var weapon = bagTab == BagTab.Weapons ? session.Weapon(bagSelection) : null;
        var artifact = bagTab == BagTab.Artifacts ? session.Artifact(bagSelection) : null;
        RoguelikeItemDefinition material = null;
        if ((bagTab == BagTab.Consumables || bagTab == BagTab.Quest) && !string.IsNullOrEmpty(bagSelection))
            session.Items.TryGetItem(bagSelection, out material);
        bool showDetail = weapon != null || artifact != null || material != null;
        Layout(false, showDetail, training != null);
        string[] names = english ? new[] { "Weapons", "Artifacts", "Consumables", "Quest Items" }
            : new[] { "武器", "圣遗物", "消耗道具", "任务道具" };
        var inventory = session.Snapshot.Inventory;
        names[(int)BagTab.Weapons] += $"  {inventory.Weapons.Count}/{RoguelikeInventoryData.MaxWeapons}";
        names[(int)BagTab.Artifacts] += $"  {inventory.Artifacts.Count}/{RoguelikeInventoryData.MaxArtifacts}";
        Tabs(navigation, names, (int)bagTab, index =>
        {
            bagTab = (BagTab)index;
            bagSelection = null;
            bagPage = bagPageSize = 0;
            ResetModes();
            Render();
        });
        bool gear = bagTab == BagTab.Weapons || bagTab == BagTab.Artifacts;
        var gridHost = Rect("Bag grid", listBody);
        Stretch(gridHost);
        if (!gear) gridHost.offsetMin = new Vector2(0, 60);
        var emptyBounds = Box("Empty bag", listBody, 0, gear ? 85 : 10, listPanelWidth - 64, 54);
        var empty = Label(emptyBounds, T("没有符合条件的道具。", "No matching items."), 22, TMPro.TextAlignmentOptions.Center);
        empty.color = Muted;
        if (gear)
        {
            var window = bagGearWindow = GearGrid(gridHost, "inventory:" + bagTab, listPanelWidth - 64, 112, 82,
                id => bagSelection == id, SelectBagItem);
            void DrawGear(bool reset)
            {
                var entries = bagTab == BagTab.Weapons
                    ? FilterWeapons(bagWeaponType, bagRarity, bagLevelSort).Select(WeaponEntry).ToArray()
                    : FilterArtifacts(null, bagArtifactFilters).Select(ArtifactEntry).ToArray();
                window.SetEntries(entries, reset);
                empty.gameObject.SetActive(entries.Length == 0);
            }
            var filters = RenderBagFilters(listBody, () => DrawGear(true));
            refreshBagPanels = () =>
            {
                Layout(false, true, training != null);
                window.ResizeForSelection(bagSelection, listPanelWidth - 64, 112);
                filters.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, listPanelWidth - 64);
                foreach (RectTransform field in filters)
                    field.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, BagFilterWidth);
                emptyBounds.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, listPanelWidth - 64);
                if (training != null) RefreshTrainingPanel();
                else RefreshDetail();
            };
            refreshBagGear = () => DrawGear(true);
            DrawGear(false);
        }
        else
        {
            bagGearWindow = null;
            var grid = EquipmentGrid(gridHost, "inventory:" + bagTab, listPanelWidth - 64, 112, 0, out float size);
            RenderPagedBagItems(grid, size);
            empty.gameObject.SetActive(grid.childCount == 0);
        }
        if (showDetail) RenderBagDetail();
        if (training != null) RenderTraining();
    }

    private void RenderBagDetail()
    {
        var weapon = bagTab == BagTab.Weapons ? session.Weapon(bagSelection) : null;
        var artifact = bagTab == BagTab.Artifacts ? session.Artifact(bagSelection) : null;
        RoguelikeItemDefinition material = null;
        if (weapon == null && artifact == null && !string.IsNullOrEmpty(bagSelection))
            session.Items.TryGetItem(bagSelection, out material);
        if (weapon == null && artifact == null && material == null) return;
        bool gear = weapon != null || artifact != null;
        var reliquary = material?.Category == RoguelikeItemCatalog.ArtifactReliquaryCategory
            ? session.Reliquaries.Read(session.Snapshot, material.ItemId) : null;
        var body = DetailScroll(detailBody, "inventory-detail:" + bagSelection, out var footer,
            gear ? 106 : reliquary != null ? 56 : 0);
        if (material != null)
        {
            Heading(body, ItemName(material), "×" + session.Count(material.ItemId), MaterialArt(material), material.Rarity,
                UIItemListIcon.IconKind.Material);
            Label(body, ItemDescription(material));
            if (reliquary != null) RenderReliquaryAction(footer, material, reliquary);
            return;
        }
        string id = weapon?.InstanceId ?? artifact.InstanceId;
        if (weapon != null) ShowWeapon(body, weapon);
        else ShowArtifact(body, artifact, null);
        if (weapon != null || artifact.ArtifactSetID > 0)
        {
            string itemId = weapon != null ? RoguelikeItemIds.Weapon(weapon.Weapon.WeaponID) :
                RoguelikeItemIds.Artifact(artifact.ArtifactSetID, artifact.Artifact.Slot);
            if (session.Items.TryGetItem(itemId, out var definition))
            {
                string description = ItemDescription(definition);
                if (!string.IsNullOrWhiteSpace(description)) Label(body, description, 20);
            }
        }
        var owner = session.Owner(id);
        var state = Box("Equipped by", detailBody, 0, 0, 0, 0);
        state.anchorMin = new Vector2(0, 0); state.anchorMax = new Vector2(1, 0);
        state.offsetMin = new Vector2(0, 54); state.offsetMax = new Vector2(0, 108);
        Horizontal(state, 9);
        if (owner != null)
        {
            var avatar = Image(CharacterListPortraitPath(owner.CharacterID), state);
            ((RectTransform)avatar.transform).sizeDelta = new Vector2(41, 41);
        }
        var ownerText = Label(state, owner == null ? T("未装备", "Unequipped") :
            T(CharacterName(owner.CharacterID) + " 装备中", "Equipped by " + CharacterName(owner.CharacterID)), 22);
        ((RectTransform)ownerText.transform).sizeDelta = new Vector2(480, 42);
        GearActions(footer, id, weapon != null, true);
    }

    private void RenderPagedBagItems(RectTransform grid, float size)
    {
        var entries = new List<(RoguelikeItemDefinition Item, int Quantity)>();
        foreach (var stack in session.Snapshot.Inventory.Items)
            if (session.Items.TryGetItem(stack.ItemId, out var item) && (item.Category == "Quest") == (bagTab == BagTab.Quest))
                entries.Add((item, stack.Quantity));
        int columns = grid.GetComponent<GridLayoutGroup>().constraintCount;
        int rows = Mathf.Max(1, Mathf.FloorToInt((panelBounds.height - 64 - 60) / (size + 26 + 8)));
        int capacity = columns * rows;
        if (capacity != bagPageSize)
        {
            int first = bagPage * bagPageSize;
            int selected = entries.FindIndex(entry => entry.Item.ItemId == bagSelection);
            bool selectedWasVisible = selected >= first && selected < first + bagPageSize;
            bagPage = (selectedWasVisible ? selected : first) / capacity;
            bagPageSize = capacity;
        }
        int pages = Mathf.Max(1, Mathf.CeilToInt(entries.Count / (float)capacity));
        bagPage = Mathf.Clamp(bagPage, 0, pages - 1);
        foreach (var entry in entries.Skip(bagPage * capacity).Take(capacity))
            MaterialTile(grid, entry.Item, "×" + entry.Quantity, () => SelectBagItem(entry.Item.ItemId), size,
                bagSelection == entry.Item.ItemId);
        var footer = Rect("Inventory pages", listBody);
        footer.anchorMin = Vector2.zero; footer.anchorMax = new Vector2(1, 0);
        footer.pivot = new Vector2(.5f, 0); footer.sizeDelta = new Vector2(0, 48);
        Horizontal(footer, 8, TextAnchor.MiddleRight);
        Block(Button(footer, Ui("btn_previous_page", "上一页", "Previous"), () => { bagPage--; Render(); }, 132),
            bagPage > 0 ? null : T("已是第一页。", "This is the first page."));
        var page = Label(footer, $"{bagPage + 1} / {pages}", 22, TMPro.TextAlignmentOptions.Center);
        page.rectTransform.sizeDelta = new Vector2(88, 44);
        Block(Button(footer, Ui("btn_next_page", "下一页", "Next"), () => { bagPage++; Render(); }, 132),
            bagPage + 1 < pages ? null : T("已是最后一页。", "This is the last page."));
    }

    private void SelectBagItem(string id)
    {
        if (bagSelection == id) return;
        bool opening = string.IsNullOrEmpty(bagSelection);
        bagSelection = id;
        if (bagGearWindow == null) { Render(); return; }
        if (opening) { refreshBagPanels(); return; }
        bagGearWindow.UpdateSelection();
        if (training != null)
        {
            bool weapon = bagTab == BagTab.Weapons;
            training = new RoguelikeRosterUpgradeControls(session,
                weapon ? RoguelikeUpgradeTarget.Weapon(id) : RoguelikeUpgradeTarget.Artifact(id),
                weapon && training.IsRefinement);
            upgradeMaterial = null;
            RefreshTrainingPanel();
        }
        else RefreshDetail();
    }

    private IEnumerable<RoguelikeWeaponInstanceData> FilterWeapons(int type, int rarity, bool levelSort)
        => session.Snapshot.Inventory.Weapons
            .Where(w => session.Data.WeaponAttributesDict.TryGetValue(w.Weapon.WeaponID, out var info) &&
                (type < 0 || info.WeaponType == type) && (rarity == 0 || info.Star == rarity))
            .OrderByDescending(w => levelSort ? w.Weapon.Level : session.Data.WeaponAttributesDict[w.Weapon.WeaponID].Star)
            .ThenByDescending(w => session.Data.WeaponAttributesDict[w.Weapon.WeaponID].Star)
            .ThenByDescending(w => w.Weapon.Level).ThenBy(w => w.Weapon.WeaponID);

    private IEnumerable<RoguelikeArtifactInstanceData> FilterArtifacts(ArtifactSlot? slot, ArtifactFilters filter = null)
    {
        var entries = session.Snapshot.Inventory.Artifacts.Where(a => !slot.HasValue || slot.Value == a.Artifact.Slot);
        if (filter == null)
            return entries.OrderByDescending(a => a.Artifact.Star).ThenByDescending(a => a.Artifact.Level);

        var mainStats = filter == pickerArtifactFilters && slot.HasValue ? PickerMainStats(slot.Value) : filter.Stats;
        int Rank(RoguelikeArtifactInstanceData item, ArtifactFilterKind kind) => kind switch
        {
            ArtifactFilterKind.Set => filter.Sets.Count == 0 ? 0 : filter.Sets.IndexOf(item.ArtifactSetID),
            ArtifactFilterKind.MainStat => mainStats.Count == 0 ? 0 : mainStats.IndexOf(item.Artifact.MainAttributeType),
            _ => filter.Substats.Count == 0 ? 0 : filter.Substats.FindIndex(stat =>
                item.Artifact.SecondaryAttributes.Any(roll => roll.AttributeType == stat)),
        };
        return entries.Select(item => (Item: item, First: Rank(item, filter.Order[0]),
                Second: Rank(item, filter.Order[1]), Third: Rank(item, filter.Order[2])))
            .Where(entry => entry.First >= 0 && entry.Second >= 0 && entry.Third >= 0)
            .OrderBy(entry => entry.First).ThenBy(entry => entry.Second).ThenBy(entry => entry.Third)
            .ThenByDescending(entry => entry.Item.Artifact.Star).ThenByDescending(entry => entry.Item.Artifact.Level)
            .Select(entry => entry.Item);
    }

    private List<string> PickerMainStats(ArtifactSlot slot)
    {
        if (!pickerStatsBySlot.TryGetValue(slot, out var stats)) pickerStatsBySlot.Add(slot, stats = new List<string>());
        return stats;
    }

    private float BagFilterWidth => Mathf.Min(bagTab == BagTab.Weapons ? 184 : 155, (listPanelWidth - 80) / 3f);

    private RectTransform RenderBagFilters(RectTransform parent, Action redrawArtifacts)
    {
        var filters = Box("Filters", parent, 0, 0, listPanelWidth - 64, 75);
        Horizontal(filters, 8);
        if (bagTab == BagTab.Weapons)
        {
            float width = BagFilterWidth;
            Filter(filters, T("类型", "Type"), bagWeaponType, WeaponOptions(), v => bagWeaponType = v, width, roster: true);
            Filter(filters, T("稀有度", "Rarity"), bagRarity, RarityOptions(), v => bagRarity = v, width, roster: true);
            Filter(filters, T("排序", "Sort"), bagLevelSort,
                new[] { (false, T("稀有度", "Rarity")), (true, T("等级", "Level")) }, v => bagLevelSort = v, width, roster: true);
        }
        else RenderArtifactFilters(filters, false, redrawArtifacts);
        return filters;
    }

    private void RenderArtifactFilters(RectTransform filters, bool picker, Action redraw = null)
    {
        var entries = FilterArtifacts(picker ? artifactSlot : (ArtifactSlot?)null).ToArray();
        var bySet = entries.Where(a => a.ArtifactSetID != 0).ToLookup(a => a.ArtifactSetID);
        var sets = bySet.Select(group => (group.Key, texts.SetName(group.Key, english), ArtifactArt(group.First()), group.Count()))
            .OrderBy(option => option.Item2);
        var stats = entries.ToLookup(a => a.Artifact.MainAttributeType)
            .Select(group => ArtifactStatOption(group.Key, group.Count()));
        var filter = picker ? pickerArtifactFilters : bagArtifactFilters;
        var substats = entries.SelectMany(a => a.Artifact.SecondaryAttributes).Select(stat => stat.AttributeType)
            .Concat(filter.Substats).Distinct().Select(key => ArtifactStatOption(key, entries.Count(a =>
                a.Artifact.SecondaryAttributes.Any(stat => stat.AttributeType == key))));
        float width = picker ? Mathf.Min(155, (sidePanelWidth - 68) / 3f) : BagFilterWidth;
        OrderedFilter(filters, T("套装", "Set"), filter.Sets, sets, () => filter.Prioritize(ArtifactFilterKind.Set), width, picker,
            (preview, id) =>
            {
                var item = bySet[id].FirstOrDefault();
                if (item == null) return;
                Heading(preview, texts.SetName(id, english), T($"{bySet[id].Count()} 件符合", $"{bySet[id].Count()} matches"),
                    ArtifactArt(item), item.Artifact.Star, UIItemListIcon.IconKind.Artifact, 76);
                foreach (int pieces in new[] { 2, 4 })
                {
                    string bonus = texts.SetBonus(id, pieces, english);
                    if (string.IsNullOrWhiteSpace(bonus)) continue;
                    Label(preview, T($"{pieces} 件套", $"{pieces}-Piece"), 22);
                    Label(preview, bonus, 20).color = Muted;
                }
            }, redraw);
        OrderedFilter(filters, T("主属性", "Main Stat"), picker ? PickerMainStats(artifactSlot) : filter.Stats,
            stats, () => filter.Prioritize(ArtifactFilterKind.MainStat), width, picker, immediate: redraw);
        OrderedFilter(filters, T("副属性", "Substat"), filter.Substats, substats,
            () => filter.Prioritize(ArtifactFilterKind.Substat), width, picker, immediate: redraw);
    }

    private (string Value, string Text, string Icon, int Count) ArtifactStatOption(string key, int count)
    {
        string icon = key switch
        {
            "HPBonus" or "HPBonusFlat" or "HealBonus" => "HealingEffectBuff",
            "ATKBonus" or "ATKBonusFlat" or "CritRate" or "CritDMG" or "PhysicalDMGBonus" => "AttackBuff",
            "DEFBonus" or "DEFBonusFlat" => "DefenseBuff",
            "RechargeBonus" => "APBuff",
            _ when key.EndsWith("DMGBonus", StringComparison.Ordinal) => key.Substring(0, key.Length - "DMGBonus".Length) + "Buff",
            _ => "GeneralBuff",
        };
        return (key, StatName(key), $"art_assets/Status_Icon/{icon}.png", count);
    }

    private void OpenPicker()
    {
        if (picking) return;
        trainingBeforePicker = training;
        materialBeforePicker = upgradeMaterial;
        training = null;
        materialHold = null;
        pickerRemove = false;
        pickerSelection = null;
        picking = true;
        pickerRarity = 0;
        pickerLevelSort = false;
        pickerWeaponType = null;
        pickerArtifactFilters.Sets.Clear();
        pickerStatsBySlot.Clear();
        pickerArtifactFilters.Substats.Clear();
        Render();
    }

    private void CloseEquipmentPicker(bool committed)
    {
        var previous = trainingBeforePicker;
        string material = materialBeforePicker;
        trainingBeforePicker = null;
        materialBeforePicker = null;
        if (previous == null && committed) { pickerRemove = false; return; }
        picking = pickerRemove = false;
        pickerSelection = null;
        if (previous == null) return;
        var c = Character;
        string target = characterTab == CharacterTab.Weapon ? c.EquippedWeaponInstanceId : EquippedArtifact(c)?.InstanceId;
        if (target == previous.Target.InstanceId && !committed)
        {
            training = previous;
            upgradeMaterial = material;
        }
        else if (target != null)
        {
            bool weapon = characterTab == CharacterTab.Weapon;
            training = new RoguelikeRosterUpgradeControls(session,
                weapon ? RoguelikeUpgradeTarget.Weapon(target) : RoguelikeUpgradeTarget.Artifact(target),
                weapon && previous.IsRefinement);
            upgradeMaterial = null;
        }
        else if (committed)
        {
            picking = true;
            trainingBeforePicker = previous;
            materialBeforePicker = material;
        }
    }

    private IEnumerable<RoguelikeWeaponInstanceData> PickerWeapons(RoguelikeCharacterProgressData character)
        => FilterWeapons(pickerWeaponType ?? session.Data.CharacterAttributesDict[character.CharacterID].WeaponType,
            pickerRarity, pickerLevelSort);

    private void RenderEquipmentPicker(RoguelikeCharacterProgressData c)
    {
        bool weapon = characterTab == CharacterTab.Weapon;
        float width = sidePanelWidth - 52;
        int weaponType = session.Data.CharacterAttributesDict[c.CharacterID].WeaponType;
        void Close() { CloseEquipmentPicker(false); Render(); }
        SideHeader(weapon ? T("更换武器", "Change Weapon") : T("更换圣遗物", "Change Artifact"),
            CharacterName(c.CharacterID) + " · " + (weapon ? WeaponType(weaponType) : Slot(artifactSlot)), Close);
        var filters = Box("Picker filters", sideBody, 0, 104, width, 74.6f);
        Horizontal(filters, 8);
        if (weapon)
        {
            float unit = (width - 16) / 3.2f;
            Filter(filters, T("稀有度", "Rarity"), pickerRarity, RarityOptions(), v => pickerRarity = v, unit * .85f, roster: true);
            Filter(filters, T("排序", "Sort"), pickerLevelSort,
                new[] { (false, T("稀有度", "Rarity")), (true, T("等级", "Level")) }, v => pickerLevelSort = v, unit, roster: true);
            Filter(filters, T("装备类型", "Type"), pickerWeaponType ?? weaponType, WeaponOptions(),
                v => pickerWeaponType = v, unit * 1.35f, roster: true);
        }
        var parent = Rect("Picker inventory", sideBody);
        Stretch(parent);
        float footerHeight = weapon ? 102 : 158;
        parent.offsetMin = new Vector2(0, footerHeight);
        float countTop = weapon ? 182 : 103;
        string current = weapon ? c.EquippedWeaponInstanceId : EquippedArtifact(c)?.InstanceId;
        Action preview = null;
        void Select(string id)
        {
            if (!pickerRemove && (pickerSelection ?? current) == id) return;
            pickerSelection = id;
            pickerRemove = false;
            preview();
        }
        bool Highlight(string id) => !pickerRemove && (pickerSelection ?? current) == id;
        var window = GearGrid(parent, "picker:" + characterTab, width, 96, countTop + 28, Highlight, Select);
        var count = Label(Box("Matching equipment count", sideBody, 0, countTop, width, 25), "", 18);
        count.color = Muted;
        var empty = Label(Box("Empty equipment", sideBody, 0, countTop + 34, width, 44),
            T("没有符合筛选条件的装备。", "No matching equipment."), 20);
        empty.color = Muted;
        void DrawGear(bool reset)
        {
            var entries = weapon ? PickerWeapons(c).Select(WeaponEntry).ToArray() :
                FilterArtifacts(artifactSlot, pickerArtifactFilters).Select(ArtifactEntry).ToArray();
            window.SetEntries(entries, reset);
            count.text = T($"{entries.Length} 件装备", $"{entries.Length} items");
            empty.gameObject.SetActive(entries.Length == 0);
        }
        var footer = Rect("Picker actions", sideBody);
        footer.anchorMin = Vector2.zero; footer.anchorMax = new Vector2(1, 0);
        footer.pivot = new Vector2(.5f, 0); footer.sizeDelta = new Vector2(0, footerHeight);
        BottomRule(footer, HeadingRule, y: footerHeight - 1);
        if (!weapon)
        {
            var artifactFilters = Box("Artifact filters", footer, 0, 12, width, 44);
            Horizontal(artifactFilters, 8);
            RenderArtifactFilters(artifactFilters, true);
        }
        var status = Label(Box("Equipment selection status", footer, 0, footerHeight - 87, width, 32), "", 20);
        status.color = Muted;
        var actions = Box("Actions", footer, 0, footerHeight - 46, width, 46);
        Horizontal(actions, 8, TextAnchor.LowerRight);
        var remove = Button(actions, Ui("btn_unequip_current", "卸下当前", "Unequip Current"), () =>
        {
            pickerRemove = true;
            pickerSelection = null;
            preview();
        }, 142);
        Button(actions, Ui("btn_cancel", "取消", "Cancel"), Close, 100);
        var confirm = Button(actions, Ui("btn_confirm_equip", "确认装备", "Confirm Equip"), () =>
        {
            Commit(session.Equipment(tx => ApplyPickerChoice(tx, c.CharacterID)), () => CloseEquipmentPicker(true));
        }, 178, true);
        var confirmText = confirm.GetComponentInChildren<TMPro.TMP_Text>();
        void UpdateStatus()
        {
            var owner = pickerSelection != null ? session.Owner(pickerSelection) : null;
            bool compatible = CompatiblePickerChoice(c);
            bool transfer = compatible && owner != null && owner.CharacterID != c.CharacterID;
            string empty = Ui("tip_equip_nothing_to_remove", "当前没有装备可卸下。", "Nothing is equipped in this slot.");
            string incompatible = Ui("tip_equip_incompatible", "装备类型不匹配。", "Incompatible equipment type.");
            string same = Ui("tip_equip_already_equipped", "已装备这件物品。", "This item is already equipped.");
            status.text = pickerRemove ? T("确认后卸下当前装备。", "Confirm to remove the equipped item.") :
                pickerSelection != null && !compatible ? incompatible :
                transfer ? T($"确认后将从 {CharacterName(owner.CharacterID)} 转移给 {CharacterName(c.CharacterID)}。",
                    $"Transfer from {CharacterName(owner.CharacterID)} to {CharacterName(c.CharacterID)}.") :
                pickerSelection == current && current != null ? T("当前装备", "Currently equipped") :
                pickerSelection != null && current != null ? T("更换后，原装备将返回背包。", "The previous item will return to the inventory.") :
                T("选择装备以预览更换。", "Choose an item to preview the change.");
            confirmText.text = pickerRemove ? T("确认卸下", "Confirm Unequip") : transfer ? T("确认转移", "Confirm Transfer") :
                pickerSelection != null && pickerSelection == current ? T("当前已装备", "Already equipped") :
                Ui("btn_confirm_equip", "确认装备", "Confirm Equip");
            Block(confirm, ReadOnlyReason ?? (pickerRemove ? (current == null ? empty : null) :
                pickerSelection == null ? Ui("tip_equip_no_item_selected", "请先选择一件装备。", "Choose an item first.") :
                pickerSelection == current ? same : compatible ? null : incompatible));
            Block(remove, ReadOnlyReason ?? (current == null ? empty : pickerRemove ?
                T("已选择卸下，点击确认以应用。", "Removal is selected. Confirm to apply.") : null));
        }
        preview = () => { window.UpdateSelection(); UpdateStatus(); RefreshDetail(); };
        refreshPickerResults = () => { DrawGear(true); preview(); };
        DrawGear(false);
        UpdateStatus();
    }
}
