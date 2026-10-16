using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 已完成倍率、反应与暴击计算后的单个目标 Hit 请求。
/// 所有角色技能、角色状态、敌人技能、关卡效果和反应派生伤害最终都应从这里进入护盾与扣血。
/// </summary>
[Serializable]
public sealed class HitRequest
{
    public BattleEntity Target;
    public float DamageBeforeShield;
    public string Element;
    public float PoiseDamage;
    public DamageSourceInfo Source;
    public int HitIndex;
    public int TargetIndex;
    /// <summary>本次目标请求预备的 Count 贡献；伤害已包含在 DamageBeforeShield 内。</summary>
    public CountDamagePlan CountPlan;
}

[Serializable]
public sealed class HitResolution
{
    public HitRequest Request;
    public float DamageAfterShield;
    public DamageResolvedEvent DamageEvent;
}

/// <summary>
/// 一个逻辑 hit 对全部目标的有序结算层。
/// Resolve 只提交本层结果；Flush 时先统一按状态 ApplyOrder 派发 OnHit/OnDamage，
/// 再处理死亡声明和战斗结束。这样同一 hit 的目标不会因遍历顺序提前触发状态树。
/// </summary>
public sealed class HitBatch : IDisposable
{
    private static long _nextBatchID;
    private readonly List<HitResolution> _results = new List<HitResolution>();
    private bool _flushed;
    private readonly IDisposable _resolutionEffect;

    public long BatchID { get; }
    public IReadOnlyList<HitResolution> Results => _results;

    public HitBatch()
    {
        _resolutionEffect = BattleResolutionContext.BeginEffect();
        BatchID = ++_nextBatchID;
    }

    public HitResolution Resolve(HitRequest request)
    {
        if (_flushed) throw new InvalidOperationException("HitBatch already flushed");
        if (request == null || request.Target == null) return null;

        float incoming = request.DamageBeforeShield;
        if (float.IsNaN(incoming) || float.IsInfinity(incoming) || incoming < 0f)
            incoming = 0f;

        float afterShield = request.Target.AbsorbDamageWithShield(
            incoming,
            request.Element ?? string.Empty);
        DamageSourceInfo source = request.Source ?? DamageSourceInfo.CreateUnknown();
        DamageResolvedEvent damageEvent = request.Target.ApplyDamageCore(
            afterShield,
            source,
            source.CountsAsHit);
        damageEvent.HitBatchID = BatchID;
        damageEvent.HitIndex = request.HitIndex + 1;
        damageEvent.TargetIndex = request.TargetIndex + 1;
        request.CountPlan?.Resolve(damageEvent);

        var resolution = new HitResolution
        {
            Request = request,
            DamageAfterShield = afterShield,
            DamageEvent = damageEvent
        };
        _results.Add(resolution);
        return resolution;
    }

    public void Flush()
    {
        if (_flushed) return;
        _flushed = true;

        var events = new List<DamageResolvedEvent>(_results.Count);
        foreach (HitResolution result in _results)
            if (result?.DamageEvent != null)
                events.Add(result.DamageEvent);

        foreach (DamageResolvedEvent result in events) BattleExecution.Record(result);
        BattleEntity.FinalizeDamageEvents(events);

        foreach (HitResolution result in _results)
        {
            if (result?.DamageEvent == null || result.Request == null) continue;
            DamageResolvedEvent damageEvent = result.DamageEvent;
            DamageSourceInfo source = damageEvent.Source;
            LogManager.Log(LogCategory.HitBatch,
                $"batch#{BatchID} hit#{damageEvent.HitIndex} target#{damageEvent.TargetIndex} " +
                $"entity={damageEvent.Target?.EntityID ?? 0} " +
                $"source={source?.SourceKind.ToString() ?? "Unknown"}/" +
                $"{source?.SourceStatusID ?? string.Empty}/{source?.SourceEffectID ?? string.Empty} " +
                $"hit={damageEvent.HitLanded} shielded={result.DamageAfterShield:F1} " +
                $"hp={damageEvent.ActualHPDamage:F1}");
        }

#if GTB_POISE_SYSTEM
        // 是否触发 OnHit 与是否削韧是两条独立规则；削韧只看请求中显式填写的 PoiseDamage。
        foreach (HitResolution result in _results)
        {
            HitRequest request = result?.Request;
            if (request == null
                || request.Target == null
                || request.PoiseDamage <= 0f)
                continue;
            PoiseSystem.ApplyPoiseDamage(
                request.Target,
                request.PoiseDamage,
                result.DamageAfterShield,
                request.Source != null ? request.Source.SourceEntity : null);
        }
#endif
    }

    public void Dispose()
    {
        try { Flush(); }
        finally { _resolutionEffect.Dispose(); }
    }

    public static void ResetSession()
    {
        _nextBatchID = 0;
    }
}
