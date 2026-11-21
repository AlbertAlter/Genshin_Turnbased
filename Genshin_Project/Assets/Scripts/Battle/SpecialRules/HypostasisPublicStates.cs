using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>公共外壳、显露与复苏。穿壳资格属于本次Execution，余额始终由Count效果修改。</summary>
public static class HypostasisPublicStates
{
    public const string Shell = "ST_Hypostasis_ElementShell";
    public const string Exposure = "ST_Hypostasis_CoreExposure";
    public const string Recovery = "ST_Hypostasis_Recovery";
    public const string WindPrisms = "ST_AnemoHypostasis_RecoveryShell";
    public const string ElectroPrisms = "ST_ElectroHypostasis_RecoveryShell";
    public const string GeoShield = "ST_GeoHypostasis_RecoveryShield";
    private static readonly ConditionalWeakTable<BattleResolutionContext.CounterScope, Dictionary<StatusInstance, bool>> Windows =
        new ConditionalWeakTable<BattleResolutionContext.CounterScope, Dictionary<StatusInstance, bool>>();
    private static readonly ConditionalWeakTable<StatusInstance, IDisposable> Reactions =
        new ConditionalWeakTable<StatusInstance, IDisposable>();

    internal static void Register()
    {
        BattleSpecialRules.RegisterEnemyActionBlocker(owner => owner?.HasStatus(Recovery) == true);
        BattleSpecialRules.RegisterCondition("HypostasisShellApplies", (_, c) => !CanBypass(c?.Target, c?.DamageAttacker));
        BattleSpecialRules.RegisterCondition("HypostasisExposedDamage", (_, c) => c?.DamageEvent != null
            && (c.DamageEvent.HitLanded || c.DamageEvent.ActualHPDamage > 0f)
            && c.DamageEvent.Target == c.Caster && c.DamageEvent.Source?.SourceSide == BattleSide.Ally
            && c.Status?.RuntimeIntValue > 0 && CanBypass(c.Caster, c.DamageEvent.Source.SourceEntity));
        BattleSpecialRules.RegisterCondition("HypostasisExposureEmpty", (_, c) => c?.Caster?.GetStatus(Exposure)?.RuntimeIntValue == 0);
        BattleSpecialRules.RegisterCondition("HypostasisWind", (_, c) => ElementQuery.ReadIntrinsic(c?.Caster, "Anemo") != null);
        BattleSpecialRules.RegisterCondition("HypostasisElectro", (_, c) => ElementQuery.ReadIntrinsic(c?.Caster, "Electro") != null);
        BattleSpecialRules.RegisterCondition("HypostasisGeo", (_, c) => ElementQuery.ReadIntrinsic(c?.Caster, "Geo") != null);
        BattleSpecialRules.RegisterCondition("HypostasisPrismsEmpty", (_, c) => c?.Status?.RuntimeIntValue == 0);
        BattleSpecialRules.RegisterCondition("HypostasisPrismReaction", (_, c) => IsPrismReaction(c?.Status, c?.Caster, c?.Declaration?.Data as ReactionEventData));
        BattleSpecialRules.RegisterStatusLifecycle(Recovery, OnRecoveryCreated, StopReactions);
        BattleSpecialRules.RegisterStatusLifecycle(GeoShield, null, (_, host) => {
            if (!(host is BattleEntity owner) || !owner.IsAlive) return;
            var recovery = owner.GetStatus(Recovery);
            if (recovery?.IsActive == true && recovery.NaturalEndDepth == 0)
                owner.RemoveStatusInstance(recovery, "RecoveryShieldEnded");
        });
        BattleSpecialRules.RegisterStatusHeal("HypostasisRecoveryHeal()", HealRemainingShells);
        BattleSpecialRules.RegisterFixedStatusAmount("HypostasisPillarCollapse()", (_, status, host) =>
            (host as BattleEntity ?? status.Caster).CurrentHP);
    }

