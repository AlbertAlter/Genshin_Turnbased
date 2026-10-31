using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

/// <summary>
/// 达达利亚：命中前断流、整hit共用斩额度、命中后挂标记，以及近战具体实例。
/// 伤害/能量/冷却数值由角色效果表提供；不复制伤害公式，不产生额外主动行为。
/// </summary>
public static class TartagliaScriptHooks
{
    public const string Rules = "ST_Tartaglia_Rules", Mark = "ST_Tartaglia_Riptide";
    public const string Melee = "ST_Tartaglia_Melee", Ready = "ST_Tartaglia_C6Ready";
    public const string Charge = "ST_Tartaglia_Charge", ChargeTarget = "ST_Tartaglia_ChargeTarget";
    private sealed class OwnerMemory
    {
        internal int SlashTurn = -1, SlashCount, EnergyTurn = -1;
    }
    private sealed class MarkMemory { internal IDisposable Listener; internal bool Died; }
    private sealed class Fact
    {
        internal Hit Frame;
        internal bool HadMark, WasMelee;
    }
    private sealed class Restore : IDisposable
    {
        private Action callback;
        internal Restore(Action action) { callback = action; }
        public void Dispose() { var action = callback; callback = null; action?.Invoke(); }
    }
    private sealed class Cast : IDisposable
    {
        internal readonly BattleEntity Owner;
        internal readonly string Skill;
        internal readonly List<StatusInstance> Markers = new List<StatusInstance>();
        private readonly Cast previous = Casting.Value;
        internal Cast(BattleEntity owner, SkillMainData skill) { Owner = owner; Skill = skill?.SkillID2; Casting.Value = this; }
        public void Dispose() => Casting.Value = previous;
    }
    private sealed class Hit : IDisposable
    {
        internal readonly BattleEntity Owner;
        internal readonly HashSet<BattleEntity> Marked = new HashSet<BattleEntity>();
        internal readonly bool WasMelee;
        internal bool? SlashAllowed;
        private readonly Hit previous = Hitting.Value;
        internal Hit(BattleEntity owner, IEnumerable<BattleEntity> targets)
        {
            Owner = owner; WasMelee = InMelee(owner); Hitting.Value = this;
            if (owner?.EntityID == 1030 && targets != null)
                foreach (var target in targets)
                    if (target?.GetStatusInstances(Mark).Any(s => Active(s) && s.Caster == owner) == true) Marked.Add(target);
        }
        public void Dispose() => Hitting.Value = previous;
    }
    private sealed class Response : IDisposable
    {
        internal readonly StatusInstance Status;
        internal bool RiptideHit;
        private readonly bool isBlast;
        private readonly Response previous = Responding.Value;
        internal Response(StatusInstance status)
        {
            Status = status; Responding.Value = this;
            isBlast = status?.StatusID2 == Mark && ScriptHookContext.Current?.EventHookName == "Tartaglia_Blast";
        }
        public void Dispose()
        {
            Responding.Value = previous;
            // 普通RemoveStatus的单位选择只包含存活目标；爆击败中心后仍正常清掉该具体标记。
            if (isBlast && Active(Status) && Status.Host is BattleEntity target && !target.IsAlive)
                StatusHostAccess.Remove(target, Status, "Removed");
        }
    }
    private static readonly ConditionalWeakTable<BattleEntity, OwnerMemory> Owners = new ConditionalWeakTable<BattleEntity, OwnerMemory>();
    private static readonly ConditionalWeakTable<StatusInstance, MarkMemory> Marks = new ConditionalWeakTable<StatusInstance, MarkMemory>();
    private static readonly ConditionalWeakTable<StatusInstance, IDisposable> Listeners = new ConditionalWeakTable<StatusInstance, IDisposable>();
    private static readonly ConditionalWeakTable<BattleDeclaration, Fact> Facts = new ConditionalWeakTable<BattleDeclaration, Fact>();
    private static readonly AsyncLocal<Cast> Casting = new AsyncLocal<Cast>();
    private static readonly AsyncLocal<Hit> Hitting = new AsyncLocal<Hit>();
    private static readonly AsyncLocal<Response> Responding = new AsyncLocal<Response>();
    private static readonly AsyncLocal<string> Executing = new AsyncLocal<string>();
    private static int Turn => BattleManager.Instance?.TurnCount ?? 0;
    private static bool Active(StatusInstance status) => status?.IsActive == true && status.Caster?.IsAlive == true
        && status.Caster.EntityID == 1030 && StatusHostAccess.Contains(status.Host, status);
    internal static bool InMelee(BattleEntity owner) => owner?.EntityID == 1030 && owner.GetStatusInstances(Melee).Any(Active);
    private static bool Talent(BattleEntity owner, int number) => owner?.EntityID == 1030
        && ScriptHookEvaluator.Evaluate("Check(1030_T" + number + ")", owner);
    private static bool Empty(string value) => string.IsNullOrEmpty(value);
    private static bool MeleeAttack(string effect) => effect == "SE_MeleeNormal_Tartaglia" || effect == "SE_MeleeHeavy_Tartaglia";

