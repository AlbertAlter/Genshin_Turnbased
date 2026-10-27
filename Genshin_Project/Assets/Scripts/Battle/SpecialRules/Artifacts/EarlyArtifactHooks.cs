/// <summary>502～509中需要专用处理的条件。数值仍由原Buff/ApplyStatus/GainEnergy效果提供。</summary>
public static class EarlyArtifactHooks
{
    private const string Guardian = "ST_Artifact_450302";
    private const string Miracle = "ST_Artifact_450402";
    private static readonly string[] Elements = { "Pyro", "Hydro", "Electro", "Cryo", "Anemo", "Geo", "Dendro" };

    internal static void Register()
    {
        BattleSpecialRules.RegisterNumberedCondition("450202", (argument, context) =>
            ArtifactHitSnapshots.TargetHPAbove(context, 0.50f));
        BattleSpecialRules.RegisterNumberedCondition("450502", (argument, context) =>
        {
            BattleEntity owner = context?.Status?.Caster ?? context?.Caster;
            return owner != null && owner.GetFinalHP() > 0f && owner.CurrentHP < owner.GetFinalHP() * 0.70f;
        });
        for (int index = 0; index < Elements.Length; index++)
        {
            string element = Elements[index];
            BattleSpecialRules.RegisterNumberedCondition("4503020" + (index + 1), (argument, context) =>
            {
                var team = BattleManager.Instance?.TeamBonuses;
                BattleEntity owner = context?.Status?.Caster ?? context?.Caster;
                return team != null && !team.IsDisposed && team.Contains(owner) && team.CountElement(element) > 0;
            });
        }
        BattleSpecialRules.RegisterBattleReady(RefreshGuardianBindings);
        BattleSpecialRules.RegisterNumberedCondition("450402", (argument, context) =>
        {
            var damage = context?.DamageEvent;
            if (context?.Status?.StatusID2 != Miracle || damage == null
                || damage.Target != context.Status.Caster || damage.ActualHPDamage <= 0f) return false;
            foreach (string element in Elements) if (damage.Element == element) return true;
            return false; // 物理、无元素及非正式受伤事实不触发。
        });
        // 七种元素行动共享同一个运行时账本，包含确认阶段的Pending预留；不是执行后才补查冷却。
        BattleSpecialRules.RegisterActionCounterGroup(Miracle, action =>
            action != null && action.ActionType == "OnTrigger" && action.Cooldown > 0
            && ScriptHookParser.TryFind(action.ScriptHook, "Hook", out string id) && id?.Trim() == "450402"
                ? "MiracleElementCooldown" : null);

        BattleSpecialRules.RegisterNumberedCondition("45090301", (argument, context) => ExileTick(context, "ST_Artifact_450903", 1));
        BattleSpecialRules.RegisterNumberedCondition("45090401", (argument, context) => ExileTick(context, "ST_Artifact_450904", 3));
    }

    private static bool ExileTick(ScriptHookContext context, string statusID, int phase)
    {
        var status = context?.Status;
        return status != null && status.IsActive && status.StatusID2 == statusID
            && status.Caster != null && context.Caster == status.Caster
            && ReferenceEquals(context.StatusHost, status.Caster) && status.TriggerPhase == phase
            && BattleManager.Instance != null && (int)BattleManager.Instance.CurrentPhase == phase;
        // 仅确认本次阶段来源。AlliesOnly由公共解析器排除穿戴者，后台不排除；不在此改能量或选目标。
    }

    private static void RefreshGuardianBindings(BattleManager battle)
    {
        if (battle?.TeamBonuses == null || battle.TeamBonuses.IsDisposed) return;
        foreach (var ally in battle.Allies)
        {
            if (ally == null || ally.Entity == null) continue;
            foreach (var parent in ally.Entity.GetStatusInstances(Guardian))
            {
                if (!parent.IsActive) continue;
                bool bound = false;
                foreach (var child in parent.BoundStatuses)
                    if (child != null && child.IsActive) { bound = true; break; }
                if (!bound) StatusBindingSystem.ApplyBindings(parent, ally.Entity);
            }
        }
        // 角色逐个构筑时尚无完整编队，不能按当时的半队快照漏掉后续成员；初始化完成后补绑定一次。
    }
}
