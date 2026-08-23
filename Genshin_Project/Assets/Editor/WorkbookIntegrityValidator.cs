using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OfficeOpenXml;

public enum WorkbookValidationSeverity
{
    Warning,
    Error
}

public sealed class WorkbookValidationIssue
{
    public WorkbookValidationSeverity Severity { get; }
    public string Code { get; }
    public string Location { get; }
    public string Message { get; }

    public WorkbookValidationIssue(
        WorkbookValidationSeverity severity,
        string code,
        string location,
        string message)
    {
        Severity = severity;
        Code = code ?? string.Empty;
        Location = location ?? string.Empty;
        Message = message ?? string.Empty;
    }

    public override string ToString()
    {
        return $"[{Severity}] {Code} {Location}: {Message}";
    }
}

/// <summary>
/// Raw workbook integrity validation. It intentionally reads Excel rows before DataManager
/// converts them to dictionaries so duplicate keys cannot be silently overwritten.
/// </summary>
public static class WorkbookIntegrityValidator
{
    private sealed class TableRow
    {
        public string Workbook;
        public string Sheet;
        public int Number;
        public Dictionary<string, string> Cells;

        public string Location => $"{Workbook}/{Sheet} row {Number}";

        public string Get(string column)
        {
            return Cells.TryGetValue(column, out string value) ? value : string.Empty;
        }

        public bool Has(string column)
        {
            return Cells.ContainsKey(column);
        }
    }

