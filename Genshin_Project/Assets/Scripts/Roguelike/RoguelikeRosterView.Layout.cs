using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed partial class RoguelikeRosterView
{
    public const string CharacterListArtPath = "art_assets/UI/CharacterList/";
    private const string CharacterListPortraits = CharacterListArtPath + "Portraits/";
    private const string CharacterListTileMask = CharacterListArtPath + "Shapes/TileMask.png";
    private const string CharacterListFilterBox = CharacterListArtPath + "Shapes/FilterBox.png";
    private const string RoundedMask = CharacterListArtPath + "Shapes/RoundedMask.png";
    private const string CircleMask = CharacterListArtPath + "Shapes/CircleMask.png";
    private const float ScrollTextGutter = 14;
    private const string DetailArtPath = CharacterListArtPath + "Details/";

    public static string CharacterListPortraitPath(int id) => $"{CharacterListPortraits}{id}.png";

    private static RectTransform Rect(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    private static void Place(RectTransform rect, float x, float y, float w, float h)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(w, h);
    }

    private static void Place(RectTransform rect, Rect bounds) => Place(rect, bounds.x, bounds.y, bounds.width, bounds.height);

    private static RectTransform Box(string name, Transform parent, float x, float y, float w, float h)
    {
        var rect = Rect(name, parent);
        Place(rect, x, y, w, h);
        return rect;
    }

    private static void Stretch(RectTransform rect, float inset = 0)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset); rect.offsetMax = new Vector2(-inset, -inset);
    }

    private static void Clear(Transform rect, Transform keep = null)
    {
        foreach (Transform child in rect)
        {
            if (child == keep) continue;
            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }
    }

    private static Image Fill(RectTransform rect, Color color, bool raycast = false)
    {
        var image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = raycast;
        return image;
    }

    private (RectTransform Root, RectTransform Body) Panel(string name, UIAdaptiveFrame framePrefab, float padding = 32,
        RectTransform parent = null)
    {
        var root = Rect(name, parent != null ? parent : shell);
        root.gameObject.AddComponent<RectMask2D>();
        if (framePrefab != null)
        {
            if (parent != null) Fill(root, Color.clear, true);
            var frame = Instantiate(framePrefab, root, false);
            Stretch((RectTransform)frame.transform);
            frame.fillColor = RosterPaper;
            frame.pixelsPerUnitMultiplier = 64f / 56f;
        }
        else Fill(root, RosterPaper, parent != null);
        var body = Rect("Content", root);
        Stretch(body, padding);
        return (root, body);
    }

    private const int SectionGap = 12;

    private TMP_Text Label(RectTransform parent, string text, int size = 23,
        TextAlignmentOptions alignment = TextAlignmentOptions.Left)
    {
        var rect = Rect("Text", parent);
        if (parent.GetComponent<LayoutGroup>() == null) Stretch(rect);
        var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) label.font = font;
        label.text = text ?? "";
        label.fontSize = size;
        label.lineSpacing = 7;
        label.paragraphSpacing = 14;
        label.color = Ink;
        label.alignment = alignment;
        label.raycastTarget = false;
        if (parent.GetComponent<VerticalLayoutGroup>() != null)
            rect.gameObject.AddComponent<LayoutElement>().minHeight = size + 8;
        else if (parent.GetComponent<HorizontalLayoutGroup>() != null)
            rect.sizeDelta = new Vector2(label.preferredWidth, size + 8);
        return label;
    }

    private Button Button(RectTransform parent, string label, Action action, float width = 150, bool selected = false,
        float height = 46, int fontSize = 21)
    {
        var rect = Rect(label.Length > 0 ? label : "Select", parent);
        rect.sizeDelta = new Vector2(width, height);
        if (parent.GetComponent<LayoutGroup>() == null) Stretch(rect);
        var size = rect.gameObject.AddComponent<LayoutElement>();
        size.preferredWidth = width;
        size.minHeight = height;
        size.preferredHeight = height;
        var button = rect.gameObject.AddComponent<RosterActionButton>();
        button.ShowBlockedMessage = SetMessage;
        button.targetGraphic = Fill(rect, RosterDeep, true);
        var face = Fill(Rect("Face", rect), RosterPaper);
        Stretch(face.rectTransform, 1);
        StyleButton(button, RosterPaper, selected ? Ink : RosterDeep);
        if (action != null) button.onClick.AddListener(() => Attempt(action));
        if (label.Length == 0) return button;
        var text = Label(rect, label, fontSize, TextAlignmentOptions.Center);
        Stretch((RectTransform)text.transform, 5);
        text.enableAutoSizing = true;
        text.fontSizeMin = Mathf.Min(16, fontSize);
        text.fontSizeMax = fontSize;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        return button;
    }

    private static void Block(Button button, string reason) => ((RosterActionButton)button).SetBlockedReason(reason);

    private string ReadOnlyReason => session.CanEdit ? null :
        Ui("tip_roster_read_only", "当前流程无法修改养成或装备。", "Progression and equipment are read-only at this step.");

    private void SetRounded(Image image, Color color)
    {
        image.color = color;
        if (images.TryGetValue(RoundedMask, out var sprite) && sprite != null)
        {
            image.sprite = sprite;
            image.type = UnityEngine.UI.Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 4f;
        }
    }

    private void StyleButton(Button button, Color face, Color border)
    {
        SetRounded((Image)button.targetGraphic, border);
        var fill = button.transform.Find("Face").GetComponent<Image>();
        SetRounded(fill, face);
    }

    private static void BottomRule(RectTransform parent, Color color, float inset = 0, float y = 0)
    {
        var line = Rect("Rule", parent);
        line.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        line.anchorMin = Vector2.zero;
        line.anchorMax = new Vector2(1, 0);
        line.offsetMin = new Vector2(inset, y);
        line.offsetMax = new Vector2(-inset, y + 1);
        Fill(line, color);
    }

    private RectTransform tabsRoot;
    private Button[] tabButtons;
    private string[] tabLabels;
    private Action<int> selectTab;
    private int highlightedTab = -1;

    private void Tabs(RectTransform host, string[] names, int selected, Action<int> select)
    {
        selectTab = select;
        if (tabsRoot == null || !tabsRoot.gameObject.activeSelf || !tabLabels.SequenceEqual(names))
            BuildTabs(host, names);
        if (highlightedTab == selected) return;
        if (highlightedTab >= 0) StyleButton(tabButtons[highlightedTab], RosterPaper, Color.clear);
        StyleButton(tabButtons[selected], RosterPaper, Ink);
        highlightedTab = selected;
    }

    private void BuildTabs(RectTransform parent, string[] names)
    {
        if (tabsRoot != null)
        {
            tabsRoot.gameObject.SetActive(false);
            Destroy(tabsRoot.gameObject);
        }
        var host = tabsRoot = Rect("Tab strip", parent);
        Stretch(host);
        tabButtons = new Button[names.Length];
        tabLabels = names;
        highlightedTab = -1;
        const float padding = 6, gap = 5;
        var rim = Fill(Rect("Tab frame", host), RosterDeep);
        Stretch(rim.rectTransform);
        SetRounded(rim, RosterDeep);
        var background = Fill(Rect("Tab background", host), RosterPaper);
        Stretch(background.rectTransform, 1);
        SetRounded(background, RosterPaper);
        float width = (host.rect.width - 2 * padding - (names.Length - 1) * gap) / names.Length;
        for (int i = 0; i < names.Length; i++)
        {
            int index = i;
            float start = i / (float)names.Length, end = (i + 1) / (float)names.Length;
            var mount = Rect("Tab", host);
            mount.anchorMin = new Vector2(start, 1);
            mount.anchorMax = new Vector2(end, 1);
            mount.offsetMin = new Vector2(padding * (1 - 2 * start) + gap * start, -padding - 42);
            mount.offsetMax = new Vector2(padding * (1 - 2 * end) + gap * (end - 1), -padding);
            var button = Button(mount, names[i], () => selectTab(index), width, height: 42);
            StyleButton(button, RosterPaper, Color.clear);
            tabButtons[i] = button;
        }
    }

    private void SquareArt(RectTransform mount, string path, int rarity, UIItemListIcon.IconKind kind, float inset = 1,
        bool framed = true)
    {
        if (framed)
        {
            var rim = Fill(Rect("Icon frame", mount), new Color32(243, 233, 216, 255));
            Stretch(rim.rectTransform);
            SetRounded(rim, rim.color);
        }
        var clip = Fill(Rect("Icon mask", mount), Color.white);
        Stretch(clip.rectTransform, inset);
        SetRounded(clip, Color.white);
        clip.gameObject.AddComponent<Mask>().showMaskGraphic = false;
        var background = Fill(Rect("Rarity", clip.transform), RosterDeep);
        Stretch(background.rectTransform);
        RaritySprite(rarity, sprite =>
        {
            if (background == null || sprite == null) return;
            background.sprite = sprite;
            background.color = Color.white;
        });
        if (string.IsNullOrEmpty(path))
            Label(clip.rectTransform, "＋", 28, TextAlignmentOptions.Center).color = RosterWhite;
        else
        {
            var icon = Fill(Rect("Icon", clip.transform), Color.white);
            icon.enabled = false;
            SpriteFor(path, sprite =>
            {
                if (icon != null) UIItemListIcon.ApplySquareIcon(icon, sprite, kind);
            });
        }
    }

    private void RaritySprite(int rarity, Action<Sprite> receive)
    {
        string fallback = UIItemListIcon.GetRarityBackdropPath(rarity);
        if (rarity == 4 || rarity == 5)
            SpriteFor(CharacterListArtPath + "Rarity/" + System.IO.Path.GetFileName(fallback), sprite =>
            {
                if (sprite == null) SpriteFor(fallback, receive);
                else receive(sprite);
            });
        else SpriteFor(fallback, receive);
    }

    private void IconDisc(RectTransform mount, string configuredIcon, string element)
    {
        UICharacterAbilityIcon.Create(mount, configuredIcon, element, SpriteFor);
    }

    private RectTransform Heading(RectTransform parent, string title, string meta, string art = null, int rarity = 0,
        UIItemListIcon.IconKind kind = UIItemListIcon.IconKind.Generic, float iconSize = 86, string element = null,
        float trailing = 0)
    {
        var block = Rect("Heading", parent);
        block.gameObject.AddComponent<LayoutElement>().minHeight = (art != null ? Mathf.Max(iconSize, 80) : 80) + 14;
        float left = 0;
        if (art != null)
        {
            SquareArt(Box("Heading art", block, 0, 0, iconSize, iconSize), art, rarity, kind);
            left = iconSize + 14;
        }
        var text = Rect("Heading text", block);
        Stretch(text);
        text.offsetMin = new Vector2(left, 14);
        text.offsetMax = new Vector2(-trailing, 0);
        var name = Rect("Name", text);
        name.anchorMin = new Vector2(0, 1);
        name.anchorMax = Vector2.one;
        name.offsetMin = new Vector2(0, -50);
        name.offsetMax = Vector2.zero;
        FitLine(Label(name, title, 39), 24);
        var subtitle = Rect("Metadata", text);
        subtitle.anchorMin = new Vector2(0, 1);
        subtitle.anchorMax = Vector2.one;
        subtitle.offsetMin = new Vector2(element == null ? 0 : 28, -82);
        subtitle.offsetMax = new Vector2(0, -52);
        FitLine(Label(subtitle, meta, 20), 14).color = Muted;
        if (element != null)
        {
            var icon = Image(ElementColorPalette.ElementIconPath(element), text);
            UICharacterAbilityIcon.ApplyElementColor(icon, element);
            Place((RectTransform)icon.transform, 0, 56, 22, 22);
        }
        BottomRule(block, HeadingRule);
        return block;
    }

    private static void Horizontal(RectTransform root, int spacing, TextAnchor alignment = TextAnchor.MiddleLeft)
    {
        var row = root.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.spacing = spacing; row.childAlignment = alignment;
        row.childControlWidth = row.childControlHeight = false;
        row.childForceExpandWidth = row.childForceExpandHeight = false;
    }

    private static RectTransform Row(RectTransform parent, float height = 50, TextAnchor align = TextAnchor.MiddleLeft)
    {
        var row = Rect("Row", parent);
        if (parent.GetComponent<LayoutGroup>() == null) Stretch(row);
        row.gameObject.AddComponent<LayoutElement>().minHeight = height;
        Horizontal(row, 8, align);
        return row;
    }

    private static RectTransform Column(RectTransform parent)
    {
        var col = Rect("Column", parent);
        Stretch(col);
        ColumnLayout(col);
        return col;
    }

    private static void ColumnLayout(RectTransform col)
    {
        var layout = col.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = SectionGap;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;
    }

    private static TMP_Text FitLine(TMP_Text text, float minimum)
    {
        text.fontSizeMax = text.fontSize;
        text.fontSizeMin = minimum;
        text.enableAutoSizing = true;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        return text;
    }

    private RectTransform Scroll(RectTransform parent, string key, float rightGutter = 0)
    {
        var area = Rect("Scroll", parent);
        Stretch(area);
        var scroll = area.gameObject.AddComponent<ScrollRect>();
        var viewport = Rect("Viewport", area);
        Stretch(viewport);
        viewport.offsetMax = new Vector2(-rightGutter, 0);
        Fill(viewport, Color.clear, true);
        viewport.gameObject.AddComponent<RectMask2D>();
        var content = Rect("Content", viewport);
        content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one;
        content.pivot = new Vector2(.5f, 1);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.viewport = viewport; scroll.content = content;
        scroll.horizontal = false;
        scroll.scrollSensitivity = 62;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        var track = Rect("Scroll track", area);
        track.anchorMin = new Vector2(1, 0);
        track.anchorMax = Vector2.one;
        track.offsetMin = new Vector2(-6, 2);
        track.offsetMax = new Vector2(0, -2);
        var trackColor = new Color32(94, 84, 70, 41);
        SetRounded(Fill(track, trackColor, true), trackColor);
        var thumb = Rect("Scroll thumb", track);
        Stretch(thumb);
        var thumbColor = new Color32(139, 123, 101, 255);
        var handle = Fill(thumb, thumbColor, true);
        SetRounded(handle, thumbColor);
        var scrollbar = track.gameObject.AddComponent<Scrollbar>();
        scrollbar.targetGraphic = handle;
        scrollbar.handleRect = thumb;
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        scroll.verticalScrollbar = scrollbar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        scrolls[key] = scroll;
        return content;
    }

    private RectTransform DetailScroll(RectTransform parent, string key, out RectTransform footer, float footerHeight = 66, float top = 0)
    {
        var body = Rect("Detail body", parent);
        Stretch(body);
        body.offsetMin = new Vector2(2, footerHeight + (footerHeight > 0 ? 12 : 0));
        body.offsetMax = new Vector2(-8, -top - 2);
        var content = Scroll(body, key, ScrollTextGutter);
        ColumnLayout(content);
        footer = Rect("Footer", parent);
        footer.anchorMin = Vector2.zero; footer.anchorMax = new Vector2(1, 0);
        footer.pivot = new Vector2(.5f, 0); footer.sizeDelta = new Vector2(0, footerHeight);
        Horizontal(footer, 8, TextAnchor.LowerRight);
        if (footerHeight > 0)
        {
            var rule = Rect("Actions rule", parent);
            rule.anchorMin = Vector2.zero; rule.anchorMax = new Vector2(1, 0);
            rule.offsetMin = new Vector2(0, footerHeight + 6);
            rule.offsetMax = new Vector2(0, footerHeight + 7);
            Fill(rule, Rule);
        }
        return content;
    }

    private RectTransform Grid(RectTransform parent, string key, float size, float top = 0, int columns = 0, int gap = 18)
    {
        var host = Rect("Grid host", parent);
        Stretch(host);
        host.offsetMax = new Vector2(0, -top);
        var content = Scroll(host, key);
        var grid = content.gameObject.AddComponent<GridLayoutGroup>();
        bool compact = columns > 0;
        grid.cellSize = UIItemListIcon.GetCellSize(size, compact);
        grid.spacing = Vector2.one * (compact ? gap : 10);
        grid.padding = compact ? new RectOffset(4, 4, 4, 4) : new RectOffset(3, 3, 3, 5);
        if (compact)
        {
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = columns;
        }
        return content;
    }

    private RectTransform EquipmentGrid(RectTransform parent, string key, float width, float minimum, float top, out float size)
    {
        int columns = Mathf.Max(1, Mathf.FloorToInt(width / (minimum + 8)));
        size = Mathf.Max(1, (width - 8 * columns) / columns);
        return Grid(parent, key, size, top, columns, 8);
    }

    private void SideHeader(string title, string subtitle, Action close, string art = null, int rarity = 0,
        UIItemListIcon.IconKind kind = UIItemListIcon.IconKind.Generic)
    {
        float width = sidePanelWidth - 52;
        var block = Box("Side header", sideBody, 0, 0, width, 92);
        float left = art == null ? 0 : 69;
        if (art != null) SquareArt(Box("Side icon", block, 0, 6, 57, 57), art, rarity, kind);
        Label(Box("Title", block, left, 0, width - left - 56, 49), title, 36);
        Label(Box("Subtitle", block, left, 50, width - left - 56, 28), subtitle, 20).color = Muted;
        Button(Box("Close", block, width - 44, 0, 44, 40), "×", close, 44, height: 40, fontSize: 28);
        BottomRule(block, HeadingRule);
    }

    private Image Image(string path, RectTransform parent)
    {
        var image = Fill(Rect("Icon", parent), Color.white);
        image.enabled = false; image.preserveAspect = true;
        SpriteFor(path, sprite =>
        {
            if (image == null) return;
            image.sprite = sprite;
            image.enabled = sprite != null;
        });
        return image;
    }

    private void SpriteFor(string path, Action<Sprite> receive)
    {
        if (string.IsNullOrEmpty(path)) { receive(null); return; }
        if (images.TryGetValue(path, out var known)) { receive(known); return; }
        if (loading.TryGetValue(path, out var waiting)) { waiting.Add(receive); return; }
        loading.Add(path, new List<Action<Sprite>> { receive });
        StartCoroutine(LoadSprite(path));
    }

    private IEnumerator LoadSprite(string path)
    {
        Sprite value = null;
        yield return StreamingAssetSpriteLoader.Load(path, sprite => value = sprite);
        if (value == null && path.StartsWith("art_assets/Character_Icon/", StringComparison.Ordinal) &&
            path.EndsWith("_04.png", StringComparison.Ordinal))
            yield return StreamingAssetSpriteLoader.Load(path.Substring(0, path.Length - 7) + "_02.png", sprite => value = sprite);
        if (value == null && path.StartsWith(CharacterListPortraits, StringComparison.Ordinal))
        {
            string id = System.IO.Path.GetFileNameWithoutExtension(path);
            yield return StreamingAssetSpriteLoader.Load($"art_assets/Character_Icon/{id}_02.png", sprite => value = sprite);
        }
        images[path] = value;
        var callbacks = loading[path];
        loading.Remove(path);
        foreach (var callback in callbacks) callback(value);
    }

    private IEnumerator LoadTileSkin()
    {
        foreach (string path in new[] { UIItemListIcon.IconMaskPath, UIItemListIcon.ValueBarPath,
                     UIItemListIcon.SelectionOutlinePath })
        {
            Sprite sprite = null;
            yield return StreamingAssetSpriteLoader.Load(path, value => sprite = value, SetMessage);
            if (sprite == null) yield break;
            images.Add(path, sprite);
        }
        foreach (string path in new[] { CharacterListTileMask, CharacterListFilterBox, RoundedMask, CircleMask })
        {
            Vector4 border = path == CharacterListFilterBox ? Vector4.one * 24f :
                path == RoundedMask ? Vector4.one * 28f : Vector4.zero;
            yield return StreamingAssetSpriteLoader.Load(path, sprite => images[path] = sprite,
                error => Debug.LogWarning(error, this), border);
        }
        tileSkinReady = true;
        try { Render(); }
        catch (Exception error) { SetMessage(error.Message); Debug.LogException(error, this); }
    }

    private UIItemListIcon Tile(RectTransform parent, string art, int rarity, string value, UIItemListIcon.IconKind kind,
        Action onClick, float size = 106, bool selected = false, int rank = 0, int owner = 0, bool locked = false,
        bool compact = false, Func<bool> selection = null)
    {
        var tile = CreateTile(parent, size, compact);
        BindTile(tile, art, rarity, value, kind, onClick, size, selection ?? (() => selected), rank, owner, locked);
        return tile;
    }

    private UIItemListIcon CreateTile(RectTransform parent, float size, bool compact)
    {
        images.TryGetValue(CharacterListTileMask, out var tileMask);
        var tile = UIItemListIcon.Create(parent, size, font, images[UIItemListIcon.IconMaskPath],
            images[UIItemListIcon.ValueBarPath], images[UIItemListIcon.SelectionOutlinePath], compact, tileMask);
        var rect = (RectTransform)tile.transform;
        var layout = rect.gameObject.AddComponent<LayoutElement>();
        layout.preferredWidth = size;
        layout.preferredHeight = UIItemListIcon.GetCellSize(size, compact).y;
        return tile;
    }

    private void BindTile(UIItemListIcon tile, string art, int rarity, string value, UIItemListIcon.IconKind kind,
        Action onClick, float size, Func<bool> isSelected, int rank, int owner, bool locked, Func<bool> isCurrent = null)
    {
        var rect = (RectTransform)tile.transform;
        Sprite icon = null, backdrop = null, avatar = null;
        bool binding = true;
        void Apply()
        {
            if (tile != null && (isCurrent == null || isCurrent())) tile.Bind(icon, backdrop, value, avatar,
                onClick == null ? null : () => Attempt(onClick), isSelected(), kind,
                markIndividualSelection: kind == UIItemListIcon.IconKind.Weapon || kind == UIItemListIcon.IconKind.Artifact,
                refinementRank: rank, rarity: kind == UIItemListIcon.IconKind.Character ? 0 : rarity,
                ownerAtTop: kind == UIItemListIcon.IconKind.Weapon || kind == UIItemListIcon.IconKind.Artifact || kind == UIItemListIcon.IconKind.Material);
        }
        SpriteFor(art, sprite => { icon = sprite; if (!binding) Apply(); });
        RaritySprite(rarity, sprite => { backdrop = sprite; if (!binding) Apply(); });
        if (owner > 0) SpriteFor(CharacterListPortraitPath(owner), sprite => { avatar = sprite; if (!binding) Apply(); });
        binding = false;
        Apply();
        var lockMount = rect.Find("Locked") as RectTransform;
        if (!locked && lockMount != null) lockMount.gameObject.SetActive(false);
        if (locked)
        {
            float badge = size * .22f;
            if (lockMount == null)
            {
                lockMount = Rect("Locked", rect);
                var background = new Color32(41, 37, 31, 191);
                SetRounded(Fill(lockMount, background), background);
                var lockIcon = Image(DetailArtPath + "Lock.png", lockMount);
                Stretch(lockIcon.rectTransform, badge * .14f);
                lockIcon.color = new Color32(255, 160, 138, 255);
            }
            Place(lockMount, size * .05f, size * (rank > 0 ? .32f : .05f), badge, badge);
            lockMount.gameObject.SetActive(true);
        }
    }

    private void Filter<TValue>(RectTransform parent, string label, TValue selected, IEnumerable<(TValue Value, string Text)> source,
        Action<TValue> change, float width = 202, bool roster = false)
    {
        var options = source.ToArray();
        string current = options.FirstOrDefault(o => EqualityComparer<TValue>.Default.Equals(o.Value, selected)).Text;
        current ??= T("全部", "All");
        Button control = null;
        void Open()
        {
            float height = roster ? Mathf.Min(285, options.Length * 40 + 8) : Mathf.Min(350, options.Length * 56 + 16);
            float popupWidth = roster ? ((RectTransform)control.transform).rect.width : 300;
            var popup = OpenFilterPopup(control, height, popupWidth, roster);
            var body = Scroll(popup, "filter");
            if (roster) Stretch((RectTransform)scrolls["filter"].transform, 5);
            ColumnLayout(body);
            if (roster) body.GetComponent<VerticalLayoutGroup>().spacing = 2;
            var buttons = new List<Button>();
            Button selectedButton = null;
            foreach (var option in options)
            {
                bool active = EqualityComparer<TValue>.Default.Equals(option.Value, selected);
                var item = Button(body, option.Text, () =>
                {
                    selected = option.Value;
                    control.GetComponentInChildren<TMP_Text>().text = roster ? option.Text : label + "  ·  " + option.Text;
                    change(option.Value);
                    ApplyFilters();
                }, roster ? popupWidth - 10 : 280, active, roster ? 38 : 46, roster ? 20 : 21);
                if (roster)
                {
                    StyleButton(item, active ? RosterDeep : RosterPaper, Color.clear);
                    StyleRosterFilterText(item, active ? RosterWhite : RosterInk, 9f);
                }
                if (active) selectedButton = item;
                buttons.Add(item);
            }
            FocusFilter(control, buttons, selectedButton);
        }

        if (!roster)
        {
            control = Button(parent, label + "  ·  " + current, Open, width);
            return;
        }

        control = Dropdown(parent, label, current, Open, width);
    }

    private Button Dropdown(RectTransform parent, string label, string current, Action open, float width)
    {
        var field = Rect(label, parent);
        field.sizeDelta = new Vector2(width, 74.6f);
        var caption = Box("Label", field, 0, 0, 0, 25.2f);
        caption.anchorMax = Vector2.one;
        Label(caption, label, 18).color = RosterInk;
        var mount = Box("Dropdown", field, 0, 29.2f, 0, 45.4f);
        mount.anchorMax = Vector2.one;
        var control = Button(mount, current, open, width, height: 45.4f);
        StyleRosterFilterBox((Image)control.targetGraphic);
        StyleRosterFilterText(control, RosterInk, 11f, 28f);
        var arrow = Image(CharacterListArtPath + "Shapes/DropdownChevron.png", control.transform as RectTransform);
        var arrowRect = (RectTransform)arrow.transform;
        Place(arrowRect, -24f, 13.7f, 14f, 14f);
        arrowRect.anchorMin = arrowRect.anchorMax = Vector2.one;
        return control;
    }

    private void StyleRosterFilterBox(Image image)
    {
        if (images.TryGetValue(CharacterListFilterBox, out var sprite) && sprite != null)
        {
            image.sprite = sprite;
            image.type = UnityEngine.UI.Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 4f;
            image.color = Color.white;
        }
        else image.color = RosterPaper;
    }

    private static void StyleRosterFilterText(Button button, Color color, float left, float right = 9f)
    {
        var text = button.GetComponentInChildren<TMP_Text>();
        text.color = color;
        text.alignment = TextAlignmentOptions.Left;
        text.enableAutoSizing = false;
        text.rectTransform.offsetMin = new Vector2(left, 0f);
        text.rectTransform.offsetMax = new Vector2(-right, 0f);
    }

    private Button filterAnchor;
    private List<Button> filterKeys;

    private void FocusFilter(Button anchor, List<Button> buttons, Button selected = null)
    {
        filterAnchor = anchor;
        filterKeys = buttons;
        for (int i = 0; i < buttons.Count; i++)
        {
            var before = buttons[Mathf.Max(0, i - 1)];
            var after = buttons[Mathf.Min(buttons.Count - 1, i + 1)];
            buttons[i].navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = before,
                selectOnDown = after, selectOnLeft = before, selectOnRight = after };
        }
        (selected ?? buttons.FirstOrDefault())?.Select();
    }

    private void UpdateFilterKeys()
    {
        if (filterKeys == null || filterKeys.Count == 0 || EventSystem.current == null) return;
        int index = filterKeys.FindIndex(button => button.gameObject == EventSystem.current.currentSelectedGameObject);
        if (Input.GetKeyDown(KeyCode.Home)) filterKeys[0].Select();
        else if (Input.GetKeyDown(KeyCode.End)) filterKeys[filterKeys.Count - 1].Select();
        else if (Input.GetKeyDown(KeyCode.Tab))
        {
            int direction = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? -1 : 1;
            filterKeys[(index + direction + filterKeys.Count) % filterKeys.Count].Select();
        }
    }

    private void CloseFilterPopup()
    {
        if (filterPopup == null) return;
        filterPopup.gameObject.SetActive(false);
        Destroy(filterPopup.gameObject);
        filterPopup = null;
        if (filterAnchor != null && filterAnchor.isActiveAndEnabled) filterAnchor.Select();
        filterAnchor = null;
        filterKeys = null;
    }

    private RectTransform OpenFilterLayer(float opacity)
    {
        CloseFilterPopup();
        materialHold = null;
        filterPopup = Rect("Filter layer", shell);
        Stretch(filterPopup);
        var backdrop = Rect("Dismiss filter", filterPopup);
        Stretch(backdrop);
        var shade = Fill(backdrop, new Color(.196f, .165f, .137f, opacity), true);
        var dismiss = backdrop.gameObject.AddComponent<Button>();
        dismiss.targetGraphic = shade;
        dismiss.transition = Selectable.Transition.None;
        dismiss.navigation = new Navigation { mode = Navigation.Mode.None };
        dismiss.onClick.AddListener(CloseFilterPopup);
        return filterPopup;
    }

    private RectTransform OpenFilterPopup(Button control, float height, float width = 300, bool roster = false)
    {
        OpenFilterLayer(roster ? 0f : .15f);
        var position = shell.InverseTransformPoint(control.transform.position);
        float x = Mathf.Clamp(position.x + Width / 2 - 120, 30, Width - width - 30);
        float y = Mathf.Clamp(Height / 2 - position.y + 30, 90, Height - height - 30);
        if (roster)
        {
            var rect = (RectTransform)control.transform;
            Vector3 corner = rect.TransformPoint(new Vector3(rect.rect.xMin, rect.rect.yMin));
            var bottomLeft = shell.InverseTransformPoint(corner);
            x = Mathf.Clamp(bottomLeft.x - shell.rect.xMin, 0, shell.rect.width - width);
            y = Mathf.Clamp(shell.rect.yMax - bottomLeft.y + 4f, 0, shell.rect.height - height);
        }
        var popup = Box("Filter options", filterPopup, x, y, width, height);
        var background = Fill(popup, RosterPaper, true);
        if (roster) StyleRosterFilterBox(background);
        return popup;
    }

    private void ApplyFilters()
    {
        if (picking && pickerSelection != null)
        {
            bool shown = characterTab == CharacterTab.Weapon
                ? PickerWeapons(Character).Any(item => item.InstanceId == pickerSelection)
                : FilterArtifacts(artifactSlot, pickerArtifactFilters).Any(item => item.InstanceId == pickerSelection);
            if (!shown) { pickerSelection = null; pickerRemove = false; }
        }
        if (picking && refreshPickerResults != null)
        {
            CloseFilterPopup();
            refreshPickerResults();
            return;
        }
        if (inBag && refreshBagGear != null)
        {
            CloseFilterPopup();
            refreshBagGear();
            return;
        }
        string key = formationPicker.HasValue && characterId == 0 ? "formation-picker" :
            inBag ? "inventory:" + bagTab : picking ? "picker:" + characterTab : "roster";
        scrollPositions.Remove(key);
        scrolls.Remove(key);
        Render();
    }

    private void OrderedFilter<TValue>(RectTransform parent, string label, List<TValue> active,
        IEnumerable<(TValue Value, string Text, string Icon, int Count)> source, Action applied, float controlWidth,
        bool modal, Action<RectTransform, TValue> showPreview = null, Action immediate = null)
    {
        var options = modal ? source.ToArray() :
            new[] { (Value: default(TValue), Text: T("全部", "All"), Icon: (string)null, Count: -1) }.Concat(source).ToArray();
        var cream = new Color32(243, 233, 216, 255);
        Button control = null;
        string Summary() => active.Count > 0 ? T($"{active.Count} 项", $"{active.Count} selected") : T("全部", "All");
        void Open()
        {
            var draft = modal ? new List<TValue>(active) : active;
            bool bySet = modal && showPreview != null;
            float width = modal ? (bySet ? 1420 : 640) : 490;
            float height = modal ? (bySet ? 830 : 730) : Mathf.Clamp(options.Length * 56 + 24, 80, 450);
            var controls = new List<Button>();
            RectTransform content;
            if (modal)
            {
                var (panel, inner) = Panel("Artifact filter", panelFrame, 34, OpenFilterLayer(.56f));
                Place(panel, (Width - width) / 2, (Height - height) / 2, width, height);
                content = inner;
                float innerWidth = width - 68;
                Label(Box("Filter title", content, 0, 0, innerWidth - 60, 44), label, 34);
                Label(Box("Instructions", content, 0, 48, innerWidth - 60, 28),
                    T("按点击顺序设定筛选优先级", "Selection order sets filter priority"), 20).color = Muted;
                controls.Add(Button(Box("Close", content, innerWidth - 44, 0, 44, 40), "×", CloseFilterPopup, 44, height: 40));
            }
            else
            {
                var popup = OpenFilterPopup(control, height, width, roster: true);
                content = Rect("Filter content", popup);
                Stretch(content, 12);
            }
            float contentWidth = width - (modal ? 68 : 24);
            float top = modal ? 94 : 0;
            float footerHeight = modal ? 54 : 0;
            var footer = Rect("Filter actions", content);
            footer.anchorMin = Vector2.zero;
            footer.anchorMax = new Vector2(1, 0);
            footer.pivot = new Vector2(.5f, 0);
            footer.sizeDelta = new Vector2(0, footerHeight);
            Horizontal(footer, 8, TextAnchor.LowerRight);
            float optionsWidth = bySet ? (contentWidth - 18) * 2.3f / 3.3f : contentWidth;
            var optionsHost = Rect("Filter choices", content);
            Stretch(optionsHost);
            optionsHost.offsetMin = new Vector2(0, modal ? footerHeight + 14 : 0);
            optionsHost.offsetMax = new Vector2(optionsWidth - contentWidth, -top);
            var choices = Scroll(optionsHost, "ordered-filter");
            var grid = choices.gameObject.AddComponent<GridLayoutGroup>();
            int columns = bySet ? 2 : 1;
            float cellWidth = (optionsWidth - 8 * columns) / columns;
            grid.cellSize = new Vector2(cellWidth, modal ? 62 : 48);
            grid.spacing = Vector2.one * 8;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = columns;
            RectTransform preview = null;
            if (bySet)
            {
                var previewHost = Rect("Set preview", content);
                Stretch(previewHost);
                previewHost.offsetMin = new Vector2(optionsWidth + 18, footerHeight + 14);
                previewHost.offsetMax = new Vector2(0, -top);
                var fill = new Color32(187, 177, 164, 255);
                SetRounded(Fill(previewHost, fill), fill);
                var inset = Rect("Preview content", previewHost);
                Stretch(inset, 15);
                preview = Scroll(inset, "ordered-filter-preview", ScrollTextGutter);
                ColumnLayout(preview);
            }
            TValue previewValue = draft.Count > 0 ? draft[0] : options.Length > 0 ? options[0].Value : default;
            var rows = new List<(Button Button, TMP_Text Priority, Image Marker)>();
            void Refresh()
            {
                for (int index = 0; index < options.Length; index++)
                {
                    bool all = options[index].Count < 0;
                    int priority = all ? -1 : draft.IndexOf(options[index].Value);
                    var row = rows[index];
                    StyleButton(row.Button, priority >= 0 || (all && draft.Count == 0) ? cream : RosterPaper, RosterDeep);
                    row.Priority.text = priority < 0 ? "" : (priority + 1).ToString();
                    row.Priority.color = priority < 0 ? Ink : cream;
                    row.Marker.color = priority < 0 ? Selected : RosterDeep;
                }
                if (preview != null)
                {
                    Clear(preview);
                    if (options.Length > 0) showPreview(preview, previewValue);
                }
            }
            foreach (var item in options)
            {
                var row = Button(choices, item.Text, () =>
                {
                    if (item.Count < 0) draft.Clear();
                    else if (!draft.Remove(item.Value)) draft.Add(item.Value);
                    previewValue = item.Value;
                    Refresh();
                    if (!modal)
                    {
                        control.GetComponentInChildren<TMP_Text>().text = Summary();
                        applied();
                        immediate?.Invoke();
                    }
                }, cellWidth, height: grid.cellSize.y, fontSize: 19);
                StyleRosterFilterText(row, Ink, 87, 42);
                var mount = (RectTransform)row.transform;
                float center = grid.cellSize.y * .5f;
                var badge = Box("Priority", mount, 10, center - 14, 28, 28);
                var marker = Fill(badge, Selected);
                if (images.TryGetValue(CircleMask, out var circle)) marker.sprite = circle;
                var rank = Label(badge, "", 16, TextAlignmentOptions.Center);
                Place((RectTransform)Image(item.Icon, mount).transform, 45, center - 16, 32, 32);
                if (modal)
                    Label(Box("Match count", mount, cellWidth - 42, 0, 32, grid.cellSize.y), item.Count.ToString(), 18,
                        TextAlignmentOptions.Right).color = Muted;
                rows.Add((row, rank, marker));
                controls.Add(row);
            }
            if (options.Length == 0)
                Label(Box("Empty choices", optionsHost, 3, 3, optionsWidth - 6, 50), T("无可选项目", "No choices")).color = Muted;
            if (modal)
            {
                controls.Add(Button(footer, T("取消", "Cancel"), CloseFilterPopup, 130));
                controls.Add(Button(footer, T("重置", "Reset"), () =>
                {
                    draft.Clear();
                    if (options.Length > 0) previewValue = options[0].Value;
                    Refresh();
                }, 130));
                var confirm = Button(footer, T("应用", "Apply"), () =>
                {
                    active.Clear();
                    active.AddRange(draft);
                    control.GetComponentInChildren<TMP_Text>().text = label + (active.Count > 0 ? " · " + active.Count : "");
                    applied();
                    ApplyFilters();
                }, 130);
                controls.Add(confirm);
                StyleButton(confirm, cream, Ink);
            }
            Refresh();
            FocusFilter(control, controls);
        }
        control = modal ? Button(parent, label + (active.Count > 0 ? " · " + active.Count : ""), Open, controlWidth) :
            Dropdown(parent, label, Summary(), Open, controlWidth);
        if (modal) StyleButton(control, cream, RosterDeep);
    }

    private IEnumerable<(int, string)> WeaponOptions()
        => new[] { (-1, T("全部", "All")) }.Concat(Enumerable.Range(0, 5).Select(type => (type, WeaponType(type))));

    private IEnumerable<(int, string)> RarityOptions()
        => new[] { (0, T("全部", "All")), (5, "5★"), (4, "4★"), (3, "3★") };
}

