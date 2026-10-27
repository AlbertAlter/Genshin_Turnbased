using System;
using System.Collections.Generic;

/// <summary>
/// 北斗只在此管理本次架盾、破盾事实和真实命中目标。
/// 反击是一个战技下的三档配表效果，倍率与产能同档；不新建可选技能或伤害结算器。
/// </summary>
public static class BeidouScriptHooks
{
    public const string GuardID = "ST_Beidou_Guard";
    public const string ShieldID = "ST_Beidou_GuardShield";
    public const string BurstID = "ST_Beidou_Stormbreaker";
    public const string BrokenHook = "Beidou_ShieldBroken()";

    internal static void Register()
    {
        BattleSpecialRules.RegisterCondition("Beidou_EnemyHit", (arg, c) => string.IsNullOrEmpty(arg)
            && EnemyHit(c) && (!(c.Status?.CountRuntimeState is BeidouGuardCount.Memory memory) || !memory.Started));
        BattleSpecialRules.RegisterCondition("Beidou_IncomingHit", (arg, c) => string.IsNullOrEmpty(arg)
            && IncomingHit(c));
        BattleSpecialRules.RegisterCondition("Beidou_GuardReady", (arg, c) => string.IsNullOrEmpty(arg)
            && Active(c?.Status) && c.Status.CountRuntimeState is BeidouGuardCount.Memory memory && !memory.Started);
        BattleSpecialRules.RegisterCondition("Beidou_ShieldBroken", (arg, c) => string.IsNullOrEmpty(arg)
            && Active(c?.Status) && c.Status.CountRuntimeState is BeidouGuardCount.Memory memory && memory.Broken
            && c.Declaration?.Get<ShieldChangedEventData>()?.Reason == "Absorption");
        BattleSpecialRules.RegisterCondition("Beidou_CounterLayer", (arg, c) => int.TryParse(arg, out int layer)
            && layer >= 0 && layer <= 2 && Active(c?.Status)
            && c.Status.CountRuntimeState is BeidouGuardCount.Memory memory && memory.Started && memory.Layers == layer);
        BattleSpecialRules.RegisterCondition("Beidou_BurstHit", (arg, c) => string.IsNullOrEmpty(arg)
            && c?.Status?.StatusID2 == BurstID && OwnHit(c)
            && (c.DamageEvent.DamageType == "Normal" || c.DamageEvent.DamageType == "Heavy"));
        BattleSpecialRules.RegisterCondition("Beidou_NormalHit", (arg, c) => string.IsNullOrEmpty(arg)
            && OwnHit(c) && c.DamageEvent.DamageType == "Normal");
        BattleSpecialRules.RegisterTargets("Beidou_HitTarget()", (battle, owner) =>
        {
            var c = ScriptHookContext.Current; var target = c?.DamageEvent?.Target;
            return c?.Caster == owner && OwnHit(c) && target?.IsAlive == true
                && battle?.GetEntityByPosition(target.Side, target.SlotPosition) == target
                ? new List<int> { target.SlotPosition } : new List<int>();
        });
        BattleSpecialRules.RegisterCount("Beidou_BurstBounces", owner => owner?.EntityID == 1018
            && owner.IsAlive && owner.HasStatus(BurstID) ? owner.ConstellationLevel >= 2 ? 5 : 3 : 0);
    }

    internal static bool Active(StatusInstance status) => status?.StatusID2 == GuardID && status.IsActive
        && status.Caster?.EntityID == 1018 && status.Caster.IsAlive && status.Caster.HasStatusInstance(status);

    private static bool EnemyHit(ScriptHookContext c)
    {
        var hit = c?.DamageEvent; var source = hit?.Source;
        return IncomingHit(c) && source.SourceKind == ReactionSourceKind.EnemySkill
            && c.Declaration?.Header.ActionExecutionId > 0;
    }

    private static bool IncomingHit(ScriptHookContext c)
    {
        var hit = c?.DamageEvent; var source = hit?.Source;
        return c?.Caster?.EntityID == 1018 && c.Caster.IsAlive && hit?.HitLanded == true
            && hit.Target == c.Caster && source?.CountsAsHit == true && source.SourceEntity != null
            && source.SourceSide != c.Caster.Side;
    }

    private static bool OwnHit(ScriptHookContext c)
    {
        var hit = c?.DamageEvent; var source = hit?.Source;
        return c?.Caster?.EntityID == 1018 && c.Caster.IsAlive && hit?.HitLanded == true
            && source?.CountsAsHit == true && source.SourceEntity == c.Caster
            && hit.Target != null && hit.Target.Side != c.Caster.Side;
    }

