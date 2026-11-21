using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>通用列表图格。调用方提供已加载的共享 Sprite、白条文字和点击命令。</summary>
[DisallowMultipleComponent]
public sealed class UIItemListIcon : MonoBehaviour
{
    public enum IconKind { Generic, Character, Weapon, Artifact, Material }
    public enum ValueStyle { Standard, ShopPrice }

    public const string IconMaskPath = "art_assets/UI/ItemListPage/IconMask.png";
    public const string ValueBarPath = "art_assets/UI/ItemListPage/ValueBar.png";
    public const string SelectionOutlinePath = "art_assets/UI/ItemListPage/SelectionOutline.png";
    private const float IconUnits = 256f;
    private const float BarUnits = 72f;
    private const float OverlapUnits = 18f;
    private const float TileUnits = IconUnits + BarUnits;
    private const float CompactBarHeight = 26f;
    private const float ShopReferenceSize = 180f;
    private const float WeaponScaleLimit = 1.05f;
    private const float WeaponVisibleSize = .95f;
    private const float RarityStarSize = .16f;
    private const float BadgeInset = .05f;
    private const float BadgeSize = .25f;
    private static readonly Color32 EmptyBackdrop = new Color32(113, 103, 88, 255);
    private static readonly Color32 TextColor = new Color32(50, 42, 35, 255);
    private static readonly Color32 SelectedBadgeColor = new Color32(54, 118, 75, 255);
    private static readonly Color32 RankBadgeColor = new Color32(239, 209, 122, 255);
    private static readonly ConditionalWeakTable<Sprite, WeaponPlacement> WeaponPlacements =
        new ConditionalWeakTable<Sprite, WeaponPlacement>();

    private sealed class WeaponPlacement
    {
        public readonly float Size;
        public readonly Vector2 Center;

        public WeaponPlacement(float size, Vector2 center)
        {
            Size = size;
            Center = center;
        }
    }

    private RectTransform visual;
    private Image hitArea, backdrop, itemIcon, ownerIcon, selection;
    private UIItemListBadgeGraphic selectedBadge, refinementBadge;
    private UIItemListRarityGraphic rarityStars;
    private TMP_Text valueLabel, refinementLabel;
    private Image valueCurrency;
    private Button button;
    private Action clicked;
    private bool markIndividualSelection, ownerAtTop;

    public static Vector2 GetCellSize(float iconSize, bool compact = false, ValueStyle valueStyle = ValueStyle.Standard)
        => new Vector2(iconSize, iconSize + (valueStyle == ValueStyle.ShopPrice ? iconSize * 37f / ShopReferenceSize :
            compact ? CompactBarHeight : iconSize * BarUnits / IconUnits));

    public static string GetRarityBackdropPath(int rarity)
    {
        switch (rarity)
        {
            case 1: return "art_assets/RarityBackground/background_gray.png";
            case 2: return "art_assets/RarityBackground/background_green.png";
            case 3: return "art_assets/RarityBackground/background_blue.png";
            case 4: return "art_assets/RarityBackground/background_purple.png";
            case 5: return "art_assets/RarityBackground/background_gold.png";
            default: return null;
        }
    }

    public static UIItemListIcon Create(RectTransform parent, float iconSize, TMP_FontAsset font,
        Sprite iconMask, Sprite valueBar, Sprite selectionOutline, bool compact = false, Sprite tileMask = null,
        ValueStyle valueStyle = ValueStyle.Standard)
    {
        if (parent == null) throw new ArgumentNullException(nameof(parent));
        if (font == null) throw new ArgumentNullException(nameof(font));
        if (iconMask == null) throw new ArgumentNullException(nameof(iconMask));
        if (valueBar == null) throw new ArgumentNullException(nameof(valueBar));
        if (selectionOutline == null) throw new ArgumentNullException(nameof(selectionOutline));
        if (iconSize <= 0f || float.IsNaN(iconSize) || float.IsInfinity(iconSize))
            throw new ArgumentOutOfRangeException(nameof(iconSize));

        var tile = new GameObject("ItemListIcon", typeof(RectTransform), typeof(Image), typeof(Button));
        var rect = (RectTransform)tile.transform;
        rect.SetParent(parent, false);
        rect.sizeDelta = GetCellSize(iconSize, compact, valueStyle);
        var view = tile.AddComponent<UIItemListIcon>();
        view.Build(iconSize, font, iconMask, valueBar, selectionOutline, compact, tileMask, valueStyle);
        view.Bind(null, null, string.Empty);
        return view;
    }

