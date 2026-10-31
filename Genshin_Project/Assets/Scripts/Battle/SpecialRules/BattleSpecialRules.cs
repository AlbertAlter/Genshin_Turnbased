using System;
using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>特殊状态伤害仍使用原效果、状态来源、选定目标与效果执行编号。</summary>
public sealed class StatusDamageExecution
{
    public StatusEffectData Effect;
    public StatusInstance Status;
    public object Host;
    // 包装层只解析一次；ExecuteDefault直接消费此列表。逐跳处理器可在每段前明确替换。
    public List<BattleEntity> Targets;
    public long ExecutionID;
    // 专用处理器只确定元素/数据；继续委托原结算入口，保留暴击、护盾、反应与声明顺序。
    public Func<StatusEffectData, Task> ExecuteDefault;
}

/// <summary>特殊治疗只提供已确认的目标、基础值和可选最终上限，继续复用普通Heal结算与声明。</summary>
public sealed class StatusHealExecution
{
    public StatusEffectData Effect;
    public StatusInstance Status;
    public object Host;
    public Func<List<BattleEntity>, string, float?, Task> ExecuteDefault;
}

/// <summary>
/// 非元素反应特例的统一接入点。这里只登记处理器，不保存战斗实例、不另建事件队列。
/// 通用流程只调用本入口；新增角色在自己的脚本中登记能力，再加入静态注册列表。
/// 各回调仍在原调用点同步执行，技能作用域由 using 释放，沿用原有事件树与 ApplyOrder。
/// </summary>
public static class BattleSpecialRules
{
    private sealed class DamageRule
    {
        public Action<string, BattleEntity, string> Validate;
        public Func<string, BattleEntity, BattleEntity, HitData, int, HitData> Modify;
    }

    private static readonly Dictionary<string, Func<string, ScriptHookContext, bool>> Conditions
        = new Dictionary<string, Func<string, ScriptHookContext, bool>>(StringComparer.Ordinal);
    private static readonly Dictionary<string, Func<BattleEntity, int>> Counts
        = new Dictionary<string, Func<BattleEntity, int>>(StringComparer.Ordinal);
    private static readonly Dictionary<string, DamageRule> DamageRules
        = new Dictionary<string, DamageRule>(StringComparer.Ordinal);
    private static readonly Dictionary<string, Func<StatusEffectData, StatusInstance, float>> BuffScalarRules
        = new Dictionary<string, Func<StatusEffectData, StatusInstance, float>>(StringComparer.Ordinal);
    private static readonly Dictionary<string, Func<BattleManager, BattleEntity, List<int>>> Targets
        = new Dictionary<string, Func<BattleManager, BattleEntity, List<int>>>(StringComparer.Ordinal);
    private static readonly List<Func<BattleEntity, IDisposable>> SkillScopes
        = new List<Func<BattleEntity, IDisposable>>();
    private static readonly List<Func<BattleEntity, SkillMainData, IDisposable>> SkillExecutionScopes
        = new List<Func<BattleEntity, SkillMainData, IDisposable>>();
    private static readonly List<Action<BattleEntity, SkillMainData, BattleDeclaration>> SkillSettled
        = new List<Action<BattleEntity, SkillMainData, BattleDeclaration>>();
    private static readonly List<Func<BattleEntity, bool>> EnemyActionBlockers
        = new List<Func<BattleEntity, bool>>();
    private static readonly List<Func<BattleEntity, float, float>> HPDamageGuards
        = new List<Func<BattleEntity, float, float>>();