internal sealed class RosterActionButton : Button
{
    public Action<string> ShowBlockedMessage;
    private string blockedReason;
    private CanvasGroup blockedAppearance;

    public void SetBlockedReason(string reason)
    {
        interactable = string.IsNullOrEmpty(reason);
        if (blockedReason == reason) return;
        blockedReason = reason;
        if (!interactable && blockedAppearance == null) blockedAppearance = gameObject.AddComponent<CanvasGroup>();
        if (blockedAppearance != null) blockedAppearance.alpha = interactable ? 1f : .58f;
    }

    public override void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left && !string.IsNullOrEmpty(blockedReason))
            ShowBlockedMessage?.Invoke(blockedReason);
        else base.OnPointerClick(eventData);
    }

    public override void OnSubmit(BaseEventData eventData)
    {
        if (!string.IsNullOrEmpty(blockedReason)) ShowBlockedMessage?.Invoke(blockedReason);
        else base.OnSubmit(eventData);
    }

}

internal sealed class RosterQuickScroll : ScrollRect
{
    public const float Step = 71;
    public float Offset => -content.anchoredPosition.x;
    public float Goal { get; private set; }
    private float from, elapsed;
    private int? dragPointer;

    private float Span => Mathf.Max(0, content.rect.width - viewport.rect.width);

