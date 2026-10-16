using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 敌人战斗控制器。
/// 职责：
///  - 初始化敌人属性（Enemy_Main + Base_HP/Base_ATK 曲线 + 系数）
///  - 每回合行动（TakeEnemyTurn）：按 EnemyAI_Rule 权重挑选可用技能并释放
///  - 技能效果执行（Damage 基础版；ApplyStatus 等扩展后续轮实现）
/// </summary>
public partial class EnemyBattleController : MonoBehaviour
{
    public BattleEntity Entity { get; private set; }

    public int EnemyID { get; private set; }

    public const string AirborneStatusID = AirborneStatusHandler.EnemyStatusID;

    /// <summary>击飞独立于已停用的韧性系统；状态失效/移除前禁止所有行动。</summary>
    public bool CanAct => Entity != null && Entity.IsAlive
        && !BattleSpecialRules.BlocksEnemyAction(Entity) && !FrozenReactionHandler.IsFrozen(Entity);

    /// <summary>提供给全队的次数，不是该敌人自己的行动配额。</summary>
    public int ActionContribution => dm != null && dm.EnemyMainDict.TryGetValue(EnemyID, out var main)
        ? Mathf.Max(0, main.ActsGiven) : 0;

    // ========== AI 运行时状态 ==========
    private int _currentPatternID = 0;
    private List<EnemyAIRuleData> _currentRules = new List<EnemyAIRuleData>();
    private Dictionary<int, int> _skillCooldownLeft = new Dictionary<int, int>(); // 技能ID -> 剩余冷却回合
    private Dictionary<int, int> _skillUsedThisTurn = new Dictionary<int, int>(); // 技能ID -> 本回合已用次数
    private string _currentSkillID2 = string.Empty; // 当前执行技能ID2（伤害来源声明用，2026-08-19）
    private readonly List<EnemyAIRuleData> _weightedCandidates = new List<EnemyAIRuleData>();
    private readonly List<EnemySkillEffectData> _effectBuffer = new List<EnemySkillEffectData>();
    private readonly List<int> _cooldownKeyBuffer = new List<int>();

    // ================================================================
    //  初始化
    // ================================================================
    /// <summary>
    /// 初始化敌人实体属性。
    /// 公式（配表术语 1.14/1.15）：
    ///   TotalHP  = BaseHP(曲线) × HP_coeff
    ///   TotalATK = BaseATK(曲线,按ATK_Curve) × ATK_coeff
    /// 抗性/增伤直接读 Enemy_Main。
    /// </summary>
    public void InitEnemy(int enemyID, int level)
    {
        ResetRuntimeState();
        EnemyID = enemyID;

        var entity = GetComponent<BattleEntity>();
        if (entity == null) entity = gameObject.AddComponent<BattleEntity>();
        Entity = entity;

        var dm = DataManager.Instance;
        entity.Type = BattleEntity.EntityType.Enemy;
        entity.EntityID = enemyID;
        entity.Level = level;

        if (dm == null)
        {
            LogManager.LogWarning(LogCategory.Enemy, $"DataManager missing while initializing enemyID={enemyID}");
            return;
        }

        if (dm.EnemyMainDict.TryGetValue(enemyID, out var main))
        {
            float baseHP = dm.GetEnemyBaseHP(level);
            float baseATK = dm.GetEnemyBaseATK(main.ATK_Curve, level);

            entity.TotalHP = baseHP * main.HP_coeff;
            entity.TotalATK = baseATK * main.ATK_coeff;
            entity.CurrentHP = entity.TotalHP;

            entity.MaxPoise = main.Poise;
#if UNITY_EDITOR || GTB_POISE_SYSTEM
            entity.Poise = main.Poise;
#endif

            entity.Threat = main.Threat;

            entity.PhysicalRes = main.PhysicalRes;
            entity.PyroRes = main.PyroRes;
            entity.HydroRes = main.HydroRes;
            entity.ElectroRes = main.ElectroRes;
            entity.CryoRes = main.CryoRes;
            entity.AnemoRes = main.AnemoRes;
            entity.DendroRes = main.DendroRes;
            entity.GeoRes = main.GeoRes;

            entity.PhysicalDmgBonus = main.PhysicalDmgBonus;
            entity.PyroDmgBonus = main.PyroDmgBonus;
            entity.HydroDmgBonus = main.HydroDmgBonus;
            entity.ElectroDmgBonus = main.ElectroDmgBonus;
            entity.CryoDmgBonus = main.CryoDmgBonus;
            entity.AnemoDmgBonus = main.AnemoDmgBonus;
            entity.DendroDmgBonus = main.DendroDmgBonus;
            entity.GeoDmgBonus = main.GeoDmgBonus;

            // AI：加载第一个行为模式
            if (dm.EnemyAIDict.TryGetValue(enemyID, out var aiList) && aiList.Count > 0)
            {
                _currentPatternID = aiList[0].PatternID;
                LoadPatternRules(_currentPatternID);
            }

            LogManager.Log(LogCategory.Enemy, $"{enemyID} lv{level} initialized: HP={entity.TotalHP} (cur {entity.CurrentHP}) ATK={entity.TotalATK}");
        }
        else
        {
            LogManager.LogWarning(LogCategory.Enemy, $"EnemyMainDict missing enemyID={enemyID}");
        }
    }

