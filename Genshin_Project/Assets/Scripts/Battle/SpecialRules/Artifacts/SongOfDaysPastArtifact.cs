using System;
using System.Collections.Generic;

/// <summary>550：原WhenHealing/ApplyStatus链记录治疗；自然OnEnd传值；CountDamage只负责正式hit的加算和额度。</summary>
public static class SongOfDaysPastArtifact
{
    public const string ListenerID = "ST_Artifact_455002";
    public const string RecordID = "ST_Artifact_455003";
    public const string WaveID = "ST_Artifact_455004";
    public const string BonusID = "ST_Artifact_455005";
    private const string BonusEffectID = "STE_Artifact_45500501";
    internal const float Limit = 15000f;

    internal sealed class RecordState
    {
        internal readonly HashSet<long> Events = new HashSet<long>();
        internal bool Transferred;
    }
    internal sealed class WaveState
    {
        internal int Remaining = 5;
        internal int Spent;
        internal readonly Dictionary<long, HitUse> Hits = new Dictionary<long, HitUse>();
    }
    internal sealed class HitUse
    {
        internal BattleEntity Attacker;
        internal int Pending;
        internal bool Landed;
        internal bool Completed;
    }

    internal static void Register()
    {
        BattleSpecialRules.RegisterPreparedBaseDamageEffect(BonusID, 45500501);
        BattleSpecialRules.RegisterTargets("Hook(45500201)", (battle, owner) =>
        {
            var context = ScriptHookContext.Current;
            if (!HealingMatches(context, owner)) return new List<int>();
            StatusInstance record = Find(battle, owner, RecordID);
            BattleEntity host = record?.Caster ?? owner;
            return new List<int> { host.SlotPosition };
        });
        BattleSpecialRules.RegisterNumberedCondition("455003", (argument, context) => CanTransfer(context?.Status));
        BattleSpecialRules.RegisterNumberedCondition("455005", (argument, context) =>
        {
            StatusInstance bonus = context?.Status;
            StatusInstance wave = bonus?.Caster?.GetStatus(WaveID);
            return wave != null && wave.IsActive && wave.BoundStatuses.Contains(bonus)
                && wave.CountRuntimeState is WaveState state
                && state.Hits.TryGetValue(BattleEventHub.CurrentHitId, out HitUse use)
                && !use.Completed && context.Target != null && context.Target.Side != use.Attacker.Side;
        });
    }

    internal static BattleEntity RequireHost(StatusInstance status, object host)
    {
        if (!(host is BattleEntity owner) || owner != status.Caster || owner.CharacterCtrl == null)
            throw new InvalidOperationException("昔时之歌记录及浪潮必须保留原穿戴者宿主。");
        return owner;
    }

    private static bool HealingMatches(ScriptHookContext context, BattleEntity owner)
    {
        var heal = context?.Declaration?.Get<HealEventData>();
        return heal != null && owner != null && heal.Source == owner
            && owner.HasStatus(ListenerID) && heal.Subject != null && IsMember(BattleManager.Instance, owner)
            && IsMember(BattleManager.Instance, heal.Subject) && heal.Subject.Side == owner.Side
            && heal.Actual + heal.Overheal > 0f;
    }

    internal static void RecordHealing(StatusInstance record, BattleEntity source)
    {
        var context = ScriptHookContext.Current;
        if (!HealingMatches(context, source))
            throw new InvalidOperationException("渴盼只能累计本穿戴者已经提交的队伍治疗声明。");
        var state = (RecordState)record.CountRuntimeState;
        var heal = context.Declaration.Get<HealEventData>();
        if (!state.Transferred && state.Events.Add(context.Declaration.Header.EventId))
            record.RuntimeValue = Math.Min(Limit, record.RuntimeValue + heal.Actual + heal.Overheal);
        // 不换Caster、宿主和ApplyOrder，不刷新2回合；生命之契抵扣不在Actual+Overheal中。
    }

