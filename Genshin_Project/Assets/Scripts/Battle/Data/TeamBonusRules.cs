using System;
using System.Collections.Generic;
using System.Globalization;
using OfficeOpenXml;

/// <summary>TeamBonus 每个子表是一个固定机制的参数集，不解释任意配表指令。</summary>
public sealed class TeamBonusParameters
{
    private readonly Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);
    public bool Enabled => Int("Enabled") == 1;
    public string Text(string key) => values.TryGetValue(key, out string value) ? value : string.Empty;
    public float Number(string key) => values.TryGetValue(key, out string value)
        ? float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture) : 0f;
    public int Int(string key) => (int)Number(key);
    public bool Has(string key) => values.ContainsKey(key);
    internal int ParameterCount => values.Count;
    internal void Add(string key, string value)
    {
        if (values.ContainsKey(key)) throw new TableValidationException("TeamBonus 重复参数：" + key);
        values.Add(key, value);
    }
    public HashSet<string> Tokens(string key)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (string token in Text(key).Split(';'))
            if (!string.IsNullOrWhiteSpace(token)) result.Add(token.Trim());
        return result;
    }
    public HashSet<int> CharacterIDs()
    {
        var result = new HashSet<int>();
        foreach (string token in Tokens("CharacterIDs"))
        {
            if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) || id <= 0)
                throw new TableValidationException("TeamBonus CharacterIDs 必须是分号分隔的正整数：" + token);
            result.Add(id);
        }
        return result;
    }
}

public sealed class TeamBonusRules
{
    public TeamBonusParameters Resonance = new TeamBonusParameters();
    public TeamBonusParameters NightSoul = new TeamBonusParameters();
    public TeamBonusParameters Moonsign = new TeamBonusParameters();
    public TeamBonusParameters Hexerei = new TeamBonusParameters();
    public TeamBonusParameters Stellar = new TeamBonusParameters();
}

public static class TeamBonusTableLoader
{
    public static readonly string[] SheetNames =
        { "ElementalResonance", "NightSoulBurst", "Moonsign", "Hexerei", "Stellar" };

    public static TeamBonusRules Load(ExcelWorkbook workbook)
    {
        var rules = new TeamBonusRules
        {
            Resonance = Read(workbook.Worksheets[SheetNames[0]]),
            NightSoul = Read(workbook.Worksheets[SheetNames[1]]),
            Moonsign = Read(workbook.Worksheets[SheetNames[2]]),
            Hexerei = Read(workbook.Worksheets[SheetNames[3]]),
            Stellar = Read(workbook.Worksheets[SheetNames[4]])
        };
        Validate(rules.Resonance,
            "Enabled;PairCount;PyroATK;HydroHP;AnemoAP;ElectroEnergy;ElectroPerTurn;CryoCrit;GeoShield;GeoDMG;GeoResDown;GeoDuration;DendroEM;DendroEMA;DendroEMB;DendroDuration;ProtectTypes;ProtectRes",
            "Enabled;PairCount;AnemoAP;ElectroPerTurn;GeoDuration;DendroDuration;ProtectTypes");
        Validate(rules.NightSoul, "Enabled;Cooldown1;Cooldown2;Cooldown3", "Enabled;Cooldown1;Cooldown2;Cooldown3");
        Validate(rules.Moonsign,
            "Enabled;GleamCount;AscendantCount;Duration;ATKStep;ATKBonus;HPStep;HPBonus;DEFStep;DEFBonus;EMStep;EMBonus;BonusCap",
            "Enabled;GleamCount;AscendantCount;Duration");
        Validate(rules.Hexerei, "Enabled;RequiredCount", "Enabled;RequiredCount");
        Validate(rules.Stellar, "Enabled;Duration;PhysicalResDown;CountCap", "Enabled;Duration;CountCap");
        foreach (string step in new[] { "ATKStep", "HPStep", "DEFStep", "EMStep" }) Positive(rules.Moonsign, step);
        foreach (var pair in new[] { (rules.Resonance, "PairCount"), (rules.Resonance, "ProtectTypes"),
            (rules.Moonsign, "GleamCount"), (rules.Moonsign, "AscendantCount"), (rules.Hexerei, "RequiredCount") })
            Positive(pair.Item1, pair.Item2);
        if (rules.Moonsign.Int("AscendantCount") < rules.Moonsign.Int("GleamCount"))
            throw new TableValidationException("TeamBonus 满辉人数不能低于初辉人数");
        if (rules.Stellar.Int("CountCap") > rules.Stellar.ParameterCount)
            throw new TableValidationException("TeamBonus 星辉计数上限超过现有参数数量；请先补齐对应档位数据");
        for (int n = 0; n <= rules.Stellar.Int("CountCap"); n++)
            Validate(rules.Stellar, "ElementDMG" + n + ";ReactionRate" + n, "");
        foreach (TeamBonusParameters p in new[] { rules.NightSoul, rules.Moonsign, rules.Hexerei, rules.Stellar })
        {
            Require(p, "CharacterIDs");
            p.CharacterIDs();
        }
        foreach (string key in new[] { "ElectroReactions", "DendroGroupA", "DendroGroupB" })
        {
            Require(rules.Resonance, key);
            foreach (string token in rules.Resonance.Tokens(key))
            {
                bool recognized = false;
                foreach (ReactionType reaction in Enum.GetValues(typeof(ReactionType)))
                    if (reaction != ReactionType.None && (token == reaction.ToString()
                        || ReactionFilterMatcher.Matches(token, reaction))) recognized = true;
                if (!recognized) throw new TableValidationException("TeamBonus 未识别的反应/元素：" + token);
            }
        }
        return rules;
    }

