using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OfficeOpenXml;
using UnityEngine;

/// <summary>One enemy appearance workbook: committed mode base plus explicitly configured personal-status overrides.</summary>
public sealed class EnemyAppearanceTable
{
    public const string FileName = CharacterCardSwitchTable.FileName;
    public const string SheetName = "Enemy";
    private readonly Dictionary<int, Dictionary<int, int>> entries = new Dictionary<int, Dictionary<int, int>>();
    private CharacterCardSwitchTable statusOverrides;
    public IEnumerable<int> ConfiguredEnemies => entries.Keys.Concat(statusOverrides?.EntityIDs ?? Enumerable.Empty<int>()).Distinct();
    public bool Contains(int enemy) => entries.ContainsKey(enemy) || (statusOverrides?.Contains(enemy) ?? false);
    public bool TryResolve(int enemy, int pattern, out int variant)
    {
        variant = 0;
        return entries.TryGetValue(enemy, out var modes) && modes.TryGetValue(pattern, out variant);
    }
    public bool TryResolve(int enemy, int pattern, Func<int, bool> hasActiveStatus, out int variant)
    {
        // State conditions share the character parser. No mode, stun or recovery is mutated by UI.
        if (statusOverrides != null && statusOverrides.TryResolveCondition(enemy, hasActiveStatus, out variant)) return true;
        if (entries.ContainsKey(enemy)) return TryResolve(enemy, pattern, out variant);
        if (statusOverrides?.Contains(enemy) ?? false) { variant = statusOverrides.DefaultVariant(enemy); return true; }
        variant = 0; return false;
    }
    public bool TryResolve(BattleEntity entity, out int variant) => TryResolve(entity.EntityID,
        entity.EnemyCtrl?.CurrentPatternID ?? 0, id => CharacterCardSwitchTable.HasActivePersonalStatus(entity, id), out variant);
    public IEnumerable<int> Variants(int enemy)
    {
        var variants = entries.TryGetValue(enemy, out var modes) ? new HashSet<int>(modes.Values) : new HashSet<int>();
        if (statusOverrides?.Contains(enemy) ?? false) variants.UnionWith(statusOverrides.Variants(enemy));
        return variants;
    }
    public static EnemyAppearanceTable Load() => CardAppearanceSwitchTables.Load().Enemies;
    internal void ValidatePublishedReferences(string art)
    {
        var data = DataManager.Instance;
        if (data != null && data.IsLoaded)
            Validate(art, id => data.EnemyAIDict.TryGetValue(id, out var modes)
                ? modes.Select(mode => mode.PatternID).ToHashSet() : null);
        else ValidateRuntimeReferences(art);
        if (data != null && data.IsLoaded) ValidateStatusReferences(art, data.StatusMainDict.Values.Select(status => status.StatusID).ToHashSet());
    }
    public static EnemyAppearanceTable ReadFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var package = new ExcelPackage(stream);
        return ReadSheet(package.Workbook.Worksheets[SheetName]);
    }
    public static EnemyAppearanceTable ReadSheet(ExcelWorksheet sheet)
    {
        if (sheet?.Dimension == null) throw new InvalidDataException("卡面切换表缺少 Enemy 子表。");
        var h = ExcelTableHeaders.For(sheet); h.Require("EnemyID", "StatusID", "PatternID", "CardID");
        var table = new EnemyAppearanceTable();
        for (int row = h.DataStartRow; row <= sheet.Dimension.End.Row; row++)
        {
            string patternText = h.Cell(row, "PatternID").Text.Trim();
            if (patternText.Length == 0) continue; // Defaults and status rows use the shared condition parser below.
            if (h.Cell(row, "StatusID").Text.Trim().Length > 0)
                throw new InvalidDataException($"Enemy第{row}行不能同时填写StatusID与PatternID。");
            int Number(string key)
            {
                if (!int.TryParse(h.Cell(row, key).Text.Trim(), out int n) || n <= 0)
                    throw new InvalidDataException($"外观总表第{row}行{key}须为正整数。");
                return n;
            }
            int enemy = Number("EnemyID"), pattern = Number("PatternID");
            string card = h.Cell(row, "CardID").Text.Trim(), prefix = enemy + "_";
            if (!card.StartsWith(prefix, StringComparison.Ordinal) || !int.TryParse(card.Substring(prefix.Length), out int variant)
                || variant <= 0 || card != prefix + variant.ToString("D2"))
                throw new InvalidDataException($"Enemy第{row}行CardID须为{enemy}_01这样的完整编号。");
            if (!table.entries.TryGetValue(enemy, out var modes)) table.entries.Add(enemy, modes = new Dictionary<int, int>());
            if (modes.ContainsKey(pattern)) throw new InvalidDataException($"外观总表重复：{enemy}/模式{pattern}。");
            modes.Add(pattern, variant);
        }
        table.statusOverrides = CharacterCardSwitchTable.ReadSheet(sheet, "EnemyID", row => h.Cell(row, "PatternID").Text.Trim().Length == 0);
        foreach (int enemy in table.entries.Keys)
            if (!table.statusOverrides.Contains(enemy)) throw new InvalidDataException($"敌人{enemy}缺少默认卡面行。");
        return table;
    }
    // Data always comes from published runtime packages, including validation invoked by Editor tools.
    public void ValidateRuntimeReferences(string artRoot)
    {
        var available = new Dictionary<int, HashSet<int>>();
        string root = Path.Combine(Application.streamingAssetsPath, "Data", "Enemies");
        foreach (string file in Directory.EnumerateFiles(root, "*.xlsx"))
        {
            string name = Path.GetFileName(file);
            if (name.StartsWith("~$") || name.StartsWith("#") || name.StartsWith("0_")) continue;
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var package = new ExcelPackage(stream);
            var sheet = package.Workbook.Worksheets["EnemyAI"];
            if (sheet?.Dimension == null) continue;
            var h = ExcelTableHeaders.For(sheet); h.Require("EnemyID", "PatternID");
            for (int row = h.DataStartRow; row <= sheet.Dimension.End.Row; row++)
            {
                if (!int.TryParse(h.Cell(row, "EnemyID").Text.Trim(), out int enemy) || !entries.ContainsKey(enemy)) continue;
                if (!available.TryGetValue(enemy, out var modes)) available.Add(enemy, modes = new HashSet<int>());
                if (int.TryParse(h.Cell(row, "PatternID").Text.Trim(), out int mode)) modes.Add(mode);
            }
        }
        Validate(artRoot, id => available.TryGetValue(id, out var modes) ? modes : null);
        if (statusOverrides != null)
        {
            var statuses = new HashSet<int>();
            string data = Path.Combine(Application.streamingAssetsPath, "Data");
            var files = new[] { Path.Combine(data, "StatusData_Overall.xlsx") }
                .Concat(Directory.EnumerateFiles(Path.Combine(data, "EnemyStatus"), "*.xlsx"));
            foreach (string file in files)
            {
                if (!DataSourcePreflightValidator.IsPublishedWorkbook(file)) continue;
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var package = new ExcelPackage(stream);
                foreach (var sheet in package.Workbook.Worksheets)
                {
                    if (!sheet.Name.EndsWith("_Main", StringComparison.Ordinal)) continue;
                    var headers = ExcelTableHeaders.For(sheet); int column = headers.Optional("StatusID");
                    if (column < 1) continue;
                    for (int row = headers.DataStartRow; row <= (sheet.Dimension?.End.Row ?? 0); row++)
                        if (int.TryParse(sheet.Cells[row, column].Text, out int id)) statuses.Add(id);
                }
            }
            ValidateStatusReferences(artRoot, statuses);
        }
    }

    private void Validate(string artRoot, Func<int, HashSet<int>> patterns)
    {
        foreach (var enemy in entries)
        {
            var modes = patterns(enemy.Key);
            if (modes == null) throw new InvalidDataException("外观总表敌人没有已发布行为模式：" + enemy.Key);
            if (!modes.SetEquals(enemy.Value.Keys)) throw new InvalidDataException($"外观总表{enemy.Key}必须且只能覆盖该敌人已发布的全部模式。");
            foreach (int variant in new HashSet<int>(enemy.Value.Values))
                if (!File.Exists(Path.Combine(artRoot, "Enemy", $"{enemy.Key}_{variant:D2}.png")))
                    throw new FileNotFoundException($"外观总表缺少差分素材：Enemy/{enemy.Key}_{variant:D2}.png");
        }
    }

    private void ValidateStatusReferences(string artRoot, HashSet<int> knownStatuses)
    {
        if (statusOverrides == null) return;
        // The public enemy registry includes future enemies whose AI packages are not implemented yet.
        string registry = Path.Combine(Application.streamingAssetsPath, "Data", "Enemy.xlsx");
        using var stream = new FileStream(registry, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var package = new ExcelPackage(stream);
        var sheet = package.Workbook.Worksheets.First(); var h = ExcelTableHeaders.For(sheet);
        string idHeader = h.Optional("EnemyID") > 0 ? "EnemyID" : "顺序ID"; h.Require(idHeader);
        var ids = new HashSet<int>();
        for (int row = h.DataStartRow; row <= (sheet.Dimension?.End.Row ?? 0); row++)
            if (int.TryParse(h.Cell(row, idHeader).Text, out int id)) ids.Add(id);
        foreach (int enemy in statusOverrides.EntityIDs)
        {
            if (!ids.Contains(enemy)) throw new InvalidDataException("状态卡面切换的敌人未在已发布Enemy.xlsx登记：" + enemy);
            foreach (int status in statusOverrides.ConditionStatuses(enemy))
                if (!knownStatuses.Contains(status)) throw new InvalidDataException($"敌人{enemy}的状态卡面条件不存在：{status}。");
            foreach (int variant in statusOverrides.Variants(enemy))
                if (!File.Exists(Path.Combine(artRoot, "Enemy", $"{enemy}_{variant:D2}.png")))
                    throw new FileNotFoundException($"状态卡面切换缺少素材：Enemy/{enemy}_{variant:D2}.png。");
        }
    }
}
