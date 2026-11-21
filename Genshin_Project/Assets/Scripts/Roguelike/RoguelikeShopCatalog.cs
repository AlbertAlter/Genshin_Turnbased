using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using OfficeOpenXml;
using UnityEngine;

public enum RoguelikeShopKind { Starglitter = 0, Stardust = 1, Mora = 2 }

internal readonly struct RoguelikeShopNode
{
    private readonly int chapter;
    private readonly int number;

    private RoguelikeShopNode(int chapter, int number)
    {
        this.chapter = chapter;
        this.number = number;
    }

    internal static RoguelikeShopNode Parse(string value, string error)
    {
        int id = RoguelikeStageId.Parse(value, error);
        return new RoguelikeShopNode(id / 100, id % 100);
    }

    internal bool IsReached(RoguelikeProgressSnapshot progress, long offset = 0)
    {
        if (progress.CurrentChapterId < chapter) return false;
        long distance = (long)progress.CurrentStageIndex + 1 - number;
        if (progress.CurrentChapterId == chapter) return distance >= offset;
        for (int previous = chapter; previous < progress.CurrentChapterId; previous++)
        {
            // 旧档没有此前章节长度时，按该章节已经完整通过处理。
            if (progress.CompletedChapterNodeCounts == null ||
                !progress.CompletedChapterNodeCounts.TryGetValue(previous, out int count))
                return true;
            if (previous == chapter && number > count) return false;
            distance += count;
            if (distance >= offset) return true;
        }
        return false;
    }
}

internal sealed class RoguelikeShopListing
{
    internal string Id;
    internal string ItemId;
    internal string Name;
    internal string Source;
    internal string LedgerKey;
    internal RoguelikeItemType ItemType;
    internal int WeaponId;
    internal int Price;
    internal int? InitialStock;
    internal RoguelikeShopNode OpensAt;
    internal List<(RoguelikeShopNode Node, long Offset, int Quantity)> Restocks = new();
}

public sealed class RoguelikeShopCatalog
{
    public const string WorkbookName = "ItemPrice.xlsx";
    private readonly Dictionary<RoguelikeShopKind, List<RoguelikeShopListing>> listings = new();
    private readonly Dictionary<string, List<(RoguelikeShopNode Node, int Delta)>> discounts =
        new(StringComparer.Ordinal);

    private RoguelikeShopCatalog() { }

