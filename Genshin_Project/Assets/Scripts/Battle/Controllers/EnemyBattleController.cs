using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>敌人的生命阶段。每个阶段对应一管独立血量。</summary>
public readonly struct EnemyHealthPhases
{
    public static readonly EnemyHealthPhases Single = new EnemyHealthPhases(1, 1);
    public readonly int Current, Count;

    public EnemyHealthPhases(int currentPhase, int phaseCount)
    {
        if (phaseCount < 1) throw new System.ArgumentOutOfRangeException(nameof(phaseCount));
        if (currentPhase < 1 || currentPhase > phaseCount)
            throw new System.ArgumentOutOfRangeException(nameof(currentPhase));
        Current = currentPhase;
        Count = phaseCount;
    }

    public int Remaining(float currentHP) => Count - Current + (Count > 0 && currentHP > 0 ? 1 : 0);
}

/// <summary>
/// 敌人战斗控制器。
/// 职责：
///  - 初始化敌人属性（Enemy_Main + Base_HP/Base_ATK 曲线 + 系数）
///  - 每回合行动（TakeEnemyTurn）：按 EnemyAI_Rule 权重挑选可用技能并释放
///  - 完整技能行动声明与效果执行（Damage / ApplyStatus / ApplyElement等公共入口）
/// </summary>
public partial class EnemyBattleController : MonoBehaviour
{
    public BattleEntity Entity { get; private set; }

    public int EnemyID { get; private set; }

    public EnemyHealthPhases HealthPhases { get; private set; } = EnemyHealthPhases.Single;

    // 由敌人的阶段机制在初始化和切阶段时调用，阶段从 1 开始。
    public void SetHealthPhase(int currentPhase, int phaseCount)
    {
        HealthPhases = new EnemyHealthPhases(currentPhase, phaseCount);
    }

    public const string AirborneStatusID = AirborneStatusHandler.EnemyStatusID;

    /// <summary>击飞由普通状态控制；状态失效/移除前禁止所有行动。</summary>
    private bool CanActIgnoringPresence => Entity != null && Entity.IsAlive
        && !BattleSpecialRules.BlocksEnemyAction(Entity) && !FrozenReactionHandler.IsFrozen(Entity);
    public bool CanAct => CanActIgnoringPresence && !Entity.IsOffField;
    public bool CanContributeActions => CanActIgnoringPresence; // 离场保留总池贡献，只取消分配资格。
    private bool _statusCalledSkill;

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
        entity.EnemyCtrl = this;

        var dm = DataManager.Instance;
        entity.Type = BattleEntity.EntityType.Enemy;
        entity.Side = BattleSide.Enemy;
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
            BattleAttributeInitialization.Prepare(entity);

            // AI：加载第一个行为模式
            if (dm.EnemyAIDict.TryGetValue(enemyID, out var aiList) && aiList.Count > 0)
            {
                _currentPatternID = aiList[0].PatternID;
                LoadPatternRules(_currentPatternID);
            }

