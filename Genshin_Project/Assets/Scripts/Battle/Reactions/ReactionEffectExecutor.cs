using System.Collections.Generic;

/// <summary>统一执行反应生成的派生命中；派生 Hit 的元素量由 handler 明确填写。</summary>
public static class ReactionEffectExecutor
{
    /// <summary>主命中结算后落地碎冰；护盾吸收、实际扣血为零也不阻止碎冰。</summary>
    public static void FinalizePrimaryHit(ReactionResult result, float actualHPDamage)
    {
        if (result == null || result.PendingShatter == null) return;
        PendingShatterEffect pending = result.PendingShatter;
        result.PendingShatter = null;
        if (pending.Target == null) return;

        ReactionStateSystem.RemoveEntityState(pending.Target, ReactionType.Frozen);
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
                ReactionType.Shatter,
                countsAsHit: false),
            ReactionType = ReactionType.Shatter
        });
        ReactionOccurrence occurrence = new ReactionOccurrence
        {
            Type = ReactionType.Shatter,
            DisplayName = "碎冰",
            SourceEntity = pending.Source,
            Target = pending.Target,
            SourceEffectID = pending.SourceEffectID,
            InvolvedElements = new List<string> { "Cryo" }
        };
        result.TriggeredReactions.Insert(0, occurrence);
        StatusOnHitHookSystem.NotifyReactions(new[] { occurrence });
        LogManager.Log(LogCategory.Reaction, $"碎冰：{pending.Target.EntityID} 受到 {pending.Damage:F1} 物理剧变伤害");
    }

    public static void ExecuteDerivedHits(ReactionResult result)
    {
        using var resolutionEffect = BattleResolutionContext.BeginEffect();
        if (result == null) return;
        using var responses = BattleResolutionContext.BeginCollection();
        StatusOnHitHookSystem.NotifyReactions(result.TriggeredReactions);

        using (var batch = new HitBatch())
        {
            int targetIndex = 0;
            foreach (ReactionDerivedHit hit in result.DerivedHits)
            {
                if (hit == null || hit.Target == null) continue;

                DamageSourceInfo source = hit.Source ?? DamageSourceInfo.CreateUnknown();
                source.CountsAsHit = false;
                batch.Resolve(new HitRequest
                {
                    Target = hit.Target,
                    DamageBeforeShield = hit.Damage,
                    Element = hit.DamageElement,
                    DamageType = "Reaction",
                    // 派生反应是否削韧由 handler 产生的数值决定，与 CountsAsHit 无关。
                    PoiseDamage = hit.PoiseDamage,
                    Source = source,
                    HitIndex = 0,
                    TargetIndex = targetIndex++
                });
            }
            batch.Flush();
        }
    }
}
