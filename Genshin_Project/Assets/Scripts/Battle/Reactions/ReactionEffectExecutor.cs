/// <summary>统一执行反应生成的派生命中；派生 Hit 的元素量由 handler 明确填写。</summary>
public static class ReactionEffectExecutor
{
    /// <summary>主伤害完成护盾结算并实际扣血后，落地依赖扣血结果的碎冰。</summary>
    public static void FinalizePrimaryHit(ReactionResult result, float actualHPDamage)
    {
        if (result == null || result.PendingShatter == null) return;
        PendingShatterEffect pending = result.PendingShatter;
        result.PendingShatter = null;
        if (actualHPDamage <= 0f || pending.Target == null) return;

        ReactionStateSystem.RemoveEntityState(pending.Target, ReactionType.Frozen);
        PoiseSystem.BreakPoise(pending.Target, pending.Source);
        result.DerivedHits.Insert(0, new ReactionDerivedHit
        {
            Target = pending.Target,
            Damage = pending.Damage,
            DamageElement = "Physical",
            PoiseDamage = 0f,
            ElementAmount = 0f,
            // 碎冰来源声明（2026-08-19）：归属触发碎冰的攻击方角色/技能/效果
            Source = DamageSourceInfo.Create(
                pending.Source,
                pending.SourceKind,
                pending.SourceSkillID,
                pending.SourceEffectID,
                ReactionType.Shatter),
            ReactionType = ReactionType.Shatter
        });
        result.TriggeredReactions.Insert(0, new ReactionOccurrence
        {
            Type = ReactionType.Shatter,
            DisplayName = "碎冰",
            SourceEntity = pending.Source,
            Target = pending.Target,
            SourceEffectID = pending.SourceEffectID
        });
        LogManager.Log(LogCategory.Reaction, $"碎冰：{pending.Target.EntityID} 受到 {pending.Damage:F1} 物理剧变伤害");
    }

    public static void ExecuteDerivedHits(ReactionResult result)
    {
        if (result == null) return;

        foreach (ReactionDerivedHit hit in result.DerivedHits)
        {
            if (hit == null || hit.Target == null) continue;

            float finalDamage = hit.Target.AbsorbDamageWithShield(hit.Damage, hit.DamageElement);
            // 带来源扣血（2026-08-19）：反应派生伤害统一入口（来源快照保留原始归属）
            hit.Target.TakeDamage(finalDamage, hit.Source ?? DamageSourceInfo.CreateUnknown());
            PoiseSystem.ApplyPoiseDamage(hit.Target, hit.PoiseDamage, finalDamage);
        }
    }
}