    public static TeamBonusParameters Read(ExcelWorksheet sheet)
    {
        if (sheet?.Dimension == null) throw new TableValidationException("TeamBonus 缺少或空白子表");
        int keyColumn = Header(sheet, "Key"), valueColumn = Header(sheet, "Value");
        var result = new TeamBonusParameters();
        for (int row = 2; row <= sheet.Dimension.End.Row; row++)
        {
            string key = sheet.Cells[row, keyColumn].Text.Trim();
            if (key.Length == 0 || key.StartsWith("#", StringComparison.Ordinal)
                || (row == 2 && key == "string")) continue;
            try { result.Add(key, Convert.ToString(sheet.Cells[row, valueColumn].Value, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty); }
            catch (TableValidationException e) { throw new TableValidationException(sheet.Name + " 第" + row + "行：" + e.Message); }
        }
        return result;
    }

    private static int Header(ExcelWorksheet sheet, string name)
    {
        int found = 0;
        for (int c = 1; c <= sheet.Dimension.End.Column; c++)
            if (sheet.Cells[1, c].Text.Trim() == name)
            {
                if (found > 0) throw new TableValidationException(sheet.Name + " 重复表头：" + name);
                found = c;
            }
        if (found == 0) throw new TableValidationException(sheet.Name + " 缺少表头：" + name);
        return found;
    }
    private static void Require(TeamBonusParameters p, string key)
    { if (!p.Has(key)) throw new TableValidationException("TeamBonus 缺少参数：" + key); }
    private static void Positive(TeamBonusParameters p, string key)
    { if (p.Number(key) <= 0) throw new TableValidationException("TeamBonus 参数必须大于0：" + key); }
    private static void Validate(TeamBonusParameters p, string numeric, string integers)
    {
        var integerKeys = new HashSet<string>(integers.Split(';'));
        foreach (string key in numeric.Split(';'))
        {
            Require(p, key);
            if (!float.TryParse(p.Text(key), NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
                || float.IsNaN(value) || float.IsInfinity(value)
                || (key != "AnemoAP" && value < 0f)
                || (integerKeys.Contains(key) && (value != Math.Truncate(value) || value > int.MaxValue || value < int.MinValue))
                || (key == "Enabled" && value != 0f && value != 1f))
                throw new TableValidationException("TeamBonus 参数值非法：" + key + "=" + p.Text(key));
        }
    }
}