    // ================================================================
    //  AI 行动
    // ================================================================
    /// <summary>
    /// 单独驱动一个敌人时的兼容入口：初始化回合并执行一次行动。
    /// 正式全队回合由 BattleManager 汇总 ActsGiven，逐次调用 TakeEnemyActionAsync。
    /// </summary>
    public void TakeEnemyTurn()
    {
        if (BattleExecution.IsBusy) return;
        BattleExecution.Observe(BattleExecution.Run(TakeEnemyTurnAsync));
    }

    public async Task TakeEnemyTurnAsync()
    {
        BeginEnemyTurn();
        await TakeEnemyActionAsync();
    }

    /// <summary>每个敌方回合仅调用一次，不能在每次获分配行动时重置技能上限。</summary>
    public void BeginEnemyTurn()
    {
        _skillUsedThisTurn.Clear();
        if (Entity != null && Entity.IsAlive && !CanAct)
            LogManager.Log(LogCategory.Enemy, $"{Entity.EntityID} 受行动限制，不行动且不贡献行动次数");
    }

    public async Task TakeEnemyActionAsync()
    {
        if (!CanAct) return;
        if (BattleManager.Instance != null && BattleManager.Instance.IsBattleOver) return;

        // 无可用技能时本次机会空过；不把机会无限转移，也不清空本回合使用记录。
        var rule = PickSkillByWeight();
        if (rule != null && dm.EnemySkillMainDict.TryGetValue(rule.SkillID, out var skill))
            await ExecuteEnemySkillAsync(skill);
    }

    private DataManager dm => DataManager.Instance;

    /// <summary>
    /// 按权重挑选当前模式下的可用技能。
    /// 处于冷却或已达每回合使用上限的技能不参与权重求和。
    /// </summary>
    private EnemyAIRuleData PickSkillByWeight()
    {
        _weightedCandidates.Clear();
        int totalWeight = 0;
        foreach (var rule in _currentRules)
        {
            if (rule == null || rule.Weight <= 0) continue;
            if (_skillCooldownLeft.TryGetValue(rule.SkillID, out int cd) && cd > 0) continue;

            if (dm == null || dm.EnemySkillMainDict == null ||
                !dm.EnemySkillMainDict.TryGetValue(rule.SkillID, out var skill)) continue;

            int used = _skillUsedThisTurn.TryGetValue(rule.SkillID, out int u) ? u : 0;
            if (skill.UsePerTurn > 0 && used >= skill.UsePerTurn) continue;

            _weightedCandidates.Add(rule);
            totalWeight += rule.Weight;
        }

        if (_weightedCandidates.Count == 0 || totalWeight <= 0)
        {
            if (LogManager.IsEnabled(LogCategory.AI))
                LogManager.Log(LogCategory.AI, $"无可用候选技能。冷却={string.Join(",", _skillCooldownLeft)}");
            return null;
        }

        int roll = BattleRandom.NextInt(0, totalWeight);
        int acc = 0;
        // [AI调试] 打印候选与随机数，确认跳舞是否进入候选
        if (LogManager.IsEnabled(LogCategory.AI))
            LogManager.Log(LogCategory.AI, $"候选技能: {string.Join(",", _weightedCandidates.ConvertAll(c => c.SkillID))} (总权重{totalWeight}) roll={roll}");
        foreach (var rule in _weightedCandidates)
        {
            acc += rule.Weight;
            if (roll < acc) return rule;
        }
        return _weightedCandidates[_weightedCandidates.Count - 1];
    }

