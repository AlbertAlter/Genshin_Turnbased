using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// 优菈：单份初始状态收集命中批次，配表Count效果统一给所有光降之剑充能。
/// 剑本身只保存独立能量、计时和引爆记忆，不各自监听充能。技能倍率/增益仍由配表提供。
/// </summary>
public static class EulaScriptHooks
{
    public const string Root = "ST_Eula_Rule", Heart = "ST_Eula_Grimheart", Sword = "ST_Eula_Lightfall";
    private static readonly AsyncLocal<Cast> Casting = new AsyncLocal<Cast>();
    private static readonly AsyncLocal<Hit> Hitting = new AsyncLocal<Hit>();
    private static readonly ConditionalWeakTable<BattleDeclaration, Hit> Charges = new ConditionalWeakTable<BattleDeclaration, Hit>();
    private sealed class Cast : IDisposable
    {
        internal readonly BattleEntity Owner;
        internal readonly string Skill;
        internal int? Hearts;
        private readonly Cast previous = Casting.Value;
        internal Cast(BattleEntity owner, SkillMainData skill) { Owner = owner; Skill = skill?.SkillID2; Casting.Value = this; }
        public void Dispose() => Casting.Value = previous;
    }
    private sealed class Hit : IDisposable
    {
        internal readonly long ID;
        internal readonly BattleEntity Owner;
        internal readonly HashSet<BattleEntity> Targets = new HashSet<BattleEntity>();
        internal readonly HashSet<BattleEntity> LowHP = new HashSet<BattleEntity>();
        internal BattleDeclaration Cause;
        private readonly Hit previous = Hitting.Value;
        internal Hit(long id, BattleEntity owner, IEnumerable<BattleEntity> targets)
        {
            ID = id; Owner = owner; Hitting.Value = this;
            if (owner?.EntityID == 1040 && targets != null)
                foreach (var target in targets)
                    if (target?.IsAlive == true && target.CurrentHP < target.GetFinalHP() * .5f) LowHP.Add(target);
        }
        public void Dispose()
        {
            Hitting.Value = previous;
            if (Cause == null || Targets.Count == 0 || Owner?.IsAlive != true) return;
            var root = Owner.GetStatus(Root);
            if (!Active(root) || !Owner.GetStatusInstances(Sword).Any(s => Active(s) && !Closing(s))) return;
            Charges.Add(Cause, this);
            if (!DataManager.Instance.StatusActionDict.TryGetValue(Root, out var actions)) return;
            foreach (var action in actions)
                if (action.ActionType == "OnTrigger" && action.ScriptHook == "Eula_ChargeHit()")
                    Owner.CharacterCtrl.TryExecuteEventStatusAction(root, action, root.Host,
                        new ScriptHookContext { Caster = Owner, Status = root, StatusHost = root.Host, Declaration = Cause }, () => Active(root));
        }
    }
    internal sealed class SwordMemory { internal bool Detonating; internal int Energy; }
    internal static bool Active(StatusInstance state) => state?.IsActive == true && state.Caster?.IsAlive == true
        && StatusHostAccess.Contains(state.Host, state);
    internal static bool Closing(StatusInstance state) => (state?.CountRuntimeState as SwordMemory)?.Detonating == true;
    internal static BattleEntity Require(StatusInstance state, object host)
    {
        if (!(host is BattleEntity owner) || owner.EntityID != 1040 || state.Caster != owner)
            throw new InvalidOperationException("优菈专属计数必须由本人持有。");
        return owner;
    }
    private static bool Empty(string text) => string.IsNullOrEmpty(text);
    private static Cast Hold(BattleEntity owner) => Casting.Value?.Owner == owner
        && Casting.Value.Skill == "SK_Skill_Eula2" ? Casting.Value : null;
    private static BattleDeclaration Cause(ScriptHookContext c) => c?.DamageEvent?.Declaration ?? c?.Declaration;
    internal static void Register()
    {
        BattleSpecialRules.RegisterSkillExecutionScope((owner, skill) => new Cast(owner, skill));
        BattleSpecialRules.RegisterHitScope((id, owner, targets) => new Hit(id, owner, targets));
        BattleSpecialRules.RegisterDeclarationObserver(Index);
        BattleSpecialRules.RegisterAutomaticSkillTargets(e => e?.SkillEffectID2 == "SE_Skill_Eula2Icewhirl"
            || e?.SkillEffectID2 == "SE_Skill_Eula2MiniSword");
        BattleSpecialRules.RegisterCastCooldownModifier((owner, skill, rounds) => owner?.EntityID == 1040
            && owner.ConstellationLevel >= 2 && skill?.SkillID2 == "SK_Skill_Eula2" ? Math.Max(1, rounds - 1) : rounds);
        BattleSpecialRules.RegisterStatusLifecycle(Root, (state, host) =>
        { Require(state, host); state.IsPermanent = true; state.RemainingPhaseCount = 0; }, null);
        BattleSpecialRules.RegisterCondition("Eula_ConsumeHearts", (a, c) =>
        {
            var cast = Hold(c?.Caster);
            if (!Empty(a) || cast == null) return false;
            if (!cast.Hearts.HasValue) cast.Hearts = c.Caster.GetStatus(Heart)?.RuntimeIntValue ?? 0;
            return true;
        });
        BattleSpecialRules.RegisterCondition("Eula_Hearts", (a, c) => int.TryParse(a, out int n) && Hold(c?.Caster)?.Hearts == n);
        BattleSpecialRules.RegisterCount("Eula_ConsumedHearts", owner => Hold(owner)?.Hearts ?? 0);
        // 项目明确要求冷酷之心获层刷新5回合。属于角色效果的计时规则，不是Count数值处理器的重施行为。
        // 配在ApplyStatus之前：已有实例只续时，WhenMax=1保留余额；尚无实例由公共ApplyStatus创建。
        BattleSpecialRules.RegisterCondition("Eula_HeartTime", (a, c) =>
        {
            if (!Empty(a) || c?.Caster?.EntityID != 1040) return false;
            var state = c.Caster.GetStatus(Heart);
            if (Active(state)) StatusBindingSystem.ExtendDuration(state, 5 - state.RemainingPhaseCount);
            return true;
        });
        BattleSpecialRules.RegisterCondition("Eula_ChargeHit", (a, c) => Empty(a) && Charge(c)?.Targets.Count > 0);
        BattleSpecialRules.RegisterCount("Eula_HitTargets", owner => Charge(ScriptHookContext.Current)?.Owner == owner
            ? Charge(ScriptHookContext.Current).Targets.Count : 0);
        BattleSpecialRules.RegisterCondition("Eula_HoldHit", (a, c) =>
        {
            var hit = Cause(c)?.Get<DamageEventData>();
            return Empty(a) && Hold(c?.Caster)?.Hearts > 0 && hit?.HitLanded == true && hit.Source?.CountsAsHit == true
                && hit.Source.SourceEntity == c.Caster && hit.Subject?.Side != c.Caster.Side
                && (hit.Source.SourceEffectID == "SE_Skill_Eula2Damage" || hit.Source.SourceEffectID == "SE_Skill_Eula2Icewhirl");
        });
        BattleSpecialRules.RegisterTargets("Eula_HitTarget()", (battle, owner) =>
        {
            var target = Cause(ScriptHookContext.Current)?.Get<DamageEventData>()?.Subject;
            return target?.IsAlive == true && target.Side != owner.Side ? new List<int> { target.SlotPosition } : new List<int>();
        });
        BattleSpecialRules.RegisterCondition("Eula_LowSwordTarget", (a, c) => Empty(a) && Hitting.Value?.Owner == c?.Caster
            && c?.Target != null && Hitting.Value.LowHP.Contains(c.Target)
            && (c.HitEffectID == "STE_Eula_LightfallDamage" || c.HitEffectID == "SE_Skill_Eula2MiniSword"));
        BattleSpecialRules.RegisterStatusDamage("Eula_LightfallDamage()", Explode);
    }
    private static Hit Charge(ScriptHookContext c) => Cause(c) is BattleDeclaration d && Charges.TryGetValue(d, out var hit)
        && hit.Owner == c.Caster && c.Status?.StatusID2 == Root ? hit : null;
    private static void Index(BattleDeclaration declaration)
    {
        var frame = Hitting.Value; var hit = declaration.Get<DamageEventData>(); var source = hit?.Source;
        if (frame?.Owner?.EntityID != 1040 || frame.ID != declaration.Header.HitId || hit?.HitLanded != true
            || source?.CountsAsHit != true || source.SourceEntity != frame.Owner || hit.Subject == null
            || hit.Subject.Side == frame.Owner.Side || (hit.DamageType != "Normal" && hit.DamageType != "Heavy"
                && hit.DamageType != "Skill" && hit.DamageType != "Burst")) return;
        frame.Cause = frame.Cause ?? declaration;
        frame.Targets.Add(hit.Subject);
    }
    private static Task Explode(StatusDamageExecution execution)
    {
        var state = execution.Status; var owner = Require(state, execution.Host);
        var memory = state.CountRuntimeState as SwordMemory;
        if (memory == null || state.StatusID2 != Sword) throw new InvalidOperationException("光降之剑缺少本实例能量记忆。");
        if (!memory.Detonating) { memory.Energy = state.RuntimeIntValue; memory.Detonating = true; }
        if (!owner.CharacterCtrl.TryGetSkillLevelScalar("Eula_LightfallBase", 3, out float basis)
            || !owner.CharacterCtrl.TryGetSkillLevelScalar("Eula_LightfallLayer", 3, out float perLayer))
            throw new InvalidOperationException("光降之剑缺少爆发等级倍率。");
        var e = execution.Effect;
        var prepared = new StatusEffectData {
            StatusEffectID = e.StatusEffectID, StatusEffectID2 = e.StatusEffectID2, EffectIndex = e.EffectIndex,
            EffectType = e.EffectType, Element = e.Element, DamageType = e.DamageType,
            Param1 = (basis + perLayer * memory.Energy).ToString("R", CultureInfo.InvariantCulture) + "*TotalATK,0",
            Param2 = e.Param2, Param3 = e.Param3, TargetType = e.TargetType, TargetSelect = e.TargetSelect,
            TargetConsecutive = e.TargetConsecutive, TargetOverride = e.TargetOverride,
            ScriptHook = e.ScriptHook, DamageHook = e.DamageHook };
        return execution.ExecuteDefault(prepared);
    }
}