    internal static void DispatchBroken(StatusInstance status, BattleDeclaration cause)
    {
        if (!Active(status) || !(status.CountRuntimeState is BeidouGuardCount.Memory memory) || memory.Started) return;
        var owner = status.Caster; var data = DataManager.Instance;
        if (owner.CharacterCtrl == null || data == null || !data.StatusActionDict.TryGetValue(GuardID, out var actions)) return;
        foreach (var action in actions)
        {
            if (action.ActionType != "OnTrigger" || action.ScriptHook != BrokenHook) continue;
            BeidouGuardCount.Capture(status);
            if (!owner.CharacterCtrl.TryExecuteEventStatusAction(status, action, owner, new ScriptHookContext
            {
                Caster = owner, Status = status, StatusHost = owner, Target = owner, Declaration = cause
            }, () => Active(status))) memory.Started = false;
            return;
        }
    }
}

/// <summary>
/// 本次架盾专属整数Count：初值0、范围0～2。仅OnHit+Action/1配表行用Param2=Stack叠加。
/// 普通重新施放替换旧架盾及其具体子状态；叠层不刷新持续时间，不把三面盾做成共享盾量。
/// </summary>
public sealed class BeidouGuardCount : CountStatusHandler
{
    internal sealed class Memory
    {
        internal IDisposable Shields;
        internal bool Broken, Requested, Started;
        internal int Layers;
    }

    private static BattleEntity Owner(StatusInstance status, object host)
    {
        if (!(host is BattleEntity owner) || owner != status.Caster || owner.EntityID != 1018)
            throw new InvalidOperationException("捉浪架盾Count必须挂在北斗本人。");
        return owner;
    }

    public override void OnCreated(StatusInstance status, object host) => OnCreated(status, host, null);
    public override void OnCreated(StatusInstance status, object host, CountStatusApplication application)
    {
        var owner = Owner(status, host);
        if (!string.IsNullOrEmpty(application?.Param2))
            throw new InvalidOperationException("捉浪Stack只允许对已经存在的本次架盾叠层。");
        status.RuntimeIntValue = 0; status.StackCount = 1;
        var memory = new Memory(); status.CountRuntimeState = memory;
        memory.Shields = BattleEventHub.Subscribe<ShieldChangedEventData>(BattleEventKind.ShieldChanged, (_, fact) =>
        {
            if (fact.Reason != "Absorption" || memory.Requested || memory.Started) return;
            foreach (var before in fact.Before)
            {
                if (before.Value <= 0 || before.Kind != ShieldKind.Skill || before.SourceStatusId != BeidouScriptHooks.ShieldID) continue;
                bool owned = false;
                foreach (var child in status.BoundStatuses)
                    if (child.StatusID2 == BeidouScriptHooks.ShieldID && child.Host == fact.Subject
                        && child.ApplyOrder == before.SourceInstanceId && child.Caster == owner) { owned = true; break; }
                if (!owned) continue;
                bool remains = false;
                foreach (var after in fact.After) if (after.ShieldId == before.ShieldId && after.Value > 0) { remains = true; break; }
                if (remains) continue;
                memory.Broken = true; memory.Requested = true;
                var cause = BattleEventHub.Current;
                // 先结清同hit的OnHit叠层、死亡和子响应，再自动反击；不抢在叠层或其余目标前面。
                BattleResolutionContext.AfterResponses(() => BattleResolutionContext.AfterSettlement(
                    () => BeidouScriptHooks.DispatchBroken(status, cause)));
                break;
            }
        }, status.ApplyOrder, () => BeidouScriptHooks.Active(status));
    }

    internal static void Capture(StatusInstance status)
    {
        if (!(status.CountRuntimeState is Memory memory)) return;
        memory.Layers = memory.Broken && ScriptHookEvaluator.Evaluate("Check(1018_T1)", status.Caster)
            ? 2 : Math.Max(0, Math.Min(2, status.RuntimeIntValue));
        memory.Started = true; // 同一档同时决定反击倍率、4/6/8产能及满层天赋，响应中不重读。
    }

    public override void OnItsTurn(StatusInstance status, object host) => Capture(status);

    public override StatusInstance OnReapplied(StatusInstance status, BattleEntity host, CountStatusApplication application)
    {
        var owner = Owner(status, host);
        if (application.Param2 == "Stack")
        {
            if (status.CountRuntimeState is Memory memory && !memory.Started)
            {
                int before = status.RuntimeIntValue; status.RuntimeIntValue = Math.Min(2, before + 1);
                if (before != status.RuntimeIntValue)
                    BattleEventHub.Publish(new ResourceEventData("Count", BeidouScriptHooks.GuardID, owner,
                        before, status.RuntimeIntValue, 1, ResourceChangeReason.Gain, true, actor: owner));
            }
            return status;
        }
        if (!string.IsNullOrEmpty(application.Param2)) throw new ArgumentException("捉浪Param2只允许留空或Stack。");
        host.RemoveStatusInstance(status, "Replaced");
        return application.ApplyTo(host);
    }

    public override void OnRemoved(StatusInstance status, object host)
    {
        if (status.CountRuntimeState is Memory memory) memory.Shields?.Dispose();
        status.CountRuntimeState = null;
    }
}
