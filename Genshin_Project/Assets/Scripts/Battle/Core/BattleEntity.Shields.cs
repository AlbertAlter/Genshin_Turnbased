using System;
using System.Collections.Generic;
using UnityEngine;

public partial class BattleEntity
{
    // ========== 护盾操作 ==========
    public void AddShield(
        float value,
        string element,
        float duration,
        float strength = 1f,
        ShieldKind kind = ShieldKind.Skill,
        int originPhase = -1,
        StatusInstance sourceStatus = null)
    {
        int resolvedOriginPhase = originPhase > 0
            ? originPhase
            : (BattleManager.Instance != null ? (int)BattleManager.Instance.CurrentPhase : -1);
        Shields.Add(new Shield
        {
            Value = value,
            Element = element,
            Duration = duration,
            Strength = strength,
            Kind = kind,
            OriginPhase = resolvedOriginPhase,
            SourceStatusID2 = sourceStatus != null ? sourceStatus.StatusID2 : null,
            SourceStatusApplyOrder = sourceStatus != null ? sourceStatus.ApplyOrder : 0,
            SourceStatus = sourceStatus
        });
    }

    public Shield AddOrReplaceCrystallizeShield(
        float value,
        string element,
        int duration,
        int originPhase)
    {
        if (value <= 0f || duration <= 0) return null;

        int resolvedOriginPhase = originPhase > 0
            ? originPhase
            : (BattleManager.Instance != null ? (int)BattleManager.Instance.CurrentPhase : -1);
        Shield shield = null;
        for (int i = Shields.Count - 1; i >= 0; i--)
        {
            Shield candidate = Shields[i];
            if (candidate == null)
            {
                Shields.RemoveAt(i);
                continue;
            }
            if (candidate.Kind != ShieldKind.Crystallize) continue;
            if (shield == null) shield = candidate;
            else Shields.RemoveAt(i);
        }
        if (shield == null)
        {
            shield = new Shield();
            Shields.Add(shield);
        }

        shield.Value = value;
        shield.Element = element;
        shield.Duration = duration;
        shield.Strength = 1f;
        shield.Kind = ShieldKind.Crystallize;
        shield.OriginPhase = resolvedOriginPhase;
        return shield;
    }

    /// <summary>
    /// 创建敌方元素盾并与产生它的 Shield 状态实例绑定。
    /// 敌方同时只保留一个元素盾；普通技能盾与结晶盾不受影响。
    /// </summary>
    public Shield AddOrReplaceEnemyElementalShield(
        float value,
        string element,
        StatusInstance sourceStatus)
    {
        if (Type != EntityType.Enemy || value <= 0f || sourceStatus == null)
            return null;

        RemoveEnemyElementalShieldObjects(null);
        Shield shield = new Shield
        {
            Value = value,
            Element = element,
            Duration = sourceStatus.RemainingPhaseCount,
            Strength = 1f,
            Kind = ShieldKind.EnemyElemental,
            OriginPhase = -1,
            SourceStatusID2 = sourceStatus.StatusID2,
            SourceStatusApplyOrder = sourceStatus.ApplyOrder,
            SourceStatus = sourceStatus
        };
        Shields.Add(shield);
        return shield;
    }

    public Shield GetEnemyElementalShield()
    {
        for (int i = Shields.Count - 1; i >= 0; i--)
        {
            Shield shield = Shields[i];
            if (shield != null && shield.Kind == ShieldKind.EnemyElemental && shield.Value > 0f)
                return shield;
        }
        return null;
    }

    public float GetEnemyElementalShieldHP()
    {
        Shield shield = GetEnemyElementalShield();
        return shield != null ? shield.Value : 0f;
    }

    private void RemoveShieldOwnedByStatus(StatusInstance status)
    {
        if (status == null) return;
        for (int i = Shields.Count - 1; i >= 0; i--)
        {
            Shield shield = Shields[i];
            if (shield == null || IsShieldOwnedByStatus(shield, status))
                Shields.RemoveAt(i);
        }
    }

    private void RefreshShieldOwnedByStatus(StatusInstance status)
    {
        if (status == null) return;
        foreach (Shield shield in Shields)
        {
            if (shield != null && IsShieldOwnedByStatus(shield, status))
                shield.Duration = status.RemainingPhaseCount;
        }
    }

    private void RemoveEnemyElementalShieldObjects(StatusInstance owner)
    {
        for (int i = Shields.Count - 1; i >= 0; i--)
        {
            Shield shield = Shields[i];
            if (shield == null)
            {
                Shields.RemoveAt(i);
                continue;
            }
            if (shield.Kind != ShieldKind.EnemyElemental) continue;
            if (owner != null && !IsShieldOwnedByStatus(shield, owner)) continue;
            Shields.RemoveAt(i);
        }
    }

    private static bool IsShieldOwnedByStatus(Shield shield, StatusInstance status)
    {
        if (ReferenceEquals(shield.SourceStatus, status)) return true;
        return shield.SourceStatusApplyOrder == status.ApplyOrder
            && string.Equals(shield.SourceStatusID2, status.StatusID2, StringComparison.Ordinal);
    }

