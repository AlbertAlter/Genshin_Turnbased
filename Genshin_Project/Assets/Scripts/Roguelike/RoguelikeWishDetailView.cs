using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>只读图鉴弹窗，按祈愿奖品的模板展示初始属性与说明。</summary>
[DisallowMultipleComponent]
public sealed class RoguelikeWishDetailView : MonoBehaviour
{
    private const int BaseLevel = 1;
    private const int BaseRefinement = 1;
    private static readonly string[] StatKeys = { "HP", "ATK", "DEF", "EMBonus", "CritRate", "CritDMG", "RechargeBonus" };
    private static readonly string[] SkillKeys = { "normal", "heavy", "skill", "burst" };
    private static readonly (string Key, string Zh, string En)[] SkillTypes =
    {
        ("ui_skill_normal", "普攻", "Normal Attack"), ("ui_skill_heavy", "重击", "Heavy Attack"),
        ("ui_skill_elemental_skill", "战技", "Elemental Skill"), ("ui_skill_elemental_burst", "爆发", "Elemental Burst")
    };
    private static readonly (string Key, string Zh, string En)[] TabNames =
    {
        ("btn_tab_stats", "基本属性", "Attributes"), ("btn_tab_skill", "技能", "Skills"),
        ("btn_tab_talent", "天赋", "Talents"), ("btn_tab_constellation", "命之座", "Constellation")
    };

    [SerializeField] private Button close, backdrop;
    [SerializeField] private RectTransform cardMount, tabBar, body, divider;
    [SerializeField] private TMP_Text itemName, stars, subtitle;
    [SerializeField] private RoguelikeWishArt art;

    private readonly Button[] tabs = new Button[TabNames.Length];
    private readonly TMP_Text[] tabLabels = new TMP_Text[TabNames.Length];
    private UICardFactory cards;
    private UICardView card;
    private RoguelikeWishLanguage language;
    private RoguelikeWishPrize prize;
    private RosterTextCatalog characterText;
    private DataManager textSource;
    private ScrollRect scroll;
    private RectTransform content;
    private GameObject previousFocus;
    private float scrollTop, dividerY;
    private Sprite selectedTabSprite, defaultTabSprite;
    private int tab;

    internal bool IsOpen => prize != null && isActiveAndEnabled;
    private bool IsCharacter => prize.Kind == RoguelikeWishKind.Character;
    private DataManager Data => language.Names;
    private bool HasCharacterDetails => IsCharacter && Data?.CharacterAttributesDict.ContainsKey(prize.Id) == true;

    internal void Open(UICardFactory factory, RoguelikeWishPrize item, RoguelikeWishLanguage locale)
    {
        if (IsOpen || item == null || !enabled) return;
        previousFocus = EventSystem.current?.currentSelectedGameObject;
        prize = item;
        language = locale;
        cards = factory;
        tab = 0;
        if (textSource != Data)
        {
            textSource = Data;
            characterText = null;
        }
        gameObject.SetActive(true);
        if (content == null) BuildContent();
        if (HasCharacterDetails && characterText == null)
        {
            try { characterText = Data.GetRosterText(); }
            catch (Exception error)
            {
                Debug.LogWarning($"祈愿详情无法读取角色面板文案，改用角色配表原文。{error.Message}", this);
                characterText = new RosterTextCatalog(Data);
            }
        }
        card = RoguelikeWishTile.RequestCard(cards, cardMount, prize);
        RefreshLanguage();
        close.Select();
    }

    internal void Close()
    {
        if (gameObject.activeSelf) gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        close.onClick.AddListener(Close);
        backdrop.onClick.AddListener(Close);
    }

    private void OnDisable()
    {
        close.onClick.RemoveListener(Close);
        backdrop.onClick.RemoveListener(Close);
        if (card != null && cards != null) cards.Release(card);
        cards = null;
        card = null;
        prize = null;
        language = null;
        ClearBody();
        var events = EventSystem.current;
        if (events != null && (events.currentSelectedGameObject == null ||
            events.currentSelectedGameObject.transform.IsChildOf(transform)))
            events.SetSelectedGameObject(previousFocus != null && previousFocus.activeInHierarchy ? previousFocus : null);
        previousFocus = null;
        if (!enabled && gameObject.activeSelf) gameObject.SetActive(false);
    }