    public static bool CanBypass(BattleEntity target, BattleEntity attacker)
    {
        if (target == null || attacker?.Side != BattleSide.Ally) return false;
        var exposure = target.GetStatus(Exposure);
        if (exposure?.IsActive != true) return false;
        var scope = BattleResolutionContext.CurrentExecution;
        if (scope == null) return exposure.RuntimeIntValue > 0;
        var windows = Windows.GetOrCreateValue(scope);
        if (!windows.TryGetValue(exposure, out bool eligible))
        {
            eligible = exposure.RuntimeIntValue > 0;
            windows.Add(exposure, eligible);
        }
        return eligible;
    }

    private static void OnRecoveryCreated(StatusInstance status, object host)
    {
        if (!(host is BattleEntity owner)) return;
        Enemy2064To2066Hooks.MarkRecoveryUsed(owner);
        var battle = BattleManager.Instance;
        if (battle != null && (int)battle.CurrentPhase == 6 && status.AddInPhase == 6)
            status.EarliestTimerTurn = battle.TurnCount + 1;
        Reactions.Add(status, BattleEventHub.Subscribe<ReactionEventData>(BattleEventKind.ReactionOccurred, (_, fact) => {
            if (!owner.IsAlive || !status.IsActive || status.DeathCleanupPending) return;
            string id = owner.HasStatus(WindPrisms) ? WindPrisms : ElectroPrisms;
            var prisms = owner.GetStatus(id);
            if (!IsPrismReaction(prisms, owner, fact)) return;
            var dm = DataManager.Instance;
            if (!dm.StatusActionDict.TryGetValue(id, out var actions)) return;
            foreach (var action in actions)
                if (action.ActionType == "OnTrigger" && action.ScriptHook == "HypostasisPrismReaction()")
                    owner.EnemyCtrl.TryExecuteEventStatusAction(prisms, action, owner,
                        new ScriptHookContext { Caster = owner, Target = owner, Status = prisms, Declaration = BattleEventHub.Current });
        }, status.ApplyOrder, () => owner.IsAlive && status.IsActive && !status.DeathCleanupPending));
    }

    private static bool IsPrismReaction(StatusInstance prisms, BattleEntity owner, ReactionEventData reaction)
    {
        if (prisms?.IsActive != true || prisms.RuntimeIntValue <= 0 || reaction?.Subject != owner
            || reaction.SourceSide != BattleSide.Ally || reaction.Reaction == ReactionType.None) return false;
        string element = prisms.StatusID2 == WindPrisms ? "Anemo" : "Electro";
        return reaction.Elements.Contains(element);
    }

    private static void StopReactions(StatusInstance status, object host)
    {
        if (Reactions.TryGetValue(status, out var subscription)) subscription.Dispose();
        Reactions.Remove(status);
    }

    private static Task HealRemainingShells(StatusHealExecution context)
    {
        var owner = context.Host as BattleEntity ?? context.Status.Caster;
        if (owner == null || !owner.IsAlive) return Task.CompletedTask;
        float fraction;
        if (owner.HasStatus(WindPrisms)) fraction = owner.GetStatus(WindPrisms).RuntimeIntValue * .15f;
        else if (owner.HasStatus(ElectroPrisms)) fraction = owner.GetStatus(ElectroPrisms).RuntimeIntValue * .15f;
        else
        {
            var shield = owner.GetEnemyElementalShield();
            fraction = shield?.SourceStatus?.StatusID2 == GeoShield ? shield.Value * .03f : 0f;
        }
        if (fraction <= 0f) return Task.CompletedTask;
        return context.ExecuteDefault(new List<BattleEntity> { owner },
            fraction.ToString("R", CultureInfo.InvariantCulture) + "*TotalHP", null);
    }

    internal sealed class Counter : CountStatusHandler
    {
        public override bool UseStandardApplication => true;
        public override bool SupportsEffectChange => true;
        public override bool EffectUsesIntegerValue => true;
        public override void OnCreated(StatusInstance status, object host) => status.RuntimeIntValue = 0;
        public override void ValidateEffectChange(StatusInstance status, BattleEntity host, CountChangeRequest request)
        {
            base.ValidateEffectChange(status, host, request);
            if (request.Operation == CountChangeOperation.Full)
                throw new InvalidOperationException("无相计数没有固定满值，请使用Count增减。");
        }
        public override void ChangeFromEffect(StatusInstance status, BattleEntity host, CountChangeRequest request)
            => request.ApplyInteger(status, host, 0, int.MaxValue);
    }
}
