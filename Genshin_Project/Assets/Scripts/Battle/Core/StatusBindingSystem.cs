using System.Collections.Generic;

/// <summary>
/// BindStatus 的独立执行器。实体状态和场地状态使用同一套目标解析，并统一维护父子状态生命周期。
/// </summary>
public static class StatusBindingSystem
{
    public static void ExtendDuration(StatusInstance parent, int delta)
    {
        ExtendDuration(parent, delta, new HashSet<StatusInstance>());
    }

    private static void ExtendDuration(StatusInstance state, int delta, HashSet<StatusInstance> visited)
    {
        if (state == null || !visited.Add(state)) return;
        if (!state.IsPermanent) state.RemainingPhaseCount += delta;
        foreach (var child in state.BoundStatuses) ExtendDuration(child, delta, visited);
    }
    public static void ApplyBindings(StatusInstance parent, object host)
    {
        ApplyBindings(parent, host, new HashSet<string>());
    }

    public static void RemoveBindings(StatusInstance parent)
    {
        if (parent == null || parent.BoundStatuses == null || parent.BoundStatuses.Count == 0) return;
        var battle = BattleManager.Instance;
        if (battle == null)
        {
            parent.BoundStatuses.Clear();
            return;
        }

        var children = new List<StatusInstance>(parent.BoundStatuses);
        parent.BoundStatuses.Clear();
        foreach (var child in children)
        {
            if (child == null) continue;
            if (RemoveFromEntities(battle, child)) continue;
            RemoveFromFields(battle, child);
        }
    }

    internal static void ApplyBindings(StatusInstance parent, object host, HashSet<string> ancestry)
    {
        if (parent == null || parent.MainData == null) return;
        if (ancestry == null) ancestry = new HashSet<string>();
        if (!ancestry.Add(parent.StatusID2))
        {
            LogManager.LogWarning(LogCategory.Bind, $"BindStatus 检测到循环状态: {parent.StatusID2}");
            return;
        }

        var data = DataManager.Instance;
        var battle = BattleManager.Instance;
        if (data == null || battle == null || battle.Field == null) return;

        var effects = new List<StatusEffectData>();
        int parentStatusID = parent.MainData.StatusID;
        foreach (var pair in data.StatusEffectDict)
        {
            var effect = pair.Value;
            if (effect != null
                && effect.StatusEffectID / 100 == parentStatusID
                && effect.EffectType == "BindStatus")
                effects.Add(effect);
        }
        effects.Sort((left, right) => left.EffectIndex.CompareTo(right.EffectIndex));

        TargetResolutionResult previous = null;
        foreach (var effect in effects)
        {
            if (string.IsNullOrEmpty(effect.Param1)) continue;
            if (!ScriptHookEvaluator.Evaluate(effect.ScriptHook, new ScriptHookContext
            {
                Caster = parent.Caster,
                Status = parent,
                StatusHost = host,
                Target = host as BattleEntity,
                HitEffectID = parent.Caster?.CharacterCtrl?.LastHitEffectID2
            })) continue;
            if (effect.Param1 == parent.StatusID2)
            {
                LogManager.LogWarning(LogCategory.Bind, $"{effect.StatusEffectID2} 不能把父状态绑定为自己的子状态");
                continue;
            }
            if (!data.StatusMainDict.TryGetValue(effect.Param1, out var childMain))
            {
                LogManager.LogWarning(LogCategory.Bind, $"{effect.StatusEffectID2} 找不到子状态 {effect.Param1}");
                continue;
            }

            int targetNumber = string.IsNullOrWhiteSpace(effect.TargetSelect)
                && effect.TargetType != "Self"
                ? -1
                : 0;
            var result = BattleTargetResolver.Resolve(
                battle,
                parent.Caster,
                host,
                effect.TargetType,
                targetNumber,
                effect.TargetConsecutive,
                effect.TargetOverride,
                effect.TargetSelect,
                null,
                previous,
                selfUsesOrigin: true);

            if (!result.IsValid)
            {
                LogManager.LogWarning(LogCategory.Bind, $"{effect.StatusEffectID2} 目标无效: {result.Error}");
                continue;
            }
            previous = result;

            if (result.Domain == BattleTargetDomain.Field)
                BindToFields(parent, effect, childMain, result, ancestry);
            else
                BindToEntities(parent, effect, childMain, result);
        }

        ancestry.Remove(parent.StatusID2);
    }

    private static void BindToEntities(
        StatusInstance parent,
        StatusEffectData effect,
        StatusMainData childMain,
        TargetResolutionResult result)
    {
        var seen = new HashSet<BattleEntity>();
        foreach (var target in BattleTargetResolver.GetEntities(BattleManager.Instance, result))
        {
            if (target == null || !seen.Add(target)) continue;
            var previous = target.GetStatusInstances(effect.Param1);
            var child = target.AddStatus(
                effect.Param1,
                parent.Caster,
                parent.IsPermanent ? 0 : parent.RemainingPhaseCount,
                effect.AddInPhase > 0 ? effect.AddInPhase : parent.AddInPhase,
                effect.TriggerPhase,
                childMain, parent.WeaponContext, effect.Param2);
            // WhenMax=1 拒绝施加时返回旧实例，它不属于本次父状态，不能抢走清理权。
            if (child != null && !previous.Contains(child) && target.HasStatusInstance(child)
                && !parent.BoundStatuses.Contains(child))
                parent.BoundStatuses.Add(child);
        }
    }

    private static void BindToFields(
        StatusInstance parent,
        StatusEffectData effect,
        StatusMainData childMain,
        TargetResolutionResult result,
        HashSet<string> ancestry)
    {
        var seen = new HashSet<FieldPosition>();
        foreach (var field in BattleTargetResolver.GetFields(BattleManager.Instance, result))
        {
            if (field == null || !seen.Add(field)) continue;
            var previous = new List<StatusInstance>(field.StatusList);
            var child = field.AddStatus(effect.Param1, parent.Caster,
                parent.IsPermanent ? 0 : parent.RemainingPhaseCount,
                effect.AddInPhase > 0 ? effect.AddInPhase : parent.AddInPhase,
                effect.TriggerPhase, childMain, parent.WeaponContext,
                effect.Param2, ancestry);
            if (child != null && !previous.Contains(child) && field.StatusList.Contains(child))
                parent.BoundStatuses.Add(child);
        }
    }

    private static bool RemoveFromEntities(BattleManager battle, StatusInstance child)
    {
        foreach (var ally in battle.Allies)
        {
            if (ally != null && ally.Entity != null && ally.Entity.HasStatusInstance(child))
                return ally.Entity.RemoveStatusInstance(child);
        }
        foreach (var enemy in battle.Enemies)
        {
            if (enemy != null && enemy.Entity != null && enemy.Entity.HasStatusInstance(child))
                return enemy.Entity.RemoveStatusInstance(child);
        }
        return false;
    }

    private static bool RemoveFromFields(BattleManager battle, StatusInstance child)
    {
        if (battle.Field == null) return false;
        foreach (BattleSide side in new[] { BattleSide.Ally, BattleSide.Enemy })
        {
            var fields = side == BattleSide.Ally ? battle.Field.AllySlots : battle.Field.EnemySlots;
            foreach (var field in fields)
            {
                if (field != null && field.RemoveStatusInstance(child)) return true;
            }
        }
        return false;
    }
}
