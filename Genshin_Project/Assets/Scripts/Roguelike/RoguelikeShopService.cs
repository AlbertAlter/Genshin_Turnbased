using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public sealed class RoguelikeShopOffer
{
    public string ProductId { get; }
    public string ItemId { get; }
    public string Name { get; }
    public string Source { get; }
    public int BasePrice { get; }
    public int UnitPrice { get; }
    // null 表示配表不限量。
    public int? Remaining { get; }
    public int MaxPurchasable { get; }
    public int InventorySpace { get; }

    internal RoguelikeShopOffer(RoguelikeShopListing listing, int unitPrice, int? remaining, int maxPurchasable,
        int inventorySpace)
    {
        ProductId = listing.Id;
        ItemId = listing.ItemId;
        Name = listing.Name;
        Source = listing.Source;
        BasePrice = listing.Price;
        UnitPrice = unitPrice;
        Remaining = remaining;
        MaxPurchasable = maxPurchasable;
        InventorySpace = inventorySpace;
    }
}

public sealed class RoguelikeShopPage
{
    public string StepId { get; }
    public RoguelikeShopKind Kind { get; }
    public string CurrencyItemId { get; }
    public int Balance { get; }
    public bool PurchasesAllowed { get; }
    public IReadOnlyList<RoguelikeShopOffer> Offers { get; }

    internal RoguelikeShopPage(string stepId, RoguelikeShopKind kind, string currencyItemId, int balance, bool purchasesAllowed,
        List<RoguelikeShopOffer> offers)
    {
        StepId = stepId;
        Kind = kind;
        CurrencyItemId = currencyItemId;
        Balance = balance;
        PurchasesAllowed = purchasesAllowed;
        Offers = offers.AsReadOnly();
    }
}

public sealed class RoguelikeShopService
{
    private readonly RoguelikeFlowService flow;
    private readonly RoguelikeInventoryService inventory;
    private readonly Lazy<RoguelikeShopCatalog> catalog;
    private readonly Action onChanged;

    internal RoguelikeShopService(RoguelikeFlowService flow, RoguelikeInventoryService inventory,
        Func<RoguelikeShopCatalog> loadCatalog, Action onChanged)
    {
        this.flow = flow;
        this.inventory = inventory;
        catalog = new Lazy<RoguelikeShopCatalog>(loadCatalog);
        this.onChanged = onChanged;
    }

    public RoguelikeSaveResult<RoguelikeShopPage> Read(RoguelikeShopKind kind)
    {
        if (!Enum.IsDefined(typeof(RoguelikeShopKind), kind))
            return Invalid<RoguelikeShopPage>("商店类型无效。");
        var current = flow.Resume();
        if (!current.Succeeded) return Failure<RoguelikeShopPage>(current);

        try
        {
            var listings = catalog.Value.Listings(kind);
            var snapshot = current.Data.Snapshot;
            string currency = RoguelikeShopCatalog.CurrencyItemId(kind);
            int balance = ItemCount(snapshot, currency);
            bool purchasesAllowed = CanPurchase(current.Data);
            var offers = new List<RoguelikeShopOffer>();
            foreach (var listing in listings)
                if (listing.OpensAt.IsReached(snapshot))
                    offers.Add(Evaluate(listing, snapshot, currency, balance, purchasesAllowed));
            return RoguelikeSaveResult<RoguelikeShopPage>.Success(
                new RoguelikeShopPage(current.Data.Flow.StepId, kind, currency, balance, purchasesAllowed, offers));
        }
        catch (Exception error) when (IsCatalogError(error))
        {
            return RoguelikeSaveResult<RoguelikeShopPage>.Failure(RoguelikeSaveErrorCode.ValidationFailed, error.Message, error);
        }
    }

