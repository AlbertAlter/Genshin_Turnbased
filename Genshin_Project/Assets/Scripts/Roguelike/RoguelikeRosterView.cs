using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>角色列表和背包的 UGUI 表现层。使用同一养成会话，场景负责显式装配。</summary>
[DisallowMultipleComponent]
public sealed partial class RoguelikeRosterView : MonoBehaviour
{
    private enum CharacterTab { Stats, Weapon, Artifact, Skills, Talents, Constellation }
    private enum BagTab { Weapons, Artifacts, Consumables, Quest }
    private enum ArtifactFilterKind { Set, MainStat, Substat }
    private sealed class ArtifactFilters
    {
        public readonly List<int> Sets = new();
        public readonly List<string> Stats = new();
        public readonly List<string> Substats = new();
        public readonly List<ArtifactFilterKind> Order = new() { ArtifactFilterKind.Set, ArtifactFilterKind.MainStat, ArtifactFilterKind.Substat };

        public void Prioritize(ArtifactFilterKind kind)
        {
            Order.Remove(kind);
            Order.Insert(0, kind);
        }
    }
    private const float Width = 1920, Height = 1080;
    private static readonly Color RosterPaper = new Color32(153, 143, 129, 255);
    private static readonly Color RosterInk = new Color32(50, 42, 35, 255);
    private static readonly Color RosterDeep = new Color32(113, 103, 88, 255);
    private static readonly Color RosterWhite = new Color32(254, 254, 254, 255);
    private static readonly Color Ink = RosterInk;
    private static readonly Color Selected = new Color32(243, 233, 216, 64);
    private static readonly Color Muted = new Color32(81, 71, 60, 255);
    private static readonly Color Rule = new Color32(113, 103, 88, 96);
    private static readonly Color HeadingRule = new Color32(250, 241, 223, 181);
    private static readonly string[] WeaponNames = { "单手剑", "双手剑", "弓", "长柄武器", "法器" };
    private static readonly string[] WeaponNamesEn = { "Sword", "Claymore", "Bow", "Polearm", "Catalyst" };
    private static readonly string[] SlotNames = { "生之花", "死之羽", "时之沙", "空之杯", "理之冠" };
    private static readonly string[] SlotNamesEn = { "Flower", "Plume", "Sands", "Goblet", "Circlet" };
    private static readonly string[] SkillNames = { "普攻", "重击", "战技", "爆发" };
    private static readonly string[] SkillNamesEn = { "Normal", "Heavy", "Skill", "Burst" };
    private static readonly string[] SkillKeys = { "normal", "heavy", "skill", "burst" };
    [SerializeField] private Rect headerBounds = new Rect(30, 0, 1860, 95);
    [SerializeField] private Rect rosterFiltersBounds = new Rect(30, 122, 1860, 74.6f);
    [SerializeField] private Rect rosterListBounds = new Rect(30, 216.6f, 1860, 833.4f);
    [SerializeField] private Rect panelBounds = new Rect(30, 188, 1860, 862);
    [SerializeField] private Rect cardBounds = new Rect(30, 122, 541.3333f, 928);
    [SerializeField, Min(0)] private float panelGap = 30, resizeSeconds = .25f;
    [SerializeField, Min(0)] private float sidePanelDesignWidth = 560;
    [SerializeField, Min(0)] private float constellationColumnGap = 30;
    [SerializeField] private bool english;
    private readonly Dictionary<string, Sprite> images = new();
    private readonly Dictionary<string, List<Action<Sprite>>> loading = new();
    private readonly Dictionary<string, float> scrollPositions = new();
    private readonly Dictionary<string, ScrollRect> scrolls = new();
    private RoguelikeRosterSession session;
    private RosterTextCatalog texts;
    private TMP_FontAsset font;
    private UIAdaptiveFrame panelFrame;
    private UICardFactory rosterCardFactory;
    private RosterCardCarousel carousel;
    private RectTransform shell, header, navigation, list, detail, side, portrait, filterPopup;
    private RectTransform listBody, detailBody, sideBody, rosterBody;
    private UIFloatingTip tip;
    private RosterQuickScroll quickScroll;
    private RectTransform quickStrip;
    private Button quickPrevious, quickNext;
    private readonly List<(int Id, Image Border)> quickAvatars = new();
    private CanvasGroup sideInput;
    private int quickScrollCharacter;
    private Coroutine motion;
    private Rect[] motionTargets;
    private float detailPanelWidth, sidePanelWidth, listPanelWidth;
    private bool tileSkinReady, initialLayout, inBag, skillMode, picking, pickerRemove;
    private int characterId, skillIndex, constellationIndex, bagPage, bagPageSize;
    private int rosterType = -1, rosterRarity, bagWeaponType = -1, bagRarity, pickerRarity;
    private int? pickerWeaponType;
    private bool bagLevelSort, pickerLevelSort;
    private string rosterElement = "", bagSelection, pickerSelection, upgradeMaterial;
    private readonly ArtifactFilters bagArtifactFilters = new(), pickerArtifactFilters = new();
    private readonly Dictionary<ArtifactSlot, List<string>> pickerStatsBySlot = new();
    private ArtifactSlot artifactSlot = ArtifactSlot.Flower;
    private CharacterTab characterTab;
    private BagTab bagTab;
    private RoguelikeRosterUpgradeControls training;
    private RoguelikeRosterUpgradeControls trainingBeforePicker;
    private string materialBeforePicker;
    private readonly List<UICardView> readinessCards = new();
    public bool Ready
    {
        get
        {
            if (!tileSkinReady || shell == null || loading.Count != 0) return false;
            shell.GetComponentsInChildren(false, readinessCards);
            foreach (var card in readinessCards)
                if (!card.Ready && string.IsNullOrEmpty(card.Error)) return false;
            return true;
        }
    }