    // 护盾结算后、扣HP和死亡声明前。处理器只能减少本次扣血，不执行伤害或治疗。
    internal static void RegisterHPDamageGuard(Func<BattleEntity, float, float> guard) => HPDamageGuards.Add(guard);
    internal static float GuardHPDamage(BattleEntity target, float amount)
    {
        foreach (var guard in HPDamageGuards)
        {
            float guarded = guard(target, amount);
            if (float.IsNaN(guarded) || float.IsInfinity(guarded) || guarded < 0f || guarded > amount)
                throw new InvalidOperationException("扣血保护只能返回0至本次原扣血量之间的有限数值。");
            amount = guarded;
        }
        return amount;
    }
    private static readonly List<Func<SkillEffectData, bool>> AutomaticSkillTargetRules
        = new List<Func<SkillEffectData, bool>>();
    private static readonly Dictionary<string, Action<StatusInstance, object>> StatusCreated
        = new Dictionary<string, Action<StatusInstance, object>>(StringComparer.Ordinal);
    private static readonly Dictionary<string, Action<StatusInstance, object>> StatusRemoved
        = new Dictionary<string, Action<StatusInstance, object>>(StringComparer.Ordinal);
    private static readonly Dictionary<string, Func<StatusDamageExecution, Task>> StatusDamageRules
        = new Dictionary<string, Func<StatusDamageExecution, Task>>(StringComparer.Ordinal);
    private static readonly Dictionary<string, Func<StatusEffectData, StatusInstance, object, float>> FixedStatusAmounts
        = new Dictionary<string, Func<StatusEffectData, StatusInstance, object, float>>(StringComparer.Ordinal);
    private static readonly Dictionary<string, Func<StatusHealExecution, Task>> StatusHealRules
        = new Dictionary<string, Func<StatusHealExecution, Task>>(StringComparer.Ordinal);
    private static readonly Dictionary<string, Func<BattleEntity, float, float>> ShieldBases
        = new Dictionary<string, Func<BattleEntity, float, float>>(StringComparer.Ordinal);
    private static readonly Dictionary<string, Func<StatusActionData, string>> ActionCounterGroups
        = new Dictionary<string, Func<StatusActionData, string>>(StringComparer.Ordinal);
    private static readonly List<Action<BattleManager>> BattleReadyRules = new List<Action<BattleManager>>();
    private static readonly List<Action<BattleEntity>> EntityResetRules = new List<Action<BattleEntity>>();
    private static readonly List<Func<long, BattleEntity, IEnumerable<BattleEntity>, IDisposable>> HitScopes
        = new List<Func<long, BattleEntity, IEnumerable<BattleEntity>, IDisposable>>();
    private static readonly List<Action<BattleEntity, string, string, StatusInstance, ReactionResult>> AnemoResults
        = new List<Action<BattleEntity, string, string, StatusInstance, ReactionResult>>();
    private static readonly Dictionary<string, Func<StatusInstance, object, CountDamageContext, CountDamageContribution>> DamagePreparations
        = new Dictionary<string, Func<StatusInstance, object, CountDamageContext, CountDamageContribution>>(StringComparer.Ordinal);
    private static readonly Dictionary<string, HashSet<int>> PreparedBaseDamageEffects
        = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
    private static readonly HashSet<string> AtomicStatusActions = new HashSet<string>(StringComparer.Ordinal);
    private static readonly List<Func<StatusInstance, IDisposable>> StatusActionScopes
        = new List<Func<StatusInstance, IDisposable>>();
    private static readonly List<Action<BattleDeclaration>> DeclarationObservers = new List<Action<BattleDeclaration>>();
    private static readonly List<Func<BattleEntity, string, long, IDisposable>> DamageEffectScopes
        = new List<Func<BattleEntity, string, long, IDisposable>>();
    private static readonly List<Action<BattleEntity, string>> DamageEffectCompleted = new List<Action<BattleEntity, string>>();
    private static readonly List<Func<BattleEntity, string, long, IDisposable>> EffectInvocationScopes
        = new List<Func<BattleEntity, string, long, IDisposable>>();
    private static readonly List<Func<BattleEntity, SkillMainData, int, int>> CastCooldownModifiers
        = new List<Func<BattleEntity, SkillMainData, int, int>>();

