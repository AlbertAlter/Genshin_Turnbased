using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;

/// <summary>裟罗只保存本次命中、具体乌羽护持和选定位置；伤害、计数、冷却与加攻继续执行源表。</summary>
public static class SaraScriptHooks
{
    public const int CharacterID = 1045;
    public const string Cover = "ST_Sara_Cover", Feather = "ST_Sara_Feather", WeakFeather = "ST_Sara_WeakFeather";
    public const string ChargeTarget = "ST_Sara_ChargeTarget", Counter = "ST_Sara_C1Counter";
    private static readonly AsyncLocal<Cast> Casting = new AsyncLocal<Cast>();
    private static readonly AsyncLocal<string> Effect = new AsyncLocal<string>();
    private sealed class Restore : IDisposable
    {
        private Action action;
        internal Restore(Action restore) { action = restore; }
        public void Dispose() { var restore = action; action = null; restore?.Invoke(); }
    }
    private sealed class Cast : IDisposable
    {
        internal readonly BattleEntity Owner;
        internal readonly string Skill;
        internal int HeavyPosition, BurstPosition;
        internal StatusInstance UsedCover;
        internal bool FeatherCreated;
        private readonly Cast previous = Casting.Value;
        internal Cast(BattleEntity owner, SkillMainData skill) { Owner = owner; Skill = skill.SkillID2; Casting.Value = this; }
        public void Dispose() => Casting.Value = previous;
    }
    private static Cast Current(BattleEntity owner) => Casting.Value?.Owner == owner ? Casting.Value : null;
    private static bool Heavy(Cast cast) => cast?.Skill is "SK_Heavy_SaraInstant" or "SK_Heavy_SaraRelease";
    private static bool Empty(string argument) => string.IsNullOrEmpty(argument);
    private static bool Own(ScriptHookContext c) => c?.Caster?.EntityID == CharacterID && c.Caster.IsAlive;
    private static bool HasFeather(ScriptHookContext c) => Own(c) && Heavy(Current(c.Caster))
        && Current(c.Caster).UsedCover != null && Current(c.Caster).HeavyPosition > 0 && !Current(c.Caster).FeatherCreated;

