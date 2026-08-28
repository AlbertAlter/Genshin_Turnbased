using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public partial class CharacterBattleController
{
    // ================================================================
    //  效果执行器
    // ================================================================
    void ExecuteEffect(SkillEffectData eff, int skillType)
    {
        // 效果级 ScriptHook（2026-08-12）：Hit()/Check() 返回假则该效果不执行（T2 命中后加攻等）
        if (eff != null && !ScriptHookEvaluator.Evaluate(eff.ScriptHook, Entity, LastHitEffectID2))
            return;
        switch (eff.EffectType)
        {
            case "Damage":
                ExecuteDamage(eff, skillType);
                break;
            case "ApplyStatus":
                ExecuteApplyStatus(eff);
                break;
            case "RemoveStatus":
                ExecuteRemoveStatus(eff);
                break;
            case "ExecuteSkill":
                ExecuteExecuteSkill(eff);
                break;
            case "ExecuteEffect":
                ExecuteExecuteEffect(eff);
                break;
            case "GainEnergy":
                ExecuteGainEnergy(eff);
                break;
            case "ChangeControl":
                ExecuteChangeControl(eff);
                break;
            case "Heal":
                ExecuteHeal(eff, skillType);
                break;
            default:
                LogManager.LogWarning(LogCategory.Effect, $"未知类型: {eff.EffectType}");
                break;
        }
    }

    /// <summary>治疗效果（2026-08-14）：Param1=治疗量公式（如 0.15*TotalATK），目标=TargetType解析。</summary>
    void ExecuteHeal(SkillEffectData eff, int skillType)
    {
        var targets = ResolveTargets(eff);
        if (targets == null) return;
        float heal = ResolveBaseValue(eff.Param1, eff.SkillEffectID2, skillType, eff.Element);
        foreach (var t in targets)
        {
            if (t == null || !t.IsAlive) continue;
            t.Heal(heal, Entity);
            LogManager.Log(LogCategory.Effect, $"治疗 {t.EntityID} +{heal:F1} (HP {t.CurrentHP:F1})");
        }
    }

    // ----------------------------------------------------------------
    //  Damage
    // ----------------------------------------------------------------
    void ExecuteDamage(SkillEffectData eff, int skillType)
    {
        long effectExecutionID = ReactionResolver.BeginEffectExecution();
        // 多段 HitData（Hits1-7，每段独立结算；空段跳过）
        var hits = GetHitDataList(eff, skillType);
        if (hits.Count == 0 || (hits.Count == 1 && hits[0].Multiplier <= 0))
        {
            LogManager.LogWarning(LogCategory.Damage, $"{eff.SkillEffectID2} 倍率为0或未匹配等级行 (skillType={skillType})，无伤害");
            return;
        }

        // 基准值（Param1 决定用哪个属性；% 引用由 ResolveBaseValue 处理）
        float baseValue = ResolveBaseValue(eff.Param1, eff.SkillEffectID2, skillType, eff.Element);

        // 选目标（支持 TargetType/TargetNumber/TargetConsecutive/TargetOverride）
        var targets = ResolveTargets(eff);
        if (targets == null || targets.Count == 0)
        {
            LogManager.Log(LogCategory.Damage, $"{Entity.EntityID} 无目标，攻击落空 ({eff.SkillEffectID2})");
            return;
        }
        if (eff.Element == "Geo" && targets.Count > 1)
        {
            // 同一岩伤害效果按敌方位置稳定结算，最后成功结晶的目标决定最终全队盾。
            targets = targets
                .OrderBy(target => target != null ? target.SlotPosition : int.MaxValue)
                .ToList();
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

        if (eff.Element == "Anemo")
        {
            ExecuteAnemoDamage(eff, skillType, hits, baseValue, targets, effectExecutionID);
            return;
        }

        foreach (var target in targets)
        {
            // 多段：每个 hit 完整结算（伤害/暴击/护盾/附着/削韧/OnHit）后才进入下一 hit；目标死亡后后续 hit 不再结算
            foreach (var hit in hits)
            {
                if (!target.IsAlive) break;

                float damage = CalculateDamage(
                    baseValue,
                    hit,
                    eff,
                    target,
                    skillType,
                    out ReactionDamageComponents damageComponents);

                // 元素反应（2026-08-14）：增幅反应（融化等）在伤害计算后、护盾吸收前结算；
                // 2026-08-15：按元素量模型消耗双方元素，攻击元素残留量决定是否上附着（冰攻融化火后不残留冰）
                ReactionResult reaction = ReactionResolver.Resolve(new ReactionContext
                {
                    SourceEntity = Entity,
                    Target = target,
                    SourceKind = ReactionSourceKind.CharacterSkill,
                    SourceSkillID = GetExecutingSkillID2(skillType),
                    SourceEffectID = eff.SkillEffectID2,
                    EffectExecutionID = effectExecutionID,
                    AttackElement = eff.Element,
                    AttackAmount = hit.ElementAura,
                    PreReactionDamage = damage,
                    DamageComponents = damageComponents,
                    DamageType = eff.DamageType,
                    PoiseDamage = hit.Poise
                });
                StatusOnHitHookSystem.NotifyReactions(reaction.TriggeredReactions);
                if (reaction.HasReaction)
                    LogManager.Log(LogCategory.Damage, $"{Entity.EntityID}攻击 {target.EntityID} 触发{reaction.TriggeredReactions[0].DisplayName}");

                // 护盾吸收
                float finalDamage = target.AbsorbDamageWithShield(reaction.FinalDamage, eff.Element);
                // 带来源扣血（2026-08-19）：角色直伤统一入口；反应主命中（蒸发/融化/激化等）标注对应反应类型
                ReactionType mainReactionType = reaction != null && reaction.HasReaction
                    ? reaction.TriggeredReactions[0].Type
                    : ReactionType.None;
                target.TakeDamage(finalDamage, DamageSourceInfo.Create(
                    Entity, ReactionSourceKind.CharacterSkill, GetExecutingSkillID2(skillType), eff.SkillEffectID2, mainReactionType));
                ReactionEffectExecutor.FinalizePrimaryHit(reaction, finalDamage);

                // 削韧
                PoiseSystem.ApplyPoiseDamage(target, hit.Poise, finalDamage, Entity);
                ReactionEffectExecutor.ExecuteDerivedHits(reaction);

                // 溅射（Splash(n; L,R)）：以该 hit 主目标为原点，左右各扩展 L/R 位，
                // 溅射目标受到主目标实际伤害 × n（继承暴击/加成结果，不独立判定）；
                // 每 1 hit 的主目标结算完（含溅射）才进入下一 hit
                if (finalDamage > 0f && TryParseSplash(eff.Param2, out float splashRate, out int splashL, out int splashR))
                {
                    int origin = target.Position != null ? target.Position.SlotIndex : -1;
                    if (origin >= 0)
                    {
                        var bm = BattleManager.Instance;
                        int leftPos = Mathf.Max(1, origin - splashL);
                        int rightPos = Mathf.Min(5, origin + splashR);
                        for (int pos = leftPos; pos <= rightPos; pos++)
                        {
                            if (pos == origin) continue;
                            var slot = bm != null && bm.Field != null ? bm.Field.GetSlot(BattleSide.Enemy, pos) : null;
                            if (slot == null || !slot.IsOccupied || slot.Occupant == null || !slot.Occupant.IsAlive) continue;
                            var splashTarget = slot.Occupant;
                            // 溅射伤害 = 主目标该hit实际伤害 × 倍率（乘倍率）
                            float splashDmg = finalDamage * splashRate;
                            // 溅射目标【独立暴击判定】：每个敌人每次伤害单独 roll，不能三个目标共用一次暴击
                            float splashCritRate = Mathf.Clamp(splashTarget.CritRate + splashTarget.GetStatusCritRate("", "", 0, "", splashTarget), 0f, 1f);
                            float splashCritMult = 1f;
                            if (UnityEngine.Random.value < splashCritRate)
                                splashCritMult = 1f + splashTarget.CritDMG;
                            float splashFinal = splashTarget.AbsorbDamageWithShield(splashDmg * splashCritMult, eff.Element);
                            splashTarget.TakeDamage(splashFinal, DamageSourceInfo.Create(
                                Entity, ReactionSourceKind.CharacterSkill, GetExecutingSkillID2(skillType), eff.SkillEffectID2, mainReactionType));
                            LogManager.Log(LogCategory.Splash, $"{target.EntityID} -> {splashTarget.EntityID} : {splashFinal:F1} (基础{splashDmg:F1}={finalDamage:F1}×{splashRate}, {(splashCritMult > 1f ? $"暴击x{splashCritMult:F2}" : "未暴击")})");
                        }
                    }
                }

                LogManager.Log(LogCategory.Damage, $"{Entity.EntityID} 攻击 {target.EntityID} : {finalDamage:F1} ({damage:F1} raw, {hit.Multiplier}×{baseValue:F1}) | {eff.Element} | 削韧 {hit.Poise}");
            }
        }
        }
    }

    private void ExecuteAnemoDamage(SkillEffectData eff, int skillType, List<HitData> hits,
        float baseValue, List<BattleEntity> targets, long effectExecutionID)
    {
        foreach (HitData hit in hits)
        {
            var contexts = new List<ReactionContext>();
            foreach (BattleEntity target in targets)
            {
                if (target == null || !target.IsAlive) continue;
                float damage = CalculateDamage(baseValue, hit, eff, target, skillType,
                    out ReactionDamageComponents components);
                contexts.Add(new ReactionContext
                {
                    SourceEntity = Entity, Target = target,
                    SourceKind = ReactionSourceKind.CharacterSkill,
                    SourceSkillID = GetExecutingSkillID2(skillType), SourceEffectID = eff.SkillEffectID2,
                    EffectExecutionID = effectExecutionID, AttackElement = "Anemo",
                    AttackAmount = hit.ElementAura, PreReactionDamage = damage,
                    DamageComponents = components, DamageType = eff.DamageType, PoiseDamage = hit.Poise
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
            for (int index = 0; index < contexts.Count; index++)
            {
                ReactionContext context = contexts[index];
                ReactionResult primaryReaction = primaryReactions[index];
                BattleEntity target = context.Target;
                float finalDamage = target.AbsorbDamageWithShield(context.PreReactionDamage, "Anemo");
                target.TakeDamage(finalDamage, DamageSourceInfo.Create(Entity,
                    ReactionSourceKind.CharacterSkill, GetExecutingSkillID2(skillType), eff.SkillEffectID2,
                    ReactionType.None));
                ReactionEffectExecutor.FinalizePrimaryHit(primaryReaction, finalDamage);
                PoiseSystem.ApplyPoiseDamage(target, hit.Poise, finalDamage, Entity);
                ReactionEffectExecutor.ExecuteDerivedHits(primaryReaction);
                LogManager.Log(LogCategory.Damage,
                    $"{Entity.EntityID} 攻击 {target.EntityID} : {finalDamage:F1} | Anemo | 削韧 {hit.Poise}");
            }
            SwirlBatchResult swirl = SwirlReactionHandler.ResolvePreparedBatch(prepared);
            StatusOnHitHookSystem.NotifyReactions(swirl.Reaction.TriggeredReactions);
            ReactionEffectExecutor.ExecuteDerivedHits(swirl.Reaction);
        }
    }

    // ----------------------------------------------------------------
    //  ApplyStatus
    // ----------------------------------------------------------------
    void ExecuteApplyStatus(SkillEffectData eff)
    {
        string statusID2 = eff.Param1;
        if (string.IsNullOrEmpty(statusID2)) return;

        var dm = DataManager.Instance;
        if (!dm.StatusMainDict.TryGetValue(statusID2, out var statusMain))
        {
            LogManager.LogWarning(LogCategory.ApplyStatus, $"状态不存在: {statusID2}");
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
            target.AddStatus(statusID2, Entity, eff.Duration, eff.AddInPhase, eff.TriggerPhase, statusMain);
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
            var inst = new StatusInstance
            {
                StatusID2 = statusID2,
                Caster = Entity,
                RemainingPhaseCount = eff.Duration,
                AddInPhase = eff.AddInPhase,
                TriggerPhase = eff.TriggerPhase,
                MainData = statusMain,
                IsActive = true,
                ApplyOrder = ++BattleEntity._applyOrderCounter
            };
            slot.StatusList.Add(inst);
            LogManager.Log(LogCategory.ApplyStatus, $"{statusID2} -> {slot} (dur={eff.Duration}, phase={eff.AddInPhase}, trig={eff.TriggerPhase})");
            StatusBindingSystem.ApplyBindings(inst, slot);
            // OnApply：位置状态施加时立刻生效（由状态施放者控制器执行）
            if (inst.Caster != null && inst.Caster.CharacterCtrl != null)
                inst.Caster.CharacterCtrl.OnStatusApplied(inst, slot);
            // 状态结算登记（2026-08-12）：位置状态按 AddInPhase/TriggerPhase 登记阶段桶
            BattleManager.Instance?.RegisterStatusTick(inst, null, slot);
            PreDamageHookSystem.RegisterPreDamageHook(inst, slot);
            // Kill 钩子登记（2026-08-19，逻辑见 KillHookSystem.cs）
            KillHookSystem.Register(inst, slot);
        }
    }

    // ----------------------------------------------------------------
    //  RemoveStatus
    // ----------------------------------------------------------------
    /// <summary>只从一个已解析出的场地位置移除状态。</summary>
    void RemoveStatusFromFieldSlot(string statusID2, FieldPosition slot)
    {
        var bm = BattleManager.Instance;
        if (bm == null || bm.Field == null || slot == null) return;
        for (int i = slot.StatusList.Count - 1; i >= 0; i--)
        {
            var inst = slot.StatusList[i];
            if (inst == null || inst.StatusID2 != statusID2) continue;
            StatusBindingSystem.RemoveBindings(inst);
            //解除 ChangeControl记录 + 计时注销
            if (inst.Caster != null && inst.Caster.CharacterCtrl != null)
                inst.Caster.CharacterCtrl.OnStatusChangeControlRemoved(inst);
            bm.UnregisterStatusTick(inst);
            PreDamageHookSystem.UnregisterPreDamageHook(inst);
            // Kill 钩子注销（2026-08-19，逻辑见 KillHookSystem.cs）
            KillHookSystem.Unregister(inst);
            slot.StatusList.RemoveAt(i);
            LogManager.Log(LogCategory.RemoveStatus, $"{statusID2} removed from {slot}");
        }
    }

    void ExecuteRemoveStatus(SkillEffectData eff)
    {
        string statusID2 = eff.Param1;
        if (string.IsNullOrEmpty(statusID2)) return;

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
            bool removed = target.RemoveStatus(statusID2, eff.TargetNumber < 0 ? -1 : 1);
            if (removed)
                LogManager.Log(LogCategory.RemoveStatus, $"{statusID2} removed from {target.EntityID}");
        }
    }

    // ----------------------------------------------------------------
    //  ExecuteSkill（递归执行目标技能的效果序列）
    // ----------------------------------------------------------------
    void ExecuteExecuteSkill(SkillEffectData eff)
    {
        string targetSkillID2 = eff.Param1;
        if (string.IsNullOrEmpty(targetSkillID2)) return;
        ExecuteSkillByID(targetSkillID2, GetSkillTypeForExecution(targetSkillID2, 1), false);
    }

    // ----------------------------------------------------------------
    //  ExecuteEffect（直接执行目标效果，箭雨状态触发用）
    // ----------------------------------------------------------------
    void ExecuteExecuteEffect(SkillEffectData eff)
    {
        string targetEffID2 = eff.Param1;
        if (string.IsNullOrEmpty(targetEffID2)) return;

        var dm = DataManager.Instance;
        if (!dm.SkillEffectDict.TryGetValue(targetEffID2, out var targetEff)) return;
        if (targetEff.EffectType == "Damage")
        {
            // 箭雨：直接按爆发类型取伤害等级
            ExecuteDamage(targetEff, 3);
        }
        else
        {
            ExecuteEffect(targetEff, GetEffectSkillType(targetEff.SkillEffectID2, 3));
        }
    }

    // ----------------------------------------------------------------
    //  GainEnergy（能量获取）
    //  配表术语 1.21:
    //    TotalEnergy = GainedEnergy × OnstageStatus × ElementStatus × (EnergyRechargeRate + RechargeBonus)
    //    Based=走该公式；Direct/Flat=固定值不受系数影响
    // ----------------------------------------------------------------
    void ExecuteGainEnergy(SkillEffectData eff)
    {
        if (!float.TryParse(eff.Param1, out float gained)) return;
        var targets = ResolveTargets(eff);
        if (targets == null) return;

        foreach (var target in targets)
        {
            float total = gained;
            if (eff.Param2 == "Based")
            {
                // 元素匹配：同元素1.5，不同0.5
                float elementStatus = string.IsNullOrEmpty(eff.Element)
                    ? 1f
                    : (target.Type == BattleEntity.EntityType.Character && target.CharacterCtrl != null
                       && target.CharacterCtrl._attrData != null
                       && target.CharacterCtrl._attrData.Element == eff.Element)
                        ? 1.5f : 0.5f;

                // 出战系数：出战=1（本阶段单人出战简化）
                float onstage = 1f;
                // 充能效率
                float recharge = target.TotalRechargeRate > 0 ? target.TotalRechargeRate : 1f;

                total = gained * onstage * elementStatus * recharge;
            }
            // Flat：固定值

            target.CurrentEnergy = Mathf.Min(target.CurrentEnergy + Mathf.RoundToInt(total), target.MaxEnergy);
            LogManager.Log(LogCategory.GainEnergy, $"{target.EntityID} +{total:F1} energy (mode={eff.Param2})");
        }
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
            if (rec.SourceStatusID2 != inst.StatusID2) continue;
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
    /// 多段 HitData 解析（2026-08-14）：技能等级表 Hits1-Hits7 逐段解析，每段独立结算（暴击/削韧/元素量）。
    /// %引用效果为单段（倍率在 Param1，削韧/元素量取 P,E 段）。
    /// </summary>
    List<HitData> GetHitDataList(SkillEffectData eff, int skillType)
    {
        var list = new List<HitData>();
        if (eff == null) return list;

        // %引用效果（如 C1 第二支箭 %SE_HeavyAttack_Amber1.Hits1.M*0.2,10,1）：单段
        // 倍率已在 Param1 中（ResolveBaseValue 计算），等级表倍率不再乘；削韧/元素量取 Param1 的 P,E 段
        if (!string.IsNullOrEmpty(eff.Param1) && eff.Param1.StartsWith("%"))
        {
            string rest = eff.Param1.Contains(",") ? eff.Param1.Substring(eff.Param1.IndexOf(',')) : "";
            float poise = 0f, aura = 0f;
            var p = rest.Split(',');
            if (p.Length > 1) float.TryParse(p[1].Trim(), out poise);
            if (p.Length > 2) float.TryParse(p[2].Trim(), out aura);
            list.Add(new HitData(1f, poise, aura));
            return list;
        }

        if (!_levelDataBySkillType.TryGetValue(skillType, out var levelList) || levelList.Count == 0)
            return list;

        // 同一 SkillType 可包含多套 ParamID；先锁定效果分组，再在组内按等级取值。
        List<SkillLevelData> effectLevels = levelList.FindAll(s => s.ParamID == eff.SkillEffectID2);
        if (effectLevels.Count == 0 && levelList.Count > 0)
        {
            string fallbackParamID = levelList[0].ParamID;
            effectLevels = levelList.FindAll(s => s.ParamID == fallbackParamID);
            LogManager.LogWarning(LogCategory.GetHitData,
                $"{eff.SkillEffectID2} 未匹配到SkillLevel分组，fallback到 {fallbackParamID}");
        }
        if (effectLevels.Count == 0) return list;

        int skillLevel = (skillType >= 0 && skillType < SkillLevels.Length) ? SkillLevels[skillType] : 1;
        int curLevel = Mathf.Clamp(skillLevel, 1, 15);
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

    /// <summary>
    /// 解析 Param2 的 Splash 参数（格式：Splash(倍率; 左扩展, 右扩展)，如 Splash(0.5; 1,1)）。
    /// 溅射伤害 = 主目标实际伤害 × 倍率，向目标位置左右各扩展指定位数。
    /// </summary>
    static bool TryParseSplash(string param2, out float rate, out int left, out int right)
    {
        rate = 0f;
        left = 0;
        right = 0;
        if (string.IsNullOrEmpty(param2)) return false;

        int p = param2.IndexOf("Splash(");
        if (p < 0) return false;

        string inner = param2.Substring(p + 7);
        int close = inner.IndexOf(')');
        if (close < 0) return false;
        inner = inner.Substring(0, close);

        // 严格按配表格式：Splash(n; X,X)——倍率后分号，左右扩展内逗号（2026-08-14 统一）
        var segs = inner.Split(';');
        if (segs.Length < 2) return false;
        if (!float.TryParse(segs[0].Trim(), out rate)) return false;

        var range = segs[1].Trim().Split(',');
        if (range.Length < 2) return false;
        int.TryParse(range[0].Trim(), out left);
        int.TryParse(range[1].Trim(), out right);
        return true;
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
        return list.Count > 0 ? list[0] : new HitData(0, 0, 0);
    }

    HitData MergeHitData(string curStr, string lv1Str)
    {
        HitData cur = HitDataParser.Parse(curStr);
        HitData lv1 = HitDataParser.Parse(lv1Str);
        return HitDataParser.MergeWithLevel1(cur, lv1);
    }

    /// <summary>
    /// 最终攻击力 = 基础 TotalATK × (1 + 状态ATKBonus加成，按生效范围过滤)（T2/C6 攻击%生效）。
    /// </summary>
    public float GetFinalATK(string effectID2, int skillType, string element)
    {
        return (Entity.TotalATK + Entity.WeaponATK)
            * (1f + Entity.WeaponATKBonus
                + Entity.GetStatusATKBonus(effectID2, GetExecutingSkillID2(skillType), skillType, element));
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
        if (param1.Contains("*DEF")) return Entity.TotalDEF * ExtractParamMultiplier(param1);
        if (param1.Contains("*MaxHP") || param1.Contains("*TotalHP")) return Entity.TotalHP * ExtractParamMultiplier(param1);
        if (param1.Contains("*TotalATK")) return GetFinalATK(effectID2, skillType, element) * ExtractParamMultiplier(param1);
        if (param1.Contains("*TotalDEF")) return Entity.TotalDEF * ExtractParamMultiplier(param1);

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
    /// 解析 % 引用：格式 %效果唯一ID.HitsN.M,P,E 或 %效果唯一ID.HitsN.M*系数,P,E
    /// 返回"基准属性值 × 引用倍率"（M token 的倍率会乘以 scale）。
    /// 削韧(P)/元素量(E) 的继承由调用方另行处理（当前 C1/STE_Bunny3 主要用 M）。
    /// </summary>
    float ResolveReferenceBaseValue(string expression, string effectID2, int skillType, string element)
    {
        // 例：%SE_HeavyAttack_Amber1.Hits1.M*0.2,10,1
        // 拆出 P,E 部分（逗号分隔）
        string main = expression;
        if (expression.Contains(","))
            main = expression.Substring(0, expression.IndexOf(','));

        // 处理 *scale（只影响 M）
        float scale = 1f;
        string refPart = main;
        if (main.Contains("*"))
        {
            string[] parts = main.Split('*');
            refPart = parts[0];
            if (parts.Length > 1 && float.TryParse(parts[1], out float s))
                scale = s;
        }

        // refPart = %SE_HeavyAttack_Amber1.Hits1.M
        string trimmed = refPart.TrimStart('%');
        string[] dotParts = trimmed.Split('.');
        if (dotParts.Length < 3) return Entity.TotalATK;

        string srcEffectID2 = dotParts[0];
        string hitToken = dotParts[1]; // Hits1
        string field = dotParts[2];    // M / P / E

        int hitIndex = 0;
        if (hitToken.StartsWith("Hits") && hitToken.Length > 4
            && int.TryParse(hitToken.Substring(4), out int hIdx))
            hitIndex = hIdx - 1;

        // 查来源效果的 HitData（按效果类型查对应 SkillLevel）
        var dm = DataManager.Instance;
        if (!dm.SkillEffectDict.TryGetValue(srcEffectID2, out var srcEff))
            return Entity.TotalATK;

        // 用来源效果对应的 SkillType 查等级（蓄力箭=重击1，爆发=3）
        int srcSkillType = 3;
        if (srcEff.SkillEffectID2.Contains("HeavyAttack") || srcEff.SkillEffectID2.Contains("Heavy"))
            srcSkillType = 1;

        HitData srcHit = GetHitData(srcEff, srcSkillType);

        // 基准属性：按源效果元素/类型简化取攻击力
        float baseVal = GetFinalATK(srcEffectID2, skillType, element);

        switch (field)
        {
            case "M": return baseVal * srcHit.Multiplier * scale;
            case "P": return srcHit.Poise * scale;
            case "E": return srcHit.ElementAura * scale;
            default: return baseVal * srcHit.Multiplier * scale;
        }
    }

    // ================================================================
    //  伤害公式（非反应版）
    //  配表术语 1.1: BaseDMG × (1+CritDMG) × (1+DMGBonus) × DEFRes × Res
    // ================================================================
    float CalculateDamage(
        float baseValue,
        HitData hit,
        SkillEffectData eff,
        BattleEntity target,
        int skillType,
        out ReactionDamageComponents components)
    {
        // 基础伤害
        float baseDMG = baseValue * hit.Multiplier;

        // 暴击判定（配表术语：暴击时取 (1+CritDMG)，不暴击取 1）
        // 每个敌人每个 hit 单独判定；CritRate 含状态加成（按 ApplyString/ApplyDamageType/ApplyElementType 过滤）
        float critMult = 1f;
        float critRate = Mathf.Clamp(Entity.CritRate + Entity.WeaponCritRate + Entity.GetStatusCritRate(eff.SkillEffectID2, GetExecutingSkillID2(skillType), skillType, eff.Element, target), 0f, 1f);
        float critRoll = UnityEngine.Random.value;
        if (critRoll < critRate)
        {
            critMult = 1f + Entity.CritDMG + Entity.WeaponCritDMG;
        }
        LogManager.Log(LogCategory.Crit, $"率={critRate:P0}(基础{Entity.CritRate:P0}+状态{Entity.GetStatusCritRate(eff.SkillEffectID2, GetExecutingSkillID2(skillType), skillType, eff.Element, target):P0}) 随机={critRoll:F4} → {(critRoll < critRate ? $"暴击 x{critMult:F2}" : "未暴击")}");

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
            float effectiveDef = target.TotalDEF * (1f - Entity.DEFReduction) * (1f - Entity.DEFIgnored);
            if (effectiveDef < 0) effectiveDef = 0;
            defRes = 1f - effectiveDef / (effectiveDef + 5f * Entity.Level + 500f);
            defRes = Mathf.Clamp(defRes, 0.1f, 1f);
        }

        // RES 抗性承伤（配表术语 1.7）
        float res = GetElementResistance(target, eff.Element ?? "None");
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
            CriticalMultiplier = critMult,
            DamageBonusMultiplier = dmgBonus,
            DefenseMultiplier = defRes,
            ResistanceMultiplier = resMultiplier
        };
        return baseDMG * critMult * dmgBonus * defRes * resMultiplier;
    }

    float GetElementBonus(string element)
    {
        switch (element)
        {
            case "Pyro": return Entity.PyroDmgBonus + Entity.WeaponPyroDmgBonus;
            case "Hydro": return Entity.HydroDmgBonus + Entity.WeaponHydroDmgBonus;
            case "Electro": return Entity.ElectroDmgBonus + Entity.WeaponElectroDmgBonus;
            case "Cryo": return Entity.CryoDmgBonus + Entity.WeaponCryoDmgBonus;
            case "Anemo": return Entity.AnemoDmgBonus + Entity.WeaponAnemoDmgBonus;
            case "Dendro": return Entity.DendroDmgBonus + Entity.WeaponDendroDmgBonus;
            case "Geo": return Entity.GeoDmgBonus + Entity.WeaponGeoDmgBonus;
            default: return Entity.PhysicalDmgBonus + Entity.WeaponPhysicalDmgBonus;
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
            default: return target.PhysicalRes + target.ResBonus + target.GetStatusResBonus(element);
        }
    }
}
