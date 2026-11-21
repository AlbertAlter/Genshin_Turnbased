using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Reusable shop page. All availability and exchange commits come from the current run's shop service.</summary>
[DisallowMultipleComponent]
public sealed partial class RoguelikeShopView : MonoBehaviour
{
    private sealed class Product
    {
        internal RoguelikeShopOffer Offer;
        internal RoguelikeItemDefinition Item;
        internal int WeaponType;
        internal string ChineseName;
        internal string EnglishName;
        internal bool IsWeapon => Item.ItemType == RoguelikeItemType.Weapon;
        internal string Group => IsWeapon ? "Weapon:" + WeaponType : Item.Category;
    }

    private readonly Dictionary<string, Sprite> sprites = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<Action<Sprite>>> pending = new(StringComparer.Ordinal);
    private IRoguelikeGameService game;
    private IRoguelikeItemCatalog items;
    private DataManager data;
    private RosterTextCatalog weaponText;
    private RoguelikeShopPage page;
    public bool Ready => isBuilt && pending.Count == 0 && (menuBackground == null || !menuBackground.IsLoading);
    public bool TryNavigateBack()
    {
        CancelRepeat();
        return submitting;
    }
    public void SetEnglish(bool value)
    {
        if (english == value) return;
        english = value;
        if (isBuilt && data != null) Render(false);
    }
    private RoguelikeShopKind shop = RoguelikeShopKind.Mora;
    private List<Product> products = new();
    private Product selection;
    private int quantity = 1;
    private int category = -1;
    private int listVersion;
    private int detailVersion;
    private bool english;
    private bool fullWeapon;
    private bool subscribed;
    private bool submitting;
    private bool isBuilt;
    private string loadError;
    private bool missingRun;

    public void Bind(IRoguelikeGameService service, DataManager templates, IRoguelikeItemCatalog itemCatalog)
    {
        Unsubscribe();
        game = service ?? throw new ArgumentNullException(nameof(service));
        data = templates ?? throw new ArgumentNullException(nameof(templates));
        items = itemCatalog ?? throw new ArgumentNullException(nameof(itemCatalog));
        weaponText = new RosterTextCatalog(data);
        EnsureLayout();
        if (isActiveAndEnabled) Connect();
    }

    public void ShowBindingError(string message)
    {
        Unsubscribe();
        game = null;
        EnsureLayout();
        page = null;
        products.Clear();
        selection = null;
        loadError = message;
        missingRun = false;
        Render(true);
    }

    private void OnEnable()
    {
        if (game != null) Connect();
    }

    private void Connect()
    {
        if (!subscribed)
        {
            game.Changed += OnGameChanged;
            subscribed = true;
        }
        LoadCommonArt();
        Refresh();
    }

    private void OnDisable()
    {
        Unsubscribe();
        StopAllCoroutines();
        pending.Clear();
        CancelRepeat();
        tip?.Hide();
    }

    private void Unsubscribe()
    {
        if (!subscribed) return;
        game.Changed -= OnGameChanged;
        subscribed = false;
    }

    private void OnDestroy()
    {
        foreach (Sprite sprite in sprites.Values)
        {
            if (sprite == null) continue;
            Destroy(sprite.texture);
            Destroy(sprite);
        }
        sprites.Clear();
    }

    private void OnGameChanged()
    {
        if (!submitting) Refresh();
    }

    private void Refresh(bool resetScroll = false)
    {
        if (game == null) return;
        var result = game.Shop.Read(shop);
        page = result.Succeeded ? result.Data : null;
        loadError = result.Succeeded ? null : result.Message;
        missingRun = !result.Succeeded &&
            (result.ErrorCode == RoguelikeSaveErrorCode.NotFound || result.ErrorCode == RoguelikeSaveErrorCode.ActiveRunRequired);
        products = page == null ? new List<Product>() : page.Offers.Select(MakeProduct).ToList();
        Render(resetScroll);
    }

    private Product MakeProduct(RoguelikeShopOffer offer)
    {
        // Shop catalog validation resolves every product against the same item catalog.
        if (!items.TryGetItem(offer.ItemId, out var item) || item == null)
            throw new InvalidOperationException("Shop item is missing from the bound item catalog: " + offer.ItemId);
        bool weapon = item.ItemType == RoguelikeItemType.Weapon;
        int type = weapon && data.WeaponAttributesDict.TryGetValue(item.ReferenceID, out var attr) ? attr.WeaponType : -1;
        string Name(bool useEnglish) => weapon
            ? data.GetUiName(UiNameKind.Weapon, item.ReferenceID, offer.Name, useEnglish)
            : Lang($"item_{item.ItemId}_name", offer.Name, useEnglish);
        return new Product { Offer = offer, Item = item, WeaponType = type, ChineseName = Name(false), EnglishName = Name(true) };
    }