    internal static string CurrencyItemId(RoguelikeShopKind kind) => kind switch
    {
        RoguelikeShopKind.Starglitter => RoguelikeWishService.StarglitterItemId,
        RoguelikeShopKind.Stardust => RoguelikeWishService.StardustItemId,
        RoguelikeShopKind.Mora => RoguelikeInventoryData.MoraItemId,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    internal IReadOnlyList<RoguelikeShopListing> Listings(RoguelikeShopKind kind) => listings[kind];

    internal int UnitPrice(RoguelikeShopListing listing, RoguelikeProgressSnapshot progress)
    {
        long price = listing.Price;
        if (discounts.TryGetValue(listing.ItemId, out var rules))
            foreach (var rule in rules)
                if (rule.Node.IsReached(progress)) price += rule.Delta;
        return (int)Math.Max(0, price);
    }

    public static RoguelikeShopCatalog LoadFromStreamingAssets(IRoguelikeItemCatalog items)
        => Load(Path.Combine(Application.streamingAssetsPath, "Data", "Roguelike", WorkbookName), items);

    public static RoguelikeShopCatalog Load(string path, IRoguelikeItemCatalog items)
    {
        if (items == null) throw new ArgumentNullException(nameof(items));
        if (!File.Exists(path)) throw new FileNotFoundException("找不到商店配表。", path);
        using var book = new ExcelPackage(new FileInfo(path));
        var catalog = new RoguelikeShopCatalog();
        catalog.ReadShop(book, items, RoguelikeShopKind.Starglitter, "星辉");
        catalog.ReadShop(book, items, RoguelikeShopKind.Stardust, "星尘");
        catalog.ReadShop(book, items, RoguelikeShopKind.Mora, "摩拉");
        catalog.ReadDiscounts(book);
        return catalog;
    }

    private void ReadShop(ExcelPackage book, IRoguelikeItemCatalog items, RoguelikeShopKind kind, string currency)
    {
        var sheet = RequireSheet(book, currency + "商店");
        var headers = ExcelTableHeaders.For(sheet);
        headers.Require("ID", "中文名", "价格", "价格单位", "首次开放兑换", "可兑换次数");
        const string nodePrefix = "追加节点";
        const string quantityPrefix = "追加数量";
        var restockColumns = headers.NumberedColumns(nodePrefix).Select(column =>
        {
            string suffix = ExcelTableHeaders.Normalize(sheet.Cells[1, column].Text).Substring(nodePrefix.Length);
            return (Node: column, Quantity: headers.Required(quantityPrefix + suffix));
        }).ToArray();
        if (headers.NumberedColumns(quantityPrefix).Count != restockColumns.Length ||
            headers.Optional(nodePrefix) > 0 || headers.Optional(quantityPrefix) > 0)
            throw new InvalidDataException($"{WorkbookName}/{sheet.Name} 的追加节点和追加数量须按编号配对。");

        string currencyId = CurrencyItemId(kind);
        if (!items.TryGetItem(currencyId, out var currencyItem) || currencyItem == null ||
            currencyItem.ItemType != RoguelikeItemType.Stack || !currencyItem.Stackable ||
            currencyItem.Category != "ExchangeCurrency")
            throw new InvalidDataException($"{WorkbookName}/{sheet.Name} 找不到有效的兑换货币 {currencyId}。");

        var products = new List<RoguelikeShopListing>();
        listings.Add(kind, products);
        var itemIds = new HashSet<string>(StringComparer.Ordinal);
        int sourceColumn = headers.Optional("原版来源");
        for (int row = headers.DataStartRow; row <= (sheet.Dimension?.End.Row ?? 1); row++)
        {
            string Field(string name) => headers.Cell(row, name).Text.Trim();
            string open = Field("首次开放兑换");
            if (open.Length == 0) continue;

            string id = Field("ID");
            string error = $"{WorkbookName}/{sheet.Name} 第 {row} 行（{id}）：";
            int price = NonNegativeInt(Field("价格"), "价格", error);
            if (Field("价格单位") != currency)
                throw new InvalidDataException(error + $"价格单位须为 {currency}。");
            var item = ResolveItem(items, id, error);
            if (!itemIds.Add(item.ItemId))
                throw new InvalidDataException(error + $"商品 {item.ItemId} 重复。");
            string limit = Field("可兑换次数");
            int? initialStock = limit.Length == 0 ? (int?)null : NonNegativeInt(limit, "可兑换次数", error);
            var listing = new RoguelikeShopListing
            {
                Id = id,
                ItemId = item.ItemId,
                Name = Field("中文名"),
                Source = sourceColumn > 0 ? sheet.Cells[row, sourceColumn].Text.Trim() : null,
                LedgerKey = $"{(int)kind}:{item.ItemId}",
                ItemType = item.ItemType,
                WeaponId = item.ReferenceID,
                Price = price,
                InitialStock = initialStock,
                OpensAt = RoguelikeShopNode.Parse(open, error),
            };
            ReadRestocks(sheet, row, listing, restockColumns, error);
            products.Add(listing);
        }
    }

    private static void ReadRestocks(ExcelWorksheet sheet, int row, RoguelikeShopListing listing,
        (int Node, int Quantity)[] columns, string error)
    {
        var anchor = listing.OpensAt;
        long offset = 0;
        long total = listing.InitialStock.GetValueOrDefault();
        foreach (var pair in columns)
        {
            string node = sheet.Cells[row, pair.Node].Text.Trim();
            string amount = sheet.Cells[row, pair.Quantity].Text.Trim();
            if (node.Length == 0 && amount.Length == 0) continue;
            int quantity = NonNegativeInt(amount, "追加数量", error);
            if (int.TryParse(node, NumberStyles.None, CultureInfo.InvariantCulture, out int distance) && distance > 0)
                offset += distance;
            else
            {
                anchor = RoguelikeShopNode.Parse(node, error);
                offset = 0;
            }
            total += quantity;
            if (listing.InitialStock.HasValue && total > int.MaxValue)
                throw new InvalidDataException(error + "累计可兑换次数超出整数范围。");
            listing.Restocks.Add((anchor, offset, quantity));
        }
    }

    private void ReadDiscounts(ExcelPackage book)
    {
        var sheet = RequireSheet(book, "打折");
        var headers = ExcelTableHeaders.For(sheet);
        headers.Require("道具id", "折扣", "节点");
        var goods = listings.Values.SelectMany(products => products).ToArray();
        for (int row = headers.DataStartRow; row <= (sheet.Dimension?.End.Row ?? 1); row++)
        {
            string id = headers.Cell(row, "道具id").Text.Trim();
            string amount = headers.Cell(row, "折扣").Text.Trim();
            string node = headers.Cell(row, "节点").Text.Trim();
            if (id.Length == 0 && amount.Length == 0 && node.Length == 0) continue;
            string error = $"{WorkbookName}/{sheet.Name} 第 {row} 行（{id}）：";
            var matchingItems = goods.Where(listing => listing.ItemId == id || listing.Id == id)
                .Select(listing => listing.ItemId).Distinct().ToArray();
            if (matchingItems.Length != 1)
                throw new InvalidDataException(error + "道具id 须对应一个已配置开放节点的商店道具。");
            if (!int.TryParse(amount, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int delta) || delta >= 0)
                throw new InvalidDataException(error + "折扣须填写负整数。");
            string itemId = matchingItems[0];
            if (!discounts.TryGetValue(itemId, out var rules))
                discounts.Add(itemId, rules = new List<(RoguelikeShopNode, int)>());
            rules.Add((RoguelikeShopNode.Parse(node, error), delta));
        }
    }

    private static RoguelikeItemDefinition ResolveItem(IRoguelikeItemCatalog items, string id, string error)
    {
        if (!items.TryGetItem(id, out var item) || item == null)
        {
            // 星辉和星尘商品允许使用武器模板编号。
            if (!int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out int weaponId) || weaponId <= 0 ||
                !items.TryGetItem(RoguelikeItemIds.Weapon(weaponId), out item) || item == null ||
                item.ItemType != RoguelikeItemType.Weapon || item.ReferenceID != weaponId)
                throw new InvalidDataException(error + "找不到商品对应的道具或武器模板编号。");
        }
        if (item.ItemType != RoguelikeItemType.Weapon &&
            (item.ItemType != RoguelikeItemType.Stack || !item.Stackable))
            throw new InvalidDataException(error + "商品须为可堆叠道具或武器。");
        return item;
    }

    private static int NonNegativeInt(string value, string field, string error)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int number))
            throw new InvalidDataException(error + $"{field}须填写非负整数。");
        return number;
    }

    private static ExcelWorksheet RequireSheet(ExcelPackage book, string name)
        => book.Workbook.Worksheets[name] ?? throw new InvalidDataException($"{WorkbookName} 缺少子表 [{name}]。");
}
