using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>一次治疗的事件快照。数值在本段预计算时固定，提交后即为实际结果。</summary>
public sealed class HealResolvedEvent
{
    public BattleEntity Source { get; }
    public BattleEntity Target { get; }
    public string SourceEffectID { get; }
    public int SegmentIndex { get; }
    public int TargetIndex { get; }
    public float HPBefore { get; }
    public float HPAfter { get; }
    public float MaxHP { get; }
    public float RequestedAmount { get; }
    public float ActualAmount => Mathf.Max(0f, HPAfter - HPBefore);
    public float OverhealAmount => Mathf.Max(0f, RequestedAmount - ActualAmount);
    public bool IsCommitted { get; private set; }

    internal HealResolvedEvent(BattleEntity source, BattleEntity target, float amount,
        float hpBefore, float hpAfter, float maxHP, string effectID, int segmentIndex, int targetIndex)
    {
        Source = source;
        Target = target;
        RequestedAmount = amount;
        HPBefore = hpBefore;
        HPAfter = hpAfter;
        MaxHP = maxHP;
        SourceEffectID = effectID ?? string.Empty;
        SegmentIndex = segmentIndex;
        TargetIndex = targetIndex;
    }

    internal void MarkCommitted() => IsCommitted = true;
}

/// <summary>
/// 一段治疗的全部目标：预计算并记录候选 → 提交全部 HP → 确认条件并预占次数 → 执行响应。
/// 提交过程不派发回调，保证群体治疗的条件判断看到本段全部治疗后的真实状态。
/// </summary>
public sealed class HealBatch : IDisposable
{
    private readonly List<HealResolvedEvent> _events = new List<HealResolvedEvent>();
    private readonly Dictionary<BattleEntity, float> _projectedHP = new Dictionary<BattleEntity, float>();
    private readonly IDisposable _effect = BattleResolutionContext.BeginEffect();
    private bool _flushed;
    public IReadOnlyList<HealResolvedEvent> Results => _events.AsReadOnly();

    public void Add(BattleEntity target, float amount, BattleEntity source = null,
        string effectID = null, int segmentIndex = 1)
    {
        if (_flushed) throw new InvalidOperationException("HealBatch already flushed");
        if (target == null || target.IsDead || amount <= 0f
            || float.IsNaN(amount) || float.IsInfinity(amount)) return;
        float before = _projectedHP.TryGetValue(target, out float projected) ? projected : target.CurrentHP;
        float after = Mathf.Max(before, Mathf.Min(target.TotalHP, before + amount));
        _projectedHP[target] = after;
        _events.Add(new HealResolvedEvent(source, target, amount, before, after, target.TotalHP,
            effectID, segmentIndex, _events.Count + 1));
    }

    public void Flush()
    {
        if (_flushed) return;
        _flushed = true;
        using (BattleResolutionContext.BeginCollection())
        {
            // 这里只记录已有状态的响应，ScriptHook/概率/次数均在提交后的确认阶段处理。
            foreach (HealResolvedEvent heal in _events)
                StatusOnHitHookSystem.CollectHeal(heal);
            foreach (HealResolvedEvent heal in _events)
            {
                heal.Target.CurrentHP = heal.HPAfter;
                heal.MarkCommitted();
                BattleExecution.Record(heal);
            }
            // 常态属性条件按读取时的真实状态求值（StatusAppliesTo），无需暂写预期 HP。
        }
    }

    public void Dispose()
    {
        try { Flush(); }
        finally { _effect.Dispose(); }
    }
}
