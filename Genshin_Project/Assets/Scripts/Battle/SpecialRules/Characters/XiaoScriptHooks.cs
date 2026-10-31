using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;

/// <summary>魈：真实重击命中数量，以及True自伤最低保留1HP。六命免费施加/消耗/清理由配表执行。</summary>
public static class XiaoScriptHooks
{
    public const string Mask = "ST_Xiao_Mask", Tamer = "ST_Xiao_Tamer", Cycling = "ST_Xiao_Cycling", Free = "ST_Xiao_Free";
    private static readonly AsyncLocal<Cast> Casting = new AsyncLocal<Cast>();
    private sealed class Cast : IDisposable
    {
        internal readonly BattleEntity Owner;
        internal readonly string Skill;
        internal readonly Dictionary<long, HashSet<BattleEntity>> Hits = new Dictionary<long, HashSet<BattleEntity>>();
        private readonly Cast previous = Casting.Value;
        internal Cast(BattleEntity owner, SkillMainData skill) { Owner = owner; Skill = skill?.SkillID2; Casting.Value = this; }
        public void Dispose() => Casting.Value = previous;
    }
    internal static bool Active(StatusInstance state) => state?.IsActive == true && state.Caster?.IsAlive == true
        && StatusHostAccess.Contains(state.Host, state);
    internal static void Register()
    {
        BattleSpecialRules.RegisterSkillExecutionScope((owner, skill) => new Cast(owner, skill));
        BattleSpecialRules.RegisterDeclarationObserver(declaration =>
        {
            var cast = Casting.Value; var hit = declaration.Get<DamageEventData>();
            if (cast?.Owner?.EntityID != 1036 || cast.Skill != "SK_Heavy_Xiao2" || hit?.HitLanded != true
                || hit.DamageType != "Heavy" || hit.Source?.CountsAsHit != true || hit.Source.SourceEntity != cast.Owner
                || hit.Source.SourceEffectID != "SE_Heavy_Xiao2Damage" || hit.Subject?.Side == cast.Owner.Side) return;
            long hitID = declaration.Header.HitId;
            if (!cast.Hits.TryGetValue(hitID, out var targets)) cast.Hits.Add(hitID, targets = new HashSet<BattleEntity>());
            targets.Add(hit.Subject);
        });
        BattleSpecialRules.RegisterCondition("Xiao_OutsideMask", (a, c) => string.IsNullOrEmpty(a)
            && c?.Caster?.EntityID == 1036 && !c.Caster.GetStatusInstances(Mask).Any(Active));
        BattleSpecialRules.RegisterCondition("Xiao_C6Hit", (a, c) => string.IsNullOrEmpty(a) && c?.Caster?.EntityID == 1036
            && c.Caster.ConstellationLevel >= 6 && c.Caster.GetStatusInstances(Mask).Any(Active)
            && Casting.Value?.Owner == c.Caster && Casting.Value.Hits.Values.Any(targets => targets.Count >= 2));
        BattleSpecialRules.RegisterCondition("Xiao_LowHP", (a, c) => string.IsNullOrEmpty(a) && c?.Caster?.EntityID == 1036
            && c.Caster.IsAlive && c.Caster.CurrentHP < c.Caster.GetFinalHP() * .5f);
        BattleSpecialRules.RegisterFixedStatusAmount("Xiao_BurstDrain()", DrainAmount);
    }
    internal static void RequireOwner(StatusInstance state, object host)
    {
        if (!(host is BattleEntity owner) || state?.Caster != owner || owner.EntityID != 1036)
            throw new InvalidOperationException("魈专属状态必须挂在本人。");
    }
    // 在TrueDamageExecutor实际提交该hit时读取HP；不先扣死再补回，不替其他True伤害增加免死。
    internal static float DrainAmount(StatusEffectData effect, StatusInstance state, object host)
    {
        RequireOwner(state, host);
        if (state.StatusID2 != Mask || effect.StatusEffectID2 != "STE_Xiao_Drain" || effect.TargetType != "Self"
            || !DamageProperties.IsTrue(effect.Element) || !string.IsNullOrWhiteSpace(effect.DamageType)
            || !float.TryParse(effect.Param1, NumberStyles.Float, CultureInfo.InvariantCulture, out float coefficient)
            || float.IsNaN(coefficient) || coefficient < 0f || coefficient > 1f)
            throw new InvalidOperationException("魈扣血钩子只接受靖妖傩舞对自身的True伤害及0～1系数。");
        float hp = state.Caster.CurrentHP;
        return Math.Max(0f, Math.Min(hp * coefficient, hp - 1f));
    }
}

/// <summary>魈专属整数Count：Tamer初值1、不另设叠层上限；Cycling初值0、上限3。重施只刷新，增减由Count效果管理。</summary>
public sealed class XiaoCounter : CountStatusHandler
{
    private readonly int initial, maximum;
    public XiaoCounter(int initial, int maximum) { this.initial = initial; this.maximum = maximum; }
    public override bool SupportsEffectChange => true;
    public override bool EffectUsesIntegerValue => true;
    public override void OnCreated(StatusInstance state, object host)
    { XiaoScriptHooks.RequireOwner(state, host); state.RuntimeIntValue = initial; state.StackCount = 1; }
    public override StatusInstance OnReapplied(StatusInstance state, BattleEntity host, CountStatusApplication application)
    {
        XiaoScriptHooks.RequireOwner(state, host);
        if (application.Caster != state.Caster || application.Duration <= 0 || application.AddInPhase != state.AddInPhase
            || application.TriggerPhase != state.TriggerPhase || !string.IsNullOrEmpty(application.Param2))
            throw new InvalidOperationException("魈Count重施须保留本人、相同计时阶段及正持续时间。");
        StatusBindingSystem.ExtendDuration(state, application.Duration - state.RemainingPhaseCount);
        return state;
    }
    public override void ChangeFromEffect(StatusInstance state, BattleEntity host, CountChangeRequest request)
    { ValidateEffectChange(state, host, request); request.ApplyInteger(state, host, 0, maximum); }
    public override void OnRemoved(StatusInstance state, object host)
        => CountChangeRequest.Parse("clear", state.Caster).ApplyInteger(state, host as BattleEntity, 0, maximum);
}
