using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// 温迪专用事实索引：战技真实命中、每份暴风之眼锁定的转化元素、C4真实获能、C6来源元素。
/// 不新增效果类型，不另建声明队列；伤害/回能/施加状态仍走公共入口。
/// </summary>
public static class VentiScriptHooks
{
    public const string SkillID = "SK_Skill_Venti";
    public const string SkillDamageID = "SE_Skill_Venti1";
    public const string AirborneTargetsHook = "Venti_Skill()";
    public const string SkillTargetsHook = "Venti_SkillTargets()";
    public const string BaseDebuffID = "ST_Venti_SkillDebuff1";
    public const string AirborneDebuffID = "ST_Venti_SkillDebuff2";
    public const string BurstID = "ST_Venti_Burst";
    public const string BurstEndPulseID = "ST_Venti_BurstEndPulse";
    public const string BurstInitialDamageID = "SE_Burst_Venti2";
    public const string BurstWindDamageID = "STE_Venti_Burst1";
    public const string BurstBonusDamageID = "STE_Venti_Burst2";
    public const string BurstRecordHook = "Venti_BurstRecord()";
    public const string BurstBonusHook = "Venti_Burst()";
    private const string C4ID = "ST_Venti_C4";
    private const string C6ElementDebuffID = "ST_Venti_C6Debuff2";
    private static readonly string[] AbsorptionPriority = { "Pyro", "Hydro", "Electro", "Cryo" };
    private sealed class BurstMemory { internal string Element; }
    private sealed class ElementMemory { internal string Element; }
    private sealed class Subscription { internal IDisposable Value; }
    private static readonly ConditionalWeakTable<StatusInstance, BurstMemory> Bursts
        = new ConditionalWeakTable<StatusInstance, BurstMemory>();
    private static readonly ConditionalWeakTable<StatusInstance, ElementMemory> DebuffElements
        = new ConditionalWeakTable<StatusInstance, ElementMemory>();
    private static readonly ConditionalWeakTable<StatusInstance, Subscription> EnergySubscriptions
        = new ConditionalWeakTable<StatusInstance, Subscription>();
    private static readonly AsyncLocal<SkillScope> Current = new AsyncLocal<SkillScope>();

