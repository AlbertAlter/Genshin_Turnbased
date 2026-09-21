using System;
using System.Collections.Generic;
using UnityEngine;

public enum EnemyShieldRuleKind
{
    None,
    ElementAmountMultiplier,
    FixedGauge,
    ElementAmountPlusPoiseFormula,
    FixedGaugePlusPoiseFormula
}

[Serializable]
public sealed class EnemyShieldRule
{
    public string ShieldElement;
    public string HitKind;
    public EnemyShieldRuleKind Kind;
    public float ElementAmountMultiplier;
    public float FixedGauge;
    public float PoiseGaugePerPoint;
    public string RawText;

    public bool HasEffect => Kind != EnemyShieldRuleKind.None;

    public float Evaluate(float attackElementAmount, float rawPoiseDamage, float currentShieldGauge)
    {
        if (!IsFinite(attackElementAmount) || !IsFinite(rawPoiseDamage) || !IsFinite(currentShieldGauge))
            throw new ArgumentOutOfRangeException(nameof(attackElementAmount), "Enemy shield inputs must be finite.");
        if (attackElementAmount < 0f || rawPoiseDamage < 0f || currentShieldGauge < 0f)
            throw new ArgumentOutOfRangeException(nameof(attackElementAmount), "Enemy shield inputs cannot be negative.");

        float result = attackElementAmount * ElementAmountMultiplier
                       + FixedGauge
                       + rawPoiseDamage * PoiseGaugePerPoint;
        if (!IsFinite(result) || result < 0f)
            throw new InvalidOperationException($"Enemy shield rule produced an invalid result: {RawText}");
        return Mathf.Clamp(result, 0f, currentShieldGauge);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}

public sealed class EnemyShieldRuleTable
{
    private readonly Dictionary<string, Dictionary<string, EnemyShieldRule>> _rules =
        new Dictionary<string, Dictionary<string, EnemyShieldRule>>(StringComparer.OrdinalIgnoreCase);

    public int Count { get; private set; }

    public void Add(EnemyShieldRule rule)
    {
        if (rule == null)
            throw new ArgumentNullException(nameof(rule));
        if (string.IsNullOrWhiteSpace(rule.ShieldElement) || string.IsNullOrWhiteSpace(rule.HitKind))
            throw new ArgumentException("Enemy shield rule keys cannot be empty.", nameof(rule));

        if (!_rules.TryGetValue(rule.ShieldElement, out Dictionary<string, EnemyShieldRule> row))
        {
            row = new Dictionary<string, EnemyShieldRule>(StringComparer.OrdinalIgnoreCase);
            _rules.Add(rule.ShieldElement, row);
        }
        if (row.ContainsKey(rule.HitKind))
            throw new InvalidOperationException($"Duplicate enemy shield rule: {rule.ShieldElement} x {rule.HitKind}");
        row.Add(rule.HitKind, rule);
        Count++;
    }

    public bool TryGet(string shieldElement, string hitKind, out EnemyShieldRule rule)
    {
        rule = null;
        return !string.IsNullOrWhiteSpace(shieldElement)
               && !string.IsNullOrWhiteSpace(hitKind)
               && _rules.TryGetValue(shieldElement, out Dictionary<string, EnemyShieldRule> row)
               && row.TryGetValue(hitKind, out rule);
    }

    public EnemyShieldRule GetOrNone(string shieldElement, string hitKind)
    {
        if (TryGet(shieldElement, hitKind, out EnemyShieldRule rule))
            return rule;
        return new EnemyShieldRule
        {
            ShieldElement = shieldElement ?? string.Empty,
            HitKind = hitKind ?? string.Empty,
            Kind = EnemyShieldRuleKind.None,
            RawText = string.Empty
        };
    }
}