    /// <summary>只修正一次真实施放新开启的正数冷却；已有计时及自动充能续轮不经过此入口。</summary>
    internal static void RegisterCastCooldownModifier(Func<BattleEntity, SkillMainData, int, int> modify)
        => CastCooldownModifiers.Add(modify);
    public static int ResolveCastCooldown(BattleEntity actor, SkillMainData skill, int rounds)
    {
        foreach (var modify in CastCooldownModifiers) rounds = modify(actor, skill, rounds);
        if (rounds <= 0) throw new InvalidOperationException("施放冷却修正必须保留正数冷却；零冷却的充能恢复尚未定义。");
        return rounds;
    }

    /// <summary>只登记无需玩家再次确认的自动后续目标；判定必须纯读配表，不抽随机、不执行效果。</summary>
    internal static void RegisterAutomaticSkillTargets(Func<SkillEffectData, bool> matches)
        => AutomaticSkillTargetRules.Add(matches);

    public static bool UsesAutomaticSkillTargets(SkillEffectData effect)
    {
        if (effect == null) return false;
        foreach (var matches in AutomaticSkillTargetRules)
            if (matches(effect)) return true;
        return false;
    }

    internal static void RegisterEffectInvocationScope(Func<BattleEntity, string, long, IDisposable> begin)
        => EffectInvocationScopes.Add(begin);
    /// <summary>一条实际效果及其全部ExecuteCount重复；不同于每次重复内部的Damage作用域。</summary>
    public static IDisposable BeginEffectInvocation(BattleEntity owner, string effect, long execution)
    {
        var scopes = new ScopeGroup();
        try { foreach (var begin in EffectInvocationScopes) scopes.Items.Add(begin(owner, effect, execution)); return scopes; }
        catch { scopes.Dispose(); throw; }
    }

    internal static void RegisterDamageEffectScope(Func<BattleEntity, string, long, IDisposable> begin,
        Action<BattleEntity, string> complete)
    { DamageEffectScopes.Add(begin); DamageEffectCompleted.Add(complete); }

    public static IDisposable BeginDamageEffect(BattleEntity owner, string effect, long execution)
    {
        var scopes = new ScopeGroup();
        try { foreach (var begin in DamageEffectScopes) scopes.Items.Add(begin(owner, effect, execution)); return scopes; }
        catch { scopes.Dispose(); throw; }
    }

    /// <summary>借用原完整Damage结束收集边界，普通Post规则不变；未经过该边界的状态伤害在其外层scope结束时收集。</summary>
    public static void CompleteDamageEffect(BattleEntity owner, string effect)
    { foreach (var complete in DamageEffectCompleted) complete(owner, effect); }

    static BattleSpecialRules()
    {
        SpecialSkillDamageCalculator.Register();
        StellarTeamBonusHandler.Register();
        LunarCrystallizeReactionHandler.Register();
        AirborneStatusHandler.Register();
        PublicControlStates.Register();
        Enemy2001To2010Hooks.Register();
        Enemy2011To2020Hooks.Register();
        Enemy2021To2030Hooks.Register();
        Enemy2031To2040Hooks.Register();
        Enemy2041To2050Hooks.Register();
        LisaScriptHooks.Register();
        VentiScriptHooks.Register();
        JeanScriptHooks.Register();
        BarbaraScriptHooks.Register();
        DilucScriptHooks.Register();
        KleeScriptHooks.Register();
        NingguangScriptHooks.Register();
        BeidouScriptHooks.Register();
        RazorScriptHooks.Register();
        BennettScriptHooks.Register();
        NoelleScriptHooks.Register();
        XianglingScriptHooks.Register();
        XingqiuScriptHooks.Register();
        FischlScriptHooks.Register();
        ChongyunScriptHooks.Register();
        SucroseScriptHooks.Register();
        QiqiScriptHooks.Register();
        MonaScriptHooks.Register();
        KeqingScriptHooks.Register();
        TartagliaScriptHooks.Register();
        DionaScriptHooks.Register();
        ZhongliC6Healing.Register();
        ZhongliScriptHooks.Register();
        XinyanScriptHooks.Register();
        AlbedoScriptHooks.Register();
        GanyuScriptHooks.Register();
        XiaoScriptHooks.Register();
        HuTaoScriptHooks.Register();
        RosariaScriptHooks.Register();
        YanfeiScriptHooks.Register();
        EulaScriptHooks.Register();
        KaeyaScriptHooks.Register();
        WeaponHooks.Register();
        ArtifactHooks.RegisterSpecialRules();
    }