    public void SetEnglish(bool value)
    {
        if (english == value) return;
        english = value;
        if (shell != null) Render();
    }

    public void Bind(RectTransform mount, RoguelikeRosterSession source, UICardFactory cardFactory,
        TMP_FontAsset textFont, UIAdaptiveFrame framePrefab)
    {
        if (shell != null) throw new InvalidOperationException("角色界面已经绑定。");
        session = source ?? throw new ArgumentNullException(nameof(source));
        font = textFont;
        panelFrame = framePrefab;
        rosterCardFactory = cardFactory;
        shell = Rect("CharacterRoster", mount);
        Stretch(shell);
        Fill(shell, RosterPaper);
        shell.gameObject.AddComponent<RectMask2D>();
        header = Rect("Header", shell);
        Place(header, headerBounds);
        navigation = Box("Navigation", shell, 30, 103, Width - 60, 52);
        rosterBody = Rect("Character list", shell);
        Place(rosterBody, rosterListBounds);
        portrait = Rect("Character Card", shell);
        portrait.gameObject.AddComponent<RectMask2D>();
        carousel = portrait.gameObject.AddComponent<RosterCardCarousel>();
        carousel.Bind(cardFactory);
        portrait.gameObject.SetActive(false);
        (list, listBody) = Panel("List", framePrefab);
        (detail, detailBody) = Panel("Details", framePrefab);
        (side, sideBody) = Panel("Side", framePrefab, 26);
        sideInput = side.gameObject.AddComponent<CanvasGroup>();
        side.gameObject.SetActive(false);
        formationHost = Rect("Battle formation", shell);
        Stretch(formationHost);
        formationCardParking = Rect("Retained party cards", formationHost);
        formationCardParking.gameObject.AddComponent<CanvasGroup>().alpha = 0;
        tip = UIFloatingTip.Create(shell, font, session.Data);
        texts = session.Texts;
        var loaded = session.Reload();
        if (!loaded.Succeeded) SetMessage(loaded.Message);
        StartCoroutine(LoadTileSkin());
    }

    public void ShowBindingError(RectTransform mount, TMP_FontAsset textFont, string reason)
    {
        font = textFont;
        if (shell != null) shell.gameObject.SetActive(false);
        var error = Box("Character roster unavailable", mount, 80, 80, 1760, 100);
        Label(error, reason, 28);
    }

    public void ShowCharacterList()
    {
        ResetFormation();
        inBag = false;
        characterId = 0;
        ResetModes();
        if (tileSkinReady) Render();
    }

    public void ShowInventory()
    {
        ResetFormation();
        inBag = true;
        ResetModes();
        if (tileSkinReady) Render();
    }