    /// <summary>
    /// 提交单次 Hit 的敌方元素盾损失。返回实际扣除量；破盾时统一移除来源状态。
    /// </summary>
    public float ApplyEnemyElementalShieldLoss(float requestedLoss, out bool broken)
    {
        broken = false;
        if (requestedLoss <= 0f || float.IsNaN(requestedLoss) || float.IsInfinity(requestedLoss))
            return 0f;

        Shield shield = GetEnemyElementalShield();
        if (shield == null) return 0f;

        float applied = Mathf.Min(requestedLoss, shield.Value);
        shield.Value = Mathf.Max(0f, shield.Value - applied);
        if (shield.Value > 0f) return applied;

        broken = true;
        if (StatusDict.TryGetValue(shield.SourceStatusID2 ?? string.Empty, out StatusInstance owner)
            && IsShieldOwnedByStatus(shield, owner))
        {
            RemoveStatus(owner.StatusID2, -1);
        }
        else
        {
            Shields.Remove(shield);
        }
        return applied;
    }

    public void RemoveShields(ShieldKind kind)
    {
        if (kind == ShieldKind.EnemyElemental)
        {
            var owners = new List<StatusInstance>();
            foreach (Shield shield in Shields)
            {
                StatusInstance owner = shield != null ? shield.SourceStatus : null;
                if (shield != null && shield.Kind == kind && owner != null && !owners.Contains(owner))
                    owners.Add(owner);
            }
            foreach (StatusInstance owner in owners)
                RemoveStatus(owner.StatusID2, -1);
        }
        Shields.RemoveAll(shield => shield == null || shield.Kind == kind);
    }

    public void TickShields(int currentPhase)
    {
        if (currentPhase <= 0) return;

        for (int i = Shields.Count - 1; i >= 0; i--)
        {
            Shield shield = Shields[i];
            if (shield == null)
            {
                Shields.RemoveAt(i);
                continue;
            }
            if (shield.Kind != ShieldKind.Crystallize || shield.OriginPhase != currentPhase)
                continue;

            shield.Duration -= 1f;
            if (shield.Duration <= 0f)
                Shields.RemoveAt(i);
        }
    }

    /// <summary>
    /// 多个护盾同时承伤而非串联叠加，因此返回当前最强单盾的有效基础盾值。
    /// 不含元素吸收倍率；需要查询某种伤害的实际最大吸收量时使用带元素参数的重载。
    /// </summary>
    public float GetTotalShieldHP()
    {
        return GetMaximumShieldCapacity(null, false);
    }

    /// <summary>返回面对指定伤害元素时，当前所有护盾中最大的实际吸收量。</summary>
    public float GetTotalShieldHP(string damageElement)
    {
        return GetMaximumShieldCapacity(damageElement, true);
    }

    private float GetMaximumShieldCapacity(string damageElement, bool includeAbsorptionMultiplier)
    {
        float maximum = 0f;
        foreach (Shield shield in Shields)
        {
            if (shield == null || shield.Value <= 0f || shield.Kind == ShieldKind.EnemyElemental) continue;

            float factor = (1f + ShieldStrength) * shield.Strength;
            if (includeAbsorptionMultiplier)
                factor *= GetShieldAbsorptionMultiplier(shield.Element, damageElement);
            if (factor <= 0f || float.IsNaN(factor) || float.IsInfinity(factor)) continue;

            float capacity = shield.Value * factor;
            if (!float.IsNaN(capacity) && capacity > maximum)
                maximum = capacity;
        }
        return maximum;
    }

    public float AbsorbDamageWithShield(float incomingDamage, string damageElement)
    {
        if (incomingDamage <= 0f || Shields.Count == 0)
            return Mathf.Max(0f, incomingDamage);

        float maxBlocked = 0f;
        for (int i = Shields.Count - 1; i >= 0; i--)
        {
            Shield shield = Shields[i];
            if (shield == null || shield.Value <= 0f)
            {
                Shields.RemoveAt(i);
                continue;
            }
            if (shield.Kind == ShieldKind.EnemyElemental)
                continue;

            float absorbMultiplier = GetShieldAbsorptionMultiplier(shield.Element, damageElement);
            float factor = absorbMultiplier * (1f + ShieldStrength) * shield.Strength;
            if (factor <= 0f || float.IsNaN(factor) || float.IsInfinity(factor))
                continue;

            float capacity = shield.Value * factor;
            if (capacity <= 0f || float.IsNaN(capacity))
                continue;

            float blocked = Mathf.Min(incomingDamage, capacity);
            if (blocked > 0f)
            {
                shield.Value = Mathf.Max(0f, shield.Value - blocked / factor);
                maxBlocked = Mathf.Max(maxBlocked, blocked);
            }
            if (shield.Value <= 0f)
                Shields.RemoveAt(i);
        }

        return Mathf.Max(0f, incomingDamage - maxBlocked);
    }

    private static float GetShieldAbsorptionMultiplier(string shieldElement, string damageElement)
    {
        if (string.IsNullOrEmpty(shieldElement) || shieldElement == "None") return 1f;
        if (shieldElement == "Geo") return 2.5f;
        return shieldElement == damageElement ? 2.5f : 1.5f;
    }
}
