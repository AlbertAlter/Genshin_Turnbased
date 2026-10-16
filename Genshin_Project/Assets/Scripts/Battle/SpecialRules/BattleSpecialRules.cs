using System;
using System.Collections.Generic;

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
    private static readonly Dictionary<string, Func<BattleManager, BattleEntity, List<int>>> Targets
        = new Dictionary<string, Func<BattleManager, BattleEntity, List<int>>>(StringComparer.Ordinal);
    private static readonly List<Func<BattleEntity, IDisposable>> SkillScopes
        = new List<Func<BattleEntity, IDisposable>>();
    private static readonly List<Func<BattleEntity, bool>> EnemyActionBlockers
        = new List<Func<BattleEntity, bool>>();

    static BattleSpecialRules()
    {
        AirborneStatusHandler.Register();
        LisaScriptHooks.Register();
        KaeyaScriptHooks.Register();
        HuskCountHandler.RegisterHooks();
        PaleFlameCountHandler.RegisterHooks();
    }

    internal static void RegisterCondition(string name, Func<string, ScriptHookContext, bool> evaluate)
        => Conditions.Add(name, evaluate);
    internal static void RegisterCount(string name, Func<BattleEntity, int> evaluate)
        => Counts.Add(name, evaluate);
    internal static void RegisterTargets(string hook, Func<BattleManager, BattleEntity, List<int>> resolve)
        => Targets.Add(hook, resolve);
    internal static void RegisterSkillScope(Func<BattleEntity, IDisposable> begin)
        => SkillScopes.Add(begin);
    internal static void RegisterEnemyActionBlocker(Func<BattleEntity, bool> blocks)
        => EnemyActionBlockers.Add(blocks);
    internal static void RegisterDamage(string hook, Action<string, BattleEntity, string> validate,
        Func<string, BattleEntity, BattleEntity, HitData, int, HitData> modify)
        => DamageRules.Add(hook, new DamageRule { Validate = validate, Modify = modify });

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
        GetDamageRule(hook).Validate(hook, caster, effectType);
    }

    public static HitData ModifyHit(string hook, BattleEntity caster, BattleEntity target, HitData hit, int skillLevel)
        => string.IsNullOrWhiteSpace(hook) ? hit : GetDamageRule(hook).Modify(hook, caster, target, hit, skillLevel);

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