    private void Update()
    {
        if (!IsOpen) return;
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            if (!HasCharacterDetails) { close.Select(); return; }
            var focused = EventSystem.current?.currentSelectedGameObject;
            int index = Array.FindIndex(tabs, button => button.gameObject == focused) + 1;
            int count = tabs.Length + 1;
            bool reverse = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            int next = (index + count + (reverse ? -1 : 1)) % count;
            (next == 0 ? close : tabs[next - 1]).Select();
        }
    }

    internal void RefreshLanguage()
    {
        if (!IsOpen) return;
        itemName.text = language.Name(prize);
        stars.text = new string('★', prize.Stars);
        bool hasCharacterDetails = HasCharacterDetails;
        tabBar.gameObject.SetActive(hasCharacterDetails);
        body.offsetMax = new Vector2(body.offsetMax.x, scrollTop + (hasCharacterDetails ? 0 : 52));
        if (divider != null)
            divider.anchoredPosition = new Vector2(divider.anchoredPosition.x, dividerY + (hasCharacterDetails ? 0 : 52));
        var navigation = close.navigation;
        navigation.mode = Navigation.Mode.Explicit;
        navigation.selectOnDown = hasCharacterDetails ? tabs[tab] : null;
        close.navigation = navigation;
        subtitle.text = "";
        for (int i = 0; i < tabs.Length; i++)
        {
            var label = TabNames[i];
            tabLabels[i].text = language.Label(label.Key, label.Zh, label.En);
            tabs[i].GetComponent<LayoutElement>().preferredWidth = Mathf.Ceil(tabLabels[i].preferredWidth) + 36;
        }
        RefreshTabStyles();

        ClearBody();
        bool rendered = IsCharacter ? RenderCharacter() : RenderWeapon();
        if (!rendered)
        {
            string message = IsCharacter && !hasCharacterDetails
                ? language.Label("tip_wish_character_details_missing", "该角色的详情尚未录入。", "Details for this character are not available yet.")
                : language.Label("tip_wish_details_missing", "暂无详情资料。", "Details are unavailable.");
            AddText(content, message, 18);
        }
        LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        scroll.StopMovement();
        scroll.verticalNormalizedPosition = 1;
    }

    private bool RenderCharacter()
    {
        int id = prize.Id;
        var overview = Data?.GetCharacterOverview(id);
        if (Data == null) return false;
        Data.CharacterAttributesDict.TryGetValue(id, out var info);
        subtitle.text = string.Join(" · ", new[] { language.Element(overview?.Element ?? info?.Element),
            language.Nation(overview?.Nation ?? info?.Nation), language.WeaponType(overview?.WeaponType ?? info?.WeaponType ?? -1) }
            .Where(part => !string.IsNullOrEmpty(part)));
        if (info == null) return false;
        if (tab == 0)
        {
            AddText(content, language.Label("ui_wish_base_attributes", "基础属性 · Lv. {0}", "Base Attributes · Lv. {0}", BaseLevel), 19);
            var stats = RoguelikeRosterStats.ReadBase(Data, id, BaseLevel, 0);
            foreach (string key in StatKeys) AddStat(RoguelikeRosterStats.Name(key, language.English), key, stats[key]);
            return true;
        }

        var levels = CharacterSkillLevelInfo.Resolve(id, BaseLevel, 0, 0, new[] { 1, 1, 1, 1 }, Data);
        if (tab == 1)
        {
            string[] ids = { info.NormalAttackID, info.HeavyAttackID, info.ElementalSkillID, info.ElementalBurstID };
            Data.CharacterSkillDict.TryGetValue(id, out var skills);
            for (int i = 0; i < ids.Length; i++)
            {
                var skill = skills?.Find(row => row.SkillID2 == ids[i]);
                var type = SkillTypes[i];
                string category = language.Label(type.Key, type.Zh, type.En);
                string title = skill == null ? category : Data.GetUiName(UiNameKind.Skill, skill.SkillID, skill.SkillName, language.English);
                string caption = language.Label("ui_wish_skill_level", "{0} · Lv. {1}", "{0} · Lv. {1}", category, levels.GetOwnLevel(i));
                AddEntry(title, caption, characterText.CharacterDescription(
                    $"character_{id}_{SkillKeys[i]}_desc", skill?.Description, levels, language.English), skill?.Icon, info.Element);
            }
            return true;
        }
        if (tab == 2 && Data.TalentDict.TryGetValue(id, out var talents))
        {
            foreach (var talent in talents.OrderBy(row => row.TalentIndex))
            {
                int index = talent.TalentIndex;
                string unlock = talent.UnlockAfter <= 0 ? language.Label("ui_talent_unlocked_initial", "初始解锁", "Unlocked by default") :
                    language.Label("ui_talent_unlock_at_ascension", "完成 Lv.{0} 突破后解锁", "Unlocks after the Lv. {0} Ascension.", talent.UnlockAfter);
                AddEntry(Data.GetUiName(UiNameKind.Talent, id, talent.TalentName, language.English, index), unlock,
                    characterText.CharacterDescription($"character_{id}_talent_{index}_desc", talent.Description, levels, language.English),
                    $"{id}_T{index:00}", info.Element);
            }
            return talents.Count != 0;
        }
        if (tab == 3 && Data.ConstellationDict.TryGetValue(id, out var constellations))
        {
            foreach (var row in constellations.OrderBy(item => item.ConstellationIndex))
            {
                int index = row.ConstellationIndex;
                AddEntry(Data.GetUiName(UiNameKind.Constellation, id, row.ConstellationName, language.English, index),
                    language.Label("ui_wish_constellation_level", "命之座第 {0} 层", "Constellation {0}", index), characterText.CharacterDescription(
                        $"character_{id}_constellation_{index}_desc", row.Description, levels, language.English),
                    $"{id}_C{index:00}", info.Element);
            }
            return constellations.Count != 0;
        }
        return false;
    }

    private bool RenderWeapon()
    {
        if (Data == null || !Data.WeaponAttributesDict.TryGetValue(prize.Id, out var weapon)) return false;
        subtitle.text = language.WeaponType(weapon.WeaponType);
        var panel = Data.GetWeaponPanel(prize.Id, BaseLevel, 0);
        AddText(content, language.Label("ui_wish_base_attributes", "基础属性 · Lv. {0}", "Base Attributes · Lv. {0}", BaseLevel), 19);
        AddStat(language.Label("ui_weapon_base_atk", "武器基础攻击力", "Weapon Base ATK"), "ATK", panel.BaseATK);
        if (!string.IsNullOrEmpty(panel.SecondaryAttribute))
            AddStat(RoguelikeRosterStats.Name(panel.SecondaryAttribute, language.English), panel.SecondaryAttribute, panel.SecondaryValue);
        string key = $"weapon_{prize.Id}_effect_title";
        string title = Data.LangDict.ContainsKey(key) ? Data.GetLangText(key, language.English) :
            language.Label("ui_weapon_effect", "武器效果", "Weapon Effect");
        AddEntry(title, language.Label("ui_weapon_refinement", "精炼 {0} 阶", "Refinement Rank {0}", BaseRefinement),
            (characterText ?? new RosterTextCatalog(Data)).WeaponDescription(weapon, BaseRefinement, BaseRefinement, language.English));
        return true;
    }

    private void BuildContent()
    {
        var strip = tabBar.gameObject.AddComponent<HorizontalLayoutGroup>();
        strip.spacing = 9;
        strip.childForceExpandWidth = false;
        strip.childControlWidth = true;
        strip.childControlHeight = true;
        for (int i = 0; i < tabs.Length; i++)
        {
            int index = i;
            var button = Instantiate(close, tabBar, false);
            button.name = TabNames[i].En;
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => { tab = index; RefreshLanguage(); });
            var colors = button.colors;
            colors.selectedColor = colors.highlightedColor;
            button.colors = colors;
            var label = button.GetComponentInChildren<TMP_Text>();
            Stretch(label.rectTransform);
            label.fontSize = 17;
            label.fontStyle = FontStyles.Normal;
            label.color = itemName.color;
            button.gameObject.AddComponent<LayoutElement>().preferredHeight = 40;
            tabs[i] = button;
            tabLabels[i] = label;
        }
        art?.Load("WishButtonLight", 96, sprite => { defaultTabSprite = sprite; RefreshTabStyles(); });
        art?.Load("WishButtonDark", 96, sprite => { selectedTabSprite = sprite; RefreshTabStyles(); });
        scrollTop = body.offsetMax.y;
        dividerY = divider == null ? 0 : divider.anchoredPosition.y;
        for (int i = 0; i < tabs.Length; i++)
        {
            tabs[i].navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit, selectOnUp = close,
                selectOnLeft = tabs[(i + tabs.Length - 1) % tabs.Length],
                selectOnRight = tabs[(i + 1) % tabs.Length]
            };
        }
        scroll = body.gameObject.AddComponent<ScrollRect>();
        body.gameObject.AddComponent<Image>().color = Color.clear;
        body.gameObject.AddComponent<RectMask2D>();
        content = Rect("Content", body);
        content.anchorMin = new Vector2(0, 1);
        content.anchorMax = Vector2.one;
        content.pivot = new Vector2(.5f, 1);
        content.sizeDelta = Vector2.zero;
        content.anchoredPosition = Vector2.zero;
        AddColumn(content, 0).padding = new RectOffset(0, 12, 12, 3);
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.viewport = body;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.scrollSensitivity = 58;
        scroll.movementType = ScrollRect.MovementType.Clamped;
    }

    private void RefreshTabStyles()
    {
        for (int i = 0; i < tabs.Length; i++)
        {
            bool selected = i == tab;
            var image = (Image)tabs[i].targetGraphic;
            var sprite = selected ? selectedTabSprite : defaultTabSprite;
            if (sprite != null)
            {
                image.sprite = sprite;
                image.type = Image.Type.Sliced;
            }
            image.color = sprite != null ? Color.white : selected
                ? new Color32(113, 103, 88, 255) : new Color32(153, 143, 129, 255);
            tabLabels[i].color = selected ? new Color32(248, 239, 221, 255) : itemName.color;
        }
    }

    private void AddStat(string label, string key, float value)
    {
        var row = Rect("Attribute", content);
        row.gameObject.AddComponent<LayoutElement>().minHeight = 43;
        var left = AddText(row, label, 21, TextAlignmentOptions.MidlineLeft);
        Stretch(left.rectTransform, 0, .65f);
        var right = AddText(row, RoguelikeRosterStats.FormatValue(key, value), 21, TextAlignmentOptions.MidlineRight);
        Stretch(right.rectTransform, .65f, 1);
        var rule = Rect("Divider", row);
        rule.anchorMin = Vector2.zero;
        rule.anchorMax = new Vector2(1, 0);
        rule.sizeDelta = new Vector2(0, 1);
        rule.anchoredPosition = Vector2.zero;
        var image = rule.gameObject.AddComponent<Image>();
        image.color = new Color32(49, 40, 33, 58);
        image.raycastTarget = false;
    }

    private void AddEntry(string title, string caption, string description, string icon = null, string element = null)
    {
        var entry = Rect("Detail entry", content);
        var layout = AddColumn(entry, 5);
        layout.padding = new RectOffset(0, 2, 10, 10);
        var header = Rect("Heading", entry);
        var row = header.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = false;
        row.childAlignment = TextAnchor.MiddleLeft;
        row.spacing = 12;
        if (CharacterSkillIconResolver.TryGetRelativePath(icon, out _))
        {
            var badge = Rect("Skill icon background", header);
            var size = badge.gameObject.AddComponent<LayoutElement>();
            size.minWidth = size.preferredWidth = size.minHeight = size.preferredHeight = 41;
            UICharacterAbilityIcon.Create(badge, icon, element);
        }
        var heading = Rect("Title and caption", header);
        heading.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        AddColumn(heading, 3);
        AddText(heading, title, 23);
        AddText(heading, caption, 16);
        if (!string.IsNullOrWhiteSpace(description))
        {
            var label = AddText(entry, description, 18);
            label.richText = true;
            label.lineSpacing = 5;
        }
    }

    private TMP_Text AddText(RectTransform parent, string value, float size, TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft)
    {
        var label = Rect("Text", parent).gameObject.AddComponent<TextMeshProUGUI>();
        label.font = itemName.font;
        label.fontSize = size;
        label.color = itemName.color;
        label.alignment = alignment;
        label.richText = false;
        label.enableWordWrapping = true;
        label.raycastTarget = false;
        label.text = value ?? "";
        return label;
    }

    private static VerticalLayoutGroup AddColumn(RectTransform rect, float spacing)
    {
        var layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = spacing;
        layout.childForceExpandHeight = false;
        return layout;
    }

    private static RectTransform Rect(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    private static void Stretch(RectTransform rect, float left = 0, float right = 1)
    {
        rect.anchorMin = new Vector2(left, 0);
        rect.anchorMax = new Vector2(right, 1);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private void ClearBody()
    {
        if (content == null) return;
        foreach (Transform child in content)
        {
            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }
    }
}