            LogManager.Log(LogCategory.Enemy, $"{enemyID} lv{level} initialized: HP={entity.TotalHP} (cur {entity.CurrentHP}) ATK={entity.TotalATK}");
            if (BattleManager.Instance?.IsBattleRunning == true) ApplyInitiateStatuses();
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
            LogManager.Log(LogCategory.Enemy, $"{Entity.EntityID} 受行动限制，不行动；保留行动贡献={CanContributeActions}");
    }

    public bool IsLastGuaranteedAction { get; private set; } = true;
    public int CurrentPatternID => _currentPatternID;

    public void SwitchPattern(int patternID)
    {
        if (patternID <= 0 || dm == null || !dm.EnemyAIDict.TryGetValue(EnemyID, out var patterns)
            || !patterns.Exists(p => p.PatternID == patternID))
            throw new System.InvalidOperationException($"敌人{EnemyID}没有定义模式{patternID}。");
        if (!dm.EnemyAIRuleDict.TryGetValue(EnemyID, out var rules)
            || !rules.Exists(r => r.PatternID == patternID && r.Weight > 0))
            throw new System.InvalidOperationException($"敌人{EnemyID}的模式{patternID}没有可抽选的技能。");
        LoadPatternRules(patternID);
    }

    public bool CanReceiveOrdinaryAction => CanAct && !EnemyChangeControl.IsFrozen(Entity);
    public bool CanReceiveExtraAction => CanReceiveOrdinaryAction && !EnemyChangeControl.IsExtraFrozen(Entity);

    public Task TakeEnemyActionAsync() => TakeEnemyActionAsync(true);

    public async Task TakeEnemyActionAsync(bool isLastGuaranteedAction)
    {
        if (!CanAct) return;
        if (BattleManager.Instance != null && BattleManager.Instance.IsBattleOver) return;
        IsLastGuaranteedAction = isLastGuaranteedAction;
        if (!CanReceiveOrdinaryAction) return;

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
            if (!MeetsSkillLimits(rule.SkillID)) continue;
            if (!ScriptHookEvaluator.Evaluate(rule.ScriptHook, new ScriptHookContext { Caster = Entity })) continue;

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

    /// <summary>只查询配置存在、剩余冷却及回合次数，不求值资格钩子，供互斥必放规则复用。</summary>
    public bool MeetsSkillLimits(int skillID)
    {
        if (dm == null || !dm.EnemySkillMainDict.TryGetValue(skillID, out var skill)) return false;
        if (_skillCooldownLeft.TryGetValue(skillID, out int remaining) && remaining > 0) return false;
        _skillUsedThisTurn.TryGetValue(skillID, out int used);
        return skill.UsePerTurn <= 0 || used < skill.UsePerTurn;
    }

    /// <summary>
    /// 执行敌人技能：遍历其全部效果（EnemySkill_Effect + EnemySkill_Param）。
    /// 一个技能的全部效果与hit构成一次行动；不为每个效果或目标另发行动声明。
    /// </summary>
    private Task ExecuteEnemySkillAsync(EnemySkillMainData skill, bool fromStatus = false)
        => BattleDeathResolution.RunAsync(() => ExecuteEnemySkillCoreAsync(skill, fromStatus));

    private async Task ExecuteEnemySkillCoreAsync(EnemySkillMainData skill, bool fromStatus)
    {
        // 同一敌方行动的全部效果、hit及响应共享额度，不把每个hit误当一次行动。
        using var resolutionAction = BattleResolutionContext.EnterAction(new BattleResolutionContext.CounterScope());
        using var actionExecution = BattleEventHub.BeginActionExecution();
        BattleEventHub.Publish(new ActionEventData(Entity, skill.EnemySkillID2, "EnemySkill", false));
        await BattleExecution.Flush();
        // 行动前的正式状态伤害（例如风之眼）可能已经击杀/限制该单位或结束战斗。
        // 此时不能继续写技能使用/冷却、执行原效果或伪造ActionCompleted。
        if (!(fromStatus ? CanActIgnoringPresence : CanAct) || BattleExecution.IsCancelled || BattleManager.Instance?.IsBattleOver == true) return;
        LogManager.Log(LogCategory.Enemy, $"{EnemyID} uses skill {skill.EnemySkillID2} ({skill.EnemySkillName})");
        if (!fromStatus) BattleExecution.RecordSkillPrompt(Entity, skill.EnemySkillName, skill.EnemySkillID2);

        // 标记本回合使用次数
        _skillUsedThisTurn[skill.EnemySkillID] = (_skillUsedThisTurn.TryGetValue(skill.EnemySkillID, out int u) ? u : 0) + 1;

        // 施放后设置冷却
        if (skill.Cooldown > 0)
            _skillCooldownLeft[skill.EnemySkillID] = Mathf.CeilToInt(skill.Cooldown);

        // 按 EffectIndex 排序执行效果
        // 局部列表防止嵌套技能修改外层正在遍历的缓冲。
        // EnemySkill_Effect 的 SkillEffectID 规则：技能ID*100 + 序号（如 200102 -> 20010201）。
        var effects = new List<EnemySkillEffectData>();
        foreach (var kv in dm.EnemySkillEffectDict)
        {
            // SkillEffectID / 100 == 技能ID（20010201/100 = 200102）
            if (kv.Value.SkillEffectID / 100 == skill.EnemySkillID)
                effects.Add(kv.Value);
        }

        effects.Sort((a, b) => a.EffectIndex.CompareTo(b.EffectIndex));

        bool previousStatusCalledSkill = _statusCalledSkill;
        _statusCalledSkill = fromStatus;
        string previousSkill = _currentSkillID2;
        var previousTargets = _lastEnemyTargetResult;
        _currentSkillID2 = skill.EnemySkillID2;
        _lastEnemyTargetResult = null;
        try
        {
            foreach (var eff in effects)
            {
                if (!Entity.IsAlive || BattleExecution.IsCancelled || BattleManager.Instance?.IsBattleOver == true) return;
                await ExecuteEnemyEffectAsync(eff);
            }
            // 中途取消/战斗已结束不冒充完整行动；异常同样直接退出而不提交Completed。
            if (BattleExecution.IsCancelled || BattleManager.Instance?.IsBattleOver == true) return;
            BattleEventHub.Publish(new ActionEventData(Entity, skill.EnemySkillID2, "EnemySkill", true));
            await BattleExecution.Flush();
        }
        finally { _currentSkillID2 = previousSkill; _lastEnemyTargetResult = previousTargets; _statusCalledSkill = previousStatusCalledSkill; }
    }

    /// <summary>
    /// 执行单个敌人效果，复用公共伤害、状态和元素施加入口。
    /// </summary>
    private Task ExecuteEnemyEffectAsync(EnemySkillEffectData eff, StatusInstance sourceStatus = null)
    {
        int count = ExecuteCountEvaluator.Evaluate(eff.ExecuteCount, eff.ExecuteCountHandler, Entity);
        string repeatHook = Enemy2011To2020Hooks.HandlesHealHook(eff) ? null : eff.DamageHook;
        return count == 0 ? Task.CompletedTask : BattleExecution.RepeatEffect(count, repeatHook,
            executionID => ExecuteEnemyEffectOnceAsync(eff, executionID, sourceStatus), Entity, eff.SkillEffectID2);
    }

    private async Task ExecuteEnemyEffectOnceAsync(EnemySkillEffectData eff, long executionID, StatusInstance sourceStatus)
    {
        using var resolutionEffect = BattleResolutionContext.BeginEffect();
        BattleExecution.ThrowIfCancelled();
        if (!ScriptHookEvaluator.Evaluate(eff.ScriptHook, Entity)) return;
        switch (eff.EffectType)
        {
            case "Summon":
                await BattleExecution.Play(new BattlePresentationRequest(BattlePresentationKind.Effect, Entity, eff.SkillEffectID2),
                    () => Enemy2021To2030Hooks.Summon(Entity, sourceStatus, eff.Param1));
                break;
            case "ClearAura":
                await BattleExecution.Play(new BattlePresentationRequest(BattlePresentationKind.Effect, Entity, eff.SkillEffectID2), () => {
                    foreach (var target in ResolveEnemyTargets(eff, sourceStatus)) Enemy2021To2030Hooks.ClearAuras(target);
                });
                break;
            case "Damage":
                await ExecuteEnemyDamageAsync(eff, executionID, sourceStatus);
                break;
            case "Heal":
                await ExecuteEnemyHealAsync(eff, sourceStatus);
                break;
            case "ApplyStatus":
                await BattleExecution.Play(new BattlePresentationRequest(BattlePresentationKind.Effect,
                    Entity, eff.SkillEffectID2), () => ExecuteEnemyApplyStatus(eff, sourceStatus));
                break;
            case "RemoveStatus":
                await BattleExecution.Play(new BattlePresentationRequest(BattlePresentationKind.Effect,
                    Entity, eff.SkillEffectID2), () => ExecuteEnemyRemoveStatus(eff, sourceStatus));
                break;
            case "Cooldown":
                await BattleExecution.Play(new BattlePresentationRequest(BattlePresentationKind.Effect,
                    Entity, eff.SkillEffectID2), () => ExecuteEnemyCooldown(eff, sourceStatus));
                break;
            case "GrantAction":
                await BattleExecution.Play(new BattlePresentationRequest(BattlePresentationKind.Effect,
                    Entity, eff.SkillEffectID2), () => {
                        if (!int.TryParse(eff.Param1, out int delta))
                            throw new System.FormatException("GrantAction的Param1必须为有符号整数变动值。");
                        if (BattleManager.Instance == null || !BattleManager.Instance.TryGrantEnemyActions(delta))
                            LogManager.Log(LogCategory.Enemy, "GrantAction跳过：当前没有正在执行的敌方行动池。");
                    });
                break;
            case "SwitchPattern":
                await BattleExecution.Play(new BattlePresentationRequest(BattlePresentationKind.Effect,
                    Entity, eff.SkillEffectID2), () => {
                        if (!int.TryParse(eff.Param1, out int pattern) || pattern <= 0)
                            throw new System.FormatException("SwitchPattern的Param1必须为正整数模式ID。");
                        SwitchPattern(pattern);
                    });
                break;
            case "BondOfLife":
                await BattleExecution.Play(new BattlePresentationRequest(BattlePresentationKind.Effect,
                    Entity, eff.SkillEffectID2), () => BondOfLifeEffect.Execute(eff.Param1,
                        Entity, ResolveEnemyTargets(eff, sourceStatus), null, eff.SkillEffectID2, 0, eff.Element));
                break;
            case "NightSoul":
                await BattleExecution.Play(new BattlePresentationRequest(BattlePresentationKind.Effect,
                    Entity, eff.SkillEffectID2), () => NightSoulEffect.Execute(eff.Param1,
                        ResolveEnemyTargets(eff, sourceStatus), eff.SkillEffectID2));
                break;
            case "Count":
                await BattleExecution.Play(new BattlePresentationRequest(BattlePresentationKind.Effect,
                    Entity, eff.SkillEffectID2), () => CountEffect.Execute(eff.Param1, eff.Param2,
                        Entity, ResolveEnemyTargets(eff, sourceStatus), eff.SkillEffectID2, targetType: eff.TargetType));
                break;
            case "ApplyElement":
                await BattleExecution.Play(new BattlePresentationRequest(BattlePresentationKind.Effect,
                    Entity, eff.SkillEffectID2), () => ApplyElementEffect.Execute(eff.Element, eff.Param1,
                        Entity, ResolveEnemyTargets(eff, sourceStatus), sourceStatus == null ? ReactionSourceKind.EnemySkill : ReactionSourceKind.StatusEffect,
                        sourceStatus == null ? _currentSkillID2 : null, eff.SkillEffectID2, executionID, sourceStatus));
                break;
            case "Revive":
                await BattleExecution.Play(new BattlePresentationRequest(BattlePresentationKind.Effect,
                    Entity, eff.SkillEffectID2), () => ReviveEffect.Execute(eff.Param1, Entity, ResolveEnemyTargets(eff, sourceStatus)));
                break;
            default:
                throw new System.NotSupportedException($"敌方效果 {eff.SkillEffectID2} 的类型 {eff.EffectType} 尚未实现，不能宣告行动完成。");
        }
    }

    /// <summary>
    /// 敌人施加状态：与角色共用统一目标解析和状态系统。
    /// </summary>
    private void ExecuteEnemyApplyStatus(EnemySkillEffectData eff, StatusInstance sourceStatus)
    {
        string statusID2 = CountStatusSystem.ResolvePublicStatusReference(eff.Param1);
        if (string.IsNullOrEmpty(statusID2)) return;

        if (!dm.StatusMainDict.TryGetValue(statusID2, out var statusMain))
        {
            LogManager.LogWarning(LogCategory.Enemy, $"状态不存在: {statusID2}");
            return;
        }

        int addPhase = eff.AddInPhase > 0 ? eff.AddInPhase : 5; // 敌方行动阶段=5
        if (BattleTargetResolver.IsTeamTarget(eff.TargetType))
        {
            foreach (var team in BattleTargetResolver.GetTeams(BattleManager.Instance, ResolveEnemyTargetResult(eff, sourceStatus)))
                team.AddStatus(statusID2, Entity, eff.Duration, addPhase, eff.TriggerPhase, statusMain, null, eff.Param2,
                    enchantElement: eff.Element, enchantOverride: eff.Param3);
            return;
        }
        if (!string.IsNullOrEmpty(eff.TargetType) && eff.TargetType.EndsWith("Field", System.StringComparison.Ordinal))
        {
            foreach (var field in ResolveEnemyTargetFields(eff, sourceStatus))
            {
                field.AddStatus(statusID2, Entity, eff.Duration, addPhase, eff.TriggerPhase,
                    statusMain, null, eff.Param2, enchantElement: eff.Element, enchantOverride: eff.Param3);
                LogManager.Log(LogCategory.Enemy, $"ApplyStatus {statusID2} -> {field} (dur={eff.Duration}, phase={addPhase})");
            }
            return;
        }

        foreach (var target in ResolveEnemyTargets(eff, sourceStatus))
        {
            target.AddStatus(statusID2, Entity, eff.Duration, addPhase, eff.TriggerPhase, statusMain, null, eff.Param2,
                enchantElement: eff.Element, enchantOverride: eff.Param3);
            LogManager.Log(LogCategory.Enemy, $"ApplyStatus {statusID2} -> {target.EntityID} (dur={eff.Duration}, phase={addPhase})");
        }
    }

    private void ExecuteEnemyRemoveStatus(EnemySkillEffectData effect, StatusInstance sourceStatus)
    {
        if (string.IsNullOrWhiteSpace(effect.Param1)) return;
        if (BattleTargetResolver.IsTeamTarget(effect.TargetType))
        {
            foreach (var team in BattleTargetResolver.GetTeams(BattleManager.Instance, ResolveEnemyTargetResult(effect, sourceStatus)))
                team.RemoveStatus(effect.Param1);
        }
        else if (effect.TargetType != null && effect.TargetType.EndsWith("Field", System.StringComparison.Ordinal))
        {
            foreach (var field in ResolveEnemyTargetFields(effect, sourceStatus)) field.RemoveStatus(effect.Param1);
        }
        else foreach (var target in ResolveEnemyTargets(effect, sourceStatus)) target.RemoveStatus(effect.Param1);
    }

    /// <summary>
    /// 敌人伤害：倍率×攻击×增伤×防御承伤×抗性承伤；属性范围按实际DamageType过滤，不使用倍率分类推断。
    /// 目标由 EnemySkill_Effect 的 TargetType/TargetNumber/TargetConsecutive/TargetOverride 统一解析。
    /// </summary>
    private async Task ExecuteEnemyDamageAsync(EnemySkillEffectData authoredEffect, long effectExecutionID, StatusInstance sourceStatus)
    {
        var eff = authoredEffect;
        string skillID = sourceStatus == null ? _currentSkillID2 : null;
        var sourceKind = sourceStatus == null ? ReactionSourceKind.EnemySkill : ReactionSourceKind.StatusEffect;
        using var damageQuery = DamageQueryContext.Enter(eff.SkillEffectID2, eff.DamageType, skillID);
        DamageProperties properties = DamageProperties.Parse(eff.Element, eff.Param3);
        properties.RequireImplementedFormula();
        if (DamageProperties.IsFixed(eff.Element))
        {
            TableValidator.ValidateDamageProperties("EnemySkill_Effect", 0, "Damage", eff.Element,
                eff.Param1, eff.Param2, eff.Param3, eff.DamageHook);
            bool fixedBase = DamageProperties.TryReadFixedTrueBase(eff.Param1, out float basis);
            if (!fixedBase && eff.Param1 != "M*ATK" && eff.Param1 != "M*TotalATK")
                throw new System.FormatException("敌人 True/None 的 Param1 支持固定数值或 M*ATK：" + eff.SkillEffectID2);
            dm.EnemySkillParamDict.TryGetValue(eff.SkillEffectID2, out var trueParam);
            var trueHits = GetEnemyHitDataList(trueParam, allowZero: true);
            if (fixedBase && trueHits.Count == 0) trueHits.Add(new HitData(1f, 0f));
            await TrueDamageExecutor.ExecuteAsync(ResolveEnemyTargets(eff, sourceStatus), trueHits,
                index => fixedBase ? basis : Entity.GetFinalATK(eff.SkillEffectID2, skillID, -1, eff.Element),
                EnemyDamageSource(sourceStatus, skillID, eff.SkillEffectID2),
                eff.SkillEffectID2, eff.DamageType, eff.Element, eff.Param2, properties,
                eff.TargetType == "Self" ? sourceStatus?.Host as BattleEntity ?? (_statusCalledSkill ? Entity : null) : null);
            return;
        }
        List<HitData> hits;
        if (sourceStatus != null) hits = ParseEnemyStatusHits(eff.Param1);
        else
        {
            if (!dm.EnemySkillParamDict.TryGetValue(eff.SkillEffectID2, out var param))
                throw new System.InvalidOperationException("敌人伤害缺少倍率表：" + eff.SkillEffectID2);
            hits = GetEnemyHitDataList(param);
        }
        if (hits.Count == 0) throw new System.FormatException("敌人伤害没有有效hit：" + eff.SkillEffectID2);

        var targets = ResolveEnemyTargets(eff, sourceStatus);
        if (targets.Count == 0) return;

        // 增伤区（配表术语 1.16）：TotalDMG = TotalATK × SkillMultiplier × (1+DMGBonus) × DEFRes × Res
        // DMGBonus = 敌人自身所有状态中 MultiplierPart=DMGBonus 的效果数值累加（如 ST_Bunny_debuff=-0.1 → ×0.9）
        var allStatuses = Entity.GetEffectiveStatusList();
        if (LogManager.IsEnabled(LogCategory.DmgBonus))
            LogManager.Log(LogCategory.DmgBonus, $"{Entity.EntityID} 行动前身上状态数={allStatuses.Count}");
        for (int hitIndex = 0; hitIndex < hits.Count; hitIndex++)
        {
            var enchant = EnchantStatusSystem.Resolve(Entity, authoredEffect.DamageType, authoredEffect.Element);
            eff = enchant.HasValue ? authoredEffect.WithDamageElement(enchant.Value.Element) : authoredEffect;
            if (enchant.HasValue) hits[hitIndex] = new HitData(hits[hitIndex].Multiplier, enchant.Value.ElementAmount);
            properties = DamageProperties.Parse(eff.Element, eff.Param3);
            float finalATK = Entity.GetFinalATK(eff.SkillEffectID2, skillID, -1, eff.Element);
            await BattleExecution.Play(new BattlePresentationRequest(BattlePresentationKind.Hit, Entity, eff.SkillEffectID2, hitIndex + 1, targets), () =>
            {
                using var hitResponses = BattleResolutionContext.BeginCollection();
                HitData hit = hits[hitIndex];
                var hitTargets = DamageHitTargets.Expand(targets, eff.Param2, eff.Element,
                    eff.TargetType == "Self" ? sourceStatus?.Host as BattleEntity ?? (_statusCalledSkill ? Entity : null) : null);
                long logicalHitID = BattleEventHub.NextIdentity();
                using var hitSnapshot = BattleSpecialRules.BeginHit(logicalHitID, Entity,
                    hitTargets.ConvertAll(entry => entry.Target));
                float baseDmg = finalATK * hit.Multiplier;
                if (eff.Element == "Anemo")
                {
                    ExecuteEnemyAnemoDamage(
                        eff, hit, hitIndex, hitTargets, baseDmg, effectExecutionID, logicalHitID, sourceStatus);
                    return;
                }

                var reactions = new List<ReactionResult>();
                var resolutions = new List<HitResolution>();
                using (var batch = new HitBatch(logicalHitID))
                {
                    for (int targetIndex = 0; targetIndex < hitTargets.Count; targetIndex++)
                    {
                        var entry = hitTargets[targetIndex];
                        BattleEntity target = entry.Target;
                        float targetBase = baseDmg * entry.Rate;
                        float damage;
                        ReactionDamageComponents components;
                        if (properties.UsesSpecialFormula)
                            damage = SpecialSkillDamageCalculator.Calculate(Entity, target, targetBase,
                                properties, eff.Element, eff.SkillEffectID2, skillID,
                                eff.DamageType, out components);
                        else
                        {
                            targetBase += Entity.BaseDMGBonusFlat + Entity.GetStatusBaseDMGBonusFlat(
                                eff.SkillEffectID2, skillID, -1, eff.Element, target);
                            float dmgBonusFactor = Mathf.Max(0f, 1f + Entity.GetStatusDMGBonus(
                                eff.SkillEffectID2, skillID, -1, eff.Element, target));
                            float res = GetTargetResistance(target, eff.Element);
                            float resFactor = res < 0f ? 1f - res / 2f
                                : (res < 0.75f ? 1f - res : 1f / (1f + 4f * res));
                            float targetDEF = target.GetFinalDEF();
                            float defRes = 1f - targetDEF / (targetDEF + 5f * Entity.Level + 500f);
                            if (defRes < 0f) defRes = 0f;
                            damage = targetBase * dmgBonusFactor * resFactor * defRes;
                            components = new ReactionDamageComponents
                            {
                                SkillBaseDamage = targetBase, DamageBonusMultiplier = dmgBonusFactor,
                                DefenseMultiplier = defRes, ResistanceMultiplier = resFactor
                            };
                        }
                        DMGTakenSnapshot taken = target.PrepareDMGTaken(
                            eff.SkillEffectID2, skillID, eff.DamageType, eff.Element);
                        ReactionResult reaction = ReactionResolver.Resolve(new ReactionContext
                        {
                            SourceEntity = Entity,
                            Target = target,
                            SourceKind = sourceKind,
                            SourceSkillID = skillID,
                            SourceEffectID = eff.SkillEffectID2,
                            EffectExecutionID = effectExecutionID,
                            LogicalHitID = batch.RuntimeHitID,
                            TargetIndex = targetIndex + 1,
                            AttackElement = eff.Element,
                            AttackAmount = hit.ElementAura,
                            PreReactionDamage = damage,
                            DamageComponents = components,
                            DamageType = eff.DamageType,
                        });
                        StatusOnHitHookSystem.NotifyReactions(reaction.TriggeredReactions);
                        reactions.Add(reaction);
                        resolutions.Add(batch.Resolve(new HitRequest
                        {
                            Properties = properties,
                            Target = target,
                            DamageBeforeShield = reaction.FinalDamage,
                            Element = eff.Element,
                            ElementAmount = hit.ElementAura,
                            DamageType = eff.DamageType,
                            EnemyShieldHit = reaction.EnemyShieldHit,
                            DMGTaken = taken,
                            Source = EnemyDamageSource(sourceStatus, skillID, eff.SkillEffectID2, reaction.PrimaryDamageReactionType),
                            HitIndex = hitIndex,
                            TargetIndex = targetIndex
                        }));
                    }
                    batch.Flush();
                }
                ReactionResult sharedReactions = ReactionEffectExecutor.PrepareLunarBatch(reactions);
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
                ReactionEffectExecutor.ExecuteDerivedHits(sharedReactions);
            });
        }
    }

    private static List<HitData> GetEnemyHitDataList(EnemySkillParamData param, bool allowZero = false)
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
            if (allowZero)
            {
                if (float.IsNaN(hit.Multiplier) || float.IsInfinity(hit.Multiplier) || hit.Multiplier < 0f)
                    throw new System.FormatException("True 的倍率必须为有限非负数：" + raw);
                hits.Add(hit);
                continue;
            }
            if (hit.Multiplier > 0f) hits.Add(hit);
        }
        return hits;
    }

    /// <summary>
    /// 累加单个状态提供的 DMGBonus（配表 MultiplierPart 任一列==DMGBonus 的效果数值）。
    /// 效果数值在状态的 StatusEffect 里（如 ST_Bunny_debuff -> STE_Bunny_debuff Param1=-0.1）。
    /// </summary>
    private void ExecuteEnemyAnemoDamage(EnemySkillEffectData eff, HitData hit, int hitIndex,
        List<DamageHitTargets.Entry> targets, float baseDmg, long effectExecutionID, long logicalHitID, StatusInstance sourceStatus)
    {
        string skillID = sourceStatus == null ? _currentSkillID2 : null;
        var sourceKind = sourceStatus == null ? ReactionSourceKind.EnemySkill : ReactionSourceKind.StatusEffect;
        var contexts = new List<ReactionContext>();
        var takenSnapshots = new List<DMGTakenSnapshot>();
        foreach (var entry in targets)
        {
            BattleEntity target = entry.Target;
            float targetBase = baseDmg * entry.Rate;
            targetBase += Entity.BaseDMGBonusFlat + Entity.GetStatusBaseDMGBonusFlat(
                eff.SkillEffectID2, skillID, -1, "Anemo", target);
            float dmgBonusFactor = Mathf.Max(0f, 1f + Entity.GetStatusDMGBonus(
                eff.SkillEffectID2, skillID, -1, "Anemo", target));
            float res = GetTargetResistance(target, "Anemo");
            float resFactor = res < 0f ? 1f - res / 2f
                : (res < 0.75f ? 1f - res : 1f / (1f + 4f * res));
            float targetDEF = target.GetFinalDEF();
            float defRes = 1f - targetDEF / (targetDEF + 5f * Entity.Level + 500f);
            if (defRes < 0f) defRes = 0f;
            float damage = targetBase * dmgBonusFactor * resFactor * defRes;
            takenSnapshots.Add(target.PrepareDMGTaken(
                eff.SkillEffectID2, skillID, eff.DamageType, "Anemo"));
            contexts.Add(new ReactionContext
            {
                SourceEntity = Entity, Target = target, SourceKind = sourceKind,
                SourceSkillID = skillID, SourceEffectID = eff.SkillEffectID2,
                EffectExecutionID = effectExecutionID, AttackElement = "Anemo",
                LogicalHitID = logicalHitID, TargetIndex = contexts.Count + 1,
                AttackAmount = hit.ElementAura, PreReactionDamage = damage,
                DamageComponents = new ReactionDamageComponents
                { SkillBaseDamage = targetBase,
                  DamageBonusMultiplier = dmgBonusFactor, DefenseMultiplier = defRes,
                  ResistanceMultiplier = resFactor },
                DamageType = eff.DamageType
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
                resolutions.Add(batch.Resolve(new HitRequest
                {
                    Properties = DamageProperties.Parse(eff.Element, eff.Param3),
                    Target = contexts[index].Target,
                    DamageBeforeShield = primaryReactions[index].FinalDamage,
                    EnemyShieldHit = primaryReactions[index].EnemyShieldHit,
                    Element = "Anemo",
                    ElementAmount = hit.ElementAura,
                    DamageType = eff.DamageType,
                    DMGTaken = takenSnapshots[index],
                    Source = EnemyDamageSource(sourceStatus, skillID, eff.SkillEffectID2),
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
            case "Physical": return target.PhysicalRes + bonus;
            default: return 0f;
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
        HealthPhases = EnemyHealthPhases.Single;
        _currentPatternID = 0;
        _currentRules.Clear();
        _skillCooldownLeft.Clear();
        _skillUsedThisTurn.Clear();
        _weightedCandidates.Clear();
        _cooldownKeyBuffer.Clear();
        _currentSkillID2 = string.Empty;
        _lastEnemyTargetResult = null;
    }
}