    private void Build(float iconSize, TMP_FontAsset font, Sprite iconMask, Sprite valueBar, Sprite selectionOutline,
        bool compact, Sprite tileMask, ValueStyle valueStyle)
    {
        hitArea = GetComponent<Image>();
        hitArea.color = Color.clear;
        button = GetComponent<Button>();
        button.targetGraphic = hitArea;
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(HandleClick);

        visual = CreateLayer(transform, "Visual", Vector2.zero, Vector2.one);
        bool shopPrice = valueStyle == ValueStyle.ShopPrice;
        float shopScale = iconSize / ShopReferenceSize;
        float tileHeight = GetCellSize(iconSize, compact, valueStyle).y;
        var fitter = visual.gameObject.AddComponent<AspectRatioFitter>();
        fitter.aspectRatio = iconSize / tileHeight;
        fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;

        bool roundWholeTile = compact && tileMask != null;
        float barTop = (tileHeight - iconSize) / tileHeight;
        if (roundWholeTile)
        {
            var mask = visual.gameObject.AddComponent<Image>();
            mask.sprite = tileMask;
            mask.raycastTarget = false;
            visual.gameObject.AddComponent<Mask>().showMaskGraphic = false;
        }

        float barEnd = roundWholeTile ? barTop : barTop + OverlapUnits / TileUnits;
        var bar = CreateImage(visual, "ValueBar", Vector2.zero, new Vector2(1f, barEnd));
        if (roundWholeTile) bar.color = new Color32(254, 254, 254, 255);
        else bar.sprite = valueBar;

        var iconArea = CreateLayer(visual, "IconArea", new Vector2(0f, barTop), Vector2.one);
        if (!roundWholeTile)
        {
            var mask = iconArea.gameObject.AddComponent<Image>();
            mask.sprite = iconMask;
            mask.raycastTarget = false;
            iconArea.gameObject.AddComponent<Mask>().showMaskGraphic = false;
        }
        backdrop = CreateImage(iconArea, "Rarity", Vector2.zero, Vector2.one);
        itemIcon = CreateImage(iconArea, "Icon", Vector2.zero, Vector2.one);
        ownerIcon = CreateImage(iconArea, "EquippedBy", new Vector2(.69f, .03f), new Vector2(.97f, .31f));
        ownerIcon.preserveAspect = true;

        selectedBadge = CreateBadge(iconArea, "SelectedBadge", BadgeInset, SelectedBadgeColor, true);
        refinementBadge = CreateBadge(iconArea, "RefinementBadge", 1f - BadgeInset - BadgeSize, RankBadgeColor, false);
        var rank = CreateLayer(refinementBadge.transform, "Number", Vector2.zero, Vector2.one);
        refinementLabel = rank.gameObject.AddComponent<TextMeshProUGUI>();
        refinementLabel.font = font;
        refinementLabel.fontSize = iconSize * .16f;
        refinementLabel.fontStyle = FontStyles.Bold;
        refinementLabel.alignment = TextAlignmentOptions.Center;
        refinementLabel.color = TextColor;
        refinementLabel.raycastTarget = false;

        var valueArea = CreateLayer(visual, "Value contents", new Vector2(.04f, 0f), new Vector2(.96f, barTop));
        if (shopPrice)
        {
            var row = valueArea.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.childAlignment = TextAnchor.MiddleCenter;
            row.childForceExpandWidth = row.childForceExpandHeight = false;
            row.spacing = 5f * shopScale;
            valueCurrency = CreateImage(valueArea, "Currency", Vector2.zero, Vector2.one);
            valueCurrency.preserveAspect = true;
            var size = valueCurrency.gameObject.AddComponent<LayoutElement>();
            size.minWidth = size.preferredWidth = size.minHeight = size.preferredHeight = 24f * shopScale;
        }
        var label = CreateLayer(valueArea, "Value", Vector2.zero, Vector2.one);
        valueLabel = label.gameObject.AddComponent<TextMeshProUGUI>();
        valueLabel.font = font;
        valueLabel.fontSize = shopPrice ? 20f * shopScale : compact ? 19f : iconSize * .15f;
        valueLabel.color = TextColor;
        valueLabel.alignment = TextAlignmentOptions.Center;
        valueLabel.enableWordWrapping = false;
        valueLabel.overflowMode = TextOverflowModes.Ellipsis;
        valueLabel.richText = false;
        valueLabel.raycastTarget = false;

        // Keep the star strip over the seam, outside the square icon's mask.
        float starHeight = (shopPrice ? 17f * shopScale : iconSize * RarityStarSize) / tileHeight;
        rarityStars = CreateLayer(visual, "RarityStars", new Vector2(0f, barTop - starHeight * (shopPrice ? .25f : .45f)),
            new Vector2(1f, barTop + starHeight * (shopPrice ? .75f : .55f))).gameObject.AddComponent<UIItemListRarityGraphic>();
        rarityStars.raycastTarget = false;

        selection = CreateImage(visual, "Selected", Vector2.zero, Vector2.one);
        selection.sprite = selectionOutline;
    }

