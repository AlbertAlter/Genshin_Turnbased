using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class RoguelikeRosterView
{
    private const int PartySize = 4;
    private const int QuickFormation = -1;
    private RectTransform formationHost, formationCardParking;
    private readonly Dictionary<int, UICardView> formationCards = new();
    private readonly List<int> formationPicks = new();
    private int[] formationSlots;
    private int formationPreset;
    private int? formationPicker;
    private bool animateFormationPicker;
    private string encounterTitle;
    private Action formationConfirmed, formationCancelled;

    /// <summary>战斗节点宿主传入遭遇标题，在编队成功保存后继续进入战斗。</summary>
    public void ShowFormation(string encounter = null, Action confirmed = null, Action cancelled = null)
    {
        if (session?.Snapshot == null) return;
        ResetFormation();
        ResetModes();
        inBag = false;
        characterId = 0;
        formationSlots = session.Snapshot.PartyCharacterIds.Concat(Enumerable.Repeat(0, PartySize)).Take(PartySize).ToArray();
        formationPreset = Math.Max(1, session.Snapshot.ActivePartyPreset);
        encounterTitle = encounter;
        formationConfirmed = confirmed;
        formationCancelled = cancelled;
        if (tileSkinReady) Render();
    }

    private void ResetFormation()
    {
        formationSlots = null;
        formationPicker = null;
        animateFormationPicker = false;
        formationPicks.Clear();
        formationConfirmed = formationCancelled = null;
    }

    private void ReleaseFormationCards()
    {
        if (rosterCardFactory != null)
            foreach (var cardView in formationCards.Values) rosterCardFactory.Release(cardView);
        formationCards.Clear();
    }

    private void RetainFormationCards()
    {
        foreach (int id in formationCards.Keys.ToArray())
        {
            var card = formationCards[id];
            if (formationSlots.Contains(id) && card != null && !card.Released)
                card.transform.SetParent(formationCardParking, false);
            else
            {
                if (rosterCardFactory != null) rosterCardFactory.Release(card);
                formationCards.Remove(id);
            }
        }
    }

    private void BackFromCharacter()
    {
        if (formationSlots == null) { ShowCharacterList(); return; }
        characterId = 0;
        ResetModes();
        Render();
    }

    private void OpenFormationPicker(int slot)
    {
        formationPicker = slot;
        animateFormationPicker = true;
        formationPicks.Clear();
        scrollPositions.Remove("formation-picker");
        scrolls.Remove("formation-picker");
        Render();
    }

    private void CloseFormationPicker()
    {
        formationPicker = null;
        formationPicks.Clear();
        BackFromCharacter();
    }

    private void CancelFormation()
    {
        if (formationCancelled != null) formationCancelled();
        else ShowCharacterList();
    }

    private void ConfirmFormation()
    {
        int[] party = formationSlots.Where(id => id > 0).ToArray();
        var result = session.Equipment(tx => tx.SetParty(party, formationPreset));
        if (!result.Succeeded) { SetMessage(result.Message); return; }
        if (formationConfirmed != null) formationConfirmed();
        else ShowCharacterList();
    }

    private void PickFormationCharacter(int id)
    {
        if (!session.CanEdit || !formationPicker.HasValue) return;
        if (formationPicker == QuickFormation)
        {
            if (!formationPicks.Remove(id))
            {
                if (formationPicks.Count >= PartySize)
                {
                    SetMessage(T("最多可选择四名角色。", "Choose up to four characters."));
                    return;
                }
                formationPicks.Add(id);
            }
            BackFromCharacter();
            return;
        }
        int slot = formationPicker.Value;
        int previous = Array.IndexOf(formationSlots, id);
        if (previous >= 0) formationSlots[previous] = formationSlots[slot];
        formationSlots[slot] = id;
        CloseFormationPicker();
    }

    private void ApplyQuickFormation()
    {
        for (int i = 0; i < PartySize; i++) formationSlots[i] = i < formationPicks.Count ? formationPicks[i] : 0;
        CloseFormationPicker();
    }

    private void RenderFormation()
    {
        Layout(false, false, false, roster: true);
        var board = Rect("Formation board", formationHost);
        Stretch(board);
        var boardInput = board.gameObject.AddComponent<CanvasGroup>();
        boardInput.interactable = boardInput.blocksRaycasts = !formationPicker.HasValue;
        Label(Box("Encounter", board, 70, 135, Width - 140, 52),
            encounterTitle ?? T("出战队伍", "Battle Party"), 36, TextAlignmentOptions.Center);
        Label(Box("Instructions", board, 70, 191, Width - 140, 41),
            T("点击卡位选择角色，或用快速编队一次安排多个位置。", "Choose a slot or use Quick Formation to fill several slots."),
            23, TextAlignmentOptions.Center).color = Muted;
        const float width = 292, gap = 34;
        var presets = Box("Party presets", board, Width / 2 - 360, 240, 720, 52);
        for (int index = 1; index <= 10; index++)
        {
            int number = index;
            var mount = Box("Preset " + number, presets, (index - 1) * 72, 0, 66, 48);
            Button(mount, number == formationPreset ? $"[{number}]" : number.ToString(), () =>
            {
                if (number == formationPreset) return;
                formationPreset = number;
                var snapshot = session.Snapshot;
                var members = snapshot.PartyPresets?.Find(entry => entry.Number == number)?.Members
                    ?? (number == Math.Max(1, snapshot.ActivePartyPreset) ? snapshot.PartyCharacterIds : new List<int>());
                formationSlots = members.Concat(Enumerable.Repeat(0, PartySize)).Take(PartySize).ToArray();
                Render();
            }, 66, height: 48);
        }
        float height = width / UICardFactory.Aspect;
        float left = (Width - PartySize * width - (PartySize - 1) * gap) / 2;
        for (int i = 0; i < PartySize; i++)
        {
            int slot = i;
            int id = formationSlots[slot];
            var character = id > 0 ? session.Character(id) : null;
            float x = left + i * (width + gap);
            var mount = Box("Party slot", board, x, 320, width, height);
            Block(Button(mount, character == null ? T("＋\n选择角色", "＋\nChoose Character") : "",
                () => OpenFormationPicker(slot), width, height: height, fontSize: 27), ReadOnlyReason);
            if (character != null && rosterCardFactory != null)
            {
                if (!formationCards.TryGetValue(id, out var card))
                    formationCards.Add(id, card = rosterCardFactory.Request(mount, BattleSide.Ally, id, showShadows: false));
                var artwork = (RectTransform)card.transform;
                artwork.SetParent(mount, false);
                artwork.SetAsLastSibling();
                var name = Box("Nameplate", mount, 12, height - 61, width - 24, 42);
                var color = new Color32(48, 39, 33, 224);
                SetRounded(Fill(name, color), color);
                Label(name, CharacterName(id) + "  " + Lv(character.Level), 22, TextAlignmentOptions.Center).color = RosterWhite;
            }
            var number = Box("Slot number", mount, 16, 16, 38, 38);
            var gold = new Color32(232, 209, 162, 255);
            SetRounded(Fill(number, gold), gold);
            Label(number, (slot + 1).ToString(), 22, TextAlignmentOptions.Center);
            if (character == null) continue;
            var actions = Box("Slot actions", board, x, 334 + height, width, 46);
            Horizontal(actions, 9, TextAnchor.MiddleCenter);
            Button(actions, T("角色详情", "Details"), () => SelectCharacter(id), 138);
            Block(Button(actions, T("移出", "Remove"), () => { formationSlots[slot] = 0; Render(); }, 104), ReadOnlyReason);
        }
        Fill(Box("Formation rule", board, 72, 964, Width - 144, 1), Rule);
        Button(Box("Back to route", board, 72, 984, 178, 46), T("返回路线", "Return to Route"), CancelFormation, 178);
        var footer = Box("Formation actions", board, Width - 628, 984, 556, 46);
        Horizontal(footer, 10, TextAnchor.MiddleRight);
        int count = formationSlots.Count(id => id != 0);
        Label(footer, $"{count} / {PartySize}", 22);
        Block(Button(footer, T("快速编队", "Quick Formation"), () => OpenFormationPicker(QuickFormation), 176), ReadOnlyReason);
        Block(Button(footer, T("确认编队", "Confirm Party"), ConfirmFormation, 178, true), ReadOnlyReason ??
            (count > 0 ? null : T("请先编入至少一名角色。", "Add at least one character to the party.")));
        if (formationPicker.HasValue) RenderFormationDrawer(formationPicker.Value);
    }

    private void RenderFormationDrawer(int slot)
    {
        var shade = Rect("Dismiss character picker", formationHost);
        Stretch(shade);
        var dismiss = shade.gameObject.AddComponent<Button>();
        dismiss.targetGraphic = Fill(shade, new Color32(50, 42, 35, 102), true);
        dismiss.transition = Selectable.Transition.None;
        dismiss.onClick.AddListener(CloseFormationPicker);
        bool quick = slot == QuickFormation;
        const float width = 738, innerWidth = width - 48;
        var drawer = Box("Formation picker", formationHost, Width - width, 0, width, Height);
        Fill(drawer, new Color32(187, 177, 164, 255), true);
        if (animateFormationPicker)
        {
            animateFormationPicker = false;
            StartCoroutine(SlideFormationDrawer(drawer, width));
        }
        var body = Rect("Drawer content", drawer);
        Stretch(body, 24);
        Label(Box("Picker title", body, 0, 0, innerWidth - 120, 44), quick ? T("快速编队", "Quick Formation") :
            T($"选择 {slot + 1} 号位角色", $"Choose a character for slot {slot + 1}"), 30);
        Label(Box("Picker hint", body, 0, 46, innerWidth, 37), quick
            ? T("按选择顺序填入 1–4 号位，再点一次可取消选择。", "Pick in slot order. Select again to deselect.")
            : T("点击头像加入此位，已上阵角色可移动或交换位置。", "Select a portrait to assign the slot or swap party members."), 20).color = Muted;
        Button(Box("Close", body, innerWidth - 94, 0, 94, 42), T("关闭", "Close"), CloseFormationPicker, 94, height: 42);
        var filters = Box("Filters", body, 0, 96, innerWidth, 74.6f);
        Horizontal(filters, 10, TextAnchor.UpperLeft);
        RenderRosterFilters(filters);
        var host = Rect("Available characters", body);
        Stretch(host);
        host.offsetMin = new Vector2(0, quick ? 60 : 0);
        const int columns = 4, gap = 13;
        float size = (innerWidth - 8 - (columns - 1) * gap) / columns;
        var grid = Grid(host, "formation-picker", size, 183, columns, gap);
        grid.GetComponent<GridLayoutGroup>().cellSize = new Vector2(size, size + 69);
        foreach (var character in FilteredRoster())
        {
            int id = character.CharacterID;
            int index = quick ? formationPicks.IndexOf(id) : Array.IndexOf(formationSlots, id);
            var cell = Rect("Candidate", grid);
            int rarity = session.Data.CharacterAttributesDict[id].Star;
            Tile(cell, CharacterListPortraitPath(id), rarity, Lv(character.Level), UIItemListIcon.IconKind.Character,
                () => PickFormationCharacter(id), size, index >= 0, compact: true);
            if (index >= 0)
            {
                var marker = Box("Party position", cell, 8, 8, 29, 29);
                var gold = new Color32(232, 209, 162, 255);
                SetRounded(Fill(marker, gold), gold);
                Label(marker, (index + 1).ToString(), 18, TextAlignmentOptions.Center);
            }
            Button(Box("Details", cell, 0, size + 30, size, 37), T("角色详情", "Details"),
                () => SelectCharacter(id), size, height: 37, fontSize: 19);
        }
        if (!quick) return;
        var actions = Box("Quick formation actions", body, 0, Height - 96, innerWidth, 46);
        Horizontal(actions, 10, TextAnchor.MiddleRight);
        Label(actions, $"{formationPicks.Count} / {PartySize}", 22);
        Block(Button(actions, T("应用编队", "Apply Formation"), ApplyQuickFormation, 174, true), ReadOnlyReason ??
            (formationPicks.Count > 0 ? null : T("请先选择角色。", "Select a character first.")));
    }

    private static IEnumerator SlideFormationDrawer(RectTransform drawer, float width)
    {
        for (float elapsed = 0; elapsed < .18f; elapsed += Time.unscaledDeltaTime)
        {
            if (drawer == null || !drawer.gameObject.activeInHierarchy) yield break;
            drawer.anchoredPosition = new Vector2(Width - width * RosterMotion.Ease(elapsed / .18f), 0);
            yield return null;
        }
        if (drawer != null) drawer.anchoredPosition = new Vector2(Width - width, 0);
    }

    private void RenderFormationDetailAction()
    {
        int slot = Array.IndexOf(formationSlots, characterId);
        if (!formationPicker.HasValue && slot < 0) return;
        bool quick = formationPicker == QuickFormation;
        string label = quick ? formationPicks.Contains(characterId) ? T("取消选择", "Deselect") : T("选入队伍", "Select") :
            formationPicker.HasValue ? T($"加入 {formationPicker.Value + 1} 号位", $"Assign Slot {formationPicker.Value + 1}") :
            T("移出队伍", "Remove");
        Block(Button(Box("Formation action", header, headerBounds.width - 178, 24, 178, 44), label, () =>
        {
            if (formationPicker.HasValue) PickFormationCharacter(characterId);
            else { formationSlots[slot] = 0; BackFromCharacter(); }
        }, 178, true, 44), ReadOnlyReason);
    }
}
