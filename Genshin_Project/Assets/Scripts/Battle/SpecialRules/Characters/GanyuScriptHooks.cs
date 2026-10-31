using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;

/// <summary>甘雨：蓄力位置绑定、即时射击、冰莲唯一性及具体命中身份。伤害与资源变动仍由配表执行。</summary>
public static class GanyuScriptHooks
{
    public const string Charge = "ST_Ganyu_Charge", FrostCharge = "ST_Ganyu_FrostCharge";
    public const string Marker = "ST_Ganyu_ChargeTarget", Lotus = "ST_Ganyu_Lotus", Shower = "ST_Ganyu_Shower";
    public const string Ready = "ST_Ganyu_C6Ready", C4 = "ST_Ganyu_C4", C4Bonus = "ST_Ganyu_C4Bonus";
    private static readonly AsyncLocal<Cast> Casting = new AsyncLocal<Cast>();
    private static readonly AsyncLocal<Response> Responding = new AsyncLocal<Response>();
    private sealed class Cast : IDisposable
    {
        internal readonly BattleEntity Owner;
        internal readonly string Skill;
        internal readonly List<StatusInstance> Markers = new List<StatusInstance>();
        private readonly Cast previous = Casting.Value;
        internal Cast(BattleEntity owner, SkillMainData skill) { Owner = owner; Skill = skill?.SkillID2; Casting.Value = this; }
        public void Dispose() => Casting.Value = previous;
    }
    private sealed class Response : IDisposable
    {
        internal readonly StatusInstance State;
        internal bool LotusHit;
        private readonly Response previous = Responding.Value;
        internal Response(StatusInstance state) { State = state; Responding.Value = this; }
        public void Dispose() => Responding.Value = previous;
    }
    private static bool Empty(string value) => string.IsNullOrEmpty(value);
    internal static bool Active(StatusInstance state) => state?.IsActive == true && state.Caster?.IsAlive == true
        && StatusHostAccess.Contains(state.Host, state);
    internal static void Register()
    {
        BattleSpecialRules.RegisterSkillExecutionScope((owner, skill) => new Cast(owner, skill));
        BattleSpecialRules.RegisterStatusActionScope(state => new Response(state));
        BattleSpecialRules.RegisterStatusLifecycle(Marker, (state, host) =>
        {
            var cast = Casting.Value;
            if (cast?.Owner == state.Caster && (cast.Skill == "SK_Heavy_Ganyu1" || cast.Skill == "SK_Heavy_Ganyu2"))
                cast.Markers.Add(state);
        }, null);
        foreach (string id in new[] { Charge, FrostCharge })
            BattleSpecialRules.RegisterStatusLifecycle(id, BindCharge, null);
        BattleSpecialRules.RegisterStatusLifecycle(Lotus, (state, host) =>
        {
            if (!(host is FieldPosition field) || state.Caster?.EntityID != 1035 || field.Side == state.Caster.Side)
                throw new InvalidOperationException("冰莲必须挂在甘雨选定的敌方位置。");
            foreach (var slot in field.OwnerField.EnemySlots)
                foreach (var old in slot.StatusList.ToArray())
                    if (old.StatusID2 == Lotus && old.Caster == state.Caster) slot.RemoveStatusInstance(old, "Replaced");
        }, null);
        BattleSpecialRules.RegisterTargets("Ganyu_BloomTargets()", (battle, owner) =>
        {
            var marker = battle.Field.EnemySlots.SelectMany(s => s.StatusList)
                .FirstOrDefault(s => s.StatusID2 == Marker && s.Caster == owner && Active(s));
            return marker?.Host is FieldPosition position ? Area(position.SlotIndex) : new List<int>();
        });
        BattleSpecialRules.RegisterTargets("Ganyu_LotusArea()", (battle, owner) =>
            ScriptHookContext.Current?.StatusHost is FieldPosition position ? Area(position.SlotIndex) : new List<int>());
        BattleSpecialRules.RegisterTargets("Ganyu_HitTarget()", (battle, owner) =>
        {
            var c = ScriptHookContext.Current; var fact = (c?.DamageEvent?.Declaration ?? c?.Declaration)?.Get<DamageEventData>();
            return fact?.Subject?.IsAlive == true ? new List<int> { fact.Subject.SlotPosition } : new List<int>();
        });
        BattleSpecialRules.RegisterCondition("Ganyu_C6Ready", (a, c) => Empty(a) && c?.Caster?.EntityID == 1035
            && c.Caster.GetStatusInstances(Ready).Any(Active));
        BattleSpecialRules.RegisterCondition("Ganyu_LotusHit", (a, c) => Empty(a)
            && Responding.Value?.State == c?.Status && Responding.Value?.LotusHit == true);
        BattleSpecialRules.RegisterCondition("Ganyu_FrostHit", (a, c) => Empty(a) && FrostHit(c));
        BattleSpecialRules.RegisterCondition("Ganyu_OnField", (a, c) => Empty(a) && c?.Recipient?.IsAlive == true
            && c.Recipient.Side == c.Caster?.Side && c.Recipient.IsOnField);
        BattleSpecialRules.RegisterCondition("Ganyu_ShowerActive", (a, c) => Empty(a)
            && c?.Caster?.EntityID == 1035 && c.Caster.GetStatusInstances(Shower).Any(Active));
        StatusSnapshotConditions.RegisterLiveTargetStatus(C4Bonus);
        BattleSpecialRules.RegisterCondition("Ganyu_C4Target", (a, c) => Empty(a) && c?.Recipient?.CharacterCtrl != null
            && c.Recipient.Side == c.Caster?.Side && c.Target != null && c.DamageType != "Reaction"
            && c.Target.GetStatusInstances(C4).Any(root => Active(root) && root.Caster == c.Caster
                && root.BoundStatuses.Contains(c.Status)));
        BattleSpecialRules.RegisterBuffScalar("Ganyu_C4Value()", (effect, child) =>
        {
            var root = BattleManager.Instance?.Enemies.SelectMany(e => e.Entity.GetStatusInstances(C4))
                .FirstOrDefault(state => Active(state) && state.Caster == child.Caster && state.BoundStatuses.Contains(child));
            if (!float.TryParse(effect.Param1, NumberStyles.Float, CultureInfo.InvariantCulture, out float perCount))
                throw new InvalidOperationException("西狩增伤缺少合法的每层增伤数值。");
            return (root?.RuntimeIntValue ?? 0) * perCount;
        });
        BattleSpecialRules.RegisterDeclarationObserver(d =>
        {
            var response = Responding.Value; var hit = d.Get<DamageEventData>();
            if (response?.State?.StatusID2 == Lotus && hit?.HitLanded == true && hit.Source?.CountsAsHit == true
                && hit.Source.SourceStatus == response.State && hit.Source.SourceEffectID == "STE_Ganyu_LotusDamage")
                response.LotusHit = true;
        });
    }
    private static void BindCharge(StatusInstance state, object host)
    {
        var cast = Casting.Value;
        if (host != state.Caster || state.Caster?.EntityID != 1035 || cast?.Owner != state.Caster
            || (cast.Skill != "SK_Heavy_Ganyu1" && cast.Skill != "SK_Heavy_Ganyu2"))
            throw new InvalidOperationException("甘雨蓄力缺少本人本次射击的位置。");
        foreach (var marker in cast.Markers.Where(Active)) state.BoundStatuses.Add(marker);
    }
    private static List<int> Area(int center)
        => Enumerable.Range(Math.Max(1, center - 1), Math.Min(5, center + 1) - Math.Max(1, center - 1) + 1).ToList();
    private static bool FrostHit(ScriptHookContext c)
    {
        var hit = (c?.DamageEvent?.Declaration ?? c?.Declaration)?.Get<DamageEventData>();
        return c?.Caster?.EntityID == 1035 && hit?.HitLanded == true && hit.Source?.CountsAsHit == true
            && hit.Source.SourceEntity == c.Caster && hit.Subject?.Side != c.Caster.Side
            && (hit.Source.SourceEffectID == "SE_Ganyu_FrostflakeAttackArrow" || hit.Source.SourceEffectID == "SE_Ganyu_FrostflakeAttackBloom");
    }
}