    public float CenterOn(int index)
        => Snap(index * Step + (64 - viewport.rect.width) * .5f);

    private float Snap(float offset) => Mathf.Clamp(Mathf.Round(offset / Step) * Step, 0, Span);

    public void Restore(float offset, float goal)
    {
        SetOffset(Mathf.Clamp(offset, 0, Span));
        MoveTo(goal);
    }

    private void SetOffset(float offset)
        => SetContentAnchoredPosition(new Vector2(-offset, content.anchoredPosition.y));

    private void MoveTo(float offset)
    {
        StopMovement();
        from = Offset;
        Goal = Snap(offset);
        elapsed = 0;
    }

    public override void OnBeginDrag(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Left || dragPointer.HasValue) return;
        dragPointer = e.pointerId;
        StopMovement();
        base.OnBeginDrag(e);
    }

    public override void OnDrag(PointerEventData e)
    {
        if (dragPointer == e.pointerId) base.OnDrag(e);
    }

    public override void OnEndDrag(PointerEventData e)
    {
        if (dragPointer != e.pointerId) return;
        dragPointer = null;
        base.OnEndDrag(e);
        MoveTo(Offset);
    }

    public void CancelDrag()
    {
        if (!dragPointer.HasValue) return;
        OnEndDrag(new PointerEventData(EventSystem.current) { pointerId = dragPointer.Value });
    }

    public override void OnScroll(PointerEventData e)
    {
        if (dragPointer.HasValue || Span <= 0) return;
        float delta = Mathf.Abs(e.scrollDelta.x) > Mathf.Abs(e.scrollDelta.y) ? e.scrollDelta.x : -e.scrollDelta.y;
        if (delta == 0) return;
        MoveTo(Goal + Mathf.Sign(delta) * Step);
        e.Use();
    }

    protected override void LateUpdate()
    {
        if (!dragPointer.HasValue && content != null && Offset != Goal)
        {
            elapsed += Time.unscaledDeltaTime;
            SetOffset(Mathf.Lerp(from, Goal, RosterMotion.Ease(elapsed / .20f)));
        }
        base.LateUpdate();
    }

    protected override void OnDisable()
    {
        dragPointer = null;
        base.OnDisable();
    }
}

