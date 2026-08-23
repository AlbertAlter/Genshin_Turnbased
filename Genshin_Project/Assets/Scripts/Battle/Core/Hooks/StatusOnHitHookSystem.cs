using System.Collections.Generic;

/// <summary>
/// 统一 OnHit 触发系统（2026-08-19，状态钩子任务）：
/// 所有角色/敌人/状态/反应/燃烧伤害统一在 BattleEntity.TakeDamage 内调用 NotifyHit，
/// 由本系统读取受击实体身上及所在位置的场地状态，匹配 StatusAction-OnHit 行执行。
/// 敌人攻击角色同样走此入口；Kaeya_C4 只是使用本系统的钩子之一，不独占脚本。
/// </summary>
public static class StatusOnHitHookSystem
{
    /// <summary>
    /// 受击通知入口：遍历受击实体状态 + 所在位置场地状态（快照副本，避免执行中修改集合）。
    /// 每个满足 HitSource / ScriptHook / 次数限制的 OnHit 行动由施放者控制器统一执行。
    /// </summary>
    public static void NotifyHit(DamageResolvedEvent damageEvent)
    {
        if (damageEvent == null || damageEvent.Target == null) return;
        var dm = DataManager.Instance;
        if (dm == null) return;

        BattleEntity target = damageEvent.Target;

        // 1. 实体身上的状态
        foreach (StatusInstance inst in target.GetStatusList())
            NotifyHitForStatus(inst, target, damageEvent, dm);

        // 2. 实体所在位置的场地状态（目标死亡后场地状态依然存在）
        if (target.Position != null)
        {
            foreach (StatusInstance inst in new List<StatusInstance>(target.Position.StatusList))
                NotifyHitForStatus(inst, target.Position, damageEvent, dm);
        }
    }

    private static void NotifyHitForStatus(
        StatusInstance inst,
        object host,
        DamageResolvedEvent damageEvent,
        DataManager dm)
    {
        if (inst == null || inst.Caster == null || inst.Caster.CharacterCtrl == null) return;
        if (!dm.StatusActionDict.TryGetValue(inst.StatusID2, out var actions)) return;

        string hitEffectID = damageEvent.Source != null ? damageEvent.Source.SourceEffectID : null;
        foreach (StatusActionData act in actions)
        {
            if (act.ActionType != "OnHit") continue;
            // HitSource：只有被指定效果命中后才触发此次 OnHit 对应行动
            if (!string.IsNullOrEmpty(act.HitSource) && act.HitSource != hitEffectID) continue;

            var context = new ScriptHookContext
            {
                Caster = inst.Caster,
                Target = damageEvent.Target,
                Status = inst,
                StatusHost = host,
                HitEffectID = hitEffectID,
                DamageEvent = damageEvent,
                ActionTargetPositions = BattleManager.Instance != null
                    ? BattleManager.Instance.PendingActionTargetPositions
                    : null
            };
            // 钩子求值 + 每回合/全场次数 + 冷却 + 执行统一走施放者控制器入口
            inst.Caster.CharacterCtrl.TryExecuteEventStatusAction(inst, act, host, context);
        }
    }
}