/// <summary>西狩：敌人各自持有整数进度，初值1、范围0～5。Count只改值；重施保留进度并刷新父子持续时间。</summary>
public sealed class GanyuC4Count : CountStatusHandler
{
    public override bool SupportsEffectChange => true;
    public override bool EffectUsesIntegerValue => true;
    public override void OnCreated(StatusInstance state, object host)
    {
        if (!(host is BattleEntity target) || state.Caster?.EntityID != 1035 || target.Side == state.Caster.Side)
            throw new InvalidOperationException("西狩计数必须由甘雨施加给敌人。");
        state.RuntimeIntValue = 1; state.StackCount = 1;
    }
    public override StatusInstance OnReapplied(StatusInstance state, BattleEntity host, CountStatusApplication application)
    {
        if (state.Caster != application.Caster || application.AddInPhase != state.AddInPhase
            || application.TriggerPhase != state.TriggerPhase || application.Duration <= 0 || !string.IsNullOrEmpty(application.Param2))
            throw new InvalidOperationException("西狩重施仅刷新同来源、同计时阶段的持续时间。");
        StatusBindingSystem.ExtendDuration(state, application.Duration - state.RemainingPhaseCount);
        return state;
    }
    public override void ChangeFromEffect(StatusInstance state, BattleEntity host, CountChangeRequest request)
    { ValidateEffectChange(state, host, request); request.ApplyInteger(state, host, 0, 5); }
    public override void OnRemoved(StatusInstance state, object host)
        => CountChangeRequest.Parse("clear", state.Caster).ApplyInteger(state, host as BattleEntity, 0, 5);
}