internal static class RosterMotion
{
    // CSS cubic-bezier(.22,.72,.24,1). Solve the time axis before evaluating distance.
    public static float Ease(float time)
    {
        if (time <= 0 || time >= 1) return Mathf.Clamp01(time);
        float low = 0, high = 1;
        for (int i = 0; i < 12; i++)
        {
            float t = (low + high) * .5f;
            if (Bezier(t, .22f, .24f) < time) low = t;
            else high = t;
        }
        return Bezier((low + high) * .5f, .72f, 1);
    }

    private static float Bezier(float t, float a, float b)
    {
        float u = 1 - t;
        return 3 * u * u * t * a + 3 * u * t * t * b + t * t * t;
    }
}

internal sealed class RosterCardCarousel : MonoBehaviour
{
    private sealed class Entry
    {
        public int Id, Index;
        public RectTransform Mount;
        public UICardView Card;
    }

    private UICardFactory factory;
    private Entry current, incoming;
    private int requestedId, requestedIndex;
    private bool sliding;
    private float elapsed;

    public void Bind(UICardFactory cardFactory) => factory = cardFactory;

    public void Select(int id, int index)
    {
        requestedId = id;
        requestedIndex = index;
        if (!sliding) Prepare();
    }

    private void Prepare()
    {
        if (factory == null || incoming?.Id == requestedId) return;
        Release(ref incoming);
        if (requestedId == 0 || current?.Id == requestedId) return;
        var mount = new GameObject("Sliding card", typeof(RectTransform)).GetComponent<RectTransform>();
        mount.SetParent(transform, false);
        mount.anchorMin = Vector2.zero;
        mount.anchorMax = Vector2.one;
        var card = factory.Request(mount, BattleSide.Ally, requestedId, showShadows: false);
        incoming = new Entry { Id = requestedId, Index = requestedIndex, Mount = mount, Card = card };
        Position(incoming, current == null ? 0 : Direction);
    }

