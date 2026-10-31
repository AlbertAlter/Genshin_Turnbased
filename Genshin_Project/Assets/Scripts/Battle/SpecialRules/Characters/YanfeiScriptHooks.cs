using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

/// <summary>烟绯：APSpent只记账，整次我方行为及其后续效果或换人响应结清后，灼灼才按20AP产印。</summary>
public static class YanfeiScriptHooks
{
    public const string Seals = "ST_Yanfei_Seals", Brilliance = "ST_Yanfei_Brilliance", Proviso = "ST_Yanfei_Proviso";
    private static readonly AsyncLocal<Cast> Casting = new AsyncLocal<Cast>();
    private static readonly AsyncLocal<Hit> Hitting = new AsyncLocal<Hit>();
    private static readonly ConditionalWeakTable<BattleDeclaration, Hit> CriticalFacts = new ConditionalWeakTable<BattleDeclaration, Hit>();
    private static readonly ConditionalWeakTable<BattleDeclaration, List<Award>> Awards = new ConditionalWeakTable<BattleDeclaration, List<Award>>();
    private sealed class Award { internal StatusInstance State; internal int Count; }
    private sealed class Cast : IDisposable
    {
        internal readonly BattleEntity Owner; internal readonly string Skill, Action; internal int? Seals;
        internal readonly bool IsAllyAction;
        internal readonly Cast Previous = Casting.Value;
        internal bool NormalGranted;
        internal Cast(BattleEntity owner, SkillMainData skill)
        {
            Owner = owner; Skill = skill?.SkillID2; Action = skill?.ActionType;
            IsAllyAction = owner?.Side == BattleSide.Ally && ActiveActionQuery.IsActiveAction(owner, skill);
            Casting.Value = this;
        }
        public void Dispose() => Casting.Value = Previous;
    }
    private sealed class Hit : IDisposable
    {
        internal readonly BattleEntity Owner; internal readonly long ID;
        internal readonly HashSet<BattleEntity> LowHP = new HashSet<BattleEntity>();
        internal readonly HashSet<BattleEntity> Targets = new HashSet<BattleEntity>();
        internal bool CriticalRecorded;
        private readonly Hit previous = Hitting.Value;
        internal Hit(long id, BattleEntity owner, IEnumerable<BattleEntity> targets)
        {
            ID = id; Owner = owner; Hitting.Value = this;
            if (owner?.EntityID == 1039 && targets != null)
                foreach (var target in targets)
                    if (target?.IsAlive == true && target.Side != owner.Side && target.CurrentHP < target.GetFinalHP() * .5f) LowHP.Add(target);
        }
        public void Dispose() => Hitting.Value = previous;
    }
    internal static int ReadSeals(BattleEntity owner) => owner?.EntityID == 1039 ? owner.GetStatus(Seals)?.RuntimeIntValue ?? 0 : 0;
    internal static bool Active(StatusInstance state) => state?.IsActive == true && state.Caster?.IsAlive == true && StatusHostAccess.Contains(state.Host, state);
    internal static BattleEntity Require(StatusInstance state, object host)
    {
        if (!(host is BattleEntity owner) || owner.EntityID != 1039 || state.Caster != owner)
            throw new InvalidOperationException("烟绯计数必须由本人持有。");
        return owner;
    }
    private static Cast Heavy(BattleEntity owner)
    {
        for (var cast = Casting.Value; cast != null; cast = cast.Previous)
            if (cast.Owner == owner && cast.Skill == "SK_Heavy_Yanfei") return cast;
        return null;
    }
    private static BattleDeclaration Cause(ScriptHookContext c) => c?.DamageEvent?.Declaration ?? c?.Declaration;
    internal static void Register()
    {
        BattleSpecialRules.RegisterSkillExecutionScope((owner, skill) => new Cast(owner, skill));
        BattleSpecialRules.RegisterSkillSettled((owner, skill, cause) =>
        {
            if (owner?.Side != BattleSide.Ally || HasAllyAction(Casting.Value?.Previous)) return;
            var battle = BattleManager.Instance;
            if (battle == null) return;
            foreach (var ally in battle.Allies)
            {
                var state = ally?.Entity?.GetStatus(Brilliance);
                if (Active(state)) AwardAP(state, cause);
            }
        });
        BattleSpecialRules.RegisterHitScope((id, owner, targets) => new Hit(id, owner, targets));
        BattleSpecialRules.RegisterDeclarationObserver(Index);
        // 一条重击Damage保留一次手选，按已消费印数读取同级独立标量，不能插成五次选择。
        BattleSpecialRules.RegisterDamage("Yanfei_HeavyRatio()", (_, owner, type) =>
        {
            if (owner?.EntityID != 1039 || type != "Damage")
                throw new InvalidOperationException("烟绯丹火印倍率仅用于本人重击伤害。");
        }, (_, owner, target, hit, level) =>
        {
            int seals = Heavy(owner)?.Seals ?? ReadSeals(owner);
            if (seals == 0) return hit;
            if (seals < 0 || seals > 4 || owner.CharacterCtrl == null
                || !owner.CharacterCtrl.TryGetSkillLevelScalar("Yanfei_HeavySeals" + seals, 1, out float ratio))
                throw new InvalidOperationException("烟绯缺少对应丹火印数量的重击等级倍率。");
            return new HitData(ratio, hit.ElementAura);
        });
        BattleSpecialRules.RegisterCondition("Yanfei_SealAtLeast", (a, c) => int.TryParse(a, out int n)
            && n >= 1 && n <= 4 && ReadSeals(c?.Caster) >= n);
        BattleSpecialRules.RegisterCondition("Yanfei_HeavySeals", (a, c) => int.TryParse(a, out int n)
            && n >= 0 && n <= 4 && c?.Caster?.EntityID == 1039 && (Heavy(c.Caster)?.Seals ?? ReadSeals(c.Caster)) == n);
        // 配在本次重击Count clear上；灼灼尚未发放本次AP收益，倍率/天赋读取同一份旧印消费记忆。
        BattleSpecialRules.RegisterCondition("Yanfei_ConsumeSeals", (a, c) =>
        {
            var cast = Heavy(c?.Caster);
            if (!string.IsNullOrEmpty(a) || cast == null) return false;
            if (!cast.Seals.HasValue) cast.Seals = ReadSeals(c.Caster);
            return true;
        });
        BattleSpecialRules.RegisterCondition("Yanfei_ConsumedSeals", (a, c) => string.IsNullOrEmpty(a) && Heavy(c?.Caster)?.Seals > 0);
        BattleSpecialRules.RegisterCondition("Yanfei_NormalGrant", (a, c) => string.IsNullOrEmpty(a)
            && c?.Caster?.EntityID == 1039 && Casting.Value?.Owner == c.Caster
            && Casting.Value.Action == "Normal" && Casting.Value.NormalGranted);
        BattleSpecialRules.RegisterCondition("Yanfei_HeavyCritical", (a, c) => string.IsNullOrEmpty(a)
            && Cause(c) is BattleDeclaration d && CriticalFacts.TryGetValue(d, out var hit) && hit.Owner == c.Caster);
        BattleSpecialRules.RegisterCondition("Yanfei_LowTargetHP", (a, c) => string.IsNullOrEmpty(a)
            && Hitting.Value?.Owner == c?.Caster && c?.Target != null && Hitting.Value.LowHP.Contains(c.Target));
        BattleSpecialRules.RegisterTargets("Yanfei_CriticalTargets()", (battle, owner) =>
        {
            var d = Cause(ScriptHookContext.Current);
            if (d == null || !CriticalFacts.TryGetValue(d, out var hit) || hit.Owner != owner) return new List<int>();
            return hit.Targets.Where(t => t.IsAlive && battle.GetEntityByPosition(t.Side, t.SlotPosition) == t)
                .Select(t => t.SlotPosition).OrderBy(p => p).ToList();
        });
        BattleSpecialRules.RegisterStatusLifecycle(Proviso, (state, host) =>
        {
            var owner = Require(state, host); var cast = Heavy(owner);
            if (cast?.Seals == null) throw new InvalidOperationException("关联条款缺少本次重击消费记录。");
            state.Snapshot = new Dictionary<string, float> { ["Seals"] = cast.Seals.Value };
        }, null);
        BattleSpecialRules.RegisterBuffScalar("Yanfei_ProvisoValue()", (effect, state) =>
        {
            if (state.Snapshot == null || !state.Snapshot.TryGetValue("Seals", out float seals)
                || !float.TryParse(effect.Param1, NumberStyles.Float, CultureInfo.InvariantCulture, out float ratio))
                throw new InvalidOperationException("关联条款缺少消费记忆或每印增伤参数。");
            return seals * ratio;
        });
        BattleSpecialRules.RegisterCondition("Yanfei_APReward", (a, c) => string.IsNullOrEmpty(a)
            && FindAward(c)?.Count > 0);
        BattleSpecialRules.RegisterCount("Yanfei_APAwards", owner =>
        {
            var award = FindAward(ScriptHookContext.Current);
            return award?.State.Caster == owner ? award.Count : 0;
        });
    }
    private static Award FindAward(ScriptHookContext context)
        => Cause(context) is BattleDeclaration cause && Awards.TryGetValue(cause, out var awards)
            ? awards.Find(a => a.State == context.Status) : null;
    private static bool HasAllyAction(Cast cast)
    {
        for (; cast != null; cast = cast.Previous) if (cast.IsAllyAction) return true;
        return false;
    }
    private static void Index(BattleDeclaration declaration)
    {
        var damage = declaration.Get<DamageEventData>(); var source = damage?.Source;
        if (source?.SourceEntity?.EntityID != 1039 || !source.CountsAsHit || !damage.HitLanded
            || damage.Subject == null || damage.Subject.Side == source.SourceEntity.Side) return;
        var cast = Casting.Value;
        if (cast?.Owner == source.SourceEntity && cast.Action == "Normal" && damage.DamageType == "Normal" && !cast.NormalGranted)
            cast.NormalGranted = true;
        var hit = Hitting.Value;
        if (hit?.Owner != source.SourceEntity || hit.ID != declaration.Header.HitId
            || string.IsNullOrEmpty(source.SourceEffectID)
            || !source.SourceEffectID.StartsWith("SE_Heavy_YanfeiDamage", StringComparison.Ordinal)) return;
        hit.Targets.Add(damage.Subject);
        if (damage.Critical?.IsCritical == true && !hit.CriticalRecorded)
        { hit.CriticalRecorded = true; CriticalFacts.Add(declaration, hit); }
    }
    internal static void OnPayment(StatusInstance state, APSpentEventData payment)
    {
        if (payment.Subject?.Side != state.Caster.Side || payment.Amount <= 0) return;
        CountEffect.Execute(Brilliance, payment.Amount.ToString(CultureInfo.InvariantCulture), state.Caster,
            new[] { state.Caster }, "Yanfei_APAccounting", state);
        // 包含全队实际付款（换人、无ActionType的蓄力起手等），但付款本身不产生新印。
        // 非主动起手只记账，等待下一次我方行为或换人结束；不是伪造一次主动行为。
    }
    internal static void OnSwitch(StatusInstance state, SwitchEventData change)
    {
        if (change.Subject?.Side != state.Caster.Side) return;
        var cause = BattleEventHub.Current;
        // 技能中的自动换人仍属于外层行动，不在技能中途发印。
        if (HasAllyAction(Casting.Value)) return;
        BattleResolutionContext.AfterSettlement(() => AwardAP(state, cause));
    }
    private static void AwardAP(StatusInstance state, BattleDeclaration cause)
    {
        int count = state.RuntimeIntValue / 20;
        if (count <= 0 || cause == null || !Active(state)) return;
        var awards = Awards.GetOrCreateValue(cause);
        if (awards.Exists(a => a.State == state)) return;
        using var declaration = BattleEventHub.Enter(cause);
        CountEffect.Execute(Brilliance, (-count * 20).ToString(CultureInfo.InvariantCulture), state.Caster,
            new[] { state.Caster }, "Yanfei_APAccounting", state);
        awards.Add(new Award { State = state, Count = count });
        if (!DataManager.Instance.StatusActionDict.TryGetValue(Brilliance, out var actions))
            throw new InvalidOperationException("灼灼缺少产印行动。");
        foreach (var action in actions.Where(a => a.ActionType == "OnTrigger" && a.ScriptHook == "Yanfei_APReward()"))
            state.Caster.CharacterCtrl.TryExecuteEventStatusAction(state, action, state.Host,
                new ScriptHookContext { Caster = state.Caster, Status = state, StatusHost = state.Host, Declaration = cause }, () => Active(state));
    }
}