    /// <summary>提交当前 StepId；购买成功后应使用返回的新流程上下文或重新读取商店。</summary>
    public RoguelikeSaveResult<RoguelikeFlowContext> Purchase(string stepId, RoguelikeShopKind kind,
        string productId, int quantity)
    {
        if (!Enum.IsDefined(typeof(RoguelikeShopKind), kind) || string.IsNullOrWhiteSpace(productId) || quantity <= 0)
            return Invalid<RoguelikeFlowContext>("兑换须填写有效商店、商品 ID 和正数量。");
        RoguelikeSaveResult<RoguelikeFlowContext> committed;
        using (flow.BeginSaveTransaction())
        {
            var current = flow.Resume();
            if (!current.Succeeded) return current;
            if (current.Data.Flow.StepId != stepId || !CanPurchase(current.Data))
                return Invalid<RoguelikeFlowContext>("当前步骤不允许兑换，或商店页面已过期。");

            RoguelikeShopListing listing;
            try { listing = catalog.Value.Listings(kind).FirstOrDefault(product => product.Id == productId); }
            catch (Exception error) when (IsCatalogError(error))
            {
                return RoguelikeSaveResult<RoguelikeFlowContext>.Failure(RoguelikeSaveErrorCode.ValidationFailed, error.Message, error);
            }
            var snapshot = current.Data.Snapshot;
            if (listing == null || !listing.OpensAt.IsReached(snapshot))
                return Invalid<RoguelikeFlowContext>("商品未开放兑换。");

            string currency = RoguelikeShopCatalog.CurrencyItemId(kind);
            var offer = Evaluate(listing, snapshot, currency, ItemCount(snapshot, currency), true);
            if (quantity > offer.InventorySpace)
                return RoguelikeSaveResult<RoguelikeFlowContext>.Failure(RoguelikeSaveErrorCode.InventoryFull,
                    "背包容量不足，无法兑换所选数量。");
            if (quantity > offer.MaxPurchasable)
                return Invalid<RoguelikeFlowContext>("兑换数量超过剩余库存或货币余额。");

            RoguelikeInventoryTransaction draft = null;
            try
            {
                draft = inventory.CreateDraft(snapshot);
                int cost = checked(offer.UnitPrice * quantity);
                bool paysWithSameItem = cost > 0 && listing.ItemType == RoguelikeItemType.Stack && listing.ItemId == currency;
                if (paysWithSameItem) cost -= quantity;
                if (cost > 0)
                    draft.ConsumeItems(new[] { new RoguelikeItemStackData { ItemId = currency, Quantity = cost } });
                if (listing.ItemType == RoguelikeItemType.Weapon)
                    draft.AddWeapons(listing.WeaponId, quantity);
                else if (!paysWithSameItem)
                    draft.AddItems(new[] { new RoguelikeItemStackData { ItemId = listing.ItemId, Quantity = quantity } });

                var proposed = draft.ExportSnapshot();
                proposed.ShopPurchaseCounts ??= new Dictionary<string, int>(StringComparer.Ordinal);
                proposed.ShopPurchaseCounts[listing.LedgerKey] = checked(Purchased(snapshot, listing) + quantity);
                committed = flow.CommitProgressActionWithContext(stepId, proposed, RoguelikeSaveReason.ShopPurchased);
            }
            catch (Exception error) when (error is ArgumentException || error is InvalidOperationException || error is OverflowException)
            {
                return RoguelikeSaveResult<RoguelikeFlowContext>.Failure(RoguelikeSaveErrorCode.InvalidOperation, error.Message, error);
            }
            finally { draft?.Close(); }
        }
        if (committed.Succeeded) onChanged?.Invoke();
        return committed;
    }

    private RoguelikeShopOffer Evaluate(RoguelikeShopListing listing, RoguelikeProgressSnapshot snapshot,
        string currency, int balance, bool canPurchase)
    {
        int purchased = Purchased(snapshot, listing);
        int? remaining = null;
        if (listing.InitialStock is int stock)
        {
            foreach (var restock in listing.Restocks)
                if (restock.Node.IsReached(snapshot, restock.Offset)) stock += restock.Quantity;
            remaining = Math.Max(0, stock - purchased);
        }

        int price = catalog.Value.UnitPrice(listing, snapshot);
        int max = Math.Min(remaining ?? int.MaxValue, int.MaxValue - purchased);
        if (price > 0) max = Math.Min(max, balance / price);
        int space = int.MaxValue;
        if (listing.ItemType == RoguelikeItemType.Weapon)
            space = Math.Max(0, RoguelikeInventoryData.MaxWeapons - snapshot.Inventory.Weapons.Count);
        else if (listing.ItemId != currency || price == 0)
            space = Math.Max(0, RoguelikeInventoryData.StackLimit(listing.ItemId) - ItemCount(snapshot, listing.ItemId));
        return new RoguelikeShopOffer(listing, price, remaining, canPurchase ? Math.Min(max, space) : 0, space);
    }

    private static bool CanPurchase(RoguelikeFlowContext current)
        => current.Flow.Step != RoguelikeFlowStep.Battle && current.Flow.Step != RoguelikeFlowStep.ChapterCompleted;

    private static int ItemCount(RoguelikeProgressSnapshot snapshot, string itemId)
        => snapshot.Inventory.Items.Find(item => item.ItemId == itemId)?.Quantity ?? 0;

    private static int Purchased(RoguelikeProgressSnapshot snapshot, RoguelikeShopListing listing)
        => snapshot.ShopPurchaseCounts != null && snapshot.ShopPurchaseCounts.TryGetValue(listing.LedgerKey, out int count) ? count : 0;

    private static bool IsCatalogError(Exception error)
        => error is TableValidationException || error is InvalidDataException || error is IOException ||
           error is FormatException || error is UnauthorizedAccessException;

    private static RoguelikeSaveResult<T> Invalid<T>(string message)
        => RoguelikeSaveResult<T>.Failure(RoguelikeSaveErrorCode.InvalidOperation, message);

    private static RoguelikeSaveResult<T> Failure<T>(RoguelikeSaveResult result)
        => RoguelikeSaveResult<T>.Failure(result.ErrorCode, result.Message, result.Exception);
}
