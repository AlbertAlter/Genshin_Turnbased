using System;

/// <summary>公共魔导状态强化：创建实例时固定强度系数，读取时不检查窗口，不改共享配表或旧实例。</summary>
public static class HexereiStatusEnhancement
{
    public const string Window = "ST_Fischl_HexereiBoost";

    internal static void Capture(StatusInstance status)
    {
        if (status == null) return;
        status.HexereiMultiplier = 1f;
        var battle = BattleManager.Instance;
        var team = battle?.TeamBonuses;
        if (status.Caster?.IsAlive != true || team?.HasHexerei != true
            || !team.Contains(status.Caster) || DataManager.Instance?.HexereiStatusIDs.Contains(status.StatusID2) != true) return;
        foreach (var window in team.Host.GetStatusInstances(Window))
            if (window?.IsActive == true && window.Caster?.IsAlive == true && window.Caster.EntityID == 1024
                && window.Caster.ConstellationLevel >= 6)
            { status.HexereiMultiplier = 2f; return; }
    }

    /// <summary>所有Buff参数路径共用；只乘本份状态的系数，次数、时长、Count与绑定均不改变。</summary>
    public static bool TryResolveScalar(StatusEffectData effect, StatusInstance status, out float value)
    {
        value = 0f;
        if (effect == null) return false;
        bool found = WeaponHooks.TryResolveBuffScalar(effect, status, out value)
            || BattleSpecialRules.TryResolveBuffScalar(effect, status, out value)
            || (status?.Caster?.CharacterCtrl?.TryResolveStatusLevelScalar(effect, out value) ?? false)
            || BuffExpressionEvaluator.TryResolveScalar(effect.Param1, status, out value);
        if (!found) return false;
        value *= status?.HexereiMultiplier ?? 1f;
        if (float.IsNaN(value) || float.IsInfinity(value))
            throw new InvalidOperationException("Buff参数必须为有限值：" + effect.StatusEffectID2);
        return true;
    }
}
