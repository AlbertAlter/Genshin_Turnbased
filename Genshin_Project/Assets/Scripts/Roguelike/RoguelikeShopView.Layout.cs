using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed partial class RoguelikeShopView
{
    [SerializeField] private RectTransform backdropMount;
    [SerializeField] private RectTransform headerMount;
    [SerializeField] private RectTransform catalogMount;
    [SerializeField] private RectTransform detailMount;
    [SerializeField] private UIAdaptiveFrame framePrefab;
    [SerializeField] private TMP_FontAsset font;
    [SerializeField, Min(1)] private int columns = 6;

    private static readonly Color32 DeepBrown = new(113, 103, 88, 255);
    private static readonly Color32 Dark = new(70, 58, 48, 255);
    private static readonly Color32 LightBrown = new(153, 143, 129, 255);
    private static readonly Color32 Paper = new(247, 239, 221, 255);
    private static readonly Color32 White = new(254, 254, 254, 255);
    private static readonly Color32 Muted = new(218, 203, 181, 255);
    private static readonly Color32 Gold = new(239, 207, 141, 255);
    private static readonly Color32 Ink = new(53, 45, 37, 255);

    public const string EmblemArtPath = "art_assets/Others/PaimonEmblem_LightBrown.png";
    private static readonly string[] RequiredTileArt =
        { UIItemListIcon.IconMaskPath, UIItemListIcon.ValueBarPath, UIItemListIcon.SelectionOutlinePath };

    private sealed class ShopButton
    {
        internal Button Control;
        internal Image Face;
        internal TMP_Text Label;
    }

    private readonly Dictionary<string, (RectTransform Root, UIItemListIcon Card)> tiles = new(StringComparer.Ordinal);
    private RoguelikeShopPage renderedPage;
    private float renderedTileSize;
    private bool renderedEnglish;
    private readonly ShopButton[] shopButtons = new ShopButton[3];
    private readonly ShopButton[] categoryButtons = new ShopButton[7];
    private readonly ShopButton[] weaponButtons = new ShopButton[2];
    private readonly UIHoldRepeatButton[] repeats = new UIHoldRepeatButton[2];
    private Image poster, emblem, balanceIcon, detailMask, detailBackdrop, detailIcon, unitCurrency, totalCurrency;
    private StreamingSpriteReference menuBackground;
    private TMP_Text title, balance, count, catalogTitle, status, detailName, detailStars;
    private TMP_Text priceLabel, stockLabel, quantityLabel, totalLabel, unitPrice, stock, total, statNames, statValues, description;
    private ShopButton language, minus, plus, maximum, exchangeButton;
    private Button exchange => exchangeButton.Control;
    private TMP_InputField search, quantityInput;
    private ScrollRect gridScroll, detailScroll;
    private RectTransform filters, detailContent, weaponModes, rows;
    private UIFloatingTip tip;
    private int balanceVersion;

    private void EnsureLayout()
    {
        if (isBuilt) return;
        if (backdropMount == null || headerMount == null || catalogMount == null || detailMount == null ||
            font == null || framePrefab == null)
            throw new InvalidOperationException("Shop layout, project frame, and project font must be assigned.");
        isBuilt = true;
        AddFrame(backdropMount, false, Dark, 1f);
        poster = Fill(Stretch(backdropMount, "Menu background", 20, 19, 20, 19), Color.white);
        poster.transform.SetAsFirstSibling();
        menuBackground = UIProjectMenuBackground.Apply(poster.rectTransform);
        BuildHeader();
        BuildCatalog();
        BuildDetail();
        tip = UIFloatingTip.Create((RectTransform)transform, font, data);
    }

    private void BuildHeader()
    {
        title = Label(Region(headerMount, "Shop title", 0, 0, 0, 1, 0, 2, 300, 0),
            42, TextAlignmentOptions.MidlineLeft, Paper);
        Fill(Region(headerMount, "Header rule", 0, 0, 1, 0, 0, 0, 0, 1), LightBrown);
        var tabs = Region(headerMount, "Shop tabs", .5f, .5f, .5f, .5f, -300, -29, 300, 29);
        var choices = Horizontal(tabs, 9);
        for (int i = 0; i < shopButtons.Length; i++)
        {
            var kind = (RoguelikeShopKind)i;
            var tab = ShopControl(Child(tabs, "Shop " + kind), () => SelectShop(kind), 23);
            shopButtons[i] = tab;
            var image = Fill(Region(tab.Face.rectTransform, "Currency", 0, .5f, 0, .5f, 12, -15, 42, 15), Color.clear);
            image.preserveAspect = true;
            tab.Label.rectTransform.offsetMin = new Vector2(43, 0);
            tab.Face.rectTransform.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            tab.Face.rectTransform.name = kind.ToString();
            tabCurrencyIcons[i] = image;
        }
        choices.childForceExpandWidth = true;

        language = ShopControl(Region(headerMount, "Language", 1, .5f, 1, .5f, -78, -24, 0, 24),
            () => { english = !english; Render(false); }, 19);
        balanceIcon = Fill(Region(headerMount, "Balance currency", 1, .5f, 1, .5f, -313, -18, -277, 18), Color.clear);
        balanceIcon.preserveAspect = true;
        balance = Label(Region(headerMount, "Balance", 1, .5f, 1, .5f, -269, -26, -95, 26),
            25, TextAlignmentOptions.MidlineLeft, Paper);
    }

    private readonly Image[] tabCurrencyIcons = new Image[3];

    private void BuildCatalog()
    {
        var toolbar = Region(catalogMount, "Catalog toolbar", 0, 1, 1, 1, 0, -56, 0, 0);
        catalogTitle = Label(Region(toolbar, "Catalog title", 0, 0, 0, 1, 0, 0, 143, 0),
            30, TextAlignmentOptions.MidlineLeft, Paper);
        count = Label(Region(toolbar, "Count", 0, 0, 0, 1, 143, 0, 258, 0),
            18, TextAlignmentOptions.MidlineLeft, Muted);

        filters = Region(toolbar, "Weapon filters", 0, 0, 1, 1, 265, 7, -225, -7);
        Horizontal(filters, 5).childForceExpandWidth = true;
        for (int i = 0; i < categoryButtons.Length; i++)
        {
            int categoryId = i - 1;
            var slot = Child(filters, "Category " + categoryId);
            slot.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            categoryButtons[i] = ShopControl(slot, () => SelectCategory(categoryId), 18);
        }

        search = Input(Region(toolbar, "Search", 1, 0, 1, 1, -210, 7, 0, -7), false);
        search.onValueChanged.AddListener(_ => Render(true));
        var grid = Region(catalogMount, "Goods", 0, 0, 1, 1, 0, 0, 0, -67);
        gridScroll = Scroll(grid, true);
        rows = gridScroll.content;
        status = Label(Stretch(grid, "Status", 18, 50, 18, 18), 25, TextAlignmentOptions.Center, Muted);
        status.enableWordWrapping = true;
    }

    private void BuildDetail()
    {
        AddFrame(detailMount, true, Dark, 1.75f);
        detailContent = Stretch(detailMount, "Detail content", 32, 21, 32, 27);
        emblem = Fill(Region(detailContent, "Paimon emblem", .5f, 1, .5f, 1, -130, -177, 130, -1), Color.clear);
        emblem.preserveAspect = true;
        var art = Region(detailContent, "Detail art", .5f, 1, .5f, 1, -68, -149, 68, -13);
        detailMask = Fill(art, Color.white);
        art.gameObject.AddComponent<Mask>().showMaskGraphic = false;
        detailBackdrop = Fill(Stretch(art, "Rarity"), DeepBrown);
        detailIcon = Fill(Stretch(art, "Icon"), Color.white);
        detailStars = Label(Region(detailContent, "Rarity stars", 0, 1, 1, 1, 0, -182, 0, -151),
            23, TextAlignmentOptions.Center, Gold);
        detailName = Label(Region(detailContent, "Name", 0, 1, 1, 1, 0, -244, 0, -181),
            30, TextAlignmentOptions.Center, Paper);
        detailName.enableWordWrapping = true;
        detailName.enableAutoSizing = true;
        detailName.fontSizeMin = 22;
        detailName.fontSizeMax = 30;

        weaponModes = Region(detailContent, "Weapon levels", 0, 1, 1, 1, 0, -286, 0, -247);
        Horizontal(weaponModes, 8).childForceExpandWidth = true;
        for (int i = 0; i < weaponButtons.Length; i++)
        {
            bool full = i == 1;
            var mode = Child(weaponModes, full ? "Full weapon" : "Base weapon");
            mode.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            weaponButtons[i] = ShopControl(mode, () =>
            {
                fullWeapon = full;
                if (selection?.IsWeapon == true) ShowWeapon(selection);
            }, 18);
        }

        var readout = Region(detailContent, "Details scroll", 0, 0, 1, 1, 0, 250, 0, -297);
        detailScroll = Scroll(readout, false);
        statNames = Label(Region(detailScroll.content, "Stat names", 0, 1, 1, 1, 0, -63, -105, 0),
            20, TextAlignmentOptions.TopLeft, Muted);
        statValues = Label(Region(detailScroll.content, "Stat values", 1, 1, 1, 1, -105, -63, 0, 0),
            20, TextAlignmentOptions.TopRight, Paper);
        description = Label(Region(detailScroll.content, "Description", 0, 1, 1, 1, 0, -116, 0, -65),
            20, TextAlignmentOptions.TopLeft, Paper);
        description.enableWordWrapping = true;
        description.richText = true;

        var checkout = Region(detailContent, "Checkout", 0, 0, 1, 0, 0, 0, 0, 242);
        Fill(Region(checkout, "Checkout rule", 0, 1, 1, 1, 0, -1, 0, 0), LightBrown);
        priceLabel = CheckoutLabel(checkout, "Unit price label", 8);
        stockLabel = CheckoutLabel(checkout, "Stock label", 46);
        quantityLabel = CheckoutLabel(checkout, "Quantity label", 84);
        totalLabel = CheckoutLabel(checkout, "Total label", 129);
        (unitCurrency, unitPrice) = Money(checkout, "Unit price", 8);
        unitPrice.richText = true;
        stock = CheckoutValue(checkout, "Remaining stock", 46);
        (totalCurrency, total) = Money(checkout, "Total cost", 129);

        var stepper = Region(checkout, "Quantity stepper", 1, 1, 1, 1, -253, -125, 0, -84);
        minus = ShopControl(Region(stepper, "Minus", 0, 0, 0, 1, 0, 0, 38, 0), null, 23);
        quantityInput = Input(Region(stepper, "Quantity", 0, 0, 0, 1, 44, 0, 127, 0), true);
        plus = ShopControl(Region(stepper, "Plus", 0, 0, 0, 1, 133, 0, 171, 0), null, 23);
        maximum = ShopControl(Region(stepper, "Maximum", 0, 0, 1, 1, 177, 0, 0, 0), () =>
        {
            if (selection == null) return;
            quantity = selection.Offer.MaxPurchasable;
            UpdateQuantity();
        }, 17);
        repeats[0] = AddRepeat(minus, -1);
        repeats[1] = AddRepeat(plus, 1);
        StepperGlyph(minus, false);
        StepperGlyph(plus, true);
        quantityInput.onEndEdit.AddListener(value =>
        {
            if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int amount)) quantity = amount;
            UpdateQuantity();
        });
        exchangeButton = ShopControl(Region(checkout, "Exchange", 0, 0, 1, 0, 0, 0, 0, 57), Purchase, 24);
    }

    private UIHoldRepeatButton AddRepeat(ShopButton control, int step)
    {
        var repeat = control.Control.gameObject.AddComponent<UIHoldRepeatButton>();
        repeat.Bind(control.Control, () => StepQuantity(step));
        return repeat;
    }

    private void CancelRepeat()
    {
        foreach (var repeat in repeats)
            if (repeat != null) repeat.Cancel();
    }

    private void LoadCommonArt()
    {
        SpriteFor(EmblemArtPath, sprite =>
        {
            if (sprite == null) return;
            emblem.sprite = sprite;
            emblem.color = new Color32(255, 255, 255, 58);
        });
        if (TileArtSettled) return;
        foreach (string path in RequiredTileArt)
            SpriteFor(path, sprite =>
            {
                if (path == UIItemListIcon.IconMaskPath && sprite != null) detailMask.sprite = sprite;
                if (TileArtSettled) Render(false);
            });
    }

    private bool TileArtSettled => Array.TrueForAll(RequiredTileArt, path => sprites.ContainsKey(path));

    private void UpdateHeader()
    {
        title.text = T("◆  商店", "◆  Shop");
        catalogTitle.text = T("兑换一览", "Items");
        Paint(language, false, T("EN", "中文"));
        ((TMP_Text)search.placeholder).text = T("搜索商品", "Search items");
        filters.gameObject.SetActive(shop != RoguelikeShopKind.Mora);
        string[] labels = english
            ? new[] { "All", "Sword", "Claymore", "Bow", "Polearm", "Catalyst", "Materials" }
            : new[] { "全部", "单手剑", "双手剑", "弓箭", "长枪", "法器", "材料" };
        for (int i = 0; i < categoryButtons.Length; i++) Paint(categoryButtons[i], category == i - 1, labels[i]);
        string[] tabs = english ? new[] { "Starglitter", "Stardust", "Mora" } : new[] { "星辉商店", "星尘商店", "摩拉商店" };
        for (int i = 0; i < shopButtons.Length; i++)
        {
            Paint(shopButtons[i], (int)shop == i, tabs[i]);
            Image icon = tabCurrencyIcons[i];
            SpriteFor(CurrencyPath((RoguelikeShopKind)i), sprite => BindImage(icon, sprite));
        }
        balance.text = page == null ? "—" : Number(page.Balance);
        int version = ++balanceVersion;
        SpriteFor(CurrencyPath(shop), sprite =>
        {
            if (version == balanceVersion) BindImage(balanceIcon, sprite);
        });
    }

    private void UpdateCatalog(List<Product> visible, string message, bool resetScroll)
    {
        float oldOffset = resetScroll ? 0 : rows.anchoredPosition.y;
        gridScroll.StopMovement();
        count.text = page == null ? "" : Number(visible.Count) + T(" 件商品", visible.Count == 1 ? " item" : " items");
        bool ready = TileArtSettled && Array.TrueForAll(RequiredTileArt, path => sprites[path] != null);
        status.text = message ?? (visible.Count == 0 ? T("没有符合条件的商品。", "No matching items.") :
            !ready ? (TileArtSettled
                ? T("商品图格资源未发布。", "Item grid artwork is missing.")
                : T("正在加载商品图格。", "Loading item grid.")) : "");
        const float gap = 16;
        int columnCount = Mathf.Max(1, columns);
        if (page != renderedPage) Canvas.ForceUpdateCanvases();
        float size = Mathf.Max(1, (gridScroll.viewport.rect.width - 12 - gap * (columnCount - 1)) / columnCount);
        if (page != renderedPage || renderedEnglish != english || !Mathf.Approximately(size, renderedTileSize))
        {
            // Prices, stock, labels and click commands belong to this shop snapshot.
            listVersion++;
            foreach (var tile in tiles.Values)
            {
                tile.Root.gameObject.SetActive(false);
                Destroy(tile.Root.gameObject);
            }
            tiles.Clear();
            renderedPage = page;
            renderedEnglish = english;
            renderedTileSize = size;
        }
        var shown = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(status.text))
            foreach (var product in visible) shown.Add(product.Offer.ProductId);
        foreach (var tile in tiles)
            if (tile.Value.Root.gameObject.activeSelf && !shown.Contains(tile.Key)) tile.Value.Root.gameObject.SetActive(false);
        if (shown.Count == 0)
        {
            rows.sizeDelta = Vector2.zero;
            rows.anchoredPosition = Vector2.zero;
            return;
        }

        float top = 6, rowHeight = 0;
        for (int index = 0; index < visible.Count; index++)
        {
            int column = index % columnCount;
            if (column == 0 && index > 0)
            {
                top += rowHeight + gap;
                rowHeight = 0;
            }
            var product = visible[index];
            if (!tiles.TryGetValue(product.Offer.ProductId, out var tile))
                tile = CreateTile(product, size, listVersion);
            tile.Root.anchoredPosition = new Vector2(6 + column * (size + gap), -top);
            if (!tile.Root.gameObject.activeSelf) tile.Root.gameObject.SetActive(true);
            tile.Card.SetSelected(selection == product);
            rowHeight = Mathf.Max(rowHeight, tile.Root.sizeDelta.y);
        }
        rows.sizeDelta = new Vector2(0, top + rowHeight + 6);
        float maxOffset = Mathf.Max(0, rows.sizeDelta.y - gridScroll.viewport.rect.height);
        rows.anchoredPosition = new Vector2(0, Mathf.Clamp(oldOffset, 0, maxOffset));
    }

    private (RectTransform Root, UIItemListIcon Card) CreateTile(Product product, float size, int version)
    {
        var offer = product.Offer;
        float topHeight = UIItemListIcon.GetCellSize(size, compact: true, valueStyle: UIItemListIcon.ValueStyle.ShopPrice).y;
        var root = Top(rows, "Offer " + offer.ProductId, 0, 0, size, topHeight);
        var card = UIItemListIcon.Create(root, size, font, sprites[RequiredTileArt[0]], sprites[RequiredTileArt[1]],
            sprites[RequiredTileArt[2]], compact: true, tileMask: sprites[RequiredTileArt[0]],
            valueStyle: UIItemListIcon.ValueStyle.ShopPrice);
        var cardRect = (RectTransform)card.transform;
        cardRect.anchorMin = cardRect.anchorMax = Vector2.up;
        cardRect.pivot = Vector2.up;
        cardRect.anchoredPosition = Vector2.zero;

        Sprite icon = null, backdrop = null, currency = null;
        var kind = product.IsWeapon ? UIItemListIcon.IconKind.Weapon : UIItemListIcon.IconKind.Material;
        void BindCard()
        {
            if (version != listVersion || card == null) return;
            card.Bind(icon, backdrop, Number(offer.UnitPrice), onClick: () => SelectProduct(product),
                selected: selection?.Offer.ProductId == offer.ProductId, kind: kind, rarity: product.Item.Rarity, currency: currency);
        }
        SpriteFor(IconPath(product), sprite => { icon = sprite; BindCard(); });
        SpriteFor(UIItemListIcon.GetRarityBackdropPath(product.Item.Rarity), sprite => { backdrop = sprite; BindCard(); });
        SpriteFor(CurrencyPath(shop), sprite => { currency = sprite; BindCard(); });
        BindCard();

        string badge = offer.Remaining == 0 ? T("已兑完", "Sold Out") :
            offer.BasePrice > offer.UnitPrice ? T("折扣", "Sale") :
            offer.Remaining is int left ? T("剩余 ", "Left ") + Number(left) : "";
        if (badge.Length != 0)
        {
            var chip = Top(root, "Stock badge", 5, 5, 82, 25);
            Fill(chip, new Color32(53, 45, 37, 230));
            var label = Label(Stretch(chip, "Label"), 15, TextAlignmentOptions.Center, Paper);
            label.text = badge;
            label.enableAutoSizing = true;
            label.fontSizeMin = 10;
            label.fontSizeMax = 15;
        }

        var caption = Top(root, "Name background", 0, topHeight - 1, size, 31);
        var nameFace = Fill(caption, Dark, true);
        var nameButton = caption.gameObject.AddComponent<Button>();
        nameButton.targetGraphic = nameFace;
        nameButton.transition = Selectable.Transition.None;
        nameButton.onClick.AddListener(() => SelectProduct(product));
        var name = Label(Stretch(caption, "Name", 6, 5, 6, 5), 17, TextAlignmentOptions.Center, Paper);
        name.enableWordWrapping = true;
        name.text = Name(product);
        float height = Mathf.Max(31, Mathf.Ceil(name.GetPreferredValues(name.text, size - 12, 0).y) + 10);
        caption.sizeDelta = new Vector2(size, height);
        root.sizeDelta = new Vector2(size, topHeight + height - 1);
        tiles.Add(offer.ProductId, (root, card));
        return (root, card);
    }

    private void SetDescription(string effect, string left, string right)
    {
        bool showStats = !string.IsNullOrEmpty(left);
        statNames.text = left;
        statValues.text = right;
        description.text = effect ?? "";
        float top = showStats ? 63 : 0;
        Canvas.ForceUpdateCanvases();
        float width = Mathf.Max(1, detailScroll.viewport.rect.width);
        float height = Mathf.Ceil(description.GetPreferredValues(description.text, width, 0).y) + 8;
        description.rectTransform.offsetMax = new Vector2(0, -top);
        description.rectTransform.offsetMin = new Vector2(0, -top - height);
        detailScroll.content.sizeDelta = new Vector2(0, top + height);
        detailScroll.StopMovement();
        detailScroll.content.anchoredPosition = Vector2.zero;
    }

    private void UpdateQuantity()
    {
        if (selection == null) return;
        var offer = selection.Offer;
        int max = offer.MaxPurchasable;
        quantity = max == 0 ? 0 : Mathf.Clamp(quantity, 1, max);
        priceLabel.text = T("单价", "Unit Price");
        stockLabel.text = T("剩余可兑换", "Stock Left");
        quantityLabel.text = T("兑换数量", "Quantity");
        totalLabel.text = T("合计", "Total");
        unitPrice.text = offer.BasePrice > offer.UnitPrice
            ? Number(offer.UnitPrice) + "  <s>" + Number(offer.BasePrice) + "</s>" : Number(offer.UnitPrice);
        stock.text = offer.Remaining.HasValue ? Number(offer.Remaining.Value) : T("不限", "Unlimited");
        total.text = Number((long)offer.UnitPrice * quantity);
        quantityInput.SetTextWithoutNotify(quantity.ToString(CultureInfo.InvariantCulture));
        quantityInput.interactable = max > 0 && !submitting;
        Paint(minus, false, "");
        Paint(plus, false, "");
        Paint(maximum, false, T("最大", "Max"));
        minus.Control.interactable = quantity > 1 && !submitting;
        plus.Control.interactable = quantity < max && !submitting;
        maximum.Control.interactable = quantity < max && !submitting;
        exchange.interactable = max > 0 && !submitting;
        string action = offer.Remaining == 0 ? T("已兑完", "Sold Out") :
            page?.PurchasesAllowed != true ? T("当前阶段不可兑换", "Unavailable at this stage") :
            offer.InventorySpace == 0 ? ExchangeCapacityTip(selection.IsWeapon) :
            max == 0 ? (offer.UnitPrice > page.Balance ? T("货币不足", "Insufficient currency") :
                T("无法兑换", "Cannot exchange")) : T("确认兑换", "Confirm Exchange");
        Paint(exchangeButton, exchange.interactable, action);
    }

    private void SetCurrency(Image icon, int version)
    {
        BindImage(icon, null);
        SpriteFor(CurrencyPath(shop), sprite =>
        {
            if (version == detailVersion) BindImage(icon, sprite);
        });
    }

    private static string CurrencyPath(RoguelikeShopKind kind)
        => $"art_assets/ItemIcon/{RoguelikeShopCatalog.CurrencyItemId(kind)}.png";

    private static void BindImage(Image image, Sprite sprite)
    {
        if (image == null) return;
        image.sprite = sprite;
        image.color = sprite == null ? Color.clear : Color.white;
    }

    private UIAdaptiveFrame AddFrame(RectTransform parent, bool fill, Color color, float scale)
    {
        var frame = Instantiate(framePrefab, parent, false);
        var rect = (RectTransform)frame.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        frame.showFill = fill;
        frame.fillColor = color;
        frame.pixelsPerUnitMultiplier = scale;
        return frame;
    }

    private static HorizontalLayoutGroup Horizontal(RectTransform rect, float spacing)
    {
        var layout = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = spacing;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = layout.childForceExpandHeight = true;
        return layout;
    }

    private ScrollRect Scroll(RectTransform host, bool withScrollbar)
    {
        var viewport = Stretch(host, "Viewport", 0, 0, withScrollbar ? 12 : 0, 0);
        Fill(viewport, Color.clear, true);
        viewport.gameObject.AddComponent<RectMask2D>();
        var content = Region(viewport, "Content", 0, 1, 1, 1, 0, 0, 0, 0);
        content.pivot = new Vector2(.5f, 1);
        var scroll = host.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 45;
        if (withScrollbar)
        {
            var track = Region(host, "Scroll bar", 1, 0, 1, 1, -8, 0, 0, 0);
            Fill(track, new Color32(89, 77, 66, 255), true);
            var slide = Stretch(track, "Sliding Area", 1, 2, 1, 2);
            var handle = Fill(Stretch(slide, "Handle"), LightBrown, true);
            var bar = track.gameObject.AddComponent<Scrollbar>();
            bar.direction = Scrollbar.Direction.BottomToTop;
            bar.targetGraphic = handle;
            bar.handleRect = handle.rectTransform;
            scroll.verticalScrollbar = bar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        }
        return scroll;
    }

    private TMP_InputField Input(RectTransform rect, bool number)
    {
        var box = Fill(rect, Dark, true);
        var viewport = Stretch(rect, "Text viewport", 8, 1, 8, 1);
        viewport.gameObject.AddComponent<RectMask2D>();
        var label = Label(Stretch(viewport, "Text"), 19,
            number ? TextAlignmentOptions.Center : TextAlignmentOptions.MidlineLeft, Paper);
        var placeholder = Label(Stretch(viewport, "Placeholder"), 19, label.alignment, Muted);
        var input = rect.gameObject.AddComponent<TMP_InputField>();
        input.targetGraphic = box;
        input.textViewport = viewport;
        input.textComponent = label;
        input.placeholder = placeholder;
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.contentType = number ? TMP_InputField.ContentType.IntegerNumber : TMP_InputField.ContentType.Standard;
        input.characterLimit = number ? 10 : 60;
        return input;
    }

    private ShopButton ShopControl(RectTransform rect, UnityAction action, float size)
    {
        var face = Fill(rect, DeepBrown, true);
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = face;
        button.transition = Selectable.Transition.ColorTint;
        var colors = button.colors;
        colors.highlightedColor = new Color32(240, 240, 240, 255);
        colors.pressedColor = new Color32(208, 208, 208, 255);
        colors.disabledColor = new Color32(156, 156, 156, 255);
        button.colors = colors;
        if (action != null) button.onClick.AddListener(action);
        var label = Label(Stretch(rect, "Label", 3, 0, 3, 0), size, TextAlignmentOptions.Center, Paper);
        return new ShopButton { Control = button, Face = face, Label = label };
    }

    private static void Paint(ShopButton button, bool selected, string value)
    {
        button.Face.color = selected ? Paper : DeepBrown;
        button.Label.color = selected ? Ink : White;
        button.Label.text = value;
    }

    private TMP_Text CheckoutLabel(RectTransform parent, string name, float top)
        => Label(Region(parent, name, 0, 1, 0, 1, 0, -top - 34, 164, -top), 20, TextAlignmentOptions.MidlineLeft, Muted);

    private TMP_Text CheckoutValue(RectTransform parent, string name, float top)
        => Label(Region(parent, name, 1, 1, 1, 1, -190, -top - 34, 0, -top), 21, TextAlignmentOptions.MidlineRight, Paper);

    private (Image Icon, TMP_Text Value) Money(RectTransform parent, string name, float top)
    {
        var rect = Region(parent, name, 1, 1, 1, 1, -220, -top - 34, 0, -top);
        var row = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.childAlignment = TextAnchor.MiddleRight;
        row.childForceExpandWidth = row.childForceExpandHeight = false;
        row.spacing = 6;
        var icon = Fill(Child(rect, "Currency"), Color.clear);
        icon.preserveAspect = true;
        var size = icon.gameObject.AddComponent<LayoutElement>();
        size.minWidth = size.preferredWidth = size.minHeight = size.preferredHeight = 24;
        return (icon, Label(Child(rect, "Amount"), 21, TextAlignmentOptions.MidlineRight, Paper));
    }

    private static void StepperGlyph(ShopButton control, bool plus)
    {
        control.Label.gameObject.SetActive(false);
        var glyph = Stretch(control.Face.rectTransform, "Glyph", 8, 8, 8, 8).gameObject.AddComponent<UIStrokeIcon>();
        glyph.color = Paper;
        var horizontal = new[] { new Vector2(5, 12), new Vector2(19, 12) };
        glyph.SetPaths(plus ? new[] { horizontal, new[] { new Vector2(12, 5), new Vector2(12, 19) } }
            : new[] { horizontal }, 2.4f);
    }

    private TMP_Text Label(RectTransform parent, float size, TextAlignmentOptions alignment, Color color)
    {
        var label = parent.gameObject.AddComponent<TextMeshProUGUI>();
        label.font = font;
        label.fontSize = size;
        label.fontStyle = FontStyles.Normal;
        label.alignment = alignment;
        label.color = color;
        label.richText = false;
        label.enableWordWrapping = false;
        label.raycastTarget = false;
        return label;
    }

    private static Image Fill(RectTransform parent, Color color, bool hit = false)
    {
        var image = parent.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = hit;
        return image;
    }

    private static RectTransform Child(RectTransform parent, string name)
    {
        var child = new GameObject(name, typeof(RectTransform));
        child.layer = parent.gameObject.layer;
        var rect = (RectTransform)child.transform;
        rect.SetParent(parent, false);
        return rect;
    }

    private static RectTransform Region(RectTransform parent, string name, float left, float bottom, float right, float top,
        float minX, float minY, float maxX, float maxY)
    {
        var rect = Child(parent, name);
        rect.anchorMin = new Vector2(left, bottom);
        rect.anchorMax = new Vector2(right, top);
        rect.offsetMin = new Vector2(minX, minY);
        rect.offsetMax = new Vector2(maxX, maxY);
        return rect;
    }

    private static RectTransform Stretch(RectTransform parent, string name, float left = 0, float top = 0,
        float right = 0, float bottom = 0)
        => Region(parent, name, 0, 0, 1, 1, left, bottom, -right, -top);

    private static RectTransform Top(RectTransform parent, string name, float x, float y, float width, float height)
    {
        var rect = Child(parent, name);
        rect.anchorMin = rect.anchorMax = Vector2.up;
        rect.pivot = Vector2.up;
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
        return rect;
    }
}

