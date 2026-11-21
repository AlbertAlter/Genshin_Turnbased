using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using OfficeOpenXml;
using UnityEngine;

/// <summary>每层固定奖励；章节分表、按 StageID 关联，与选中的战斗或事件内容无关。</summary>
public sealed class RoguelikeNodeRewardCatalog
{
    public const string WorkbookName = "NodeRewards.xlsx";
    private readonly Dictionary<int, List<RoguelikeItemStackData>> rewards = new();

    public static RoguelikeNodeRewardCatalog LoadFromStreamingAssets(IRoguelikeItemCatalog items, IEnumerable<int> stages)
        => Load(Path.Combine(Application.streamingAssetsPath, "Data", "Roguelike", WorkbookName), items, stages);

    public static RoguelikeNodeRewardCatalog Load(string path, IRoguelikeItemCatalog items, IEnumerable<int> stages)
    {
        if (items == null || stages == null) throw new ArgumentNullException();
        var expected = new HashSet<int>(stages);
        if (expected.Count == 0) throw new InvalidDataException("节点奖励缺少 StageConfig 节点定义。");
        var result = new RoguelikeNodeRewardCatalog();
        using var book = new ExcelPackage(new FileInfo(path));
        foreach (var sheet in book.Workbook.Worksheets)
        {
            if (sheet.Name == "说明") continue;
            if (!sheet.Name.StartsWith("Chapter", StringComparison.Ordinal) ||
                !int.TryParse(sheet.Name.Substring(7), NumberStyles.None, CultureInfo.InvariantCulture, out int chapter) || chapter < 1)
                throw new InvalidDataException($"{WorkbookName} 的章节子表必须命名为 Chapter1、Chapter2 等：{sheet.Name}。");
            var headers = ExcelTableHeaders.For(sheet);
            headers.Require("StageID", "ItemID", "Quantity");
            for (int row = headers.DataStartRow; row <= (sheet.Dimension?.End.Row ?? 0); row++)
            {
                string stageText = headers.Cell(row, "StageID").Text.Trim();
                string itemId = headers.Cell(row, "ItemID").Text.Trim();
                string countText = headers.Cell(row, "Quantity").Text.Trim();
                if (stageText.Length == 0 && itemId.Length == 0 && countText.Length == 0) continue;
                string where = $"{WorkbookName}/{sheet.Name} 第 {row} 行：";
                int stage = RoguelikeStageId.Parse(stageText, where);
                if (stage / 100 != chapter || !expected.Contains(stage))
                    throw new InvalidDataException(where + "StageID 不属于本章或未在 StageConfig 中定义。");
                if (!int.TryParse(countText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) || count <= 0)
                    throw new InvalidDataException(where + "Quantity 必须为正整数。");
                if (!items.TryGetItem(itemId, out var item) || item == null || item.ItemType != RoguelikeItemType.Stack || !item.Stackable)
                    throw new InvalidDataException(where + "ItemID 必须是物品表中已定义的可堆叠道具：" + itemId);
                if (count > RoguelikeInventoryData.StackLimit(itemId))
                    throw new InvalidDataException(where + "单次奖励数量超过该道具上限：" + itemId);
                if (!result.rewards.TryGetValue(stage, out var list)) result.rewards.Add(stage, list = new());
                if (list.Exists(entry => entry.ItemId == itemId))
                    throw new InvalidDataException(where + "同一节点的 ItemID 不能重复：" + itemId);
                list.Add(new RoguelikeItemStackData { ItemId = itemId, Quantity = count });
            }
        }
        foreach (int stage in expected)
            if (!result.rewards.ContainsKey(stage)) throw new InvalidDataException($"{WorkbookName} 缺少 R{stage} 的节点奖励。");
        return result;
    }

    internal RoguelikePendingNodeReward Capture(RoguelikeProgressSnapshot snapshot)
    {
        int stage = checked(snapshot.CurrentChapterId * 100 + snapshot.CurrentStageIndex + 1);
        if (!rewards.TryGetValue(stage, out var list)) throw new InvalidDataException($"缺少 R{stage} 的节点奖励。");
        var pending = new RoguelikePendingNodeReward
        {
            StageId = stage, NodeId = snapshot.CurrentNodeId,
            Items = new List<RoguelikeItemStackData>(),
        };
        foreach (var item in list)
            pending.Items.Add(new RoguelikeItemStackData { ItemId = item.ItemId, Quantity = item.Quantity });
        return pending;
    }
}
