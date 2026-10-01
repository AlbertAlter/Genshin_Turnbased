using System;
using System.Globalization;
using System.Text.RegularExpressions;

public static class EnemyShieldRuleParser
{
    private const string NumberPattern = @"(?:0|[1-9]\d*)(?:\.\d+)?";
    private static readonly Regex BareNumber = new Regex($@"^(?<value>{NumberPattern})$", RegexOptions.CultureInvariant);
    private static readonly Regex FixedGauge = new Regex($@"^(?<fixed>{NumberPattern})U$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex PoiseOnly = new Regex($@"^(?<poise>{NumberPattern})U\*PoiseDMG$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex ElementPlusPoise = new Regex($@"^(?<element>{NumberPattern})\+(?<poise>{NumberPattern})U\*PoiseDMG$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex FixedPlusPoise = new Regex($@"^(?<fixed>{NumberPattern})U\+(?<poise>{NumberPattern})U\*PoiseDMG$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex InitialGauge = new Regex($@"^(?<gauge>{NumberPattern})U$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>解析 Status_Effect.Shield 对敌方目标配置的初始元素盾量，例如 12U。</summary>
    public static float ParseInitialGauge(string rawText)
    {
        string raw = (rawText ?? string.Empty).Trim();
        Match match = InitialGauge.Match(raw);
        if (!match.Success)
            throw new FormatException($"Enemy shield initial gauge must use a numeric U value, for example 12U. Actual: [{raw}].");

        float gauge = ParseNumber(match.Groups["gauge"].Value, raw);
        if (gauge <= 0f)
            throw new FormatException($"Enemy shield initial gauge must be greater than zero. Actual: [{raw}].");
        return gauge;
    }

    public static EnemyShieldRule Parse(string rawText, string shieldElement, string hitKind)
    {
        string raw = (rawText ?? string.Empty).Trim();
        if (raw.Length == 0)
            return Create(shieldElement, hitKind, EnemyShieldRuleKind.None, raw);

        Match match = BareNumber.Match(raw);
        if (match.Success)
        {
            EnemyShieldRule rule = Create(shieldElement, hitKind, EnemyShieldRuleKind.ElementAmountMultiplier, raw);
            rule.ElementAmountMultiplier = ParseNumber(match.Groups["value"].Value, raw);
            return rule;
        }

        match = FixedGauge.Match(raw);
        if (match.Success)
        {
            EnemyShieldRule rule = Create(shieldElement, hitKind, EnemyShieldRuleKind.FixedGauge, raw);
            rule.FixedGauge = ParseNumber(match.Groups["fixed"].Value, raw);
            return rule;
        }

        match = ElementPlusPoise.Match(raw);
        if (match.Success)
        {
            EnemyShieldRule rule = Create(shieldElement, hitKind, EnemyShieldRuleKind.ElementAmountPlusPoiseFormula, raw);
            rule.ElementAmountMultiplier = ParseNumber(match.Groups["element"].Value, raw);
            rule.PoiseGaugePerPoint = ParseNumber(match.Groups["poise"].Value, raw);
            return rule;
        }

        match = FixedPlusPoise.Match(raw);
        if (match.Success)
        {
            EnemyShieldRule rule = Create(shieldElement, hitKind, EnemyShieldRuleKind.FixedGaugePlusPoiseFormula, raw);
            rule.FixedGauge = ParseNumber(match.Groups["fixed"].Value, raw);
            rule.PoiseGaugePerPoint = ParseNumber(match.Groups["poise"].Value, raw);
            return rule;
        }

        match = PoiseOnly.Match(raw);
        if (match.Success)
        {
            EnemyShieldRule rule = Create(shieldElement, hitKind, EnemyShieldRuleKind.FixedGaugePlusPoiseFormula, raw);
            rule.PoiseGaugePerPoint = ParseNumber(match.Groups["poise"].Value, raw);
            return rule;
        }

        throw new FormatException($"Unsupported EnemyShield rule [{raw}] at {shieldElement} x {hitKind}.");
    }

    private static EnemyShieldRule Create(string shieldElement, string hitKind, EnemyShieldRuleKind kind, string raw)
    {
        if (string.IsNullOrWhiteSpace(shieldElement))
            throw new ArgumentException("Shield element cannot be empty.", nameof(shieldElement));
        if (string.IsNullOrWhiteSpace(hitKind))
            throw new ArgumentException("Hit kind cannot be empty.", nameof(hitKind));
        return new EnemyShieldRule
        {
            ShieldElement = shieldElement.Trim(),
            HitKind = hitKind.Trim(),
            Kind = kind,
            RawText = raw
        };
    }

    private static float ParseNumber(string value, string raw)
    {
        if (!float.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out float result)
            || float.IsNaN(result) || float.IsInfinity(result) || result < 0f)
            throw new FormatException($"Invalid numeric value in EnemyShield rule [{raw}].");
        return result;
    }
}
