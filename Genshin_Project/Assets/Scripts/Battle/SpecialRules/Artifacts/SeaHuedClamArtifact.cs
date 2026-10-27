using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

/// <summary>539 海染：初始化普通状态监听治疗；泡沫Count只保存本实例累计值与去重身份。</summary>
public static class SeaHuedClamArtifact
{
    public const string ListenerID = "ST_Artifact_453902";
    public const string BubbleID = "ST_Artifact_453903";
    private const string CreateEffectID = "STE_Artifact_45390201";
    private const string BurstEffectID = "STE_Artifact_45390301";
    private const string BurstHook = "Hook(45390302)";
    internal const float Limit = 30000f;

    internal sealed class BubbleRuntime
    {
        internal readonly HashSet<long> HealingEvents = new HashSet<long>();
        internal bool Detonated;
    }

    private sealed class Subscription { internal IDisposable Value; }
    private sealed class TeamGeneration
    {
        internal long Session;
        internal readonly Dictionary<BattleSide, int> LastTurns = new Dictionary<BattleSide, int>();
    }
    private static readonly ConditionalWeakTable<StatusInstance, Subscription> Subscriptions
        = new ConditionalWeakTable<StatusInstance, Subscription>();
    private static readonly ConditionalWeakTable<BattleManager, TeamGeneration> Generations
        = new ConditionalWeakTable<BattleManager, TeamGeneration>();

    internal static void Register()
    {
        BattleSpecialRules.RegisterStatusLifecycle(ListenerID, Start, Stop);
        BattleSpecialRules.RegisterNumberedCondition("453903", (argument, context) => CanBurst(context?.Status));
        BattleSpecialRules.RegisterNumberedCondition("45390301", (argument, context) => CanBurst(context?.Status));
        BattleSpecialRules.RegisterStatusDamage(BurstHook, Burst);
    }

    private static void Start(StatusInstance status, object host)
    {
        BattleEntity owner = RequireOwner(status, host);
        var subscription = new Subscription();
        Subscriptions.Add(status, subscription);
        subscription.Value = BattleEventHub.Subscribe<HealEventData>(BattleEventKind.HealResolved,
            (header, heal) => OnHealing(status, owner, header, heal), status.ApplyOrder,
            () => status.IsActive && owner.HasStatusInstance(status));
    }

    private static void Stop(StatusInstance status, object host)
    {
        if (!Subscriptions.TryGetValue(status, out var subscription)) return;
        subscription.Value?.Dispose();
        Subscriptions.Remove(status);
    }

    private static void OnHealing(StatusInstance listener, BattleEntity owner,
        BattleEventHeader header, HealEventData heal)
    {
        BattleManager battle = BattleManager.Instance;
        if (battle == null || heal.Source != owner || heal.Subject == null
            || heal.Subject.Side != owner.Side || heal.Subject.Type != BattleEntity.EntityType.Character) return;
        bool partyTarget = false;
        foreach (var ally in battle.Allies)
            if (ally != null && ally.Entity == heal.Subject) { partyTarget = true; break; }
        if (!partyTarget) return;
        // 治疗已由公共HealBatch提交。抵扣生命之契的量不在Actual+Overheal内。
        StatusInstance bubble = FindBubble(battle, owner.Side);
        if (bubble == null)
        {
            TeamGeneration generation = Generations.GetValue(battle, key => new TeamGeneration());
            if (generation.Session != header.SessionId)
            {
                generation.Session = header.SessionId;
                generation.LastTurns.Clear();
            }
            int last;
            if (generation.LastTurns.TryGetValue(owner.Side, out last) && last == battle.TurnCount) return;
            DataManager dm = DataManager.Instance;
            if (dm == null || !dm.StatusMainDict.TryGetValue(BubbleID, out var main)
                || !dm.StatusEffectDict.TryGetValue(CreateEffectID, out var create)
                || create.EffectType != "ApplyStatus" || create.Param1 != BubbleID || create.TargetType != "Self"
                || create.Duration <= 0 || main.StatusType != "Count")
                throw new InvalidOperationException("海染泡沫缺少有效的ApplyStatus创建配置。");
            bubble = owner.AddStatus(BubbleID, owner, create.Duration, create.AddInPhase,
                create.TriggerPhase, main, listener.WeaponContext, create.Param2);
            generation.LastTurns[owner.Side] = battle.TurnCount;
        }
        // 全队只有一个泡沫，只记录创建者治疗；不因其他治疗者或重施而换来源/刷新时间。
        if (bubble.Caster != owner || !(bubble.CountRuntimeState is BubbleRuntime runtime)
            || runtime.Detonated || !runtime.HealingEvents.Add(header.EventId)) return;
        bubble.RuntimeValue = Math.Min(Limit, bubble.RuntimeValue + Math.Max(0f, heal.Actual + heal.Overheal));
    }