    private int Direction => incoming.Index >= current.Index ? 1 : -1;

    private void LateUpdate()
    {
        if (incoming == null || !incoming.Card.Ready) return;
        if (current == null)
        {
            current = incoming;
            incoming = null;
            return;
        }
        sliding = true;
        elapsed += Time.unscaledDeltaTime;
        float progress = RosterMotion.Ease(elapsed / .26f);
        Position(current, -Direction * progress);
        Position(incoming, Direction * (1 - progress));
        if (elapsed < .26f) return;
        Release(ref current);
        current = incoming;
        incoming = null;
        sliding = false;
        elapsed = 0;
        Prepare();
    }

    private void Position(Entry entry, float offset)
    {
        var displacement = new Vector2(((RectTransform)transform).rect.width * offset, 0);
        entry.Mount.offsetMin = entry.Mount.offsetMax = displacement;
    }

    private void Release(ref Entry entry)
    {
        if (entry == null) return;
        if (factory != null) factory.Release(entry.Card);
        entry.Mount.gameObject.SetActive(false);
        Destroy(entry.Mount.gameObject);
        entry = null;
    }

    private void OnDisable()
    {
        Release(ref incoming);
        Release(ref current);
        sliding = false;
        elapsed = 0;
    }

    private void OnEnable() => Prepare();
}
