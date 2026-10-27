using System;

/// <summary>Buff/Debuff 的统一只读数值入口。Count 只读取来源角色的 RuntimeValue。</summary>
public static class BuffExpressionEvaluator
{
    public static bool TryResolveScalar(string expression, StatusInstance status, out float value)
    {
        value = 0f;
        if (string.IsNullOrWhiteSpace(expression)) return false;
        try
        {
            value = BuffScalarParser.Evaluate(expression,
                status?.WeaponContext == null ? (Func<int, float>)null : status.WeaponContext.ResolveParam,
                id => ReadCount(status, id), attribute => ReadAttribute(status, attribute));
            return true;
        }
        catch (FormatException)
        {
            // 保留旧 TryResolveScalar 对不支持表达式返回 false 的约定。
            // Count 配错类型/ID 则由 ReadCount 抛错，不能静默当作0。
            return false;
        }
    }

    private static float ReadAttribute(StatusInstance status, string attribute)
    {
        if (attribute != "TotalRechargeRate")
            throw new FormatException("未支持的 Buff 属性：" + attribute);
        // 当前只接绝缘这类DMGBonus公式，禁止充能Buff读自身最终充能形成递归。
        if (status?.Caster == null || status.MainData?.GetMultiplierPart() != "DMGBonus")
            throw new InvalidOperationException("TotalRechargeRate当前只允许DMGBonus公式读取来源角色的最终充能。");
        return status.Caster.GetFinalRechargeRate();
    }

    private static float ReadCount(StatusInstance source, string id)
    {
        BattleEntity owner = source?.Caster;
        if (owner == null) throw new InvalidOperationException("Count 表达式缺少 Buff 施放者：" + id);
        StatusMainData main = null;
        DataManager.Instance?.StatusMainDict.TryGetValue(id, out main);
        StatusInstance count = owner.GetStatus(id);
        if (main == null) main = count?.MainData;
        if (main == null || !CountStatusSystem.IsCount(main))
            throw new InvalidOperationException("Count 表达式必须引用已定义的 Count/NightSoul 状态：" + id);
        // 定义正确但尚未施加、已经移除或未激活时，贡献为0。不创建、不消耗、不钳制。
        return count != null && count.IsActive ? count.RuntimeValue : 0f;
    }
}