internal sealed class UIHoldRepeatButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    private Button button;
    private Func<bool> step;
    private int? pointer;
    private float nextRepeat;
    private bool repeated;
    private bool clearAfterDispatch;

    internal void Bind(Button control, Func<bool> action)
    {
        button = control;
        step = action;
        button.onClick.AddListener(() => { if (!repeated) step(); });
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (pointer.HasValue || eventData.button != PointerEventData.InputButton.Left || !button.IsInteractable()) return;
        pointer = eventData.pointerId;
        repeated = false;
        clearAfterDispatch = false;
        nextRepeat = Time.unscaledTime + .35f;
    }

    public void OnPointerUp(PointerEventData eventData) => Release(eventData.pointerId);
    public void OnPointerExit(PointerEventData eventData) => Release(eventData.pointerId);

    private void Release(int id)
    {
        if (pointer != id) return;
        pointer = null;
        clearAfterDispatch = true;
    }

    private void Update()
    {
        if (!pointer.HasValue || !button.IsInteractable() || Time.unscaledTime < nextRepeat) return;
        repeated = true;
        if (step()) nextRepeat = Time.unscaledTime + .085f;
        else nextRepeat = float.PositiveInfinity;
    }

    private void LateUpdate()
    {
        if (!clearAfterDispatch) return;
        repeated = clearAfterDispatch = false;
    }

    internal void Cancel()
    {
        pointer = null;
        if (repeated) clearAfterDispatch = true;
    }

    private void OnDisable()
    {
        Cancel();
        repeated = clearAfterDispatch = false;
    }
}