    internal static void Register()
    {
        BattleSpecialRules.RegisterSkillScope(caster => new SkillScope(caster));
        BattleSpecialRules.RegisterTargets(AirborneTargetsHook, (battle, caster) => Targets(battle, caster, true));
        BattleSpecialRules.RegisterTargets(SkillTargetsHook, (battle, caster) => Targets(battle, caster, false));
        BattleSpecialRules.RegisterStatusLifecycle(BurstID, (status, host) =>
        {
            if (!(host is BattleEntity owner) || owner != status.Caster || owner.EntityID != 1012)
                throw new InvalidOperationException("暴风之眼必须施加在温迪本人。");
            Bursts.Add(status, new BurstMemory());
            if (Current.Value?.Caster == owner) Current.Value.Burst = status;
        }, null);
        BattleSpecialRules.RegisterAnemoResult(RecordAbsorption);
        BattleSpecialRules.RegisterDamage(BurstRecordHook, (hook, caster, type) =>
        {
            if (caster?.EntityID != 1012 || type != "Damage")
                throw new NotSupportedException("Venti_BurstRecord仅用于温迪爆发风伤。");
        }, (hook, caster, target, hit, level) => hit);
        BattleSpecialRules.RegisterStatusDamage(BurstRecordHook, context =>
        {
            if (context.Effect.StatusEffectID2 != BurstWindDamageID || context.Effect.Element != "Anemo"
                || ResolveBurst(context.Status) == null)
                throw new InvalidOperationException("暴风之眼风伤缺少对应的具体爆发实例。");
            return context.ExecuteDefault(context.Effect);
        });
        BattleSpecialRules.RegisterStatusDamage(BurstBonusHook, ExecuteAbsorbedDamage);
        BattleSpecialRules.RegisterTargets("Venti_BurstEnergy()", EnergyTargets);
        BattleSpecialRules.RegisterTargets("Venti_HitTarget()", (battle, caster) =>
        {
            if (battle == null || caster?.EntityID != 1012) return new List<int>();
            var hit = ScriptHookContext.Current?.DamageEvent;
            var target = hit?.Target;
            return hit?.HitLanded == true && hit.Source?.SourceEntity == caster && target != null
                && target.IsAlive && target.Side != caster.Side
                && battle.GetEntityByPosition(target.Side, target.SlotPosition) == target
                    ? new List<int> { target.SlotPosition } : new List<int>();
        });
        BattleSpecialRules.RegisterCondition("1012_C4", (argument, context) =>
            string.IsNullOrEmpty(argument) && EnergyMatches(context?.Status, context?.Declaration?.Get<ResourceEventData>()));
        BattleSpecialRules.RegisterStatusLifecycle(C4ID, StartEnergyListener, StopEnergyListener);
        BattleSpecialRules.RegisterCondition("1012_C6", (argument, context) =>
            string.IsNullOrEmpty(argument) && context?.Caster?.EntityID == 1012
                && context.Caster.ConstellationLevel >= 6 && GetHitBurst(context.DamageEvent) != null
                && GetAbsorbedElement(GetHitBurst(context.DamageEvent)) != null);
        BattleSpecialRules.RegisterStatusLifecycle(C6ElementDebuffID, (status, host) =>
        {
            var burst = GetHitBurst(ScriptHookContext.Current?.DamageEvent);
            string element = GetAbsorbedElement(burst);
            if (burst?.Caster != status.Caster || element == null)
                throw new InvalidOperationException("温迪C6转化减抗缺少本次命中的已锁定元素。");
            DebuffElements.Add(status, new ElementMemory { Element = element });
        }, null);
        BattleSpecialRules.RegisterCondition("1012_C6Element", (argument, context) =>
            string.IsNullOrEmpty(argument) && context?.Status != null
                && DebuffElements.TryGetValue(context.Status, out var memory)
                && memory.Element == context.DamageElement);
        BattleSpecialRules.RegisterCondition("1012_C2", (argument, context) =>
        {
            BattleEntity source = context?.Caster;
            BattleEntity target = context?.Target;
            if (!string.IsNullOrEmpty(argument) || source?.EntityID != 1012 || source.ConstellationLevel < 2
                || context?.Status?.StatusID2 != AirborneDebuffID || target == null
                || !target.HasStatus(AirborneStatusHandler.EnemyStatusID)) return false;
            foreach (var status in target.GetStatusInstances(BaseDebuffID))
                if (status.IsActive && status.Caster == source) return true;
            return false;
        });
    }

