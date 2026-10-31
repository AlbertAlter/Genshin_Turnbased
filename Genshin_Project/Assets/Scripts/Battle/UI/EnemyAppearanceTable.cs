using System;
using System.Collections.Generic;
using System.IO;
using OfficeOpenXml;
using UnityEngine;

/// <summary>One authoritative enemy/mode-to-static-variant mapping. No state predicates or priorities.</summary>
public sealed class EnemyAppearanceTable
{
    public const string FileName = "EnemyAppearance.xlsx";
    private readonly Dictionary<int, Dictionary<int, int>> entries = new Dictionary<int, Dictionary<int, int>>();
    public bool Contains(int enemy) => entries.ContainsKey(enemy);
    public bool TryResolve(int enemy, int pattern, out int variant)
    {
        variant = 0;
        return entries.TryGetValue(enemy, out var modes) && modes.TryGetValue(pattern, out variant);
    }
    public IEnumerable<int> Variants(int enemy) => entries.TryGetValue(enemy, out var modes)
        ? new HashSet<int>(modes.Values) : new HashSet<int>();
    public static EnemyAppearanceTable Load()
    {
        var table = ReadFile(Path.Combine(Application.streamingAssetsPath, "Data", "UI", FileName));
        table.ValidateRuntimeReferences(Path.Combine(Application.streamingAssetsPath, "art_assets"));
        return table;
    }
    public static EnemyAppearanceTable ReadFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var package = new ExcelPackage(stream);
        var sheet = package.Workbook.Worksheets["EnemyAppearance"];
        if (sheet?.Dimension == null) throw new InvalidDataException("外观总表缺少 EnemyAppearance 子表。");
        var h = ExcelTableHeaders.For(sheet); h.Require("EnemyID", "PatternID", "Variant");
        var table = new EnemyAppearanceTable();
        for (int row = h.DataStartRow; row <= sheet.Dimension.End.Row; row++)
        {
            if (h.Cell(row, "EnemyID").Text.Trim().Length == 0) continue;
            int Number(string key)
            {
                if (!int.TryParse(h.Cell(row, key).Text.Trim(), out int n) || n <= 0)
                    throw new InvalidDataException($"外观总表第{row}行{key}须为正整数。");
                return n;
            }
            int enemy = Number("EnemyID"), pattern = Number("PatternID"), variant = Number("Variant");
            if (!table.entries.TryGetValue(enemy, out var modes)) table.entries.Add(enemy, modes = new Dictionary<int, int>());
            if (modes.ContainsKey(pattern)) throw new InvalidDataException($"外观总表重复：{enemy}/模式{pattern}。");
            modes.Add(pattern, variant);
        }
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
        foreach (var enemy in entries)
        {
            if (!available.TryGetValue(enemy.Key, out var modes)) throw new InvalidDataException("外观总表敌人没有已发布行为模式：" + enemy.Key);
            if (!modes.SetEquals(enemy.Value.Keys)) throw new InvalidDataException($"外观总表{enemy.Key}必须且只能覆盖该敌人已发布的全部模式。");
            foreach (int variant in new HashSet<int>(enemy.Value.Values))
                if (!File.Exists(Path.Combine(artRoot, "Enemy", $"{enemy.Key}_{variant:D2}.png")))
                    throw new FileNotFoundException($"外观总表缺少差分素材：Enemy/{enemy.Key}_{variant:D2}.png");
        }
    }
}
