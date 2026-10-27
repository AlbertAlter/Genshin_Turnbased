using System;

[Serializable]
public sealed class EnemyShieldHitResult
{
    [NonSerialized] public BattleEntity Target;
    [NonSerialized] internal Shield ShieldAtStart;
    public string AttackElement;
    public float AttackElementAmount;
    public bool Committed;
    public bool HadShieldAtStart;
    public string ShieldRow;
    public string HitColumn;
    public string RawRule;
    public float RequestedLoss;
    public float AppliedLoss;
    public float RemainingShield;
    public bool BrokeShield;
}

/// <summary>EnemyShield.Type1 的单 Hit 查询与耐久提交入口。</summary>
public static class EnemyElementShieldSystem
{
    public static EnemyShieldHitResult ResolveHit(
        BattleEntity target,
        string attackElement,
        float attackElementAmount,
        string hitKind = null,
        EnemyShieldRuleTable rules = null)
    {
        var result = PrepareHit(target, attackElement, attackElementAmount, hitKind);
        CommitHit(result, rules);
        return result;
    }

    /// <summary>保存接触开始时的盾及削盾规则行；仅用于耐久结算，不决定本体是否免伤。</summary>
    public static EnemyShieldHitResult PrepareHit(BattleEntity target, string attackElement,
        float attackElementAmount, string hitKind = null)
    {
        var result = new EnemyShieldHitResult
        {
            Target = target, AttackElement = attackElement, AttackElementAmount = attackElementAmount,
            HitColumn = ResolveHitColumn(attackElement, hitKind)
        };
        Shield shield = target != null ? target.GetEnemyElementalShield() : null;
        if (shield == null || DamageProperties.IsFixed(attackElement)) return result;

        result.ShieldAtStart = shield;
        result.HadShieldAtStart = true;
        result.ShieldRow = GetEffectiveShieldRow(target, shield);
        result.RemainingShield = shield.Value;
        return result;
    }

    /// <summary>同一接触只扣一次GU；普通伤害入口复用反应结果，不对破盾后的新盾补扣。</summary>
    public static void CommitHit(EnemyShieldHitResult result, EnemyShieldRuleTable rules = null)
    {
        if (result == null || result.Committed) return;
        result.Committed = true;
        BattleEntity target = result.Target;
        if (!result.HadShieldAtStart || target == null
            || !ReferenceEquals(result.ShieldAtStart, target.GetEnemyElementalShield())) return;

        EnemyShieldRuleTable table = rules ?? (DataManager.Instance != null
            ? DataManager.Instance.EnemyShieldRules
            : null);
        if (table == null
            || !table.TryGet(result.ShieldRow, result.HitColumn, out EnemyShieldRule rule))
        {
            LogResult(target, result.AttackElementAmount, result);
            return;
        }

        result.RawRule = rule.RawText;
        result.RequestedLoss = rule.Evaluate(
            result.AttackElementAmount,
            result.ShieldAtStart.Value);
        result.AppliedLoss = target.ApplyEnemyElementalShieldLoss(
            result.RequestedLoss,
            out bool broken);
        result.BrokeShield = broken;
        result.RemainingShield = target.GetEnemyElementalShieldHP();
        LogResult(target, result.AttackElementAmount, result);
    }

    private static string GetEffectiveShieldRow(BattleEntity target, Shield shield)
    {
        if (string.Equals(shield.Element, "Hydro", StringComparison.OrdinalIgnoreCase)
            && FrozenReactionHandler.IsFrozen(target))
            return "Freeze";
        // Status_Effect 配表用 Cryo 表示冻元素盾，EnemyShield.Type1 使用 Freeze 行。
        return string.Equals(shield.Element, "Cryo", StringComparison.OrdinalIgnoreCase)
            ? "Freeze"
            : shield.Element;
    }

    private static string ResolveHitColumn(string attackElement, string hitKind)
    {
        if (!string.IsNullOrWhiteSpace(hitKind)) return hitKind.Trim();
        return string.Equals(attackElement, "Physical", StringComparison.OrdinalIgnoreCase)
            ? "Physical"
            : attackElement?.Trim();
    }

    private static void LogResult(
        BattleEntity target,
        float attackElementAmount,
        EnemyShieldHitResult result)
    {
        LogManager.Log(LogCategory.Effect,
            $"[EnemyShield] target={target.EntityID} elementAmount={attackElementAmount:F3} " +
            $"row={result.ShieldRow} column={result.HitColumn} rule={result.RawRule ?? "<blank>"} " +
            $"loss={result.AppliedLoss:F3} " +
            $"remaining={result.RemainingShield:F3} broken={result.BrokeShield}");
    }
}
