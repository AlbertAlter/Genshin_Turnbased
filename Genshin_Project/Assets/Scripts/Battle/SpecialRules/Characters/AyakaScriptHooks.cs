using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

/// <summary>绫华：配表负责数值、状态与冷却效果；此处记录完整行动、爆发选定位置及两回合授予时钟。</summary>
public static class AyakaScriptHooks
{
    public const int CharacterID = 1041;
    public const string Root = "ST_Ayaka_Senho", Infusion = "ST_Ayaka_CryoInfusion";
    public const string C1Counter = "ST_Ayaka_C1Counter", C6Rule = "ST_Ayaka_C6Rule", ThinIce = "ST_Ayaka_UsurahiButou";
    private static readonly AsyncLocal<Cast> Casting = new AsyncLocal<Cast>();
    private static readonly ConditionalWeakTable<StatusInstance, GrantClock> Clocks = new ConditionalWeakTable<StatusInstance, GrantClock>();
    private sealed class GrantClock { internal int NextTurn; }
    private sealed class EmptyScope : IDisposable { public void Dispose() { } }
    private static readonly IDisposable Empty = new EmptyScope();
    private sealed class Cast : IDisposable
    {
        internal readonly BattleEntity Owner;
        internal readonly string Skill;
        internal readonly StatusInstance HeavyBuff;
        internal int Center;
        private readonly Cast previous = Casting.Value;
        internal Cast(BattleEntity owner, SkillMainData skill)
        { Owner = owner; Skill = skill.SkillID2; HeavyBuff = skill.ActionType == "Heavy" ? owner.GetStatus(ThinIce) : null; Casting.Value = this; }
        public void Dispose() => Casting.Value = previous;
    }
    internal static bool Active(StatusInstance status) => status?.IsActive == true && status.Caster?.IsAlive == true
        && StatusHostAccess.Contains(status.Host, status);
    internal static BattleEntity Require(StatusInstance status, object host)
    {
        if (host is not BattleEntity owner || owner.EntityID != CharacterID || status.Caster != owner)
            throw new InvalidOperationException("绫华专属自身状态必须由本人持有。");
        return owner;
    }
    private static int Turn => Math.Max(1, BattleManager.Instance?.TurnCount ?? 1);
    private static bool NoArgument(string text) => string.IsNullOrEmpty(text);
    private static Cast Current(BattleEntity owner) => Casting.Value?.Owner == owner ? Casting.Value : null;
    internal static void Register()
    {
        BattleSpecialRules.RegisterSkillExecutionScope((owner, skill) => owner?.EntityID == CharacterID && skill != null ? new Cast(owner, skill) : Empty);
        BattleSpecialRules.RegisterEffectInvocationScope(CaptureBurstCenter);
        BattleSpecialRules.RegisterSkillSettled(Settled);
        BattleSpecialRules.RegisterAllyTurnStarted(battle => {
            foreach (var ally in battle.Allies)
            {
                if (ally?.Entity?.EntityID != CharacterID || !ally.Entity.IsAlive) continue;
                if (ally.IsActive && ally.Entity.IsOnField)
                    Dispatch(ally.Entity.GetStatus(Root), "Ayaka_AllyTurnStart()", null, "Ayaka_AllyTurnStart");
                Dispatch(ally.Entity.GetStatus(C6Rule), "Ayaka_C6Due()", null);
            }
        });
        BattleSpecialRules.RegisterCondition("Ayaka_AllyTurnStart", (a, c) => NoArgument(a) && c?.Caster?.EntityID == CharacterID
            && c.Caster.CharacterCtrl?.IsActive == true && c.Caster.IsOnField && c.EventHookName == "Ayaka_AllyTurnStart");
        BattleSpecialRules.RegisterCondition("Ayaka_NoC2", (a, c) => NoArgument(a) && c?.Caster?.EntityID == CharacterID && c.Caster.ConstellationLevel < 2);
        BattleSpecialRules.RegisterCondition("Ayaka_AttackSettled", (a, c) => NoArgument(a) && AttackEnded(c, false));
        BattleSpecialRules.RegisterCondition("Ayaka_HeavySettled", (a, c) => NoArgument(a) && AttackEnded(c, true));
        BattleSpecialRules.RegisterCondition("Ayaka_C1Full", (a, c) => NoArgument(a) && c?.Caster?.EntityID == CharacterID
            && c.Status?.StatusID2 == C1Counter && c.Status.RuntimeIntValue >= 6);
        BattleSpecialRules.RegisterCondition("Ayaka_C6Due", (a, c) => NoArgument(a) && c?.Status?.StatusID2 == C6Rule
            && Active(c.Status) && Clocks.TryGetValue(c.Status, out var clock) && Turn >= clock.NextTurn);
        BattleSpecialRules.RegisterCondition("Ayaka_BurstHit", (a, c) => NoArgument(a) && c?.Caster?.EntityID == CharacterID
            && c.DamageEvent?.HitLanded == true && c.DamageEvent.Source?.SourceEntity == c.Caster
            && c.DamageEvent.Target?.Side != c.Caster.Side && IsBurstDamage(c.DamageEvent.Source.SourceEffectID));
        BattleSpecialRules.RegisterTargets("Ayaka_BurstCenter()", (_, owner) => new List<int> { BurstCenter(owner) });
        BattleSpecialRules.RegisterTargets("Ayaka_BurstArea()", (_, owner) => {
            int center = BurstCenter(owner); var side = owner.Side == BattleSide.Ally ? BattleSide.Enemy : BattleSide.Ally;
            return BattlePositionSystem.GetAdjacentPositions(side, center, 1);
        });
        BattleSpecialRules.RegisterTargets("Ayaka_HitTarget()", (battle, owner) => {
            var target = ScriptHookContext.Current?.DamageEvent?.Target;
            return target?.IsAlive == true && target.Side != owner.Side && battle.GetEntityByPosition(target.Side, target.SlotPosition) == target
                ? new List<int> { target.SlotPosition } : new List<int>();
        });
        BattleSpecialRules.RegisterStatusLifecycle(Infusion, (status, host) => {
            var owner = Require(status, host);
            if (ScriptHookEvaluator.Evaluate("Check(1041_T2)", owner))
                BattleManager.Instance?.APManager?.RecoverAP(5, allowOverflow: true, actor: owner);
        }, null);
        BattleSpecialRules.RegisterStatusLifecycle(C6Rule, (status, host) => {
            Require(status, host); Clocks.Add(status, new GrantClock { NextTurn = Turn });
        }, null);
        BattleSpecialRules.RegisterStatusLifecycle(ThinIce, (status, host) => {
            var owner = Require(status, host); var rule = owner.GetStatus(C6Rule);
            if (Active(rule) && Clocks.TryGetValue(rule, out var clock)) clock.NextTurn = Turn + 2;
        }, null);
    }
    private static bool AttackEnded(ScriptHookContext context, bool heavyOnly)
    {
        var end = context?.Declaration?.Get<ActionEventData>();
        return context?.Caster?.EntityID == CharacterID && context.EventHookName == (heavyOnly ? "Ayaka_HeavySettled()" : "Ayaka_AttackSettled()")
            && context.Declaration?.Kind == BattleEventKind.ActionCompleted
            && end?.Subject == context.Caster && (heavyOnly ? end.ActionType == "Heavy" : end.ActionType is "Normal" or "Heavy");
    }
    private static bool IsBurstDamage(string id) => id is "SE_Burst_AyakaCut" or "SE_Burst_AyakaCutC2"
        or "SE_Burst_AyakaFinalCut" or "SE_Burst_AyakaFinalCutC2" or "SE_Burst_AyakaBloom";
    private static int BurstCenter(BattleEntity owner)
    {
        var cast = Current(owner);
        if (cast?.Skill != "SK_Burst_Ayaka" || cast.Center <= 0) throw new InvalidOperationException("神里流·霜灭缺少本次锁定位置。");
        return cast.Center;
    }
    private static IDisposable CaptureBurstCenter(BattleEntity owner, string effect, long execution)
    {
        var cast = Current(owner);
        bool first = effect == "SE_Burst_AyakaCut" && owner?.ConstellationLevel < 2
            || effect == "SE_Burst_AyakaCutC2" && owner?.ConstellationLevel >= 2;
        if (cast?.Skill == "SK_Burst_Ayaka" && cast.Center == 0 && first)
        {
            var side = owner.Side == BattleSide.Ally ? BattleSide.Enemy : BattleSide.Ally;
            cast.Center = owner.CharacterCtrl.ForcedTargetPositions?.FirstOrDefault(p => BattlePositionSystem.IsValidPosition(side, p)) ?? 0;
            if (cast.Center == 0)
            {
                var result = BattleTargetResolver.Resolve(BattleManager.Instance, owner, owner, "Enemy", 1, 1,
                    null, null, owner.CharacterCtrl.ForcedTargetPositions, null);
                if (result.IsValid && result.Positions.Count > 0) cast.Center = result.Positions[0];
            }
        }
        return Empty;
    }
    private static void Settled(BattleEntity owner, SkillMainData skill, BattleDeclaration cause)
    {
        if (owner?.EntityID != CharacterID || !owner.IsAlive || skill?.ActionType is not ("Normal" or "Heavy")) return;
        Dispatch(owner.GetStatus(C1Counter), "Ayaka_AttackSettled()", cause);
        var cast = Current(owner);
        if (skill.ActionType == "Heavy" && cast?.Skill == skill.SkillID2 && Active(cast.HeavyBuff))
            Dispatch(cast.HeavyBuff, "Ayaka_HeavySettled()", cause);
    }
    private static void Dispatch(StatusInstance status, string hook, BattleDeclaration cause, string eventHook = null)
    {
        var owner = status?.Caster; var data = DataManager.Instance;
        if (!Active(status) || owner?.CharacterCtrl == null || data == null || !data.StatusActionDict.TryGetValue(status.StatusID2, out var actions)) return;
        foreach (var action in actions)
            if (action.ActionType == "OnTrigger" && action.ScriptHook == hook)
                owner.CharacterCtrl.TryExecuteEventStatusAction(status, action, status.Host, new ScriptHookContext {
                    Caster = owner, Status = status, StatusHost = status.Host, Declaration = cause, EventHookName = eventHook ?? hook
                }, () => Active(status));
    }
}

/// <summary>雪晴樱吹雪：初值0，0～6整数；跨回合保留，仅由配表Count增减，六次完整行为后清零。</summary>
public sealed class AyakaAttackCounter : CountStatusHandler
{
    public override bool UseStandardApplication => true;
    public override bool SupportsEffectChange => true;
    public override bool EffectUsesIntegerValue => true;
    public override void OnCreated(StatusInstance status, object host)
    { AyakaScriptHooks.Require(status, host); status.RuntimeIntValue = 0; status.StackCount = 1; }
    public override void ChangeFromEffect(StatusInstance status, BattleEntity host, CountChangeRequest request)
    { AyakaScriptHooks.Require(status, host); ValidateEffectChange(status, host, request); request.ApplyInteger(status, host, 0, 6); }
}
