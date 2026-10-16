using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

/// <summary>丽莎表约定：引雷按目标增加倍率；T1 使用本次苍雷命中前的三层记录。</summary>
public static class LisaScriptHooks
{
    public const string DamageHook = "Lisa_Skill()";
    public const string ConductiveID = "ST_Lisa_Conductive";
    public const string TargetHook = "1011_T1_3Conductive()";
    public const string Talent2TargetHook = "ST_Lisa_T2()";
    public const string Constellation2TargetHook = "ST_Lisa_C2()";

    internal static void Register()
    {
        BattleSpecialRules.RegisterSkillScope(BeginSkill);
        BattleSpecialRules.RegisterDamage(DamageHook, ValidateDamageHook, ModifyHit);
        BattleSpecialRules.RegisterCount("1011_C1", GetC1ExecuteCount);
        BattleSpecialRules.RegisterCondition("1011_T1",
            (argument, context) => string.IsNullOrEmpty(argument) && EvaluateTalent(context));
        BattleSpecialRules.RegisterTargets(TargetHook, GetTripleTargetPositions);
        BattleSpecialRules.RegisterTargets(Talent2TargetHook,
            (battle, caster) => GetTriggerTargetPositions(battle, caster, Talent2TargetHook));
        BattleSpecialRules.RegisterTargets(Constellation2TargetHook,
            (battle, caster) => GetTriggerTargetPositions(battle, caster, Constellation2TargetHook));
    }

    public static List<int> GetTriggerTargetPositions(BattleManager battle, BattleEntity caster, string hook)
    {
        var result = new List<int>();
        ScriptHookContext context = ScriptHookContext.Current;
        DamageResolvedEvent hit = context?.DamageEvent;
        string statusID = hook == Talent2TargetHook ? "ST_Lisa_T2"
            : hook == Constellation2TargetHook ? "ST_Lisa_C2" : null;
        string effectID = hook == Talent2TargetHook ? "SE_BurstAttack_Lisa" : "SE_Heavy_Lisa1";
        BattleEntity target = hit?.Target;
        if (statusID == null || battle == null || caster == null || caster.EntityID != 1011
            || context?.Caster != caster || context.Status?.StatusID2 != statusID
            || hit == null || !hit.HitLanded || hit.Source?.SourceEntity != caster
            || hit.Source.SourceEffectID != effectID || target == null || !target.IsAlive
            || target.Side == caster.Side) return result;
        // 目标死亡或被同位置的新单位替换后，不把旧事件套到新单位身上。
        if (battle.GetEntityByPosition(target.Side, target.SlotPosition) == target)
            result.Add(target.SlotPosition);
        return result;
    }

    private sealed class SkillScope : IDisposable
    {
        public BattleEntity Caster;
        public readonly HashSet<BattleEntity> TripleTargets = new HashSet<BattleEntity>();
        public readonly HashSet<BattleEntity> SkillHitTargets = new HashSet<BattleEntity>();
        private readonly SkillScope previous;
        public SkillScope(BattleEntity caster)
        {
            previous = Current.Value;
            Caster = caster;
            Current.Value = this;
        }
        public void Dispose() => Current.Value = previous;
    }

    private static readonly AsyncLocal<SkillScope> Current = new AsyncLocal<SkillScope>();

    // 子技能拥有独立记录，结束/取消/异常后恢复调用方；不留静态跨战斗缓存。
    public static IDisposable BeginSkill(BattleEntity caster) => new SkillScope(caster);

    public static void ValidateDamageHook(string hook, BattleEntity caster, string effectType)
    {
        if (string.IsNullOrWhiteSpace(hook)) return;
        if (hook.Trim() != DamageHook || caster == null || caster.EntityID != 1011 || effectType != "Damage")
            throw new NotSupportedException($"未支持的伤害钩子或使用位置：{hook}，角色={caster?.EntityID}，类型={effectType}");
    }

