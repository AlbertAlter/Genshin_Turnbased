using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

public partial class DataManager
{
    internal string GetRoguelikeItemDescription(RoguelikeItemDefinition item, bool english)
    {
        string experienceKey = item.Category switch
        {
            "CharacterExperience" => "item_character_experience_desc",
            "WeaponExperience" => "item_weapon_experience_desc",
            "ArtifactExperience" => "item_artifact_experience_desc",
            _ => null,
        };
        if (experienceKey != null && LangDict.ContainsKey(experienceKey))
        {
            string value = item.ExperienceValue.ToString(english ? "N0" : "0", CultureInfo.InvariantCulture);
            return GetLangText(experienceKey, english, value);
        }
        string key = $"item_{item.ItemId}_desc";
        return LangDict.ContainsKey(key) ? GetLangText(key, english) : item.Description;
    }

    private RosterTextCatalog rosterText;
    private RoguelikeItemCatalog itemCatalog;
    private ArtifactGenerationConfig artifactConfig;
    private LevelUpRequirementsTable levelRequirements;
    internal RosterTextCatalog GetRosterText() => rosterText ??= LoadRosterText();
    internal RoguelikeItemCatalog GetItemCatalog() => itemCatalog ??= RoguelikeItemCatalog.LoadFromStreamingAssets();
    internal ArtifactGenerationConfig GetArtifactConfig() => artifactConfig ??= ArtifactConfigurationLoader.LoadFromStreamingAssets();
    internal LevelUpRequirementsTable GetLevelRequirements() => levelRequirements ??= LevelUpRequirementsLoader.LoadFromStreamingAssets();

    private RoguelikeWishCatalog wishes;
    private RoguelikeStartingLoadout startingLoadout;
    internal RoguelikeWishCatalog GetWishCatalog(IEnumerable<int> stageIds = null) =>
        wishes ??= stageIds == null ? RoguelikeWishCatalog.LoadFromStreamingAssets() : RoguelikeWishCatalog.LoadFromStreamingAssets(stageIds);
    internal RoguelikeStartingLoadout GetStartingLoadout() => startingLoadout ??= RoguelikeStartingLoadout.ReadPublished();

    internal void PreloadRosterCatalogs()
    {
        GetRosterText();
        GetItemCatalog();
        GetLevelRequirements();
        GetArtifactConfig();
        GetStartingLoadout();
    }

    private void InvalidateRosterCatalogs()
    {
        wishes = null;
        startingLoadout = null;
        rosterText = null;
        itemCatalog = null;
        artifactConfig = null;
        levelRequirements = null;
    }

    public RosterTextCatalog LoadRosterText()
    {
        var catalog = LoadCharacterPageText();
        using (var book = OpenDataWorkbook(Path.Combine(ChartsPath, "Artifacts.xlsx")))
        {
            var sheet = book.Workbook.Worksheets["Sheet1"];
            var cols = ExcelTableHeaders.For(sheet);
            int minRarity = cols.Required("LR"), maxRarity = cols.Required("HR");
            for (int row = cols.DataStartRow; row <= GetSheetMaxRow(sheet); row++)
            {
                int id = SafeGetInt(sheet.Cells[row, cols.Required("ArtifactSetID")].Value);
                if (id > 0) catalog.Sets.Add(id, (
                    sheet.Cells[row, cols.Required("Name")].Text,
                    sheet.Cells[row, cols.Required("两件套效果")].Text,
                    sheet.Cells[row, cols.Required("四件套效果")].Text,
                    SafeGetInt(sheet.Cells[row, minRarity].Value),
                    SafeGetInt(sheet.Cells[row, maxRarity].Value)));
            }
            LoadArtifactSetVersions(book.Workbook.Worksheets["Sheet2"], catalog);
        }
        return catalog;
    }

    private void LoadArtifactSetVersions(OfficeOpenXml.ExcelWorksheet sheet, RosterTextCatalog catalog)
    {
        if (sheet?.Dimension == null) return;
        var columns = ExcelTableHeaders.For(sheet);
        int versionColumn = columns.Optional("首次出场版本");
        if (versionColumn < 0) return;
        int idColumn = columns.Required("ArtifactSetID");
        for (int row = columns.DataStartRow; row <= GetSheetMaxRow(sheet); row++)
        {
            string versionText = sheet.Cells[row, versionColumn].Text.Trim();
            if (versionText.Length == 0) continue;
            int setId = SafeGetInt(sheet.Cells[row, idColumn].Value);
            if (!catalog.Sets.ContainsKey(setId) || catalog.SetVersions.ContainsKey(setId) ||
                !Version.TryParse(versionText, out var version) || version.Major < 1 || version.Build >= 0)
                throw new InvalidDataException($"Artifacts.xlsx/Sheet2 第 {row} 行的套装 ID 或首次出场版本无效，版本应为 x.y。");
            catalog.SetVersions.Add(setId, version);
        }
    }

