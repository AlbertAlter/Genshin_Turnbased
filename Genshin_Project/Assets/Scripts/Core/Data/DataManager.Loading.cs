using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using OfficeOpenXml;
using UnityEngine;

public partial class DataManager
{
    private static HashSet<string> ReadAuthoredEffectFields(OfficeOpenXml.ExcelWorksheet sheet, int row)
    {
        var fields = new HashSet<string>(StringComparer.Ordinal);
        if (sheet.Dimension == null) return fields;
        foreach (var column in ExcelTableHeaders.For(sheet).Columns)
        {
            if (string.IsNullOrWhiteSpace(sheet.Cells[row, column.Value].Text)) continue;
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.IgnoreCase;
            var field = typeof(SkillEffectData).GetField(column.Key, flags)
                ?? typeof(StatusEffectData).GetField(column.Key, flags);
            fields.Add(field?.Name ?? column.Key); // 显式0仍算填写，表头大小写/空格不影响参数覆盖。
        }
        return fields;
    }
    private readonly List<string> _loadErrors = new List<string>();

    /// <summary>最近一次整套加载是否成功。</summary>
    public bool IsLoaded { get; private set; }

    /// <summary>
    /// 安全重载全部配表。只有全部文件读取和校验成功才提交新数据；
    /// 任何异常都会恢复重载前的整套集合，并继续向调用方抛出。
    /// </summary>
    public void ReloadAllData()
    {
        LoadDataAtomically(() =>
        {
            IReadOnlyList<string> preflightErrors = DataSourcePreflightValidator.Validate(ChartsPath);
            if (preflightErrors.Count > 0)
                throw new DataLoadException("配表文件预检失败", preflightErrors);

            LoadAllTables();

            if (_loadErrors.Count > 0)
                throw new DataLoadException($"配表校验失败，共 {_loadErrors.Count} 处", _loadErrors);
        });

        Debug.Log($"DataManager: load complete. {CharacterOverviewList.Count} chars, {EnemyMainDict.Count} enemies, {StatusMainDict.Count} statuses");
    }

    /// <summary>供安全重载流程与 EditMode 回滚测试共用。</summary>
    protected void LoadDataAtomically(Action loadAction)
    {
        if (loadAction == null)
            throw new ArgumentNullException(nameof(loadAction));

        DataManagerState previousState = DataManagerState.Capture(this);
        bool wasLoaded = IsLoaded;

        new DataManagerState().ApplyTo(this);
        _loadErrors.Clear();

        try
        {
            loadAction();
            IsLoaded = true;
            _loadErrors.Clear();
        }
        catch (Exception exception)
        {
            previousState.ApplyTo(this);
            IsLoaded = wasLoaded;
            string[] collectedErrors = _loadErrors.ToArray();
            _loadErrors.Clear();

            if (exception is DataLoadException)
                throw;

            var errors = new List<string>(collectedErrors) { exception.Message };
            throw new DataLoadException("配表加载失败，已恢复上一次成功数据", errors, exception);
        }
    }

    /// <summary>执行一次配表校验并收集错误，所有表读完后统一报告。</summary>
    private void TryValidate(string tag, Action validate)
    {
        try
        {
            validate();
        }
        catch (TableValidationException exception)
        {
            // 行校验器通常只知道Sheet/行号；不能丢掉调用方提供的工作簿与效果身份。
            _loadErrors.Add($"[{tag}] {exception.Message}");
        }
        catch (Exception exception)
        {
            _loadErrors.Add($"[{tag}] {exception.Message}");
        }
    }

