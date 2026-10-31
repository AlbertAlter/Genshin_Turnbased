using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

/// <summary>
/// 阳华/瑰银使用真实命中批次，分别持有限次；瑰银在本人身上独立计时。
/// 配表管理伤害、产能、增益和生灭计数修改；此处只管理事件身份、随机目标和本次爆发的消费记忆。
/// </summary>
public static class AlbedoScriptHooks
{
    public const string Solar = "ST_Albedo_Solar", Silver = "ST_Albedo_Silver";
    public const string Reckoning = "ST_Albedo_Reckoning", C4Rule = "ST_Albedo_C4Rule";
    private static readonly AsyncLocal<Cast> Casting = new AsyncLocal<Cast>();
    private static readonly AsyncLocal<HitFrame> Hitting = new AsyncLocal<HitFrame>();
    private static readonly AsyncLocal<Response> Responding = new AsyncLocal<Response>();
    private static readonly ConditionalWeakTable<BattleDeclaration, List<Request>> Requests = new ConditionalWeakTable<BattleDeclaration, List<Request>>();
    private static readonly ConditionalWeakTable<object, Dictionary<(StatusInstance, BattleEntity), bool>> NormalClaims
        = new ConditionalWeakTable<object, Dictionary<(StatusInstance, BattleEntity), bool>>();

    private sealed class Cast : IDisposable
    {
        internal readonly BattleEntity Owner;
        internal readonly string Skill;
        internal int Consumed;
        private readonly Cast previous = Casting.Value;
        internal Cast(BattleEntity owner, SkillMainData skill) { Owner = owner; Skill = skill?.SkillID2; Casting.Value = this; }
        public void Dispose() => Casting.Value = previous;
    }
    private sealed class Request
    {
        internal StatusInstance Root;
        internal BattleDeclaration Cause;
        internal readonly List<BattleEntity> Targets = new List<BattleEntity>();
    }
    private sealed class Response : IDisposable
    {
        internal readonly StatusInstance State;
        internal bool Landed;
        private readonly Response previous = Responding.Value;
        internal Response(StatusInstance state) { State = state; Responding.Value = this; }
        public void Dispose() => Responding.Value = previous;
    }
    private sealed class HitFrame : IDisposable
    {
        internal readonly long ID;
        internal readonly BattleEntity Source;
        internal readonly List<Request> Candidates = new List<Request>();
        private readonly HitFrame previous = Hitting.Value;
        internal HitFrame(long id, BattleEntity source)
        {
            ID = id; Source = source; Hitting.Value = this;
            // 只让命中开始前已在场的花响应，避免同hit新生成的瑰银追溯触发。
            var battle = BattleManager.Instance;
            if (battle == null || source == null) return;
            foreach (var ally in battle.Allies)
            {
                var owner = ally?.Entity;
                if (owner?.EntityID != 1034 || !owner.IsAlive || owner.Side != source.Side) continue;
                var solar = FindSolar(owner);
                if (solar != null) Candidates.Add(new Request { Root = solar });
                foreach (var silver in owner.GetStatusInstances(Silver).Where(Active)) Candidates.Add(new Request { Root = silver });
            }
        }
        public void Dispose()
        {
            Hitting.Value = previous;
            using var collection = BattleResolutionContext.BeginCollection();
            foreach (var request in Candidates)
                if (request.Cause != null && Active(request.Root)) Queue(request);
        }
    }