/// <summary>冷酷之心0～2；公共ApplyStatus管理单实例，角色获层效果负责续时，Count只改数值。</summary>
public sealed class EulaGrimheartCount : CountStatusHandler
{
    public override bool UseStandardApplication => true;
    public override bool SupportsEffectChange => true;
    public override bool EffectUsesIntegerValue => true;
    public override void OnCreated(StatusInstance state, object host)
    { EulaScriptHooks.Require(state, host); state.RuntimeIntValue = 0; }
    public override void ValidateEffectChange(StatusInstance state, BattleEntity host, CountChangeRequest request)
    { base.ValidateEffectChange(state, host, request); EulaScriptHooks.Require(state, host); }
    public override void ChangeFromEffect(StatusInstance state, BattleEntity host, CountChangeRequest request)
    { ValidateEffectChange(state, host, request); request.ApplyInteger(state, host, 0, 2); }
}

/// <summary>每把光降之剑独立0～30，六命初值5；不监听命中，不处理重新施加，不在归零时删除。</summary>
public sealed class EulaLightfallCount : CountStatusHandler
{
    public override bool UseStandardApplication => true;
    public override bool SupportsEffectChange => true;
    public override bool EffectUsesIntegerValue => true;
    public override void OnCreated(StatusInstance state, object host)
    {
        var owner = EulaScriptHooks.Require(state, host);
        state.RuntimeIntValue = owner.ConstellationLevel >= 6 ? 5 : 0;
        state.CountRuntimeState = new EulaScriptHooks.SwordMemory();
    }
    public override void ValidateEffectChange(StatusInstance state, BattleEntity host, CountChangeRequest request)
    { base.ValidateEffectChange(state, host, request); EulaScriptHooks.Require(state, host); }
    public override void ChangeFromEffect(StatusInstance state, BattleEntity host, CountChangeRequest request)
    {
        ValidateEffectChange(state, host, request);
        if (EulaScriptHooks.Closing(state)) return; // OnEnd尚未移除的本剑已经结算，不能给自身补层。
        request.ApplyInteger(state, host, 0, 30);
    }
    // OnDestroy在移除后执行，必须保留该实例能量供引爆；不主动clear。
}