    private static StatusInstance FindBubble(BattleManager battle, BattleSide side)
    {
        foreach (var ally in battle.Allies)
        {
            StatusInstance found = FindBubbleOn(ally != null ? ally.Entity : null, side);
            if (found != null) return found;
        }
        foreach (var enemy in battle.Enemies)
        {
            StatusInstance found = FindBubbleOn(enemy != null ? enemy.Entity : null, side);
            if (found != null) return found;
        }
        return null;
    }

    private static StatusInstance FindBubbleOn(BattleEntity entity, BattleSide side)
    {
        if (entity == null || entity.Side != side) return null;
        foreach (StatusInstance status in entity.GetStatusInstances(BubbleID))
            if (status.IsActive) return status;
        return null;
    }

    private static bool CanBurst(StatusInstance status)
    {
        return status != null && status.StatusID2 == BubbleID && status.IsActive
            && !status.IsPermanent && status.RemainingPhaseCount <= 0
            && status.CountRuntimeState is BubbleRuntime runtime && !runtime.Detonated;
    }

    private static async Task Burst(StatusDamageExecution context)
    {
        if (context.Effect.StatusEffectID2 != BurstEffectID || context.Effect.Element != "Physical")
            throw new InvalidOperationException("海染伤害钩子只能用于泡沫爆炸效果。");
        // 只解析本钩子约定的单hit“系数*RuntimeValue,0”，不静默忽略Param1的修改。
        string[] hit = (context.Effect.Param1 ?? string.Empty).Split(',');
        string[] formula = hit[0].Split('*');
        float ratio, aura;
        if (hit.Length != 2 || formula.Length != 2 || formula[1].Trim() != "RuntimeValue"
            || !float.TryParse(formula[0], NumberStyles.Float, CultureInfo.InvariantCulture, out ratio)
            || float.IsNaN(ratio) || float.IsInfinity(ratio) || ratio < 0f
            || !float.TryParse(hit[1], NumberStyles.Float, CultureInfo.InvariantCulture, out aura) || aura != 0f)
            throw new InvalidOperationException("海染Damage.Param1必须为非负有限系数*RuntimeValue,0。");
        if (!CanBurst(context.Status)) return;
        var runtime = (BubbleRuntime)context.Status.CountRuntimeState;
        runtime.Detonated = true; // 一份泡沫至多一次，即使效果ExecuteCount被误填为多次。
        float recorded = Math.Min(Limit, Math.Max(0f, context.Status.RuntimeValue));
        var targets = new List<BattleEntity>();
        if (context.Targets != null)
            foreach (BattleEntity target in context.Targets)
                if (target != null && target.IsAlive && target.Side != context.Status.Caster.Side
                    && !targets.Contains(target)) targets.Add(target);
        await BattleExecution.Play(new BattlePresentationRequest(BattlePresentationKind.Hit,
            context.Status.Caster, context.Effect.StatusEffectID2, 1, targets), () =>
        {
            using (var batch = new HitBatch())
            {
                for (int index = 0; index < targets.Count; index++)
                {
                    BattleEntity target = targets[index];
                    DamageSourceInfo source = DamageSourceInfo.FromStatus(context.Status, context.Effect.StatusEffectID2);
                    source.CountsAsHit = false; // 不是一次普攻/战技等攻击命中，不派发OnHit。
                    batch.Resolve(new HitRequest
                    {
                        Target = target,
                        DamageBeforeShield = recorded * ratio * ReactionDamageCalculator.GetResistanceMultiplier(target, "Physical"),
                        Element = "Physical", ElementAmount = 0f, DamageType = context.Effect.DamageType,
                        Source = source, HitIndex = 0, TargetIndex = index
                    });
                }
                batch.Flush();
            }
        });
        // 不进入普通直伤、反应、暴击或Count攻击贡献流程；护盾、受伤/死亡声明仍走HitBatch。
    }

    internal static BattleEntity RequireOwner(StatusInstance status, object host)
    {
        if (!(host is BattleEntity owner) || owner != status.Caster || owner.Type != BattleEntity.EntityType.Character)
            throw new InvalidOperationException("海染初始化状态与泡沫必须挂在穿戴者本人。");
        return owner;
    }
}

public sealed class SeaHuedClamCountHandler : CountStatusHandler
{
    public override void OnCreated(StatusInstance status, object host)
    {
        SeaHuedClamArtifact.RequireOwner(status, host);
        status.RuntimeValue = 0f;
        status.StackCount = 1;
        status.CountRuntimeState = new SeaHuedClamArtifact.BubbleRuntime();
    }

    public override StatusInstance OnReapplied(StatusInstance status, BattleEntity host, CountStatusApplication application)
    {
        SeaHuedClamArtifact.RequireOwner(status, host);
        return status; // 不改来源、不刷新计时、不清空累计量。
    }

    public override void OnRemoved(StatusInstance status, object host)
    {
        status.CountRuntimeState = null;
    }
}
