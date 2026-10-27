using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

/// <summary>菲谢尔的场地奥兹、完整行为命中池与整效果反应池。数值、概率及状态赋予仍由配表执行。</summary>
public static class FischlScriptHooks
{
    public const string Oz = "ST_Fischl_Oz", EnemyEnd = "ST_Fischl_OzEnemyEnd";
    public const string Charge = "ST_Fischl_Charge", ChargeTarget = "ST_Fischl_ChargeTarget";
    public const string C1 = "ST_Fischl_C1";
    private static readonly AsyncLocal<Cast> Casting = new AsyncLocal<Cast>();
    private static readonly AsyncLocal<Invocation> Invoking = new AsyncLocal<Invocation>();
    private static readonly AsyncLocal<StatusFrame> Responding = new AsyncLocal<StatusFrame>();
    private static readonly ConditionalWeakTable<StatusInstance, Memory> Memories = new ConditionalWeakTable<StatusInstance, Memory>();
    private static readonly ConditionalWeakTable<BattleDeclaration, Cast> Actions = new ConditionalWeakTable<BattleDeclaration, Cast>();
    private static readonly ConditionalWeakTable<BattleDeclaration, StatusInstance> HeavyHits = new ConditionalWeakTable<BattleDeclaration, StatusInstance>();
    private static readonly ConditionalWeakTable<BattleDeclaration, List<Group>> ReactionFacts = new ConditionalWeakTable<BattleDeclaration, List<Group>>();
    private static readonly ConditionalWeakTable<BattleDeclaration, List<Request>> Requests = new ConditionalWeakTable<BattleDeclaration, List<Request>>();

    private sealed class Cast : IDisposable
    {
        internal readonly BattleEntity Owner;
        internal readonly string Skill, Action;
        internal readonly bool HadOz;
        internal BattleEntity Primary;
        internal readonly HashSet<BattleEntity> Hits = new HashSet<BattleEntity>();
        internal readonly List<StatusInstance> Markers = new List<StatusInstance>();
        private readonly Cast previous = Casting.Value;
        internal Cast(BattleEntity owner, SkillMainData skill)
        { Owner = owner; Skill = skill?.SkillID2; Action = skill?.ActionType; HadOz = FindOz(owner) != null; Casting.Value = this; }
        public void Dispose() => Casting.Value = previous;
    }
    private sealed class StatusFrame : IDisposable
    {
        internal readonly StatusInstance Status;
        internal bool ShotHit;
        private readonly StatusFrame previous = Responding.Value;
        internal StatusFrame(StatusInstance status) { Status = status; Responding.Value = this; }
        public void Dispose() => Responding.Value = previous;
    }
    private sealed class Memory
    {
        internal readonly List<IDisposable> Listeners = new List<IDisposable>();
        internal readonly HashSet<long> HeavyHitIDs = new HashSet<long>();
        internal readonly Dictionary<long, Group> Fallback = new Dictionary<long, Group>();
    }
    private sealed class Group
    {
        internal StatusInstance Root;
        internal BattleDeclaration Cause, Overload, Conduct;
        internal bool Managed, Submitted;
        internal readonly HashSet<BattleEntity> Targets = new HashSet<BattleEntity>();
        internal void Submit()
        {
            if (Submitted) return; Submitted = true;
            if (!Active(Root)) return;
            if (Cause != null && Talent(Root.Caster, 2)) Queue(Root, "A4", Cause, Targets);
            if (Overload != null) Queue(Root, "HexereiATK", Overload, null);
            if (Conduct != null) Queue(Root, "HexereiEM", Conduct, null);
        }
    }
    private sealed class Invocation : IDisposable
    {
        internal readonly string Effect;
        internal readonly long ID;
        internal readonly Dictionary<StatusInstance, Group> Groups = new Dictionary<StatusInstance, Group>();
        private readonly Invocation previous = Invoking.Value;
        internal Invocation(string effect, long id)
        {
            Effect = effect; ID = id; Invoking.Value = this;
            if (effect == "STE_Fischl_OzDamage" && Responding.Value != null) Responding.Value.ShotHit = false;
        }
        public void Dispose()
        {
            Invoking.Value = previous;
            using var collection = BattleResolutionContext.BeginCollection();
            foreach (var group in Groups.Values) group.Submit();
        }
    }
    private sealed class Request
    {
        internal StatusInstance Status;
        internal string Kind;
        internal List<BattleEntity> Targets;
        internal BattleEntity Chosen;
    }

