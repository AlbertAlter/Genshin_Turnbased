using System;
using System.Collections.Generic;
using System.IO;
using OfficeOpenXml;
using UnityEngine;

public enum ArtifactSlot
{
    Flower = 1,
    Plume = 2,
    Sands = 3,
    Goblet = 4,
    Circlet = 5
}

[Serializable]
public sealed class ArtifactSecondaryAttribute
{
    public string AttributeType;
    public float Value;
    public int RollCount;
}

[Serializable]
public sealed class GeneratedArtifact
{
    public int Star;
    public ArtifactSlot Slot;
    public int Level;
    public int MaxLevel;
    public string MainAttributeType;
    public float MainAttributeValue;
    public List<ArtifactSecondaryAttribute> SecondaryAttributes = new List<ArtifactSecondaryAttribute>();
}

public sealed class ArtifactMainAttributeGrowth
{
    public string AttributeType;
    public int Star;
    public float InitialStat;
    public float LevelBonus;
}

public sealed class ArtifactAttributeWeight
{
    public ArtifactSlot Slot;
    public string AttributeType;
    public float Weight;
}

public sealed class ArtifactSecondaryAttributeRule
{
    public string AttributeType;
    public float Weight;
    public float Stat;
}

/// <summary>
/// 圣遗物生成所需的只读配置容器。既可由工作簿加载，也可由测试或其他数据源自行构造。
/// </summary>
public sealed class ArtifactGenerationConfig
{
    private readonly Dictionary<int, int> _maxLevels = new Dictionary<int, int>();
    private readonly Dictionary<string, Dictionary<int, ArtifactMainAttributeGrowth>> _mainGrowth =
        new Dictionary<string, Dictionary<int, ArtifactMainAttributeGrowth>>(StringComparer.Ordinal);
    private readonly Dictionary<ArtifactSlot, List<ArtifactAttributeWeight>> _mainWeights =
        new Dictionary<ArtifactSlot, List<ArtifactAttributeWeight>>();
    private readonly List<ArtifactSecondaryAttributeRule> _secondaryRules =
        new List<ArtifactSecondaryAttributeRule>();

    public void AddMaxLevel(int star, int maxLevel)
    {
        if (star <= 0) throw new ArgumentOutOfRangeException(nameof(star));
        if (maxLevel < 0) throw new ArgumentOutOfRangeException(nameof(maxLevel));
        if (_maxLevels.ContainsKey(star))
            throw new InvalidDataException($"MaxLevel 中存在重复星级：{star}");

        _maxLevels.Add(star, maxLevel);
    }

    public void AddMainGrowth(string attributeType, int star, float initialStat, float levelBonus)
    {
        RequireAttributeType(attributeType, nameof(attributeType));
        if (star <= 0) throw new ArgumentOutOfRangeException(nameof(star));

        if (!_mainGrowth.TryGetValue(attributeType, out Dictionary<int, ArtifactMainAttributeGrowth> byStar))
        {
            byStar = new Dictionary<int, ArtifactMainAttributeGrowth>();
            _mainGrowth.Add(attributeType, byStar);
        }

        if (byStar.ContainsKey(star))
            throw new InvalidDataException($"LevelBonus 中存在重复项：{attributeType}/{star}星");

        byStar.Add(star, new ArtifactMainAttributeGrowth
        {
            AttributeType = attributeType,
            Star = star,
            InitialStat = initialStat,
            LevelBonus = levelBonus
        });
    }

    public void AddMainWeight(ArtifactSlot slot, string attributeType, float weight)
    {
        ValidateSlot(slot);
        RequireAttributeType(attributeType, nameof(attributeType));
        if (weight <= 0f) throw new ArgumentOutOfRangeException(nameof(weight));

        if (!_mainWeights.TryGetValue(slot, out List<ArtifactAttributeWeight> weights))
        {
            weights = new List<ArtifactAttributeWeight>();
            _mainWeights.Add(slot, weights);
        }

        for (int index = 0; index < weights.Count; index++)
        {
            if (string.Equals(weights[index].AttributeType, attributeType, StringComparison.Ordinal))
                throw new InvalidDataException($"AttributeWeight 中存在重复项：{slot}/{attributeType}");
        }

        weights.Add(new ArtifactAttributeWeight
        {
            Slot = slot,
            AttributeType = attributeType,
            Weight = weight
        });
    }