    public RosterTextCatalog LoadCharacterPageText()
    {
        var catalog = new RosterTextCatalog(this);
        using (var book = OpenDataWorkbook(Path.Combine(ChartsPath, "lang", "lang_CharacterPage.xlsx")))
        {
            foreach (var sheet in book.Workbook.Worksheets)
            {
                if (!sheet.Name.StartsWith("lang_character_", StringComparison.Ordinal)) continue;
                var headers = ExcelTableHeaders.For(sheet);
                for (int row = headers.DataStartRow; row <= GetSheetMaxRow(sheet); row++)
                {
                    string key = sheet.Cells[row, headers.Required("key")].Text.Trim();
                    if (key.Length != 0) catalog.Text.Add(key, (
                        sheet.Cells[row, headers.Required("chinese")].Text,
                        sheet.Cells[row, headers.Required("english")].Text));
                }
            }
            var mapping = book.Workbook.Worksheets["参数映射"];
            var cols = ExcelTableHeaders.For(mapping);
            var levelCols = Enumerable.Range(1, 15).Select(l => cols.Required("Level" + l)).ToArray();
            string[] kinds = { "Normal", "Heavy", "Skill", "Burst" };
            for (int row = cols.DataStartRow; row <= GetSheetMaxRow(mapping); row++)
            {
                string key = mapping.Cells[row, cols.Required("key")].Text.Trim();
                if (key.Length == 0) continue;
                int index = SafeGetInt(mapping.Cells[row, cols.Required("ParamIndex")].Value);
                int skill = Array.IndexOf(kinds, mapping.Cells[row, cols.Required("SkillType")].Text.Trim());
                if (skill < 0) throw new InvalidDataException("未知展示技能类型：" + key);
                int sourceRow = row;
                catalog.Parameters.Add((key, index), (skill,
                    levelCols.Select(col => SafeGetFloat(mapping.Cells[sourceRow, col].Value)).ToArray()));
            }
        }
        return catalog;
    }
}

public sealed class RosterTextCatalog
{
    private readonly DataManager data;
    internal readonly Dictionary<string, (string Chinese, string English)> Text = new();
    internal readonly Dictionary<(string Key, int Index), (int Skill, float[] Values)> Parameters = new();
    internal readonly Dictionary<int, (string Name, string Two, string Four, int MinRarity, int MaxRarity)> Sets = new();
    internal readonly Dictionary<int, Version> SetVersions = new();

    public RosterTextCatalog(DataManager data) { this.data = data; }

    public string CharacterDescription(string key, string fallback, CharacterSkillLevelInfo levels, bool english,
        CharacterSkillLevelInfo target = null)
        => DescribeCharacter(key, fallback, levels.GetOwnLevel, english,
            target == null ? null : new Func<int, int>(target.GetOwnLevel));

    internal string BattleCharacterDescription(string key, string fallback, Func<int, int> effectiveLevel, bool english)
        => DescribeCharacter(key, fallback, effectiveLevel, english, null);

    private string DescribeCharacter(string key, string fallback, Func<int, int> current, bool english, Func<int, int> target)
    {
        if (!Text.TryGetValue(key, out var row)) return fallback ?? "";
        var template = english ? row.English : row.Chinese;
        return FormatPreview(template, english, (index, _) =>
        {
            if (!Parameters.TryGetValue((key, index), out var parameter)) return (null, null);
            string Value(Func<int, int> level) =>
                parameter.Values[level(parameter.Skill) - 1].ToString("0.0", CultureInfo.InvariantCulture);
            return (Value(current), target == null ? null : Value(target));
        });
    }

    public string WeaponDescription(WeaponAttributesData weapon, int rank, int target, bool english)
    {
        string key = $"weapon_{weapon.WeaponID}_effect_desc";
        string template = data.LangDict.ContainsKey(key) ? data.GetRawLangText(key, english) : weapon.Description;
        return FormatPreview(template, english, (index, unit) =>
        {
            if (!data.WeaponParamDict.TryGetValue(weapon.WeaponID, out var fields) || !fields.ContainsKey(index + 1))
                return (null, null);
            float displayScale = unit.Length == 0 ? 1f : 100f;
            string Value(int r) => (data.ResolveWeaponParam(weapon.WeaponID, index + 1, r) * displayScale)
                .ToString("0.##", CultureInfo.InvariantCulture);
            return (Value(rank), Value(target));
        });
    }

    public string SetName(int id, bool english) => data.GetUiName(UiNameKind.ArtifactSet, id,
        Sets.TryGetValue(id, out var row) ? row.Name : id.ToString(CultureInfo.InvariantCulture), english);

    public string SetBonus(int id, int pieces, bool english)
    {
        string key = $"artifact_{id}_{pieces}pc_desc";
        if (data.LangDict.ContainsKey(key)) return data.GetLangRichText(key, english);
        return Sets.TryGetValue(id, out var row) ? pieces == 2 ? row.Two : row.Four : "";
    }

    private string FormatPreview(string template, bool english, Func<int, string, (string Current, string Target)> values)
    {
        string withValues = Regex.Replace(template ?? "", @"(?<!\{)\{(\d+)\}([%％]?)", match =>
        {
            string unit = match.Groups[2].Value;
            var (current, target) = values(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), unit);
            if (current == null) return "—" + unit;
            if (target == null || current == target) return current + unit;
            return english ? $"{current}{unit} ({target}{unit})" : $"{current}{unit}（{target}{unit}）";
        });
        return data.FormatCharacterPageDescription(withValues);
    }
}