    internal void ResizeCompact(float size)
    {
        float height = GetCellSize(size, true).y;
        float barTop = CompactBarHeight / height;
        float stars = size * RarityStarSize / height;
        visual.GetComponent<AspectRatioFitter>().aspectRatio = size / height;
        var bar = (RectTransform)visual.Find("ValueBar");
        var area = itemIcon.rectTransform.parent as RectTransform;
        bool masked = visual.GetComponent<Mask>() != null;
        bar.anchorMax = new Vector2(1, masked ? barTop : barTop + OverlapUnits / TileUnits);
        area.anchorMin = new Vector2(0, barTop);
        ((RectTransform)valueLabel.transform.parent).anchorMax = new Vector2(.96f, barTop);
        rarityStars.rectTransform.anchorMin = new Vector2(0, barTop - stars * .45f);
        rarityStars.rectTransform.anchorMax = new Vector2(1, barTop + stars * .55f);
        refinementLabel.fontSize = size * .16f;
    }

    /// <summary>整格重新绑定。value 由页面传入；rarity 为 0 时不显示星级。</summary>
    public void Bind(Sprite icon, Sprite rarityBackdrop, string value, Sprite equippedBy = null,
        Action onClick = null, bool selected = false, IconKind kind = IconKind.Generic,
        bool markIndividualSelection = false, int refinementRank = 0, int rarity = 0, bool ownerAtTop = false,
        Sprite currency = null)
    {
        backdrop.sprite = rarityBackdrop;
        backdrop.color = rarityBackdrop == null ? (Color)EmptyBackdrop : Color.white;
        ApplySquareIcon(itemIcon, icon, kind);
        ownerIcon.sprite = equippedBy;
        ownerIcon.enabled = equippedBy != null;
        float left = BadgeInset, right = 1f - BadgeInset - BadgeSize;
        PlaceBadge(refinementBadge.rectTransform, ownerAtTop ? left : right);
        PlaceBadge(selectedBadge.rectTransform, ownerAtTop ? right : left);
        valueLabel.text = value ?? string.Empty;
        if (valueCurrency != null)
        {
            valueCurrency.sprite = currency;
            valueCurrency.gameObject.SetActive(currency != null);
        }
        rarityStars.SetRarity(rarity);
        this.markIndividualSelection = markIndividualSelection;
        this.ownerAtTop = ownerAtTop;
        bool showRank = kind == IconKind.Weapon && refinementRank > 0;
        refinementLabel.text = showRank ? refinementRank.ToString(CultureInfo.InvariantCulture) : string.Empty;
        refinementBadge.gameObject.SetActive(showRank);
        clicked = onClick;
        SetSelected(selected);
        UpdateInput();
    }

    /// <summary>为正方形区域内的子图标应用项目比例和等比居中。</summary>
    public static void ApplySquareIcon(Image image, Sprite icon, IconKind kind)
    {
        if (image == null) throw new ArgumentNullException(nameof(image));

        float size = kind switch
        {
            IconKind.Character => 1f,
            IconKind.Weapon => WeaponVisibleSize,
            IconKind.Artifact => .95f,
            IconKind.Material => .84f,
            _ => .72f
        };
        var center = new Vector2(.5f, .5f);
        if (kind == IconKind.Weapon && icon != null)
        {
            WeaponPlacement placement = WeaponPlacements.GetValue(icon, MeasureWeapon);
            size = placement.Size;
            center = placement.Center;
        }

        var rect = image.rectTransform;
        var half = Vector2.one * (size * .5f);
        rect.pivot = new Vector2(.5f, .5f);
        rect.anchorMin = center - half;
        rect.anchorMax = center + half;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        image.preserveAspect = true;
        image.sprite = icon;
        image.enabled = icon != null;
    }