    public void AddSecondaryRule(string attributeType, float weight, float stat)
    {
        RequireAttributeType(attributeType, nameof(attributeType));
        if (weight <= 0f) throw new ArgumentOutOfRangeException(nameof(weight));
        if (stat < 0f) throw new ArgumentOutOfRangeException(nameof(stat));

        for (int index = 0; index < _secondaryRules.Count; index++)
        {
            if (string.Equals(_secondaryRules[index].AttributeType, attributeType, StringComparison.Ordinal))
                throw new InvalidDataException($"SecondaryAttribute 中存在重复项：{attributeType}");
        }

        _secondaryRules.Add(new ArtifactSecondaryAttributeRule
        {
            AttributeType = attributeType,
            Weight = weight,
            Stat = stat
        });
    }

    public int GetMaxLevel(int star)
    {
        if (!_maxLevels.TryGetValue(star, out int maxLevel))
            throw new ArgumentException($"没有配置 {star} 星圣遗物的最大等级。", nameof(star));
        return maxLevel;
    }

    public ArtifactMainAttributeGrowth GetMainGrowth(string attributeType, int star)
    {
        string growthType = NormalizeMainGrowthType(attributeType);
        if (!_mainGrowth.TryGetValue(growthType, out Dictionary<int, ArtifactMainAttributeGrowth> byStar) ||
            !byStar.TryGetValue(star, out ArtifactMainAttributeGrowth growth))
        {
            throw new InvalidDataException($"缺少主属性成长配置：{attributeType}（成长类型 {growthType}）/{star}星");
        }

        return growth;
    }

    public IReadOnlyList<ArtifactAttributeWeight> GetMainWeights(ArtifactSlot slot)
    {
        ValidateSlot(slot);
        if (!_mainWeights.TryGetValue(slot, out List<ArtifactAttributeWeight> weights) || weights.Count == 0)
            throw new InvalidDataException($"部位 {slot} 没有主属性权重配置。");
        return weights;
    }

    public IReadOnlyList<ArtifactSecondaryAttributeRule> SecondaryRules => _secondaryRules;

    public void Validate()
    {
        if (_maxLevels.Count == 0) throw new InvalidDataException("MaxLevel 没有有效数据。");
        if (_secondaryRules.Count < 4) throw new InvalidDataException("副属性种类少于 4，无法完成生成。");

        for (int slotValue = (int)ArtifactSlot.Flower; slotValue <= (int)ArtifactSlot.Circlet; slotValue++)
        {
            ArtifactSlot slot = (ArtifactSlot)slotValue;
            IReadOnlyList<ArtifactAttributeWeight> weights = GetMainWeights(slot);
            for (int index = 0; index < weights.Count; index++)
            {
                foreach (int star in _maxLevels.Keys)
                    GetMainGrowth(weights[index].AttributeType, star);
            }
        }
    }

    private static string NormalizeMainGrowthType(string attributeType)
    {
        switch (attributeType)
        {
            case "PyroDMGBonus":
            case "HydroDMGBonus":
            case "ElectroDMGBonus":
            case "CryoDMGBonus":
            case "AnemoDMGBonus":
            case "GeoDMGBonus":
            case "DendroDMGBonus":
                return "ElementDMGBonus";
            default:
                return attributeType;
        }
    }

    private static void ValidateSlot(ArtifactSlot slot)
    {
        if (slot < ArtifactSlot.Flower || slot > ArtifactSlot.Circlet)
            throw new ArgumentOutOfRangeException(nameof(slot), slot, "圣遗物部位必须在 1 到 5 之间。");
    }

    private static void RequireAttributeType(string attributeType, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(attributeType))
            throw new ArgumentException("属性类型不能为空。", parameterName);
    }
}

public static class ArtifactConfigurationLoader
{
    public const string WorkbookName = "ArtifactsAttributes.xlsx";

    public static ArtifactGenerationConfig LoadFromStreamingAssets()
    {
        string path = Path.Combine(Application.streamingAssetsPath, "Data", WorkbookName);
        return Load(path);
    }