    internal static void RegisterStatusLifecycle(string statusID,
        Action<StatusInstance, object> created, Action<StatusInstance, object> removed)
    {
        if (created != null) StatusCreated.Add(statusID, created);
        if (removed != null) StatusRemoved.Add(statusID, removed);
    }

    internal static void RegisterActionCounterGroup(string statusID, Func<StatusActionData, string> group)
        => ActionCounterGroups.Add(statusID, group);

    /// <summary>仅登记明确要求整组提交的行动；不改变其他状态效果之间原有的响应边界。</summary>
    internal static void RegisterAtomicStatusAction(string statusID) => AtomicStatusActions.Add(statusID);
    internal static void RegisterStatusActionScope(Func<StatusInstance, IDisposable> begin)
        => StatusActionScopes.Add(begin);
    public static IDisposable BeginStatusAction(StatusInstance status)
    {
        var scopes = new ScopeGroup();
        try
        {
            if (status != null && AtomicStatusActions.Contains(status.StatusID2))
                scopes.Items.Add(BattleResolutionContext.BeginCollection());
            foreach (var begin in StatusActionScopes) scopes.Items.Add(begin(status));
            return scopes;
        }
        catch { scopes.Dispose(); throw; }
    }

    public static string GetActionCounterKey(StatusInstance status, StatusActionData action, string fallback)
    {
        if (status == null || !ActionCounterGroups.TryGetValue(status.StatusID2, out var group)) return fallback;
        string key = group(action);
        return string.IsNullOrEmpty(key) ? fallback : "Shared:" + key;
    }

    internal static void RegisterBattleReady(Action<BattleManager> ready) => BattleReadyRules.Add(ready);

    internal static void RegisterEntityReset(Action<BattleEntity> reset) => EntityResetRules.Add(reset);

    /// <summary>重置清场，不是战斗内状态移除或资源消耗；只撤回处理器自己的已应用数值。</summary>
    public static void OnEntityReset(BattleEntity entity)
    {
        foreach (var reset in EntityResetRules) reset(entity);
    }

    /// <summary>完整编队与队伍固有元素已确定，首个阶段尚未开始；不是战斗行动或资源获得声明。</summary>
    public static void OnBattleReady(BattleManager battle)
    {
        foreach (var ready in BattleReadyRules) ready(battle);
    }

    internal static void RegisterHitScope(Func<long, BattleEntity, IEnumerable<BattleEntity>, IDisposable> begin)
        => HitScopes.Add(begin);

    /// <summary>仅索引已经提交的事实；不得在记录边界执行效果、改资源或发布新声明。</summary>
    internal static void RegisterDeclarationObserver(Action<BattleDeclaration> observe) => DeclarationObservers.Add(observe);
    internal static void ObserveDeclaration(BattleDeclaration declaration)
    {
        foreach (var observe in DeclarationObservers) observe(declaration);
    }

    internal static void RegisterAnemoResult(Action<BattleEntity, string, string, StatusInstance, ReactionResult> record)
        => AnemoResults.Add(record);

    /// <summary>本逻辑hit的全部扩散已真实结算，只索引既有事实，不发布第二份声明或直接触发状态行动。</summary>
    public static void OnAnemoResult(BattleEntity source, string effectID, string hook,
        StatusInstance status, ReactionResult result)
    {
        foreach (var record in AnemoResults) record(source, effectID, hook, status, result);
    }