    private static readonly HashSet<string> AllowedEffectTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "AddTurn", "ApplyStatus", "BindStatus", "Buff", "ChangeControl", "Damage", "Debuff",
        "ExecuteEffect", "ExecuteSkill", "GainEnergy", "Heal", "RemoveStatus", "Shield"
    };

    private static readonly HashSet<string> AllowedElements = new HashSet<string>(StringComparer.Ordinal)
    {
        "None", "Physical", "Pyro", "Hydro", "Electro", "Cryo", "Anemo", "Geo", "Dendro"
    };

    private static readonly HashSet<string> AllowedDamageTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "Normal", "Heavy", "Skill", "Burst"
    };

    private static readonly HashSet<string> AllowedTargetTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "Self", "Enemy", "EnemyField", "Allies", "AlliesOnly", "AllyField"
    };

    private static readonly HashSet<string> AllowedSkillActionTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "Normal", "Heavy", "Skill", "Burst"
    };

    private static readonly HashSet<string> AllowedStatusActionTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "OnApply", "OnItsTurn", "OnTrigger", "OnEnd", "OnHit"
    };

    private static readonly HashSet<string> SchemaTypeTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "int", "long", "float", "double", "decimal", "string", "bool", "boolean"
    };

    public static IReadOnlyList<WorkbookValidationIssue> Validate(string dataRoot)
    {
        var issues = new List<WorkbookValidationIssue>();
        if (string.IsNullOrWhiteSpace(dataRoot) || !Directory.Exists(dataRoot))
        {
            issues.Add(new WorkbookValidationIssue(
                WorkbookValidationSeverity.Error,
                "DATA_ROOT_MISSING",
                dataRoot ?? string.Empty,
                "Runtime table directory does not exist."));
            return issues;
        }

        var presentSheets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<TableRow> rows = LoadRows(dataRoot, issues, presentSheets);
        ValidateRequiredSources(presentSheets, issues);

        var seen = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        var characterSkillIds = new HashSet<int>();
        var characterSkillNames = new HashSet<string>(StringComparer.Ordinal);
        var characterEffectIds = new HashSet<int>();
        var characterEffectNames = new HashSet<string>(StringComparer.Ordinal);
        var statusIds = new HashSet<int>();
        var statusNames = new HashSet<string>(StringComparer.Ordinal);
        var statusEffectNames = new HashSet<string>(StringComparer.Ordinal);
        var enemyIds = new HashSet<int>();
        var enemySkillIds = new HashSet<int>();
        var enemyEffectNames = new HashSet<string>(StringComparer.Ordinal);
        var enemyPatterns = new HashSet<string>(StringComparer.Ordinal);
        var weaponIds = new HashSet<int>();
        var weaponSkillIds = new HashSet<int>();

        foreach (TableRow row in rows)
        {
            CollectPrimaryKeys(
                row,
                issues,
                seen,
                characterSkillIds,
                characterSkillNames,
                characterEffectIds,
                characterEffectNames,
                statusIds,
                statusNames,
                statusEffectNames,
                enemyIds,
                enemySkillIds,
                enemyEffectNames,
                enemyPatterns,
                weaponIds,
                weaponSkillIds);
            ValidateEnums(row, issues);
        }

        foreach (TableRow row in rows)
        {
            ValidateReferences(
                row,
                issues,
                characterSkillIds,
                characterSkillNames,
                characterEffectIds,
                characterEffectNames,
                statusIds,
                statusNames,
                statusEffectNames,
                enemyIds,
                enemySkillIds,
                enemyEffectNames,
                enemyPatterns,
                weaponIds,
                weaponSkillIds);
        }

        return issues;
    }

    private static List<TableRow> LoadRows(
        string dataRoot,
        List<WorkbookValidationIssue> issues,
        HashSet<string> presentSheets)
    {
        var rows = new List<TableRow>();
        foreach (string path in Directory.GetFiles(dataRoot, "*.xlsx", SearchOption.AllDirectories)
                     .Where(path => !Path.GetFileName(path).StartsWith("~$", StringComparison.Ordinal))
                     .Where(path => !Path.GetFileNameWithoutExtension(path).StartsWith("0_", StringComparison.Ordinal)))
        {
            string relative = path.Substring(dataRoot.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Replace(Path.DirectorySeparatorChar, '/');
            try
            {
                using var package = new ExcelPackage(new FileInfo(path));
                foreach (ExcelWorksheet sheet in package.Workbook.Worksheets)
                {
                    presentSheets.Add(relative + "|" + sheet.Name);
                    if (sheet.Dimension == null) continue;

                    var headers = new Dictionary<int, string>();
                    for (int column = sheet.Dimension.Start.Column; column <= sheet.Dimension.End.Column; column++)
                    {
                        string header = sheet.Cells[1, column].Text.Trim();
                        if (!string.IsNullOrEmpty(header)) headers[column] = header;
                    }

                    for (int number = 2; number <= sheet.Dimension.End.Row; number++)
                    {
                        var cells = new Dictionary<string, string>(StringComparer.Ordinal);
                        foreach (KeyValuePair<int, string> header in headers)
                            cells[header.Value] = sheet.Cells[number, header.Key].Text.Trim();
                        if (cells.Values.All(string.IsNullOrWhiteSpace) || IsSchemaTypeRow(cells.Values)) continue;
                        rows.Add(new TableRow
                        {
                            Workbook = relative,
                            Sheet = sheet.Name,
                            Number = number,
                            Cells = cells
                        });
                    }
                }
            }
            catch (Exception exception)
            {
                issues.Add(new WorkbookValidationIssue(
                    WorkbookValidationSeverity.Error,
                    "WORKBOOK_UNREADABLE",
                    relative,
                    exception.Message));
            }
        }
        return rows;
    }

    private static bool IsSchemaTypeRow(IEnumerable<string> values)
    {
        string[] nonEmpty = values.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        return nonEmpty.Length > 0 && nonEmpty.All(value => SchemaTypeTokens.Contains(value));
    }

    private static void ValidateRequiredSources(
        HashSet<string> presentSheets,
        List<WorkbookValidationIssue> issues)
    {
        string[] required =
        {
            "Characters.xlsx|Sheet1",
            "Character_Growth_Curve.xlsx|GrowthCurve_1",
            "Character_Growth_Curve.xlsx|GrowthCurve_2",
            "StatusData.xlsx|StatusData_Main",
            "StatusData.xlsx|Status_Effect",
            "StatusData.xlsx|Status_Action",
            "StatusData_Enemy.xlsx|StatusData_Main_Enemy",
            "EnemyAttributes.xlsx|Enemy_Main",
            "EnemySkill.xlsx|EnemySkill_Main",
            "EnemySkill.xlsx|EnemySkill_Effect",
            "EnemySkill.xlsx|EnemySkill_Param",
            "EnemySkill.xlsx|EnemyAI",
            "EnemySkill.xlsx|EnemyAI_Rule",
            "ReactionLevelCoefficient.xlsx|Sheet1"
        };

        foreach (string source in required)
        {
            if (!presentSheets.Contains(source))
            {
                issues.Add(new WorkbookValidationIssue(
                    WorkbookValidationSeverity.Error,
                    "REQUIRED_SHEET_MISSING",
                    source,
                    "Required workbook or sheet is missing."));
            }
        }
    }

    private static void CollectPrimaryKeys(
        TableRow row,
        List<WorkbookValidationIssue> issues,
        Dictionary<string, Dictionary<string, string>> seen,
        HashSet<int> characterSkillIds,
        HashSet<string> characterSkillNames,
        HashSet<int> characterEffectIds,
        HashSet<string> characterEffectNames,
        HashSet<int> statusIds,
        HashSet<string> statusNames,
        HashSet<string> statusEffectNames,
        HashSet<int> enemyIds,
        HashSet<int> enemySkillIds,
        HashSet<string> enemyEffectNames,
        HashSet<string> enemyPatterns,
        HashSet<int> weaponIds,
        HashSet<int> weaponSkillIds)
    {
        bool characterWorkbook = row.Workbook.StartsWith("Characters/", StringComparison.OrdinalIgnoreCase);
        if (characterWorkbook && row.Sheet == "Attributes")
            RegisterUnique(row, "CharacterID", "character.id", issues, seen, (HashSet<int>)null);

        if (characterWorkbook && row.Sheet == "Skills")
        {
            RegisterUnique(row, "SkillID", "character-skill.id", issues, seen, characterSkillIds);
            RegisterUnique(row, "SkillID2", "character-skill.id2", issues, seen, characterSkillNames);
        }
        else if (characterWorkbook && row.Sheet == "SkillsEffect")
        {
            RegisterUnique(row, "SkillEffectID", "character-effect.id", issues, seen, characterEffectIds);
            RegisterUnique(row, "SkillEffectID2", "character-effect.id2", issues, seen, characterEffectNames);
        }

        if (IsStatusMainSheet(row.Sheet))
        {
            RegisterUnique(row, "StatusID", "status.id", issues, seen, statusIds);
            RegisterUnique(row, "StatusID2", "status.id2", issues, seen, statusNames);
        }
        else if (IsStatusEffectSheet(row.Sheet))
        {
            RegisterUnique(row, "StatusEffectID", "status-effect.id", issues, seen, (HashSet<int>)null);
            RegisterUnique(row, "StatusEffectID2", "status-effect.id2", issues, seen, statusEffectNames);
        }

        if (row.Sheet == "Enemy_Main")
            RegisterUnique(row, "EnemyID", "enemy.id", issues, seen, enemyIds);
        else if (row.Sheet == "EnemySkill_Main")
            RegisterUnique(row, "EnemySkillID", "enemy-skill.id", issues, seen, enemySkillIds);
        else if (row.Sheet == "EnemySkill_Effect")
        {
            RegisterUnique(row, "SkillEffectID", "enemy-effect.id", issues, seen, (HashSet<int>)null);
            RegisterUnique(row, "SkillEffectID2", "enemy-effect.id2", issues, seen, enemyEffectNames);
        }
        else if (row.Sheet == "EnemyAI")
        {
            string pattern = row.Get("EnemyID") + ":" + row.Get("PatternID");
            RegisterComposite(row, pattern, "enemy-ai.pattern", issues, seen);
            if (!string.IsNullOrWhiteSpace(pattern)) enemyPatterns.Add(pattern);
        }

        if (row.Workbook.Equals("WeaponAttributes.xlsx", StringComparison.OrdinalIgnoreCase) && row.Sheet == "Attributes")
            RegisterUnique(row, "WeaponID", "weapon.id", issues, seen, weaponIds);
        else if (row.Sheet == "WeaponSkill_Main")
            RegisterUnique(row, "WeaponSkillID", "weapon-skill.id", issues, seen, weaponSkillIds);
        else if (row.Sheet == "WeaponParam")
            RegisterComposite(row, row.Get("id") + ":" + row.Get("index"), "weapon-param.id-index", issues, seen);
    }

    private static void ValidateEnums(TableRow row, List<WorkbookValidationIssue> issues)
    {
        ValidateEnum(row, "EffectType", AllowedEffectTypes, issues);
        ValidateEnum(row, "Element", AllowedElements, issues);
        ValidateEnum(row, "DamageType", AllowedDamageTypes, issues);
        ValidateEnum(row, "TargetType", AllowedTargetTypes, issues);

        if (row.Sheet == "Skills")
            ValidateEnum(row, "ActionType", AllowedSkillActionTypes, issues);
        else if (row.Sheet.IndexOf("Status", StringComparison.OrdinalIgnoreCase) >= 0 && row.Has("ActionType"))
            ValidateEnum(row, "ActionType", AllowedStatusActionTypes, issues);

        if (row.Has("PatternType"))
            ValidateEnum(row, "PatternType", new HashSet<string>(StringComparer.Ordinal) { "Weighted" }, issues);

        if (row.Has("WeaponType") && int.TryParse(row.Get("WeaponType"), out int weaponType) &&
            (weaponType < 0 || weaponType > 4))
        {
            AddIssue(issues, "INVALID_ENUM", row, $"WeaponType has illegal value [{weaponType}].");
        }
    }

    private static void ValidateReferences(
        TableRow row,
        List<WorkbookValidationIssue> issues,
        HashSet<int> characterSkillIds,
        HashSet<string> characterSkillNames,
        HashSet<int> characterEffectIds,
        HashSet<string> characterEffectNames,
        HashSet<int> statusIds,
        HashSet<string> statusNames,
        HashSet<string> statusEffectNames,
        HashSet<int> enemyIds,
        HashSet<int> enemySkillIds,
        HashSet<string> enemyEffectNames,
        HashSet<string> enemyPatterns,
        HashSet<int> weaponIds,
        HashSet<int> weaponSkillIds)
    {
        bool characterWorkbook = row.Workbook.StartsWith("Characters/", StringComparison.OrdinalIgnoreCase);
        if (characterWorkbook && row.Sheet == "Attributes")
        {
            ValidateTextReference(row, "NormalAttackID", characterSkillNames, issues);
            ValidateTextReference(row, "HeavyAttackID", characterSkillNames, issues);
            ValidateTextReference(row, "ElementalSkillID", characterSkillNames, issues);
            ValidateTextReference(row, "ElementalBurstID", characterSkillNames, issues);
        }
        else if (characterWorkbook && row.Sheet == "SkillsEffect" && TryInt(row.Get("SkillEffectID"), out int skillEffectId))
        {
            ValidateNumericReference(row, "SkillEffectID/100", skillEffectId / 100, characterSkillIds, issues);
        }
        else if (characterWorkbook && row.Sheet == "SkillLevel")
        {
            ValidateTextReference(row, "ParamID", characterEffectNames, issues);
        }

        if (IsStatusEffectSheet(row.Sheet) && TryInt(row.Get("StatusEffectID"), out int statusEffectId))
            ValidateNumericReference(row, "StatusEffectID/100", statusEffectId / 100, statusIds, issues);
        if (IsStatusActionSheet(row.Sheet))
            ValidateTextReference(row, "StatusID2", statusNames, issues);

        string effectType = row.Get("EffectType");
        if (effectType == "ApplyStatus")
            ValidateTextReference(row, "Param1", statusNames, issues);
        else if (effectType == "ExecuteEffect")
        {
            string value = row.Get("Param1");
            if (!string.IsNullOrWhiteSpace(value) &&
                !characterEffectNames.Contains(value) &&
                !statusEffectNames.Contains(value) &&
                !enemyEffectNames.Contains(value))
            {
                AddIssue(issues, "INVALID_REFERENCE", row, $"Param1 references missing effect [{value}].");
            }
        }

        if (row.Sheet == "EnemySkill_Effect" && TryInt(row.Get("SkillEffectID"), out int enemyEffectId))
            ValidateNumericReference(row, "SkillEffectID/100", enemyEffectId / 100, enemySkillIds, issues);
        else if (row.Sheet == "EnemySkill_Param")
            ValidateTextReference(row, "SkillEffectID2", enemyEffectNames, issues);
        else if (row.Sheet == "EnemyAI")
            ValidateNumericReference(row, "EnemyID", row.Get("EnemyID"), enemyIds, issues);
        else if (row.Sheet == "EnemyAI_Rule")
        {
            ValidateNumericReference(row, "EnemyID", row.Get("EnemyID"), enemyIds, issues);
            ValidateNumericReference(row, "SkillID", row.Get("SkillID"), enemySkillIds, issues);
            string pattern = row.Get("EnemyID") + ":" + row.Get("PatternID");
            if (!enemyPatterns.Contains(pattern))
                AddIssue(issues, "MISSING_RELATED_DATA", row, $"AI rule references missing pattern [{pattern}].");
        }

        if (row.Sheet == "WeaponParam")
            ValidateNumericReference(row, "id", row.Get("id"), weaponIds, issues);
        else if (row.Sheet == "WeaponSkill_Effect" && TryInt(row.Get("SkillEffectID"), out int weaponEffectId))
            ValidateNumericReference(row, "SkillEffectID/100", weaponEffectId / 100, weaponSkillIds, issues);
    }

    private static bool IsStatusMainSheet(string sheet)
    {
        return sheet == "StatusData_Main" || sheet == "StatusData_Main_Enemy" ||
               sheet == "StatusData_Overall_Main" || sheet == "StatusWeapon_Main";
    }

    private static bool IsStatusEffectSheet(string sheet)
    {
        return sheet == "Status_Effect" || sheet == "StatusEffect_Overall" || sheet == "StatusWeapon_Effect";
    }

    private static bool IsStatusActionSheet(string sheet)
    {
        return sheet == "Status_Action" || sheet == "Status_Action_Enemy" ||
               sheet == "StatusAction_Overall" || sheet == "StatusWeapon_Action";
    }

    private static void RegisterUnique(
        TableRow row,
        string column,
        string keySpace,
        List<WorkbookValidationIssue> issues,
        Dictionary<string, Dictionary<string, string>> seen,
        HashSet<int> numericValues)
    {
        string value = row.Get(column);
        if (string.IsNullOrWhiteSpace(value)) return;
        RegisterComposite(row, value, keySpace, issues, seen);
        if (numericValues != null && TryInt(value, out int number)) numericValues.Add(number);
    }

    private static void RegisterUnique(
        TableRow row,
        string column,
        string keySpace,
        List<WorkbookValidationIssue> issues,
        Dictionary<string, Dictionary<string, string>> seen,
        HashSet<string> textValues)
    {
        string value = row.Get(column);
        if (string.IsNullOrWhiteSpace(value)) return;
        RegisterComposite(row, value, keySpace, issues, seen);
        textValues?.Add(value);
    }

    private static void RegisterComposite(
        TableRow row,
        string value,
        string keySpace,
        List<WorkbookValidationIssue> issues,
        Dictionary<string, Dictionary<string, string>> seen)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (!seen.TryGetValue(keySpace, out Dictionary<string, string> values))
        {
            values = new Dictionary<string, string>(StringComparer.Ordinal);
            seen[keySpace] = values;
        }
        if (values.TryGetValue(value, out string firstLocation))
        {
            AddIssue(issues, "DUPLICATE_ID", row,
                $"Duplicate [{value}] in {keySpace}; first declared at {firstLocation}.");
        }
        else values[value] = row.Location;
    }

    private static void ValidateEnum(
        TableRow row,
        string column,
        HashSet<string> allowed,
        List<WorkbookValidationIssue> issues)
    {
        if (!row.Has(column)) return;
        string value = row.Get(column);
        if (!string.IsNullOrWhiteSpace(value) && !allowed.Contains(value))
            AddIssue(issues, "INVALID_ENUM", row, $"{column} has illegal value [{value}].");
    }

    private static void ValidateTextReference(
        TableRow row,
        string column,
        HashSet<string> valid,
        List<WorkbookValidationIssue> issues)
    {
        string value = row.Get(column);
        if (!string.IsNullOrWhiteSpace(value) && !valid.Contains(value))
            AddIssue(issues, "INVALID_REFERENCE", row, $"{column} references missing value [{value}].");
    }

    private static void ValidateNumericReference(
        TableRow row,
        string column,
        string value,
        HashSet<int> valid,
        List<WorkbookValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (!TryInt(value, out int number))
        {
            AddIssue(issues, "INVALID_REFERENCE", row, $"{column} must be an integer reference, got [{value}].");
            return;
        }
        ValidateNumericReference(row, column, number, valid, issues);
    }

    private static void ValidateNumericReference(
        TableRow row,
        string column,
        int value,
        HashSet<int> valid,
        List<WorkbookValidationIssue> issues)
    {
        if (!valid.Contains(value))
            AddIssue(issues, "INVALID_REFERENCE", row, $"{column} references missing value [{value}].");
    }

    private static bool TryInt(string value, out int result)
    {
        return int.TryParse(value, out result);
    }

    private static void AddIssue(
        List<WorkbookValidationIssue> issues,
        string code,
        TableRow row,
        string message)
    {
        issues.Add(new WorkbookValidationIssue(
            WorkbookValidationSeverity.Error,
            code,
            row.Location,
            message));
    }
}
