using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

/// <summary>罗莎莉亚：扣费前AP、每次爆发独立位置记忆、固定暴击分享及整次战技回能限次。</summary>
public static class RosariaScriptHooks
{
    public const string Field = "ST_Rosaria_Rites", Share = "ST_Rosaria_CritShare";
    private static readonly AsyncLocal<Preparation> Preparing = new AsyncLocal<Preparation>();
    private static readonly AsyncLocal<Cast> Casting = new AsyncLocal<Cast>();
    private static readonly ConditionalWeakTable<StatusInstance, List<int>> Positions = new ConditionalWeakTable<StatusInstance, List<int>>();
    private static readonly ConditionalWeakTable<BattleDeclaration, Cast> EnergyFacts = new ConditionalWeakTable<BattleDeclaration, Cast>();

    private sealed class Preparation : IDisposable
    {
        internal readonly BattleEntity Owner;
        internal readonly bool HalfAP;
        private readonly Preparation previous = Preparing.Value;
        internal Preparation(BattleEntity owner)
        {
            Owner = owner;
            var ap = owner?.CharacterCtrl?.APManager;
            HalfAP = ap != null && ap.MaxAP > 0 && (long)ap.CurrentAP * 2 >= ap.MaxAP;
            Preparing.Value = this;
        }
        public void Dispose() => Preparing.Value = previous;
    }
    private sealed class Cast : IDisposable
    {
        internal readonly BattleEntity Owner;
        internal readonly string Skill;
        internal readonly bool HalfAP;
        internal readonly List<int> Selected = new List<int>();
        internal bool EnergyRecorded;
        internal float? SharedCrit;
        private readonly Cast previous = Casting.Value;
        internal Cast(BattleEntity owner, SkillMainData skill)
        {
            Owner = owner; Skill = skill?.SkillID2;
            HalfAP = Preparing.Value?.Owner == owner && Preparing.Value.HalfAP;
            Casting.Value = this;
        }
        public void Dispose() => Casting.Value = previous;
    }
    private sealed class EmptyScope : IDisposable { public void Dispose() { } }
    internal static void Register()
    {
        BattleSpecialRules.RegisterSkillScope(owner => new Preparation(owner));
        BattleSpecialRules.RegisterSkillExecutionScope((owner, skill) => new Cast(owner, skill));
        BattleSpecialRules.RegisterEffectInvocationScope(CaptureSelection);
        BattleSpecialRules.RegisterDeclarationObserver(Index);
        BattleSpecialRules.RegisterStatusLifecycle(Field, CaptureField, null);
        BattleSpecialRules.RegisterStatusLifecycle(Share, CaptureShare, null);
        BattleSpecialRules.RegisterBuffScalar("Rosaria_FixedCritShare()", (_, state) =>
        {
            if (state?.Snapshot == null || !state.Snapshot.TryGetValue("SharedCrit", out float value))
                throw new InvalidOperationException("罗莎莉亚暴击分享缺少本次施放时记录。");
            return value;
        });
        BattleSpecialRules.RegisterCondition("Rosaria_PreCastHalfAP", (a, c) => Empty(a)
            && Current(c?.Caster, "SK_Skill_Rosaria")?.HalfAP == true);
        BattleSpecialRules.RegisterCondition("Rosaria_Critical", (a, c) => Empty(a) && Attack(c)
            && c.DamageEvent.Critical?.IsCritical == true);
        BattleSpecialRules.RegisterCondition("Rosaria_SkillEnergy", (a, c) => Empty(a)
            && (c?.DamageEvent?.Declaration ?? c?.Declaration) is BattleDeclaration d
            && EnergyFacts.TryGetValue(d, out var cast) && cast.Owner == c.Caster);
        BattleSpecialRules.RegisterCondition("Rosaria_BurstHit", (a, c) => Empty(a) && Attack(c)
            && (c.DamageEvent.Source.SourceEffectID == "SE_Burst_RosariaDamage"
                || c.DamageEvent.Source.SourceEffectID == "STE_Rosaria_RitesDamage"));
        BattleSpecialRules.RegisterTargets("Rosaria_OtherAllies()", (battle, owner) => battle.Allies
            .Where(a => a?.Entity?.IsAlive == true && a.Entity != owner && a.Entity.Side == owner.Side)
            .Select(a => a.Entity.SlotPosition).ToList());
        BattleSpecialRules.RegisterTargets("Rosaria_HitEnemy()", (battle, owner) =>
        {
            var target = ScriptHookContext.Current?.DamageEvent?.Target;
            return target?.IsAlive == true && target.Side != owner?.Side
                && battle.GetEntityByPosition(target.Side, target.SlotPosition) == target
                ? new List<int> { target.SlotPosition } : new List<int>();
        });
        BattleSpecialRules.RegisterTargets("Rosaria_FieldAnchor()", (_, owner) =>
        {
            var cast = Current(owner, "SK_Burst_Rosaria");
            if (cast == null || cast.Selected.Count == 0) throw new InvalidOperationException("终命的圣礼缺少本次所选位置。");
            // 一份领域只登记一次阶段行动；锚点承载状态，实际范围保存在实例记忆中。
            return new List<int> { cast.Selected[cast.Selected.Count / 2] };
        });
        BattleSpecialRules.RegisterTargets("Rosaria_RitesPositions()", (_, owner) =>
        {
            var state = ScriptHookContext.Current?.Status;
            return state?.Caster == owner && Positions.TryGetValue(state, out var positions)
                ? new List<int>(positions) : new List<int>();
        });
    }
    private static bool Empty(string text) => string.IsNullOrEmpty(text);
    private static Cast Current(BattleEntity owner, string skill) => owner?.EntityID == 1038
        && Casting.Value?.Owner == owner && Casting.Value.Skill == skill ? Casting.Value : null;
    private static bool Attack(ScriptHookContext c) => c?.Caster?.EntityID == 1038 && c.Caster.IsAlive
        && c.DamageEvent?.HitLanded == true && c.DamageEvent.Source?.CountsAsHit == true
        && c.DamageEvent.Source.SourceEntity == c.Caster && c.DamageEvent.Target?.Side != c.Caster.Side;
    private static IDisposable CaptureSelection(BattleEntity owner, string effect, long execution)
    {
        var cast = Current(owner, "SK_Burst_Rosaria");
        if (cast != null && effect == "SE_Burst_RosariaDamage")
        {
            // 在第一段伤害之前记录选择，目标死亡或换位不改变后续领域的位置。
            var targets = BattleTargetResolver.Resolve(BattleManager.Instance, owner, owner, "Enemy", 3, 1,
                null, null, owner.CharacterCtrl.ForcedTargetPositions, null);
            cast.Selected.Clear();
            if (targets.IsValid) cast.Selected.AddRange(targets.Positions.Distinct().OrderBy(p => p));
        }
        return new EmptyScope();
    }
    private static void CaptureField(StatusInstance state, object host)
    {
        var cast = Current(state.Caster, "SK_Burst_Rosaria");
        if (!(host is FieldPosition slot) || slot.Side == state.Caster?.Side || cast == null || cast.Selected.Count == 0)
            throw new InvalidOperationException("终命的圣礼只能从本次爆发创建在敌方场地。");
        Positions.Add(state, new List<int>(cast.Selected));
        // 二命只延长这份新领域，不改动之前施放的领域。
        if (state.Caster.ConstellationLevel >= 2) state.RemainingPhaseCount += 1;
    }
    private static void CaptureShare(StatusInstance state, object host)
    {
        var cast = Current(state.Caster, "SK_Burst_Rosaria");
        if (cast == null || !(host is BattleEntity recipient) || recipient == state.Caster || recipient.Side != state.Caster.Side)
            throw new InvalidOperationException("罗莎莉亚暴击分享只能由本次爆发施加给其他队友。");
        if (!cast.SharedCrit.HasValue)
        {
            var owner = state.Caster;
            // 无伤害/目标上下文：仅分享当前角色暴击率，不纳入指定伤害或敌人条件的暴击率。
            float rate = owner.CritRate + owner.WeaponCritRate + owner.GetStatusCritRate("", "", -1, "");
            if (!DataManager.Instance.StatusEffectDict.TryGetValue("STE_Rosaria_ShareCrit", out var effect)
                || !float.TryParse(effect.Param1, NumberStyles.Float, CultureInfo.InvariantCulture, out float ratio)
                || !float.TryParse(effect.Param2, NumberStyles.Float, CultureInfo.InvariantCulture, out float limit)
                || float.IsNaN(ratio) || float.IsInfinity(ratio) || ratio < 0
                || float.IsNaN(limit) || float.IsInfinity(limit) || limit < 0)
                throw new InvalidOperationException("罗莎莉亚暴击分享需要Param1比例和Param2上限。");
            cast.SharedCrit = Math.Max(0f, Math.Min(limit, rate * ratio));
        }
        state.Snapshot = new Dictionary<string, float> { ["SharedCrit"] = cast.SharedCrit.Value };
    }
    private static void Index(BattleDeclaration declaration)
    {
        var damage = declaration.Get<DamageEventData>();
        var source = damage?.Source;
        var cast = Current(source?.SourceEntity, "SK_Skill_Rosaria");
        if (cast == null || cast.EnergyRecorded || damage.HitLanded != true || damage.Critical?.IsCritical != true
            || source.CountsAsHit != true || source.SourceEffectID != "SE_Skill_RosariaDamage"
            || damage.Subject == null || damage.Subject.Side == cast.Owner.Side) return;
        // 在真实暴击结果提交时占用该次技能额度；以后各目标/各hit不能重复领取。
        cast.EnergyRecorded = true;
        EnergyFacts.Add(declaration, cast);
    }
}