    private void LoadAllTables()
    {
        LoadCharacterOverview();
        LoadCharacterSheets();
        LoadGrowthCurve();
        LoadWeaponAttributes();
        LoadStatusData();
        LoadEnemyStatusData();
        LoadOverallStatusData();
        LoadWeaponStatusData();
        LoadArtifactStatusData();
        LoadEnemyAttributes();
        LoadEnemyCurves();
        LoadEnemyPackages();
        LoadReactionLevelCoefficient();
        LoadEnemyShieldRules();
        LoadTeamBonus();
        LoadHexereiStatusRules();
        TryValidate("Immune状态及效果", () => DamageImmunity.ValidateDefinitions(this));
        TryValidate("Enchant状态及施加参数", () => EnchantStatusSystem.ValidateDefinitions(this));
        TryValidate("Count效果及状态引用", () => CountEffect.ValidateDefinitions(this));

        // 旧 StageConfig.xlsx 为可选兼容数据；当前战斗使用 Resources/StageConfig.json。
        LoadStageConfig();
    }

}

/// <summary>
/// 工作表第1行的字段索引：按名称读取，不保留任何历史列号回退。
/// 每个只读工作表只建立一次索引；弱引用缓存不持有已关闭的工作簿。
/// </summary>
public sealed class ExcelTableHeaders
{
    private static readonly ConditionalWeakTable<ExcelWorksheet, ExcelTableHeaders> Cache
        = new ConditionalWeakTable<ExcelWorksheet, ExcelTableHeaders>();
    private static readonly HashSet<string> TypeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "type", "int", "long", "short", "byte", "uint", "float", "double", "decimal", "string", "bool", "boolean" };
    private readonly ExcelWorksheet sheet;
    private readonly Dictionary<string, int> columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    private ExcelTableHeaders(ExcelWorksheet sheet)
    {
        this.sheet = sheet;
        for (int column = 1; column <= (sheet.Dimension?.End.Column ?? 0); column++)
        {
            string name = Normalize(sheet.Cells[1, column].Text);
            if (name.Length == 0) continue;
            if (columns.ContainsKey(name))
                throw new TableValidationException($"[{sheet.Name}] 重复表头：{name}");
            columns.Add(name, column);
        }
    }

    public static ExcelTableHeaders For(ExcelWorksheet sheet)
    {
        if (sheet == null) throw new ArgumentNullException(nameof(sheet));
        return Cache.GetValue(sheet, value => new ExcelTableHeaders(value));
    }

    public static string Normalize(string name) => (name ?? string.Empty).Trim().TrimStart('\uFEFF').Trim();
    public IEnumerable<KeyValuePair<string, int>> Columns => columns;
    public int Optional(string name) => columns.TryGetValue(Normalize(name), out int column) ? column : -1;

    public int Required(string name)
    {
        int column = Optional(name);
        if (column < 1) throw new TableValidationException($"[{sheet.Name}] 缺少表头：{name}");
        return column;
    }

    public ExcelRange Cell(int row, string name) => sheet.Cells[row, Required(name)];

    public void Require(params string[] names)
    { foreach (string name in names) Required(name); }

    /// <summary>按字段后缀数字排序，而不是物理列顺序；移动初始化槽不改变施加顺序。</summary>
    public List<int> NumberedColumns(string prefix)
    {
        var ordered = new SortedDictionary<int, int>();
        foreach (var pair in columns)
            if (pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(pair.Key.Substring(prefix.Length), out int slot) && slot > 0)
            {
                if (ordered.ContainsKey(slot))
                    throw new TableValidationException($"[{sheet.Name}] 重复编号表头：{pair.Key}");
                ordered.Add(slot, pair.Value);
            }
        return new List<int>(ordered.Values);
    }

    /// <summary>仅跳过明确的类型行；不再以第一列是否为数字来猜测，文本字段移动到最前也不会漏首行。</summary>
    public int DataStartRow
    {
        get
        {
            bool hasType = false;
            foreach (var pair in columns)
            {
                if (pair.Key.StartsWith("备注", StringComparison.Ordinal) || pair.Key.StartsWith("Notes", StringComparison.OrdinalIgnoreCase)) continue;
                string value = sheet.Cells[2, pair.Value].Text.Trim();
                if (value.Length == 0) continue;
                if (!TypeNames.Contains(value)) return 2;
                hasType = true;
            }
            return hasType ? 3 : 2;
        }
    }
}
