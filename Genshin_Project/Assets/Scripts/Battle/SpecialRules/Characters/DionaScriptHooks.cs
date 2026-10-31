using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

/// <summary>迪奥娜：蓄力位置绑定、猫爪真实命中、具体护盾与受益者生命条件。数值和效果由配表执行。</summary>
public static class DionaScriptHooks
{
    public const string Rules = "ST_Diona_Rules", Charge = "ST_Diona_Charge", ChargeTarget = "ST_Diona_ChargeTarget";
    public const string Shield = "ST_Diona_Shield", Mist = "ST_Diona_Mist";
    private sealed class Cast : IDisposable
    {
        internal readonly BattleEntity Owner;
        internal readonly string Skill;
        internal readonly List<StatusInstance> Markers = new List<StatusInstance>();
        private readonly Cast previous = Casting.Value;
        internal Cast(BattleEntity owner, SkillMainData skill) { Owner = owner; Skill = skill?.SkillID2; Casting.Value = this; }
        public void Dispose() => Casting.Value = previous;
    }
    private static readonly AsyncLocal<Cast> Casting = new AsyncLocal<Cast>();
    private static readonly ConditionalWeakTable<StatusInstance, IDisposable> Listeners = new ConditionalWeakTable<StatusInstance, IDisposable>();
    private static bool Active(StatusInstance status) => status?.IsActive == true && status.Caster?.EntityID == 1031
        && status.Caster.IsAlive && StatusHostAccess.Contains(status.Host, status);
    private static bool Empty(string value) => string.IsNullOrEmpty(value);
    private static bool Talent(BattleEntity owner, int index) => owner?.EntityID == 1031
        && ScriptHookEvaluator.Evaluate("Check(1031_T" + index + ")", owner);

    internal static void Register()
    {
        BattleSpecialRules.RegisterSkillExecutionScope((owner, skill) => new Cast(owner, skill));
        BattleSpecialRules.RegisterBattleReady(battle =>
        {
            foreach (var controller in battle.Allies)
            {
                var owner = controller?.Entity;
                if (owner?.EntityID != 1031 || !owner.IsAlive || owner.HasStatus(Rules)) continue;
                if (!DataManager.Instance.StatusMainDict.TryGetValue(Rules, out var main))
                    throw new InvalidOperationException("迪奥娜缺少猫爪命中规则状态。");
                var state = owner.AddStatus(Rules, owner, 0, 2, 0, main);
                controller.RecordCharacterInitializationStatus(state, 0);
            }
        });
        BattleSpecialRules.RegisterStatusLifecycle(ChargeTarget, (state, host) =>
        {
            if (Casting.Value?.Owner == state.Caster && Casting.Value.Skill == "SK_Heavy_Diona") Casting.Value.Markers.Add(state);
        }, null);
        BattleSpecialRules.RegisterStatusLifecycle(Charge, (state, host) =>
        {
            var cast = Casting.Value;
            if (cast?.Owner != state.Caster || cast.Skill != "SK_Heavy_Diona")
                throw new InvalidOperationException("迪奥娜蓄力必须保留本次手选位置。");
            foreach (var marker in cast.Markers)
                if (StatusHostAccess.Contains(marker.Host, marker)) state.BoundStatuses.Add(marker);
        }, null);
        BattleSpecialRules.RegisterStatusLifecycle(Shield, StartShield, Stop);
        BattleSpecialRules.RegisterCondition("Diona_PawHit", (a, c) => Empty(a) && Active(c?.Status)
            && c.Status.StatusID2 == Rules && IsPawHit(c));
        BattleSpecialRules.RegisterCondition("Diona_Shielded", (a, c) => Empty(a) && Active(c?.Status)
            && c.Status.StatusID2 == Shield && Talent(c.Caster, 1) && HasShield(c.Status));
        BattleSpecialRules.RegisterCondition("Diona_LowHP", (a, c) => Empty(a) && RecipientHP(c, false));
        BattleSpecialRules.RegisterCondition("Diona_HighHP", (a, c) => Empty(a) && RecipientHP(c, true));
    }

    private static bool IsPawHit(ScriptHookContext context)
    {
        var fact = (context.DamageEvent?.Declaration ?? context.Declaration)?.Get<DamageEventData>();
        return fact?.HitLanded == true && fact.Source?.CountsAsHit == true
            && fact.Source.SourceEntity == context.Caster && fact.Source.SourceEffectID == "SE_Skill_DionaPaws"
            && fact.Subject != null && fact.Subject.Side != context.Caster.Side;
    }

    private static bool RecipientHP(ScriptHookContext context, bool high)
    {
        if (!Active(context?.Status) || !(context.StatusHost is BattleEntity recipient)
            || recipient != context.Recipient || !recipient.IsAlive || recipient.Side != context.Caster.Side) return false;
        // 取这份增益的实际宿主；不用敌方命中目标，也不读迪奥娜自己的生命比例。
        return high ? recipient.CurrentHP > .5f * recipient.GetFinalHP()
            : recipient.CurrentHP <= .5f * recipient.GetFinalHP();
    }

    private static bool HasShield(StatusInstance status)
    {
        if (!(status.Host is BattleEntity target) || !target.IsAlive) return false;
        foreach (var shield in target.Shields)
            if (shield.Value > 0 && ReferenceEquals(shield.SourceStatus, status)) return true;
        return false;
    }

    private static void StartShield(StatusInstance status, object host)
    {
        if (!(host is BattleEntity recipient) || status.Caster?.EntityID != 1031 || recipient.Side != status.Caster.Side)
            throw new InvalidOperationException("冻冻猫爪护盾必须由迪奥娜施加给同队角色。");
        Listeners.Add(status, BattleEventHub.Subscribe<ShieldChangedEventData>(BattleEventKind.ShieldChanged, (_, fact) =>
        {
            if (fact.Subject != recipient || fact.Reason != "Absorption") return;
            bool existed = false, remains = false;
            foreach (var before in fact.Before) if (before.SourceInstanceId == status.ApplyOrder && before.Value > 0) existed = true;
            foreach (var after in fact.After) if (after.SourceInstanceId == status.ApplyOrder && after.Value > 0) remains = true;
            if (existed && !remains) recipient.RemoveStatusInstance(status, "ShieldBroken");
        }, status.ApplyOrder, () => Active(status)));
    }
    private static void Stop(StatusInstance status, object host)
    {
        if (Listeners.TryGetValue(status, out var listener)) listener.Dispose();
        Listeners.Remove(status);
    }
}