    private void OnDestroy()
    {
        ReleaseFormationCards();
        if (shell != null) Destroy(shell.gameObject);
        foreach (var sprite in images.Values)
            if (sprite != null) { Destroy(sprite.texture); Destroy(sprite); }
    }

    private void OnEnable()
    {
        if (shell == null) return;
        shell.gameObject.SetActive(true);
        if (tileSkinReady) Render();
    }

    private void OnDisable()
    {
        if (shell == null) return;
        if (motion != null) StopCoroutine(motion);
        motion = null;
        CloseFilterPopup();
        materialHold = null;
        tip.Hide();
        ReleaseFormationCards();
        shell.gameObject.SetActive(false);
    }

    private string T(string zh, string en) => english ? en : zh;
    private string Ui(string key, string zh, string en, params object[] args)
        => session.Data.LangDict.ContainsKey(key) ? session.Data.GetLangText(key, english, args) : T(zh, en);
    private string CharacterName(int id)
    {
        var info = session.Data.GetCharacterOverview(id);
        return session.Data.GetUiName(UiNameKind.Character, id, info?.Name ?? id.ToString(), english);
    }
    private string WeaponName(WeaponAttributesData w) => session.Data.GetUiName(UiNameKind.Weapon, w.WeaponID, w.WeaponName, english);
    private string WeaponType(int type) => type >= 0 && type < WeaponNames.Length ? (english ? WeaponNamesEn : WeaponNames)[type] : "—";
    private string Slot(ArtifactSlot slot) => (english ? SlotNamesEn : SlotNames)[(int)slot - 1];
    private static string Lv(int level) => "Lv. " + level;
    private static string Range(string from, string to) => from == to ? from : from + " → " + to;
    private static string WeaponArt(int id) => $"art_assets/Weapon_Icon/{id}_01.png";
    private string ItemName(RoguelikeItemDefinition item) => Ui($"item_{item.ItemId}_name", item.Name, item.Name);
    private string ItemDescription(RoguelikeItemDefinition item) => item.Category == "Constellation"
        ? Ui("item_constellation_desc", item.Description, item.Description, CharacterName(item.ReferenceID))
        : session.Data.GetRoguelikeItemDescription(item, english);
    private RoguelikeCharacterProgressData Character => session.Character(characterId);

    private void SetMessage(string value) => tip?.Show(value);

    private void Attempt(Action operation)
    {
        try { tip.Hide(); operation(); }
        catch (Exception error) { SetMessage(error.Message); }
    }

    private void Commit(RoguelikeSaveResult result, Action succeeded = null)
    {
        if (!result.Succeeded) { SetMessage(result.Message); return; }
        if (session.Snapshot != null) succeeded?.Invoke();
        else ResetModes();
        Render();
    }

    private void ResetModes()
    {
        training = null;
        trainingBeforePicker = null;
        materialBeforePicker = null;
        materialHold = null;
        picking = skillMode = pickerRemove = false;
        pickerSelection = upgradeMaterial = null;
    }

    private void Update()
    {
        if (shell == null || !shell.gameObject.activeInHierarchy || !tileSkinReady) return;
        if (filterPopup != null) UpdateFilterKeys();
        TickMaterialHold();
    }

    public bool ShowPageExit => filterPopup == null && !picking && training == null && !skillMode &&
        (inBag || characterId == 0) && formationSlots == null;

    public bool TryNavigateBack()
    {
        if (shell == null || !shell.gameObject.activeInHierarchy || !tileSkinReady) return false;
        if (filterPopup != null) CloseFilterPopup();
        else if (picking) Attempt(() => { CloseEquipmentPicker(false); Render(); });
        else if (training != null || skillMode) Attempt(CloseTraining);
        else if (!inBag && characterId != 0) Attempt(BackFromCharacter);
        else if (formationSlots != null) Attempt(formationPicker.HasValue ? CloseFormationPicker : CancelFormation);
        else return false;
        return true;
    }

    private void OnApplicationFocus(bool focused)
    {
        if (focused) return;
        materialHold = null;
        if (quickScroll != null) quickScroll.CancelDrag();
    }