/// <summary>丹火印初值0，整数0～3/六命4；重施只刷新3回合，Count仅改数值，归零不删除。</summary>
public sealed class YanfeiSealCount : CountStatusHandler
{
    public override bool SupportsEffectChange => true;
    public override bool EffectUsesIntegerValue => true;
    public override void OnCreated(StatusInstance state, object host) { YanfeiScriptHooks.Require(state, host); state.RuntimeIntValue = 0; }
    public override void OnCreated(StatusInstance state, object host, CountStatusApplication application)
    { Reject(application); OnCreated(state, host); }
    public override StatusInstance OnReapplied(StatusInstance state, BattleEntity host, CountStatusApplication application)
    { Refresh(state, host, application); return state; }
    internal static void Reject(CountStatusApplication application)
    {
        if (!string.IsNullOrEmpty(application?.Param2)) throw new InvalidOperationException("烟绯Count重新施加不接受数值命令，请用Count效果。");
    }
    internal static void Refresh(StatusInstance state, BattleEntity host, CountStatusApplication application)
    {
        YanfeiScriptHooks.Require(state, host); Reject(application);
        if (application?.Caster != host) throw new InvalidOperationException("烟绯Count只能由本人重新施加。");
        bool changed = state.AddInPhase != application.AddInPhase || state.TriggerPhase != application.TriggerPhase;
        if (changed) BattleManager.Instance?.UnregisterStatusTick(state);
        state.RemainingPhaseCount = application.Duration; state.IsPermanent = application.Duration <= 0;
        state.AddInPhase = application.AddInPhase; state.TriggerPhase = application.TriggerPhase;
        if (changed) BattleManager.Instance?.RegisterStatusTick(state, host, null);
    }
    public override void ValidateEffectChange(StatusInstance state, BattleEntity host, CountChangeRequest request)
    { base.ValidateEffectChange(state, host, request); YanfeiScriptHooks.Require(state, host); }
    public override void ChangeFromEffect(StatusInstance state, BattleEntity host, CountChangeRequest request)
    { ValidateEffectChange(state, host, request); request.ApplyInteger(state, host, 0, host.ConstellationLevel >= 6 ? 4 : 3); }
    public override void OnRemoved(StatusInstance state, object host)
        => CountChangeRequest.Parse("clear", state.Caster).ApplyInteger(state, host as BattleEntity, 0, 4);
}