    private static WeaponPlacement MeasureWeapon(Sprite icon)
    {
        var center = new Vector2(.5f, .5f);
        var fallback = new WeaponPlacement(WeaponVisibleSize, center);
        Texture2D texture = icon.texture;
        Rect source = icon.rect;
        // Published weapon icons use a dedicated texture. Atlas sprites use the safe inset.
        if (texture == null || !texture.isReadable || icon.packed ||
            source != new Rect(0, 0, texture.width, texture.height))
            return fallback;

        Color32[] pixels;
        try { pixels = texture.GetPixels32(); }
        catch (UnityException) { return fallback; }
        int width = texture.width, height = texture.height;
        int left = width, bottom = height, right = -1, top = -1;
        for (int y = 0; y < height; y++)
        {
            int row = y * width, first = 0;
            while (first < width && pixels[row + first].a < 16) first++;
            if (first == width) continue;
            int last = width - 1;
            while (last > first && pixels[row + last].a < 16) last--;
            if (first < left) left = first;
            if (last > right) right = last;
            if (y < bottom) bottom = y;
            top = y;
        }
        if (right < 0) return fallback;

        float spriteSize = Mathf.Max(width, height);
        float visibleSize = Mathf.Max(right - left + 1, top - bottom + 1);
        float size = Mathf.Min(WeaponScaleLimit, WeaponVisibleSize * spriteSize / visibleSize);
        var visibleCenter = new Vector2((left + right + 1) * .5f, (bottom + top + 1) * .5f);
        var offset = (visibleCenter - source.center) * (size / spriteSize);
        return new WeaponPlacement(size, center - offset);
    }

    public void SetSelected(bool selected)
    {
        selection.enabled = selected;
        selectedBadge.enabled = selected && markIndividualSelection;
        float avatarBottom = ownerAtTop ? (selectedBadge.enabled ? .41f : .69f) : .03f;
        var ownerRect = ownerIcon.rectTransform;
        ownerRect.anchorMin = new Vector2(.69f, avatarBottom);
        ownerRect.anchorMax = new Vector2(.97f, avatarBottom + .28f);
    }

    public void FitValueText(float size, float minimum)
    {
        valueLabel.fontSizeMax = size;
        valueLabel.fontSizeMin = minimum;
        valueLabel.enableAutoSizing = true;
    }

    private void HandleClick()
    {
        if (isActiveAndEnabled) clicked?.Invoke();
    }

    private void OnEnable()
    {
        if (visual != null) visual.gameObject.SetActive(true);
        UpdateInput();
    }

    private void OnDisable()
    {
        if (visual != null) visual.gameObject.SetActive(false);
        UpdateInput();
    }

    private void UpdateInput()
    {
        if (button == null) return;
        bool interactive = isActiveAndEnabled && clicked != null;
        button.interactable = interactive;
        hitArea.raycastTarget = interactive;
    }

    private void OnDestroy()
    {
        if (button != null) button.onClick.RemoveListener(HandleClick);
        clicked = null;
    }

    private static RectTransform CreateLayer(Transform parent, string name, Vector2 min, Vector2 max)
    {
        var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        rect.SetParent(parent, false);
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return rect;
    }

    private static Image CreateImage(Transform parent, string name, Vector2 min, Vector2 max)
    {
        var image = CreateLayer(parent, name, min, max).gameObject.AddComponent<Image>();
        image.raycastTarget = false;
        return image;
    }

    private static UIItemListBadgeGraphic CreateBadge(Transform parent, string name, float left, Color fill, bool check)
    {
        var min = new Vector2(left, 1f - BadgeInset - BadgeSize);
        var max = min + Vector2.one * BadgeSize;
        var badge = CreateLayer(parent, name, min, max).gameObject.AddComponent<UIItemListBadgeGraphic>();
        badge.Configure(fill, check);
        return badge;
    }

