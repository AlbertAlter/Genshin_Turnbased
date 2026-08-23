using System.Collections.Generic;

/// <summary>
/// BindStatus 的独立执行器。实体状态和场地状态使用同一套目标解析，并统一维护父子状态生命周期。
/// </summary>
public static class StatusBindingSystem
{
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

    private static void ApplyBindings(StatusInstance parent, object host, HashSet<string> ancestry)
    {
        if (parent == null || parent.MainData == null) return;
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
            var child = target.AddStatus(
                effect.Param1,
                parent.Caster,
                parent.RemainingPhaseCount,
                effect.AddInPhase,
                effect.TriggerPhase,
                childMain);
            if (child != null && !parent.BoundStatuses.Contains(child))
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
            if (field.StatusList.Exists(status => status != null && status.StatusID2 == effect.Param1))
                continue;

            var child = new StatusInstance
            {
                StatusID2 = effect.Param1,
                StackCount = 1,
                Caster = parent.Caster,
                RemainingPhaseCount = parent.RemainingPhaseCount,
                AddInPhase = effect.AddInPhase,
                TriggerPhase = effect.TriggerPhase,
                MainData = childMain,
                IsActive = true,
                ApplyOrder = ++BattleEntity._applyOrderCounter
            };
            field.StatusList.Add(child);
            parent.BoundStatuses.Add(child);

            ApplyBindings(child, field, ancestry);
            if (child.Caster != null && child.Caster.CharacterCtrl != null)
                child.Caster.CharacterCtrl.OnStatusApplied(child, field);
            BattleManager.Instance.RegisterStatusTick(child, null, field);
            PreDamageHookSystem.RegisterPreDamageHook(child);
            // Kill 钩子登记（2026-08-19，逻辑见 KillHookSystem.cs）
            KillHookSystem.Register(child, field);
        }
    }

    private static bool RemoveFromEntities(BattleManager battle, StatusInstance child)
    {
        foreach (var ally in battle.Allies)
        {
            if (ally != null && ally.Entity != null && ally.Entity.GetStatus(child.StatusID2) == child)
                return ally.Entity.RemoveStatus(child.StatusID2, -1);
        }
        foreach (var enemy in battle.Enemies)
        {
            if (enemy != null && enemy.Entity != null && enemy.Entity.GetStatus(child.StatusID2) == child)
                return enemy.Entity.RemoveStatus(child.StatusID2, -1);
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
                if (field == null || !field.StatusList.Remove(child)) continue;
                RemoveBindings(child);
                battle.UnregisterStatusTick(child);
                PreDamageHookSystem.UnregisterPreDamageHook(child.StatusID2);
                // Kill 钩子注销（2026-08-19，逻辑见 KillHookSystem.cs）
                KillHookSystem.Unregister(child);
                return true;
            }
        }
        return false;
    }
}