    public static ArtifactGenerationConfig Load(string workbookPath)
    {
        if (string.IsNullOrWhiteSpace(workbookPath))
            throw new ArgumentException("工作簿路径不能为空。", nameof(workbookPath));
        if (!File.Exists(workbookPath))
            throw new FileNotFoundException("找不到圣遗物属性工作簿。", workbookPath);

        var config = new ArtifactGenerationConfig();
        using (var package = new ExcelPackage(new FileInfo(workbookPath)))
        {
            LoadMaxLevels(RequireSheet(package, "MaxLevel"), config);
            LoadMainGrowth(RequireSheet(package, "LevelBonus"), config);
            LoadMainWeights(RequireSheet(package, "AttributeWeight"), config);
            LoadSecondaryRules(RequireSheet(package, "SecondaryAttribute"), config);
        }

        config.Validate();
        return config;
    }

    private static void LoadMaxLevels(ExcelWorksheet sheet, ArtifactGenerationConfig config)
    {
        ValidateHeaders(sheet, "Star", "MaxLevel");
        for (int row = 2; row <= LastRow(sheet); row++)
        {
            int star = ReadInt(sheet, row, 1);
            if (star <= 0) continue;
            config.AddMaxLevel(star, ReadInt(sheet, row, 2));
        }
    }

    private static void LoadMainGrowth(ExcelWorksheet sheet, ArtifactGenerationConfig config)
    {
        ValidateHeaders(sheet, "AttributeType", "ArtifactStar", "InitialStat", "LevelBonus");
        string currentAttributeType = null;
        for (int row = 2; row <= LastRow(sheet); row++)
        {
            string attributeType = sheet.Cells[row, 1].Text.Trim();
            if (!string.IsNullOrEmpty(attributeType)) currentAttributeType = attributeType;

            int star = ReadInt(sheet, row, 2);
            if (star <= 0) continue;
            if (string.IsNullOrEmpty(currentAttributeType))
                throw new InvalidDataException($"LevelBonus 第 {row} 行缺少 AttributeType，且没有可继承的上一项。");

            config.AddMainGrowth(
                currentAttributeType,
                star,
                ReadFloat(sheet, row, 3),
                ReadFloat(sheet, row, 4));
        }
    }

    private static void LoadMainWeights(ExcelWorksheet sheet, ArtifactGenerationConfig config)
    {
        ValidateHeaders(sheet, "Slot", "AttributeType", "Weight");
        for (int row = 2; row <= LastRow(sheet); row++)
        {
            int slot = ReadInt(sheet, row, 1);
            if (slot <= 0) continue;
            config.AddMainWeight(
                (ArtifactSlot)slot,
                sheet.Cells[row, 2].Text.Trim(),
                ReadFloat(sheet, row, 3));
        }
    }

    private static void LoadSecondaryRules(ExcelWorksheet sheet, ArtifactGenerationConfig config)
    {
        ValidateHeaders(sheet, "SecondaryAttribute", "Weight", "Stat");
        for (int row = 2; row <= LastRow(sheet); row++)
        {
            string attributeType = sheet.Cells[row, 1].Text.Trim();
            if (string.IsNullOrEmpty(attributeType)) continue;
            config.AddSecondaryRule(
                attributeType,
                ReadFloat(sheet, row, 2),
                ReadFloat(sheet, row, 3));
        }
    }

    private static ExcelWorksheet RequireSheet(ExcelPackage package, string sheetName)
    {
        ExcelWorksheet sheet = package.Workbook.Worksheets[sheetName];
        if (sheet == null) throw new InvalidDataException($"{WorkbookName} 缺少工作表 {sheetName}。");
        return sheet;
    }

    private static void ValidateHeaders(ExcelWorksheet sheet, params string[] expected)
    {
        for (int index = 0; index < expected.Length; index++)
        {
            string actual = sheet.Cells[1, index + 1].Text.Trim();
            if (!string.Equals(actual, expected[index], StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"{WorkbookName}/{sheet.Name} 第 {index + 1} 列应为 {expected[index]}，实际为 [{actual}]。");
            }
        }
    }