    /// <summary>在本逻辑hit全部目标计算前采集事实；子hit按身份隔离，不预抽暴击或冻结面板。</summary>
    public static IDisposable BeginHit(long hitID, BattleEntity source, IEnumerable<BattleEntity> targets)
    {
        var scopes = new ScopeGroup();
        try
        {
            scopes.Items.Add(BattleEventHub.BeginHit(hitID));
            foreach (var begin in HitScopes) scopes.Items.Add(begin(hitID, source, targets));
            return scopes;
        }
        catch { scopes.Dispose(); throw; }
    }

    public static void OnStatusCreated(StatusInstance status, object host)
    {
        using var formulaIsolation = StatusFormulaSnapshot.Suspend();
        HexereiStatusEnhancement.Capture(status);
        StatusFormulaSnapshot.Capture(status);
        if (status != null && StatusCreated.TryGetValue(status.StatusID2, out var created)) created(status, host);
    }

    public static void OnStatusRemoved(StatusInstance status, object host)
    {
        if (status != null && StatusRemoved.TryGetValue(status.StatusID2, out var removed)) removed(status, host);
    }

    internal static void RegisterNumberedCondition(string id, Func<string, ScriptHookContext, bool> evaluate)
        => RegisterCondition("Hook:" + id, evaluate);

    internal static void RegisterStatusDamage(string hook, Func<StatusDamageExecution, Task> execute)
        => StatusDamageRules.Add(hook, execute);

    // True状态专用数值钩子只返回本段原始扣血量，仍经过TrueDamageExecutor/HitBatch及原声明流程。
    internal static void RegisterFixedStatusAmount(string hook, Func<StatusEffectData, StatusInstance, object, float> resolve)
        => FixedStatusAmounts.Add(hook, resolve);
    public static bool HasFixedStatusAmount(string hook)
        => hook != null && FixedStatusAmounts.ContainsKey(hook.Trim());
    public static float ResolveFixedStatusAmount(StatusEffectData effect, StatusInstance status, object host)
        => FixedStatusAmounts[effect.DamageHook.Trim()](effect, status, host);

    internal static void RegisterStatusHeal(string hook, Func<StatusHealExecution, Task> execute)
        => StatusHealRules.Add(hook, execute);
    // 特定盾的基础公式修正，发生在通用ShieldBonus/ShieldBonusFlat之前，不改变已生成的盾。
    internal static void RegisterShieldBase(string effectID, Func<BattleEntity, float, float> resolve)
        => ShieldBases.Add(effectID, resolve);
    public static float ResolveShieldBase(string effectID, BattleEntity source, float basis)
        => effectID != null && ShieldBases.TryGetValue(effectID, out var resolve) ? resolve(source, basis) : basis;
    public static bool HasStatusHealHandler(string hook)
        => hook != null && StatusHealRules.ContainsKey(hook.Trim());
    public static Task ExecuteStatusHeal(StatusHealExecution context)
    {
        if (context?.Effect?.EffectType != "Heal")
            throw new InvalidOperationException("专用状态治疗处理器只能执行Heal效果。");
        return StatusHealRules[context.Effect.DamageHook.Trim()](context);
    }

    // 少量普通Buff也需要正式hit的预备/取消/提交；复用同一贡献管线，不把它们伪装成Count状态。
    internal static void RegisterDamagePreparation(string statusID,
        Func<StatusInstance, object, CountDamageContext, CountDamageContribution> prepare)
        => DamagePreparations.Add(statusID, prepare);
    internal static bool HasDamagePreparation(string statusID)
        => statusID != null && DamagePreparations.ContainsKey(statusID);
    /// <summary>明确由命中预备管线独占的基础加算Buff；不能再由普通/特殊反应属性汇总读取同一效果。</summary>
    internal static void RegisterPreparedBaseDamageEffect(string statusID, int effectID)
    {
        if (!PreparedBaseDamageEffects.TryGetValue(statusID, out var effects))
            PreparedBaseDamageEffects.Add(statusID, effects = new HashSet<int>());
        if (!effects.Add(effectID)) throw new InvalidOperationException("重复登记基础贡献效果：" + effectID);
    }
    internal static bool IsPreparedBaseDamageEffect(StatusInstance status, StatusEffectData effect)
        => status?.StatusID2 != null && effect != null
            && PreparedBaseDamageEffects.TryGetValue(status.StatusID2, out var effects) && effects.Contains(effect.StatusEffectID);
    internal static CountDamageContribution PrepareDamage(StatusInstance status, object host, CountDamageContext context)
        => DamagePreparations[status.StatusID2](status, host, context);

