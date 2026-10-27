using System.Collections.Generic;

/// <summary>510冒险家：完整队伍就绪后初始化固定生命上限，不产生治疗或战斗结束响应。</summary>
public static class AdventurerArtifactHooks
{
    public const string PersonalID = "ST_Artifact_451001";
    public const string ParentID = "ST_Artifact_451002";
    public const string SharedID = "ST_Artifact_451003";

    internal static void Register()
    {
        BattleSpecialRules.RegisterNumberedCondition("45100201", (argument, context) =>
        {
            var team = BattleManager.Instance?.TeamBonuses;
            return context?.Status?.StatusID2 == ParentID && context.Status.IsActive
                && context.Caster == context.Status.Caster && team != null && !team.IsDisposed
                && team.Contains(context.Caster);
        });
        BattleSpecialRules.RegisterBattleReady(Initialize);
    }

    private static void Initialize(BattleManager battle)
    {
        if (battle?.TeamBonuses == null || battle.TeamBonuses.IsDisposed) return;
        var parents = new List<StatusInstance>();
        foreach (var ally in battle.Allies)
            if (ally?.Entity != null)
                foreach (var parent in ally.Entity.GetStatusInstances(ParentID))
                    if (parent.IsActive && EquipmentStatusLifetime.CanExecute(parent)) parents.Add(parent);
        parents.Sort((left, right) => left.ApplyOrder.CompareTo(right.ApplyOrder));
        foreach (var parent in parents)
        {
            bool hasBinding = false;
            foreach (var child in parent.BoundStatuses)
                if (child != null && child.IsActive && child.StatusID2 == SharedID) { hasBinding = true; break; }
            if (!hasBinding) StatusBindingSystem.ApplyBindings(parent, parent.Caster);
        }
        // 生命净差交由随后运行的ArtifactMaxHPBonuses统一结算。
    }
}