    /// <summary>
    /// 执行敌人技能：遍历其全部效果（EnemySkill_Effect + EnemySkill_Param）。
    /// 当前实现 Damage 类型；ApplyStatus 等留后续轮。
    /// </summary>
    private async Task ExecuteEnemySkillAsync(EnemySkillMainData skill)
    {
        // 同一敌方行动的全部效果、hit及响应共享额度，不把每个hit误当一次行动。
        using var resolutionAction = BattleResolutionContext.EnterAction(new BattleResolutionContext.CounterScope());
        LogManager.Log(LogCategory.Enemy, $"{EnemyID} uses skill {skill.EnemySkillID2} ({skill.EnemySkillName})");

        // 标记本回合使用次数
        _skillUsedThisTurn[skill.EnemySkillID] = (_skillUsedThisTurn.TryGetValue(skill.EnemySkillID, out int u) ? u : 0) + 1;

        // 施放后设置冷却
        if (skill.Cooldown > 0)
            _skillCooldownLeft[skill.EnemySkillID] = Mathf.CeilToInt(skill.Cooldown);

        // 按 EffectIndex 排序执行效果
        // EnemySkill_Effect 的 SkillEffectID 规则：技能ID*100 + 序号（如 2000101 -> 效果1:200010101）
        _effectBuffer.Clear();
        foreach (var kv in dm.EnemySkillEffectDict)
        {
            // SkillEffectID / 100 == 技能ID（200010101/100 = 2000101）
            if (kv.Value.SkillEffectID / 100 == skill.EnemySkillID)
                _effectBuffer.Add(kv.Value);
        }

        _effectBuffer.Sort((a, b) => a.EffectIndex.CompareTo(b.EffectIndex));

        _currentSkillID2 = skill.EnemySkillID2;
        _lastEnemyTargetResult = null;
        foreach (var eff in _effectBuffer)
        {
            if (BattleManager.Instance != null && BattleManager.Instance.IsBattleOver) break;
            await ExecuteEnemyEffectAsync(eff);
        }
    }

    /// <summary>
    /// 执行单个敌人效果。当前实现 Damage；其他类型打印日志占位。
    /// </summary>
    private Task ExecuteEnemyEffectAsync(EnemySkillEffectData eff)
    {
        int count = ExecuteCountEvaluator.Evaluate(eff.ExecuteCount, eff.ExecuteCountHandler, Entity);
        return count == 0 ? Task.CompletedTask : BattleExecution.RepeatEffect(count, eff.DamageHook,
            executionID => ExecuteEnemyEffectOnceAsync(eff, executionID));
    }

