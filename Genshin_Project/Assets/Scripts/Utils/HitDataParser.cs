using System;
using System.Globalization;

/// <summary>Damage hit parameters use M,E. Poise is no longer authored in hit strings.</summary>
public static class HitDataParser
{
    /// <summary>等级表空格向上沿用；保留显式零及最近一次明确填写的属性、附着量。</summary>
    public static string InheritLevelFields(string current, string previous)
    {
        if (string.IsNullOrWhiteSpace(current)) return previous ?? string.Empty;
        if (string.IsNullOrWhiteSpace(previous)) return current;
        string[] fields = SplitHit(current);
        string[] inherited = SplitHit(previous);
        string multiplier = fields[0].Trim();
        if (multiplier.Length == 0) multiplier = inherited[0];
        else if (TryNumber(multiplier, out _) && inherited[0].Contains("*"))
        {
            // 仅倍率省略属性时沿用；治疗的完整加法公式和引用表达式不拼接。
            int star = inherited[0].IndexOf('*');
            string attribute = inherited[0].Substring(star);
            if (!attribute.Contains("+") && !attribute.Contains("-") && !inherited[0].StartsWith("%"))
                multiplier += attribute;
        }
        string aura = fields.Length == 2 && !string.IsNullOrWhiteSpace(fields[1])
            ? fields[1] : inherited.Length == 2 ? inherited[1] : string.Empty;
        return string.IsNullOrWhiteSpace(aura) ? multiplier : multiplier + "," + aura;
    }

    /// <summary>Parse "1.24*TotalATK,2", or a multiplier-only higher-level value.</summary>
    public static HitData Parse(string hitString)
    {
        string[] parts = SplitHit(hitString);
        string multiplierText = parts[0].Replace("*TotalATK", "").Replace("*TotalDEF", "")
            .Replace("*TotalHP", "").Replace("*ATK", "").Replace("*DEF", "").Replace("*MaxHP", "");
        TryNumber(multiplierText, out float multiplier);
        float aura = 0f;
        if (parts.Length == 2) TryNumber(parts[1], out aura);
        return new HitData(multiplier, aura);
    }

    /// <summary>Only omitted fields inherit; an explicitly authored zero remains zero.</summary>
    public static HitData MergeWithLevel1(string currentString, string level1String)
    {
        HitData current = Parse(currentString);
        HitData level1 = Parse(level1String);
        string[] parts = SplitHit(currentString);
        float multiplier = string.IsNullOrWhiteSpace(parts[0]) ? level1.Multiplier : current.Multiplier;
        float aura = parts.Length < 2 || string.IsNullOrWhiteSpace(parts[1])
            ? level1.ElementAura : current.ElementAura;
        return new HitData(multiplier, aura);
    }

    /// <summary>Resolve %Effect.HitsN.M*scale,E (or a numeric/ scaled E override).</summary>
    public static HitData ResolveReferencedHit(string expression, HitData sourceHit)
    {
        var reference = ParseReference(expression);
        if (reference.effectId == null || reference.fieldType != 0)
            throw new FormatException("Damage reference must start with %Effect.HitsN.M: " + expression);

        string[] parts = SplitHit(expression);
        string auraToken = parts.Length == 2 ? parts[1].Trim() : "E";
        float aura;
        if (auraToken == "E" || auraToken.Length == 0)
            aura = sourceHit.ElementAura;
        else if (auraToken.StartsWith("E*", StringComparison.Ordinal) &&
                 TryNumber(auraToken.Substring(2), out float scale))
            aura = sourceHit.ElementAura * scale;
        else if (!TryNumber(auraToken, out aura))
            throw new FormatException("Element amount must be E, E*scale or a number: " + expression);

        return new HitData(sourceHit.Multiplier * reference.scale, aura);
    }

    /// <summary>Field codes retain 0=M and 2=E; the former P field is invalid.</summary>
    public static (string effectId, int hitIndex, int fieldType, float scale) ParseReference(string expression)
    {
        string main = SplitHit(expression)[0].Trim();
        if (!main.StartsWith("%", StringComparison.Ordinal)) return (null, 0, 0, 1f);

        float scale = 1f;
        int star = main.IndexOf('*');
        if (star >= 0)
        {
            if (!TryNumber(main.Substring(star + 1), out scale))
                throw new FormatException("Invalid reference multiplier: " + expression);
            main = main.Substring(0, star);
        }
        string[] fields = main.Substring(1).Split('.');
        if (fields.Length != 3 || string.IsNullOrWhiteSpace(fields[0]) ||
            !fields[1].StartsWith("Hits", StringComparison.Ordinal) ||
            !int.TryParse(fields[1].Substring(4), out int hitNumber) || hitNumber < 1)
            throw new FormatException("Expected %Effect.HitsN.M reference: " + expression);

        int fieldType;
        switch (fields[2])
        {
            case "M": fieldType = 0; break;
            case "E": fieldType = 2; break;
            default: throw new FormatException("Hit references only support M and E: " + expression);
        }
        return (fields[0], hitNumber - 1, fieldType, scale);
    }

    private static string[] SplitHit(string value)
    {
        string[] parts = (value ?? string.Empty).Split(',', ';');
        if (parts.Length > 2)
            throw new FormatException("Hit parameters now use M,E; remove the old poise/P field: " + value);
        return parts;
    }

    private static bool TryNumber(string value, out float number)
    {
        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
    }
}