    private void SelectShop(RoguelikeShopKind next)
    {
        if (shop == next) return;
        shop = next;
        category = -1;
        search.SetTextWithoutNotify("");
        selection = null;
        quantity = 1;
        Refresh(true);
    }

    private void SelectCategory(int next)
    {
        category = next;
        Render(true);
    }

    private void SelectProduct(Product product)
    {
        CancelRepeat();
        if (selection != null && tiles.TryGetValue(selection.Offer.ProductId, out var previous))
            previous.Card.SetSelected(false);
        selection = product;
        quantity = 1;
        if (tiles.TryGetValue(product.Offer.ProductId, out var current)) current.Card.SetSelected(true);
        ShowDetail();
    }

    private List<Product> DisplayedProducts()
    {
        string query = search.text.Trim();
        var visible = products.Where(product =>
                (shop == RoguelikeShopKind.Mora || category == -1 ||
                 (category == 5 ? !product.IsWeapon : product.IsWeapon && product.WeaponType == category)) &&
                (query.Length == 0 || product.ChineseName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                 product.EnglishName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0))
            .ToList();
        var groupOrder = products.Select(product => product.Group).Distinct()
            .Select((group, order) => (group, order)).ToDictionary(entry => entry.group, entry => entry.order);
        var highest = visible.GroupBy(product => product.Group)
            .ToDictionary(group => group.Key, group => group.Max(product => product.Item.Rarity));
        return visible.OrderBy(product => product.Offer.Remaining == 0)
            .ThenByDescending(product => highest[product.Group])
            .ThenBy(product => groupOrder[product.Group])
            .ThenByDescending(product => product.Item.Rarity)
            .ThenByDescending(product => product.Offer.UnitPrice)
            .ToList();
    }

    private void Render(bool resetScroll)
    {
        CancelRepeat();
        UpdateHeader();
        var visible = DisplayedProducts();
        string previous = selection?.Offer.ProductId;
        selection = visible.FirstOrDefault(product => product.Offer.ProductId == previous) ?? visible.FirstOrDefault();
        if (selection?.Offer.ProductId != previous) quantity = 1;
        string message = missingRun
            ? T("当前没有进行中的肉鸽存档。", "No active roguelike save.")
            : !string.IsNullOrEmpty(loadError)
                ? T("商店加载失败。", "Could not load the shop.") + "\n" + loadError
                : null;
        UpdateCatalog(visible, message, resetScroll);
        ShowDetail();
    }

    private void ShowDetail()
    {
        int request = ++detailVersion;
        var product = selection;
        detailContent.gameObject.SetActive(product != null);
        if (product == null) return;
        detailName.text = Name(product);
        detailStars.text = new string('★', Mathf.Clamp(product.Item.Rarity, 0, 5));
        var kind = product.IsWeapon ? UIItemListIcon.IconKind.Weapon : UIItemListIcon.IconKind.Material;
        UIItemListIcon.ApplySquareIcon(detailIcon, null, kind);
        detailBackdrop.sprite = null;
        detailBackdrop.color = DeepBrown;
        SpriteFor(IconPath(product), sprite =>
        {
            if (request == detailVersion && detailIcon != null) UIItemListIcon.ApplySquareIcon(detailIcon, sprite, kind);
        });
        SpriteFor(UIItemListIcon.GetRarityBackdropPath(product.Item.Rarity), sprite =>
        {
            if (request != detailVersion || detailBackdrop == null || sprite == null) return;
            detailBackdrop.sprite = sprite;
            detailBackdrop.color = Color.white;
        });
        SetCurrency(unitCurrency, request);
        SetCurrency(totalCurrency, request);
        weaponModes.gameObject.SetActive(product.IsWeapon);
        if (product.IsWeapon) ShowWeapon(product);
        else SetDescription(data.GetRoguelikeItemDescription(product.Item, english), "", "");
        UpdateQuantity();
    }

