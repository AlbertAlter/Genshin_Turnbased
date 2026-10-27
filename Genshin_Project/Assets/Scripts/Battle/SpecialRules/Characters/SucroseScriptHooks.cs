using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

/// <summary>砂糖：具体风灵实例的转化记录、实时精通分享、真实命中筛选。效果执行及声明仍走公共入口。</summary>
public static class SucroseScriptHooks
{
    public const string Wind = "ST_Sucrose_Wind";
    public const string WindDamage = "STE_Sucrose_WindDamage", AbsorbedDamage = "STE_Sucrose_AbsorbedDamage";
    public const string SkillDamage = "SE_Skill_Sucrose", Share = "ST_Sucrose_EMShare", C6 = "ST_Sucrose_C6Bonus";
    public const string RecordHook = "Sucrose_WindRecord()", AbsorbedHook = "Sucrose_Absorbed()";
    private static readonly string[] Priority = { "Pyro", "Electro", "Cryo", "Hydro" };
    private static readonly AsyncLocal<HitFrame> Hitting = new AsyncLocal<HitFrame>();
    private static readonly AsyncLocal<SkillFrame> Casting = new AsyncLocal<SkillFrame>();
    private static readonly ConditionalWeakTable<BattleDeclaration, HitFrame> Facts = new ConditionalWeakTable<BattleDeclaration, HitFrame>();
    private sealed class HitFrame : IDisposable
    {
        internal readonly BattleEntity Owner;
        internal long FirstShare;
        private readonly HitFrame previous = Hitting.Value;
        internal HitFrame(BattleEntity owner) { Owner = owner; Hitting.Value = this; }
        public void Dispose() => Hitting.Value = previous;
    }
    private sealed class SkillFrame : IDisposable
    {
        internal readonly BattleEntity Owner;
        internal readonly HashSet<BattleEntity> Targets = new HashSet<BattleEntity>();
        private readonly SkillFrame previous = Casting.Value;
        internal SkillFrame(BattleEntity owner) { Owner = owner; Casting.Value = this; }
        public void Dispose() => Casting.Value = previous;
    }