    internal static void Register()
    {
        BattleSpecialRules.RegisterSkillExecutionScope((owner, skill) => owner?.EntityID == CharacterID && skill != null
            ? new Cast(owner, skill) : new Restore(null));
        BattleSpecialRules.RegisterEffectInvocationScope((owner, id, execution) => {
            var previous = Effect.Value; Effect.Value = id; return new Restore(() => Effect.Value = previous);
        });
        BattleSpecialRules.RegisterHitScope((_, owner, targets) => {
            var cast = Current(owner);
            if (cast?.Skill == "SK_Burst_Sara" && Effect.Value == "SE_Burst_Sara" && cast.BurstPosition == 0)
                cast.BurstPosition = targets.FirstOrDefault(t => t?.IsOnField == true && t.Side != owner.Side)?.SlotPosition ?? 0;
            return new Restore(null);
        });
        BattleSpecialRules.RegisterDeclarationObserver(declaration => {
            var cast = Casting.Value;
            if (!Heavy(cast) || cast.HeavyPosition != 0 || declaration?.Get<DamageEventData>() is not DamageEventData hit
                || !hit.HitLanded || hit.Source?.CountsAsHit != true || hit.Source.SourceEntity != cast.Owner
                || hit.Source.SourceEffectID is not ("SE_Heavy_SaraInstant" or "SE_Heavy_SaraRelease")
                || hit.Subject == null || hit.Subject.Side == cast.Owner.Side) return;
            cast.HeavyPosition = hit.Subject.SlotPosition;
            cast.UsedCover = cast.Owner.GetStatus(Cover); // 命中时记录，后续只消费这一份，避免删除响应中新授予的护持。
        });
        BattleSpecialRules.RegisterAutomaticSkillTargets(e => e?.SkillEffectID2 == "SE_Burst_SaraBounces");
        BattleSpecialRules.RegisterCount("Sara_BurstBounces", owner => owner?.EntityID == CharacterID
            ? (owner.ConstellationLevel >= 4 ? 6 : 4) : throw new InvalidOperationException("裟罗弹射次数缺少正确来源。"));
        BattleSpecialRules.RegisterTargets("Sara_HeavyFeatherTarget()", (_, owner) => HasFeather(new ScriptHookContext { Caster = owner })
            ? new List<int> { Current(owner).HeavyPosition } : new List<int>());
        BattleSpecialRules.RegisterTargets("Sara_BurstTarget()", (_, owner) => Current(owner)?.Skill == "SK_Burst_Sara"
            && Current(owner).BurstPosition > 0 ? new List<int> { Current(owner).BurstPosition } : new List<int>());
        BattleSpecialRules.RegisterTargets("Sara_ChargeTarget()", (_, owner) => ScriptHookContext.Current?.Status is StatusInstance state
            && state.StatusID2 == ChargeTarget && state.Caster == owner && state.Host is FieldPosition field
            ? new List<int> { field.SlotIndex } : new List<int>());
        BattleSpecialRules.RegisterTargets("Sara_FeatherArea()", (_, owner) => ScriptHookContext.Current?.Status is StatusInstance state
            && state.Caster == owner && state.StatusID2 is Feather or WeakFeather && state.Host is FieldPosition field
            ? Area(field.SlotIndex, field.Side) : new List<int>());
        BattleSpecialRules.RegisterCondition("Sara_HeavyFeather", (a, c) => Empty(a) && HasFeather(c));
        BattleSpecialRules.RegisterCondition("Sara_C1Full", (a, c) => Empty(a) && Own(c)
            && c.Caster.GetStatus(Counter)?.RuntimeIntValue == 3);
        BattleSpecialRules.RegisterCondition("Sara_C1Cooling", (a, c) => Empty(a) && Own(c)
            && c.Caster.GetStatus(Counter)?.RuntimeIntValue == 3 && c.Caster.CharacterCtrl.GetCooldownRemaining(2) > 0);
        BattleSpecialRules.RegisterCondition("Sara_DecorumHit", (a, c) => Empty(a) && Own(c)
            && c.DamageEvent?.HitLanded == true && c.DamageEvent.Source?.CountsAsHit == true
            && c.DamageEvent.Source.SourceEntity == c.Caster && c.DamageEvent.Source.SourceEffectID == "STE_Sara_FeatherDamage"
            && c.DamageEvent.Source.SourceStatus?.StatusID2 == Feather);
        BattleSpecialRules.RegisterStatusLifecycle(Feather, (state, host) => {
            var cast = Current(state.Caster);
            if (!HasFeather(new ScriptHookContext { Caster = state.Caster }) || host is not FieldPosition field
                || field.Side == state.Caster.Side || field.SlotIndex != cast.HeavyPosition)
                throw new InvalidOperationException("普通乌羽必须来自本次持有乌羽护持的重击命中位置。");
            cast.FeatherCreated = true;
            if (cast.UsedCover.IsActive && StatusHostAccess.Contains(cast.UsedCover.Host, cast.UsedCover))
                StatusHostAccess.Remove(cast.UsedCover.Host, cast.UsedCover, "ConsumedSaraCover");
        }, null);
        BattleSpecialRules.RegisterBuffScalar("Sara_BaseAttack()", (effect, state) => {
            if (state.Caster?.EntityID != CharacterID || state.StatusID2 != "ST_Sara_AttackBonus"
                || !state.Caster.CharacterCtrl.TryResolveStatusLevelScalar(effect, out float ratio))
                throw new InvalidOperationException("裟罗加攻须读取来源者的战技等级与基础攻击力。");
            return state.Caster.GetTotalBaseATK() * ratio;
        });
        BattleSpecialRules.RegisterEnergyScalar("Sara_DecorumEnergy()", (effect, state) => {
            if (state.Caster?.EntityID != CharacterID || state.StatusID2 != "ST_Sara_Rule"
                || !float.TryParse(effect.Param1, NumberStyles.Float, CultureInfo.InvariantCulture, out float coefficient))
                throw new InvalidOperationException("裟罗天赋回能缺少正确来源与数值系数。");
            return coefficient * state.Caster.GetFinalRechargeRate();
        });
    }
    private static List<int> Area(int center, BattleSide side)
    {
        var result = new List<int>();
        for (int p = center - 1; p <= center + 1; p++) if (p >= 1 && p <= BattlePositionSystem.GetSideCount(side)) result.Add(p);
        return result;
    }
}

/// <summary>一命永久整数计数0～3；第三次的减冷却和clear由状态效果依次执行。</summary>
public sealed class SaraFeatherCounter : CountStatusHandler
{
    public override bool UseStandardApplication => true;
    public override bool SupportsEffectChange => true;
    public override bool EffectUsesIntegerValue => true;
    private static void Require(StatusInstance state, object host)
    {
        if (host is not BattleEntity owner || owner.EntityID != SaraScriptHooks.CharacterID || state.Caster != owner)
            throw new InvalidOperationException("裟罗一命计数须挂在本人。");
    }
    public override void OnCreated(StatusInstance state, object host) { Require(state, host); state.RuntimeIntValue = 0; }
    public override void ChangeFromEffect(StatusInstance state, BattleEntity host, CountChangeRequest request)
    { ValidateEffectChange(state, host, request); Require(state, host); request.ApplyInteger(state, host, 0, 3); }
}
