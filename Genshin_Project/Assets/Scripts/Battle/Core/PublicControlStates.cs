using System;
using System.Collections.Generic;

/// <summary>公共控制的施加限制、行动限制和蓄力打断。</summary>
public static class PublicControlStates
{
    internal static bool IsControl(StatusMainData main) => HasType(main, "Control");
    internal static bool IsCharge(StatusMainData main) => HasType(main, "Charge");
    private static bool HasType(StatusMainData main, string type)
        => string.Equals(main?.StatusType, type, StringComparison.OrdinalIgnoreCase);

    internal static void OnStatusApplied(BattleEntity target, StatusInstance status)
    {
        if (IsControl(status.MainData)) InterruptCharges(target);
        else if (IsCharge(status.MainData) && IsControlled(target))
            target.RemoveStatusInstance(status, "Interrupted");
    }

    private static bool IsControlled(BattleEntity target)
    {
        foreach (var status in target.StatusDict.Values)
            if (status.IsActive && IsControl(status.MainData)) return true;
        foreach (var reaction in ReactionStateSystem.ActiveStates)
            if (reaction.Target == target && reaction.InterruptsCharges) return true;
        return false;
    }

    internal static void InterruptCharges(BattleEntity target)
    {
        using var resolution = BattleEventHub.BeginResolution();
        using var collection = BattleResolutionContext.BeginCollection();
        foreach (var status in target.GetStatusList())
            if (status.IsActive && IsCharge(status.MainData))
            {
                var parent = status.BindingParent;
                target.RemoveStatusInstance(status, "Interrupted");
                if (parent?.IsActive == true)
                    StatusHostAccess.Remove(parent.Host, parent, "Interrupted");
            }
    }

    public const string Imprisoned = "ST_Imprisoned";
    public const string OffField = "OffField";
    public const string EnemyStun = "ST_Stun_Enemy";
    public const string EnemyStealth = "ST_Stealth_Enemy";
    public static bool IsHidden(BattleEntity target) => target?.HasStatus(EnemyStealth) == true;
    public static bool CanApply(BattleEntity target, string id, BattleEntity caster = null)
    {
        if (IsEnemyOnly(id))
            return target?.Type == BattleEntity.EntityType.Enemy && target.Side == BattleSide.Enemy
                && caster?.Type == BattleEntity.EntityType.Enemy && caster.Side == BattleSide.Enemy;
        return id != Imprisoned || target?.Type == BattleEntity.EntityType.Character;
    }

    internal static bool IsEnemyOnly(string id) => id == EnemyStun || id == EnemyStealth
        || id == Enemy2061To2063Hooks.Core || id == Enemy2061To2063Hooks.Paralysis
        || id == HypostasisPublicStates.Shell || id == HypostasisPublicStates.Exposure || id == HypostasisPublicStates.Recovery;

    internal static void Register()
    {
        // 状态Self指真实宿主；瞄准场地绑定自身蓄力时显式选择施放者。
        BattleSpecialRules.RegisterTargets("ChargeCaster()", (_, caster) => caster?.IsAlive == true
            ? new List<int> { caster.SlotPosition } : new List<int>());
        BattleSpecialRules.RegisterEnemyActionBlocker(enemy => enemy?.HasStatus(EnemyStun) == true);
        BattleSpecialRules.RegisterStatusLifecycle(EnemyStun, (status, host) => {
            var battle = BattleManager.Instance;
            if (battle != null && status.AddInPhase == 6 && (int)battle.CurrentPhase == 6)
                status.EarliestTimerTurn = battle.TurnCount + 1;
        }, null);
    }
}