    internal static void Register()
    {
        // 基础断流规则不依赖突破/命座。正式开战和复活均保留正常初始化生命周期。
        BattleSpecialRules.RegisterBattleReady(battle =>
        {
            foreach (var controller in battle.Allies)
            {
                var owner = controller?.Entity;
                if (owner?.EntityID != 1030 || !owner.IsAlive || owner.HasStatus(Rules)) continue;
                if (!DataManager.Instance.StatusMainDict.TryGetValue(Rules, out var main))
                    throw new InvalidOperationException("达达利亚缺少基础断流规则状态。");
                var status = owner.AddStatus(Rules, owner, 0, 2, 0, main);
                controller.RecordCharacterInitializationStatus(status, 0);
            }
        });
        BattleSpecialRules.RegisterStatusLifecycle(Rules, StartRules, StopRules);
        BattleSpecialRules.RegisterStatusLifecycle(Mark, StartMark, StopMark);
        BattleSpecialRules.RegisterStatusLifecycle(ChargeTarget, (state, host) =>
        {
            if (Casting.Value?.Owner == state.Caster && Casting.Value.Skill == "SK_Heavy_Tartaglia") Casting.Value.Markers.Add(state);
        }, null);
        BattleSpecialRules.RegisterStatusLifecycle(Charge, (state, host) =>
        {
            var cast = Casting.Value;
            if (cast?.Owner != state.Caster || cast.Skill != "SK_Heavy_Tartaglia")
                throw new InvalidOperationException("达达利亚蓄力必须保留本次手选位置。");
            foreach (var marker in cast.Markers) if (StatusHostAccess.Contains(marker.Host, marker)) state.BoundStatuses.Add(marker);
        }, null);
        BattleSpecialRules.RegisterStatusLifecycle(Ready, (state, host) =>
        {
            var root = state.Caster?.GetStatusInstances(Melee).FirstOrDefault(Active);
            if (root == null) throw new InvalidOperationException("达达利亚六命必须绑定本次近战状态。");
            root.BoundStatuses.Add(state);
        }, null);
        BattleSpecialRules.RegisterSkillExecutionScope((owner, skill) => new Cast(owner, skill));
        BattleSpecialRules.RegisterHitScope((id, owner, targets) => new Hit(owner, targets));
        BattleSpecialRules.RegisterStatusActionScope(status => new Response(status));
        BattleSpecialRules.RegisterEffectInvocationScope((owner, effect, execution) =>
        {
            string previous = Executing.Value; Executing.Value = effect;
            return new Restore(() => Executing.Value = previous);
        });
        BattleSpecialRules.RegisterDeclarationObserver(Index);
        // 两种爆发共用一次全敌确认；预选不求值ScriptHook，近战分支不能再弹一次确认。
        BattleSpecialRules.RegisterAutomaticSkillTargets(effect => effect?.SkillEffectID2 == "SE_Burst_TartagliaMelee");
        foreach (string name in new[] { "Flash", "Slash", "Blast", "Death", "ApplyMark" })
        {
            string kind = name;
            BattleSpecialRules.RegisterCondition("Tartaglia_" + kind, (a, c) => Empty(a) && Active(c?.Status)
                && c.EventHookName == "Tartaglia_" + kind && c.Declaration != null);
        }
        BattleSpecialRules.RegisterCondition("Tartaglia_Melee", (a, c) => Empty(a) && InMelee(c?.Caster));
        BattleSpecialRules.RegisterCondition("Tartaglia_Ranged", (a, c) => Empty(a) && c?.Caster?.EntityID == 1030 && !InMelee(c.Caster));
        BattleSpecialRules.RegisterCondition("Tartaglia_NoT1", (a, c) => Empty(a) && c?.Caster?.EntityID == 1030 && !Talent(c.Caster, 1));
        BattleSpecialRules.RegisterCondition("Tartaglia_NoC1", (a, c) => Empty(a) && c?.Caster?.EntityID == 1030 && c.Caster.ConstellationLevel < 1);
        BattleSpecialRules.RegisterCondition("Tartaglia_MeleeRound", (a, c) => Empty(a) && Active(c?.Status)
            && c.Status.StatusID2 == Melee && c.Status.RemainingPhaseCount > 1);
        BattleSpecialRules.RegisterCondition("Tartaglia_ResetOnExit", (a, c) => Empty(a) && InMelee(c?.Caster)
            && c.Caster.GetStatusInstances(Melee).Any(root => Active(root) && root.BoundStatuses.Any(s => s.StatusID2 == Ready && Active(s))));
        BattleSpecialRules.RegisterCondition("Tartaglia_LivingMark", (a, c) => Empty(a) && Active(c?.Status)
            && c.Status.StatusID2 == Mark && c.Status.Host is BattleEntity target && target.IsAlive);
        BattleSpecialRules.RegisterCondition("Tartaglia_RiptideEnergy", (a, c) => Empty(a) && c?.Caster?.EntityID == 1030
            && Responding.Value?.Status == c.Status && Responding.Value.RiptideHit
            && Owners.GetValue(c.Caster, _ => new OwnerMemory()).EnergyTurn != Turn);
        BattleSpecialRules.RegisterTargets("Tartaglia_EventTarget()", (_, owner) => EventTarget(owner));
        BattleSpecialRules.RegisterTargets("Tartaglia_Center()", (_, owner) => Center(owner));
        BattleSpecialRules.RegisterTargets("Tartaglia_Area()", (_, owner) => Area(owner, true));
        BattleSpecialRules.RegisterTargets("Tartaglia_Neighbors()", (_, owner) => Area(owner, false));
    }

