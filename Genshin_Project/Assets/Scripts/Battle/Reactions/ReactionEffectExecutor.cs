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
        using var resolution = BattleEventHub.BeginResolution();
        using var execution = BattleEventHub.BeginEffectExecution(result.EffectExecutionID);
        using var hit = BattleEventHub.BeginHit(result.RuntimeHitID, result.TargetIndex);
        using var cause = BattleEventHub.Enter(result.ContactDeclaration);

        ReactionStateSystem.RemoveEntityState(pending.Target, ReactionType.Frozen);
        result.DerivedHits.Insert(0, new ReactionDerivedHit
        {
            EnemyShieldHit = result.EnemyShieldHit,
            Target = pending.Target,
            Damage = pending.Damage,
            DamageElement = "Physical",
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
        result.DerivedHits[0].Source.EquipmentSource = pending.EquipmentSource;
        occurrence.Declaration = BattleEventHub.Record(occurrence, new ReactionEventData(occurrence));
        result.DerivedHits[0].CauseEventIds = new[] { occurrence.Declaration.Header.EventId };
        StatusOnHitHookSystem.NotifyReactions(new[] { occurrence });
        LogManager.Log(LogCategory.Reaction, $"碎冰：{pending.Target.EntityID} 受到 {pending.Damage:F1} 物理剧变伤害");
    }

    public static void ExecuteDerivedHits(ReactionResult result)
        => ExecuteDerivedHitBatch(new[] { result });

    /// <summary>同一逻辑hit全部目标一起提交雷暴云，不能逐目标生成并丢掉先前目标的贡献者。</summary>
    public static void ExecuteDerivedHitBatch(IReadOnlyList<ReactionResult> results)
    {
        using var resolutionEffect = BattleResolutionContext.BeginEffect();
        if (results == null) return;
        using var responses = BattleResolutionContext.BeginCollection();
        ReactionResult lunar = PrepareLunarBatch(results);
        foreach (var result in results) if (result != null) ExecuteDerivedHitsCore(result);
        if (lunar != null) ExecuteDerivedHitsCore(lunar);
    }

    /// <summary>本hit全部目标完成后汇总雷云和月笼，保留原逐目标碎冰/派生收尾顺序。</summary>
    public static ReactionResult PrepareLunarBatch(IReadOnlyList<ReactionResult> results)
    {
        var merged = new SwirlPreparedBatch();
        var all = new List<ReactionResult>();
        if (results != null) foreach (var source in results)
        {
            if (source == null) continue;
            all.Add(source);
            var pending = source.PendingReverseSwirl;
            source.PendingReverseSwirl = null; // 先消费计划，子响应及重复调用不重复扩散。
            if (pending == null || pending.Resolved) continue;
            merged.Targets.AddRange(pending.Targets);
            if (merged.EffectExecutionID == 0) merged.EffectExecutionID = pending.EffectExecutionID;
            pending.Resolved = true;
        }
        ReactionResult result = null;
        if (merged.Targets.Count > 0)
        {
            result = SwirlReactionHandler.ResolvePreparedBatch(merged).Reaction;
            all.Add(result);
        }
        var hits = LunarElectroChargedReactionHandler.CompleteBatch(all);
        hits.AddRange(LunarCrystallizeReactionHandler.CompleteBatch(all));
        if (result == null && hits.Count == 0) return null;
        result = result ?? new ReactionResult();
        result.DerivedHits.AddRange(hits);
        return result;
    }

    private static void ExecuteDerivedHitsCore(ReactionResult result)
    {
        StatusOnHitHookSystem.NotifyReactions(result.TriggeredReactions);

        HitBatch batch = null;
        long currentExplicitHit = 0;
        try
        {
            int targetIndex = 0;
            foreach (ReactionDerivedHit hit in result.DerivedHits)
            {
                if (hit == null || hit.Target == null) continue;
                if (batch == null || currentExplicitHit != hit.RuntimeHitID)
                {
                    batch?.Dispose();
                    batch = new HitBatch(hit.RuntimeHitID);
                    currentExplicitHit = hit.RuntimeHitID;
                    targetIndex = 0;
                }

                DamageSourceInfo source = hit.Source ?? DamageSourceInfo.CreateUnknown();
                source.CountsAsHit = false;
                HitResolution resolution = batch.Resolve(new HitRequest
                {
                    EnemyShieldHit = hit.EnemyShieldHit ?? (hit.RuntimeHitID == 0
                        && result.EnemyShieldHit?.Target == hit.Target ? result.EnemyShieldHit : null),
                    Properties = hit.Properties,
                    CauseEventIds = hit.CauseEventIds,
                    Target = hit.Target,
                    DamageBeforeShield = hit.Damage,
                    Element = hit.DamageElement,
                    ElementAmount = hit.ElementAmount,
                    DamageType = "Reaction",
                    Source = source,
                    HitIndex = 0,
                    TargetIndex = targetIndex++
                });
                batch.AfterCommit(resolution, hit.OnResolved); // 场地盾须等本hit全部目标准备完，再读取实际扣盾/血结果。
            }
        }
        finally { batch?.Dispose(); }
    }
}