    private async Task ExecuteEnemyEffectOnceAsync(EnemySkillEffectData eff, long executionID)
    {
        using var resolutionEffect = BattleResolutionContext.BeginEffect();
        BattleExecution.ThrowIfCancelled();
        if (!ScriptHookEvaluator.Evaluate(eff.ScriptHook, Entity)) return;
        switch (eff.EffectType)
        {
            case "Damage":
                await ExecuteEnemyDamageAsync(eff, executionID);
                break;
            case "ApplyStatus":
                await BattleExecution.Play(new BattlePresentationRequest(BattlePresentationKind.Effect,
                    Entity, eff.SkillEffectID2), () => ExecuteEnemyApplyStatus(eff));
                break;
            case "BondOfLife":
                await BattleExecution.Play(new BattlePresentationRequest(BattlePresentationKind.Effect,
                    Entity, eff.SkillEffectID2), () => BondOfLifeEffect.Execute(eff.Param1,
                        Entity, ResolveEnemyTargets(eff), null, eff.SkillEffectID2, 0, eff.Element));
                break;
            case "NightSoul":
                await BattleExecution.Play(new BattlePresentationRequest(BattlePresentationKind.Effect,
                    Entity, eff.SkillEffectID2), () => NightSoulEffect.Execute(eff.Param1,
                        ResolveEnemyTargets(eff), eff.SkillEffectID2));
                break;
            default:
                LogManager.Log(LogCategory.Enemy, $"effect {eff.SkillEffectID2} type {eff.EffectType} not implemented yet");
                break;
        }
    }

    /// <summary>
    /// 敌人施加状态：与角色共用统一目标解析和状态系统。
    /// </summary>
    private void ExecuteEnemyApplyStatus(EnemySkillEffectData eff)
    {
        string statusID2 = CountStatusSystem.ResolvePublicStatusReference(eff.Param1);
        if (string.IsNullOrEmpty(statusID2)) return;

        if (!dm.StatusMainDict.TryGetValue(statusID2, out var statusMain))
        {
            LogManager.LogWarning(LogCategory.Enemy, $"状态不存在: {statusID2}");
            return;
        }

        int addPhase = eff.AddInPhase > 0 ? eff.AddInPhase : 5; // 敌方行动阶段=5
        if (!string.IsNullOrEmpty(eff.TargetType) && eff.TargetType.EndsWith("Field", System.StringComparison.Ordinal))
        {
            foreach (var field in ResolveEnemyTargetFields(eff))
            {
                field.AddStatus(statusID2, Entity, eff.Duration, addPhase, eff.TriggerPhase,
                    statusMain, null, eff.Param2);
                LogManager.Log(LogCategory.Enemy, $"ApplyStatus {statusID2} -> {field} (dur={eff.Duration}, phase={addPhase})");
            }
            return;
        }

        foreach (var target in ResolveEnemyTargets(eff))
        {
            target.AddStatus(statusID2, Entity, eff.Duration, addPhase, eff.TriggerPhase, statusMain, null, eff.Param2);
            LogManager.Log(LogCategory.Enemy, $"ApplyStatus {statusID2} -> {target.EntityID} (dur={eff.Duration}, phase={addPhase})");
        }
    }

