using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

/// <summary>七七只索引命中前资格和真实声明；治疗、挂符、回能、复活及限次使用既有配表执行入口。</summary>
public static class QiqiScriptHooks
{
    public const string Herald = "ST_Qiqi_Herald", Talisman = "ST_Qiqi_Talisman";
    public const string Life = "ST_Qiqi_LifeProlongation", Arcanum = "ST_Qiqi_Arcanum", Resurrection = "ST_Qiqi_Resurrection";
    public const string ColdDamage = "STE_Qiqi_ColdDamage";
    private static readonly AsyncLocal<HitFrame> Hitting = new AsyncLocal<HitFrame>();
    private static readonly ConditionalWeakTable<BattleDeclaration, HitFrame> Hits = new ConditionalWeakTable<BattleDeclaration, HitFrame>();
    private static readonly ConditionalWeakTable<StatusInstance, IDisposable> Listeners = new ConditionalWeakTable<StatusInstance, IDisposable>();
    private sealed class HitFrame : IDisposable
    {
        internal readonly BattleEntity Owner;
        internal readonly StatusInstance Herald;
        internal long FirstNormal;
        internal readonly HashSet<BattleEntity> Cryo = new HashSet<BattleEntity>();
        internal readonly HashSet<BattleEntity> Marked = new HashSet<BattleEntity>();
        internal readonly HashSet<BattleEntity> NormalTargets = new HashSet<BattleEntity>();
        private readonly HitFrame previous = Hitting.Value;
        internal HitFrame(BattleEntity owner, IEnumerable<BattleEntity> targets)
        {
            Owner = owner; Hitting.Value = this;
            if (owner?.EntityID != 1027 || targets == null) return;
            Herald = owner.GetStatus(QiqiScriptHooks.Herald);
            foreach (var target in targets)
            {
                if (target == null || target.Side == owner.Side) continue;
                if (ElementQuery.HasAura(target, "Cryo")) Cryo.Add(target);
                foreach (var mark in target.GetStatusInstances(Talisman))
                    if (mark.IsActive && mark.Caster == owner) { Marked.Add(target); break; }
            }
        }
        public void Dispose() => Hitting.Value = previous;
    }
    internal static void Register()
    {
        BattleSpecialRules.RegisterHitScope((_, owner, targets) => new HitFrame(owner, targets));
        BattleSpecialRules.RegisterDeclarationObserver(Index);
        BattleSpecialRules.RegisterStatusLifecycle(Life, StartLife, Stop);
        BattleSpecialRules.RegisterStatusLifecycle(Resurrection, StartResurrection, Stop);
        BattleSpecialRules.RegisterCondition("Qiqi_HeraldHit", (a, c) => Empty(a) && FirstNormal(c, out var hit)
            && hit.Herald == c.Status && Active(c.Status));
        BattleSpecialRules.RegisterCondition("Qiqi_ArcanumHit", (a, c) => Empty(a) && FirstNormal(c, out _)
            && c.Status?.StatusID2 == Arcanum && NormalTargets(BattleManager.Instance, c.Caster).Count > 0);
        BattleSpecialRules.RegisterCondition("Qiqi_MarkedColdHit", (a, c) => Empty(a) && Frame(c, out var hit)
            && c.DamageEvent?.Source?.SourceEffectID == ColdDamage && hit.Marked.Contains(c.DamageEvent.Target));
        BattleSpecialRules.RegisterCondition("Qiqi_C2Cryo", (a, c) => Empty(a) && c?.Caster?.EntityID == 1027
            && Hitting.Value?.Owner == c.Caster && Hitting.Value.Cryo.Contains(c.Target));
        BattleSpecialRules.RegisterCondition("Qiqi_TalismanHit", (a, c) => Empty(a) && TalismanAttacker(c) != null);
        BattleSpecialRules.RegisterCondition("Qiqi_Reaction", (a, c) => Empty(a) && Reactor(c) != null);
        BattleSpecialRules.RegisterCondition("Qiqi_Revive", (a, c) => Empty(a) && Active(c?.Status)
            && c.Status.StatusID2 == Resurrection && c.Caster.ConstellationLevel >= 6
            && c.Declaration?.Get<ActionEventData>() is ActionEventData action && action.Subject == c.Caster
            && action.ActionType == "Burst" && action.SkillId == "SK_Burst_Qiqi"
            && Fallen(BattleManager.Instance, c.Caster).Count > 0);
        BattleSpecialRules.RegisterTargets("Qiqi_NormalTargets()", NormalTargets);
        BattleSpecialRules.RegisterTargets("Qiqi_TalismanAttacker()", (battle, owner) => Position(battle, owner, TalismanAttacker(ScriptHookContext.Current)));
        BattleSpecialRules.RegisterTargets("Qiqi_Reactor()", (battle, owner) => Position(battle, owner, Reactor(ScriptHookContext.Current)));
        BattleSpecialRules.RegisterTargets("Qiqi_FallenAllies()", Fallen);
    }
    private static bool Empty(string argument) => string.IsNullOrEmpty(argument);
    private static bool Active(StatusInstance status) => status?.IsActive == true && status.Caster?.IsAlive == true
        && status.Caster.EntityID == 1027 && StatusHostAccess.Contains(status.Host, status);
    private static bool Frame(ScriptHookContext context, out HitFrame frame)
    {
        frame = null;
        return context?.Caster?.EntityID == 1027 && context.DamageEvent?.Declaration != null
            && Hits.TryGetValue(context.DamageEvent.Declaration, out frame) && frame.Owner == context.Caster;
    }
    private static bool FirstNormal(ScriptHookContext context, out HitFrame frame)
        => Frame(context, out frame) && frame.FirstNormal != 0
            && frame.FirstNormal == context.DamageEvent.Declaration.Header.EventId;
    private static void Index(BattleDeclaration declaration)
    {
        var frame = Hitting.Value;
        if (frame?.Owner?.EntityID != 1027 || declaration?.Kind != BattleEventKind.DamageResolved) return;
        var hit = declaration.Get<DamageEventData>(); var source = hit?.Source;
        if (hit?.HitLanded != true || source?.SourceEntity != frame.Owner || !source.CountsAsHit || hit.Subject?.Side == frame.Owner.Side) return;
        Hits.Add(declaration, frame);
        if (hit.DamageType == "Normal" || hit.DamageType == "Heavy")
        {
            if (frame.FirstNormal == 0) frame.FirstNormal = declaration.Header.EventId;
            frame.NormalTargets.Add(hit.Subject);
        }
    }
    private static List<int> NormalTargets(BattleManager battle, BattleEntity owner)
    {
        var result = new List<int>();
        if (!Frame(ScriptHookContext.Current, out var frame) || frame.Owner != owner) return result;
        foreach (var target in frame.NormalTargets)
            if (target?.IsAlive == true && target.Side != owner.Side && battle?.GetEntityByPosition(target.Side, target.SlotPosition) == target)
                result.Add(target.SlotPosition);
        result.Sort(); return result;
    }
    private static BattleEntity TalismanAttacker(ScriptHookContext context)
    {
        var mark = context?.Status; var hit = context?.DamageEvent; var attacker = hit?.Source?.SourceEntity;
        return mark?.StatusID2 == Talisman && Active(mark) && hit?.HitLanded == true && hit.Source.CountsAsHit
            && mark.Host == hit.Target && attacker?.IsAlive == true && attacker.CharacterCtrl != null
            && attacker.Side == mark.Caster.Side ? attacker : null;
    }
    private static BattleEntity Reactor(ScriptHookContext context)
    {
        var fact = context?.Declaration?.Get<ReactionEventData>(); var actor = fact?.Source;
        return context?.Status?.StatusID2 == Life && Active(context.Status) && actor?.IsAlive == true
            && actor.CharacterCtrl != null && actor.Side == context.Caster.Side ? actor : null;
    }
    private static List<int> Position(BattleManager battle, BattleEntity owner, BattleEntity member)
        => member?.IsAlive == true && member.Side == owner?.Side
            && battle?.GetEntityByPosition(member.Side, member.SlotPosition) == member
            ? new List<int> { member.SlotPosition } : new List<int>();
    private static List<int> Fallen(BattleManager battle, BattleEntity owner)
    {
        var result = new List<int>();
        if (battle == null || owner?.IsAlive != true || owner.EntityID != 1027) return result;
        foreach (var ally in battle.Allies)
            if (ally?.Entity?.IsDead == true && ally.Entity.Side == owner.Side
                && battle.GetEntityByPosition(owner.Side, ally.Entity.SlotPosition) == ally.Entity) result.Add(ally.Entity.SlotPosition);
        result.Sort(); return result;
    }
    private static BattleEntity RequireOwner(StatusInstance status, object host)
    {
        if (!(host is BattleEntity owner) || status.Caster != owner || owner.EntityID != 1027)
            throw new InvalidOperationException("七七初始化监听必须挂在本人。");
        return owner;
    }
    private static void StartLife(StatusInstance status, object host)
    {
        var owner = RequireOwner(status, host);
        Listeners.Add(status, BattleEventHub.Subscribe<ReactionEventData>(BattleEventKind.ReactionOccurred, (_, fact) =>
        {
            if (fact.Source?.CharacterCtrl != null && fact.Source.Side == owner.Side)
                Request(status, "Qiqi_Reaction()", BattleEventHub.Current);
        }, status.ApplyOrder, () => Active(status)));
    }
    private static void StartResurrection(StatusInstance status, object host)
    {
        var owner = RequireOwner(status, host);
        Listeners.Add(status, BattleEventHub.Subscribe<ActionEventData>(BattleEventKind.ActionCompleted, (_, fact) =>
        {
            if (fact.Subject == owner && fact.ActionType == "Burst" && fact.SkillId == "SK_Burst_Qiqi")
                Request(status, "Qiqi_Revive()", BattleEventHub.Current);
        }, status.ApplyOrder, () => Active(status)));
    }
    private static void Request(StatusInstance status, string hook, BattleDeclaration declaration)
    {
        if (!Active(status) || declaration == null || !DataManager.Instance.StatusActionDict.TryGetValue(status.StatusID2, out var actions)) return;
        foreach (var action in actions)
            if (action.ActionType == "OnTrigger" && action.ScriptHook == hook)
                status.Caster.CharacterCtrl.TryExecuteEventStatusAction(status, action, status.Host,
                    new ScriptHookContext { Caster = status.Caster, Status = status, StatusHost = status.Host, Declaration = declaration }, () => Active(status));
    }
    private static void Stop(StatusInstance status, object host)
    {
        if (Listeners.TryGetValue(status, out var listener)) listener.Dispose();
        Listeners.Remove(status);
    }
}