    public static HitData ModifyHit(string hook, BattleEntity caster, BattleEntity target, HitData hit, int skillLevel)
    {
        if (string.IsNullOrWhiteSpace(hook)) return hit;
        ValidateDamageHook(hook, caster, "Damage");
        if (target != null && Current.Value?.Caster == caster)
            Current.Value.SkillHitTargets.Add(target);
        int stacks = GetConductiveStacks(target);
        if (stacks == 0) return hit;

        float oneStack = ReadConductiveCoefficient(skillLevel);
        hit.Multiplier += CalculateBonus(stacks, oneStack);
        if (stacks == 3 && Current.Value?.Caster == caster)
            Current.Value.TripleTargets.Add(target);
        return hit;
    }

    public static int GetConductiveStacks(BattleEntity target)
    {
        if (target == null || !target.StatusDict.TryGetValue(ConductiveID, out StatusInstance status)
            || !status.IsActive) return 0;
        return Math.Max(0, Math.Min(3, status.StackCount));
    }

    public static int CapC1Count(int hitTargetCount)
        => Math.Max(0, Math.Min(5, hitTargetCount));

    public static int GetC1ExecuteCount(BattleEntity caster)
    {
        if (caster == null || caster.EntityID != 1011 || caster.ConstellationLevel < 1
            || Current.Value?.Caster != caster) return 0;
        return CapC1Count(Current.Value.SkillHitTargets.Count);
    }

    public static float CalculateBonus(int stacks, float oneStackCoefficient)
    {
        int count = Math.Max(0, Math.Min(3, stacks));
        // 策划一级：(54*n-6)%；表中 0.48 是一层增量。高等级以该参数同比成长。
        // 一/二/三层分别是参数的 1、2.125、3.25 倍，零层不减倍率。
        return count == 0 ? 0f : oneStackCoefficient * (1.125f * count - 0.125f);
    }

    private static float ReadConductiveCoefficient(int skillLevel)
    {
        var dm = DataManager.Instance;
        if (dm == null || !dm.SkillLevelDict.TryGetValue("1011_2_" + ConductiveID, out var levels))
            throw new InvalidOperationException("Lisa_Skill 缺少 SkillLevel 参数：" + ConductiveID);
        int level = Math.Max(1, Math.Min(15, skillLevel));
        if (!levels.TryGetValue(level, out SkillLevelData data))
            throw new InvalidOperationException($"Lisa_Skill 缺少引雷参数等级 {level}");
        if (!float.TryParse(data.Hits1, NumberStyles.Float, CultureInfo.InvariantCulture, out float coefficient)
            || float.IsNaN(coefficient) || float.IsInfinity(coefficient) || coefficient < 0f)
            throw new InvalidOperationException($"Lisa_Skill 引雷倍率非法：{data.Hits1}");
        return coefficient;
    }

    public static bool EvaluateTalent(ScriptHookContext context)
    {
        BattleEntity caster = context?.Caster;
        return caster != null && caster.EntityID == 1011 && Current.Value?.Caster == caster
            && ScriptHookEvaluator.Evaluate("Check(1011_T1)", context)
            && GetTripleTargetPositions(BattleManager.Instance, caster).Count > 0;
    }

    public static List<int> GetTripleTargetPositions(BattleManager battle, BattleEntity caster)
    {
        var result = new List<int>();
        if (battle == null || caster == null || caster.EntityID != 1011 || Current.Value?.Caster != caster)
            return result;
        foreach (BattleEntity target in Current.Value.TripleTargets)
        {
            // 引雷随后已被 RemoveStatus 清除；只验证原来命中的实体仍在场、存活。
            if (target == null || !target.IsAlive || target.Side == caster.Side) continue;
            if (battle.GetEntityByPosition(target.Side, target.SlotPosition) == target)
                result.Add(target.SlotPosition);
        }
        result.Sort();
        return result;
    }
}
