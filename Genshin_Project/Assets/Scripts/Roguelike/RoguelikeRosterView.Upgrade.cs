using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed partial class RoguelikeRosterView
{
    private sealed class MaterialHold
    {
        public RoguelikeRosterUpgradeControls Controls;
        public string ItemId;
        public int Pointer;
        public Camera Camera;
        public float Next;
        public bool Repeated;
    }

    private MaterialHold materialHold;
    private Button materialPlus;
    private int suppressMaterialClickUntil = -1;
    private Action<bool> refreshTrainingPreview;

    private void TickMaterialHold()
    {
        var hold = materialHold;
        if (hold == null) return;
        bool pressed = false;
        Vector2 position = Input.mousePosition;
        if (hold.Pointer < 0) pressed = Input.GetMouseButton(0);
        else
        {
            for (int i = 0; i < Input.touchCount; i++)
            {
                var touch = Input.GetTouch(i);
                if (touch.fingerId != hold.Pointer) continue;
                pressed = touch.phase != TouchPhase.Ended && touch.phase != TouchPhase.Canceled;
                position = touch.position;
                break;
            }
        }
        if (!pressed || training != hold.Controls || !session.CanEdit || materialPlus == null ||
            !materialPlus.IsInteractable() || !RectTransformUtility.RectangleContainsScreenPoint(
                (RectTransform)materialPlus.transform, position, hold.Camera))
        {
            if (hold.Repeated && !pressed) suppressMaterialClickUntil = Time.frameCount + 1;
            materialHold = null;
            return;
        }
        if (Time.unscaledTime < hold.Next) return;
        hold.Next = Time.unscaledTime + .095f;
        Attempt(() =>
        {
            if (hold.Controls.Adjust(hold.ItemId, 1))
            {
                hold.Repeated = true;
                refreshTrainingPreview?.Invoke(true);
            }
            else materialHold = null;
        });
    }

    private void RefreshTrainingPanel()
    {
        RememberScrolls(sideBody);
        Clear(sideBody);
        materialHold = null;
        materialPlus = null;
        refreshTrainingPreview = null;
        RenderTraining();
        RestoreScrolls(sideBody);
        RefreshDetail();
    }

    private void OpenTraining(RoguelikeUpgradeTarget target, bool refinement = false)
    {
        if (!session.CanEdit) return;
        if (training?.Target.Kind == target.Kind && training.Target.InstanceId == target.InstanceId &&
            training.Target.CharacterId == target.CharacterId && training.IsRefinement == refinement) return;
        ResetModes();
        training = new RoguelikeRosterUpgradeControls(session, target, refinement);
        RefreshTrainingLayout();
    }

    private void CloseTraining()
    {
        ResetModes();
        RefreshTrainingLayout();
    }

    private void RefreshTrainingLayout()
    {
        if (inBag && refreshBagPanels != null) refreshBagPanels();
        else Render();
    }

    private void RetargetTraining(RoguelikeCharacterProgressData c, CharacterTab tab, bool refine = false)
    {
        var weapon = session.Weapon(c.EquippedWeaponInstanceId);
        var artifact = EquippedArtifact(c);
        var next = tab switch
        {
            CharacterTab.Stats => RoguelikeUpgradeTarget.Character(c.CharacterID),
            CharacterTab.Weapon when weapon != null => RoguelikeUpgradeTarget.Weapon(weapon.InstanceId),
            CharacterTab.Artifact when artifact != null => RoguelikeUpgradeTarget.Artifact(artifact.InstanceId),
            _ => null
        };
        materialHold = null;
        upgradeMaterial = null;
        training = next == null ? null : new RoguelikeRosterUpgradeControls(session, next, refine && tab == CharacterTab.Weapon);
    }

    private RoguelikeUpgradePreview LevelPreview(LevelUpTarget kind)
    {
        if (training == null || training.IsRefinement || training.Target.Kind != kind) return null;
        try { return training.Preview(); }
        catch (InvalidOperationException error) { SetMessage(error.Message); return null; }
    }

    private int RefinementPreview(int current)
    {
        try { return training.PreviewRefinement(); }
        catch (InvalidOperationException error) { SetMessage(error.Message); return current; }
    }

    private IEnumerable<RoguelikeWeaponInstanceData> RefineCopies(RoguelikeWeaponInstanceData target)
    {
        if (target == null || target.Weapon.Refinement >= 5 || target.Weapon.WeaponID == 3034)
            return Enumerable.Empty<RoguelikeWeaponInstanceData>();
        return session.Snapshot.Inventory.Weapons.Where(copy => copy.InstanceId != target.InstanceId &&
            copy.Weapon.WeaponID == target.Weapon.WeaponID && !copy.IsLocked && session.Owner(copy.InstanceId) == null)
            .OrderBy(copy => copy.Weapon.Level).ThenBy(copy => copy.Weapon.Refinement);
    }

    private IEnumerable<RoguelikeItemDefinition> UpgradeMaterials(LevelUpTarget target)
    {
        foreach (var stack in session.Snapshot.Inventory.Items)
            if (stack.Quantity > 0 && session.Items.TryGetExperienceValue(stack.ItemId, target, out _) &&
                session.Items.TryGetItem(stack.ItemId, out var material))
                yield return material;
    }

    private string ChapterLevelCap => Ui("ui_upgrade_chapter_level_cap", "已达本章等级上限", "Chapter level cap reached");
    private string NoMaterialsReason => Ui("tip_upgrade_insufficient_materials", "没有足够的升级材料。", "Not enough upgrade materials.");

    private (int Level, bool Reached, bool Maximum) ExperienceLevelState(RoguelikeUpgradeTarget target)
    {
        int characterAndWeaponMaximum = RoguelikeUpgradeLimits.MaximumCharacterAndWeaponLevel;
        var (level, maximum) = target.Kind switch
        {
            LevelUpTarget.Character => (session.Character(target.CharacterId)?.Level ?? 0, characterAndWeaponMaximum),
            LevelUpTarget.Weapon => (session.Weapon(target.InstanceId)?.Weapon.Level ?? 0, characterAndWeaponMaximum),
            LevelUpTarget.Artifact when session.Artifact(target.InstanceId)?.Artifact is GeneratedArtifact artifact =>
                (artifact.Level, artifact.MaxLevel),
            _ => (0, int.MaxValue)
        };
        var limits = session.Limits;
        int chapterCap = target.Kind == LevelUpTarget.Artifact ? limits.ArtifactLevel : limits.CharacterAndWeaponLevel;
        return (level, level >= Math.Min(chapterCap, maximum), level >= maximum);
    }

    private string UpgradeBlock(RoguelikeUpgradeTarget target) => ReadOnlyReason ??
        (ExperienceLevelState(target).Reached || UpgradeMaterials(target.Kind).Any() ? null : NoMaterialsReason);

    private string RefinementBlock(RoguelikeWeaponInstanceData weapon)
    {
        if (ReadOnlyReason is string readOnly) return readOnly;
        if (weapon == null) return Ui("tip_upgrade_weapon_not_equipped", "请先装备武器。", "Equip a weapon first.");
        if (weapon.Weapon.WeaponID == 3034)
            return Ui("tip_upgrade_refinement_unsupported", "这把武器无法精炼。", "This weapon cannot be refined.");
        if (weapon.Weapon.Refinement >= 5)
            return Ui("tip_upgrade_refinement_max", "已达到精炼上限。", "Maximum refinement rank reached.");
        return RefineCopies(weapon).Any() ? null :
            Ui("tip_upgrade_no_refinement_copy", "需要未装备且未锁定的同名武器。", "An unlocked, unequipped copy is required.");
    }

    private string MaterialShortage(string id, int required, int owned)
    {
        string name = session.Items.TryGetItem(id, out var item) ? ItemName(item) : id;
        return Ui("tip_upgrade_insufficient_named_material", $"{name}不足，需 {required} 件，现有 {owned} 件。",
            $"Not enough {name}. Need {required}, have {owned}.", name, required, owned);
    }

    private void RenderTraining()
    {
        var controls = training;
        var target = controls.Target;
        string title = controls.IsRefinement ? T("武器精炼", "Weapon Refinement") : target.Kind switch
        {
            LevelUpTarget.Character => T("角色升级", "Level Up Character"),
            LevelUpTarget.Weapon => T("武器升级", "Level Up Weapon"),
            _ => T("圣遗物强化", "Enhance Artifact")
        };
        string subtitle = "", art = null;
        int rarity = 0;
        var kind = UIItemListIcon.IconKind.Character;
        var weapon = target.Kind == LevelUpTarget.Weapon ? session.Weapon(target.InstanceId) : null;
        if (target.Kind == LevelUpTarget.Character && session.Data.CharacterAttributesDict.TryGetValue(target.CharacterId, out var character))
        {
            subtitle = CharacterName(target.CharacterId);
            art = CharacterListPortraitPath(target.CharacterId);
            rarity = character.Star;
        }
        else if (weapon != null && session.Data.WeaponAttributesDict.TryGetValue(weapon.Weapon.WeaponID, out var def))
        {
            subtitle = WeaponName(def);
            art = WeaponArt(def.WeaponID);
            rarity = def.Star;
            kind = UIItemListIcon.IconKind.Weapon;
        }
        else if (target.Kind == LevelUpTarget.Artifact && session.Artifact(target.InstanceId) is RoguelikeArtifactInstanceData artifact)
        {
            subtitle = texts.SetName(artifact.ArtifactSetID, english) + " · " + Slot(artifact.Artifact.Slot);
            art = ArtifactArt(artifact);
            rarity = artifact.Artifact.Star;
            kind = UIItemListIcon.IconKind.Artifact;
        }
        SideHeader(title, subtitle, CloseTraining, art, rarity, kind);
        var body = DetailScroll(sideBody, "upgrade:" + target.Kind + ":" + target.InstanceId, out var footer, 60, 104);
        if (controls.IsRefinement)
        {
            int current = weapon?.Weapon.Refinement ?? 1;
            int next = RefinementPreview(current);
            StatRow(body, T("精炼阶数", "Refinement Rank"), Range(current.ToString(), next.ToString()));
            Label(body, T("未装备的同名武器", "Unequipped copies"), 24);
            var copies = RefineCopies(weapon).ToArray();
            if (copies.Length == 0) Label(body, T("没有可用的精炼材料。", "No eligible refinement copies.")).color = Muted;
            else
            {
                var grid = TrainingGrid(body, sidePanelWidth - 62 - ScrollTextGutter, copies.Length, 100, out float size);
                foreach (var copy in copies)
                {
                    bool selected = controls.IncludesCopy(copy.InstanceId);
                    int remaining = 5 - next;
                    string reason = ReadOnlyReason ?? (selected || copy.Weapon.Refinement <= remaining ? null : remaining == 0 ?
                        Ui("tip_upgrade_material_requirement_met", "已放入足够的精炼材料。", "Enough refinement materials have been added.") :
                        T($"这把武器可提升 {copy.Weapon.Refinement} 阶，当前只剩 {remaining} 阶可提升。",
                            $"This copy adds {copy.Weapon.Refinement} ranks. Only {remaining} ranks remain."));
                    Action select = reason == null ? () => { controls.ToggleCopy(copy.InstanceId); RefreshTrainingPanel(); } : null;
                    var tile = WeaponTile(grid, copy, select, size, selected);
                    if (tile == null || reason == null) continue;
                    tile.gameObject.AddComponent<CanvasGroup>().alpha = .58f;
                    var blocker = Button((RectTransform)tile.transform, "", () => { }, size, height: UIItemListIcon.GetCellSize(size, true).y);
                    StyleButton(blocker, Color.clear, Color.clear);
                    Block(blocker, reason);
                }
            }
            Button(footer, Ui("btn_cancel", "取消", "Cancel"), CloseTraining, 110);
            Block(Button(footer, Ui("btn_confirm_refinement", "确认精炼", "Confirm Refinement"), () => Commit(controls.Commit(),
                () => training = new RoguelikeRosterUpgradeControls(session, target, true)), 218, true),
                RefinementBlock(weapon) ?? (controls.HasSelection ? null :
                    Ui("tip_upgrade_no_refinement_material_selected", "请先选择精炼材料。", "Select a refinement material first.")));
            return;
        }

        var levelState = ExperienceLevelState(target);
        if (levelState.Reached)
        {
            if (!levelState.Maximum) Label(body, ChapterLevelCap, 24, TextAlignmentOptions.Center).color = Muted;
            else StatRow(body, T("等级", "Level"), target.Kind == LevelUpTarget.Artifact ? "+" + levelState.Level : Lv(levelState.Level));
            return;
        }

        var materials = UpgradeMaterials(target.Kind).OrderBy(item => item.Rarity).ThenBy(item => item.ExperienceValue).ToArray();
        var active = materials.FirstOrDefault(item => item.ItemId == upgradeMaterial) ?? materials.LastOrDefault();
        var tiles = new List<(RoguelikeItemDefinition Item, UIItemListIcon Tile, TMP_Text Quantity)>();
        TMP_Text amount = null;
        Button minus = null, quick = null;
        if (active == null) Label(body, T("没有可用的经验材料。", "No eligible EXP materials.")).color = Muted;
        else
        {
            float width = sidePanelWidth - 62 - ScrollTextGutter;
            float gridWidth = Mathf.Min(400, width);
            int rows = Mathf.CeilToInt(materials.Length / 3f);
            float contentHeight = 226 + rows * (gridWidth / 3f + 34);
            var spacer = Rect("Material panel spacing", body);
            spacer.gameObject.AddComponent<LayoutElement>().minHeight =
                Mathf.Max(0, panelBounds.height - 52 - 104 - 72 - contentHeight);
            Label(body, T("选择升级材料", "Select upgrade materials"), 24, TextAlignmentOptions.Center);
            var grid = TrainingGrid(body, gridWidth, materials.Length, (gridWidth - 28) / 3f, out float size, 3, 14);
            foreach (var material in materials)
            {
                var mount = Rect("Material", grid);
                var tile = MaterialTile(mount, material, session.Count(material.ItemId).ToString("N0"),
                    () =>
                    {
                        materialHold = null;
                        active = material;
                        upgradeMaterial = material.ItemId;
                        refreshTrainingPreview(false);
                    }, size, selection: () => active == material);
                var badge = Box("Selected amount", mount, size - 45, 4, 41, 29);
                SetRounded(Fill(badge, RosterDeep), RosterDeep);
                var quantity = Label(badge, "", 18, TextAlignmentOptions.Center);
                quantity.color = RosterWhite;
                tiles.Add((material, tile, quantity));
            }
            var row = Rect("Quantity row", body);
            row.gameObject.AddComponent<LayoutElement>().minHeight = 56;
            var label = FitLine(Label(row, T("使用数量", "Quantity"), 20), 16);
            label.rectTransform.anchorMax = new Vector2(.5f, 1);
            label.rectTransform.offsetMax = new Vector2(-80, 0);
            var quantityControls = Rect("Quantity controls", row);
            quantityControls.anchorMin = new Vector2(.5f, 0);
            quantityControls.anchorMax = new Vector2(.5f, 1);
            quantityControls.sizeDelta = new Vector2(136, 0);
            Horizontal(quantityControls, 8, TextAnchor.MiddleCenter);
            minus = Button(quantityControls, "-", () =>
            {
                if (controls.Adjust(active.ItemId, -1)) refreshTrainingPreview(true);
            }, 36, height: 36);
            amount = Label(quantityControls, "", 21, TextAlignmentOptions.Center);
            amount.rectTransform.sizeDelta = new Vector2(48, 40);
            materialPlus = Button(quantityControls, "+", () =>
            {
                bool repeated = materialHold?.Repeated == true || Time.frameCount <= suppressMaterialClickUntil;
                materialHold = null;
                if (!repeated && controls.Adjust(active.ItemId, 1)) refreshTrainingPreview(true);
            }, 36, height: 36);
            materialPlus.gameObject.AddComponent<RosterMaterialHoldTrigger>().Begin = e =>
            {
                if (e.button != PointerEventData.InputButton.Left || materialHold != null || !materialPlus.IsInteractable()) return;
                suppressMaterialClickUntil = -1;
                materialHold = new MaterialHold { Controls = controls, ItemId = active.ItemId, Pointer = e.pointerId,
                    Camera = e.pressEventCamera, Next = Time.unscaledTime + .36f };
            };
            var quickMount = Rect("Quick add", row);
            quickMount.anchorMin = quickMount.anchorMax = new Vector2(1, .5f);
            quickMount.pivot = new Vector2(1, .5f);
            quickMount.sizeDelta = new Vector2(124, 46);
            quick = Button(quickMount, Ui("btn_upgrade_quick_add", "快捷放入", "Quick add"),
                () => { controls.QuickAdd(materials); refreshTrainingPreview(true); }, 124);
            StyleButton(quick, new Color32(243, 233, 216, 255), RosterDeep);
            BottomRule(row, Rule);
        }
        var gained = StatRow(body, T("新增经验", "EXP Gained"), "");
        var level = StatRow(body, "", "");
        var cap = Label(body, Ui("ui_upgrade_max_level_xp_hint", "已达到强化上限。", "Enhancement cap reached."), 20);
        cap.color = Muted;
        var apply = Button(footer, target.Kind == LevelUpTarget.Artifact ?
            Ui("btn_upgrade_artifact_apply", "强化", "Enhance") : Ui("btn_upgrade_apply", "升级", "Level Up"),
            () => Commit(controls.Commit(), () => training = new RoguelikeRosterUpgradeControls(session, target)), 190, true, 50);
        StyleButton(apply, new Color32(243, 233, 216, 255), Ink);
        RoguelikeUpgradePreview previous = null;
        string noPlan = Ui("tip_upgrade_configuration_unavailable", "当前无法生成升级预览。", "The upgrade preview is unavailable.");
        string enough = Ui("tip_upgrade_material_requirement_met", "已放入足够的升级材料。", "Enough upgrade materials have been added.");
        refreshTrainingPreview = updateDetail =>
        {
            var plan = LevelPreview(target.Kind);
            foreach (var entry in tiles)
            {
                int quantity = controls.Selected(entry.Item.ItemId);
                entry.Tile.SetSelected(entry.Item == active);
                entry.Quantity.text = quantity.ToString("N0");
                entry.Quantity.transform.parent.gameObject.SetActive(quantity > 0);
            }
            if (active != null)
            {
                int used = controls.Selected(active.ItemId);
                int owned = session.Count(active.ItemId);
                amount.text = used.ToString("N0");
                Block(minus, ReadOnlyReason ?? (used > 0 ? null :
                    Ui("tip_upgrade_no_material_to_remove", "尚未放入此材料。", "None of this material has been added.")));
                Block(materialPlus, ReadOnlyReason ?? (plan == null ? noPlan : plan.Result.ReachedLevelCap ? enough : used >= owned ?
                    MaterialShortage(active.ItemId, used + 1, owned) : controls.AdditionalLimit(active.ItemId) == 0 ? enough : null));
                Block(quick, ReadOnlyReason ?? (plan == null ? noPlan : plan.Result.ReachedLevelCap ? enough :
                    materials.Any(item => controls.Selected(item.ItemId) < session.Count(item.ItemId)) ? null : NoMaterialsReason));
            }
            bool maxArtifact = plan?.Result.IsMaximumLevel == true && target.Kind == LevelUpTarget.Artifact;
            gained.Row.gameObject.SetActive(plan != null);
            level.Row.gameObject.SetActive(plan != null && !maxArtifact);
            cap.gameObject.SetActive(maxArtifact);
            if (plan != null)
            {
                gained.Value.text = "+" + plan.GainedExperience.ToString("N0");
                level.Label.text = plan.Result.ReachedLevelCap ? Ui("ui_upgrade_stored_xp", "已存经验", "Stored EXP") :
                    target.Kind == LevelUpTarget.Artifact ? "+" + plan.Result.Level : Lv(plan.Result.Level);
                level.Value.text = plan.Result.ReachedLevelCap ? plan.Result.Experience.ToString("N0") :
                    $"{plan.Result.Experience:N0} / {plan.NextLevelExperience:N0}";
            }
            Block(apply, ReadOnlyReason ?? (plan == null ? noPlan : controls.HasSelection ? null :
                plan.Result.ReachedLevelCap ? ChapterLevelCap : active == null ? NoMaterialsReason :
                Ui("tip_upgrade_no_material_selected", "请先放入升级材料。", "Add upgrade materials first.")));
            bool changed = previous?.Result.Level != plan?.Result.Level || previous?.TargetAscension != plan?.TargetAscension;
            previous = plan;
            if (updateDetail && changed) RefreshDetail();
        };
        refreshTrainingPreview(false);
    }

    private static RectTransform TrainingGrid(RectTransform parent, float width, int count, float minimum, out float size,
        int fixedColumns = 0, int gap = 8)
    {
        int columns = fixedColumns > 0 ? fixedColumns : Mathf.Max(1, Mathf.FloorToInt((width + gap) / (minimum + gap)));
        size = (width - (columns - 1) * gap) / columns;
        float height = Mathf.CeilToInt(count / (float)columns) * (size + 26 + gap) - gap;
        var row = Rect("Material grid", parent);
        row.gameObject.AddComponent<LayoutElement>().minHeight = height;
        var mount = Rect("Centered grid", row);
        mount.anchorMin = mount.anchorMax = mount.pivot = new Vector2(.5f, 1f);
        mount.sizeDelta = new Vector2(width, height);
        var layout = mount.gameObject.AddComponent<GridLayoutGroup>();
        layout.cellSize = UIItemListIcon.GetCellSize(size, compact: true);
        layout.spacing = Vector2.one * gap;
        layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        layout.constraintCount = columns;
        layout.childAlignment = TextAnchor.UpperCenter;
        return mount;
    }
}

internal sealed class RosterMaterialHoldTrigger : MonoBehaviour, IPointerDownHandler
{
    public Action<PointerEventData> Begin;
    public void OnPointerDown(PointerEventData eventData) => Begin?.Invoke(eventData);
}
