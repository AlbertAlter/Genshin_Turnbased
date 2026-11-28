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
    private string formationCondition;
    private Action formationConfirmed, formationCancelled;
    [SerializeField] private Rect formationEnemyPreviewButtonBounds = new Rect(1640, 240, 208, 48);
    private int[] formationEnemyIds = Array.Empty<int>();
    private bool formationEnemyPreviewOpen;
    private RectTransform formationEnemyPreviewOverlay;
    private readonly Dictionary<int, RoguelikeBattleAllyBuild> formationTrials = new();
    private bool formationIsBattle;

    /// <summary>战斗或事件战斗宿主显式传入标题与实际敌阵ID，在编队成功保存后继续入战。</summary>
    public void ShowFormation(string encounter = null, Action confirmed = null, Action cancelled = null,
        IEnumerable<int> enemyIds = null, IEnumerable<StageTrialSetup> trials = null, string conditionText = null)
    {
        if (session?.Snapshot == null) return;
        ResetFormation();
        ResetModes();
        formationIsBattle = trials != null;
        foreach (var build in RoguelikeTrialParty.Build(trials, session.Snapshot, new DataManagerRoguelikeEquipmentCatalog(session.Data), session.Artifacts))
            formationTrials.Add(build.Character.CharacterID, build);
        inBag = false;
        characterId = 0;
        formationSlots = session.Snapshot.PartyCharacterIds.Concat(Enumerable.Repeat(0, PartySize)).Take(PartySize).ToArray();
        var savedNode = session.Snapshot.Flow?.Node;
        if (formationIsBattle && savedNode?.BattlePartyCharacterIds != null)
            formationSlots = savedNode.BattlePartyCharacterIds.Select(id => savedNode.TrialCharacterIds?.Contains(id) == true ? -id : id)
                .Concat(Enumerable.Repeat(0, PartySize)).Take(PartySize).ToArray();
        formationPreset = Math.Max(1, session.Snapshot.ActivePartyPreset);
        encounterTitle = encounter;
        formationCondition = conditionText;
        formationConfirmed = confirmed;
        formationCancelled = cancelled;
        // 输入来自本次实际敌阵。按EnemyID去重，使用Enemy_Main.Threat从高到低排列；同威胁保留敌阵顺序。
        formationEnemyIds = OrderFormationEnemyPortraits(enemyIds, id =>
            session.Data.EnemyMainDict.TryGetValue(id, out var enemy) ? enemy.Threat :
                throw new InvalidOperationException("敌人预览缺少Enemy_Main数据：" + id));
        if (tileSkinReady) Render();
    }

    private void ResetFormation()
    {
        CloseFormationEnemyPreview();
        formationEnemyIds = Array.Empty<int>();
        formationTrials.Clear();
        formationIsBattle = false;
        formationCondition = null;
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
        CloseFormationEnemyPreview();
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
        int[] party = formationSlots.Where(id => id != 0).Select(Math.Abs).ToArray();
        var result = formationIsBattle ? session.SaveBattleParty(party, formationSlots.Where(id => id < 0).Select(id => -id), formationPreset)
            : session.Equipment(tx => tx.SetParty(party, formationPreset));
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
                if (formationPicks.Count >= PartySize && !formationPicks.Any(pick => Math.Abs(pick) == Math.Abs(id)))
                {
                    SetMessage(T("最多可选择四名角色。", "Choose up to four characters."));
                    return;
                }
                formationPicks.Add(id);
                formationPicks.RemoveAll(pick => pick != id && Math.Abs(pick) == Math.Abs(id));
            }
            BackFromCharacter();
            return;
        }
        int slot = formationPicker.Value;
        int previous = Array.FindIndex(formationSlots, pick => Math.Abs(pick) == Math.Abs(id));
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
        // 条件由宿主使用与战斗内相同的Lang入口解析；切换角色/预设不丢失本场说明。
        bool hasCondition = !string.IsNullOrWhiteSpace(formationCondition);
        var instruction = Label(Box(hasCondition ? "Battle condition" : "Instructions", board, 70, 191, Width - 140, 41),
            hasCondition ? Ui("ui_battle_event_conditions", "挑战条件", "Challenge Conditions") + " · " + formationCondition :
                T("点击卡位选择角色，或用快速编队一次安排多个位置。", "Choose a slot or use Quick Formation to fill several slots."),
            23, TextAlignmentOptions.Center);
        instruction.color = hasCondition ? RosterInk : Muted;
        FitLine(instruction, 18);
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
            var character = id < 0 && formationTrials.TryGetValue(-id, out var trial) ? trial.Character : id > 0 ? session.Character(id) : null;
            float x = left + i * (width + gap);
            var mount = Box("Party slot", board, x, 320, width, height);
            Block(Button(mount, character == null ? T("＋\n选择角色", "＋\nChoose Character") : "",
                () => OpenFormationPicker(slot), width, height: height, fontSize: 27), ReadOnlyReason);
            if (character != null && rosterCardFactory != null)
            {
                if (!formationCards.TryGetValue(id, out var card))
                    formationCards.Add(id, card = rosterCardFactory.Request(mount, BattleSide.Ally, Math.Abs(id), showShadows: false));
                var artwork = (RectTransform)card.transform;
                artwork.SetParent(mount, false);
                artwork.SetAsLastSibling();
                var name = Box("Nameplate", mount, 12, height - 61, width - 24, 42);
                var color = new Color32(48, 39, 33, 224);
                SetRounded(Fill(name, color), color);
                Label(name, CharacterName(Math.Abs(id)) + "  " + Lv(character.Level), 22, TextAlignmentOptions.Center).color = RosterWhite;
                if (id < 0) TrialBadge(mount, width);
            }
            var number = Box("Slot number", mount, 16, 16, 38, 38);
            var gold = new Color32(232, 209, 162, 255);
            SetRounded(Fill(number, gold), gold);
            Label(number, (slot + 1).ToString(), 22, TextAlignmentOptions.Center);
            if (character == null) continue;
            var actions = Box("Slot actions", board, x, 334 + height, width, 46);
            Horizontal(actions, 9, TextAnchor.MiddleCenter);
            if (id > 0) Button(actions, T("角色详情", "Details"), () => SelectCharacter(id), 138);
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
        RenderFormationEnemyPreviewButton(board);
        if (formationPicker.HasValue) RenderFormationDrawer(formationPicker.Value);
    }

    internal static int[] OrderFormationEnemyPortraits(IEnumerable<int> ids, Func<int, int> threat)
        => (ids ?? Enumerable.Empty<int>()).Distinct().OrderByDescending(threat).ToArray();

    private void RenderFormationEnemyPreviewButton(RectTransform board)
    {
        if (formationEnemyIds.Length == 0) return;
        var mount = Box("Enemy preview button", board, formationEnemyPreviewButtonBounds.x,
            formationEnemyPreviewButtonBounds.y, formationEnemyPreviewButtonBounds.width, formationEnemyPreviewButtonBounds.height);
        var button = Button(mount, Ui("btn_formation_enemy_preview", "敌人预览", "Enemy Preview"),
            ToggleFormationEnemyPreview, formationEnemyPreviewButtonBounds.width, height: formationEnemyPreviewButtonBounds.height);
        button.gameObject.AddComponent<UIButtonClickFeedback>().Bind((RectTransform)button.transform);
        if (formationEnemyPreviewOpen && !formationPicker.HasValue) BuildFormationEnemyPreview();
    }

    private void ToggleFormationEnemyPreview()
    {
        if (formationEnemyPreviewOpen) { CloseFormationEnemyPreview(); return; }
        formationEnemyPreviewOpen = true;
        BuildFormationEnemyPreview();
    }

    private void CloseFormationEnemyPreview()
    {
        formationEnemyPreviewOpen = false;
        if (formationEnemyPreviewOverlay == null) return;
        formationEnemyPreviewOverlay.gameObject.SetActive(false);
        Destroy(formationEnemyPreviewOverlay.gameObject);
        formationEnemyPreviewOverlay = null;
    }

    private void BuildFormationEnemyPreview()
    {
        if (formationEnemyPreviewOverlay != null || formationEnemyIds.Length == 0) return;
        var overlay = formationEnemyPreviewOverlay = Rect("Enemy portrait preview", formationHost);
        Stretch(overlay);
        var outside = overlay.gameObject.AddComponent<Button>();
        outside.targetGraphic = Fill(overlay, Color.clear, true);
        outside.transition = Selectable.Transition.None;
        outside.navigation = new Navigation { mode = Navigation.Mode.None };
        outside.onClick.AddListener(CloseFormationEnemyPreview);
        const float iconSize = 84, gap = 12, padding = 16;
        int columns = Math.Min(5, formationEnemyIds.Length), rows = (formationEnemyIds.Length + columns - 1) / columns;
        float width = padding * 2 + columns * iconSize + (columns - 1) * gap;
        float height = padding * 2 + rows * iconSize + (rows - 1) * gap;
        var panel = Panel("Enemy portraits", panelFrame, padding, overlay);
        Place(panel.Root, formationEnemyPreviewButtonBounds.xMax - width,
            formationEnemyPreviewButtonBounds.yMax + 10, width, height);
        // 吃掉面板内部点击；头像本身只显示，没有详情、名称、等级、数量或威胁度数字。
        var inside = panel.Root.gameObject.AddComponent<Button>();
        inside.targetGraphic = panel.Root.GetComponent<Image>();
        inside.transition = Selectable.Transition.None;
        inside.navigation = new Navigation { mode = Navigation.Mode.None };
        for (int index = 0; index < formationEnemyIds.Length; index++)
        {
            var portrait = Box("Enemy portrait", panel.Body, index % columns * (iconSize + gap),
                index / columns * (iconSize + gap), iconSize, iconSize);
            var clip = Fill(portrait, Color.white); SetRounded(clip, Color.white);
            portrait.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var image = Image($"art_assets/Enemy_Icon/{formationEnemyIds[index]}_01.png", portrait);
            Stretch(image.rectTransform); image.raycastTarget = false;
        }
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
        int trialCount = 0;
        foreach (var trial in formationTrials.Values)
        {
            var character = trial.Character;
            var a = session.Data.CharacterAttributesDict[character.CharacterID];
            if ((rosterType >= 0 && rosterType != a.WeaponType) || (rosterRarity > 0 && rosterRarity != a.Star) ||
                (rosterElement.Length > 0 && rosterElement != a.Element)) continue;
            int id = -character.CharacterID;
            var cell = Rect("Trial candidate", grid);
            int index = quick ? formationPicks.IndexOf(id) : Array.IndexOf(formationSlots, id);
            Tile(cell, CharacterListPortraitPath(-id), a.Star, Lv(character.Level), UIItemListIcon.IconKind.Character,
                () => PickFormationCharacter(id), size, index >= 0, compact: true);
            TrialBadge(cell, size);
            if (index >= 0) Label(Box("Trial position", cell, 8, 8, 29, 29), (index + 1).ToString(), 18, TextAlignmentOptions.Center);
            trialCount++;
        }
        // 补齐本行空格，正式角色总从下一行开始；试用角色始终在列表顶部。
        for (int i = 0; i < (columns - trialCount % columns) % columns; i++) Rect("Trial row spacing", grid);
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

    private void TrialBadge(RectTransform parent, float width)
    {
        var badge = Box("Trial badge", parent, width - 67, 7, 60, 28);
        var color = new Color32(113, 103, 88, 240);
        SetRounded(Fill(badge, color), color);
        Label(badge, Ui("ui_character_trial", "试用", "Trial"), 18, TextAlignmentOptions.Center).color = RosterWhite;
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
