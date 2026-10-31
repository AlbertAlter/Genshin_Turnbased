using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;

/// <summary>辛焱：真实命中人数选盾档；每名角色独立护盾，只有本人护盾喷火。数值和效果顺序由配表管理。</summary>
public static class XinyanScriptHooks
{
    public const string Shield = "ST_Xinyan_Shield", CriticalRule = "ST_Xinyan_C1Rule";
    private sealed class Cast : IDisposable
    {
        internal readonly BattleEntity Owner;
        internal readonly string Skill;
        internal readonly HashSet<BattleEntity> Hits = new HashSet<BattleEntity>();
        private readonly Cast previous = Casting.Value;
        private readonly IDisposable listener;
        internal Cast(BattleEntity owner, SkillMainData skill)
        {
            Owner = owner; Skill = skill?.SkillID2; Casting.Value = this;
            if (owner?.EntityID == 1033 && Skill == "SK_Skill_Xinyan")
                listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, fact) =>
                {
                    if (fact.HitLanded && fact.Source?.CountsAsHit == true && fact.Source.SourceEntity == owner
                        && fact.Source.SourceEffectID == "SE_Skill_Xinyan" && fact.Subject?.Side != owner.Side)
                        Hits.Add(fact.Subject);
                }, 0, () => Casting.Value == this);
        }
        public void Dispose() { listener?.Dispose(); Casting.Value = previous; }
    }
    private static readonly AsyncLocal<Cast> Casting = new AsyncLocal<Cast>();
    private static bool Active(StatusInstance status) => status?.IsActive == true && status.Caster?.EntityID == 1033
        && status.Caster.IsAlive && StatusHostAccess.Contains(status.Host, status);

    internal static void Register()
    {
        BattleSpecialRules.RegisterSkillExecutionScope((owner, skill) => new Cast(owner, skill));
        BattleSpecialRules.RegisterStatusLifecycle(Shield, StartShield, Stop);
        BattleSpecialRules.RegisterStatusLifecycle(CriticalRule, StartCriticalRule, Stop);
        BattleSpecialRules.RegisterCondition("Xinyan_ShieldTier", (arg, context) => context?.Status?.StatusID2 == Shield
            && int.TryParse(arg, out int tier) && tier >= 1 && tier <= 3 && context.Status.RuntimeIntValue == tier);
        BattleSpecialRules.RegisterCondition("Xinyan_OwnShield", (arg, context) => string.IsNullOrEmpty(arg)
            && Active(context?.Status) && context.Status.StatusID2 == Shield && context.Status.Host == context.Caster
            && HasShield(context.Status));
        BattleSpecialRules.RegisterCondition("Xinyan_Critical", (arg, context) => string.IsNullOrEmpty(arg)
            && Active(context?.Status) && context.Status.StatusID2 == CriticalRule
            && OwnCritical(context.Caster, context.Declaration?.Get<DamageEventData>()));
        BattleSpecialRules.RegisterTargets("Xinyan_SwingTargets()", (battle, owner) =>
        {
            var cast = Casting.Value;
            if (owner?.EntityID != 1033 || cast?.Owner != owner || cast.Skill != "SK_Skill_Xinyan") return new List<int>();
            return cast.Hits.Where(target => target.IsAlive && battle.GetEntityByPosition(target.Side, target.SlotPosition) == target)
                .Select(target => target.SlotPosition).OrderBy(slot => slot).ToList();
        });
        BattleSpecialRules.RegisterBuffScalar("Xinyan_Defense()", (effect, state) =>
        {
            if (state?.Caster?.EntityID != 1033 || !float.TryParse(effect.Param1, NumberStyles.Float,
                CultureInfo.InvariantCulture, out float ratio)) throw new InvalidOperationException("辛焱防御转攻击缺少合法系数。");
            return ratio * state.Caster.GetFinalDEF();
        });
    }

    // 二命是本段最终暴击率固定100%，不是向面板加100%；仍走统一逐目标暴击抽取与日志。
    public static bool ForcesCritical(BattleEntity owner, string effectID, string element)
        => owner?.EntityID == 1033 && owner.ConstellationLevel >= 2
            && effectID == "SE_Burst_Xinyan" && string.Equals(element, "Physical", StringComparison.OrdinalIgnoreCase);

    public static int SelectShieldTier(int hitCount, bool firstTalent)
        => hitCount >= (firstTalent ? 3 : 4) ? 3 : hitCount >= (firstTalent ? 2 : 3) ? 2 : 1;

    private static void StartShield(StatusInstance state, object host)
    {
        var owner = state.Caster; var cast = Casting.Value;
        if (!(host is BattleEntity recipient) || owner?.EntityID != 1033 || recipient.Side != owner.Side
            || cast?.Owner != owner) throw new InvalidOperationException("热情拂扫须在辛焱本次施放内给同队角色施加。");
        if (cast.Skill == "SK_Burst_Xinyan" && owner.ConstellationLevel >= 2) state.RuntimeIntValue = 3;
        else if (cast.Skill == "SK_Skill_Xinyan")
            state.RuntimeIntValue = SelectShieldTier(cast.Hits.Count, ScriptHookEvaluator.Evaluate("Check(1033_T1)", owner));
        else throw new InvalidOperationException("热情拂扫没有合法的战技或二命施放来源。");
        // 盾档是这份普通Shield实例的生成数据，不是可加减的Count。
        state.ScriptRuntimeState = BattleEventHub.Subscribe<ShieldChangedEventData>(BattleEventKind.ShieldChanged, (_, fact) =>
        {
            if (fact.Subject != recipient || fact.Reason != "Absorption") return;
            bool existed = fact.Before.Any(s => s.SourceInstanceId == state.ApplyOrder && s.Value > 0);
            bool remains = fact.After.Any(s => s.SourceInstanceId == state.ApplyOrder && s.Value > 0);
            if (existed && !remains) recipient.RemoveStatusInstance(state, "ShieldBroken");
        }, state.ApplyOrder, () => Active(state));
    }
    private static bool HasShield(StatusInstance state) => state.Host is BattleEntity target && target.IsAlive
        && target.Shields.Any(shield => shield.Value > 0 && ReferenceEquals(shield.SourceStatus, state));
    private static bool OwnCritical(BattleEntity owner, DamageEventData fact)
        => fact?.HitLanded == true && fact.Critical?.IsCritical == true && fact.Source?.CountsAsHit == true
            && fact.Source.SourceEntity == owner && fact.Subject != null && fact.Subject.Side != owner.Side;
    private static void StartCriticalRule(StatusInstance state, object host)
    {
        if (host != state.Caster || state.Caster?.EntityID != 1033)
            throw new InvalidOperationException("辛焱一命监听状态必须初始化在本人身上。");
        state.ScriptRuntimeState = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, fact) =>
        {
            if (!OwnCritical(state.Caster, fact) || !DataManager.Instance.StatusActionDict.TryGetValue(CriticalRule, out var actions)) return;
            foreach (var action in actions)
                if (action.ActionType == "OnTrigger" && action.ScriptHook == "Xinyan_Critical()")
                    state.Caster.CharacterCtrl.TryExecuteEventStatusAction(state, action, state.Host,
                        new ScriptHookContext { Caster = state.Caster, Status = state, StatusHost = state.Host,
                            Declaration = BattleEventHub.Current }, () => Active(state));
        }, state.ApplyOrder, () => Active(state));
    }
    private static void Stop(StatusInstance state, object host)
    { (state.ScriptRuntimeState as IDisposable)?.Dispose(); state.ScriptRuntimeState = null; }
}