/// <summary>
/// 灼灼同时携带重击增伤和累计AP。APSpent只加计数；我方完整行为及其派生/结束响应、或独立换人响应结清后，
/// 每20点通过OnTrigger/Yanfei_APReward()执行ApplyStatus+Count产一印，ExecuteCount=Yanfei_APAwards。
/// 例如旧2印、余数18、重击实付22：本次消耗旧2印，整条行动结束再获得2印、余数0。
/// 归零不删除；阶段6配Count clear清余数。阵亡/移除不补发，后台正常累计和领取。
/// </summary>
public sealed class YanfeiBrillianceCount : CountStatusHandler
{
    private sealed class Memory : IDisposable
    {
        internal IDisposable Payment, Switch;
        internal int Turn;
        public void Dispose() { Payment?.Dispose(); Switch?.Dispose(); }
    }
    public override bool SupportsEffectChange => true;
    public override bool EffectUsesIntegerValue => true;
    public override void OnCreated(StatusInstance state, object host)
    {
        YanfeiScriptHooks.Require(state, host); state.RuntimeIntValue = 0;
        var memory = new Memory { Turn = BattleManager.Instance?.TurnCount ?? 0 }; state.CountRuntimeState = memory;
        memory.Payment = BattleEventHub.Subscribe<APSpentEventData>(BattleEventKind.APSpent,
            (_, fact) =>
            {
                int turn = BattleManager.Instance?.TurnCount ?? 0;
                // 阶段6之后若还有自动行动付款，其余数仍属于旧回合，不能带入新回合。
                if (memory.Turn != turn)
                {
                    CountEffect.Execute(YanfeiScriptHooks.Brilliance, "clear", state.Caster,
                        new[] { state.Caster }, "Yanfei_RoundBoundary", state);
                    memory.Turn = turn;
                }
                YanfeiScriptHooks.OnPayment(state, fact);
            }, state.ApplyOrder, () => YanfeiScriptHooks.Active(state));
        memory.Switch = BattleEventHub.Subscribe<SwitchEventData>(BattleEventKind.SwitchResolved,
            (_, fact) => YanfeiScriptHooks.OnSwitch(state, fact), state.ApplyOrder, () => YanfeiScriptHooks.Active(state));
    }
    public override void OnCreated(StatusInstance state, object host, CountStatusApplication application)
    { YanfeiSealCount.Reject(application); OnCreated(state, host); }
    public override StatusInstance OnReapplied(StatusInstance state, BattleEntity host, CountStatusApplication application)
    { YanfeiSealCount.Refresh(state, host, application); return state; }
    public override void ValidateEffectChange(StatusInstance state, BattleEntity host, CountChangeRequest request)
    { base.ValidateEffectChange(state, host, request); YanfeiScriptHooks.Require(state, host); }
    public override void ChangeFromEffect(StatusInstance state, BattleEntity host, CountChangeRequest request)
    { ValidateEffectChange(state, host, request); request.ApplyInteger(state, host, 0, int.MaxValue); }
    public override void OnRemoved(StatusInstance state, object host)
    {
        (state.CountRuntimeState as Memory)?.Dispose();
        CountChangeRequest.Parse("clear", state.Caster).ApplyInteger(state, host as BattleEntity, 0, int.MaxValue);
    }
}
