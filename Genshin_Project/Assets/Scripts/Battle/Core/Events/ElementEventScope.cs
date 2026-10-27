using System;
using System.Collections.Generic;
using System.Threading;

/// <summary>普通附着和冻结/燃烧虚拟池的不可变快照；不把反应显示状态当普通附着。</summary>
public sealed class ElementPoolSnapshot
{
    public string Element { get; }
    public ReactionPoolKind Kind { get; }
    private readonly float finiteAmount;
    public bool IsIntrinsic { get; }
    public float Amount => !IsIntrinsic ? finiteAmount
        : throw new InvalidOperationException("固有元素快照没有有限元素量。");
    public string SourceStatusId { get; }
    public long PoolApplyOrder { get; }
    public int SourceEntityId { get; }
    public string SourceEffectId { get; }
    public string SourceSkillId { get; }
    public int OriginPhase { get; }
    internal ElementPoolSnapshot(string element, ReactionPoolKind kind, float amount, int source,
        string effect, string skill, int phase)
    { Element = element; Kind = kind; finiteAmount = amount; SourceEntityId = source;
        SourceEffectId = effect; SourceSkillId = skill; OriginPhase = phase; }

    internal ElementPoolSnapshot(ElementPoolReading pool)
    {
        Element = pool.Element; Kind = pool.Kind; IsIntrinsic = pool.IsIntrinsic;
        if (!IsIntrinsic) finiteAmount = pool.Amount;
        SourceEntityId = pool.SourceEntityID; SourceEffectId = pool.SourceEffectID;
        SourceSkillId = pool.SourceSkillID; SourceStatusId = pool.SourceStatusID;
        OriginPhase = pool.OriginPhase; PoolApplyOrder = pool.PoolApplyOrder;
    }
}

public sealed class AuraChangedEventData : BattleEventData
{
    public IReadOnlyList<ElementPoolSnapshot> Before { get; }
    public IReadOnlyList<ElementPoolSnapshot> After { get; }
    public IReadOnlyList<string> RefreshedPools { get; }
    public string Reason { get; }
    internal AuraChangedEventData(BattleEntity target, List<ElementPoolSnapshot> before,
        List<ElementPoolSnapshot> after, List<string> refreshed, string reason)
        : base(BattleEventKind.AuraChanged, target)
    { Before = before.AsReadOnly(); After = after.AsReadOnly(); RefreshedPools = refreshed.AsReadOnly(); Reason = reason; }
}

public sealed class ElementContactEventData : BattleEventData
{
    public BattleEntity Source { get; }
    public int SourceId { get; }
    public string EffectId { get; }
    public string Element { get; }
    public float RequestedAmount { get; }
    public float RemainingAmount { get; }
    public bool CanApplyAura { get; }
    public bool CanTriggerReaction { get; }
    public IReadOnlyList<ReactionType> Reactions { get; }
    internal ElementContactEventData(ReactionContext context, ReactionResult result)
        : base(BattleEventKind.ElementContactResolved, context.Target)
    {
        Source = context.SourceEntity; SourceId = Source != null ? Source.EntityID : 0;
        EffectId = context.SourceEffectID; Element = context.AttackElement;
        RequestedAmount = context.AttackAmount; RemainingAmount = result.RemainingAttackAmount;
        CanApplyAura = context.CanApplyAura; CanTriggerReaction = context.CanTriggerReaction;
        var reactions = new List<ReactionType>();
        foreach (var occurrence in result.TriggeredReactions) reactions.Add(occurrence.Type);
        Reactions = reactions.AsReadOnly();
    }
}