    private static int LastRow(ExcelWorksheet sheet)
    {
        return sheet.Dimension == null ? 1 : sheet.Dimension.End.Row;
    }

    private static int ReadInt(ExcelWorksheet sheet, int row, int column)
    {
        object value = sheet.Cells[row, column].Value;
        if (value is int intValue) return intValue;
        if (value is double doubleValue) return (int)doubleValue;
        if (int.TryParse(Convert.ToString(value), out int parsed)) return parsed;
        return 0;
    }

    private static float ReadFloat(ExcelWorksheet sheet, int row, int column)
    {
        object value = sheet.Cells[row, column].Value;
        if (value is float floatValue) return floatValue;
        if (value is double doubleValue) return (float)doubleValue;
        if (value is int intValue) return intValue;
        if (float.TryParse(Convert.ToString(value), out float parsed)) return parsed;
        return 0f;
    }
}

/// <summary>
/// 圣遗物生成器：只依赖生成配置与统一随机源，不读写角色、战斗、背包或存档状态。
/// </summary>
public sealed class ArtifactGenerator
{
    private static readonly float[] RollMultipliers = { 0.7f, 0.8f, 0.9f, 1f };

    private readonly ArtifactGenerationConfig _config;
    private readonly IBattleRandomSource _random;

    public ArtifactGenerator(ArtifactGenerationConfig config, IBattleRandomSource randomSource = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _random = randomSource;
        _config.Validate();
    }

    public static ArtifactGenerator CreateFromStreamingAssets(IBattleRandomSource randomSource = null)
    {
        return new ArtifactGenerator(ArtifactConfigurationLoader.LoadFromStreamingAssets(), randomSource);
    }

    public GeneratedArtifact Generate(int star, ArtifactSlot? slot = null, int targetLevel = 0)
    {
        int maxLevel = _config.GetMaxLevel(star);
        if (targetLevel < 0 || targetLevel > maxLevel)
            throw new ArgumentOutOfRangeException(nameof(targetLevel), targetLevel, $"目标等级必须在 0 到 {maxLevel} 之间。");

        ArtifactSlot selectedSlot = slot ?? (ArtifactSlot)(NextIndex(5, "随机部位") + 1);

        ArtifactAttributeWeight mainRule = SelectWeighted(_config.GetMainWeights(selectedSlot), item => item.Weight);
        ArtifactMainAttributeGrowth growth = _config.GetMainGrowth(mainRule.AttributeType, star);
        var artifact = new GeneratedArtifact
        {
            Star = star,
            Slot = selectedSlot,
            Level = 0,
            MaxLevel = maxLevel,
            MainAttributeType = mainRule.AttributeType,
            MainAttributeValue = growth.InitialStat
        };

        int initialSecondaryCount = Next01("初始副属性数量") < 0.8d ? 3 : 4;
        for (int index = 0; index < initialSecondaryCount; index++)
            AddNewSecondaryAttribute(artifact);

        UpgradeToLevel(artifact, targetLevel);
        return artifact;
    }

    public void UpgradeToLevel(GeneratedArtifact artifact, int targetLevel)
    {
        if (artifact == null) throw new ArgumentNullException(nameof(artifact));
        int configuredMaxLevel = _config.GetMaxLevel(artifact.Star);
        if (artifact.MaxLevel != configuredMaxLevel)
            throw new InvalidOperationException($"圣遗物 MaxLevel={artifact.MaxLevel} 与 {artifact.Star} 星配置值 {configuredMaxLevel} 不一致。");
        if (targetLevel < artifact.Level)
            throw new ArgumentOutOfRangeException(nameof(targetLevel), targetLevel, "强化接口不支持降低等级。");
        if (targetLevel > artifact.MaxLevel)
            throw new ArgumentOutOfRangeException(nameof(targetLevel), targetLevel, $"目标等级不能超过 {artifact.MaxLevel}。");
        if (artifact.SecondaryAttributes == null)
            throw new InvalidOperationException("圣遗物副属性集合不能为空。");

        ArtifactMainAttributeGrowth growth = _config.GetMainGrowth(artifact.MainAttributeType, artifact.Star);
        while (artifact.Level < targetLevel)
        {
            artifact.Level++;
            artifact.MainAttributeValue = growth.InitialStat + growth.LevelBonus * artifact.Level;

            if (artifact.Level % 4 != 0) continue;
            if (artifact.SecondaryAttributes.Count < 4)
                AddNewSecondaryAttribute(artifact);
            else
                ReinforceSecondaryAttribute(artifact);
        }
    }

