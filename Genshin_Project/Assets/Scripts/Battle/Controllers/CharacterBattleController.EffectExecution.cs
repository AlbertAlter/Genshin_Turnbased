using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public partial class CharacterBattleController
{
    // ================================================================
    //  效果执行器
    // ================================================================
    void ExecuteEffect(SkillEffectData eff, int skillType)
        => BattleExecution.Enqueue(() => ExecuteEffectAsync(eff, skillType));

    async Task ExecuteEffectAsync(SkillEffectData eff, int skillType, EffectSelectionPlan plan = null)
    {
        if (eff == null || !EquipmentStatusLifetime.CanExecute(_currentEffectSourceStatus)) return;
        var callerPositions = plan != null ? new List<int>(ForcedTargetPositions) : null;
        if (plan != null) UsePlannedTargets(plan);
        try
        {
            // 预选只保存目标；倍率、次数和条件仍在原执行时机求值。
            BattleSpecialRules.ValidateDamageHook(eff.DamageHook, Entity, eff.EffectType);
            int count = ExecuteCountEvaluator.Evaluate(eff.ExecuteCount, eff.ExecuteCountHandler, Entity);
            if (count == 0) return;
            await BattleExecution.RepeatEffect(count, null,
                executionID => ExecuteEffectOnceAsync(eff, skillType, executionID, plan), CurrentDamageSourceEntity, eff.SkillEffectID2);
        }
        finally
        {
            if (plan != null)
            {
                ForcedTargetPositions.Clear();
                ForcedTargetPositions.AddRange(callerPositions);
            }
        }
    }

    async Task ExecuteEffectOnceAsync(SkillEffectData eff, int skillType, long executionID, EffectSelectionPlan plan = null)
    {
        using var resolutionEffect = BattleResolutionContext.BeginEffect();
        BattleExecution.ThrowIfCancelled();
        if (eff == null || !EquipmentStatusLifetime.CanExecute(_currentEffectSourceStatus)
            || !ScriptHookEvaluator.Evaluate(eff.ScriptHook, Entity, LastHitEffectID2)) return;
        switch (eff.EffectType)
        {
            case "Damage": await ExecuteDamageAsync(eff, skillType, executionID); break;
            case "Heal": await ExecuteHealAsync(eff, skillType); break;
            default:
                await BattleExecution.Play(new BattlePresentationRequest(
                    BattlePresentationKind.Effect, CurrentDamageSourceEntity, eff.SkillEffectID2), () =>
                {
                    if (!EquipmentStatusLifetime.CanExecute(_currentEffectSourceStatus)) return;
                    switch (eff.EffectType)
                    {
                        case "ApplyStatus": ExecuteApplyStatus(eff); break;
                        case "RemoveStatus": ExecuteRemoveStatus(eff); break;
                        case "ExecuteSkill": ExecuteExecuteSkill(eff, plan?.CalledSkill); break;
                        case "ExecuteEffect": ExecuteExecuteEffect(eff, plan?.CalledEffect); break;
                        case "GainEnergy": ExecuteGainEnergy(eff); break;
                        case "SwitchIn": SwitchInEffect.Execute(ResolveTargets(eff)); break;
                        case "Revive":
                            ReviveEffect.Execute(eff.Param1, CurrentDamageSourceEntity, ResolveTargets(eff), _currentEffectSourceStatus);
                            break;
                        case "ChangeControl": ExecuteChangeControl(eff); break;
                        case "ResetCooldown": ExecuteResetCooldown(eff.Param1, eff.SkillEffectID2); break;
                        case "ReduceCooldown": ExecuteReduceCooldown(eff.Param1, eff.Param2, eff.SkillEffectID2); break;
                        case "BondOfLife":
                            BondOfLifeEffect.Execute(eff.Param1, CurrentDamageSourceEntity, ResolveTargets(eff),
                                _currentEffectSourceStatus, eff.SkillEffectID2, skillType, eff.Element);
                            break;
                        case "NightSoul":
                            NightSoulEffect.Execute(eff.Param1, ResolveTargets(eff), eff.SkillEffectID2);
                            break;
                        case "Count":
                            CountEffect.Execute(eff.Param1, eff.Param2, CurrentDamageSourceEntity,
                                ResolveTargets(eff), eff.SkillEffectID2, _currentEffectSourceStatus, eff.TargetType);
                            break;
                        case "ApplyElement":
                            ApplyElementEffect.Execute(eff.Element, eff.Param1, CurrentDamageSourceEntity,
                                ResolveTargets(eff), CurrentDamageSourceKind, GetCurrentDamageSourceSkillID(skillType),
                                eff.SkillEffectID2, executionID, _currentEffectSourceStatus);
                            break;
                        default: LogManager.LogWarning(LogCategory.Effect, $"未知类型: {eff.EffectType}"); break;
                    }
                });
                break;
        }
        await BattleExecution.Flush();
    }

    /// <summary>治疗效果（2026-08-14）：Param1=治疗量公式（如 0.15*TotalATK），目标=TargetType解析。</summary>
    async Task ExecuteHealAsync(SkillEffectData eff, int skillType)
    {
        var targets = ResolveTargets(eff);
        if (targets == null) return;
        await ExecuteHealingSegmentsAsync(targets, eff.SkillEffectID2, eff.Param1, skillType,
            eff.Element, CurrentDamageSourceEntity, _currentEffectSourceStatus);
    }

    // ----------------------------------------------------------------
    //  Damage
    // ----------------------------------------------------------------
    BattleEntity CurrentDamageSourceEntity
        => _currentEffectSourceStatus != null && _currentEffectSourceStatus.Caster != null
            ? _currentEffectSourceStatus.Caster
            : Entity;

    ReactionSourceKind CurrentDamageSourceKind
        => _currentEffectSourceStatus != null
            ? ReactionSourceKind.StatusEffect
            : ReactionSourceKind.CharacterSkill;

    string GetCurrentDamageSourceSkillID(int skillType)
        => _currentEffectSourceStatus != null ? string.Empty : GetExecutingSkillID2(skillType);

    DamageSourceInfo CreateCurrentDamageSource(
        string effectID,
        int skillType,
        ReactionType reactionType)
    {
        return _currentEffectSourceStatus != null
            ? DamageSourceInfo.FromStatus(_currentEffectSourceStatus, effectID, reactionType)
            : DamageSourceInfo.Create(
                Entity,
                ReactionSourceKind.CharacterSkill,
                GetExecutingSkillID2(skillType),
                effectID,
                reactionType);
    }

    async Task ExecuteDamageAsync(SkillEffectData authoredEffect, int skillType, long effectExecutionID)
    {
        var eff = authoredEffect;
        using var damageQuery = DamageQueryContext.Enter(eff.SkillEffectID2, eff.DamageType,
            GetCurrentDamageSourceSkillID(skillType));
        using var completion = BattleSpecialRules.BeginDamageEffect(CurrentDamageSourceEntity, eff.SkillEffectID2, effectExecutionID);
        DamageProperties.Parse(eff.Element, eff.Param3).RequireImplementedFormula();
        if (DamageProperties.IsFixed(eff.Element))
        {
            TableValidator.ValidateDamageProperties("SkillsEffect", 0, "Damage", eff.Element,
                eff.Param1, eff.Param2, eff.Param3, eff.DamageHook);
            await ExecuteTrueSkillDamageAsync(eff, skillType);
            return;
        }
        // 多段 HitData（Hits1-7，每段独立结算；空段跳过）
        var hits = GetHitDataList(eff, skillType);
        if (hits.Count == 0 || (hits.Count == 1 && hits[0].Multiplier <= 0))
        {
            LogManager.LogWarning(LogCategory.Damage, $"{eff.SkillEffectID2} 倍率为0或未匹配等级行 (skillType={skillType})，无伤害");
            return;
        }

        // 基准值（Param1 决定用哪个属性；% 引用由 ResolveBaseValue 处理）
        string[] directHits = GetDirectHitExpressions(eff.Param1);

        // 选目标（支持 TargetType/TargetNumber/TargetConsecutive/TargetOverride）
        var targets = ResolveTargets(eff);
        if (targets == null || targets.Count == 0)
        {
            LogManager.Log(LogCategory.Damage, $"{Entity.EntityID} 无目标，攻击落空 ({eff.SkillEffectID2})");
            return;
        }

        using (StatusOnHitHookSystem.BeginDamageEffect(
                   Entity,
                   eff.SkillEffectID2,
                   _currentExecutingSkillIsActiveAction))
        {
        // 命中记录（Hit()钩子：有目标即算命中，不管是否造成伤害/伤害数值）
        LastHitEffectID2 = eff.SkillEffectID2;
        //记录序列内命中（Hit()钩子：序列内后续效果可判定"该效果命中过"）
        if (!string.IsNullOrEmpty(eff.SkillEffectID2))
            _sequenceHitEffectIDs.Add(eff.SkillEffectID2);


        // hit 是层：先让本 hit 的全部目标完成护盾/扣血，再按全局 ApplyOrder 触发状态树。
        for (int hitIndex = 0; hitIndex < hits.Count; hitIndex++)
        {
            // 每hit只读选取一次；同批全部目标共用，后续hit可以读取前一hit移除/施加后的状态。
            var enchant = EnchantStatusSystem.Resolve(CurrentDamageSourceEntity, authoredEffect.DamageType, authoredEffect.Element);
            eff = enchant.HasValue ? authoredEffect.WithDamageElement(enchant.Value.Element) : authoredEffect;
            if (enchant.HasValue) hits[hitIndex] = new HitData(hits[hitIndex].Multiplier, enchant.Value.ElementAmount);
            float baseValue = directHits != null ? 0f : ResolveBaseValue(eff.Param1, eff.SkillEffectID2, skillType, eff.Element);
            if (eff.Element == "Geo" && targets.Count > 1)
                targets = targets.OrderBy(target => target != null ? target.SlotPosition : int.MaxValue).ToList();
            if (eff.Element == "Anemo")
            {
                await ExecuteAnemoDamageAsync(eff, skillType, new List<HitData> { hits[hitIndex] }, baseValue,
                    targets, effectExecutionID, directHits == null ? null : new[] { directHits[hitIndex] },
                    hitIndex, enchant?.ElementAmount);
                continue;
            }
            await BattleExecution.Play(new BattlePresentationRequest(BattlePresentationKind.Hit, CurrentDamageSourceEntity, eff.SkillEffectID2, hitIndex + 1, targets), () =>
            {
                if (!EquipmentStatusLifetime.CanExecute(_currentEffectSourceStatus)) return;
                using var hitResponses = BattleResolutionContext.BeginCollection();
                using var countDamage = new CountDamageBatch();
                HitData hit = hits[hitIndex];
                var hitTargets = DamageHitTargets.Expand(targets, eff.Param2, eff.Element);
                long logicalHitID = BattleEventHub.NextIdentity();
                using var hitSnapshot = BattleSpecialRules.BeginHit(logicalHitID, CurrentDamageSourceEntity,
                    hitTargets.Select(entry => entry.Target));
                float hitBaseValue = directHits != null ? ResolveDirectHitBase(directHits[hitIndex], eff, skillType) : baseValue;
                var primaryReactions = new List<ReactionResult>();
                var primaryResolutions = new List<HitResolution>();
                var primaryRawDamages = new List<float>();
                var resolvedHits = new List<HitData>();

                using (var batch = new HitBatch(logicalHitID))
                {
                    for (int targetIndex = 0; targetIndex < hitTargets.Count; targetIndex++)
                    {
                        var entry = hitTargets[targetIndex];
                        BattleEntity target = entry.Target;
                        HitData targetHit = entry.Scale(BattleSpecialRules.ModifyHit(eff.DamageHook, Entity, target, hit,
                            GetEffectiveSkillLevel(skillType)));
                        if (enchant.HasValue) targetHit.ElementAura = enchant.Value.ElementAmount;
                        CountDamagePlan countPlan = countDamage.Prepare(new CountDamageContext
                        {
                            Attacker = CurrentDamageSourceEntity, Target = target, SourceKind = CurrentDamageSourceKind,
                            EffectID = eff.SkillEffectID2, SkillID = GetCurrentDamageSourceSkillID(skillType),
                            DamageType = eff.DamageType, Element = eff.Element, SkillType = skillType,
                            EffectExecutionID = effectExecutionID, HitIndex = hitIndex + 1, TargetIndex = targetIndex + 1
                        });
                        float damage = CalculateDamage(
                            hitBaseValue,
                            targetHit,
                            eff,
                            target,
                            skillType,
                            out ReactionDamageComponents damageComponents,
                            countPlan.BaseDamageBonus);
                        DMGTakenSnapshot taken = target.PrepareDMGTaken(
                            eff.SkillEffectID2, GetCurrentDamageSourceSkillID(skillType), eff.DamageType, eff.Element);
                        ReactionResult reaction = ReactionResolver.Resolve(new ReactionContext
                        {
                            SourceEntity = CurrentDamageSourceEntity,
                            EquipmentSource = _currentEffectSourceStatus?.EquipmentSource,
                            Target = target,
                            SourceKind = CurrentDamageSourceKind,
                            SourceSkillID = GetCurrentDamageSourceSkillID(skillType),
                            SourceEffectID = eff.SkillEffectID2,
                            EffectExecutionID = effectExecutionID,
                            LogicalHitID = batch.RuntimeHitID,
                            TargetIndex = targetIndex + 1,
                            AttackElement = eff.Element,
                            AttackAmount = targetHit.ElementAura,
                            PreReactionDamage = damage,
                            DamageComponents = damageComponents,
                            DamageType = eff.DamageType,
                        });
                        StatusOnHitHookSystem.NotifyReactions(reaction.TriggeredReactions);
                        if (reaction.HasReaction)
                            LogManager.Log(LogCategory.Damage, $"{Entity.EntityID}攻击 {target.EntityID} 触发{reaction.DisplayNameSummary}");

                        CriticalHitResult primaryCritical = ResolveFinalCriticalDamage(
                            reaction.FinalDamage,
                            target,
                            eff.SkillEffectID2,
                            GetExecutingSkillID2(skillType),
                            skillType,
                            eff.Element,
                            hitIndex + 1,
                            entry.IsSplash ? "技能溅射" : "技能直伤", damageReaction: reaction.PrimaryDamageReactionType);
                        ReactionType mainReactionType = reaction.PrimaryDamageReactionType;
                        HitResolution primaryResolution = batch.Resolve(new HitRequest
                        {
                            Properties = DamageProperties.Parse(eff.Element, eff.Param3),
                            Target = target,
                            DamageBeforeShield = primaryCritical.DamageAfterCritical,
                            Critical = primaryCritical,
                            Element = eff.Element,
                            ElementAmount = targetHit.ElementAura,
                            DamageType = eff.DamageType,
                            DMGTaken = taken,
                            EnemyShieldHit = reaction.EnemyShieldHit,
                            Source = CreateCurrentDamageSource(
                                eff.SkillEffectID2, skillType, mainReactionType),
                            HitIndex = hitIndex,
                            TargetIndex = targetIndex,
                            CountPlan = countPlan
                        });
                        primaryReactions.Add(reaction);
                        primaryResolutions.Add(primaryResolution);
                        primaryRawDamages.Add(damage);
                        resolvedHits.Add(targetHit);

                        if (entry.IsSplash)
                            LogManager.Log(LogCategory.Splash,
                                $"hit#{hitIndex + 1} -> {target.EntityID} : {primaryResolution.DamageAfterShield:F1} " +
                                $"(暴击前{reaction.FinalDamage:F1}，倍率{entry.Rate}，元素量{targetHit.ElementAura})");
                    }
                    batch.Flush();
                }

                // 先完成本 Hit 的反应结果；作用域结束时统一确认并执行全局状态响应。
                ReactionResult lunar = ReactionEffectExecutor.PrepareLunarBatch(primaryReactions);
                for (int index = 0; index < primaryReactions.Count; index++)
                {
                    HitResolution resolution = primaryResolutions[index];
                    float actualHPDamage = resolution?.DamageEvent != null
                        ? resolution.DamageEvent.ActualHPDamage
                        : 0f;
                    ReactionEffectExecutor.FinalizePrimaryHit(primaryReactions[index], actualHPDamage);
                    ReactionEffectExecutor.ExecuteDerivedHits(primaryReactions[index]);
                    LogManager.Log(LogCategory.Damage,
                        $"{Entity.EntityID} 攻击 {resolution.Request.Target.EntityID} : {resolution.DamageAfterShield:F1} " +
                        $"({primaryRawDamages[index]:F1} raw, {resolvedHits[index].Multiplier}×{hitBaseValue:F1}) | {eff.Element}");
                }
                ReactionEffectExecutor.ExecuteDerivedHits(lunar);
            });
        }
        }
    }

    private async Task ExecuteAnemoDamageAsync(SkillEffectData eff, int skillType, List<HitData> hits,
        float baseValue, List<BattleEntity> targets, long effectExecutionID, string[] directHits,
        int hitOffset = 0, float? enchantAmount = null)
    {
        for (int hitIndex = 0; hitIndex < hits.Count; hitIndex++)
        {
            int actualHitIndex = hitOffset + hitIndex;
            await BattleExecution.Play(new BattlePresentationRequest(BattlePresentationKind.Hit, CurrentDamageSourceEntity, eff.SkillEffectID2, actualHitIndex + 1, targets), () =>
            {
                if (!EquipmentStatusLifetime.CanExecute(_currentEffectSourceStatus)) return;
                using var hitResponses = BattleResolutionContext.BeginCollection();
                using var countDamage = new CountDamageBatch();
                HitData hit = hits[hitIndex];
                var hitTargets = DamageHitTargets.Expand(targets, eff.Param2, eff.Element);
                long logicalHitID = BattleEventHub.NextIdentity();
                using var hitSnapshot = BattleSpecialRules.BeginHit(logicalHitID, CurrentDamageSourceEntity,
                    hitTargets.Select(entry => entry.Target));
                var contexts = new List<ReactionContext>();
                var countPlans = new List<CountDamagePlan>();
                var takenSnapshots = new List<DMGTakenSnapshot>();
                foreach (var entry in hitTargets)
                {
                    BattleEntity target = entry.Target;
                    HitData targetHit = entry.Scale(BattleSpecialRules.ModifyHit(eff.DamageHook, Entity, target, hit,
                        GetEffectiveSkillLevel(skillType)));
                    if (enchantAmount.HasValue) targetHit.ElementAura = enchantAmount.Value;
                    float hitBaseValue = directHits != null ? ResolveDirectHitBase(directHits[hitIndex], eff, skillType) : baseValue;
                    CountDamagePlan countPlan = countDamage.Prepare(new CountDamageContext
                    {
                        Attacker = CurrentDamageSourceEntity, Target = target, SourceKind = CurrentDamageSourceKind,
                        EffectID = eff.SkillEffectID2, SkillID = GetCurrentDamageSourceSkillID(skillType),
                        DamageType = eff.DamageType, Element = "Anemo", SkillType = skillType,
                        EffectExecutionID = effectExecutionID, HitIndex = actualHitIndex + 1, TargetIndex = contexts.Count + 1
                    });
                    countPlans.Add(countPlan);
                    takenSnapshots.Add(target.PrepareDMGTaken(eff.SkillEffectID2,
                        GetCurrentDamageSourceSkillID(skillType), eff.DamageType, "Anemo"));
                    float damage = CalculateDamage(hitBaseValue, targetHit, eff, target, skillType,
                        out ReactionDamageComponents components, countPlan.BaseDamageBonus);
                    contexts.Add(new ReactionContext
                    {
                        SourceEntity = CurrentDamageSourceEntity, Target = target,
                        EquipmentSource = _currentEffectSourceStatus?.EquipmentSource,
                        SourceKind = CurrentDamageSourceKind,
                        SourceSkillID = GetCurrentDamageSourceSkillID(skillType), SourceEffectID = eff.SkillEffectID2,
                        EffectExecutionID = effectExecutionID, AttackElement = "Anemo",
                        LogicalHitID = logicalHitID, TargetIndex = contexts.Count + 1,
                        AttackAmount = targetHit.ElementAura, PreReactionDamage = damage,
                        DamageComponents = components, DamageType = eff.DamageType
                    });
                }
                var primaryReactions = new List<ReactionResult>(contexts.Count);
                foreach (ReactionContext context in contexts)
                {
                    ReactionResult primaryReaction = ReactionResolver.Resolve(context);
                    StatusOnHitHookSystem.NotifyReactions(primaryReaction.TriggeredReactions);
                    primaryReactions.Add(primaryReaction);
                }

                SwirlPreparedBatch prepared = SwirlReactionHandler.PrepareBatch(contexts);
                var resolutions = new List<HitResolution>(contexts.Count);
                using (var batch = new HitBatch(logicalHitID))
                {
                    for (int index = 0; index < contexts.Count; index++)
                    {
                        ReactionContext context = contexts[index];
                        BattleEntity target = context.Target;
                        CriticalHitResult critical = ResolveFinalCriticalDamage(
                            primaryReactions[index].FinalDamage,
                            target,
                            eff.SkillEffectID2,
                            GetExecutingSkillID2(skillType),
                            skillType,
                            "Anemo",
                            actualHitIndex + 1,
                            "风元素直伤", damageReaction: primaryReactions[index].PrimaryDamageReactionType);
                        resolutions.Add(batch.Resolve(new HitRequest
                        {
                            Properties = DamageProperties.Parse(eff.Element, eff.Param3),
                            Target = target,
                            DamageBeforeShield = critical.DamageAfterCritical,
                            Critical = critical,
                            Element = "Anemo",
                            ElementAmount = context.AttackAmount,
                            DamageType = eff.DamageType,
                            EnemyShieldHit = primaryReactions[index].EnemyShieldHit,
                            DMGTaken = takenSnapshots[index],
                            Source = CreateCurrentDamageSource(
                                eff.SkillEffectID2, skillType, ReactionType.None),
                            HitIndex = actualHitIndex,
                            TargetIndex = index,
                            CountPlan = countPlans[index]
                        }));
                    }
                    batch.Flush();
                }
                for (int index = 0; index < primaryReactions.Count; index++)
                {
                    HitResolution resolution = resolutions[index];
                    float actualHPDamage = resolution?.DamageEvent != null
                        ? resolution.DamageEvent.ActualHPDamage
                        : 0f;
                    ReactionEffectExecutor.FinalizePrimaryHit(primaryReactions[index], actualHPDamage);
                    ReactionEffectExecutor.ExecuteDerivedHits(primaryReactions[index]);
                    LogManager.Log(LogCategory.Damage,
                        $"hit#{actualHitIndex + 1} {Entity.EntityID} 攻击 {resolution.Request.Target.EntityID} : " +
                        $"{resolution.DamageAfterShield:F1} | Anemo");
                }
                SwirlBatchResult swirl = SwirlReactionHandler.ResolvePreparedBatch(prepared);
                BattleSpecialRules.OnAnemoResult(CurrentDamageSourceEntity, eff.SkillEffectID2,
                    eff.DamageHook, _currentEffectSourceStatus, swirl.Reaction);
                StatusOnHitHookSystem.NotifyReactions(swirl.Reaction.TriggeredReactions);
                ReactionEffectExecutor.ExecuteDerivedHits(swirl.Reaction);
            });
        }
    }

    // ----------------------------------------------------------------
    //  ApplyStatus
    // ----------------------------------------------------------------
    void ExecuteApplyStatus(SkillEffectData eff)
    {
        string statusID2 = CountStatusSystem.ResolvePublicStatusReference(eff.Param1);
        if (string.IsNullOrEmpty(statusID2)) return;

        var dm = DataManager.Instance;
        if (!dm.StatusMainDict.TryGetValue(statusID2, out var statusMain))
        {
            LogManager.LogWarning(LogCategory.ApplyStatus, $"状态不存在: {statusID2}");
            return;
        }

        if (BattleTargetResolver.IsTeamTarget(eff.TargetType))
        {
            foreach (var team in BattleTargetResolver.GetTeams(BattleManager.Instance, ResolveSkillTargetResult(eff)))
                team.AddStatus(statusID2, Entity, eff.Duration, eff.AddInPhase, eff.TriggerPhase,
                    statusMain, _currentEffectSourceStatus?.WeaponContext, eff.Param2, _currentEffectSourceStatus?.EquipmentSource,
                    enchantElement: eff.Element, enchantOverride: eff.Param3);
            return;
        }
        // Field 目标：状态挂到场地位置（含空位），不依赖单位
        if (!string.IsNullOrEmpty(eff.TargetType) && eff.TargetType.EndsWith("Field", StringComparison.Ordinal))
        {
            ApplyStatusToField(eff, statusID2, statusMain);
            return;
        }

        // 其他目标：挂到实体
        var targets = ResolveTargets(eff);
        if (targets == null || targets.Count == 0) return;

        foreach (var target in targets)
        {
            target.AddStatus(statusID2, Entity, eff.Duration, eff.AddInPhase,
                eff.TriggerPhase, statusMain, _currentEffectSourceStatus?.WeaponContext, eff.Param2,
                _currentEffectSourceStatus?.EquipmentSource, enchantElement: eff.Element, enchantOverride: eff.Param3);
            LogManager.Log(LogCategory.ApplyStatus, $"{statusID2} -> {target.EntityID} (dur={eff.Duration}, phase={eff.AddInPhase}, trig={eff.TriggerPhase})");
        }
    }

    // 施加状态到场地位置（EnemyField）：按 TargetOverride/强制位置选位置，状态挂到 FieldPosition.StatusList
    void ApplyStatusToField(SkillEffectData eff, string statusID2, StatusMainData statusMain)
    {
        var bm = BattleManager.Instance;
        if (bm == null || bm.Field == null) return;

        var fields = ResolveTargetFields(eff);
        foreach (var slot in fields)
        {
            slot.AddStatus(statusID2, Entity, eff.Duration, eff.AddInPhase, eff.TriggerPhase,
                statusMain, _currentEffectSourceStatus?.WeaponContext, eff.Param2,
                equipmentSource: _currentEffectSourceStatus?.EquipmentSource, enchantElement: eff.Element, enchantOverride: eff.Param3);
            LogManager.Log(LogCategory.ApplyStatus, $"{statusID2} -> {slot} (dur={eff.Duration}, phase={eff.AddInPhase}, trig={eff.TriggerPhase})");
        }
    }

    // ----------------------------------------------------------------
    //  RemoveStatus
    // ----------------------------------------------------------------
    /// <summary>只从一个已解析出的场地位置移除状态。</summary>
    void RemoveStatusFromFieldSlot(string statusID2, FieldPosition slot)
    {
        slot?.RemoveStatus(statusID2);
    }

    void ExecuteRemoveStatus(SkillEffectData eff)
    {
        string statusID2 = eff.Param1;
        if (string.IsNullOrEmpty(statusID2)) return;
        if (BattleTargetResolver.IsTeamTarget(eff.TargetType))
        {
            foreach (var team in BattleTargetResolver.GetTeams(BattleManager.Instance, ResolveSkillTargetResult(eff)))
                team.RemoveStatus(statusID2);
            return;
        }

        //位置状态移除（EnemyField/AllyField目标）：从目标侧的位置状态列表移除（兔兔伯爵爆炸后移除兔子）
        if (!string.IsNullOrEmpty(eff.TargetType) && eff.TargetType.EndsWith("Field", StringComparison.Ordinal))
        {
            foreach (var field in ResolveTargetFields(eff))
                RemoveStatusFromFieldSlot(statusID2, field);
            return;
        }

        var targets = ResolveTargets(eff);
        if (targets == null) return;
        foreach (var target in targets)
        {
            bool removed = target.RemoveStatus(statusID2, -1); // 目标数量不是移除层数，按效果定义清除同 ID 全部实例。
            if (removed)
                LogManager.Log(LogCategory.RemoveStatus, $"{statusID2} removed from {target.EntityID}");
        }
    }

    // ----------------------------------------------------------------
    //  ExecuteSkill（递归执行目标技能的效果序列）
    // ----------------------------------------------------------------
    void ExecuteExecuteSkill(SkillEffectData eff, SkillSelectionPlan plan = null)
    {
        string targetSkillID2 = eff.Param1;
        if (string.IsNullOrEmpty(targetSkillID2)) return;
        QueueSkillByID(targetSkillID2, GetSkillTypeForExecution(targetSkillID2, 1), false, plan);
    }

    // ----------------------------------------------------------------
    //  ExecuteEffect（直接执行目标效果，箭雨状态触发用）
    // ----------------------------------------------------------------
    void ExecuteExecuteEffect(SkillEffectData eff, EffectSelectionPlan plan = null)
    {
        if (plan != null)
        {
            BattleExecution.Enqueue(() => ExecuteEffectAsync(plan.Effect,
                GetEffectSkillType(plan.Effect.SkillEffectID2, 3), plan));
            return;
        }
        string targetEffID2 = eff.Param1;
        if (string.IsNullOrEmpty(targetEffID2)) return;

        var dm = DataManager.Instance;
        if (!_effectById.TryGetValue(targetEffID2, out var targetEff)
            && !dm.SkillEffectDict.TryGetValue(targetEffID2, out targetEff)) return;
        var called = CreateCalledEffect(targetEff, eff);
        ExecuteEffect(called, GetEffectSkillType(called.SkillEffectID2, 3));
    }

    // 调用行的 ScriptHook / ExecuteCount 属于调用本身，不能再次复制到子效果求值/重复。
    public SkillEffectData CreateCalledEffect(SkillEffectData original, object invocation)
    {
        var result = CloneEffect(original);
        foreach (string name in new[] { "Element", "DamageType", "DamageHook", "Duration", "AddInPhase",
            "TriggerPhase", "TargetType", "TargetNumber", "TargetConsecutive", "TargetOverride" })
        {
            var field = invocation.GetType().GetField(name);
            if (field == null || !HasAuthoredCallField(invocation, name)) continue;
            typeof(SkillEffectData).GetField(name).SetValue(result, field.GetValue(invocation));
            if (name == "TargetConsecutive") result.TargetConsecutiveSet = true;
        }
        if (HasAuthoredCallField(invocation, "Param2"))
            result.Param1 = (string)invocation.GetType().GetField("Param2").GetValue(invocation);
        if (HasAuthoredCallField(invocation, "Param3"))
            result.Param2 = (string)invocation.GetType().GetField("Param3").GetValue(invocation);
        // 显式覆盖的条件已由调用行求值；不能对子效果再次抽概率，也不再沿用被替换的旧条件。
        if (HasAuthoredCallField(invocation, "ScriptHook")) result.ScriptHook = null;
        // 调用行自身已重复 N 次，显式覆盖次数时子效果每次只执行一次。
        if (HasAuthoredCallField(invocation, "ExecuteCount"))
        {
            result.ExecuteCount = 1;
            result.ExecuteCountHandler = null;
        }
        return result;
    }

    private static bool HasAuthoredCallField(object invocation, string name)
    {
        var fields = invocation is SkillEffectData skill ? skill.AuthoredFields : ((StatusEffectData)invocation).AuthoredFields;
        if (fields != null) return fields.Contains(name);
        var field = invocation.GetType().GetField(name);
        object value = field?.GetValue(invocation);
        if (value is string text) return !string.IsNullOrWhiteSpace(text);
        if (name == "TargetConsecutive" && invocation is SkillEffectData effect && effect.TargetConsecutiveSet) return true;
        if (name == "ExecuteCount")
            return (value is int count && count != 1)
                || (invocation is SkillEffectData countedSkill && !string.IsNullOrEmpty(countedSkill.ExecuteCountHandler))
                || (invocation is StatusEffectData status && !string.IsNullOrEmpty(status.ExecuteCountHandler));
        return value is int number && number != 0;
    }

    // ----------------------------------------------------------------
    //  ChangeControl（修改按键绑定）
    //  术语：param1=按钮类型(Normal/Heavy/Skill/Burst/All)，param2=新技能ID2或Freeze，param3=0/1
    //  状态效果表挂载 = 跟状态 duration 走（状态施加时执行，状态消失时解除）；
    //  技能效果表挂载 = 填了 Duration 一切照常（Duration 回合后结束）；没填 = 视为永久（不记录旧技能信息）
    //  覆盖判定（术语）：解除时识别当前按钮绑定是否依旧为效果所设定的 skill 字段（Param2），
    //  若不同（已被其他 Change 覆盖）则不返回。
    // ----------------------------------------------------------------
    void ExecuteChangeControl(SkillEffectData eff)
    {
        var btnTypes = ParseButtonTypes(eff.Param1);
        if (btnTypes == null)
        {
            LogManager.LogError(LogCategory.ChangeControl, $"{eff.SkillEffectID2} Param1 非法: {eff.Param1}（需 Normal/Heavy/Skill/Burst/All）");
            return;
        }

        // 冻结型：Param2=Freeze，指定按钮无法使用（All=全部按钮）
        if (eff.Param2 == "Freeze")
        {
            _ccRecords.Add(new ControlChangeRecord
            {
                IsFreeze = true,
                FrozenSkillTypes = btnTypes,
                IsSkillEffect = true,
                IsPermanent = eff.Duration <= 0,
                RemainingTurns = eff.Duration,
            });
            LogManager.Log(LogCategory.ChangeControl, $"{eff.SkillEffectID2} 冻结按钮 [{string.Join(",", btnTypes)}] (dur={eff.Duration}, 永久={eff.Duration <= 0})");
            OnControlChanged?.Invoke();
            return;
        }

        // 替换型：Param2 = 新技能ID2（只能替换单个按钮，All 只能配 Freeze）
        string newSkillID2 = eff.Param2;
        if (string.IsNullOrEmpty(newSkillID2))
        {
            LogManager.LogError(LogCategory.ChangeControl, $"{eff.SkillEffectID2} 未填 Param2（新技能ID）");
            return;
        }
        if (!_skillById2.ContainsKey(newSkillID2))
        {
            LogManager.LogError(LogCategory.ChangeControl, $"替换技能不存在: {newSkillID2}");
            return;
        }
        if (btnTypes.Count != 1)
        {
            LogManager.LogError(LogCategory.ChangeControl, $"{eff.SkillEffectID2} 替换型 Param1 只能指定单个按钮（{eff.Param1}），All 只能配 Freeze");
            return;
        }
        int skillType = btnTypes[0];
        string originalID = _boundSkills[skillType];
        int origCD = _cooldownRemaining.TryGetValue(originalID, out int cd) ? cd : 0;
        int origCharge = _currentCharges.TryGetValue(originalID, out int ch) ? ch : 0;

        ApplyParam3OnSwap(eff.Param3, newSkillID2);

        _boundSkills[skillType] = newSkillID2;

        // 没填 Duration：视为永久，不记录旧技能信息
        if (eff.Duration > 0)
        {
            _ccRecords.Add(new ControlChangeRecord
            {
                SkillType = skillType,
                OriginalSkillID = originalID,
                OrigCooldownSnapshot = origCD,
                OrigChargeSnapshot = origCharge,
                NewSkillID = newSkillID2,
                IsSkillEffect = true,
                RemainingTurns = eff.Duration,
            });
        }
        LogManager.Log(LogCategory.ChangeControl, $"{eff.SkillEffectID2} 替换: {originalID} -> {newSkillID2} (Param3={eff.Param3}, dur={eff.Duration})");
        OnControlChanged?.Invoke();
    }

    // ----------------------------------------------------------------
    //  状态驱动入口（BattleEntity 在状态施加/消失时调用）
    // ----------------------------------------------------------------
    /// <summary>状态施加成功后调用：扫描该状态名下所有 ChangeControl 效果并执行。</summary>
    public void OnStatusChangeControlApplied(StatusInstance inst)
    {
        if (inst == null || inst.MainData == null) return;
        var dm = DataManager.Instance;
        if (dm == null) return;
        int parentStatusID = inst.MainData.StatusID;
        foreach (var kv in dm.StatusEffectDict)
        {
            var eff = kv.Value;
            if (eff.StatusEffectID / 100 != parentStatusID) continue;
            if (eff.EffectType != "ChangeControl") continue;
            ApplyChangeControlEffect(eff, inst);
        }
    }

    /// <summary>状态消失（移除/到期）时调用：解除该状态名下所有 ChangeControl 记录。</summary>
    public void OnStatusChangeControlRemoved(StatusInstance inst)
    {
        if (inst == null) return;
        bool changed = false;
        for (int i = _ccRecords.Count - 1; i >= 0; i--)
        {
            var rec = _ccRecords[i];
            if (rec.IsSkillEffect) continue; // 技能效果版由 OnTurnStart 计时
            if (!ReferenceEquals(rec.SourceStatus, inst)) continue;
            if (RevertChangeControlRecord(rec))
                _ccRecords.RemoveAt(i);
            changed = true;
        }
        if (changed) OnControlChanged?.Invoke();
    }

    /// <summary>执行一条状态效果表的 ChangeControl（状态施加时）。</summary>
    void ApplyChangeControlEffect(StatusEffectData eff, StatusInstance inst)
    {
        var btnTypes = ParseButtonTypes(eff.Param1);
        if (btnTypes == null)
        {
            LogManager.LogError(LogCategory.ChangeControl, $"{eff.StatusEffectID2} Param1 非法: {eff.Param1}（需 Normal/Heavy/Skill/Burst/All）");
            return;
        }

        // 冻结型
        if (eff.Param2 == "Freeze")
        {
            _ccRecords.Add(new ControlChangeRecord
            {
                IsFreeze = true,
                FrozenSkillTypes = btnTypes,
                SourceStatusID2 = inst.StatusID2,
                SourceStatus = inst,
            });
            LogManager.Log(LogCategory.ChangeControl, $"{inst.StatusID2} 冻结按钮 [{string.Join(",", btnTypes)}]");
            OnControlChanged?.Invoke();
            return;
        }

        // 替换型
        string newSkillID2 = eff.Param2;
        if (string.IsNullOrEmpty(newSkillID2))
        {
            LogManager.LogError(LogCategory.ChangeControl, $"{eff.StatusEffectID2} 未填 Param2（新技能ID）");
            return;
        }
        if (!_skillById2.ContainsKey(newSkillID2))
        {
            LogManager.LogError(LogCategory.ChangeControl, $"替换技能不存在: {newSkillID2}");
            return;
        }
        if (btnTypes.Count != 1)
        {
            LogManager.LogError(LogCategory.ChangeControl, $"{eff.StatusEffectID2} 替换型 Param1 只能指定单个按钮（{eff.Param1}），All 只能配 Freeze");
            return;
        }
        int skillType = btnTypes[0];
        string originalID = _boundSkills[skillType];
        int origCD = _cooldownRemaining.TryGetValue(originalID, out int cd) ? cd : 0;
        int origCharge = _currentCharges.TryGetValue(originalID, out int ch) ? ch : 0;

        ApplyParam3OnSwap(eff.Param3, newSkillID2);

        _boundSkills[skillType] = newSkillID2;
        _ccRecords.Add(new ControlChangeRecord
        {
            SkillType = skillType,
            OriginalSkillID = originalID,
            OrigCooldownSnapshot = origCD,
            OrigChargeSnapshot = origCharge,
            NewSkillID = newSkillID2,
            SourceStatusID2 = inst.StatusID2,
            SourceStatus = inst,
        });
        LogManager.Log(LogCategory.ChangeControl, $"{inst.StatusID2} 替换: {originalID} -> {newSkillID2} (Param3={eff.Param3})");
        OnControlChanged?.Invoke();
    }

    /// <summary>Param3：1=换上时重置冷却、次数读 InitialCharge；0/空=不重置（保持冻结值）。</summary>
    void ApplyParam3OnSwap(string param3, string newSkillID2)
    {
        if (param3 != "1") return;
        if (_cooldownRemaining.ContainsKey(newSkillID2))
            _cooldownRemaining[newSkillID2] = 0;
        if (_skillById2.TryGetValue(newSkillID2, out var nsk) && nsk.MaxCharge > 0)
        {
            int init = nsk.InitialCharge;
            if (init <= 0) init = nsk.MaxCharge;
            _currentCharges[newSkillID2] = init;
        }
    }

    /// <summary>
    /// 解除一条 ChangeControl 记录。返回 true = 记录应被移除。
    /// 术语：该状态消失时需识别当前技能按键绑定是否依旧为效果所设定的 skill 字段（Param2），
    /// 若不同（已被其他 Change 覆盖）则不将其返回；相同则返回原技能，冷却/次数写回快照。
    /// </summary>
    bool RevertChangeControlRecord(ControlChangeRecord rec)
    {
        if (rec.IsFreeze)
        {
            LogManager.Log(LogCategory.ChangeControl, $"{rec.SourceStatusID2} 解冻按钮 [{string.Join(",", rec.FrozenSkillTypes)}]");
            return true;
        }

        bool stillBound = _boundSkills[rec.SkillType] == rec.NewSkillID;
        if (!stillBound)
        {
            LogManager.Log(LogCategory.ChangeControl, $"{rec.SourceStatusID2} 结束：按钮已被其他 Change 覆盖({_boundSkills[rec.SkillType]})，不返回 {rec.OriginalSkillID}");
            return true;
        }

        _boundSkills[rec.SkillType] = rec.OriginalSkillID;
        if (_cooldownRemaining.ContainsKey(rec.OriginalSkillID))
            _cooldownRemaining[rec.OriginalSkillID] = rec.OrigCooldownSnapshot;
        if (_currentCharges.ContainsKey(rec.OriginalSkillID))
            _currentCharges[rec.OriginalSkillID] = rec.OrigChargeSnapshot;
        LogManager.Log(LogCategory.ChangeControl, $"{rec.SourceStatusID2} 结束：按钮返回 {rec.OriginalSkillID}，冷却/次数恢复快照({rec.OrigCooldownSnapshot}/{rec.OrigChargeSnapshot})");
        return true;
    }

    /// <summary>解析 Param1 按钮类型，非法返回 null。All=全部按钮。</summary>
    List<int> ParseButtonTypes(string param1)
    {
        switch (param1)
        {
            case "Normal": return new List<int> { 0 };
            case "Heavy": return new List<int> { 1 };
            case "Skill": return new List<int> { 2 };
            case "Burst": return new List<int> { 3 };
            case "All": return new List<int> { 0, 1, 2, 3 };
            default: return null;
        }
    }

    // ================================================================
    //  等级数据查询
    // ================================================================
    // 查 SkillLevel 表里 ParamID==效果ID 的行，返回其 SkillType（找不到回退 fallback）
    private int GetEffectSkillType(string effectID2, int fallback)
    {
        var dm = DataManager.Instance;
        if (dm == null) return fallback;
        foreach (var kv in dm.SkillLevelDict)
        {
            foreach (var lv in kv.Value.Values)
            {
                if (lv.ParamID == effectID2)
                    return lv.SkillType;
            }
        }
        return fallback;
    }

    /// <summary>
    /// 技能等级表 Hits1-Hits7 逐段解析，每段独立结算暴击和元素量。
    /// %引用效果为单段，统一采用 M,E，不读取削韧参数。
    /// </summary>
    List<HitData> GetHitDataList(SkillEffectData eff, int skillType)
    {
        var list = new List<HitData>();
        if (eff == null) return list;
        string[] direct = GetDirectHitExpressions(eff.Param1);
        if (direct != null)
        {
            foreach (string expression in direct) list.Add(HitDataParser.Parse(expression));
            return list;
        }

        // %引用效果（如 %SE_HeavyAttack_Amber1.Hits1.M*0.2,1）：单段。
        // 倍率由 ResolveBaseValue 计算，这里只提供引用后的元素量。
        if (!string.IsNullOrEmpty(eff.Param1) && eff.Param1.StartsWith("%"))
        {
            HitData reference = HitDataParser.ResolveReferencedHit(eff.Param1,
                GetReferenceSourceHit(eff.Param1, skillType));
            list.Add(new HitData(1f, reference.ElementAura));
            return list;
        }

        if (!_levelDataBySkillType.TryGetValue(skillType, out var levelList) || levelList.Count == 0)
            return list;

        // 同一 SkillType 可包含多套 ParamID；先锁定效果分组，再在组内按等级取值。
        List<SkillLevelData> effectLevels = levelList.FindAll(s => s.ParamID == eff.SkillEffectID2);
        if (effectLevels.Count == 0) return list;

        int curLevel = GetEffectiveSkillLevel(skillType);
        SkillLevelData lv1 = effectLevels.Find(s => s.SkillLevel == 1);
        SkillLevelData cur = effectLevels.Find(s => s.SkillLevel == curLevel);
        if (cur == null) cur = lv1;
        if (lv1 == null) lv1 = cur;
        if (cur == null) return list;

        // Hits1-7 逐段解析（空段跳过）
        for (int n = 1; n <= 7; n++)
        {
            string curStr = GetHitsField(cur, n);
            string lv1Str = GetHitsField(lv1, n);
            if (string.IsNullOrEmpty(curStr) && string.IsNullOrEmpty(lv1Str)) continue;
            list.Add(MergeHitData(curStr, lv1Str));
        }
        return list;
    }

    private static string[] GetDirectHitExpressions(string param)
    {
        if (string.IsNullOrWhiteSpace(param) || param.TrimStart().StartsWith("%")
            || param.TrimStart().StartsWith("M*")) return null;
        string[] hits = param.Split(';');
        foreach (string hit in hits)
        {
            string coefficient = hit.Split(',')[0].Split('*')[0].Trim();
            if (!float.TryParse(coefficient, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float value)
                || float.IsNaN(value) || float.IsInfinity(value))
                throw new FormatException("Damage Param1 需要 M,E 或以分号分隔的多段 Hit: " + param);
        }
        return hits;
    }

    private float ResolveDirectHitBase(string hit, SkillEffectData effect, int skillType)
        => ResolveDirectHitBase(hit, effect.SkillEffectID2, skillType, effect.Element);

    private float ResolveDirectHitBase(string hit, string effectID, int skillType, string element)
    {
        string expression = hit.Split(',')[0].Trim();
        int star = expression.IndexOf('*');
        string basis = star >= 0 ? expression.Substring(star + 1).Trim() : "ATK";
        switch (basis)
        {
            case "ATK": case "TotalATK": return GetFinalATK(effectID, skillType, element);
            case "DEF": case "TotalDEF": return SourceAttribute("TotalDEF", effectID, skillType, element);
            case "MaxHP": case "TotalHP": return SourceAttribute("TotalHP", effectID, skillType, element);
            default: throw new FormatException("未知 Damage 基准属性: " + basis);
        }
    }


    static string GetHitsField(SkillLevelData d, int idx)
    {
        if (d == null) return "";
        switch (idx)
        {
            case 1: return d.Hits1;
            case 2: return d.Hits2;
            case 3: return d.Hits3;
            case 4: return d.Hits4;
            case 5: return d.Hits5;
            case 6: return d.Hits6;
            case 7: return d.Hits7;
            default: return "";
        }
    }

    HitData GetHitData(SkillEffectData eff, int skillType)
    {
        var list = GetHitDataList(eff, skillType);
        return list.Count > 0 ? list[0] : new HitData(0, 0);
    }

    HitData MergeHitData(string curStr, string lv1Str)
    {
        return HitDataParser.MergeWithLevel1(curStr, lv1Str);
    }

    /// <summary>
    /// 最终攻击力统一由实体聚合：攻击百分比结算后，再加固定ATKBonusFlat。
    /// </summary>
    public float GetFinalATK(string effectID2, int skillType, string element)
    {
        return SourceAttribute("TotalATK", effectID2, skillType, element);
    }

    float ResolveBaseValue(string param1, string effectID2, int skillType, string element)
    {
        // Param1 格式： "M*ATK" 或 "%SE_HeavyAttack_Amber1.Hits1.M*0.2"
        if (string.IsNullOrEmpty(param1)) return GetFinalATK(effectID2, skillType, element);

        // % 引用：查来源效果的倍率×系数（C1 第二支箭 / STE_Bunny3 3倍爆炸）
        if (param1.StartsWith("%"))
        {
            return ResolveReferenceBaseValue(param1, effectID2, skillType, element);
        }

        // 纯乘法形式 "M*属性"：系数 M 生效（如 0.3*TotalHP → 0.3×TotalHP；修复前系数被丢弃，2026-08-19）
        // 含 + 等复杂形式保持旧行为（返回属性值本身，不解析后续表达式）
        if (param1.Contains("*ATK")) return GetFinalATK(effectID2, skillType, element) * ExtractParamMultiplier(param1);
        if (param1.Contains("*DEF")) return SourceAttribute("TotalDEF", effectID2, skillType, element) * ExtractParamMultiplier(param1);
        if (param1.Contains("*MaxHP") || param1.Contains("*TotalHP")) return SourceAttribute("TotalHP", effectID2, skillType, element) * ExtractParamMultiplier(param1);
        if (param1.Contains("*TotalATK")) return GetFinalATK(effectID2, skillType, element) * ExtractParamMultiplier(param1);
        if (param1.Contains("*TotalDEF")) return SourceAttribute("TotalDEF", effectID2, skillType, element) * ExtractParamMultiplier(param1);

        return GetFinalATK(effectID2, skillType, element);
    }

    /// <summary>提取 Param1 开头的数字系数（"0.3*TotalHP" → 0.3）；非纯乘法形式或解析失败返回 1。</summary>
    static float ExtractParamMultiplier(string param1)
    {
        if (string.IsNullOrEmpty(param1)) return 1f;
        int star = param1.IndexOf('*');
        if (star <= 0) return 1f;
        string head = param1.Substring(0, star).Trim();
        if (float.TryParse(
                head,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out float multiplier))
            return multiplier;
        return 1f;
    }

    /// <summary>
    /// 解析 % 引用：格式 %效果唯一ID.HitsN.M,E 或 %效果唯一ID.HitsN.M*系数,E
    /// 返回"基准属性值 × 引用倍率"（M token 的倍率会乘以 scale）。
    /// E 可沿用来源元素量，也可写 E*系数或直接填数值；不再支持 P。
    /// </summary>
    float ResolveReferenceBaseValue(string expression, string effectID2, int skillType, string element)
    {
        HitData resolved = HitDataParser.ResolveReferencedHit(expression,
            GetReferenceSourceHit(expression, skillType));
        // 引用只提供倍率/元素量；属性限定仍属于正在出伤的效果，不能借来源效果的DamageType。
        return GetFinalATK(effectID2, skillType, element) * resolved.Multiplier;
    }

    private HitData GetReferenceSourceHit(string expression, int fallbackSkillType)
    {
        var reference = HitDataParser.ParseReference(expression);
        int sourceSkillType = GetEffectSkillType(reference.effectId, fallbackSkillType);
        if (reference.effectId == null || reference.hitIndex >= 7 ||
            !_levelDataBySkillType.TryGetValue(sourceSkillType, out var levels))
            throw new FormatException("Missing SkillLevel reference: " + expression);

        var effectLevels = levels.FindAll(row => row.ParamID == reference.effectId);
        int level = SourceSkillLevel(reference.effectId, sourceSkillType);
        SkillLevelData first = effectLevels.Find(row => row.SkillLevel == 1);
        SkillLevelData current = effectLevels.Find(row => row.SkillLevel == level) ?? first;
        if (first == null) first = current;
        string currentHit = GetHitsField(current, reference.hitIndex + 1);
        string firstHit = GetHitsField(first, reference.hitIndex + 1);
        if (string.IsNullOrWhiteSpace(currentHit) && string.IsNullOrWhiteSpace(firstHit))
            throw new FormatException("Missing SkillLevel hit: " + expression);
        return MergeHitData(currentHit, firstHit);
    }

    // ================================================================
    //  暴击前直伤公式（反应系统会在此结果上继续结算）
    //  最终暴击统一由 ResolveFinalCriticalDamage 在每 hit、每目标的护盾结算前执行。
    // ================================================================
    float CalculateDamage(
        float baseValue,
        HitData hit,
        SkillEffectData eff,
        BattleEntity target,
        int skillType,
        out ReactionDamageComponents components,
        float countBaseDamageBonus = 0f)
    {
        // 基础伤害
        float baseDMG = baseValue * hit.Multiplier;

        DamageProperties properties = DamageProperties.Parse(eff.Element, eff.Param3);
        if (properties.UsesSpecialFormula)
            return SpecialSkillDamageCalculator.Calculate(CurrentDamageSourceEntity, target, baseDMG,
                properties, eff.Element, eff.SkillEffectID2, GetCurrentDamageSourceSkillID(skillType),
                eff.DamageType, out components, countBaseDamageBonus);

        baseDMG += countBaseDamageBonus + Entity.BaseDMGBonusFlat + Entity.GetStatusBaseDMGBonusFlat(
            eff.SkillEffectID2, GetExecutingSkillID2(skillType), skillType, eff.Element, target);

        // 增伤区（配表术语 1.8）
        float dmgBonus = 1f + Entity.DMGBonus + GetElementBonus(eff.Element)
            + Entity.GetStatusDMGBonus(eff.SkillEffectID2, GetExecutingSkillID2(skillType), skillType, eff.Element, target);

        // DEF 承伤（配表术语 1.12，对怪物用等级公式）
        float defRes;
        if (target.Type == BattleEntity.EntityType.Enemy)
        {
            // 怪物 DEFRes = (AttackerLevel+100) / [(AttackerLevel+100)+(DefenderLevel+100)*(1-减防)*(1-无视)]
            float attacker = Entity.Level + 100f;
            float defender = (target.Level + 100f) * (1f - Entity.DEFReduction) * (1f - Entity.DEFIgnored);
            defRes = attacker / (attacker + defender);
        }
        else
        {
            // 对角色：DEFRes = 1 - TotalDEF/(TotalDEF + 5×AttackerLevel + 500)
            float effectiveDef = target.GetFinalDEF() * (1f - Entity.DEFReduction) * (1f - Entity.DEFIgnored);
            if (effectiveDef < 0) effectiveDef = 0;
            defRes = 1f - effectiveDef / (effectiveDef + 5f * Entity.Level + 500f);
            defRes = Mathf.Clamp(defRes, 0.1f, 1f);
        }

        // RES 抗性承伤（配表术语 1.7）
        float res = GetElementResistance(target, eff.Element);
        float resMultiplier;
        if (res < 0)
            resMultiplier = 1f - res / 2f;
        else if (res < 0.75f)
            resMultiplier = 1f - res;
        else
            resMultiplier = 1f / (1f + 4f * res);

        components = new ReactionDamageComponents
        {
            SkillBaseDamage = baseDMG,
            DamageBonusMultiplier = dmgBonus,
            DefenseMultiplier = defRes,
            ResistanceMultiplier = resMultiplier
        };
        return baseDMG * dmgBonus * defRes * resMultiplier;
    }

    /// <summary>
    /// 每个 hit、每个实际受击单位的最终暴击入口。状态暴击率只求值一次，
    /// 结算与日志复用同一个结果，概率型 ScriptHook 不会因日志再次抽取。
    /// </summary>
    CriticalHitResult ResolveFinalCriticalDamage(
        float damageBeforeCritical,
        BattleEntity target,
        string effectID2,
        string skillID2,
        int skillType,
        string element,
        int hitNumber,
        string damageChannel,
        bool forceCritical = false, ReactionType? damageReaction = null)
    {
        var memory = StatusFormulaSnapshot.For(Entity, effectID2);
        float statusCritRate = memory != null
            ? memory.CryoCrit(target) + memory.Bonus("CritRate", effectID2, element, target, damageReaction)
            : Entity.GetStatusCritRate(
            effectID2,
            skillID2,
            skillType,
            element,
            target, damageReaction);
        float critRate = Mathf.Clamp(
            SourceValue("CritRate", Entity.CritRate) + SourceValue("WeaponCritRate", Entity.WeaponCritRate) + statusCritRate,
            0f,
            1f);
        // 信使只覆盖本次追加hit的判定率；仍使用统一入口与一次随机抽取，不修改角色面板。
        if (forceCritical) critRate = 1f;
        float statusCritDMG = memory != null ? memory.Bonus("CritDMG", effectID2, element, target, damageReaction)
            : Entity.GetStatusCritDMG(effectID2, skillID2, skillType, element, target, damageReaction);
        float critDMG = SourceValue("CritDMG", Entity.CritDMG) + SourceValue("WeaponCritDMG", Entity.WeaponCritDMG) + statusCritDMG;
        CriticalHitResult result = CriticalHitResolver.Resolve(
            damageBeforeCritical,
            critRate,
            critDMG);

        LogManager.Log(
            LogCategory.Crit,
            $"{damageChannel} 最终暴击 | hit#{hitNumber} | 目标={target?.EntityID} | " +
            $"暴击前={result.DamageBeforeCritical:F1} | 率={result.CriticalRate:P1}" +
            $"(角色{SourceValue("CritRate", Entity.CritRate):P1}+武器{SourceValue("WeaponCritRate", Entity.WeaponCritRate):P1}+状态{statusCritRate:P1}) | " +
            $"暴伤={critDMG:P1}(角色{SourceValue("CritDMG", Entity.CritDMG):P1}+武器{SourceValue("WeaponCritDMG", Entity.WeaponCritDMG):P1}+状态{statusCritDMG:P1}) | " +
            $"随机={result.Roll:F4} → {(result.IsCritical ? $"暴击 x{result.Multiplier:F2}" : "未暴击")} | " +
            $"暴击后={result.DamageAfterCritical:F1}" + (memory != null ? $" | 快照={memory.Identity} | 位置={target?.Side}:{target?.SlotPosition}" : ""));
        return result;
    }

    float GetElementBonus(string element)
    {
        var memory = StatusFormulaSnapshot.For(Entity);
        if (memory != null) return memory.ElementBonus(element);
        switch (element)
        {
            case "Pyro": return Entity.PyroDmgBonus + Entity.WeaponPyroDmgBonus;
            case "Hydro": return Entity.HydroDmgBonus + Entity.WeaponHydroDmgBonus;
            case "Electro": return Entity.ElectroDmgBonus + Entity.WeaponElectroDmgBonus;
            case "Cryo": return Entity.CryoDmgBonus + Entity.WeaponCryoDmgBonus;
            case "Anemo": return Entity.AnemoDmgBonus + Entity.WeaponAnemoDmgBonus;
            case "Dendro": return Entity.DendroDmgBonus + Entity.WeaponDendroDmgBonus;
            case "Geo": return Entity.GeoDmgBonus + Entity.WeaponGeoDmgBonus;
            case "Physical": return Entity.PhysicalDmgBonus + Entity.WeaponPhysicalDmgBonus;
            default: return 0f;
        }
    }

    float GetElementResistance(BattleEntity target, string element)
    {
        switch (element)
        {
            case "Pyro": return target.PyroRes + target.ResBonus + target.GetStatusResBonus(element);
            case "Hydro": return target.HydroRes + target.ResBonus + target.GetStatusResBonus(element);
            case "Electro": return target.ElectroRes + target.ResBonus + target.GetStatusResBonus(element);
            case "Cryo": return target.CryoRes + target.ResBonus + target.GetStatusResBonus(element);
            case "Anemo": return target.AnemoRes + target.ResBonus + target.GetStatusResBonus(element);
            case "Dendro": return target.DendroRes + target.ResBonus + target.GetStatusResBonus(element);
            case "Geo": return target.GeoRes + target.ResBonus + target.GetStatusResBonus(element);
            case "Physical": return target.PhysicalRes + target.ResBonus + target.GetStatusResBonus(element);
            default: return 0f;
        }
    }
}