/// <summary>
/// 元素池写回事务边界。嵌套低层增删只记账，根作用域结束才发布最终变化。
/// 扩散分别在各个提交阶段建立作用域，不能包住整个三阶段合成一次净变化。
/// </summary>
public sealed class ElementEventScope : IDisposable
{
    private sealed class Entry
    {
        internal BattleEntity Target;
        internal List<ElementPoolSnapshot> Before;
        internal readonly List<string> Refreshed = new List<string>();
        internal readonly List<long> Causes = new List<long>();
    }
    private static readonly AsyncLocal<ElementEventScope> Slot = new AsyncLocal<ElementEventScope>();
    private readonly ElementEventScope root;
    private readonly List<Entry> entries = new List<Entry>();
    private readonly string reason;
    private readonly IDisposable collection, resolution;
    private readonly List<long> causes = new List<long>();
    private bool disposed;

    public static ElementEventScope Begin(string reason, BattleEntity target = null)
        => new ElementEventScope(reason, target);
    private ElementEventScope(string reason, BattleEntity target)
    {
        this.reason = reason;
        root = Slot.Value ?? this;
        if (root == this)
        {
            resolution = BattleEventHub.BeginResolution();
            collection = BattleResolutionContext.BeginCollection();
            Slot.Value = this;
        }
        Touch(target);
    }
    public void AddCause(long eventId, BattleEntity target = null)
    {
        Touch(target);
        List<long> ids = target == null ? root.causes : root.entries.Find(e => ReferenceEquals(e.Target, target)).Causes;
        if (eventId > 0 && !ids.Contains(eventId)) ids.Add(eventId);
    }
    public void Touch(BattleEntity target)
    {
        if (target == null || root.entries.Exists(e => ReferenceEquals(e.Target, target))) return;
        root.entries.Add(new Entry { Target = target, Before = Capture(target) });
    }
    public void Refresh(BattleEntity target, ReactionPoolKind kind, string element)
    {
        Touch(target);
        Entry entry = root.entries.Find(e => ReferenceEquals(e.Target, target));
        string key = kind + ":" + element;
        if (entry != null && !entry.Refreshed.Contains(key)) entry.Refreshed.Add(key);
    }
    private static List<ElementPoolSnapshot> Capture(BattleEntity target)
    {
        var pools = new List<ElementPoolSnapshot>();
        foreach (var pool in ElementQuery.Capture(target))
            pools.Add(new ElementPoolSnapshot(pool));
        return pools;
    }
    private static bool Equal(List<ElementPoolSnapshot> a, List<ElementPoolSnapshot> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (a[i].Element != b[i].Element || a[i].Kind != b[i].Kind || a[i].IsIntrinsic != b[i].IsIntrinsic
                || !a[i].IsIntrinsic && a[i].Amount != b[i].Amount
                || a[i].SourceStatusId != b[i].SourceStatusId || a[i].PoolApplyOrder != b[i].PoolApplyOrder
                || a[i].SourceEntityId != b[i].SourceEntityId || a[i].SourceEffectId != b[i].SourceEffectId
                || a[i].SourceSkillId != b[i].SourceSkillId || a[i].OriginPhase != b[i].OriginPhase) return false;
        return true;
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (root != this) return;
        Slot.Value = null;
        try
        {
            // 先冻结全部目标，再提交候选；不能让一个目标的响应污染后一个目标的快照。
            var results = new List<BattleDeclaration>();
            foreach (Entry entry in entries)
            {
                List<ElementPoolSnapshot> after = Capture(entry.Target);
                // 只把确实留在场上的池记作刷新，不广播反应中间态。
                var refreshed = entry.Refreshed.FindAll(key => after.Exists(p => p.Kind + ":" + p.Element == key));
                if (!Equal(entry.Before, after) || refreshed.Count > 0)
                {
                    var ids = new List<long>(causes); ids.AddRange(entry.Causes);
                    results.Add(BattleEventHub.Record(null,
                        new AuraChangedEventData(entry.Target, entry.Before, after, refreshed, reason),
                        causes: ids.Count > 0 ? ids : null));
                }
            }
            foreach (var result in results) BattleEventHub.Dispatch(result);
        }
        finally { try { collection.Dispose(); } finally { resolution.Dispose(); } }
    }
}