    public static bool HasStatusDamageHandler(string hook)
        => hook != null && StatusDamageRules.ContainsKey(hook.Trim());

    public static Task ExecuteStatusDamage(StatusDamageExecution context)
    {
        if (context?.Effect == null || context.Effect.EffectType != "Damage")
            throw new InvalidOperationException("专用状态伤害处理器只能执行Damage效果。");
        return StatusDamageRules[context.Effect.DamageHook.Trim()](context);
    }

    internal static void RegisterCondition(string name, Func<string, ScriptHookContext, bool> evaluate)
        => Conditions.Add(name, evaluate);
    internal static void RegisterCount(string name, Func<BattleEntity, int> evaluate)
        => Counts.Add(name, evaluate);
    internal static void RegisterTargets(string hook, Func<BattleManager, BattleEntity, List<int>> resolve)
        => Targets.Add(hook, resolve);
    internal static void RegisterSkillScope(Func<BattleEntity, IDisposable> begin)
        => SkillScopes.Add(begin);
    internal static void RegisterSkillExecutionScope(Func<BattleEntity, SkillMainData, IDisposable> begin)
        => SkillExecutionScopes.Add(begin);
    internal static void RegisterSkillSettled(Action<BattleEntity, SkillMainData, BattleDeclaration> settled)
        => SkillSettled.Add(settled);
    /// <summary>
    /// 真实我方行动全部效果及ActionCompleted的响应结清之后调用，仍在技能作用域内。
    /// 不伪造第二次行为声明；回调提交的状态行动继续由原执行队列清算。
    /// 取消、异常、非主动技能容器不调用；嵌套行动是否等待外层由处理器按作用域判断。
    /// </summary>
    internal static void CompleteSkillSettlement(BattleEntity owner, SkillMainData skill, BattleDeclaration declaration)
    { foreach (var settled in SkillSettled) settled(owner, skill, declaration); }
    internal static void RegisterEnemyActionBlocker(Func<BattleEntity, bool> blocks)
        => EnemyActionBlockers.Add(blocks);
    internal static void RegisterDamage(string hook, Action<string, BattleEntity, string> validate,
        Func<string, BattleEntity, BattleEntity, HitData, int, HitData> modify)
        => DamageRules.Add(hook, new DamageRule { Validate = validate, Modify = modify });

    /// <summary>被动Buff的只读参数覆盖，不是额外伤害效果，不得在查询中执行状态行动。</summary>
    internal static void RegisterBuffScalar(string hook, Func<StatusEffectData, StatusInstance, float> resolve)
        => BuffScalarRules.Add(hook, resolve);
    public static bool TryResolveBuffScalar(StatusEffectData effect, StatusInstance status, out float value)
    {
        value = 0f;
        if (effect == null || (effect.EffectType != "Buff" && effect.EffectType != "Debuff")
            || string.IsNullOrWhiteSpace(effect.DamageHook)
            || !BuffScalarRules.TryGetValue(effect.DamageHook.Trim(), out var resolve)) return false;
        value = resolve(effect, status);
        if (float.IsNaN(value) || float.IsInfinity(value)) throw new InvalidOperationException("Buff参数必须为有限值：" + effect.DamageHook);
        return true;
    }

