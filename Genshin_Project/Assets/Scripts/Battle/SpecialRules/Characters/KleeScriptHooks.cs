using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

/// <summary>
/// 可莉只在此索引本次技能/逻辑hit的真实结果。概率在配表Chance中抽取，伤害/赋予/回能仍走公共执行器。
/// 多目标先提交整批事实，再由第一条符合资格的事实请求一次响应；嵌套hit不会覆盖父hit。
/// </summary>
public static class KleeScriptHooks
{
    public const string C1Damage = "STE_Klee_C1Damage";
    private sealed class SkillFrame : IDisposable
    {
        internal readonly SkillFrame Previous = Skill.Value;
        internal readonly BattleEntity Owner;
        internal long FirstSkillHit;
        internal string FirstSkillEffect;
        internal SkillFrame(BattleEntity owner) { Owner = owner; Skill.Value = this; }
        public void Dispose() => Skill.Value = Previous;
    }
    private sealed class HitFrame : IDisposable
    {
        internal readonly HitFrame Previous = Hit.Value;
        internal readonly BattleEntity Owner;
        internal readonly long ID;
        internal long C1, Spark, Critical;
        internal readonly HashSet<BattleEntity> Targets = new HashSet<BattleEntity>();
        internal HitFrame(long id, BattleEntity owner) { ID = id; Owner = owner; Hit.Value = this; }
        public void Dispose() => Hit.Value = Previous;
    }
    private sealed class HeavyFrame : IDisposable
    {
        internal readonly HeavyFrame Previous = Heavy.Value;
        internal readonly BattleEntity Owner;
        internal readonly string SkillID;
        internal readonly bool UsedSpark;
        internal HeavyFrame(BattleEntity owner, SkillMainData skill)
        {
            Owner = owner; SkillID = skill?.SkillID2;
            Heavy.Value = this;
            // PrepareSkillExecution已成功完成费用/能量/冷却检查。失败施放绝不消费火花。
            // 在付款响应Flush及实际伤害之前消费，保存本次资格，不在属性getter中改Count。
            try
            {
                if (owner?.EntityID == 1016 && skill?.ActionType == "Heavy")
                {
                    var spark = owner.GetStatus(KleeSparkCount.ID);
                    if (spark?.IsActive == true && spark.RuntimeIntValue > 0)
                    {
                        UsedSpark = true;
                        KleeSparkCount.Change(spark, owner, -1, ResourceChangeReason.Spend);
                    }
                }
            }
            catch { Heavy.Value = Previous; throw; }
        }
        public void Dispose() => Heavy.Value = Previous;
    }
    private sealed class ResponseFrame : IDisposable
    {
        internal readonly ResponseFrame Previous = Response.Value;
        internal readonly StatusInstance Status;
        internal bool C1Landed;
        internal ResponseFrame(StatusInstance status) { Status = status; Response.Value = this; }
        public void Dispose() => Response.Value = Previous;
    }
    private static readonly AsyncLocal<SkillFrame> Skill = new AsyncLocal<SkillFrame>();
    private static readonly AsyncLocal<HitFrame> Hit = new AsyncLocal<HitFrame>();
    private static readonly AsyncLocal<HeavyFrame> Heavy = new AsyncLocal<HeavyFrame>();
    private static readonly AsyncLocal<ResponseFrame> Response = new AsyncLocal<ResponseFrame>();
    private static readonly ConditionalWeakTable<BattleDeclaration, HitFrame> HitFacts = new ConditionalWeakTable<BattleDeclaration, HitFrame>();

    internal static void Register()
    {
        // 首次投掷手选；后两跳仍按原Consecutive=2随机，只是不再进入玩家预选列表。
        BattleSpecialRules.RegisterAutomaticSkillTargets(effect => effect.EffectType == "Damage"
            && effect.TargetType == "Enemy" && effect.TargetConsecutive == 2
            && (effect.SkillEffectID2 == "SE_Skill_Klee2" || effect.SkillEffectID2 == "SE_Skill_Klee3"));
        BattleSpecialRules.RegisterSkillScope(owner => new SkillFrame(owner));
        BattleSpecialRules.RegisterSkillExecutionScope((owner, skill) => new HeavyFrame(owner, skill));
        BattleSpecialRules.RegisterHitScope((id, owner, targets) => new HitFrame(id, owner));
        BattleSpecialRules.RegisterStatusActionScope(status => new ResponseFrame(status));
        BattleSpecialRules.RegisterDeclarationObserver(Index);
        BattleSpecialRules.RegisterCondition("Klee_C1Hit", (arg, c) => Empty(arg) && Matches(c, Facts(c)?.C1 ?? 0));
        BattleSpecialRules.RegisterCondition("Klee_SparkHit", (arg, c) => Empty(arg) && Matches(c, Facts(c)?.Spark ?? 0));
        BattleSpecialRules.RegisterCondition("Klee_HeavyCritical", (arg, c) => Empty(arg) && Matches(c, Facts(c)?.Critical ?? 0));
        BattleSpecialRules.RegisterCondition("Klee_SkillEnergy", (arg, c) => (arg == "1" || arg == "2" || arg == "3")
            && c?.Caster?.EntityID == 1016 && Skill.Value?.Owner == c.Caster
            && Skill.Value.FirstSkillHit != 0 && Skill.Value.FirstSkillEffect == "SE_Skill_Klee" + arg);
        BattleSpecialRules.RegisterCondition("Klee_SparkAP", (arg, c) => Empty(arg)
            && c?.Caster?.EntityID == 1016 && c.Caster.GetStatus(KleeSparkCount.ID)?.RuntimeIntValue > 0);
        BattleSpecialRules.RegisterCondition("Klee_SparkDamage", (arg, c) => Empty(arg)
            && c?.Caster?.EntityID == 1016 && Heavy.Value?.Owner == c.Caster && Heavy.Value?.UsedSpark == true && c.DamageType == "Heavy"
            && c.DamageSkillID == Heavy.Value.SkillID);
        BattleSpecialRules.RegisterCondition("Klee_C1Committed", (arg, c) => Empty(arg)
            && Response.Value?.Status == c?.Status && Response.Value?.C1Landed == true);
        BattleSpecialRules.RegisterTargets("Klee_HitTarget()", (battle, owner) =>
        {
            var target = ScriptHookContext.Current?.DamageEvent?.Target;
            return Valid(battle, owner, target) ? new List<int> { target.SlotPosition } : new List<int>();
        });
        BattleSpecialRules.RegisterTargets("Klee_HitTargets()", (battle, owner) =>
        {
            var result = new List<int>(); var frame = Facts(ScriptHookContext.Current);
            var cause = ScriptHookContext.Current?.DamageEvent?.Declaration;
            if (frame == null || owner?.EntityID != 1016 || frame.Owner != owner || cause?.Header.HitId != frame.ID) return result;
            foreach (var target in frame.Targets) if (Valid(battle, owner, target)) result.Add(target.SlotPosition);
            result.Sort(); return result; // 随机选一个交给公共TargetSelect=1/Consecutive=4，只抽一次。
        });
    }

