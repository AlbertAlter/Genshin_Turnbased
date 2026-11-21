using System.Collections.Generic;

/// <summary>角色、敌人共用的永久退场效果。复用死亡清理规则，以Withdrawn声明替代Death。</summary>
public static class WithdrawnEffect
{
    public static int Execute(IReadOnlyList<BattleEntity> targets, BattleEntity source = null)
    {
        var battle = BattleManager.Instance;
        if (targets == null || battle?.IsBattleOver == true || BattleExecution.IsCancelled) return 0;
        using var resolution = BattleEventHub.BeginResolution();
        using var collection = BattleResolutionContext.BeginCollection();
        var committed = new List<(BattleEntity Entity, float HP)>();
        var seen = new HashSet<BattleEntity>();
        // 同组目标先全部归零，再统一清理和声明；不在目标循环内提前判胜或执行响应。
        foreach (var target in targets)
        {
            if (target == null || !target.IsAlive || target.HasWithdrawn || !seen.Add(target)
                || battle != null && !battle.ContainsBattleEntity(target)) continue;
            committed.Add((target, target.CurrentHP));
            target.HasWithdrawn = true;
            target.CurrentHP = 0f;
        }
        foreach (var item in committed) EquipmentStatusLifetime.OnWithdrawn(item.Entity);
        foreach (var item in committed) CharacterStatusLifetime.OnWithdrawn(item.Entity);
        foreach (var item in committed)
        {
            if (item.Entity.Type == BattleEntity.EntityType.Enemy)
                foreach (var status in item.Entity.GetStatusList())
                    item.Entity.RemoveStatusInstance(status, "EnemyWithdrawn");
            battle?.DetachWithdrawnEntity(item.Entity);
        }
        foreach (var item in committed)
        {
            BattleEventHub.Publish(new HPChangedEventData(item.Entity, item.HP, 0f, "Withdrawn"));
            BattleEventHub.Publish(new WithdrawnEventData(item.Entity, source));
        }
        if (committed.Count > 0) battle?.CheckBattleEnd();
        return committed.Count;
    }
}