    private static void Index(BattleDeclaration declaration)
    {
        var damage = declaration.Get<DamageEventData>();
        if (damage != null)
        {
            var owner = damage.Source.SourceEntity; var frame = Hitting.Value;
            if (owner?.EntityID == 1030 && damage.Source.CountsAsHit && damage.HitLanded)
            {
                Facts.Add(declaration, new Fact { Frame = frame?.Owner == owner ? frame : null,
                    HadMark = frame?.Owner == owner && frame.Marked.Contains(damage.Subject),
                    WasMelee = frame?.Owner == owner ? frame.WasMelee : InMelee(owner) });
                var response = Responding.Value;
                if (response?.Status == damage.Source.SourceStatus)
                {
                    if (damage.Source.SourceEffectID == "SE_Tartaglia_Flash" || damage.Source.SourceEffectID == "SE_Tartaglia_Slash")
                        response.RiptideHit = true;
                }
            }
        }
        var resource = declaration.Get<ResourceEventData>();
        // GainEnergy即使已满也提交Gain声明；次数由实际执行登记，查询条件不消费额度。
        if (resource?.Resource == "Energy" && resource.Subject?.EntityID == 1030 && Executing.Value == "SE_Tartaglia_RiptideEnergy")
            Owners.GetValue(resource.Subject, _ => new OwnerMemory()).EnergyTurn = Turn;
    }
    private static void StartRules(StatusInstance status, object host)
    {
        if (status.Caster?.EntityID != 1030 || host != status.Caster) throw new InvalidOperationException("断流规则必须挂在达达利亚本人。");
        Owners.Remove(status.Caster);
        Listeners.Add(status, BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, damage) =>
        {
            var cause = BattleEventHub.Current;
            if (damage.Source.SourceEntity != status.Caster || !damage.HitLanded || !damage.Source.CountsAsHit
                || damage.Subject?.Side == status.Caster.Side || !Facts.TryGetValue(cause, out var fact)) return;
            string effect = damage.Source.SourceEffectID;
            bool mark = effect == "SE_Tartaglia_ChargeAttack" || effect == "SE_Burst_TartagliaRanged" || effect == "SE_Tartaglia_Death"
                || (MeleeAttack(effect) && fact.WasMelee && Talent(status.Caster, 2) && damage.Critical?.IsCritical == true);
            if (!mark) return;
            // 同层及其派生响应全部结清再挂/刷新；断流破传播也不能替换邻位尚待执行爆/斩的旧标记。
            BattleResolutionContext.AfterResponses(() =>
                BattleResolutionContext.AfterSettlement(() => Request(status, "ApplyMark", cause, damage.Subject)));
        }, status.ApplyOrder, () => Active(status)));
    }
    private static void StopRules(StatusInstance status, object host)
    {
        if (Listeners.TryGetValue(status, out var listener)) listener.Dispose();
        Listeners.Remove(status);
        if (status.Caster != null) Owners.Remove(status.Caster);
    }
    private static void StartMark(StatusInstance status, object host)
    {
        if (status.Caster?.EntityID != 1030 || !(host is BattleEntity target) || target.Side == status.Caster.Side)
            throw new InvalidOperationException("断流必须挂在敌方单位。");
        var memory = new MarkMemory(); Marks.Add(status, memory);
        memory.Listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, damage) =>
        {
            if (damage.Subject != target) return;
            var cause = BattleEventHub.Current;
            if (damage.CausedDeath && !memory.Died)
            {
                memory.Died = true;
                Request(status, "Death", cause, target);
            }
            if (damage.Source.SourceEntity != status.Caster || !damage.Source.CountsAsHit || !damage.HitLanded
                || !Facts.TryGetValue(cause, out var fact) || !fact.HadMark) return;
            string effect = damage.Source.SourceEffectID;
            if (effect == "SE_Tartaglia_ChargeAttack") Request(status, "Flash", cause, target);
            if (MeleeAttack(effect) && fact.WasMelee && SpendSlash(status.Caster, fact.Frame)) Request(status, "Slash", cause, target);
            if (effect == "SE_Burst_TartagliaMelee") Request(status, "Blast", cause, target);
        }, status.ApplyOrder, () => Active(status));
    }
    private static void StopMark(StatusInstance status, object host)
    { if (Marks.TryGetValue(status, out var memory)) memory.Listener?.Dispose(); Marks.Remove(status); }
    private static bool SpendSlash(BattleEntity owner, Hit hit)
    {
        if (hit == null || hit.Owner != owner) return false;
        if (hit.SlashAllowed.HasValue) return hit.SlashAllowed.Value;
        var memory = Owners.GetValue(owner, _ => new OwnerMemory());
        if (memory.SlashTurn != Turn) { memory.SlashTurn = Turn; memory.SlashCount = 0; }
        bool allowed = memory.SlashCount < 2;
        if (allowed) memory.SlashCount++;
        hit.SlashAllowed = allowed;
        return allowed;
    }
    private static void Request(StatusInstance status, string kind, BattleDeclaration cause, BattleEntity target)
    {
        if (kind == "ApplyMark" && target?.IsAlive != true) return;
        if (!Active(status) || cause == null || !DataManager.Instance.StatusActionDict.TryGetValue(status.StatusID2, out var actions)) return;
        foreach (var action in actions)
            if (action.ActionType == "OnTrigger" && action.ScriptHook == "Tartaglia_" + kind + "()")
                status.Caster.CharacterCtrl.TryExecuteEventStatusAction(status, action, status.Host,
                    new ScriptHookContext { Caster = status.Caster, Status = status, StatusHost = status.Host,
                        Declaration = cause, Target = target, EventHookName = "Tartaglia_" + kind }, () => Active(status));
    }
    private static List<int> EventTarget(BattleEntity owner)
    {
        var context = ScriptHookContext.Current; var target = context?.Target;
        return context?.Caster == owner && target?.IsAlive == true && target.Side != owner.Side
            ? new List<int> { target.SlotPosition } : new List<int>();
    }
    private static List<int> Center(BattleEntity owner)
    {
        var state = ScriptHookContext.Current?.Status;
        return state?.Caster == owner && state.StatusID2 == Mark && state.Host is BattleEntity target
            ? new List<int> { target.SlotPosition } : new List<int>();
    }
    private static List<int> Area(BattleEntity owner, bool includeCenter)
    {
        var center = Center(owner); var result = new List<int>();
        if (center.Count == 0) return result;
        int count = BattlePositionSystem.GetSideCount(owner.Side == BattleSide.Ally ? BattleSide.Enemy : BattleSide.Ally);
        for (int position = Math.Max(1, center[0] - 1); position <= Math.Min(count, center[0] + 1); position++)
            if (includeCenter || position != center[0]) result.Add(position);
        return result;
    }
}