    internal static bool CanTransfer(StatusInstance record) => record?.StatusID2 == RecordID && record.IsActive
        && !record.IsPermanent && record.RemainingPhaseCount <= 0
        && record.CountRuntimeState is RecordState state && !state.Transferred;

    internal static StatusInstance Find(BattleManager battle, BattleEntity owner, string id)
    {
        StatusInstance found = null;
        if (battle == null || owner == null) return null;
        foreach (var ally in battle.Allies)
        {
            if (ally?.Entity == null || ally.Entity.Side != owner.Side) continue;
            foreach (StatusInstance candidate in ally.Entity.GetStatusInstances(id))
            {
                if (!candidate.IsActive || (candidate.CountRuntimeState is RecordState record && record.Transferred)) continue;
                if (found != null && found != candidate)
                    throw new InvalidOperationException("昔时之歌同队出现多份" + id);
                found = candidate;
            }
        }
        return found;
    }

    private static bool IsMember(BattleManager battle, BattleEntity actor)
    {
        if (battle == null || actor == null) return false;
        foreach (var ally in battle.Allies) if (ally?.Entity == actor) return true;
        return false;
    }

    internal static CountDamageContribution Prepare(StatusInstance wave, BattleEntity owner, CountDamageContext context)
    {
        long hitID = BattleEventHub.CurrentHitId;
        if (!wave.IsActive || !owner.IsAlive || !EquipmentStatusLifetime.CanExecute(wave)
            || context == null || hitID == 0 || context.Attacker?.CharacterCtrl?.IsActive != true
            || !IsMember(BattleManager.Instance, context.Attacker) || context.Attacker.Side != owner.Side
            || context.Target == null || context.Target.Side == owner.Side
            || (context.SourceKind != ReactionSourceKind.CharacterSkill && context.SourceKind != ReactionSourceKind.StatusEffect)
            || (context.DamageType != "Normal" && context.DamageType != "Heavy"
                && context.DamageType != "Skill" && context.DamageType != "Burst")) return null;
        var state = (WaveState)wave.CountRuntimeState;
        if (!state.Hits.TryGetValue(hitID, out HitUse use))
        {
            if (state.Remaining <= 0) return null;
            use = new HitUse { Attacker = context.Attacker };
            state.Hits.Add(hitID, use);
            state.Remaining--; // 每逻辑hit只预占一次；嵌套hit不能使用已预占的第五次。
        }
        if (use.Completed || use.Attacker != context.Attacker) return null;

        StatusInstance bonus = null;
        foreach (var candidate in wave.BoundStatuses)
            if (candidate.StatusID2 == BonusID && candidate.IsActive && context.Attacker.HasStatusInstance(candidate))
            { bonus = candidate; break; }
        var dm = DataManager.Instance;
        if (bonus == null || dm == null || !dm.StatusEffectDict.TryGetValue(BonusEffectID, out var effect)
            || effect.EffectType != "Buff" || !BuffExpressionEvaluator.TryResolveScalar(effect.Param1, bonus, out float value)
            || float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
        {
            if (use.Pending == 0) { state.Remaining++; state.Hits.Remove(hitID); }
            throw new InvalidOperationException("浪潮缺少有效的绑定基础伤害Buff或Count表达式。");
        }
        use.Pending++;
        // 本入口读取原Buff的0.08*Count，并作为唯一基础加算送入既有乘区，不能再在面板汇总重复加一次。
        return new CountDamageContribution(value,
            result => Finish(wave, owner, state, hitID, use, true),
            () => Finish(wave, owner, state, hitID, use, false));
    }

    private static void Finish(StatusInstance wave, BattleEntity owner, WaveState state, long hitID, HitUse use, bool landed)
    {
        use.Pending--;
        use.Landed |= landed;
        // CountDamageBatch在全部目标结算后才Finish；第五hit的所有主/溅射目标都已经取到原加成。
        if (landed && !use.Completed)
        {
            use.Completed = true;
            state.Spent++;
            // Remaining还包含其他嵌套hit的预占；必须真正完成5hit，不能把尚可取消的预占当作已消费。
            if (state.Spent == 5 && owner.HasStatusInstance(wave)) owner.RemoveStatusInstance(wave, "SongFiveHits");
        }
        if (use.Pending != 0) return;
        if (!use.Landed) state.Remaining++; // 整hit取消/未命中，归还唯一预占；不改新一轮浪潮。
        state.Hits.Remove(hitID);
    }
}

public sealed class SongRecordCountHandler : CountStatusHandler
{
    public override void OnCreated(StatusInstance status, object host)
    {
        var owner = SongOfDaysPastArtifact.RequireHost(status, host);
        if (SongOfDaysPastArtifact.Find(BattleManager.Instance, owner, SongOfDaysPastArtifact.RecordID) != null)
            throw new InvalidOperationException("渴盼必须通过目标重定向累计到现有记录。");
        status.RuntimeValue = 0f;
        status.CountRuntimeState = new SongOfDaysPastArtifact.RecordState();
        status.StackCount = 1;
        status.IsPermanent = false;
        status.RemainingPhaseCount = 2;
        status.AddInPhase = 1;
        status.TriggerPhase = 0;
        SongOfDaysPastArtifact.RecordHealing(status, owner);
    }
    public override StatusInstance OnReapplied(StatusInstance status, BattleEntity host, CountStatusApplication application)
    {
        SongOfDaysPastArtifact.RequireHost(status, host);
        SongOfDaysPastArtifact.RecordHealing(status, application.Caster);
        return status;
    }
    public override void OnRemoved(StatusInstance status, object host)
    { status.RuntimeValue = 0f; status.CountRuntimeState = null; }
}

public sealed class SongWaveCountHandler : CountStatusHandler
{
    public override void OnCreated(StatusInstance status, object host)
    {
        var owner = SongOfDaysPastArtifact.RequireHost(status, host);
        StatusInstance record = ScriptHookContext.Current?.Status;
        if (!SongOfDaysPastArtifact.CanTransfer(record) || record.Caster != owner)
            throw new InvalidOperationException("浪潮只能接收正在自然到期的这一份渴盼。");
        float recorded = record.RuntimeValue; // 先冻结，绝不等原记录OnRemoved清零后再读取。
        StatusInstance old = SongOfDaysPastArtifact.Find(BattleManager.Instance, owner, SongOfDaysPastArtifact.WaveID);
        if (old != null) old.Caster.RemoveStatusInstance(old, "SongNewWave");
        ((SongOfDaysPastArtifact.RecordState)record.CountRuntimeState).Transferred = true;
        status.RuntimeValue = Math.Min(SongOfDaysPastArtifact.Limit, Math.Max(0f, recorded));
        status.CountRuntimeState = new SongOfDaysPastArtifact.WaveState();
        status.StackCount = 1;
        status.IsPermanent = false;
        status.RemainingPhaseCount = 3;
        status.AddInPhase = 1;
        status.TriggerPhase = 0;
    }
    public override StatusInstance OnReapplied(StatusInstance status, BattleEntity host, CountStatusApplication application)
    {
        SongOfDaysPastArtifact.RequireHost(status, host);
        if (!SongOfDaysPastArtifact.CanTransfer(ScriptHookContext.Current?.Status))
            throw new InvalidOperationException("浪潮重施必须来自新一轮自然到期的渴盼。");
        host.RemoveStatusInstance(status, "SongNewWave");
        return application.ApplyTo(host); // 新轮次、新5hit和完整3回合，不相加，不让旧回调消费新池。
    }
    public override CountDamageContribution PrepareDamage(StatusInstance status, object host, CountDamageContext context)
        => SongOfDaysPastArtifact.Prepare(status, SongOfDaysPastArtifact.RequireHost(status, host), context);
    public override void OnRemoved(StatusInstance status, object host)
    { status.RuntimeValue = 0f; status.CountRuntimeState = null; }
}
