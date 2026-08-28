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
        int originPhase = -1)
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
            OriginPhase = resolvedOriginPhase
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

    public void RemoveShields(ShieldKind kind)
    {
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
            if (shield == null || shield.Value <= 0f) continue;

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