    internal static void Register()
    {
        BattleSpecialRules.RegisterSkillExecutionScope((owner, skill) => new Cast(owner, skill));
        BattleSpecialRules.RegisterStatusActionScope(state => new Response(state));
        BattleSpecialRules.RegisterHitScope((id, source, targets) => new HitFrame(id, source));
        BattleSpecialRules.RegisterDeclarationObserver(Index);
        BattleSpecialRules.RegisterAutomaticSkillTargets(effect => effect?.SkillEffectID2 == "SE_Skill_Albedo"
            || effect?.SkillEffectID2 == "SE_Burst_AlbedoFatal");
        BattleSpecialRules.RegisterStatusLifecycle(Solar, (state, host) =>
        {
            if (!(host is FieldPosition field) || state.Caster?.EntityID != 1034 || field.Side == state.Caster.Side)
                throw new InvalidOperationException("阳华必须由阿贝多施加在敌方场地。");
            var old = FindSolar(state.Caster);
            if (old != null) StatusHostAccess.Remove(old.Host, old, "Replaced");
        }, null);
        BattleSpecialRules.RegisterStatusLifecycle(Silver, (state, host) => RequireOwner(state, host), null);
        BattleSpecialRules.RegisterCondition("Albedo_FlowerHit", (a, c) => Empty(a) && RequestFor(c) != null);
        BattleSpecialRules.RegisterCondition("Albedo_OwnFlowerHit", (a, c) => Empty(a)
            && Responding.Value?.State == c?.Status && Responding.Value?.Landed == true);
        BattleSpecialRules.RegisterTargets("Albedo_Owner()", (battle, owner) => owner?.IsAlive == true
            ? new List<int> { owner.SlotPosition } : new List<int>());
        BattleSpecialRules.RegisterTargets("Albedo_FlowerTarget()", (battle, owner) =>
        {
            var request = RequestFor(ScriptHookContext.Current);
            var targets = request?.Targets.Where(t => t.IsAlive && t.Side != owner.Side
                && battle.GetEntityByPosition(t.Side, t.SlotPosition) == t).OrderBy(t => t.SlotPosition).ToList();
            if (targets == null || targets.Count == 0) return new List<int>();
            return new List<int> { targets[targets.Count == 1 ? 0 : BattleRandom.NextInt(0, targets.Count)].SlotPosition };
        });
        BattleSpecialRules.RegisterCondition("Albedo_HasSolar", (a, c) => Empty(a) && FindSolar(c?.Caster) != null);
        BattleSpecialRules.RegisterCondition("Albedo_HasSilver", (a, c) => Empty(a) && c?.Caster?.EntityID == 1034
            && c.Caster.GetStatusInstances(Silver).Any(Active));
        BattleSpecialRules.RegisterCondition("Albedo_LowHP", (a, c) => Empty(a) && c?.Target?.IsAlive == true
            && c.Target.CurrentHP < c.Target.TotalHP * .5f);
        BattleSpecialRules.RegisterCondition("Albedo_BurstDestroyedSilver", (a, c) => Empty(a)
            && c?.Caster?.EntityID == 1034 && c.Caster.IsAlive && c.Caster.ConstellationLevel >= 6
            && c.Status?.StatusID2 == Silver && c.Declaration?.Kind == BattleEventKind.StatusRemoved
            && c.Declaration.Get<StatusEventData>() is StatusEventData fact && fact.InstanceId == c.Status.ApplyOrder
            && fact.Reason == "Removed" && Burst(c.Caster) != null);
        BattleSpecialRules.RegisterBuffScalar("Albedo_Defense()", (effect, state) => Coefficient(effect) * state.Caster.GetFinalDEF());
        BattleSpecialRules.RegisterBuffScalar("Albedo_Consumed()", (effect, state) =>
            Coefficient(effect) * (Burst(state.Caster)?.Consumed ?? 0) * state.Caster.GetFinalDEF());
        BattleSpecialRules.RegisterDamagePreparation(C4Rule, PrepareNormal);
        BattleSpecialRules.RegisterCondition("Albedo_NormalBonus", (a, c) => Empty(a) && c?.Caster?.IsAlive == true
            && c.DamageType == "Normal" && NormalBonus(c.Caster, c.Recipient));
        BattleSpecialRules.RegisterCondition("Albedo_C6Team", (a, c) => Empty(a) && TeamBonus(c?.Caster));
    }
    private static float Coefficient(StatusEffectData effect)
        => float.TryParse(effect.Param1, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
            ? value : throw new InvalidOperationException("阿贝多加算缺少合法的防御系数。");
    private static bool Empty(string arg) => string.IsNullOrEmpty(arg);
    internal static bool Active(StatusInstance state) => state?.IsActive == true && state.Caster?.IsAlive == true
        && StatusHostAccess.Contains(state.Host, state);
    internal static void RequireOwner(StatusInstance state, object host)
    {
        if (!(host is BattleEntity owner) || state.Caster != owner || owner.EntityID != 1034)
            throw new InvalidOperationException("阿贝多专属计数与瑰银必须由本人持有。");
    }
    internal static StatusInstance FindSolar(BattleEntity owner)
    {
        var field = BattleManager.Instance?.Field;
        if (owner?.EntityID != 1034 || !owner.IsAlive || field == null) return null;
        var slots = owner.Side == BattleSide.Ally ? field.EnemySlots : field.AllySlots;
        return slots.SelectMany(slot => slot.StatusList).FirstOrDefault(s => s.StatusID2 == Solar && s.Caster == owner && Active(s));
    }
    private static Cast Burst(BattleEntity owner) => Casting.Value?.Owner == owner
        && Casting.Value.Skill == "SK_Burst_Albedo" ? Casting.Value : null;
    internal static void SaveConsumption(BattleEntity owner, int value)
    { var cast = Burst(owner); if (cast != null) cast.Consumed = value; }
    private static object NormalScope => (object)BattleResolutionContext.CurrentAction
        ?? BattleResolutionContext.CurrentEffect ?? (object)Hitting.Value;
    private static CountDamageContribution PrepareNormal(StatusInstance state, object host, CountDamageContext context)
    {
        if (!Active(state) || context.Attacker?.Side != state.Caster.Side || context.Attacker.CharacterCtrl == null
            || context.Target?.IsAlive != true || context.Target.Side == state.Caster.Side || context.DamageType != "Normal") return null;
        object scope = NormalScope;
        if (scope == null) return null;
        var claims = NormalClaims.GetValue(scope, _ => new Dictionary<(StatusInstance, BattleEntity), bool>());
        var key = (state, context.Attacker);
        if (claims.ContainsKey(key)) return null;
        var silver = state.Caster.GetStatusInstances(Silver).FirstOrDefault(Active);
        claims.Add(key, silver != null);
        if (silver != null) state.Caster.RemoveStatusInstance(silver, "AlbedoNormalConsumed");
        return null; // 普攻增伤由绑定的Buff配表提供；属性查询不消耗瑰银。
    }
    private static bool NormalBonus(BattleEntity owner, BattleEntity recipient)
    {
        object scope = NormalScope;
        var root = owner.GetStatus(C4Rule);
        return root != null && Active(root) && scope != null && recipient != null
            && NormalClaims.TryGetValue(scope, out var claims) && claims.TryGetValue((root, recipient), out bool applies) && applies;
    }
    private static bool TeamBonus(BattleEntity owner)
    {
        var battle = BattleManager.Instance;
        if (owner?.IsAlive != true || battle == null) return false;
        if (battle.TeamBonuses?.Contains(owner) == true && battle.TeamBonuses.Resonance.HasEnemyFieldMoonCage) return true;
        return battle.Allies.Any(a => a?.Entity?.IsAlive == true && a.Entity.Side == owner.Side
            && a.Entity.Shields.Any(s => s.Kind == ShieldKind.Crystallize && s.Value > 0 && s.Duration > 0));
    }
    private static Request RequestFor(ScriptHookContext c)
        => c?.Declaration != null && Active(c.Status) && Requests.TryGetValue(c.Declaration, out var requests)
            ? requests.Find(r => r.Root == c.Status) : null;
    private static void Index(BattleDeclaration declaration)
    {
        var frame = Hitting.Value;
        var hit = declaration.Get<DamageEventData>(); var source = hit?.Source;
        var response = Responding.Value;
        if (response != null && hit?.HitLanded == true && source?.CountsAsHit == true && source.SourceStatus == response.State
            && (source.SourceEffectID == "STE_Albedo_SolarDamage" || source.SourceEffectID == "STE_Albedo_SilverDamage"))
            response.Landed = true;
        if (frame == null || frame.ID != declaration.Header.HitId || hit?.HitLanded != true
            || source?.CountsAsHit != true || source.SourceEntity != frame.Source || hit.Resonance != null
            || hit.Subject == null || hit.Subject.Side == source.SourceSide) return;
        foreach (var request in frame.Candidates)
        {
            if (!Active(request.Root) || request.Root == source.SourceStatus) continue;
            if (request.Root.StatusID2 == Silver && source.SourceStatus?.StatusID2 == Silver) continue;
            request.Cause = request.Cause ?? declaration;
            if (!request.Targets.Contains(hit.Subject)) request.Targets.Add(hit.Subject);
        }
    }
    private static void Queue(Request request)
    {
        var state = request.Root;
        Requests.GetValue(request.Cause, _ => new List<Request>()).Add(request);
        if (!DataManager.Instance.StatusActionDict.TryGetValue(state.StatusID2, out var actions)) return;
        foreach (var action in actions)
            if (action.ActionType == "OnTrigger" && action.ScriptHook == "Albedo_FlowerHit()")
                state.Caster.CharacterCtrl.TryExecuteEventStatusAction(state, action, state.Host,
                    new ScriptHookContext { Caster = state.Caster, Status = state, StatusHost = state.Host, Declaration = request.Cause },
                    () => Active(state));
    }
}

/// <summary>生灭计数：初值0，普通阳华获取上限4；六命每份瑰银+4不受此途径上限限制。
/// ApplyStatus只建立/刷新7回合；Count只修改数值。爆发clear前保留本次消费数，0不删除。</summary>
public sealed class AlbedoReckoningCount : CountStatusHandler
{
    public override bool SupportsEffectChange => true;
    public override bool EffectUsesIntegerValue => true;
    public override void OnCreated(StatusInstance state, object host)
    { AlbedoScriptHooks.RequireOwner(state, host); state.RuntimeIntValue = 0; state.StackCount = 1; }
    public override StatusInstance OnReapplied(StatusInstance state, BattleEntity host, CountStatusApplication application)
    {
        AlbedoScriptHooks.RequireOwner(state, host);
        if (application.Caster != host || !string.IsNullOrEmpty(application.Param2))
            throw new InvalidOperationException("生灭计数重施仅接受本人施加，数值请填写Count效果。");
        bool changed = state.AddInPhase != application.AddInPhase || state.TriggerPhase != application.TriggerPhase;
        if (changed) BattleManager.Instance?.UnregisterStatusTick(state);
        state.IsPermanent = application.Duration <= 0; state.RemainingPhaseCount = application.Duration;
        state.AddInPhase = application.AddInPhase; state.TriggerPhase = application.TriggerPhase;
        if (changed) BattleManager.Instance?.RegisterStatusTick(state, host, null);
        return state;
    }
    public override void ChangeFromEffect(StatusInstance state, BattleEntity host, CountChangeRequest request)
    {
        ValidateEffectChange(state, host, request); AlbedoScriptHooks.RequireOwner(state, host);
        if (request.EffectID == "SE_Burst_AlbedoConsume" && request.Operation == CountChangeOperation.Clear)
            AlbedoScriptHooks.SaveConsumption(host, state.RuntimeIntValue);
        if (request.EffectID == "STE_Albedo_SolarCount" && request.Operation == CountChangeOperation.Add && request.Delta > 0)
        {
            if (state.RuntimeIntValue >= 4) return; // 不裁掉六命已给出的超额层数。
            request.ApplyInteger(state, host, 0, 4);
        }
        else request.ApplyInteger(state, host, 0, int.MaxValue);
    }
    public override void OnRemoved(StatusInstance state, object host)
    {
        CountChangeRequest.Parse("clear", state.Caster).ApplyInteger(state, host as BattleEntity, 0, int.MaxValue);
    }
}
