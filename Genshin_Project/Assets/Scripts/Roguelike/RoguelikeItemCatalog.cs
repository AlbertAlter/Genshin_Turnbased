using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using OfficeOpenXml;
using UnityEngine;

/// <summary>物品编号只在接收装备/关联定义时构造；不能用编号猜测道具使用效果。</summary>
public static class RoguelikeItemIds
{
    public static string Constellation(int characterId) { return Prefix(characterId) + "01"; }
    public static string Weapon(int weaponId) { return Prefix(weaponId); }
    public static string Artifact(int setId, ArtifactSlot slot)
    {
        if (!Enum.IsDefined(typeof(ArtifactSlot), slot)) throw new ArgumentOutOfRangeException(nameof(slot));
        return Prefix(setId) + ((int)slot).ToString("D2", CultureInfo.InvariantCulture);
    }

    private static string Prefix(int id)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
        return "6" + id.ToString(CultureInfo.InvariantCulture);
    }
}

/// <summary>独立、只读物品目录，可注入背包服务；不会启动 DataManager 或生成装备。</summary>
public sealed class RoguelikeItemCatalog : IRoguelikeItemCatalog
{
    public const string WorkbookName = "Items.xlsx";
    private readonly Dictionary<string, RoguelikeItemDefinition> definitions =
        new Dictionary<string, RoguelikeItemDefinition>(StringComparer.Ordinal);
    private readonly Dictionary<int, string> talentMaterials = new Dictionary<int, string>();

    public int Count { get { return definitions.Count; } }