    internal static void Register()
    {
        BattleSpecialRules.RegisterSkillExecutionScope((owner, skill) => new Cast(owner, skill));
        BattleSpecialRules.RegisterStatusActionScope(status => new StatusFrame(status));
        BattleSpecialRules.RegisterEffectInvocationScope((owner, effect, execution) => new Invocation(effect, execution));
        BattleSpecialRules.RegisterDeclarationObserver(Index);
        BattleSpecialRules.RegisterAutomaticSkillTargets(effect => effect?.SkillEffectID2 == "SE_Skill_FischlDamage"
            || effect?.SkillEffectID2 == "SE_Skill_FischlC2Damage" || effect?.SkillEffectID2 == "SE_Burst_FischlDamage"
            || effect?.SkillEffectID2 == "SE_Burst_FischlC4Damage");
        BattleSpecialRules.RegisterStatusLifecycle(Oz, StartOz, Stop);
        BattleSpecialRules.RegisterStatusLifecycle(C1, StartC1, Stop);
        BattleSpecialRules.RegisterStatusLifecycle(ChargeTarget, (status, host) =>
        {
            if (Casting.Value?.Owner == status.Caster && Casting.Value.Skill == "SK_Heavy_Fischl") Casting.Value.Markers.Add(status);
        }, null);
        BattleSpecialRules.RegisterStatusLifecycle(Charge, (status, host) =>
        {
            var cast = Casting.Value;
            if (cast?.Owner != status.Caster || cast.Skill != "SK_Heavy_Fischl")
                throw new InvalidOperationException("菲谢尔蓄力必须保留本次手选的场地标记。");
            foreach (var marker in cast.Markers) if (StatusHostAccess.Contains(marker.Host, marker)) status.BoundStatuses.Add(marker);
        }, null);
        foreach (string kind in new[] { "A1", "A4", "C1", "C6", "HexereiATK", "HexereiEM" })
        {
            string captured = kind;
            BattleSpecialRules.RegisterCondition("Fischl_" + kind, (a, c) => Empty(a) && GetRequest(c, captured) != null);
        }
        BattleSpecialRules.RegisterCondition("Fischl_C4Oz", (a, c) => Empty(a) && c?.Caster?.EntityID == 1024
            && c.Caster.ConstellationLevel >= 4 && Casting.Value?.Owner == c.Caster
            && Casting.Value.Skill == "SK_Burst_Fischl" && Casting.Value.HadOz);
        BattleSpecialRules.RegisterCondition("Fischl_NoC2", (a, c) => Empty(a) && c?.Caster?.EntityID == 1024
            && c.Caster.ConstellationLevel < 2);
        BattleSpecialRules.RegisterCondition("Fischl_OzHit", (a, c) => Empty(a) && Responding.Value?.Status == c?.Status
            && Root(c?.Status) != null && Responding.Value.ShotHit);
        BattleSpecialRules.RegisterCondition("Fischl_C6Attacked", (a, c) => Empty(a) && GetRequest(c, "C6")?.Chosen != null);
        BattleSpecialRules.RegisterTargets("Fischl_OzArea()", (battle, owner) => Area(battle, owner, FindOz(owner)));
        BattleSpecialRules.RegisterTargets("Fischl_OzTarget()", OzTarget);
        foreach (string kind in new[] { "A4", "C1", "C6" })
        {
            string captured = kind;
            BattleSpecialRules.RegisterTargets("Fischl_" + kind + "Target()", (battle, owner) => RequestTarget(battle, owner, captured));
        }
    }
    private static bool Empty(string argument) => string.IsNullOrEmpty(argument);
    private static bool Talent(BattleEntity owner, int n) => owner?.EntityID == 1024
        && ScriptHookEvaluator.Evaluate("Check(1024_T" + n + ")", owner);
    private static bool Active(StatusInstance status) => status?.Caster?.IsAlive == true && status.IsActive
        && StatusHostAccess.Contains(status.Host, status);
    private static bool Valid(BattleManager battle, BattleEntity owner, BattleEntity target)
        => battle != null && owner?.IsAlive == true && target?.IsAlive == true && target.Side != owner.Side
            && battle.GetEntityByPosition(target.Side, target.SlotPosition) == target;
    internal static StatusInstance FindOz(BattleEntity owner)
    {
        var field = BattleManager.Instance?.Field;
        if (field == null || owner?.EntityID != 1024 || !owner.IsAlive) return null;
        var slots = owner.Side == BattleSide.Ally ? field.EnemySlots : field.AllySlots;
        foreach (var slot in slots) foreach (var status in slot.StatusList)
            if (status?.StatusID2 == Oz && status.Caster == owner && Active(status)) return status;
        return null;
    }
    private static StatusInstance Root(StatusInstance status)
    {
        if (status?.StatusID2 == Oz) return Active(status) ? status : null;
        var root = status?.StatusID2 == EnemyEnd ? FindOz(status.Caster) : null;
        return root?.BoundStatuses.Contains(status) == true ? root : null;
    }
    private static List<int> Area(BattleManager battle, BattleEntity owner, StatusInstance root)
    {
        var result = new List<int>();
        if (root?.Host is FieldPosition slot && Active(root))
            for (int p = Math.Max(1, slot.SlotIndex - 1); p <= Math.Min(BattlePositionSystem.GetSideCount(slot.Side), slot.SlotIndex + 1); p++)
                result.Add(p);
        return result;
    }
    private static List<int> OzTarget(BattleManager battle, BattleEntity owner)
    {
        var root = Root(ScriptHookContext.Current?.Status);
        if (root?.Caster != owner) return new List<int>();
        var positions = owner.ConstellationLevel >= 2 ? new List<int>() : Area(battle, owner, root);
        if (owner.ConstellationLevel >= 2)
            for (int p = 1; p <= BattlePositionSystem.GetSideCount(((FieldPosition)root.Host).Side); p++) positions.Add(p);
        var targets = new List<BattleEntity>();
        foreach (int p in positions)
        {
            var target = battle.GetEntityByPosition(((FieldPosition)root.Host).Side, p);
            if (Valid(battle, owner, target)) targets.Add(target);
        }
        if (targets.Count == 0) return new List<int>();
        return new List<int> { targets[targets.Count == 1 ? 0 : BattleRandom.NextInt(0, targets.Count)].SlotPosition };
    }
    private static Request GetRequest(ScriptHookContext context, string kind)
    {
        if (context?.Declaration == null || !Active(context.Status) || !Requests.TryGetValue(context.Declaration, out var list)) return null;
        return list.Find(request => request.Status == context.Status && request.Kind == kind);
    }
    private static List<int> RequestTarget(BattleManager battle, BattleEntity owner, string kind)
    {
        var request = GetRequest(ScriptHookContext.Current, kind);
        var targets = request?.Targets?.FindAll(target => Valid(battle, owner, target));
        if (targets == null || targets.Count == 0) return new List<int>();
        targets.Sort((a, b) => a.SlotPosition.CompareTo(b.SlotPosition));
        request.Chosen = targets[targets.Count == 1 ? 0 : BattleRandom.NextInt(0, targets.Count)];
        return new List<int> { request.Chosen.SlotPosition };
    }
    private static void StartOz(StatusInstance status, object host)
    {
        var owner = status.Caster;
        if (owner?.EntityID != 1024 || !(host is FieldPosition field) || field.Side == owner.Side)
            throw new InvalidOperationException("奥兹必须由菲谢尔施加在敌方场地。");
        var old = FindOz(owner);
        if (old != null) StatusHostAccess.Remove(old.Host, old, "Replaced");
        if (owner.ConstellationLevel >= 6 && !status.IsPermanent) status.RemainingPhaseCount++;
        var memory = new Memory(); Memories.Add(status, memory);
        memory.Listeners.Add(BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (header, hit) =>
        {
            var cause = BattleEventHub.Current;
            if (cause != null && HeavyHits.TryGetValue(cause, out var root) && root == status
                && memory.HeavyHitIDs.Add(header.HitId != 0 ? header.HitId : header.EventId)) Queue(status, "A1", cause, null);
        }, status.ApplyOrder, () => Active(status)));
        memory.Listeners.Add(BattleEventHub.Subscribe<ActionEventData>(BattleEventKind.ActionCompleted, (header, action) =>
        {
            var cause = BattleEventHub.Current;
            if (owner.ConstellationLevel < 6 || action.Subject == owner || action.Subject?.Side != owner.Side
                || (action.ActionType != "Normal" && action.ActionType != "Heavy")
                || cause == null || !Actions.TryGetValue(cause, out var cast) || cast.Hits.Count == 0) return;
            Queue(status, "C6", cause, cast.Hits);
        }, status.ApplyOrder, () => Active(status)));
        memory.Listeners.Add(BattleEventHub.Subscribe<ReactionEventData>(BattleEventKind.ReactionOccurred, (_, reaction) =>
        {
            if (ReactionFacts.TryGetValue(BattleEventHub.Current, out var groups))
                foreach (var group in groups) if (group.Root == status && !group.Managed) group.Submit();
        }, status.ApplyOrder, () => Active(status)));
    }
    private static void StartC1(StatusInstance status, object host)
    {
        if (!(host is BattleEntity owner) || owner.EntityID != 1024 || status.Caster != owner)
            throw new InvalidOperationException("幽邃鸦眼必须初始化在菲谢尔本人。");
        var memory = new Memory(); Memories.Add(status, memory);
        memory.Listeners.Add(BattleEventHub.Subscribe<ActionEventData>(BattleEventKind.ActionCompleted, (_, action) =>
        {
            var cause = BattleEventHub.Current;
            if (action.Subject == owner && action.ActionType == "Normal" && cause != null
                && Actions.TryGetValue(cause, out var cast) && cast.Primary != null)
                Queue(status, "C1", cause, new[] { cast.Primary });
        }, status.ApplyOrder, () => Active(status)));
    }
    private static void Stop(StatusInstance status, object host)
    {
        if (Memories.TryGetValue(status, out var memory)) foreach (var listener in memory.Listeners) listener.Dispose();
        Memories.Remove(status);
    }
    private static void Queue(StatusInstance status, string kind, BattleDeclaration cause, IEnumerable<BattleEntity> targets)
    {
        if (!Active(status) || cause == null) return;
        var request = new Request { Status = status, Kind = kind, Targets = targets == null ? null : new List<BattleEntity>(targets) };
        var requests = Requests.GetValue(cause, _ => new List<Request>());
        if (requests.Exists(r => r.Status == status && r.Kind == kind)) return;
        requests.Add(request);
        if (!DataManager.Instance.StatusActionDict.TryGetValue(status.StatusID2, out var actions)) return;
        string hook = "Fischl_" + kind + "()";
        foreach (var action in actions) if (action.ActionType == "OnTrigger" && action.ScriptHook == hook)
            status.Caster.CharacterCtrl.TryExecuteEventStatusAction(status, action, status.Host,
                new ScriptHookContext { Caster = status.Caster, Status = status, StatusHost = status.Host, Declaration = cause },
                () => Active(status));
    }
    private static void Index(BattleDeclaration declaration)
    {
        var hit = declaration.Get<DamageEventData>(); var source = hit?.Source;
        if (hit != null && source?.CountsAsHit == true && hit.Subject?.Side != source.SourceSide)
        {
            var cast = Casting.Value;
            if (cast?.Owner == source.SourceEntity && source.SourceKind == ReactionSourceKind.CharacterSkill && source.SourceSkillID == cast.Skill)
            {
                if (cast.Primary == null && declaration.Header.TargetIndex == 1) cast.Primary = hit.Subject;
                if (hit.HitLanded) cast.Hits.Add(hit.Subject);
            }
            var action = Responding.Value;
            if (hit.HitLanded && action?.Status == source.SourceStatus && source.SourceEffectID == "STE_Fischl_OzDamage") action.ShotHit = true;
            var root = FindOz(source.SourceEntity);
            if (hit.HitLanded && hit.DamageType == "Heavy" && Talent(source.SourceEntity, 1)
                && root?.Host is FieldPosition field && hit.Subject?.SlotPosition == field.SlotIndex)
                HeavyHits.Add(declaration, root);
        }
        var completed = declaration.Get<ActionEventData>(); var current = Casting.Value;
        if (declaration.Kind == BattleEventKind.ActionCompleted && current?.Owner == completed?.Subject
            && completed.SkillId == current.Skill) Actions.Add(declaration, current);
        var reaction = declaration.Get<ReactionEventData>();
        if (reaction == null || reaction.Subject == null) return;
        var battle = BattleManager.Instance;
        if (battle == null) return;
        foreach (var ally in battle.Allies)
        {
            var owner = ally?.Entity; var root = FindOz(owner);
            if (root == null || reaction.Subject.Side == owner.Side || !Memories.TryGetValue(root, out var memory)) continue;
            bool electro = reaction.Reaction != ReactionType.Spread && ContainsElectro(reaction.Elements);
            bool magic = battle.TeamBonuses?.HasHexerei == true && reaction.SourceWasPartyMember && reaction.SourceSide == owner.Side;
            bool overload = magic && reaction.Reaction == ReactionType.Overloaded;
            bool conduct = magic && (reaction.Reaction == ReactionType.ElectroCharged || reaction.Reaction == ReactionType.LunarElectroCharged);
            // 天赋2的追加不递归触发自己；仍可正常触发魔导和其他人的合法响应。
            electro &= Invoking.Value?.Effect != "STE_Fischl_A4Damage";
            if (!electro && !overload && !conduct) continue;
            var invocation = Invoking.Value; Group group;
            if (invocation != null && invocation.ID == declaration.Header.EffectExecutionId)
            {
                if (!invocation.Groups.TryGetValue(root, out group)) invocation.Groups.Add(root, group = new Group { Root = root, Managed = true });
            }
            else
            {
                long key = declaration.Header.EffectExecutionId != 0 ? declaration.Header.EffectExecutionId : -declaration.Header.EventId;
                if (!memory.Fallback.TryGetValue(key, out group)) memory.Fallback.Add(key, group = new Group { Root = root });
            }
            if (electro) { group.Cause = group.Cause ?? declaration; group.Targets.Add(reaction.Subject); }
            if (overload) group.Overload = group.Overload ?? declaration;
            if (conduct) group.Conduct = group.Conduct ?? declaration;
            ReactionFacts.GetValue(declaration, _ => new List<Group>()).Add(group);
        }
    }
    private static bool ContainsElectro(IReadOnlyList<string> elements)
    { if (elements != null) foreach (string element in elements) if (element == "Electro") return true; return false; }
}
