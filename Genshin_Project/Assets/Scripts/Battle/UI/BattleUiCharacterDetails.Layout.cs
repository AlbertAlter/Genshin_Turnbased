using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class BattleUiCharacterDetails
{
    private static readonly Color Ink = new Color32(55, 46, 38, 255);
    private static readonly Color Deep = new Color32(113, 103, 88, 255);
    private static readonly Color Light = new Color32(215, 200, 172, 255);
    private static readonly Color Paper = new Color32(216, 207, 189, 255);
    private static readonly Color Rule = new Color32(130, 115, 97, 86);
    private static readonly string[] WeaponTypes = { "sword", "claymore", "bow", "polearm", "catalyst" };
    private const string SceneArt = "art_assets/UI/BattleField1/UI_components/";
    private const string DarkDisc = SceneArt + "DeepBrownCircle.png";
    private const string LightDisc = SceneArt + "LightBrownCircle.png";
    private Button[] mainTabs;

    private void BuildWindow()
    {
        overlay = Node("Battle character details", canvas.transform);
        Stretch(overlay);
        var backdrop = Node("Dismiss outside", overlay);
        Stretch(backdrop);
        Click(backdrop, Close, new Color32(0, 0, 0, 179));

        panel = Node("Character panel", overlay);
        Vector2 fraction = new Vector2(Mathf.Clamp(panelFraction.x, .4f, 1), Mathf.Clamp(panelFraction.y, .4f, 1));
        panel.anchorMin = (Vector2.one - fraction) * .5f;
        panel.anchorMax = (Vector2.one + fraction) * .5f;
        panel.offsetMin = panel.offsetMax = Vector2.zero;
        panel.anchoredPosition = new Vector2(0, -12);
        Fill(panel, Color.clear, true);
        var frame = Instantiate(framePrefab, panel, false);
        Stretch((RectTransform)frame.transform);
        frame.BindOpeningDecoration(opening => Fill(Region("Details background", opening, 423f / 1536, 0, 1, 1), new Color32(197, 190, 175, 255)));

        var side = Region("Portrait column", panel, 0, 0, .275f, 1);
        var heading = Region("Title", side, .10f, .90f, .91f, .98f);
        FixedText(heading, Ui("ui_character_details"), 24, TextAlignmentOptions.MidlineLeft);
        var cardArea = Region("Card bounds", side, .13f, .27f, .873f, .895f);
        var cardWindow = Region("Sliding card window", cardArea, 0, 0, 1, 1);
        var aspect = cardWindow.gameObject.AddComponent<AspectRatioFitter>();
        aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        aspect.aspectRatio = UICardFactory.Aspect;
        cardWindow.gameObject.AddComponent<RectMask2D>();
        carousel = cardWindow.gameObject.AddComponent<RosterCardCarousel>();
        carousel.Bind(cardFactory);
        identityName = FixedText(Region("Character name", side, .08f, .20f, .92f, .255f), "", 36);
        identitySummary = FixedText(Region("Character identity", side, .06f, .139f, .94f, .195f), "", 19);
        quickSwitch = Region("Character portraits", side, .08f, .025f, .92f, .099f);

        var right = Region("Details column", panel, .275f, 0, 1, 1);
        right.offsetMin = new Vector2(0, 11);
        right.offsetMax = new Vector2(-11, -10);
        var header = Region("Panel tabs", right, 0, 1, 1, 1);
        header.offsetMin = new Vector2(32, -65);
        header.offsetMax = new Vector2(-30, -14);
        var tabs = Region("Tabs", header, 0, 0, 1, 1);
        tabs.offsetMax = new Vector2(-61, 0);
        mainTabs = TabButtons(tabs, new[] { Ui("btn_tab_stats"), Ui("ui_character_tab_skill"),
            Ui("ui_battle_detail_equipment"), Ui("ui_battle_detail_status") }, 0, index =>
        {
            if (tab == index) return;
            tab = index;
            entry = 0;
            RenderTab();
        });
        var close = Region("Close", header, 1, 0, 1, 1);
        close.offsetMin = new Vector2(-46, 0);
        close.offsetMax = Vector2.zero;
        closeButton = TextButton(close, "×", Close, Deep, 34, Color.white);
        body = Region("Tab body", right, 0, 0, 1, 1);
        body.offsetMin = new Vector2(32, 27);
        body.offsetMax = new Vector2(-30, -92);
    }

    private void BuildQuickSwitch()
    {
        Clear(quickSwitch);
        int count = battle.AllySlotCount;
        for (int index = 0; index < count; index++)
        {
            var member = ActorAt(index);
            if (member == null) continue;
            int selectedSlot = index;
            var tile = Region(CharacterName(member.CharacterID), quickSwitch, (float)index / count, 0, (index + 1f) / count, 1);
            tile.offsetMin = new Vector2(3, 0);
            tile.offsetMax = new Vector2(-3, 0);
            Click(tile, () => { if (slot != selectedSlot) SelectMember(selectedSlot); }, index == slot ? Light : Color.clear);
            var portraitArt = Square(tile, 43);
            Art(portraitArt, "art_assets/" + CharacterCardArtManifest.PortraitFile(member.CharacterID));
        }
    }

    private Button[] TabButtons(RectTransform parent, string[] labels, int selected, Action<int> select)
    {
        Fill(parent, Deep);
        var buttons = new Button[labels.Length];
        for (int i = 0; i < labels.Length; i++)
        {
            int index = i;
            var node = Region(labels[i], parent, (float)i / labels.Length, 0, (i + 1f) / labels.Length, 1);
            node.offsetMin = new Vector2(3, 3);
            node.offsetMax = new Vector2(-3, -3);
            buttons[i] = TextButton(node, labels[i], () => select(index), Deep, 23, Color.white);
        }
        SetSelectedTab(buttons, selected);
        return buttons;
    }

    private static void SetSelectedTab(Button[] buttons, int selected)
    {
        for (int i = 0; i < buttons.Length; i++)
        {
            buttons[i].image.color = i == selected ? Light : Deep;
            buttons[i].GetComponentInChildren<TMP_Text>().color = i == selected ? Ink : Color.white;
        }
    }

    private RectTransform ScrollColumn(RectTransform parent)
    {
        var scroll = parent.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 36;

        var viewport = Region("Scroll viewport", parent, 0, 0, 1, 1);
        viewport.offsetMax = new Vector2(-12, 0);
        Fill(viewport, Color.clear, true);
        viewport.gameObject.AddComponent<RectMask2D>();
        var content = Region("Scroll content", viewport, 0, 1, 1, 1);
        content.pivot = new Vector2(.5f, 1);
        Column(content);
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.viewport = viewport;
        scroll.content = content;

        var track = Region("Scroll bar", parent, 1, 0, 1, 1);
        track.offsetMin = new Vector2(-7, 1);
        track.offsetMax = new Vector2(-2, -1);
        Fill(track, Rule, true);
        var area = Region("Sliding area", track, 0, 0, 1, 1);
        var handle = Region("Handle", area, 0, 0, 1, 1);
        var bar = track.gameObject.AddComponent<Scrollbar>();
        bar.direction = Scrollbar.Direction.BottomToTop;
        bar.handleRect = handle;
        bar.targetGraphic = Fill(handle, Deep, true);
        scroll.verticalScrollbar = bar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        return content;
    }

    private static void Column(RectTransform rect, int padding = 0, float spacing = 8)
    {
        var layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(padding, padding, padding, padding);
        layout.spacing = spacing;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
    }

    private static HorizontalLayoutGroup RowLayout(RectTransform rect, float spacing)
    {
        var layout = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = spacing;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        return layout;
    }

    private TMP_Text FlowText(RectTransform parent, string value, float size, bool rich = false)
        => Text(Node("Text", parent), value, size, TextAlignmentOptions.TopLeft, rich);

    private TMP_Text FixedText(RectTransform parent, string value, float size,
        TextAlignmentOptions align = TextAlignmentOptions.Center, Color? color = null)
    {
        var rect = Region("Label", parent, 0, 0, 1, 1);
        var label = Text(rect, value, size, align, false);
        label.color = color ?? Ink;
        label.enableAutoSizing = true;
        label.fontSizeMin = size * .74f;
        label.fontSizeMax = size;
        return label;
    }

    private TMP_Text Text(RectTransform rect, string value, float size, TextAlignmentOptions alignment, bool rich)
    {
        var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) label.font = font;
        label.fontStyle = FontStyles.Normal;
        label.fontWeight = FontWeight.Regular;
        label.fontSize = size;
        label.alignment = alignment;
        label.color = Ink;
        label.richText = rich;
        label.enableWordWrapping = true;
        label.raycastTarget = false;
        label.text = value;
        return label;
    }

    private Button TextButton(RectTransform rect, string value, Action action, Color fill, float size, Color foreground)
    {
        var button = Click(rect, action, fill);
        FixedText(rect, value, size, color: foreground);
        return button;
    }

    private static Button Click(RectTransform rect, Action action, Color color)
    {
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = Fill(rect, color, true);
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.onClick.AddListener(() => action());
        return button;
    }

    private static Image Fill(RectTransform rect, Color color, bool raycast = false)
    {
        var image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = raycast;
        return image;
    }

    private static Image Art(RectTransform rect, string path, bool vectorCircle = false)
    {
        var image = Fill(rect, Color.white);
        if (string.IsNullOrEmpty(path))
        {
            image.enabled = false;
            return image;
        }
        var asset = rect.gameObject.AddComponent<StreamingSpriteReference>();
        asset.vectorCircle = vectorCircle;
        asset.SetResolvedPath(path);
        return image;
    }

    private static RectTransform Node(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.gameObject.layer = parent.gameObject.layer;
        return rect;
    }

    private static RectTransform Region(string name, Transform parent, float left, float bottom, float right, float top)
    {
        var rect = Node(name, parent);
        rect.anchorMin = new Vector2(left, bottom);
        rect.anchorMax = new Vector2(right, top);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        return rect;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private static void Clear(RectTransform parent)
    {
        for (int index = parent.childCount - 1; index >= 0; index--)
        {
            var child = parent.GetChild(index).gameObject;
            child.SetActive(false);
            Destroy(child);
        }
    }
}