    private static bool Empty(string argument) => string.IsNullOrEmpty(argument);
    private static HitFrame Facts(ScriptHookContext context)
        => context?.DamageEvent?.Declaration != null && HitFacts.TryGetValue(context.DamageEvent.Declaration, out var frame) ? frame : null;
    private static bool Matches(ScriptHookContext context, long first)
        => first != 0 && context?.Caster?.EntityID == 1016
            && context.DamageEvent?.Declaration?.Header.EventId == first
            && context.DamageEvent.Source?.SourceEntity == context.Caster;
    private static bool Valid(BattleManager battle, BattleEntity owner, BattleEntity target)
        => battle != null && owner?.EntityID == 1016 && target?.IsAlive == true && target.Side != owner.Side
            && battle.GetEntityByPosition(target.Side, target.SlotPosition) == target;
    private static bool Bounce(DamageSourceInfo source)
        => source.SourceKind == ReactionSourceKind.CharacterSkill && source.SourceSkillID == "SK_Skill_Klee"
            && (source.SourceEffectID == "SE_Skill_Klee1" || source.SourceEffectID == "SE_Skill_Klee2"
                || source.SourceEffectID == "SE_Skill_Klee3");

    private static void Index(BattleDeclaration declaration)
    {
        var fact = declaration.Get<DamageEventData>(); var source = fact?.Source;
        if (source?.SourceEntity?.EntityID != 1016 || !fact.HitLanded || !source.CountsAsHit
            || fact.Subject == null || fact.Subject.Side == source.SourceEntity.Side) return;
        var frame = Hit.Value;
        if (frame?.Owner == source.SourceEntity && frame.ID == declaration.Header.HitId)
        {
            HitFacts.Add(declaration, frame);
            if (source.SourceEffectID != C1Damage)
            {
                frame.Targets.Add(fact.Subject);
                if (frame.C1 == 0) frame.C1 = declaration.Header.EventId;
            }
            if (fact.ActualHPDamage > 0f && (fact.DamageType == "Normal" || Bounce(source)) && frame.Spark == 0)
                frame.Spark = declaration.Header.EventId;
            if (fact.DamageType == "Heavy" && fact.Critical?.IsCritical == true && frame.Critical == 0)
                frame.Critical = declaration.Header.EventId;
        }
        var skill = Skill.Value;
        if (skill?.Owner == source.SourceEntity && Bounce(source))
        {
            if (skill.FirstSkillHit == 0)
            { skill.FirstSkillHit = declaration.Header.EventId; skill.FirstSkillEffect = source.SourceEffectID; }
        }
        var response = Response.Value;
        if (response?.Status?.StatusID2 == "ST_Klee_C1" && response.Status.Caster == source.SourceEntity
            && source.SourceEffectID == C1Damage) response.C1Landed = true;
    }
}

/// <summary>爆裂火花：可莉本人独有整数Count，初值1，范围0～3，重施加1，无期限；0保留但不提供费用资格。</summary>
public sealed class KleeSparkCount : CountStatusHandler
{
    public const string ID = "ST_Klee_Spark";
    private static void Require(StatusInstance status, object host)
    {
        if (!(host is BattleEntity owner) || owner != status.Caster || owner.EntityID != 1016)
            throw new InvalidOperationException("爆裂火花必须施加在可莉本人。");
    }
    public override void OnCreated(StatusInstance status, object host)
    {
        Require(status, host); status.RuntimeIntValue = 1; status.IsPermanent = true;
        status.StackCount = 1;
    }
    public override StatusInstance OnReapplied(StatusInstance status, BattleEntity host, CountStatusApplication application)
    {
        Require(status, host); Change(status, host, 1, ResourceChangeReason.Gain); return status;
    }
    internal static void Change(StatusInstance status, BattleEntity owner, int delta, ResourceChangeReason reason)
    {
        int before = status.RuntimeIntValue;
        status.RuntimeIntValue = Math.Max(0, Math.Min(3, before + delta));
        if (before != status.RuntimeIntValue)
            BattleEventHub.Publish(new ResourceEventData("Count", ID, owner, before, status.RuntimeIntValue,
                delta, reason, true, actor: owner));
    }
}
