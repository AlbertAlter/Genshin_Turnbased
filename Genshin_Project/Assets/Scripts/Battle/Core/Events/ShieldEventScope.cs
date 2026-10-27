using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

public sealed class ShieldSnapshot
{
    public long ShieldId { get; }
    public ShieldKind Kind { get; }
    public string Element { get; }
    public float Value { get; }
    public float Duration { get; }
    public float Strength { get; }
    public string SourceStatusId { get; }
    public long SourceInstanceId { get; }
    internal ShieldSnapshot(Shield shield, long id)
    { ShieldId = id; Kind = shield.Kind; Element = shield.Element; Value = shield.Value;
        Duration = shield.Duration; Strength = shield.Strength; SourceStatusId = shield.SourceStatusID2;
        SourceInstanceId = shield.SourceStatusApplyOrder; }
}

public sealed class ShieldChangedEventData : BattleEventData
{
    public IReadOnlyList<ShieldSnapshot> Before { get; }
    public IReadOnlyList<ShieldSnapshot> After { get; }
    public string Reason { get; }
    internal ShieldChangedEventData(BattleEntity target, List<ShieldSnapshot> before,
        List<ShieldSnapshot> after, string reason) : base(BattleEventKind.ShieldChanged, target)
    { Before = before.AsReadOnly(); After = after.AsReadOnly(); Reason = reason; }
}

/// <summary>一次已成功完成的护盾获得/重获；即使池的净数值不变，也与ShieldChanged分别记录真实事实。</summary>
public sealed class ShieldGrantedEventData : BattleEventData
{
    /// <summary>实际创造者快照；未知就保持null，绝不把获得护盾的宿主当创造者。</summary>
    public BattleEntity Creator { get; }
    public int CreatorId { get; }
    public BattleSide CreatorSide { get; }
    /// <summary>一次实际授盾批次；结晶给多人授盾共用，不用hit/效果ID合并独立结晶。</summary>
    public long GrantBatchId { get; }
    public ShieldSnapshot Shield { get; }
    public string Reason { get; }
    internal ShieldGrantedEventData(BattleEntity target, Shield shield, string reason, long batch = 0, BattleEntity creator = null)
        : base(BattleEventKind.ShieldGranted, target)
    {
        Creator = creator ?? shield.SourceStatus?.Caster; CreatorId = Creator != null ? Creator.EntityID : 0;
        CreatorSide = Creator != null ? Creator.Side : default;
        Shield = ShieldEventScope.Snapshot(shield); Reason = reason; GrantBatchId = batch > 0 ? batch : BattleEventHub.NextIdentity();
    }
}

/// <summary>同一实体嵌套护盾操作只提交一次最终池变化；多盾仍按原规则同时承伤。</summary>
public sealed class ShieldEventScope : IDisposable
{
    private sealed class Identity { internal readonly long Id = BattleEventHub.NextIdentity(); }
    private static readonly ConditionalWeakTable<Shield, Identity> Ids = new ConditionalWeakTable<Shield, Identity>();
    private static readonly AsyncLocal<ShieldEventScope> Slot = new AsyncLocal<ShieldEventScope>();
    private readonly ShieldEventScope previous;
    private readonly BattleEntity target;
    private readonly List<ShieldSnapshot> before;
    private readonly string reason;
    private readonly IDisposable collection, resolution;
    private readonly bool nested;
    private bool disposed;
    private ShieldEventScope(BattleEntity target, string reason)
    {
        this.target = target; this.reason = reason; previous = Slot.Value;
        for (var ancestor = previous; ancestor != null; ancestor = ancestor.previous)
            if (ReferenceEquals(ancestor.target, target)) { nested = true; return; }
        resolution = BattleEventHub.BeginResolution();
        collection = BattleResolutionContext.BeginCollection();
        before = Capture(target); Slot.Value = this;
    }
    public static ShieldEventScope Begin(BattleEntity target, string reason) => new ShieldEventScope(target, reason);
    internal static ShieldSnapshot Snapshot(Shield shield)
        => new ShieldSnapshot(shield, Ids.GetValue(shield, _ => new Identity()).Id);
    private static List<ShieldSnapshot> Capture(BattleEntity target)
    {
        var result = new List<ShieldSnapshot>();
        foreach (Shield shield in target.Shields)
            if (shield != null) result.Add(Snapshot(shield));
        return result;
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (nested) return;
        Slot.Value = previous;
        try
        {
            var after = Capture(target);
            bool changed = before.Count != after.Count;
            for (int i = 0; !changed && i < before.Count; i++)
                changed = before[i].ShieldId != after[i].ShieldId || before[i].Value != after[i].Value
                    || before[i].Duration != after[i].Duration || before[i].Element != after[i].Element
                    || before[i].Strength != after[i].Strength || before[i].Kind != after[i].Kind;
            if (changed) BattleEventHub.Publish(new ShieldChangedEventData(target, before, after, reason));
        }
        finally { try { collection.Dispose(); } finally { resolution.Dispose(); } }
    }
}
