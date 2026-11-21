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

    private static readonly HashSet<string> AllowedElements = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "None", "Physical", "True", "Pyro", "Hydro", "Electro", "Cryo", "Anemo", "Geo", "Dendro"
    };

    private static readonly HashSet<string> AllowedDamageTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "Normal", "Heavy", "Skill", "Burst"
    };

    private static readonly HashSet<string> AllowedTargetTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "Self", "Enemy", "EnemyField", "Ally", "Allies", "AlliesOnly", "AllyField", "AlliesField", "Field",
        "AllyTeam", "EnemyTeam"
    };

    private static readonly HashSet<string> AllowedSkillActionTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "Normal", "Heavy", "Skill", "Burst"
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

        foreach (string message in DataSourcePreflightValidator.ValidatePresence(dataRoot))
            issues.Add(new WorkbookValidationIssue(WorkbookValidationSeverity.Error, "REQUIRED_SOURCE_MISSING", dataRoot, message));
        List<TableRow> rows = LoadRows(dataRoot, issues);
        NormalizeEquipmentKeys(rows);

        var seen = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        var characterSkillIds = new HashSet<int>();
        var characterSkillNames = new HashSet<string>(StringComparer.Ordinal);
        var characterEffectNames = new HashSet<string>(StringComparer.Ordinal);
        var statusIds = new HashSet<int>();
        var statusNames = new HashSet<string>(StringComparer.Ordinal);
        var statusEffectNames = new HashSet<string>(StringComparer.Ordinal);
        var enemyIds = new HashSet<int>();
        var enemySkillIds = new HashSet<int>();
        var enemyEffectNames = new HashSet<string>(StringComparer.Ordinal);
        var enemyPatterns = new HashSet<string>(StringComparer.Ordinal);
        var weaponIds = new HashSet<int>();

        var directlyCalledEffects = new HashSet<string>(StringComparer.Ordinal);
        foreach (TableRow row in rows)
        {
            if (IsStatusActionSheet(row.Sheet) || row.Get("EffectType") == "ExecuteEffect")
                foreach (string token in row.Get("Param1").Split(';'))
                    if (!string.IsNullOrWhiteSpace(token)) directlyCalledEffects.Add(token.Trim());
            CollectPrimaryKeys(
                row,
                issues,
                seen,
                characterSkillIds,
                characterSkillNames,
                characterEffectNames,
                statusIds,
                statusNames,
                statusEffectNames,
                enemyIds,
                enemySkillIds,
                enemyEffectNames,
                enemyPatterns,
                weaponIds);
            ValidateEnums(row, issues);
        }

        foreach (TableRow row in rows)
        {
            ValidateReferences(
                row,
                issues,
                directlyCalledEffects,
                characterSkillIds,
                characterSkillNames,
                characterEffectNames,
                statusIds,
                statusNames,
                statusEffectNames,
                enemyIds,
                enemySkillIds,
                enemyEffectNames,
                enemyPatterns,
                weaponIds);
        }

        return issues;
    }

    private static List<TableRow> LoadRows(
        string dataRoot,
        List<WorkbookValidationIssue> issues)
    {
        var rows = new List<TableRow>();
        foreach (string path in Directory.GetFiles(dataRoot, "*.xlsx", SearchOption.AllDirectories)
                     .Where(DataSourcePreflightValidator.IsPublishedWorkbook))
        {
            string relative = path.Substring(dataRoot.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Replace(Path.DirectorySeparatorChar, '/');
            if (string.Equals(relative, "StatusData.xlsx", StringComparison.OrdinalIgnoreCase)
                || string.Equals(relative, "StatusData_Enemy.xlsx", StringComparison.OrdinalIgnoreCase)
                || string.Equals(relative, "EnemySkill.xlsx", StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                using var package = new ExcelPackage(new FileInfo(path));
                var missing = new List<string>();
                DataSourcePreflightValidator.CollectMissingSheets(package, relative, missing);
                foreach (string message in missing)
                    issues.Add(new WorkbookValidationIssue(WorkbookValidationSeverity.Error, "REQUIRED_SHEET_MISSING", relative, message));
                foreach (ExcelWorksheet sheet in package.Workbook.Worksheets)
                {
                    if (sheet.Dimension == null || !IsAuditedSheet(relative, sheet.Name)) continue;
                    var headers = ExcelTableHeaders.For(sheet);
                    for (int number = headers.DataStartRow; number <= sheet.Dimension.End.Row; number++)
                    {
                        var cells = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var header in headers.Columns)
                            cells[header.Key] = sheet.Cells[number, header.Value].Text.Trim();
                        if (!cells.Any(cell => cell.Value.Length > 0
                            && !cell.Key.StartsWith("备注", StringComparison.Ordinal)
                            && !cell.Key.StartsWith("Notes", StringComparison.OrdinalIgnoreCase))) continue;
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

    private static bool IsAuditedSheet(string workbook, string sheet)
    {
        if (workbook.StartsWith("Characters/", StringComparison.OrdinalIgnoreCase))
            return sheet is "Attributes" or "Skills" or "SkillsEffect";
        if (workbook.StartsWith("Enemies/", StringComparison.OrdinalIgnoreCase))
            return sheet is "Enemy_Main" or "EnemySkill_Main" or "EnemySkill_Effect" or "EnemySkill_Param" or "EnemyAI" or "EnemyAI_Rule";
        if (workbook == "WeaponAttributes.xlsx") return sheet is "Attributes" or "WeaponParam";
        if (workbook.StartsWith("CharacterStatus/", StringComparison.OrdinalIgnoreCase)
            || workbook.StartsWith("EnemyStatus/", StringComparison.OrdinalIgnoreCase)
            || workbook is "StatusData_Overall.xlsx" or "StatusData_Artifacts.xlsx" or "StatusData_Weapon.xlsx")
            return IsStatusMainSheet(sheet) || IsStatusActionSheet(sheet) || IsStatusEffectSheet(sheet);
        return false;
    }

    private static void NormalizeEquipmentKeys(List<TableRow> rows)
    {
        var artifactStatuses = new Dictionary<int, string>();
        foreach (var row in rows)
        {
            bool weapon = row.Workbook == "StatusData_Weapon.xlsx";
            if (!weapon && row.Workbook != "StatusData_Artifacts.xlsx") continue;
            if (IsStatusMainSheet(row.Sheet) && TryInt(row.Get("StatusID"), out int status))
            {
                if (weapon) row.Cells["StatusID2"] = DataManager.WeaponStatusKey(status);
                else
                {
                    if (row.Get("StatusID2").Length == 0) row.Cells["StatusID2"] = DataManager.ArtifactStatusKey(status);
                    artifactStatuses[status] = row.Get("StatusID2");
                }
            }
            if (IsStatusEffectSheet(row.Sheet) && TryInt(row.Get("StatusEffectID"), out int effect))
            {
                if (weapon) row.Cells["StatusEffectID2"] = DataManager.WeaponEffectKey(effect);
                else if (row.Get("StatusEffectID2").Length == 0) row.Cells["StatusEffectID2"] = DataManager.ArtifactEffectKey(effect);
            }
        }
        string ArtifactStatus(string value) => DataManager.ResolveArtifactReference(
            CountStatusSystem.ResolvePublicStatusReference(value), artifactStatuses, DataManager.ArtifactStatusKey);
        foreach (var row in rows)
        {
            if (row.Workbook == "StatusData_Weapon.xlsx")
            {
                if (IsStatusActionSheet(row.Sheet) && TryInt(row.Get("StatusID"), out int id))
                    row.Cells["StatusID2"] = DataManager.WeaponStatusKey(id);
                if (row.Get("EffectType") == "ApplyStatus")
                    row.Cells["Param1"] = DataManager.NormalizeWeaponEffectParam("ApplyStatus", row.Get("Param1"));
            }
            else if (row.Workbook == "StatusData_Artifacts.xlsx")
            {
                if (IsStatusActionSheet(row.Sheet))
                    row.Cells["StatusID2"] = ArtifactStatus(row.Get("StatusID2").Length > 0 ? row.Get("StatusID2") : row.Get("StatusID"));
                if (row.Get("EffectType") == "ApplyStatus")
                    row.Cells["Param1"] = string.Join(";", row.Get("Param1").Split(';').Select(ArtifactStatus));
            }
        }
    }


    private static void CollectPrimaryKeys(
        TableRow row,
        List<WorkbookValidationIssue> issues,
        Dictionary<string, Dictionary<string, string>> seen,
        HashSet<int> characterSkillIds,
        HashSet<string> characterSkillNames,
        HashSet<string> characterEffectNames,
        HashSet<int> statusIds,
        HashSet<string> statusNames,
        HashSet<string> statusEffectNames,
        HashSet<int> enemyIds,
        HashSet<int> enemySkillIds,
        HashSet<string> enemyEffectNames,
        HashSet<string> enemyPatterns,
        HashSet<int> weaponIds)
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
            RegisterUnique(row, "SkillEffectID", "character-effect.id", issues, seen, (HashSet<int>)null);
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
        {
            string phase = string.IsNullOrWhiteSpace(row.Get("Phase")) ? "0" : row.Get("Phase");
            RegisterComposite(row, row.Get("EnemyID") + ":" + phase, "enemy.id-phase", issues, seen);
            if (TryInt(row.Get("EnemyID"), out int enemyID)) enemyIds.Add(enemyID);
        }
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
        else if (row.Sheet == "WeaponParam")
            RegisterComposite(row, row.Get("WeaponID") + ":" + row.Get("index"), "weapon-param.id-index", issues, seen);
    }

    private static void ValidateEnums(TableRow row, List<WorkbookValidationIssue> issues)
    {
        string effect = row.Get("EffectType");
        try
        {
            if (row.Has("EffectType") && (IsStatusEffectSheet(row.Sheet) || row.Sheet is "SkillsEffect" or "EnemySkill_Effect"))
                TableValidator.ValidateEffectType(row.Sheet, row.Number, effect);
            if (IsStatusActionSheet(row.Sheet) && row.Has("ActionType"))
                TableValidator.ValidateStatusActionType(row.Sheet, row.Number, row.Get("ActionType"));
            if (DamageImmunity.IsImmuneType(effect)) DamageImmunity.ParseElements(row.Get("Element"));
            else ValidateEnum(row, "Element", AllowedElements, issues);
        }
        catch (TableValidationException error) { AddIssue(issues, "INVALID_ENUM", row, error.Message); }
        ValidateEnum(row, "DamageType", AllowedDamageTypes, issues);
        ValidateEnum(row, "TargetType", AllowedTargetTypes, issues);

        if (row.Sheet == "Skills")
            ValidateEnum(row, "ActionType", AllowedSkillActionTypes, issues);

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
        HashSet<string> directlyCalledEffects,
        HashSet<int> characterSkillIds,
        HashSet<string> characterSkillNames,
        HashSet<string> characterEffectNames,
        HashSet<int> statusIds,
        HashSet<string> statusNames,
        HashSet<string> statusEffectNames,
        HashSet<int> enemyIds,
        HashSet<int> enemySkillIds,
        HashSet<string> enemyEffectNames,
        HashSet<string> enemyPatterns,
        HashSet<int> weaponIds)
    {
        bool characterWorkbook = row.Workbook.StartsWith("Characters/", StringComparison.OrdinalIgnoreCase);
        if (characterWorkbook && row.Sheet == "Attributes")
        {
            ValidateTextReference(row, "NormalAttackID", characterSkillNames, issues);
            ValidateTextReference(row, "HeavyAttackID", characterSkillNames, issues);
            ValidateTextReference(row, "ElementalSkillID", characterSkillNames, issues);
            ValidateTextReference(row, "ElementalBurstID", characterSkillNames, issues);
        }
        else if (characterWorkbook && row.Sheet == "SkillsEffect" && TryInt(row.Get("SkillEffectID"), out int skillEffectId)
            && !directlyCalledEffects.Contains(row.Get("SkillEffectID2")))
        {
            ValidateNumericReference(row, "SkillEffectID/100", skillEffectId / 100, characterSkillIds, issues);
        }
        // SkillLevel.ParamID is an extensible parameter key, not a restricted effect reference.

        if (IsStatusEffectSheet(row.Sheet) && TryInt(row.Get("StatusEffectID"), out int statusEffectId))
            ValidateNumericReference(row, "StatusEffectID/100", statusEffectId / 100, statusIds, issues);
        if (IsStatusActionSheet(row.Sheet))
            ValidateTextReference(row, "StatusID2", statusNames, issues);

        string effectType = row.Get("EffectType");
        if (effectType == "ApplyStatus")
        {
            foreach (string status in row.Get("Param1").Split(';').Select(token => token.Trim()))
                if (status.Length > 0 && !statusNames.Contains(status))
                    AddIssue(issues, "INVALID_REFERENCE", row, $"Param1 references missing status [{status}].");
        }
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
            ValidateNumericReference(row, "WeaponID", row.Get("WeaponID"), weaponIds, issues);
    }

    private static bool IsStatusMainSheet(string sheet)
    {
        return sheet == "StatusData_Main" || sheet == "StatusData_Main_Enemy" ||
               sheet == "StatusData_Overall_Main" || sheet == "StatusData_Main_Artifacts" || sheet == "StatusWeapon_Main";
    }

    private static bool IsStatusEffectSheet(string sheet)
    {
        return sheet == "Status_Effect" || sheet == "StatusEffect_Overall" || sheet == "StatusWeapon_Effect";
    }

    private static bool IsStatusActionSheet(string sheet)
    {
        return sheet == "Status_Action" || sheet == "Status_Action_Enemy" ||
               sheet == "StatusAction_Overall" || sheet == "Status_Action_Artifacts" || sheet == "StatusWeapon_Action";
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
