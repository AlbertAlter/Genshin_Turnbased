using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class RoguelikeRosterView
{
    private string ReliquaryFullTip => Ui("tip_inventory_artifact_reliquary_limit", "圣遗物已到上限，无法开启宝匣。",
        "Artifact limit reached. Cannot open the reliquary.");

    private string ReliquaryBlock(RoguelikeArtifactReliquaryService.Options options)
    {
        if (ReadOnlyReason != null) return ReadOnlyReason;
        if (options == null || options.Owned == 0)
            return Ui("tip_inventory_reliquary_missing", "没有可开启的宝匣。", "No reliquaries to open.");
        if (options.FreeSlots == 0) return ReliquaryFullTip;
        if (options.SetIds.Count == 0)
            return Ui("tip_inventory_reliquary_no_sets", "此宝匣没有可兑换的套装。", "No sets are available for this reliquary.");
        return null;
    }

    private void RenderReliquaryAction(RectTransform footer, RoguelikeItemDefinition item,
        RoguelikeArtifactReliquaryService.Options options)
    {
        Button open = null;
        open = Button(footer, Ui("btn_open_reliquary", "开启", "Open"), () => OpenReliquaryDialog(open, item), 160, true);
        Block(open, ReliquaryBlock(options));
    }

    private void OpenReliquaryDialog(Button anchor, RoguelikeItemDefinition item)
    {
        var options = session.Reliquaries.Read(session.Snapshot, item.ItemId);
        string blocked = ReliquaryBlock(options);
        if (blocked != null) { SetMessage(blocked); return; }

        string stepId = session.Context.Flow.StepId;
        var (panel, content) = Panel("Open artifact reliquary", panelFrame, 34, OpenFilterLayer(.56f));
        const float width = 1420, height = 810, innerWidth = width - 68;
        const float listWidth = 840, listTop = 94, footerHeight = 60;
        Place(panel, (Width - width) / 2, (Height - height) / 2, width, height);
        Label(Box("Reliquary name", content, 0, 0, innerWidth, 44), ItemName(item), 34);
        Label(Box("Opening instructions", content, 0, 50, innerWidth, 30),
            Ui("ui_inventory_reliquary_instructions", $"选择一个套装，每个宝匣获得一件{item.Rarity}星圣遗物。",
                $"Choose a set. Each reliquary grants one {item.Rarity}-star artifact.", item.Rarity), 21).color = Muted;

        var choiceHost = Rect("Sets", content);
        Stretch(choiceHost);
        choiceHost.offsetMin = new Vector2(0, footerHeight + 14);
        choiceHost.offsetMax = new Vector2(listWidth - innerWidth, -listTop);
        var choices = Scroll(choiceHost, "reliquary-sets", 10);
        var grid = choices.gameObject.AddComponent<GridLayoutGroup>();
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 2;
        grid.spacing = Vector2.one * 8;
        grid.cellSize = new Vector2((listWidth - 18) / 2, 66);

        var previewHost = Rect("Set preview", content);
        Stretch(previewHost);
        previewHost.offsetMin = new Vector2(listWidth + 18, footerHeight + 14);
        previewHost.offsetMax = new Vector2(0, -listTop);
        var preview = DetailScroll(previewHost, "reliquary-set-preview", out _, 0);

        var footer = Rect("Opening controls", content);
        footer.anchorMin = Vector2.zero;
        footer.anchorMax = new Vector2(1, 0);
        footer.pivot = new Vector2(.5f, 0);
        footer.sizeDelta = new Vector2(0, footerHeight);
        Horizontal(footer, 10);

        var rows = new List<(int SetId, Button Button)>();
        int selectedSet = 0, quantity = 1;
        string SetName(int id) => texts.SetName(id, english);
        string SetIcon(int id) => $"art_assets/Artifact_Icon/{id}{(int)ArtifactSlot.Flower}.png";
        var minus = Button(footer, "−", null, 48);
        var amount = Label(footer, "", 23, TextAlignmentOptions.Center);
        amount.rectTransform.sizeDelta = new Vector2(78, 46);
        var plus = Button(footer, "+", null, 48);
        var max = Button(footer, Ui("btn_max_quantity", "最大", "Max"), null, 100);
        Label(footer, Ui("ui_inventory_reliquary_stock", $"持有 {options.Owned} · 圣遗物余位 {options.FreeSlots}",
            $"{options.Owned} owned · {options.FreeSlots} artifact slots free", options.Owned, options.FreeSlots), 20).color = Muted;
        var actions = Rect("Opening actions", content);
        actions.anchorMin = actions.anchorMax = actions.pivot = Vector2.right;
        actions.sizeDelta = new Vector2(320, footerHeight);
        actions.anchoredPosition = Vector2.zero;
        Horizontal(actions, 10, TextAnchor.MiddleRight);
        var cancel = Button(actions, Ui("btn_cancel", "取消", "Cancel"), CloseFilterPopup, 140);
        var confirm = Button(actions, Ui("btn_confirm_open_reliquary", "确认开启", "Confirm Open"), Confirm, 170, true);
        max.onClick.AddListener(() => { quantity = options.MaxQuantity; UpdateControls(); });
        minus.gameObject.AddComponent<UIHoldRepeatButton>().Bind(minus, () => Step(-1));
        plus.gameObject.AddComponent<UIHoldRepeatButton>().Bind(plus, () => Step(1));
        foreach (int id in options.SetIds.OrderBy(SetName))
        {
            var row = Button(choices, SetName(id), () =>
            {
                selectedSet = id;
                foreach (var choice in rows) StyleButton(choice.Button, RosterPaper, choice.SetId == id ? Ink : RosterDeep);
                DrawPreview();
                UpdateControls();
            }, grid.cellSize.x, height: grid.cellSize.y);
            StyleRosterFilterText(row, Ink, 75, 14);
            var icon = Image(SetIcon(id), (RectTransform)row.transform);
            Place((RectTransform)icon.transform, 10, 7, 52, 52);
            rows.Add((id, row));
        }
        DrawPreview();
        UpdateControls();
        FocusFilter(anchor, rows.Select(row => row.Button).Concat(new[] { minus, plus, max, cancel, confirm }).ToList());

        bool Step(int delta)
        {
            quantity = Mathf.Clamp(quantity + delta, 1, options.MaxQuantity);
            UpdateControls();
            return delta < 0 ? quantity > 1 : quantity < options.MaxQuantity;
        }

        void UpdateControls()
        {
            amount.text = quantity.ToString("N0");
            minus.interactable = quantity > 1;
            plus.interactable = max.interactable = quantity < options.MaxQuantity;
            Block(confirm, selectedSet == 0 ? Ui("tip_inventory_reliquary_select_set", "请先选择套装。", "Choose a set first.") : null);
        }

        void DrawPreview()
        {
            Clear(preview);
            preview.anchoredPosition = Vector2.zero;
            if (selectedSet == 0)
            {
                Label(preview, Ui("ui_inventory_reliquary_preview", "选择套装后查看效果。", "Select a set to see its bonuses."));
                return;
            }
            Heading(preview, SetName(selectedSet), new string('★', item.Rarity),
                SetIcon(selectedSet), item.Rarity, UIItemListIcon.IconKind.Artifact, 76);
            foreach (int pieces in new[] { 2, 4 })
            {
                string bonus = texts.SetBonus(selectedSet, pieces, english);
                if (string.IsNullOrWhiteSpace(bonus)) continue;
                Label(preview, T($"{pieces} 件套", $"{pieces}-Piece"));
                Label(preview, bonus, 20);
            }
        }

        void Confirm()
        {
            if (selectedSet == 0 || filterPopup == null || !panel.gameObject.activeInHierarchy) return;
            var result = session.OpenReliquaries(stepId, item.ItemId, selectedSet, quantity);
            if (!result.Succeeded)
            {
                session.Reload();
                if (session.Context?.Flow.StepId != stepId || result.ErrorCode == RoguelikeSaveErrorCode.InventoryFull) Render();
                SetMessage(result.ErrorCode == RoguelikeSaveErrorCode.InventoryFull
                    ? Ui("tip_inventory_reliquary_space", "圣遗物背包余位不足，无法开启所选数量。", "Not enough artifact slots for this quantity.")
                    : Ui("tip_inventory_reliquary_failed", "宝匣开启失败，请重试。", "Could not open the reliquaries. Try again."));
                return;
            }
            if (session.Snapshot != null && session.Count(item.ItemId) == 0) bagSelection = null;
            Render();
            string name = SetName(selectedSet);
            SetMessage(Ui("ui_inventory_reliquary_received", $"已获得 {quantity:N0} 件「{name}」圣遗物。",
                $"Received {quantity:N0} artifact(s) from {name}.", quantity.ToString("N0"), name));
        }
    }
}
