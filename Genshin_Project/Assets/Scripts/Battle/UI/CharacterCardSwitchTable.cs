using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using OfficeOpenXml;
using UnityEngine;

/// <summary>Configured status/card identities. The same condition parser is used by character and enemy sheets.</summary>
public sealed class CharacterCardSwitchTable
{
    public const string FileName = CardAppearanceSwitchTables.FileName;
    public const string SheetName = "Character";
    private sealed class Rule { public int Status, Variant; }
    private sealed class CharacterRules
    {
        public int Default;
        public readonly List<Rule> Conditions = new List<Rule>();
    }
    private readonly Dictionary<int, CharacterRules> entries = new Dictionary<int, CharacterRules>();
    public IEnumerable<int> EntityIDs => entries.Keys;
    public bool Contains(int character) => entries.ContainsKey(character);
    public int DefaultVariant(int character) => entries[character].Default;
    public IEnumerable<int> Variants(int character) => new[] { entries[character].Default }
        .Concat(entries[character].Conditions.Select(rule => rule.Variant)).Distinct();

    public int Resolve(int character, Func<int, bool> hasActiveStatus)
    {
        return TryResolveCondition(character, hasActiveStatus, out int variant) ? variant : entries[character].Default;
    }
    public bool TryResolveCondition(int entity, Func<int, bool> hasActiveStatus, out int variant)
    {
        variant = 0;
        if (!entries.TryGetValue(entity, out var rules)) return false;
        foreach (var rule in rules.Conditions)
            if (hasActiveStatus(rule.Status)) { variant = rule.Variant; return true; }
        return false;
    }
    public IEnumerable<int> ConditionStatuses(int entity) => entries[entity].Conditions.Select(rule => rule.Status);
    public static bool HasActivePersonalStatus(BattleEntity entity, int id) => entity.StatusDict.Values.Any(status =>
        status != null && status.IsActive && ReferenceEquals(status.Host, entity) && status.MainData?.StatusID == id);
    public int Resolve(BattleEntity entity) => Resolve(entity.EntityID, id => HasActivePersonalStatus(entity, id));

    public static CharacterCardSwitchTable Load() => CardAppearanceSwitchTables.Load().Characters;
    internal void ValidatePublishedReferences(string art)
    {
        var data = DataManager.Instance;
        if (data != null && data.IsLoaded)
            Validate(art, id => data.CharacterStatusIDs.TryGetValue(id, out var keys)
                ? keys.Select(key => data.StatusMainDict[key].StatusID).ToHashSet() : new HashSet<int>());
        else ValidateRuntimeReferences(art);
    }
    public static CharacterCardSwitchTable ReadFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var package = new ExcelPackage(stream);
        var sheet = package.Workbook.Worksheets[SheetName];
        if (sheet?.Dimension == null) throw new InvalidDataException("卡面切换表缺少 Character 子表。");
        return ReadSheet(sheet, "CharacterID");
    }

    /// <summary>Shared header-based condition grammar; callers keep their own workbook and reference validation.</summary>
    public static CharacterCardSwitchTable ReadSheet(ExcelWorksheet sheet, string entityHeader, Func<int, bool> includeRow = null)
    {
        if (sheet?.Dimension == null) throw new InvalidDataException("状态卡面切换子表为空。");
        var headers = ExcelTableHeaders.For(sheet);
        headers.Require(entityHeader, "StatusID", "CardID");
        string owner = entityHeader == "CharacterID" ? "角色" : "敌人";
        var table = new CharacterCardSwitchTable();
        for (int row = headers.DataStartRow; row <= sheet.Dimension.End.Row; row++)
        {
            if (includeRow != null && !includeRow(row)) continue;
            string characterText = headers.Cell(row, entityHeader).Text.Trim();
            string statusText = headers.Cell(row, "StatusID").Text.Trim();
            string card = headers.Cell(row, "CardID").Text.Trim();
            if (characterText.Length == 0 && statusText.Length == 0 && card.Length == 0) continue;
            bool Positive(string value, out int number) => int.TryParse(value, NumberStyles.None,
                CultureInfo.InvariantCulture, out number) && number > 0;
            if (!Positive(characterText, out int character))
                throw new InvalidDataException($"卡面切换表第{row}行 {entityHeader} 须为正整数。");
            string prefix = character.ToString(CultureInfo.InvariantCulture) + "_";
            if (!card.StartsWith(prefix, StringComparison.Ordinal) ||
                !Positive(card.Substring(prefix.Length), out int variant) ||
                card != prefix + variant.ToString("D2", CultureInfo.InvariantCulture))
                throw new InvalidDataException($"卡面切换表第{row}行 CardID 应为 {character}_01 这样的完整编号。");
            if (!table.entries.TryGetValue(character, out var rules))
                table.entries.Add(character, rules = new CharacterRules());
            if (statusText.Length == 0)
            {
                if (rules.Default != 0) throw new InvalidDataException($"{owner}{character}重复配置默认卡面。");
                rules.Default = variant;
            }
            else
            {
                if (!Positive(statusText, out int status))
                    throw new InvalidDataException($"卡面切换表第{row}行 StatusID 须为状态数字编号或留空。");
                if (rules.Conditions.Any(rule => rule.Status == status))
                    throw new InvalidDataException($"{owner}{character}重复配置状态{status}。");
                rules.Conditions.Add(new Rule { Status = status, Variant = variant });
            }
        }
        foreach (var pair in table.entries)
            if (pair.Value.Default == 0) throw new InvalidDataException($"{owner}{pair.Key}缺少 StatusID 留空的默认卡面行。");
        return table;
    }
    public void ValidateRuntimeReferences(string artRoot)
    {
        string statusRoot = Path.Combine(Application.streamingAssetsPath, "Data", "CharacterStatus");
        Validate(artRoot, id =>
        {
            var knownStatuses = new HashSet<int>();
            foreach (string file in Directory.EnumerateFiles(statusRoot, id + "_*.xlsx"))
            {
                if (Path.GetFileName(file).StartsWith("~$")) continue;
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var package = new ExcelPackage(stream);
                var sheet = package.Workbook.Worksheets["StatusData_Main"];
                if (sheet?.Dimension == null) continue;
                var headers = ExcelTableHeaders.For(sheet); headers.Require("StatusID");
                for (int row = headers.DataStartRow; row <= sheet.Dimension.End.Row; row++)
                    if (int.TryParse(headers.Cell(row, "StatusID").Text.Trim(), out int status)) knownStatuses.Add(status);
            }
            return knownStatuses;
        });
    }

    private void Validate(string artRoot, Func<int, HashSet<int>> statuses)
    {
        foreach (var pair in entries)
        {
            foreach (int variant in Variants(pair.Key))
                if (!File.Exists(Path.Combine(artRoot, "Character_Bare", $"{pair.Key}_{variant:D2}.png")))
                    throw new FileNotFoundException($"角色卡面切换表缺少 Character_Bare/{pair.Key}_{variant:D2}.png。");
            var knownStatuses = statuses(pair.Key);
            foreach (var rule in pair.Value.Conditions)
                if (!knownStatuses.Contains(rule.Status))
                    throw new InvalidDataException($"角色{pair.Key}的已发布状态表不含卡面条件 StatusID={rule.Status}。");
        }
    }
}