    private void Render()
    {
        var characters = !inBag && session.Snapshot != null && characterId != 0 ? RosterCharacters() : null;
        bool retainQuick = characters != null && quickStrip != null &&
            quickAvatars.Select(item => item.Id).SequenceEqual(characters.Select(c => c.CharacterID));
        if (!retainQuick)
        {
            quickScroll = null;
            quickStrip = null;
            quickAvatars.Clear();
            quickScrollCharacter = 0;
        }
        RememberScrolls();
        CloseFilterPopup();
        Clear(header, quickStrip);
        Clear(navigation, session.Snapshot != null && (inBag || characters != null) ? tabsRoot : null);
        Clear(listBody); Clear(detailBody);
        if (training != null || picking) Clear(sideBody);
        refreshPickerResults = null;
        refreshBagGear = null;
        bagGearWindow = null;
        refreshBagPanels = null;
        refreshTrainingPreview = null;
        Clear(rosterBody);
        bool showFormation = formationSlots != null && session.Snapshot != null && characterId == 0;
        if (showFormation) RetainFormationCards();
        else ReleaseFormationCards();
        Clear(formationHost, formationCardParking);
        formationHost.gameObject.SetActive(showFormation);
        bool showRoster = !showFormation && !inBag && session.Snapshot != null && Character == null;
        rosterBody.gameObject.SetActive(showRoster);
        materialPlus = null;
        RenderHeader(characters);
        if (session.Snapshot == null)
        {
            Layout(false, false, false);
            Label(Column(listBody), T("没有可读取的当前档案。", "There is no active save to display."), 28);
            return;
        }
        if (showFormation) RenderFormation();
        else if (inBag) RenderBag();
        else if (Character == null) RenderRoster();
        else RenderCharacter();
        RestoreScrolls();
    }

    private void RememberScrolls(RectTransform panel = null)
    {
        var keys = scrolls.Where(pair => panel == null || pair.Value == null || pair.Value.transform.IsChildOf(panel))
            .Select(pair => pair.Key).ToArray();
        foreach (string key in keys)
        {
            var scroll = scrolls[key];
            if (scroll != null) scrollPositions[key] = scroll.content.anchoredPosition.y;
            scrolls.Remove(key);
        }
    }

    private void RestoreScrolls(RectTransform panel = null)
    {
        foreach (var pair in scrolls)
        {
            var scroll = pair.Value;
            if (scroll == null || (panel != null && !scroll.transform.IsChildOf(panel))) continue;
            if (!scrollPositions.TryGetValue(pair.Key, out var position) || position <= 0) continue;
            LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
            float range = Mathf.Max(0, scroll.content.rect.height - scroll.viewport.rect.height);
            var offset = scroll.content.anchoredPosition;
            offset.y = Mathf.Clamp(position, 0, range);
            scroll.content.anchoredPosition = offset;
        }
    }

    private void RenderHeader(List<RoguelikeCharacterProgressData> characters)
    {
        var title = Label(Box("Title", header, 34, 0, 320, 91),
            formationSlots != null ? T("战前编队", "Battle Formation") : inBag ? T("背包", "Inventory") : T("角色", "Characters"), 30);
        title.color = RosterWhite;
        title.fontStyle = FontStyles.Normal;
        var diamond = Image(CharacterListArtPath + "Shapes/TitleDiamond.png", header);
        Place((RectTransform)diamond.transform, -5, 29.5f, 32, 32);
        Fill(Box("Header rule", header, 0, 91, headerBounds.width, 1), new Color32(238, 225, 204, 48));
        if (session.Snapshot == null)
        {
            Button(Box("Reload", header, 350, 18, 160, 56), T("重读存档", "Reload Save"),
                () => { var result = session.Reload(); SetMessage(result.Succeeded ? "" : result.Message); Render(); }, 160);
        }
        else if (characters != null) RenderQuickStrip(characters);
    }

    private void RenderQuickStrip(List<RoguelikeCharacterProgressData> entries)
    {
        bool created = quickStrip == null;
        if (created) BuildQuickStrip(entries);
        int index = quickAvatars.FindIndex(item => item.Id == characterId);
        if (created || quickScrollCharacter != characterId)
        {
            foreach (var item in quickAvatars)
                item.Border.color = item.Id == characterId ? new Color32(222, 199, 149, 255) : RosterDeep;
            float center = quickScroll.CenterOn(index);
            quickScroll.Restore(created ? center : quickScroll.Offset, center);
            quickScrollCharacter = characterId;
        }
        Block(quickPrevious, index > 0 ? null : T("已是列表第一位。", "This is the first character."));
        Block(quickNext, index >= 0 && index + 1 < quickAvatars.Count ? null : T("已是列表最后一位。", "This is the last character."));
        bool formation = formationSlots != null;
        Button(Box("Back", header, headerBounds.width - (formation ? 374 : 178), 24, 178, 44),
            formationPicker.HasValue ? T("‹ 返回选人", "‹ Back to Picker") :
            formation ? T("‹ 返回编队", "‹ Back to Formation") : T("‹ 角色列表", "‹ Characters"),
            BackFromCharacter, 178, height: 44);
        if (formation) RenderFormationDetailAction();
    }

