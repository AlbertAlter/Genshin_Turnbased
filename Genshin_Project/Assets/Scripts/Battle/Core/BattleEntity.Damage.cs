using System;
using System.Collections.Generic;
using UnityEngine;

public partial class BattleEntity
{
    private static bool _battleEndCheckPending;

    /// <summary>清理战斗会话的待处理结束检查，供战斗重置和测试环境清理共用。</summary>
    public static void ResetDamageResolutionSession()
    {
        _battleEndCheckPending = false;
    }

    // ========== 伤害/治疗 ==========
    /// <summary>治疗：恢复HP（不超过上限，2026-08-14）。</summary>
    public void Heal(float amount, BattleEntity source = null)
    {
        using var batch = new HealBatch();
        batch.Add(this, amount, source);
    }

    /// <summary>
    /// 带来源的扣血接口（2026-08-19，状态钩子任务）。固定顺序：
    ///  1. 记录 HPBefore；2. 扣除并限制血量不低于0；3. 生成 DamageResolvedEvent；
    ///  4. HitLanded 时触发目标的统一 OnHit（StatusOnHitHookSystem）；
    ///  5. CausedDeath 时广播死亡声明（KillHookSystem，先于战斗结束通知，确保击杀最后敌人仍能收到）；
    ///  6. 最后调用 BattleManager.CheckBattleEnd()；7. 返回事件对象。
    /// </summary>
    public DamageResolvedEvent TakeDamage(float damage, DamageSourceInfo source)
    {
        DamageSourceInfo resolvedSource = source ?? DamageSourceInfo.CreateUnknown();
        return TakeDamage(damage, resolvedSource, resolvedSource.CountsAsHit);
    }

    public DamageResolvedEvent TakeDamage(float damage, DamageSourceInfo source, bool hitLanded)
    {
        DamageResolvedEvent damageEvent = ApplyDamageCore(damage, source, hitLanded);
        FinalizeDamageEvents(new[] { damageEvent });
        return damageEvent;
    }

    /// <summary>HitBatch 使用的纯扣血步骤；不派发状态、死亡或战斗结束事件。</summary>
    internal DamageResolvedEvent ApplyDamageCore(float damage, DamageSourceInfo source, bool hitLanded)
    {
        if (float.IsNaN(damage) || float.IsInfinity(damage) || damage < 0f)
            damage = 0f;
        if (IsDead)
            hitLanded = false;

        float hpBefore = CurrentHP;
        CurrentHP -= damage;
        if (CurrentHP < 0f) CurrentHP = 0f;

        var damageEvent = new DamageResolvedEvent
        {
            Target = this,
            Source = source?.Snapshot() ?? DamageSourceInfo.CreateUnknown(),
            HPBefore = hpBefore,
            HPAfter = CurrentHP,
            RequestedHPDamage = damage,
            ActualHPDamage = Mathf.Max(0f, hpBefore - CurrentHP),
            HitLanded = hitLanded,
            CausedDeath = hpBefore > 0f && CurrentHP <= 0f
        };

        return damageEvent;
    }

    /// <summary>
    /// 完成一层伤害事件：先收集并按全局状态顺序派发 OnHit/OnDamage，随后处理死亡与胜负。
    /// 反应伤害 HitLanded=false，但只要实际扣血仍会进入 OnDamage。
    /// </summary>
    internal static void FinalizeDamageEvents(IReadOnlyList<DamageResolvedEvent> damageEvents)
    {
        using var resolutionEffect = BattleResolutionContext.BeginEffect();
        if (damageEvents == null || damageEvents.Count == 0) return;

        using var responses = BattleResolutionContext.BeginCollection();
        StatusOnHitHookSystem.NotifyDamageBatch(damageEvents);
        var deaths = new List<DamageResolvedEvent>();
        foreach (DamageResolvedEvent damageEvent in damageEvents)
            if (damageEvent != null && damageEvent.CausedDeath)
                deaths.Add(damageEvent.Snapshot());
        if (deaths.Count == 0) return;
        BattleResolutionContext.AfterResponses(() =>
        {
            using (BattleResolutionContext.BeginCollection())
                foreach (DamageResolvedEvent death in deaths)
                    KillHookSystem.NotifyDeath(death);
            _battleEndCheckPending = true;
            BattleResolutionContext.AfterSettlement(() =>
            {
                if (!_battleEndCheckPending) return;
                _battleEndCheckPending = false;
                BattleManager.Instance?.CheckBattleEnd();
            });
        });
    }

}
