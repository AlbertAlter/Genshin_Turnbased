using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 已完成倍率、反应与暴击计算，尚未乘受击方 DMGTaken 的单个目标 Hit 请求。
/// 所有角色技能、角色状态、敌人技能、关卡效果和反应派生伤害最终都应从这里进入Immune、护盾与扣血。
/// Element=True/None时传入基准×倍率，均跳过DMGTaken、暴击与附着；None受Immune和普通盾1:1抵扣，True直接扣HP。
/// </summary>
[Serializable]
public sealed class HitRequest
{
    public BattleEntity Target;
    public float DamageBeforeShield;
    /// <summary>已在最终入口完成的唯一暴击结果；派生不可暴击伤害为null，不另抽随机。</summary>
    public CriticalHitResult? Critical;
    public string Element = "Physical";
    /// <summary>原始Hit配表的附着量，不能用反应消耗后的剩余元素量代替。</summary>
    public float ElementAmount;
    public EnemyShieldHitResult EnemyShieldHit;
    public bool IsNightSoulDamage;
    /// <summary>本次直接伤害的性质；派生反应不要照抄触发技能的性质。</summary>
    public DamageProperties Properties;
    public IReadOnlyList<long> CauseEventIds;
    /// <summary>实际效果的伤害标签；派生反应填 Reaction，不继承触发技能标签。</summary>
    public string DamageType;
    /// <summary>主命中在反应改变状态前准备；未准备的独立派生伤害在结算时读取。</summary>
    public DMGTakenSnapshot DMGTaken;
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
    public float DMGTakenMultiplier;
    public float DamageBeforeShield;
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
    private readonly IDisposable _declarationResolution;
    private readonly IDisposable _declarationResponses;
    private readonly IDisposable _declarationHit;

    public long BatchID { get; }
    public long RuntimeHitID { get; }
    public IReadOnlyList<HitResolution> Results => _results;

    public HitBatch(long runtimeHitID = 0)
    {
        RuntimeHitID = runtimeHitID != 0 ? runtimeHitID : BattleEventHub.NextIdentity();
        _declarationResolution = BattleEventHub.BeginResolution();
        _declarationHit = BattleEventHub.BeginHit(RuntimeHitID);
        _resolutionEffect = BattleResolutionContext.BeginEffect();
        _declarationResponses = BattleResolutionContext.BeginCollection();
        BatchID = ++_nextBatchID;
    }

    public HitResolution Resolve(HitRequest request)
    {
        if (_flushed) throw new InvalidOperationException("HitBatch already flushed");
        if (request == null || request.Target == null) return null;
        if (!EquipmentStatusLifetime.CanExecute(request.Source))
        {
            request.CountPlan?.Resolve(null);
            return null; // 未命中、不扣盾/血、不附带命中/死亡声明；旧装备请求不可在复活后复用。
        }
        using var hit = BattleEventHub.BeginHit(RuntimeHitID, request.TargetIndex + 1);

        float incoming = request.DamageBeforeShield;
        if (float.IsNaN(incoming) || float.IsInfinity(incoming) || incoming < 0f)
            incoming = 0f;

        DamageSourceInfo source = request.Source ?? DamageSourceInfo.CreateUnknown();
        bool isTrue = DamageProperties.IsTrue(request.Element);
        bool isDirect = DamageProperties.IsDirect(request.Element);
        bool isFixed = isTrue || isDirect;
        if (isFixed || request.Properties.UsesSpecialFormula)
        {
            source = source.Snapshot();
            source.ReactionType = isFixed ? ReactionType.None : request.Properties.Reaction;
        }
        DMGTakenSnapshot taken = isFixed ? null : request.DMGTaken ?? request.Target.PrepareDMGTaken(
            source.SourceEffectID, source.SourceSkillID, request.DamageType, request.Element);
        float takenMultiplier = isFixed ? 1f : taken.GetMultiplier(source.ReactionType);
        incoming *= takenMultiplier;
        if (float.IsNaN(incoming) || float.IsInfinity(incoming)) incoming = 0f;

        EnemyShieldHitResult elementalShield = null;
        if (!isFixed)
        {
            elementalShield = request.EnemyShieldHit;
            if (elementalShield != null && elementalShield.Target != request.Target)
                throw new InvalidOperationException("Enemy shield hit belongs to another target.");
            if (elementalShield == null)
                elementalShield = EnemyElementShieldSystem.PrepareHit(request.Target, request.Element,
                    request.ElementAmount, source.ReactionType == ReactionType.Overloaded ? "Overloaded" : null);
            EnemyElementShieldSystem.CommitHit(elementalShield);
            if (request.EnemyShieldHit != null && source.ReactionType == ReactionType.Overloaded)
                EnemyElementShieldSystem.ResolveHit(request.Target, request.Element, request.ElementAmount, "Overloaded");
        }
        // 敌方GU盾只结算元素耐久；本体承伤由状态决定，不能因有盾自动免伤。
        bool isImmune = !isTrue
            && DamageImmunity.Resolve(request.Target, request.Element, source.SourceEffectID);
        float afterShield = isImmune ? 0f : isTrue ? incoming : isDirect
            ? request.Target.AbsorbDirectDamageWithShield(incoming) : request.Target.AbsorbDamageWithShield(
            incoming,
            request.Element ?? string.Empty);
        DamageResolvedEvent damageEvent = request.Target.ApplyDamageCore(
            afterShield,
            source,
            source.CountsAsHit);
        damageEvent.HitBatchID = BatchID;
        damageEvent.HitIndex = request.HitIndex + 1;
        damageEvent.TargetIndex = request.TargetIndex + 1;
        damageEvent.RuntimeHitID = RuntimeHitID;
        damageEvent.DamageBeforeShield = incoming;
        damageEvent.Critical = isFixed ? null : request.Critical;
        damageEvent.IsImmune = isImmune;
        damageEvent.ImmuneDamage = isImmune ? incoming : 0f;
        damageEvent.ShieldAbsorbed = isImmune ? 0f : Mathf.Max(0f, incoming - afterShield);
        damageEvent.Element = request.Element;
        damageEvent.DamageType = request.DamageType;
        damageEvent.ElementAmount = isFixed ? 0f : request.ElementAmount;
        damageEvent.Properties = isTrue ? default : request.Properties;
        damageEvent.IsNightSoulDamage = !isFixed && (request.IsNightSoulDamage || request.Properties.IsNightSoul);
        damageEvent.Declaration = BattleEventHub.Record(damageEvent, new DamageEventData(damageEvent), causes: request.CauseEventIds);
        request.CountPlan?.Resolve(damageEvent);

        var resolution = new HitResolution
        {
            Request = request,
            DMGTakenMultiplier = takenMultiplier,
            DamageBeforeShield = incoming,
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
        try { BattleEntity.FinalizeDamageEvents(events); }
        finally { _declarationResponses.Dispose(); }

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
                $"hit={damageEvent.HitLanded} DMGTaken=x{result.DMGTakenMultiplier:F3} " +
                $"incoming={result.DamageBeforeShield:F1} shielded={result.DamageAfterShield:F1} " +
                $"hp={damageEvent.ActualHPDamage:F1} Immune={damageEvent.IsImmune}");
        }

    }

    public void Dispose()
    {
        try { Flush(); }
        finally
        {
            _declarationResponses.Dispose();
            _resolutionEffect.Dispose();
            _declarationHit.Dispose();
            _declarationResolution.Dispose();
        }
    }

    public static void ResetSession()
    {
        _nextBatchID = 0;
    }
}
