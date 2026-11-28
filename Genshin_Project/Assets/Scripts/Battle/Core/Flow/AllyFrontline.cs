using System.Collections.Generic;

/// <summary>管理我方前台、换人付款和提交，以及阵亡补位和回场自动上场。</summary>
internal sealed class AllyFrontline
{
    internal const int SwitchAPCost = 5;

    private readonly IReadOnlyList<CharacterBattleController> _allies;
    private bool _presenceReturnPending;
    private CharacterBattleController _skillSwitchOutgoing;
    internal bool IsFreeDeathSwitchPending { get; private set; }
    internal bool IsFreeForcedSwitchPending => IsFreeDeathSwitchPending || _skillSwitchOutgoing != null;
    internal bool CanSelectForSwitch(CharacterBattleController target) => target?.Entity?.IsOnField == true
        || (_skillSwitchOutgoing == null && BattleSpecialRules.CanReturnBySwitch(target?.Entity));

    internal void RequestFreeSkillReplacement(BattleEntity outgoing)
    {
        var controller = System.Linq.Enumerable.FirstOrDefault(_allies, a => a?.Entity == outgoing && a.IsActive);
        if (controller == null || outgoing.IsOnField) return;
        // 无可补位成员时仍允许推进回合，离场不等于全灭。
        foreach (var ally in _allies)
            if (ally?.Entity?.IsOnField == true) { _skillSwitchOutgoing = controller; return; }
    }

    internal AllyFrontline(IReadOnlyList<CharacterBattleController> allies) => _allies = allies;

    internal CharacterBattleController GetActiveAlly()
    {
        // 离场者无效，阵亡的前台仍保留给免费换人流程识别。
        foreach (var ally in _allies)
            if (ally != null && ally.IsActive && ally.Entity != null && !ally.Entity.IsOffField) return ally;
        return null;
    }

    private static bool CanLeave(CharacterBattleController current)
        => current == null || current.Entity == null || !current.Entity.IsAlive || current.CanSwitch();

    internal bool CanSwitchTo(CharacterBattleController target)
    {
        if (!CanSelectForSwitch(target)) return false;
        var current = GetActiveAlly();
        return current != target && CanLeave(current);
    }

    internal bool CanSwitch()
    {
        var current = GetActiveAlly();
        if (!CanLeave(current)) return false;

        foreach (var ally in _allies)
            if (ally != null && ally != current && CanSelectForSwitch(ally))
                return true;
        return false;
    }

    internal int GetSwitchAPCost(CharacterBattleController incoming)
    {
        var outgoing = GetActiveAlly();
        if (incoming == null || incoming == outgoing) return 0;
        return APCostResolver.ForSwitch(outgoing != null ? outgoing.Entity : null,
            incoming.Entity, SwitchAPCost, IsFreeForcedSwitchPending);
    }

    internal bool TrySwitchManually(CharacterBattleController target, ActionPointManager apManager, bool chargeAP)
    {
        if (!CanSwitchTo(target)) return false;

        using var collection = BattleResolutionContext.BeginCollection();
        int cost = chargeAP ? GetSwitchAPCost(target) : 0;
        if (cost > 0 && (apManager == null || !apManager.ConsumeAP(cost))) return false;

        if (target.Entity.IsOffField) BattleSpecialRules.PrepareOffFieldSwitch(target.Entity);
        CommitSwitch(target, cost);
        return true;
    }

    internal bool TrySwitchFromEffect(BattleEntity target)
    {
        foreach (var ally in _allies)
        {
            if (ally?.Entity != target) continue;
            if (!CanSwitchTo(ally)) return false;
            CommitSwitch(ally, 0, BattleSwitchReason.SkillAutomatic);
            return true;
        }
        return false;
    }

    internal void PrepareAllyAction(CharacterBattleController active, bool canPlayerSwitch)
    {
        IsFreeDeathSwitchPending = active != null && active.Entity != null && active.Entity.IsDead
            && canPlayerSwitch && CanSwitch();
        if (IsFreeDeathSwitchPending)
            LogManager.Log(LogCategory.Turn, "出战角色已阵亡，等待免费换人");
    }

    internal bool RecordReturn(BattleEntity entity)
    {
        if (entity?.Side != BattleSide.Ally || !entity.IsAlive || entity.IsOffField) return false;
        _presenceReturnPending = true;
        return true;
    }

    internal void ResolveReturn()
    {
        if (!_presenceReturnPending) return;
        _presenceReturnPending = false;
        if (GetActiveAlly()?.Entity?.IsOnField == true) return;

        CharacterBattleController only = null;
        foreach (var ally in _allies)
        {
            if (ally?.Entity?.IsOnField != true) continue;
            if (only != null) return; // 多人在场仍由既有换人选择处理。
            only = ally;
        }
        if (only != null) CommitSwitch(only, 0, BattleSwitchReason.ReturnAutomatic);
    }

    internal void ClearPendingSwitches()
    {
        IsFreeDeathSwitchPending = false;
        _skillSwitchOutgoing = null;
        _presenceReturnPending = false;
    }

    private void CommitSwitch(CharacterBattleController target, int paidAP, BattleSwitchReason? explicitReason = null)
    {
        var previous = GetActiveAlly() ?? _skillSwitchOutgoing;
        BattleSwitchReason reason = explicitReason ?? (IsFreeDeathSwitchPending || previous?.Entity?.IsDead == true
            ? BattleSwitchReason.DeathForced : _skillSwitchOutgoing != null ? BattleSwitchReason.SkillAutomatic
            : previous?.Entity == null ? BattleSwitchReason.InitialEntry : BattleSwitchReason.Manual);

        // 付款声明保留切换前的布局，响应等整个换人提交后再执行。
        using var collection = BattleResolutionContext.BeginCollection();
        if (paidAP > 0)
            StatusOnHitHookSystem.NotifyAPUsed(previous != null ? previous.Entity : null, paidAP, APSpendKind.Switch);
        foreach (var ally in _allies)
            if (ally != null) ally.IsActive = ally == target;
        ClearPendingSwitches();
        StatusOnHitHookSystem.NotifySwitch(previous != null ? previous.Entity : null, target.Entity, reason);
    }
}
