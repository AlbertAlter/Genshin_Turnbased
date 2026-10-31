using System;
using System.Collections.Generic;
using UnityEngine;

public partial class BattleEntity
{
    /// <summary>清理战斗会话的待处理结束检查，供战斗重置和测试环境清理共用。</summary>
    public static void ResetDamageResolutionSession()
    {
        BattleDeathResolution.ResetSession();
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
    ///  1. 记录 HPBefore；2. 在护盾之后应用已登记的扣血保护，再扣HP并限制不低于0；3. 生成 DamageResolvedEvent；
    ///  4. HitLanded 时触发目标的统一 OnHit（StatusOnHitHookSystem）；
    ///  5. CausedDeath 时广播死亡声明（KillHookSystem，先于战斗结束通知，确保击杀最后敌人仍能收到）；
    ///  6. 在完整行为或独立状态效果链结清后处理复活及胜负；7. 返回事件对象。
    /// </summary>
    public DamageResolvedEvent TakeDamage(float damage, DamageSourceInfo source)
    {
        DamageSourceInfo resolvedSource = source ?? DamageSourceInfo.CreateUnknown();
        return TakeDamage(damage, resolvedSource, resolvedSource.CountsAsHit);
    }

    public DamageResolvedEvent TakeDamage(float damage, DamageSourceInfo source, bool hitLanded)
    {
        if (!EquipmentStatusLifetime.CanExecute(source)) return null;
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

        BattleEntity sourceEntity = source?.SourceEntity;
        bool sourceWasPartyMember = false;
        var battle = BattleManager.Instance;
        if (sourceEntity != null && battle != null)
            foreach (var ally in battle.Allies)
                if (ally?.Entity == sourceEntity) { sourceWasPartyMember = true; break; }
        bool sourceWasOnField = sourceWasPartyMember && sourceEntity.CharacterCtrl?.IsActive == true;
        float hpBefore = CurrentHP;
        float hpDamage = IsAlive ? BattleSpecialRules.GuardHPDamage(this, damage) : damage;
        CurrentHP -= hpDamage;
        if (CurrentHP < 0f) CurrentHP = 0f;

        var damageEvent = new DamageResolvedEvent
        {
            Target = this,
            Source = source?.Snapshot() ?? DamageSourceInfo.CreateUnknown(),
            SourceWasPartyMember = sourceWasPartyMember,
            SourceWasOnField = sourceWasOnField,
            HPBefore = hpBefore,
            HPAfter = CurrentHP,
            RequestedHPDamage = damage,
            DamageBeforeShield = damage,
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
        using var declarationResolution = BattleEventHub.BeginResolution();
        using var resolutionEffect = BattleResolutionContext.BeginEffect();
        if (damageEvents == null || damageEvents.Count == 0) return;

        bool hasDeaths = false;
        foreach (DamageResolvedEvent damage in damageEvents)
            if (damage?.CausedDeath == true) { hasDeaths = true; break; }
        using var deathWindow = BattleDeathResolution.EnterDamageBatch(hasDeaths);
        using var responses = BattleResolutionContext.BeginCollection();
        foreach (DamageResolvedEvent damage in damageEvents)
            if (damage?.CausedDeath == true) EquipmentStatusLifetime.OnDeath(damage.Target);
        foreach (DamageResolvedEvent damage in damageEvents)
            if (damage?.CausedDeath == true) CharacterStatusLifetime.OnDeath(damage.Target);
        foreach (DamageResolvedEvent damage in damageEvents)
            if (damage?.CausedDeath == true && damage.Target.Type == EntityType.Enemy)
                foreach (var status in damage.Target.GetStatusList())
                    BattleDeathResolution.RemoveAfterDeathActions(damage.Target, status, "EnemyDied");
        var deaths = new List<DamageResolvedEvent>();
        foreach (DamageResolvedEvent damageEvent in damageEvents)
            if (damageEvent != null && damageEvent.CausedDeath)
                deaths.Add(damageEvent.Snapshot());
        BattleDeathResolution.RecordDeaths(deaths);
        StatusOnHitHookSystem.NotifyDamageBatch(damageEvents);
        if (deaths.Count == 0) return;
        BattleResolutionContext.AfterResponses(() =>
        {
            using (BattleResolutionContext.BeginCollection())
                foreach (DamageResolvedEvent death in deaths)
                    KillHookSystem.NotifyDeath(death);
            BattleResolutionContext.AfterSettlement(BattleDeathResolution.AfterDamageSettlement);
        });
    }

}
