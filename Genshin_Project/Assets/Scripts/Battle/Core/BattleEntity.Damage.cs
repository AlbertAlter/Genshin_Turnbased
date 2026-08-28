using System;
using System.Collections.Generic;
using UnityEngine;

public partial class BattleEntity
{
    // ========== 伤害/治疗 ==========
    /// <summary>治疗：恢复HP（不超过上限，2026-08-14）。</summary>
    public void Heal(float amount, BattleEntity source = null)
    {
        if (amount <= 0 || IsDead || float.IsNaN(amount) || float.IsInfinity(amount)) return;
        float hpBefore = CurrentHP;
        CurrentHP = Mathf.Min(TotalHP, CurrentHP + amount);
        StatusOnHitHookSystem.NotifyHeal(source, this, amount, Mathf.Max(0f, CurrentHP - hpBefore));
    }

    /// <summary>
    /// 带来源的扣血接口（2026-08-19，状态钩子任务）。固定顺序：
    ///  1. 记录 HPBefore；2. 扣除并限制血量不低于0；3. 生成 DamageResolvedEvent；
    ///  4. HitLanded 时触发目标的统一 OnHit（StatusOnHitHookSystem）；
    ///  5. CausedDeath 时广播死亡声明（KillHookSystem，先于战斗结束通知，确保击杀最后敌人仍能收到）；
    ///  6. 最后调用 BattleManager.CheckBattleEnd()；7. 返回事件对象。
    /// </summary>
    public DamageResolvedEvent TakeDamage(float damage, DamageSourceInfo source, bool hitLanded = true)
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
            Source = source ?? DamageSourceInfo.CreateUnknown(),
            HPBefore = hpBefore,
            HPAfter = CurrentHP,
            RequestedHPDamage = damage,
            ActualHPDamage = Mathf.Max(0f, hpBefore - CurrentHP),
            HitLanded = hitLanded,
            CausedDeath = hpBefore > 0f && CurrentHP <= 0f
        };

        // 统一 OnHit：所有伤害路径（角色/敌人/状态/反应/燃烧）都走这里，防止同一 hit 重复触发
        if (hitLanded)
            StatusOnHitHookSystem.NotifyHit(damageEvent);
        // 死亡声明：目标从存活变死亡只广播一次（已死亡目标再次受击 CausedDeath=false）
        if (damageEvent.CausedDeath)
            KillHookSystem.NotifyDeath(damageEvent);

        // 战斗结束判定：敌方全灭=胜利，我方全灭=失败（2026-08-13）
        if (IsDead)
            BattleManager.Instance?.CheckBattleEnd();
        return damageEvent;
    }

}