    private void ShowWeapon(Product product)
    {
        for (int i = 0; i < weaponButtons.Length; i++)
            Paint(weaponButtons[i], i == (fullWeapon ? 1 : 0),
                i == 0 ? T("1级 · 精炼1阶", "Lv. 1 · R1") : T("90级 · 精炼5阶", "Lv. 90 · R5"));
        int weaponId = product.Item.ReferenceID;
        if (!data.WeaponAttributesDict.TryGetValue(weaponId, out var weapon))
        {
            SetDescription(T("缺少武器模板。", "Weapon data is unavailable."), "", "");
            return;
        }
        int rank = fullWeapon ? 5 : 1;
        var panel = data.GetWeaponPanel(weaponId, fullWeapon ? 90 : 1, fullWeapon ? 6 : 0);
        string left = T("基础攻击力", "Base ATK");
        string right = RoguelikeRosterStats.FormatValue("ATK", panel.BaseATK);
        if (!string.IsNullOrEmpty(panel.SecondaryAttribute))
        {
            left += "\n" + RoguelikeRosterStats.Name(panel.SecondaryAttribute, english);
            right += "\n" + RoguelikeRosterStats.FormatValue(panel.SecondaryAttribute, panel.SecondaryValue);
        }
        string effect = weaponText.WeaponDescription(weapon, rank, rank, english);
        SetDescription(effect, left, right);
    }

    private bool StepQuantity(int step)
    {
        if (selection == null || submitting) return false;
        int previous = quantity;
        quantity = (int)Math.Max(1L, Math.Min(selection.Offer.MaxPurchasable, (long)quantity + step));
        UpdateQuantity();
        return quantity != previous && (step < 0 ? quantity > 1 : quantity < selection.Offer.MaxPurchasable);
    }

    private void Purchase()
    {
        var product = selection;
        if (submitting || page == null || product == null || product.Offer.MaxPurchasable < quantity || quantity < 1) return;
        CancelRepeat();
        submitting = true;
        exchange.interactable = false;
        int count = quantity;
        RoguelikeSaveResult<RoguelikeFlowContext> result;
        try { result = game.Shop.Purchase(page.StepId, shop, product.Offer.ProductId, count); }
        finally { submitting = false; }
        if (result.Succeeded) quantity = 1;
        Refresh();
        tip.Show(result.Succeeded ? T("已兑换 ", "Exchanged ") + Name(product) + " ×" + Number(count) :
            result.ErrorCode == RoguelikeSaveErrorCode.InventoryFull
                ? Lang("tip_shop_inventory_space_insufficient",
                    T("背包容量不足，无法兑换所选数量。", "Not enough inventory space for this quantity."), english)
                : result.Message);
    }

    private string ExchangeCapacityTip(bool weapon) => weapon
        ? Lang("tip_shop_weapon_limit", T("武器已到上限，无法兑换。", "Weapon limit reached. Cannot exchange."), english)
        : Lang("tip_shop_item_limit", T("道具已到上限，无法兑换。", "Item limit reached. Cannot exchange."), english);

    private string T(string zh, string en) => english ? en : zh;
    private string Name(Product product) => T(product.ChineseName, product.EnglishName);
    private string Lang(string key, string fallback, bool useEnglish)
        => data != null && data.LangDict.ContainsKey(key) ? data.GetLangText(key, useEnglish) : fallback;
    private static string Number(long amount) => amount.ToString("N0", CultureInfo.InvariantCulture);

    private static string IconPath(Product product)
        => product.IsWeapon ? RoguelikeWishTile.WeaponIconFolder + $"{product.Item.ReferenceID}_01.png" : product.Item.Icon;

    private void SpriteFor(string path, Action<Sprite> ready)
    {
        if (string.IsNullOrEmpty(path)) { ready(null); return; }
        if (sprites.TryGetValue(path, out var sprite)) { ready(sprite); return; }
        if (pending.TryGetValue(path, out var listeners)) { listeners.Add(ready); return; }
        pending.Add(path, new List<Action<Sprite>> { ready });
        StartCoroutine(LoadSprite(path));
    }

    private IEnumerator LoadSprite(string path)
    {
        Sprite loaded = null;
        yield return StreamingAssetSpriteLoader.Load(path, sprite => loaded = sprite, message => Debug.LogWarning(message, this));
        sprites.Add(path, loaded);
        var listeners = pending[path];
        pending.Remove(path);
        foreach (var listener in listeners) listener(loaded);
    }
}