    public static bool TryEvaluateCondition(string name, string argument, ScriptHookContext context, out bool result)
    {
        result = false;
        if (name == null || !Conditions.TryGetValue(name, out var evaluate)) return false;
        result = evaluate(argument, context);
        return true; // 已识别但返回 false，不得当成未知钩子再求值。
    }

    public static bool HasCountHandler(string name) => name != null && Counts.ContainsKey(name);

    public static int EvaluateCount(string name, BattleEntity caster)
    {
        if (name == null || !Counts.TryGetValue(name, out var evaluate))
            throw new NotSupportedException($"ExecuteCount 尚未实现处理器：{name}");
        return evaluate(caster);
    }

    public static bool TryResolveTargets(string hook, BattleManager battle, BattleEntity caster, out List<int> positions)
    {
        positions = null;
        if (hook == null || !Targets.TryGetValue(hook, out var resolve)) return false;
        positions = resolve(battle, caster);
        return true; // 已识别且目标为空时仍不得回退为普通状态 ID 查询。
    }

    public static void ValidateDamageHook(string hook, BattleEntity caster, string effectType)
    {
        if (string.IsNullOrWhiteSpace(hook)) return;
        if (effectType == "Heal" && HasStatusHealHandler(hook)) return;
        if ((effectType == "Buff" || effectType == "Debuff") && BuffScalarRules.ContainsKey(hook.Trim())) return;
        GetDamageRule(hook).Validate(hook, caster, effectType);
    }

    public static HitData ModifyHit(string hook, BattleEntity caster, BattleEntity target, HitData hit, int skillLevel)
        => string.IsNullOrWhiteSpace(hook) ? hit : GetDamageRule(hook).Modify(hook, caster, target, hit, skillLevel);

    private static readonly HashSet<string> PerTargetStatusHitModifiers = new HashSet<string>(StringComparer.Ordinal);
    internal static void RegisterStatusHitModifier(string hook) => PerTargetStatusHitModifiers.Add(hook);
    public static HitData ModifyStatusHit(string hook, BattleEntity caster, BattleEntity target, HitData hit, int skillLevel)
        => hook != null && PerTargetStatusHitModifiers.Contains(hook.Trim()) ? ModifyHit(hook, caster, target, hit, skillLevel) : hit;

    private static DamageRule GetDamageRule(string hook)
    {
        if (!DamageRules.TryGetValue(hook.Trim(), out var rule))
            throw new NotSupportedException($"未支持的伤害钩子：{hook}");
        return rule;
    }

    public static bool BlocksEnemyAction(BattleEntity entity)
    {
        if (entity == null) return false;
        foreach (var blocks in EnemyActionBlockers)
            if (blocks(entity)) return true;
        return false;
    }

    public static IDisposable BeginSkill(BattleEntity caster)
    {
        var scopes = new ScopeGroup();
        try
        {
            // 所有子技能都创建作用域，包括非该角色的嵌套技能，以隔离调用方记录。
            foreach (var begin in SkillScopes) scopes.Items.Add(begin(caster));
            return scopes;
        }
        catch
        {
            scopes.Dispose();
            throw;
        }
    }

    /// <summary>已通过实际技能准备的完整调用；涵盖全部效果及取消/异常清理，不是单hit或行动次数额度。</summary>
    public static IDisposable BeginSkillExecution(BattleEntity caster, SkillMainData skill)
    {
        var scopes = new ScopeGroup();
        try
        {
            foreach (var begin in SkillExecutionScopes) scopes.Items.Add(begin(caster, skill));
            return scopes;
        }
        catch { scopes.Dispose(); throw; }
    }

    private sealed class ScopeGroup : IDisposable
    {
        public readonly List<IDisposable> Items = new List<IDisposable>();

        public void Dispose()
        {
            if (Items.Count == 0) return;
            int last = Items.Count - 1;
            var scope = Items[last];
            Items.RemoveAt(last);
            try { scope?.Dispose(); }
            finally { Dispose(); }
        }
    }
}
