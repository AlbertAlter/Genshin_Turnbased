using System;

[Serializable]
public sealed class EnemyShieldHitResult
{
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
        float rawPoiseDamage,
        string hitKind = null,
        EnemyShieldRuleTable rules = null)
    {
        var result = new EnemyShieldHitResult();
        Shield shield = target != null ? target.GetEnemyElementalShield() : null;
        if (shield == null) return result;

        result.HadShieldAtStart = true;
        result.ShieldRow = GetEffectiveShieldRow(target, shield);
        result.HitColumn = ResolveHitColumn(attackElement, hitKind);
        result.RemainingShield = shield.Value;

        EnemyShieldRuleTable table = rules ?? (DataManager.Instance != null
            ? DataManager.Instance.EnemyShieldRules
            : null);
        if (table == null
            || !table.TryGet(result.ShieldRow, result.HitColumn, out EnemyShieldRule rule))
        {
            LogResult(target, attackElementAmount, rawPoiseDamage, result);
            return result;
        }

        result.RawRule = rule.RawText;
        result.RequestedLoss = rule.Evaluate(
            attackElementAmount,
            rawPoiseDamage,
            shield.Value);
        result.AppliedLoss = target.ApplyEnemyElementalShieldLoss(
            result.RequestedLoss,
            out bool broken);
        result.BrokeShield = broken;
        result.RemainingShield = target.GetEnemyElementalShieldHP();
        LogResult(target, attackElementAmount, rawPoiseDamage, result);
        return result;
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
        return string.IsNullOrWhiteSpace(attackElement)
               || string.Equals(attackElement, "None", StringComparison.OrdinalIgnoreCase)
               || string.Equals(attackElement, "Physical", StringComparison.OrdinalIgnoreCase)
            ? "Physical"
            : attackElement.Trim();
    }

    private static void LogResult(
        BattleEntity target,
        float attackElementAmount,
        float rawPoiseDamage,
        EnemyShieldHitResult result)
    {
        LogManager.Log(LogCategory.Effect,
            $"[EnemyShield] target={target.EntityID} elementAmount={attackElementAmount:F3} " +
            $"row={result.ShieldRow} column={result.HitColumn} rule={result.RawRule ?? "<blank>"} " +
            $"PoiseDMG={rawPoiseDamage:F3} loss={result.AppliedLoss:F3} " +
            $"remaining={result.RemainingShield:F3} broken={result.BrokeShield}");
    }
}