    /// <summary>
    /// 敌人伤害：倍率×TotalATK×抗性承伤（敌人公式无暴击/无增伤状态遍历）。
    /// 目标由 EnemySkill_Effect 的 TargetType/TargetNumber/TargetConsecutive/TargetOverride 统一解析。
    /// </summary>
    private async Task ExecuteEnemyDamageAsync(EnemySkillEffectData eff, long effectExecutionID)
    {
        if (!dm.EnemySkillParamDict.TryGetValue(eff.SkillEffectID2, out var param))
        {
            LogManager.LogWarning(LogCategory.Enemy, $"no param for effect {eff.SkillEffectID2}");
            return;
        }

        List<HitData> hits = GetEnemyHitDataList(param);
        if (hits.Count == 0)
        {
            LogManager.LogWarning(LogCategory.Enemy, $"parse hit failed: {param.Hits1}");
            return;
        }

        var targets = ResolveEnemyTargets(eff);
        if (targets.Count == 0) return;

        // 增伤区（配表术语 1.16）：TotalDMG = TotalATK × SkillMultiplier × (1+DMGBonus) × DEFRes × Res
        // DMGBonus = 敌人自身所有状态中 MultiplierPart=DMGBonus 的效果数值累加（如 ST_Bunny_debuff=-0.1 → ×0.9）
        float dmgBonus = 0f;
        var allStatuses = Entity.GetEffectiveStatusList();
        if (LogManager.IsEnabled(LogCategory.DmgBonus))
            LogManager.Log(LogCategory.DmgBonus, $"{Entity.EntityID} 行动前身上状态数={allStatuses.Count}");
        var used = new Dictionary<string, int>();
        foreach (var inst in allStatuses)
        {
            if (inst?.MainData == null || inst.MainData.GetMultiplierPart() != "DMGBonus") continue;
            int count = StatusAggregation.Take(inst, used);
            if (count > 0) dmgBonus += GetStatusDMGBonus(inst) * count;
        }
        float dmgBonusFactor = 1f + dmgBonus;
        if (dmgBonusFactor < 0f) dmgBonusFactor = 0f;
        float atkBonusFactor = 1f + Entity.GetStatusATKBonus("", "", 0, "");
        for (int hitIndex = 0; hitIndex < hits.Count; hitIndex++)
        {
            await BattleExecution.Play(new BattlePresentationRequest(BattlePresentationKind.Hit, Entity, eff.SkillEffectID2, hitIndex + 1, targets), () =>
            {
                using var hitResponses = BattleResolutionContext.BeginCollection();
                HitData hit = hits[hitIndex];
                float baseDmg = Entity.TotalATK * hit.Multiplier * atkBonusFactor;
                if (eff.Element == "Anemo")
                {
                    ExecuteEnemyAnemoDamage(
                        eff, hit, hitIndex, targets, baseDmg, dmgBonusFactor, effectExecutionID);
                    return;
                }

                var reactions = new List<ReactionResult>();
                var resolutions = new List<HitResolution>();
                List<BattleEntity> hitTargets = targets.FindAll(target => target != null && target.IsAlive);
                using (var batch = new HitBatch())
                {
                    for (int targetIndex = 0; targetIndex < hitTargets.Count; targetIndex++)
                    {
                        BattleEntity target = hitTargets[targetIndex];
                        float res = GetTargetResistance(target, eff.Element);
                        float resFactor = res < 0f ? 1f - res / 2f
                            : (res < 0.75f ? 1f - res : 1f / (1f + 4f * res));
                        float targetDEF = target.GetFinalDEF();
                        float defRes = 1f - targetDEF / (targetDEF + 5f * Entity.Level + 500f);
                        if (defRes < 0f) defRes = 0f;
                        float damage = baseDmg * dmgBonusFactor * resFactor * defRes;
                        DMGTakenSnapshot taken = target.PrepareDMGTaken(
                            eff.SkillEffectID2, _currentSkillID2, eff.DamageType, eff.Element);
                        ReactionResult reaction = ReactionResolver.Resolve(new ReactionContext
                        {
                            SourceEntity = Entity,
                            Target = target,
                            SourceKind = ReactionSourceKind.EnemySkill,
                            SourceSkillID = _currentSkillID2,
                            SourceEffectID = eff.SkillEffectID2,
                            EffectExecutionID = effectExecutionID,
                            AttackElement = eff.Element,
                            AttackAmount = hit.ElementAura,
                            PreReactionDamage = damage,
                            DamageComponents = new ReactionDamageComponents
                            {
                                SkillBaseDamage = baseDmg,
                                DamageBonusMultiplier = dmgBonusFactor,
                                DefenseMultiplier = defRes,
                                ResistanceMultiplier = resFactor
                            },
                            DamageType = eff.DamageType,
                            PoiseDamage = hit.Poise
                        });
                        StatusOnHitHookSystem.NotifyReactions(reaction.TriggeredReactions);
                        reactions.Add(reaction);
                        resolutions.Add(batch.Resolve(new HitRequest
                        {
                            Target = target,
                            DamageBeforeShield = reaction.FinalDamage,
                            Element = eff.Element,
                            DamageType = eff.DamageType,
                            DMGTaken = taken,
                            PoiseDamage = hit.Poise,
                            Source = DamageSourceInfo.Create(
                                Entity,
                                ReactionSourceKind.EnemySkill,
                                _currentSkillID2,
                                eff.SkillEffectID2,
                                reaction.PrimaryDamageReactionType),
                            HitIndex = hitIndex,
                            TargetIndex = targetIndex
                        }));
                    }
                    batch.Flush();
                }
                for (int index = 0; index < reactions.Count; index++)
                {
                    HitResolution resolution = resolutions[index];
                    float actualHPDamage = resolution?.DamageEvent != null
                        ? resolution.DamageEvent.ActualHPDamage
                        : 0f;
                    ReactionEffectExecutor.FinalizePrimaryHit(reactions[index], actualHPDamage);
                    ReactionEffectExecutor.ExecuteDerivedHits(reactions[index]);
                    LogManager.Log(LogCategory.Enemy,
                        $"hit#{hitIndex + 1} {eff.SkillEffectID2} deals {resolution.DamageAfterShield:F1} dmg " +
                        $"to {resolution.Request.Target.gameObject.name} (hp left {resolution.Request.Target.CurrentHP:F1})");
                }
            });
        }
    }