    private void BuildQuickStrip(List<RoguelikeCharacterProgressData> entries)
    {
        const float step = RosterQuickScroll.Step, trackWidth = 703, stripWidth = 787;
        var strip = quickStrip = Box("Centered character strip", header, (headerBounds.width - stripWidth) / 2, 13.5f, stripWidth, 64);
        var previous = Box("Previous", strip, 0, 0, 35, 64);
        var next = Box("Next", strip, stripWidth - 35, 0, 35, 64);
        var row = Box("Visible avatars", strip, 42, 0, trackWidth, 64);
        var scroll = row.gameObject.AddComponent<RosterQuickScroll>();
        quickScroll = scroll;
        var mask = Rect("Clip", row);
        Stretch(mask);
        Fill(mask, Color.clear, true);
        mask.gameObject.AddComponent<RectMask2D>();
        var content = Rect("Avatars", mask);
        content.anchorMin = new Vector2(0, 0);
        content.anchorMax = new Vector2(0, 1);
        content.pivot = new Vector2(0, .5f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = new Vector2(Math.Max(trackWidth, entries.Count * step - 7), 0);
        Horizontal(content, 7);
        scroll.viewport = mask; scroll.content = content; scroll.vertical = false;
        scroll.inertia = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        foreach (var c in entries)
        {
            var item = Button(content, "", () => SelectCharacter(c.CharacterID), 64, height: 64);
            var border = (Image)item.targetGraphic;
            item.transition = Selectable.Transition.None;
            item.transform.Find("Face").gameObject.SetActive(false);
            quickAvatars.Add((c.CharacterID, border));
            var icon = Box("Avatar", (RectTransform)item.transform, 1, 1, 62, 62);
            int rarity = session.Data.CharacterAttributesDict.TryGetValue(c.CharacterID, out var info) ? info.Star : 0;
            SquareArt(icon, CharacterListPortraitPath(c.CharacterID), rarity, UIItemListIcon.IconKind.Character,
                inset: 0, framed: false);
        }
        void Move(int direction)
        {
            int index = quickAvatars.FindIndex(item => item.Id == characterId) + direction;
            if (index >= 0 && index < quickAvatars.Count) SelectCharacter(quickAvatars[index].Id);
        }
        quickPrevious = Button(previous, "‹", () => Move(-1), 35, height: 64, fontSize: 30);
        quickNext = Button(next, "›", () => Move(1), 35, height: 64, fontSize: 30);
    }

    private List<RoguelikeCharacterProgressData> RosterCharacters()
        => session.Snapshot.Characters.Where(c => session.Data.CharacterAttributesDict.ContainsKey(c.CharacterID)).ToList();

    private void SelectCharacter(int id)
    {
        if (characterId == id) return;
        bool fromList = characterId == 0;
        characterId = id;
        var c = Character;
        constellationIndex = Mathf.Clamp(c.ConstellationLevel, 0, 5);
        if (fromList)
        {
            ResetModes();
            characterTab = CharacterTab.Stats;
            artifactSlot = ArtifactSlot.Flower;
            skillIndex = 0;
        }
        pickerSelection = null;
        pickerRemove = false;
        if (training != null) RetargetTraining(c, characterTab, training.IsRefinement);
        Render();
    }

    private void RenderRoster()
    {
        Layout(false, false, false, roster: true);
        var filter = Row(navigation, rosterFiltersBounds.height, TextAnchor.UpperLeft);
        RenderRosterFilters(filter);
        const int gap = 18;
        float availableWidth = Mathf.Max(1, rosterListBounds.width - 8);
        int columns = Mathf.Max(1, Mathf.FloorToInt((availableWidth + gap) / (151 + gap)));
        float iconSize = (availableWidth - (columns - 1) * gap) / columns;
        var body = Grid(rosterBody, "roster", iconSize, columns: columns);
        var available = FilteredRoster();
        foreach (var c in available)
        {
            var a = session.Data.CharacterAttributesDict[c.CharacterID];
            Tile(body, CharacterListPortraitPath(c.CharacterID), a.Star, Lv(c.Level),
                UIItemListIcon.IconKind.Character, () => SelectCharacter(c.CharacterID), iconSize, compact: true);
        }
        if (available.Count == 0)
            Label(Box("Empty roster", rosterBody, 0, 0, rosterListBounds.width, 72),
                T("没有符合筛选条件的角色。", "No matching characters."), 24, TextAlignmentOptions.Center).color = RosterInk;
    }

    private List<RoguelikeCharacterProgressData> FilteredRoster() => RosterCharacters().Where(c =>
    {
        var a = session.Data.CharacterAttributesDict[c.CharacterID];
        return (rosterType < 0 || rosterType == a.WeaponType) && (rosterRarity <= 0 || rosterRarity == a.Star) &&
            (rosterElement == "" || rosterElement == a.Element);
    }).ToList();

    private void RenderRosterFilters(RectTransform filter)
    {
        filter.GetComponent<HorizontalLayoutGroup>().spacing = 10;
        var available = RosterCharacters();
        var elements = available.Select(c => session.Data.CharacterAttributesDict[c.CharacterID].Element).Distinct().ToList();
        Filter(filter, T("元素", "Element"), rosterElement,
            new[] { ("", Ui("btn_filter_all_elements", "全部元素", "All Elements")) }.Concat(elements.Select(e => (e, ElementLabel(e)))),
            v => rosterElement = v, 184, roster: true);
        Filter(filter, T("武器类型", "Weapon Type"), rosterType,
            new[] { (-1, Ui("btn_filter_all_weapon_types", "全部类型", "All Types")) }.Concat(WeaponOptions().Skip(1)),
            v => rosterType = v, 184, roster: true);
        Filter(filter, T("角色星级", "Character Rarity"), rosterRarity,
            new[] { (0, Ui("btn_filter_all_rarities", "全部星级", "All Rarities")) }.Concat(RarityOptions().Skip(1)),
            v => rosterRarity = v, 184, roster: true);
    }

    private void RenderCharacter()
    {
        Layout(true, true, picking || training != null);
        string[] names = english ? new[] { "Attributes", "Weapon", "Artifacts", "Skills", "Talents", "Constellation" }
            : new[] { "基本属性", "武器", "圣遗物", "技能", "天赋", "命之座" };
        var c = Character;
        Tabs(navigation, names, (int)characterTab, index =>
        {
            if ((int)characterTab == index) return;
            bool keepTraining = training != null;
            characterTab = (CharacterTab)index;
            ResetModes();
            if (keepTraining)
            {
                if (characterTab == CharacterTab.Artifact) artifactSlot = ArtifactSlot.Flower;
                if (characterTab == CharacterTab.Skills) skillIndex = 0;
                if (characterTab == CharacterTab.Constellation) constellationIndex = Mathf.Clamp(c.ConstellationLevel, 0, 5);
                RetargetTraining(c, characterTab);
            }
            Render();
        });
        RenderCharacterDetail(c);
        if (training != null) RenderTraining();
        else if (picking) RenderEquipmentPicker(c);
    }

    private void RefreshDetail()
    {
        RememberScrolls(detailBody);
        Clear(detailBody);
        if (inBag) RenderBagDetail();
        else RenderCharacterDetail(Character);
        RestoreScrolls(detailBody);
    }

    private void RenderCharacterDetail(RoguelikeCharacterProgressData c)
    {
        if (characterTab == CharacterTab.Constellation)
        {
            RenderConstellations(detailBody, c);
            return;
        }
        float footerHeight = characterTab == CharacterTab.Talents ? 0 : skillMode && characterTab == CharacterTab.Skills ? 106 : 60;
        var body = DetailScroll(detailBody, "character:" + characterId + ":" + characterTab, out var footer, footerHeight);
        switch (characterTab)
        {
            case CharacterTab.Stats: RenderAttributes(body, footer, c); break;
            case CharacterTab.Weapon: RenderCharacterWeapon(body, footer, c); break;
            case CharacterTab.Artifact: RenderCharacterArtifact(body, footer, c); break;
            case CharacterTab.Skills: RenderSkills(body, footer, c); break;
            case CharacterTab.Talents: RenderTalents(body, c); break;
        }
    }

    private void Layout(bool character, bool showDetail, bool showSide, bool roster = false)
    {
        float y = panelBounds.y, h = panelBounds.height;
        float left = panelBounds.xMin, right = panelBounds.xMax;
        Rect cardRect = cardBounds;
        float x = character ? cardRect.xMax + panelGap : left;
        float sideWidth = showSide ? sidePanelDesignWidth : 0;
        float sideX = right - sideWidth;
        float bagListWidth = showSide ? (panelBounds.width - sideWidth - 2 * panelGap) / 2f :
            (panelBounds.width - panelGap) * 1.4f / 2.4f;
        float detailX = character ? x : showDetail ? left + bagListWidth + panelGap : right;
        float detailRight = showSide ? sideX - panelGap : right;
        var targets = new[]
        {
            new Rect(left, y, character ? 0 : (showDetail ? detailX - panelGap - left : panelBounds.width), h),
            new Rect(detailX, y, showDetail ? detailRight - detailX : 0, h),
            new Rect(sideX, y, showSide ? right - sideX : 0, h),
        };
        listPanelWidth = targets[0].width;
        detailPanelWidth = targets[1].width;
        sidePanelWidth = targets[2].width;
        list.gameObject.SetActive(!character && !roster);
        detail.gameObject.SetActive(showDetail);
        bool wasSideVisible = side.gameObject.activeSelf;
        sideInput.interactable = sideInput.blocksRaycasts = showSide;
        if (showSide && !wasSideVisible)
        {
            Place(side, Width, y, sideWidth, h);
            side.gameObject.SetActive(true);
        }
        if (character && !portrait.gameObject.activeSelf) Place(portrait, cardRect);
        if (!character) carousel.Select(0, 0);
        portrait.gameObject.SetActive(character);
        if (character) carousel.Select(characterId, RosterCharacters().FindIndex(c => c.CharacterID == characterId));
        Rect navRect = roster ? rosterFiltersBounds : new Rect(x, cardBounds.y, right - x, y - cardBounds.y - 12);
        var panels = new[] { list, detail, side, navigation, portrait };
        bool leaving = !showSide && wasSideVisible;
        var end = new[]
        {
            targets[0], targets[1], leaving ? new Rect(Width, y, side.rect.width, h) : targets[2], navRect, cardRect
        };
        if (motion != null)
        {
            if (end.SequenceEqual(motionTargets)) return;
            StopCoroutine(motion);
            motion = null;
        }
        if (initialLayout && resizeSeconds > 0 && panels.Select(CurrentBounds).Where((bounds, i) => bounds != end[i]).Any())
        {
            motionTargets = end;
            motion = StartCoroutine(AnimateWidths(panels, end, leaving));
        }
        else
        {
            for (int i = 0; i < panels.Length; i++) Place(panels[i], end[i]);
            if (leaving) HideSide();
            initialLayout = true;
        }
    }

    private static Rect CurrentBounds(RectTransform rect)
        => new Rect(rect.anchoredPosition.x, -rect.anchoredPosition.y, rect.sizeDelta.x, rect.sizeDelta.y);

    private void HideSide()
    {
        Clear(sideBody);
        side.gameObject.SetActive(false);
    }

    private IEnumerator AnimateWidths(RectTransform[] panels, Rect[] target, bool leaving)
    {
        var from = panels.Select(CurrentBounds).ToArray();
        for (float elapsed = 0; elapsed < resizeSeconds; elapsed += Time.unscaledDeltaTime)
        {
            float ease = RosterMotion.Ease(elapsed / resizeSeconds);
            for (int i = 0; i < panels.Length; i++)
                Place(panels[i], Mathf.Lerp(from[i].x, target[i].x, ease), Mathf.Lerp(from[i].y, target[i].y, ease),
                    Mathf.Lerp(from[i].width, target[i].width, ease), Mathf.Lerp(from[i].height, target[i].height, ease));
            yield return null;
        }
        for (int i = 0; i < panels.Length; i++) Place(panels[i], target[i]);
        if (leaving) HideSide();
        motion = null;
    }
}