    public RoguelikeItemCatalog(IEnumerable<RoguelikeItemDefinition> source)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        foreach (var definition in source)
        {
            ValidateDefinition(definition);
            if (definitions.ContainsKey(definition.ItemId))
                throw new InvalidDataException("物品编号重复：" + definition.ItemId);
            if (definition.Category == "TalentMaterial")
            {
                if (talentMaterials.ContainsKey(definition.TalentMaterialType))
                    throw new InvalidDataException("天赋材料类型重复：" + definition.TalentMaterialType);
                talentMaterials.Add(definition.TalentMaterialType, definition.ItemId);
            }
            definitions.Add(definition.ItemId, Copy(definition));
        }
    }

    public bool TryGetItem(string itemId, out RoguelikeItemDefinition definition)
    {
        definition = null;
        if (itemId == null || !definitions.TryGetValue(itemId, out var found)) return false;
        definition = Copy(found);
        return true;
    }

    /// <summary>只查询匹配用途的单件经验，不消耗道具、不执行升级。</summary>
    public bool TryGetExperienceValue(string itemId, LevelUpTarget target, out int experience)
    {
        experience = 0;
        string category;
        switch (target)
        {
            case LevelUpTarget.Character: category = "CharacterExperience"; break;
            case LevelUpTarget.Weapon: category = "WeaponExperience"; break;
            case LevelUpTarget.Artifact: category = "ArtifactExperience"; break;
            default: throw new ArgumentOutOfRangeException(nameof(target));
        }
        if (!TryGetItem(itemId, out var item) || item.Category != category) return false;
        experience = item.ExperienceValue;
        return true;
    }

    /// <summary>参数直接对应升级经验表 SkillLevel 的 EXPType，不写死各档道具 ID。</summary>
    public bool TryGetTalentMaterial(int materialType, out RoguelikeItemDefinition definition)
    {
        definition = null;
        return talentMaterials.TryGetValue(materialType, out var id) && TryGetItem(id, out definition);
    }

    public static RoguelikeItemCatalog LoadFromStreamingAssets()
    {
        return Load(Path.Combine(Application.streamingAssetsPath, "Data", WorkbookName));
    }

    /// <summary>显式路径供数据加载与独立校验注入；正式运行入口使用 LoadFromStreamingAssets。</summary>
    public static RoguelikeItemCatalog Load(string workbookPath)
    {
        if (string.IsNullOrWhiteSpace(workbookPath)) throw new ArgumentException("物品表路径不能为空。", nameof(workbookPath));
        if (!File.Exists(workbookPath)) throw new FileNotFoundException("找不到物品表。", workbookPath);
        var result = new List<RoguelikeItemDefinition>();
        using (var package = new ExcelPackage(new FileInfo(workbookPath)))
        {
            LoadSheet(package, "Constellation", RoguelikeItemType.Stack, "Constellation", result);
            LoadSheet(package, "Weapon", RoguelikeItemType.Weapon, "Weapon", result);
            LoadSheet(package, "Artifact", RoguelikeItemType.Artifact, "Artifact", result);
            // 兼容原有只包含装备/命座的物品表；存在材料子表时严格读取并验证，不能忽略错误。
            if (package.Workbook.Worksheets["UpgradeMaterial"] != null)
                LoadSheet(package, "UpgradeMaterial", RoguelikeItemType.Stack, null, result);
        }
        return new RoguelikeItemCatalog(result);
    }

    private static void LoadSheet(ExcelPackage package, string name, RoguelikeItemType type, string category,
        List<RoguelikeItemDefinition> result)
    {
        var sheet = package.Workbook.Worksheets[name];
        if (sheet == null) throw new InvalidDataException(WorkbookName + " 缺少子表：" + name);
        var headers = ExcelTableHeaders.For(sheet);
        headers.Require("ItemID", "Name", "ItemType", "Category", "ReferenceID", "Slot", "Stackable", "Rarity", "Description", "Icon");
        bool materialSheet = name == "UpgradeMaterial";
        if (materialSheet) headers.Require("ExperienceValue", "TalentMaterialType");
        int last = sheet.Dimension == null ? 1 : sheet.Dimension.End.Row;
        for (int row = headers.DataStartRow; row <= last; row++)
        {
            bool any = false;
            for (int column = 1; column <= sheet.Dimension.End.Column; column++)
                if (!string.IsNullOrWhiteSpace(sheet.Cells[row, column].Text)) { any = true; break; }
            if (!any) continue;
            string location = WorkbookName + "/" + name + " 第 " + row + " 行：";
            int stack = Integer(headers, row, "Stackable", location);
            if (stack != 0 && stack != 1) throw new InvalidDataException(location + "Stackable 必须为 0 或 1。");
            var definition = new RoguelikeItemDefinition
            {
                ItemId = headers.Cell(row, "ItemID").Text.Trim(),
                Name = headers.Cell(row, "Name").Text.Trim(),
                ItemType = (RoguelikeItemType)Integer(headers, row, "ItemType", location),
                Category = headers.Cell(row, "Category").Text.Trim(),
                ReferenceID = Integer(headers, row, "ReferenceID", location),
                Slot = Integer(headers, row, "Slot", location),
                Stackable = stack == 1,
                Rarity = Integer(headers, row, "Rarity", location),
                Description = headers.Cell(row, "Description").Text,
                Icon = headers.Cell(row, "Icon").Text.Trim(),
                ExperienceValue = materialSheet ? Integer(headers, row, "ExperienceValue", location) : 0,
                TalentMaterialType = materialSheet ? Integer(headers, row, "TalentMaterialType", location) : 0,
            };
            if (definition.ItemType != type || category != null && definition.Category != category ||
                materialSheet && !IsExperienceCategory(definition.Category) && definition.Category != "TalentMaterial")
                throw new InvalidDataException(location + "ItemType / Category 与所在子表不符。");
            definition.UsageType = materialSheet ? definition.Category :
                category == "Constellation" ? "ActivateConstellation" : "Equipment";
            definition.UsageReference = materialSheet
                ? definition.TalentMaterialType.ToString(CultureInfo.InvariantCulture)
                : definition.ReferenceID.ToString(CultureInfo.InvariantCulture);
            try { ValidateDefinition(definition); }
            catch (ArgumentException exception) { throw new InvalidDataException(location + exception.Message, exception); }
            result.Add(definition);
        }
    }

    private static int Integer(ExcelTableHeaders headers, int row, string name, string location)
    {
        string text = headers.Cell(row, name).Text.Trim();
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            throw new InvalidDataException(location + name + " 必须为整数。");
        return value;
    }

    private static void ValidateDefinition(RoguelikeItemDefinition item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.ItemId) || string.IsNullOrWhiteSpace(item.Name))
            throw new ArgumentException("物品必须有编号及名称。");
        if (!Enum.IsDefined(typeof(RoguelikeItemType), item.ItemType) || item.Rarity < 0 || item.Rarity > 5)
            throw new ArgumentException("物品类型或星级无效：" + item.ItemId);
        if (item.ItemType == RoguelikeItemType.Stack)
        {
            if (!item.Stackable || item.Slot != 0) throw new ArgumentException("堆叠道具必须可堆叠且 Slot=0。");
            if (item.Category == "Constellation" && item.ItemId != RoguelikeItemIds.Constellation(item.ReferenceID))
                throw new ArgumentException("命座道具编号不符合 6+角色ID+01。");
        }
        else
        {
            if (item.Stackable) throw new ArgumentException("装备必须为不可堆叠的实例。");
            string expected;
            if (item.ItemType == RoguelikeItemType.Weapon)
            {
                if (item.Category != "Weapon" || item.Slot != 0) throw new ArgumentException("武器必须配置 Category=Weapon、Slot=0。");
                expected = RoguelikeItemIds.Weapon(item.ReferenceID);
            }
            else
            {
                if (item.Category != "Artifact") throw new ArgumentException("圣遗物必须配置 Category=Artifact。");
                expected = RoguelikeItemIds.Artifact(item.ReferenceID, (ArtifactSlot)item.Slot);
            }
            if (item.ItemId != expected) throw new ArgumentException("装备编号与来源 ID 或部位不符：" + item.ItemId);
        }
        foreach (char digit in item.ItemId)
            if (digit < '0' || digit > '9') throw new ArgumentException("ItemID 只能含数字。");
        if (!item.ItemId.StartsWith("6", StringComparison.Ordinal)) throw new ArgumentException("ItemID 必须以 6 开头。");
        bool experience = IsExperienceCategory(item.Category);
        bool talent = item.Category == "TalentMaterial";
        if (experience || talent)
        {
            if (item.ItemType != RoguelikeItemType.Stack || !item.Stackable || item.ReferenceID != 0 ||
                item.Slot != 0 || !item.ItemId.StartsWith("66", StringComparison.Ordinal))
                throw new ArgumentException("养成材料必须以 66 开头，使用堆叠数据，ReferenceID/Slot 为 0。");
            if (experience && (item.ExperienceValue <= 0 || item.TalentMaterialType != 0))
                throw new ArgumentException("经验材料必须有正经验值，TalentMaterialType 为 0。");
            if (talent && (item.ExperienceValue != 0 || item.TalentMaterialType < 1 || item.TalentMaterialType > 4))
                throw new ArgumentException("天赋材料按 1～4 种类计件，ExperienceValue 必须为 0。");
        }
        else if (item.ExperienceValue != 0 || item.TalentMaterialType != 0)
            throw new ArgumentException("非养成材料不能配置经验或天赋材料种类。");
    }

    private static bool IsExperienceCategory(string category)
    {
        return category == "CharacterExperience" || category == "WeaponExperience" || category == "ArtifactExperience";
    }

    private static RoguelikeItemDefinition Copy(RoguelikeItemDefinition item)
    {
        return new RoguelikeItemDefinition
        {
            ItemId = item.ItemId, Name = item.Name, ItemType = item.ItemType, Category = item.Category,
            ReferenceID = item.ReferenceID, Slot = item.Slot, Stackable = item.Stackable, Rarity = item.Rarity,
            Description = item.Description, Icon = item.Icon, UsageType = item.UsageType, UsageReference = item.UsageReference,
            ExperienceValue = item.ExperienceValue, TalentMaterialType = item.TalentMaterialType,
        };
    }
}