    internal static void Register()
    {
        BattleSpecialRules.RegisterHitScope((_, owner, targets) => new HitFrame(owner));
        BattleSpecialRules.RegisterSkillScope(owner => new SkillFrame(owner));
        BattleSpecialRules.RegisterDeclarationObserver(Index);
        BattleSpecialRules.RegisterStatusLifecycle(Wind, (status, host) =>
        {
            if (!(host is BattleEntity owner) || owner != status.Caster || owner.EntityID != 1026)
                throw new InvalidOperationException("大型风灵必须挂在砂糖本人。");
            // 创建时尚未登记计时桶；二命只延长本次新风灵，不重施或重复初始化。
            if (owner.ConstellationLevel >= 2) status.RemainingPhaseCount++;
            status.RuntimeElement = string.Empty;
        }, null);
        BattleSpecialRules.RegisterAnemoResult(RecordAbsorption);
        BattleSpecialRules.RegisterDamage(RecordHook, (hook, owner, type) =>
        {
            if (owner?.EntityID != 1026 || type != "Damage")
                throw new NotSupportedException("Sucrose_WindRecord仅用于砂糖风灵的风伤。");
        }, (hook, owner, target, hit, level) => hit);
        BattleSpecialRules.RegisterStatusDamage(RecordHook, context =>
        {
            if (context.Effect.StatusEffectID2 != WindDamage || context.Effect.Element != "Anemo" || !ValidWind(context.Status))
                throw new InvalidOperationException("砂糖风伤缺少本次大型风灵实例。");
            return context.ExecuteDefault(context.Effect);
        });
        BattleSpecialRules.RegisterStatusDamage(AbsorbedHook, ExecuteAbsorbed);
        BattleSpecialRules.RegisterTargets("Sucrose_SwirlAllies()", SwirlAllies);
        BattleSpecialRules.RegisterTargets("Sucrose_HitMinion()", HitMinion);
        BattleSpecialRules.RegisterTargets("Sucrose_SkillMinions()", (battle, owner) =>
        {
            var result = new List<int>(); var cast = Casting.Value;
            if (cast?.Owner != owner || owner?.EntityID != 1026) return result;
            foreach (var target in cast.Targets) if (IsMinion(battle, owner, target)) result.Add(target.SlotPosition);
            result.Sort(); return result;
        });
        BattleSpecialRules.RegisterCondition("Sucrose_AirborneHit", (argument, context) => Empty(argument)
            && IsOwnHit(context?.DamageEvent, context?.Caster)
            && context.DamageEvent.Source.SourceEffectID == WindDamage);
        BattleSpecialRules.RegisterCondition("Sucrose_EMHit", (argument, context) => Empty(argument)
            && context?.DamageEvent?.Declaration != null && Facts.TryGetValue(context.DamageEvent.Declaration, out var frame)
            && frame.Owner == context.Caster && frame.FirstShare == context.DamageEvent.Declaration.Header.EventId);
        BattleSpecialRules.RegisterCondition("Sucrose_C6Element", (argument, context) => Empty(argument)
            && FindWind(context?.Status)?.RuntimeElement == context?.DamageElement
            && !string.IsNullOrEmpty(context?.DamageElement));
        BattleSpecialRules.RegisterBuffScalar("Sucrose_EMShare()", (effect, status) =>
        {
            if (status?.StatusID2 != Share || status.Caster?.EntityID != 1026)
                throw new InvalidOperationException("砂糖精通分享必须保留砂糖施加者。");
            // 只在此专用参数入口开放TotalEM；每次查询读来源砂糖，不读受益者，也不写回状态。
            return BuffScalarParser.Evaluate(effect.Param1, readAttribute: attribute => attribute == "TotalEM"
                ? status.Caster.GetFinalEM() : throw new FormatException("砂糖精通分享仅支持来源TotalEM：" + attribute));
        });
    }

    private static bool Empty(string value) => string.IsNullOrEmpty(value);
    private static bool ValidWind(StatusInstance status) => status?.StatusID2 == Wind && status.IsActive
        && status.Caster?.IsAlive == true && status.Caster.EntityID == 1026 && status.Caster.HasStatusInstance(status);
    private static StatusInstance FindWind(StatusInstance child)
    {
        if (child?.IsActive != true || child.Caster?.EntityID != 1026 || !child.Caster.IsAlive) return null;
        foreach (var root in child.Caster.GetStatusInstances(Wind))
            if (ValidWind(root) && root.BoundStatuses.Contains(child)) return root;
        return null;
    }
    public static string GetAbsorbedElement(StatusInstance status)
        => ValidWind(status) && !string.IsNullOrEmpty(status.RuntimeElement) ? status.RuntimeElement : null;