    private sealed class SkillScope : IDisposable
    {
        internal readonly BattleEntity Caster;
        internal readonly HashSet<BattleEntity> HitTargets = new HashSet<BattleEntity>();
        internal StatusInstance Burst;
        private readonly SkillScope previous;
        private readonly IDisposable subscription;
        private bool disposed;
        internal SkillScope(BattleEntity caster)
        {
            Caster = caster; previous = Current.Value; Current.Value = this;
            try
            {
                if (caster?.EntityID == 1012)
                    subscription = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved,
                        (header, hit) =>
                        {
                            var source = hit.Source;
                            if (hit.HitLanded && hit.Subject != null && hit.Subject.Side != caster.Side
                                && source?.SourceEntity == caster && source.SourceKind == ReactionSourceKind.CharacterSkill
                                && source.SourceSkillID == SkillID && source.SourceEffectID == SkillDamageID)
                                HitTargets.Add(hit.Subject); // 护盾命中仍记录，取目标时再排除死亡/替换。
                        }, 0, () => !disposed && ReferenceEquals(Current.Value, this));
            }
            catch { Current.Value = previous; throw; }
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; subscription?.Dispose(); Current.Value = previous;
        }
    }

    /// <summary>按具体实例查询；未转化/已结束返回null，不查“最近一次爆发”。</summary>
    public static string GetAbsorbedElement(StatusInstance status)
    {
        StatusInstance burst = ResolveBurst(status);
        return burst != null && Bursts.TryGetValue(burst, out var memory) ? memory.Element : null;
    }

    private static StatusInstance ResolveBurst(StatusInstance status)
    {
        if (status?.Caster == null || !status.IsActive) return null;
        if (status.StatusID2 == BurstID)
            return status.Caster.HasStatusInstance(status) ? status : null;
        if (status.StatusID2 != BurstEndPulseID) return null;
        // 只沿实际BindStatus关系找父实例；同ID的上一轮或另一位温迪不可接管。
        foreach (var parent in status.Caster.GetStatusInstances(BurstID))
            if (parent.IsActive && parent.BoundStatuses.Contains(status)) return parent;
        return null;
    }

    private static StatusInstance InitialBurst(BattleEntity caster)
        => Current.Value?.Caster == caster ? ResolveBurst(Current.Value.Burst) : null;

    private static StatusInstance GetHitBurst(DamageResolvedEvent hit)
    {
        if (hit?.HitLanded != true || hit.Source?.SourceEntity?.EntityID != 1012) return null;
        var source = hit.Source;
        if ((source.SourceKind == ReactionSourceKind.CharacterSkill || source.SourceKind == ReactionSourceKind.StatusEffect)
            && source.SourceEffectID == BurstInitialDamageID)
            return InitialBurst(source.SourceEntity);
        if (source.SourceKind == ReactionSourceKind.StatusEffect
            && (source.SourceEffectID == BurstWindDamageID || source.SourceEffectID == BurstBonusDamageID))
        {
            var burst = ResolveBurst(source.SourceStatus);
            return burst?.Caster == source.SourceEntity ? burst : null;
        }
        return null;
    }

    private static void RecordAbsorption(BattleEntity caster, string effectID, string hook,
        StatusInstance sourceStatus, ReactionResult result)
    {
        if (caster?.EntityID != 1012 || hook != BurstRecordHook || result == null) return;
        StatusInstance burst = effectID == BurstInitialDamageID
            ? InitialBurst(caster) : effectID == BurstWindDamageID ? ResolveBurst(sourceStatus) : null;
        if (burst?.Caster != caster || !Bursts.TryGetValue(burst, out var memory) || memory.Element != null) return;
        // 先汇总本hit全部目标的真实扩散，再按火水雷冰选一次。之后所有hit都不能覆盖。
        foreach (string element in AbsorptionPriority)
            foreach (var reaction in result.TriggeredReactions)
                if (reaction.Type == ReactionType.Swirl && reaction.Declaration != null
                    && reaction.SourceEntity == caster && reaction.SourceEffectID == effectID
                    && reaction.InvolvedElements.Contains(element))
                {
                    memory.Element = element;
                    return;
                }
    }

    private static Task ExecuteAbsorbedDamage(StatusDamageExecution context)
    {
        if (context.Effect.StatusEffectID2 != BurstBonusDamageID)
            throw new InvalidOperationException("Venti_Burst仅用于暴风之眼附加伤害。");
        string element = GetAbsorbedElement(context.Status);
        if (element == null) return Task.CompletedTask; // 没转化不制造物理伤害，也不抽暴击。
        StatusEffectData source = context.Effect;
        var prepared = new StatusEffectData
        {
            StatusEffectID = source.StatusEffectID, StatusEffectID2 = source.StatusEffectID2,
            EffectIndex = source.EffectIndex, EffectType = source.EffectType,
            Element = element, DamageType = source.DamageType, Duration = source.Duration,
            AddInPhase = source.AddInPhase, TriggerPhase = source.TriggerPhase,
            Param1 = source.Param1, Param2 = source.Param2, Param3 = source.Param3,
            TargetType = source.TargetType, TargetSelect = source.TargetSelect,
            TargetConsecutive = source.TargetConsecutive, TargetOverride = source.TargetOverride,
            ScriptHook = source.ScriptHook, ExecuteCount = source.ExecuteCount,
            ExecuteCountHandler = source.ExecuteCountHandler, AuthoredFields = source.AuthoredFields
        };
        // 不修改DataManager共享行；保留原EffectExecutionID、状态来源与本次目标重算。
        return context.ExecuteDefault(prepared);
    }

    private static List<int> EnergyTargets(BattleManager battle, BattleEntity caster)
    {
        var positions = new List<int>();
        var burst = ResolveBurst(ScriptHookContext.Current?.Status);
        if (battle == null || caster?.EntityID != 1012 || burst?.Caster != caster) return positions;
        string element = GetAbsorbedElement(burst);
        foreach (var ally in battle.Allies)
        {
            BattleEntity member = ally != null ? ally.Entity : null;
            if (member == null || !member.IsAlive || member.Side != caster.Side) continue;
            var data = DataManager.Instance;
            bool matches = element != null && data != null
                && data.CharacterAttributesDict.TryGetValue(member.EntityID, out var attributes)
                && attributes.Element == element;
            if ((member == caster || matches) && !positions.Contains(member.SlotPosition))
                positions.Add(member.SlotPosition);
        }
        positions.Sort();
        return positions;
    }

    private static bool EnergyMatches(StatusInstance status, ResourceEventData fact)
        => status?.StatusID2 == C4ID && status.Caster?.EntityID == 1012
            && status.Caster.ConstellationLevel >= 4 && fact?.Resource == "Energy"
            && fact.Subject == status.Caster && fact.Added > 0d;

    private static void StartEnergyListener(StatusInstance status, object host)
    {
        if (!(host is BattleEntity owner) || owner != status.Caster || owner.EntityID != 1012)
            throw new InvalidOperationException("温迪C4初始化状态必须在本人身上。");
        var subscription = new Subscription();
        EnergySubscriptions.Add(status, subscription);
        subscription.Value = BattleEventHub.Subscribe<ResourceEventData>(BattleEventKind.ResourceChanged, (header, fact) =>
        {
            if (!EnergyMatches(status, fact)) return;
            var declaration = BattleEventHub.Current;
            var data = DataManager.Instance;
            if (data == null || !data.StatusActionDict.TryGetValue(C4ID, out var actions)) return;
            foreach (var action in actions)
                if (action.ActionType == "OnTrigger" && action.ScriptHook == "1012_C4()")
                    owner.CharacterCtrl.TryExecuteEventStatusAction(status, action, host, new ScriptHookContext
                    {
                        Caster = owner, Target = owner, Status = status,
                        StatusHost = host, Declaration = declaration
                    }, () => status.IsActive && owner.HasStatusInstance(status));
        }, status.ApplyOrder, () => status.IsActive && owner.HasStatusInstance(status));
    }

    private static void StopEnergyListener(StatusInstance status, object host)
    {
        if (!EnergySubscriptions.TryGetValue(status, out var subscription)) return;
        subscription.Value?.Dispose();
        EnergySubscriptions.Remove(status);
    }

    private static List<int> Targets(BattleManager battle, BattleEntity caster, bool minionsOnly)
    {
        var result = new List<int>();
        var scope = Current.Value;
        if (battle == null || caster?.EntityID != 1012 || scope?.Caster != caster) return result;
        foreach (var target in scope.HitTargets)
        {
            if (!target.IsAlive || target.Side == caster.Side
                || battle.GetEntityByPosition(target.Side, target.SlotPosition) != target) continue;
            if (minionsOnly)
            {
                var data = DataManager.Instance;
                if (data == null || !data.EnemyMainDict.TryGetValue(target.EntityID, out var identity)
                    || string.IsNullOrWhiteSpace(identity.EnemyType))
                    continue; // 用户确认：未分类暂不击飞，不猜Minion，不中断伤害/减抗/产能。
                if (!string.Equals(identity.EnemyType, "Minion", StringComparison.OrdinalIgnoreCase)) continue;
            }
            if (!result.Contains(target.SlotPosition)) result.Add(target.SlotPosition);
        }
        result.Sort();
        return result;
    }
}