    private static List<HitData> GetEnemyHitDataList(EnemySkillParamData param)
    {
        var hits = new List<HitData>();
        if (param == null) return hits;
        string[] rawHits =
        {
            param.Hits1, param.Hits2, param.Hits3, param.Hits4,
            param.Hits5, param.Hits6, param.Hits7, param.Hits8
        };
        foreach (string raw in rawHits)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            HitData hit = HitDataParser.Parse(raw);
            if (hit.Multiplier > 0f) hits.Add(hit);
        }
        return hits;
    }

    /// <summary>
    /// 累加单个状态提供的 DMGBonus（配表 MultiplierPart 任一列==DMGBonus 的效果数值）。
    /// 效果数值在状态的 StatusEffect 里（如 ST_Bunny_debuff -> STE_Bunny_debuff Param1=-0.1）。
    /// </summary>
    private void ExecuteEnemyAnemoDamage(EnemySkillEffectData eff, HitData hit, int hitIndex,
        List<BattleEntity> targets, float baseDmg, float dmgBonusFactor, long effectExecutionID)
    {
        var contexts = new List<ReactionContext>();
        var takenSnapshots = new List<DMGTakenSnapshot>();
        foreach (BattleEntity target in targets)
        {
            if (target == null || !target.IsAlive) continue;
            float res = GetTargetResistance(target, "Anemo");
            float resFactor = res < 0f ? 1f - res / 2f
                : (res < 0.75f ? 1f - res : 1f / (1f + 4f * res));
            float targetDEF = target.GetFinalDEF();
            float defRes = 1f - targetDEF / (targetDEF + 5f * Entity.Level + 500f);
            if (defRes < 0f) defRes = 0f;
            float damage = baseDmg * dmgBonusFactor * resFactor * defRes;
            takenSnapshots.Add(target.PrepareDMGTaken(
                eff.SkillEffectID2, _currentSkillID2, eff.DamageType, "Anemo"));
            contexts.Add(new ReactionContext
            {
                SourceEntity = Entity, Target = target, SourceKind = ReactionSourceKind.EnemySkill,
                SourceSkillID = _currentSkillID2, SourceEffectID = eff.SkillEffectID2,
                EffectExecutionID = effectExecutionID, AttackElement = "Anemo",
                AttackAmount = hit.ElementAura, PreReactionDamage = damage,
                DamageComponents = new ReactionDamageComponents
                { SkillBaseDamage = baseDmg,
                  DamageBonusMultiplier = dmgBonusFactor, DefenseMultiplier = defRes,
                  ResistanceMultiplier = resFactor },
                DamageType = eff.DamageType, PoiseDamage = hit.Poise
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
        using (var batch = new HitBatch())
        {
            for (int index = 0; index < contexts.Count; index++)
            {
                resolutions.Add(batch.Resolve(new HitRequest
                {
                    Target = contexts[index].Target,
                    DamageBeforeShield = primaryReactions[index].FinalDamage,
                    Element = "Anemo",
                    DamageType = eff.DamageType,
                    DMGTaken = takenSnapshots[index],
                    PoiseDamage = hit.Poise,
                    Source = DamageSourceInfo.Create(Entity,
                        ReactionSourceKind.EnemySkill, _currentSkillID2,
                        eff.SkillEffectID2, ReactionType.None),
                    HitIndex = hitIndex,
                    TargetIndex = index
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
        }
        SwirlBatchResult swirl = SwirlReactionHandler.ResolvePreparedBatch(prepared);
        StatusOnHitHookSystem.NotifyReactions(swirl.Reaction.TriggeredReactions);
        ReactionEffectExecutor.ExecuteDerivedHits(swirl.Reaction);
    }
    private float GetStatusDMGBonus(StatusInstance inst)
    {
        if (inst == null || inst.MainData == null) return 0f;
        if (LogManager.IsEnabled(LogCategory.DmgBonus))
            LogManager.Log(LogCategory.DmgBonus, $"状态{inst.StatusID2} MultiplierPart={inst.MainData?.GetMultiplierPart() ?? "<null>"}");
        if (inst.MainData.GetMultiplierPart() != "DMGBonus") return 0f;

        var dm = DataManager.Instance;
        if (dm == null || dm.StatusEffectDict == null) return 0f;

        float total = 0f;
        int statusID = inst.MainData.StatusID;
        foreach (var kv in dm.StatusEffectDict)
        {
            var eff = kv.Value;
            if (eff.StatusEffectID / 100 != statusID) continue;
            if (eff.EffectType != "Debuff" && eff.EffectType != "Buff") continue;
            if (BuffExpressionEvaluator.TryResolveScalar(eff.Param1, inst, out float val))
                total += val;
        }
        return total;
    }

    /// <summary>
    /// 获取目标实体对应元素的抗性。
    /// </summary>
    private float GetTargetResistance(BattleEntity target, string element)
    {
        float bonus = target.ResBonus + target.GetStatusResBonus(element);
        switch (element)
        {
            case "Pyro": return target.PyroRes + bonus;
            case "Hydro": return target.HydroRes + bonus;
            case "Electro": return target.ElectroRes + bonus;
            case "Cryo": return target.CryoRes + bonus;
            case "Anemo": return target.AnemoRes + bonus;
            case "Dendro": return target.DendroRes + bonus;
            case "Geo": return target.GeoRes + bonus;
            default: return target.PhysicalRes + bonus;
        }
    }

    // ================================================================
    //  AI 辅助
    // ================================================================
    private void LoadPatternRules(int patternID)
    {
        _currentRules.Clear();
        if (dm == null || dm.EnemyAIRuleDict == null ||
            !dm.EnemyAIRuleDict.TryGetValue(EnemyID, out var rules) || rules == null)
            return;

        foreach (var rule in rules)
        {
            if (rule != null && rule.PatternID == patternID) _currentRules.Add(rule);
        }
        if (_currentRules.Count > 0)
        {
            _currentPatternID = patternID;
            LogManager.Log(LogCategory.Enemy, $"{EnemyID} loaded pattern {patternID}: {_currentRules.Count} rules");
        }
    }

    /// <summary>
    /// 技能冷却全局递减（由 BattleManager 在全局跨回合时统一调用）。
    /// </summary>
    public void TickSkillCooldowns()
    {
        _cooldownKeyBuffer.Clear();
        _cooldownKeyBuffer.AddRange(_skillCooldownLeft.Keys);
        foreach (var k in _cooldownKeyBuffer)
        {
            _skillCooldownLeft[k]--;
            if (_skillCooldownLeft[k] <= 0) _skillCooldownLeft.Remove(k);
        }
    }

    private void ResetRuntimeState()
    {
        _currentPatternID = 0;
        _currentRules.Clear();
        _skillCooldownLeft.Clear();
        _skillUsedThisTurn.Clear();
        _weightedCandidates.Clear();
        _effectBuffer.Clear();
        _cooldownKeyBuffer.Clear();
        _currentSkillID2 = string.Empty;
        _lastEnemyTargetResult = null;
    }
}
