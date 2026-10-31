using System.Collections.Generic;

/// <summary>角色死亡：按该角色状态表的ID全场清理，不按Caster筛选，不影响装备原有来源生命期规则。</summary>
public static class CharacterStatusLifetime
{
    internal static void OnDeath(BattleEntity owner)
    {
        var data = DataManager.Instance;
        if (owner == null || owner.IsAlive || owner.Type != BattleEntity.EntityType.Character || data == null
            || !data.CharacterStatusIDs.TryGetValue(owner.EntityID, out var ids) || ids.Count == 0) return;
        using var resolution = BattleEventHub.BeginResolution();
        using var collection = BattleResolutionContext.BeginCollection();
        var pending = new List<(StatusInstance Status, object Host)>();
        var seen = new HashSet<StatusInstance>();
        void Collect(object host, IEnumerable<StatusInstance> states)
        {
            foreach (var state in states)
                if (state != null && ids.Contains(state.StatusID2) && seen.Add(state)) pending.Add((state, host));
        }
        Collect(owner, owner.GetStatusList());
        if (owner.Position != null) Collect(owner.Position, owner.Position.StatusList);
        var battle = BattleManager.Instance;
        if (battle != null)
        {
            foreach (var ally in battle.Allies) if (ally?.Entity != null) Collect(ally.Entity, ally.Entity.GetStatusList());
            foreach (var enemy in battle.Enemies) if (enemy?.Entity != null) Collect(enemy.Entity, enemy.Entity.GetStatusList());
            foreach (var team in new[] { battle.AllyTeam, battle.EnemyTeam })
                if (team != null) Collect(team, team.GetStatusList());
            if (battle.Field != null)
            {
                foreach (var field in battle.Field.AllySlots) if (field != null) Collect(field, field.StatusList);
                foreach (var field in battle.Field.EnemySlots) if (field != null) Collect(field, field.StatusList);
            }
        }
        // 先快照再移除；绑定可能提前删掉后续项，正式移除接口保证幂等。
        pending.Sort((left, right) => left.Status.ApplyOrder.CompareTo(right.Status.ApplyOrder));
        foreach (var item in pending) BattleDeathResolution.RemoveAfterDeathActions(item.Host, item.Status, "CharacterDied");
        // 保留正常StatusRemoved/解绑/护盾/属性声明及后续响应，不执行OnEnd、不伪造Count消费。
        // 此后响应合法新建的状态不是本次快照，不循环清理，以免吞掉触发或形成无限清理。
    }
}
