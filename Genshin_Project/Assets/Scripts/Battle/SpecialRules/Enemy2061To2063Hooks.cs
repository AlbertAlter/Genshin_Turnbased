using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

/// <summary>核心数值由各表的 Count 效果修改。眩晕、蓄力、升空模式及其持续时间由状态行动执行。</summary>
public static class Enemy2061To2063Hooks
{
    public const string Core = "ST_CoreProtection_Enemy";
    public const string Paralysis = "ST_CoreParalysis_Enemy";
    private const string FlightWait = "ST_RuinHunter_FlightWait";
    private const string ChargeEffect = "SE_GeovishapHatchling_Charge_Damage";
    private const string ChargeSkill = "SK_GeovishapHatchling_Charge";
    private sealed class ChargeContact
    {
        internal BattleEntity Attacker;
        internal BattleEntity ProtectedTarget;
        internal long HitId;
        internal bool HitShield;
    }

    // 仅属于本次攻击的临时记录。盾在第一击破碎时也能在两击完成后触发一次自眩晕。
    private static readonly ConditionalWeakTable<BattleResolutionContext.CounterScope, ChargeContact> Charges =
        new ConditionalWeakTable<BattleResolutionContext.CounterScope, ChargeContact>();
    private static readonly ConditionalWeakTable<StatusInstance, IDisposable> StunRemovals =
        new ConditionalWeakTable<StatusInstance, IDisposable>();

    internal static void Register()
    {
        BattleSpecialRules.RegisterCondition("CoreAttackHit", (_, c) => QualifiesForCore(c));
        BattleSpecialRules.RegisterCondition("CoreDepleted", (_, c) => c?.Caster?.GetStatus(Core)?.RuntimeIntValue == 0
            && !c.Caster.HasStatus(Paralysis));
        BattleSpecialRules.RegisterCondition("RuinGuardOwner", (_, c) => c?.Caster?.EntityID == 2062);
        BattleSpecialRules.RegisterCondition("RuinHunterOwner", (_, c) => c?.Caster?.EntityID == 2063);
        BattleSpecialRules.RegisterCondition("RuinLastGuaranteed", (_, c) => c?.Caster?.EnemyCtrl?.IsLastGuaranteedAction == true);
        BattleSpecialRules.RegisterCondition("RuinCanCharge", (_, c) => c?.Caster?.IsAlive == true && !c.Caster.HasStatus(Paralysis));
        BattleSpecialRules.RegisterCondition("HunterFlightDue", (_, c) => c?.Caster?.IsAlive == true && !c.Caster.HasStatus(FlightWait));
        BattleSpecialRules.RegisterCondition("HunterGroundAttack", (_, c) => c?.Caster?.HasStatus(FlightWait) == true);
        BattleSpecialRules.RegisterStatusLifecycle(FlightWait, (s, _) => {
            s.EarliestTimerTurn = Math.Max(1, BattleManager.Instance?.TurnCount ?? 1) + 1;
        }, null);
        BattleSpecialRules.RegisterStatusLifecycle(Paralysis, (s, host) => {
            var battle = BattleManager.Instance;
            if (battle != null && (int)battle.CurrentPhase == 6 && s.AddInPhase == 6)
                s.EarliestTimerTurn = battle.TurnCount + 1;
            if (!(host is BattleEntity owner)) return;
            // 只跟随这次绑定的眩晕。若它被提前解除，正常结束父状态，让表里的恢复行动结算。
            StunRemovals.Add(s, BattleEventHub.Subscribe<StatusEventData>(BattleEventKind.StatusRemoved, (_, fact) => {
                if (ReferenceEquals(fact.Host, owner) && fact.StatusId == PublicControlStates.EnemyStun
                    && s.BoundStatuses.Exists(child => child.ApplyOrder == fact.InstanceId))
                    owner.RemoveStatusInstance(s, "CoreStunEnded");
            }, s.ApplyOrder, () => owner.IsAlive && s.IsActive && !s.DeathCleanupPending));
        }, (s, _) => {
            if (StunRemovals.TryGetValue(s, out var listener)) listener.Dispose();
            StunRemovals.Remove(s);
        });
        BattleSpecialRules.RegisterEffectInvocationScope((actor, effect, _) => {
            var action = BattleResolutionContext.CurrentChain;
            if (effect == ChargeEffect && action != null)
            {
                Charges.Remove(action);
                Charges.Add(action, new ChargeContact { Attacker = actor });
            }
            return null;
        });
        BattleSpecialRules.RegisterHitScope((hit, actor, targets) => {
            if (actor?.EntityID != 2061 || DamageQueryContext.ResolveSkillID(ChargeEffect, null) != ChargeSkill) return null;
            if (!TryGetCharge(actor, out var charge)) return null;
            charge.HitId = hit;
            charge.ProtectedTarget = null;
            foreach (var target in targets)
                if (target.Side == BattleSide.Ally && target.HasShieldProtection()) { charge.ProtectedTarget = target; break; }
            return null;
        });
        BattleSpecialRules.RegisterDeclarationObserver(d => {
            if (!(d.Data is DamageEventData hit) || !hit.HitLanded) return;
            var action = BattleResolutionContext.CurrentChain;
            if (action == null || !Charges.TryGetValue(action, out var charge) || hit.Subject != charge.ProtectedTarget
                || d.Header.HitId != charge.HitId) return;
            var source = hit.Source;
            if (source.SourceEntity == charge.Attacker && source.SourceEffectID == ChargeEffect) charge.HitShield = true;
        });
        BattleSpecialRules.RegisterCondition("HatchlingHitShield", (_, c) => TryGetCharge(c?.Caster, out var charge) && charge.HitShield);
    }

    private static bool TryGetCharge(BattleEntity owner, out ChargeContact contact)
    {
        contact = null;
        var action = BattleResolutionContext.CurrentChain;
        return owner != null && action != null && Charges.TryGetValue(action, out contact) && contact.Attacker == owner;
    }

    private static bool QualifiesForCore(ScriptHookContext context)
    {
        var owner = context?.Caster;
        var hit = context?.DamageEvent;
        var source = hit?.Source;
        return owner?.IsAlive == true && context.Status?.RuntimeIntValue > 0 && !owner.HasStatus(Paralysis)
            && hit?.Target == owner && hit.HitLanded && source?.CountsAsHit == true
            && source.SourceSide == BattleSide.Ally
            && source.SourceEntity?.Type == BattleEntity.EntityType.Character
            && (hit.Declaration?.Header.ExecutionScopeId ?? 0) > 0;
    }

    internal sealed class CoreCounter : CountStatusHandler
    {
        public override bool UseStandardApplication => true;
        public override bool SupportsEffectChange => true;
        public override bool EffectUsesIntegerValue => true;
        public override void OnCreated(StatusInstance status, object host) => status.RuntimeIntValue = 0;
        public override void ValidateEffectChange(StatusInstance status, BattleEntity host, CountChangeRequest request)
        {
            base.ValidateEffectChange(status, host, request);
            if (request.Operation == CountChangeOperation.Full)
                throw new InvalidOperationException("核心防护没有满层值，补充层数请配置 Count 增量。");
        }
        public override void ChangeFromEffect(StatusInstance status, BattleEntity host, CountChangeRequest request)
            => request.ApplyInteger(status, host, 0, int.MaxValue);
    }
}