    private static void PlaceBadge(RectTransform rect, float left)
    {
        rect.anchorMin = new Vector2(left, 1f - BadgeInset - BadgeSize);
        rect.anchorMax = rect.anchorMin + Vector2.one * BadgeSize;
    }
}

/// <summary>星级在同一层绘制，不依赖字体或稀有度底图。</summary>
[RequireComponent(typeof(CanvasRenderer))]
internal sealed class UIItemListRarityGraphic : MaskableGraphic
{
    private static readonly Color32 Gold = new Color32(255, 212, 90, 255);
    private static readonly Color32 Edge = new Color32(217, 147, 40, 255);
    private static readonly Vector2[] Points =
    {
        new Vector2(0f, .425f), new Vector2(.131f, .160f), new Vector2(.423f, .118f),
        new Vector2(.212f, -.089f), new Vector2(.262f, -.381f), new Vector2(0f, -.243f),
        new Vector2(-.262f, -.381f), new Vector2(-.212f, -.089f), new Vector2(-.423f, .118f),
        new Vector2(-.131f, .160f)
    };

    private int count;

    public void SetRarity(int rarity)
    {
        count = Mathf.Clamp(rarity, 0, 5);
        enabled = count > 0;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect rect = GetPixelAdjustedRect();
        if (count == 0 || rect.width <= 0f || rect.height <= 0f) return;

        float size = Mathf.Min(rect.height, rect.width / count);
        float firstX = rect.center.x - (count - 1) * size * .5f;
        for (int i = 0; i < count; i++)
        {
            var center = new Vector2(firstX + i * size, rect.center.y);
            AddStar(mesh, center, size, Edge);
            AddStar(mesh, center, size * .89f, Gold);
        }
    }

    private static void AddStar(VertexHelper mesh, Vector2 center, float size, Color32 tint)
    {
        int start = mesh.currentVertCount;
        mesh.AddVert(center, tint, Vector2.zero);
        foreach (var point in Points) mesh.AddVert(center + point * size, tint, Vector2.zero);
        for (int i = 0; i < Points.Length; i++)
            mesh.AddTriangle(start, start + i + 1, start + (i + 1) % Points.Length + 1);
    }
}

/// <summary>图格角标使用几何图形绘制，勾选符号不依赖字体。</summary>
[RequireComponent(typeof(CanvasRenderer))]
internal sealed class UIItemListBadgeGraphic : MaskableGraphic
{
    private bool showCheck;

    public void Configure(Color fill, bool check)
    {
        showCheck = check;
        color = fill;
        raycastTarget = false;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect rect = GetPixelAdjustedRect();
        if (rect.width <= 0f || rect.height <= 0f) return;
        if (!showCheck)
        {
            const int segments = 32;
            mesh.AddVert(rect.center, color, Vector2.zero);
            for (int i = 0; i < segments; i++)
            {
                float angle = Mathf.PI * 2f * i / segments;
                var edge = rect.center + new Vector2(Mathf.Cos(angle) * rect.width, Mathf.Sin(angle) * rect.height) * .5f;
                mesh.AddVert(edge, color, Vector2.zero);
                mesh.AddTriangle(0, i + 1, (i + 1) % segments + 1);
            }
            return;
        }

        AddQuad(mesh, new Vector2(rect.xMin, rect.yMin), new Vector2(rect.xMin, rect.yMax),
            new Vector2(rect.xMax, rect.yMax), new Vector2(rect.xMax, rect.yMin), color);
        // Coordinates use a 24-unit box measured from the top left.
        Vector2 Point(float x, float y) => new Vector2(rect.xMin + x / 24f * rect.width, rect.yMax - y / 24f * rect.height);
        Vector2 inner = Point(10, 13), outer = Point(10, 17);
        AddQuad(mesh, Point(5, 12), Point(7, 10), inner, outer, Color.white);
        AddQuad(mesh, inner, Point(17, 6), Point(19, 8), outer, Color.white);
    }

    private static void AddQuad(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color32 tint)
    {
        int start = mesh.currentVertCount;
        mesh.AddVert(a, tint, Vector2.zero);
        mesh.AddVert(b, tint, Vector2.zero);
        mesh.AddVert(c, tint, Vector2.zero);
        mesh.AddVert(d, tint, Vector2.zero);
        mesh.AddTriangle(start, start + 1, start + 2);
        mesh.AddTriangle(start, start + 2, start + 3);
    }
}
