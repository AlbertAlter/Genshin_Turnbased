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
    /// 敌人回合行动入口：按权重选技能并执行。
    /// 每回合至少行动一次；可用行动次数由 Enemy_Main.ActsGiven 决定（当前简化：先按1次）。
    /// </summary>
    public void TakeEnemyTurn()
    {
        if (BattleExecution.IsBusy) return;
        BattleExecution.Observe(BattleExecution.Run(TakeEnemyTurnAsync));
    }

    public async Task TakeEnemyTurnAsync()
    {
        if (Entity == null || Entity.IsDead) return;
        if (BattleManager.Instance != null && BattleManager.Instance.IsBattleOver) return;
        if (FrozenReactionHandler.IsFrozen(Entity))
        {
            LogManager.Log(LogCategory.Enemy, $"{Entity.EntityID} 处于冻结状态，无法行动");
            return;
        }

        int acts = 1; // 简化：暂时固定每次行动1次，ActsGiven 扩展后续轮
#if GTB_POISE_SYSTEM
        bool skipFirstAction = PoiseSystem.ConsumeEnemyFirstAction(Entity);
#endif
        for (int i = 0; i < acts; i++)
        {
#if GTB_POISE_SYSTEM
            if (skipFirstAction)
            {
                skipFirstAction = false;
                continue;
            }
#endif

            var rule = PickSkillByWeight();
            if (rule == null) break;

            if (dm.EnemySkillMainDict.TryGetValue(rule.SkillID, out var skill))
            {
                await ExecuteEnemySkillAsync(skill);
            }
            if (BattleManager.Instance != null && BattleManager.Instance.IsBattleOver) break;
        }

        // 回合结束：重置本回合使用次数（冷却递减移至 BattleManager 全局跨回合处理）
        _skillUsedThisTurn.Clear();
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
    private async Task ExecuteEnemyEffectAsync(EnemySkillEffectData eff)
    {
        using var resolutionEffect = BattleResolutionContext.BeginEffect();
        switch (eff.EffectType)
        {
            case "Damage":
                await ExecuteEnemyDamageAsync(eff);
                break;
            case "ApplyStatus":
                await BattleExecution.Play(new BattlePresentationRequest(BattlePresentationKind.Effect,
                    Entity, eff.SkillEffectID2), () => ExecuteEnemyApplyStatus(eff));
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
        string statusID2 = eff.Param1;
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
                var inst = new StatusInstance
                {
                    StatusID2 = statusID2,
                    RemainingPhaseCount = eff.Duration,
                    IsPermanent = eff.Duration <= 0,
                    AddInPhase = addPhase,
                    TriggerPhase = eff.TriggerPhase,
                    MainData = statusMain,
                    IsActive = true,
                    ApplyOrder = ++BattleEntity._applyOrderCounter
                };
                inst.RecordSource(Entity);
                field.StatusList.Add(inst);
                StatusBindingSystem.ApplyBindings(inst, field);
                if (inst.Caster != null && inst.Caster.CharacterCtrl != null)
                    inst.Caster.CharacterCtrl.OnStatusApplied(inst, field);
                BattleManager.Instance.RegisterStatusTick(inst, null, field);
                PreDamageHookSystem.RegisterPreDamageHook(inst, field);
                // Kill 钩子登记（2026-08-19，逻辑见 KillHookSystem.cs）
                KillHookSystem.Register(inst, field);
                LogManager.Log(LogCategory.Enemy, $"ApplyStatus {statusID2} -> {field} (dur={eff.Duration}, phase={addPhase})");
            }
            return;
        }

        foreach (var target in ResolveEnemyTargets(eff))
        {
            target.AddStatus(statusID2, Entity, eff.Duration, addPhase, eff.TriggerPhase, statusMain);
            LogManager.Log(LogCategory.Enemy, $"ApplyStatus {statusID2} -> {target.EntityID} (dur={eff.Duration}, phase={addPhase})");
        }
    }

    /// <summary>
    /// 敌人伤害：倍率×TotalATK×抗性承伤（敌人公式无暴击/无增伤状态遍历）。
    /// 目标由 EnemySkill_Effect 的 TargetType/TargetNumber/TargetConsecutive/TargetOverride 统一解析。
    /// </summary>
    private async Task ExecuteEnemyDamageAsync(EnemySkillEffectData eff)
    {
        long effectExecutionID = ReactionResolver.BeginEffectExecution();
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
        foreach (var inst in allStatuses)
        {
            dmgBonus += GetStatusDMGBonus(inst);
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
                        float defRes = 1f - target.TotalDEF / (target.TotalDEF + 5f * Entity.Level + 500f);
                        if (defRes < 0f) defRes = 0f;
                        float damage = baseDmg * dmgBonusFactor * resFactor * defRes;
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
        foreach (BattleEntity target in targets)
        {
            if (target == null || !target.IsAlive) continue;
            float res = GetTargetResistance(target, "Anemo");
            float resFactor = res < 0f ? 1f - res / 2f
                : (res < 0.75f ? 1f - res : 1f / (1f + 4f * res));
            float defRes = 1f - target.TotalDEF / (target.TotalDEF + 5f * Entity.Level + 500f);
            if (defRes < 0f) defRes = 0f;
            float damage = baseDmg * dmgBonusFactor * resFactor * defRes;
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
            if (float.TryParse(eff.Param1, out float val))
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