    private void AddNewSecondaryAttribute(GeneratedArtifact artifact)
    {
        var candidates = new List<ArtifactSecondaryAttributeRule>();
        IReadOnlyList<ArtifactSecondaryAttributeRule> rules = _config.SecondaryRules;
        for (int index = 0; index < rules.Count; index++)
        {
            ArtifactSecondaryAttributeRule rule = rules[index];
            if (string.Equals(rule.AttributeType, artifact.MainAttributeType, StringComparison.Ordinal)) continue;
            if (HasSecondaryAttribute(artifact, rule.AttributeType)) continue;
            candidates.Add(rule);
        }

        if (candidates.Count == 0)
            throw new InvalidOperationException("没有可添加的副属性：主属性和已有副属性已排除全部候选项。");

        ArtifactSecondaryAttributeRule selected = SelectWeighted(candidates, item => item.Weight);
        artifact.SecondaryAttributes.Add(new ArtifactSecondaryAttribute
        {
            AttributeType = selected.AttributeType,
            Value = RollValue(selected.Stat),
            RollCount = 1
        });
    }

    private void ReinforceSecondaryAttribute(GeneratedArtifact artifact)
    {
        if (artifact.SecondaryAttributes.Count == 0)
            throw new InvalidOperationException("没有可强化的副属性。");

        ArtifactSecondaryAttribute target = artifact.SecondaryAttributes[
            NextIndex(artifact.SecondaryAttributes.Count, "强化副属性")];
        ArtifactSecondaryAttributeRule rule = FindSecondaryRule(target.AttributeType);
        target.Value += RollValue(rule.Stat);
        target.RollCount++;
    }

    private ArtifactSecondaryAttributeRule FindSecondaryRule(string attributeType)
    {
        IReadOnlyList<ArtifactSecondaryAttributeRule> rules = _config.SecondaryRules;
        for (int index = 0; index < rules.Count; index++)
        {
            if (string.Equals(rules[index].AttributeType, attributeType, StringComparison.Ordinal))
                return rules[index];
        }

        throw new InvalidDataException($"找不到副属性规则：{attributeType}");
    }

    private float RollValue(float baseStat)
    {
        return baseStat * RollMultipliers[NextIndex(RollMultipliers.Length, "副属性档位")];
    }

    private static bool HasSecondaryAttribute(GeneratedArtifact artifact, string attributeType)
    {
        for (int index = 0; index < artifact.SecondaryAttributes.Count; index++)
        {
            if (string.Equals(artifact.SecondaryAttributes[index].AttributeType, attributeType, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private T SelectWeighted<T>(IReadOnlyList<T> candidates, Func<T, float> getWeight)
    {
        if (candidates == null || candidates.Count == 0)
            throw new InvalidOperationException("加权随机没有候选项。");

        double totalWeight = 0d;
        for (int index = 0; index < candidates.Count; index++)
        {
            float weight = getWeight(candidates[index]);
            if (weight <= 0f) throw new InvalidDataException("加权随机项的权重必须大于 0。");
            totalWeight += weight;
        }

        double point = Next01("加权随机") * totalWeight;
        for (int index = 0; index < candidates.Count; index++)
        {
            point -= getWeight(candidates[index]);
            if (point < 0d) return candidates[index];
        }

        return candidates[candidates.Count - 1];
    }

    private int NextIndex(int count, string operation)
    {
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
        return Math.Min((int)(Next01(operation) * count), count - 1);
    }

    private double Next01(string operation)
    {
        double value = _random != null
            ? _random.NextFloat01()
            : BattleRandom.NextFloat01();
        if (double.IsNaN(value) || value < 0d || value >= 1d)
            throw new InvalidOperationException($"随机源在{operation}时返回了 {value}，应处于 [0, 1) 范围内。");
        return value;
    }
}