    private static bool IsOwnHit(DamageResolvedEvent hit, BattleEntity owner) => owner?.EntityID == 1026
        && hit?.HitLanded == true && hit.Target != null && hit.Target.Side != owner.Side
        && hit.Source?.SourceEntity == owner && hit.Source.CountsAsHit;
    private static bool IsShareEffect(string effect) => effect == SkillDamage || effect == WindDamage || effect == AbsorbedDamage;
    private static void Index(BattleDeclaration declaration)
    {
        var frame = Hitting.Value;
        if (frame?.Owner?.EntityID != 1026 || declaration?.Kind != BattleEventKind.DamageResolved) return;
        var hit = declaration.Get<DamageEventData>();
        if (hit?.HitLanded != true || hit.Subject?.Side == frame.Owner.Side || hit.Source?.SourceEntity != frame.Owner
            || !hit.Source.CountsAsHit || !IsShareEffect(hit.Source.SourceEffectID)) return;
        if (hit.Source.SourceEffectID == SkillDamage && Casting.Value?.Owner == frame.Owner)
            Casting.Value.Targets.Add(hit.Subject);
        if (frame.FirstShare == 0) frame.FirstShare = declaration.Header.EventId;
        Facts.Add(declaration, frame);
    }
    private static List<int> SwirlAllies(BattleManager battle, BattleEntity owner)
    {
        var result = new List<int>(); var reaction = ScriptHookContext.Current?.ReactionEvent;
        if (battle == null || owner?.EntityID != 1026 || reaction?.SourceEntity != owner || reaction.Type != ReactionType.Swirl)
            return result;
        foreach (var ally in battle.Allies)
        {
            var member = ally?.Entity;
            if (member?.IsAlive == true && member.Side == owner.Side
                && DataManager.Instance.CharacterAttributesDict.TryGetValue(member.EntityID, out var identity)
                && Array.IndexOf(Priority, identity.Element) >= 0
                && reaction.InvolvedElements.Contains(identity.Element)) result.Add(member.SlotPosition);
        }
        result.Sort(); return result;
    }
    private static List<int> HitMinion(BattleManager battle, BattleEntity owner)
    {
        var hit = ScriptHookContext.Current?.DamageEvent; var target = hit?.Target;
        return IsOwnHit(hit, owner) && IsMinion(battle, owner, target)
            ? new List<int> { target.SlotPosition } : new List<int>();
    }
    private static bool IsMinion(BattleManager battle, BattleEntity owner, BattleEntity target)
        => target?.IsAlive == true && target.Side != owner?.Side
            && battle?.GetEntityByPosition(target.Side, target.SlotPosition) == target
            && DataManager.Instance.EnemyMainDict.TryGetValue(target.EntityID, out var identity)
            && string.Equals(identity.EnemyType, "Minion", StringComparison.OrdinalIgnoreCase);
    private static void RecordAbsorption(BattleEntity owner, string effect, string hook, StatusInstance status, ReactionResult result)
    {
        if (owner?.EntityID != 1026 || effect != WindDamage || hook != RecordHook || !ValidWind(status)
            || result == null || !string.IsNullOrEmpty(status.RuntimeElement)) return;
        foreach (string element in Priority)
            foreach (var reaction in result.TriggeredReactions)
                if (reaction.Type == ReactionType.Swirl && reaction.Declaration != null && reaction.SourceEntity == owner
                    && reaction.SourceEffectID == effect && reaction.InvolvedElements.Contains(element))
                { status.RuntimeElement = element; return; }
    }
    private static Task ExecuteAbsorbed(StatusDamageExecution context)
    {
        if (context.Effect.StatusEffectID2 != AbsorbedDamage)
            throw new InvalidOperationException("砂糖转化伤害处理器仅用于大型风灵附加伤害。");
        string element = GetAbsorbedElement(context.Status);
        if (element == null) return Task.CompletedTask;
        var source = context.Effect;
        // 执行副本保留来源、倍率、目标、性质与重复次数，不改DataManager共享行。
        var prepared = new StatusEffectData
        {
            StatusEffectID = source.StatusEffectID, StatusEffectID2 = source.StatusEffectID2,
            EffectIndex = source.EffectIndex, EffectType = source.EffectType, Element = element,
            DamageType = source.DamageType, Duration = source.Duration, AddInPhase = source.AddInPhase,
            TriggerPhase = source.TriggerPhase, Param1 = source.Param1, Param2 = source.Param2, Param3 = source.Param3,
            TargetType = source.TargetType, TargetSelect = source.TargetSelect, TargetConsecutive = source.TargetConsecutive,
            TargetOverride = source.TargetOverride, ScriptHook = source.ScriptHook, ExecuteCount = source.ExecuteCount,
            ExecuteCountHandler = source.ExecuteCountHandler, AuthoredFields = source.AuthoredFields
        };
        return context.ExecuteDefault(prepared);
    }
}
