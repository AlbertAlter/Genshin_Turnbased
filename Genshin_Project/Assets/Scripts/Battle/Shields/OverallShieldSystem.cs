using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>某次实际承伤时存在的场地盾。破盾后仍可读，不通过当前状态倒推此前保护事实。</summary>
public sealed class OverallShieldProtection
{
    public BattleEntity Owner { get; }
    public StatusInstance SourceStatus { get; }
    public float DamageBeforeShield { get; }
    public float RecipientMaxHP { get; }
    internal OverallShieldProtection(Shield shield, float damage, float maxHP)
    { Owner = shield.SourceStatus.Caster; SourceStatus = shield.SourceStatus;
        DamageBeforeShield = damage; RecipientMaxHP = maxHP; }
}

/// <summary>场地盾仍由施加者身上的一份状态拥有；不把盾复制到队友，也不挂队伍宿主。</summary>
public static class OverallShieldSystem
{
    public static bool IsOverall(StatusMainData main)
        => string.Equals(main?.StatusType, "OverallShield", StringComparison.OrdinalIgnoreCase);

    public static void ValidateHost(StatusMainData main, object host, BattleEntity caster)
    {
        if (IsOverall(main) && (!(host is BattleEntity entity) || caster == null || entity != caster))
            throw new InvalidOperationException("OverallShield 必须施加在施加者本人；ApplyStatus/Shield 使用 Self。");
    }

    public static List<BattleEntity> Members(BattleManager battle, BattleSide side, bool includeOffField = false)
    {
        var result = new List<BattleEntity>();
        if (battle == null) return result;
        if (side == BattleSide.Ally)
        {
            foreach (var controller in battle.Allies)
                if (controller?.Entity?.IsAlive == true && (includeOffField || controller.Entity.IsOnField)) result.Add(controller.Entity);
        }
        else
        {
            foreach (var controller in battle.Enemies)
                if (controller?.Entity?.IsAlive == true && (includeOffField || controller.Entity.IsOnField)) result.Add(controller.Entity);
        }
        return result;
    }

    public static bool IsActive(BattleEntity owner, Shield shield)
        => owner?.IsAlive == true && shield?.Kind == ShieldKind.Overall && shield.Value > 0f
            && shield.SourceStatus?.IsActive == true && shield.SourceStatus.Caster == owner
            && ReferenceEquals(shield.SourceStatus.Host, owner) && owner.HasStatusInstance(shield.SourceStatus)
            && owner.Shields.Contains(shield);

    public static List<Shield> GetProtectingShields(BattleEntity target)
    {
        var result = new List<Shield>();
        if (target?.IsAlive != true) return result;
        var members = Members(BattleManager.Instance, target.Side, includeOffField: true);
        // 未登记实体只能查询自己的盾，不冒用另一场编队的场地盾。
        if (!members.Contains(target)) members = new List<BattleEntity> { target };
        foreach (var owner in members)
            foreach (var shield in owner.Shields)
                if (IsActive(owner, shield)) result.Add(shield);
        result.Sort((a, b) => a.SourceStatusApplyOrder.CompareTo(b.SourceStatusApplyOrder));
        return result;
    }

    public static bool HasAny(BattleManager battle)
    {
        foreach (BattleSide side in new[] { BattleSide.Ally, BattleSide.Enemy })
            foreach (var owner in Members(battle, side, includeOffField: true))
                foreach (var shield in owner.Shields)
                    if (IsActive(owner, shield)) return true;
        return false;
    }

    /// <summary>送入的伤害已经结清防抗、暴击、减伤及免疫。True不进入此层，None按1:1扣量。</summary>
    internal sealed class Hit
    {
        internal BattleEntity Target;
        internal float Damage;
        internal string Element;
        internal bool Bypass;
        internal float TargetMaxHP;
        internal readonly List<OverallShieldProtection> Protections = new List<OverallShieldProtection>();
    }

    internal sealed class Payment
    {
        internal BattleEntity Owner;
        internal Shield Shield;
        internal float Loss;
    }

    /// <summary>
    /// 每份盾读取同一批原伤害，按自己的来源强效折算消耗。
    /// 每份盾吸收该批伤害的相同比例，多个盾取最大保护比例，不累加、不串联。
    /// 此方法提交盾量；破碎来源状态由调用者在全部目标扣血后统一移除。
    /// </summary>
    internal static float[] Absorb(IReadOnlyList<Hit> hits, out List<Payment> payments)
    {
        payments = new List<Payment>();
        var fractions = new float[hits.Count];
        var shields = new HashSet<Shield>();
        for (int i = 0; i < hits.Count; i++)
            if (!hits[i].Bypass)
                foreach (var shield in GetProtectingShields(hits[i].Target)) shields.Add(shield);
        var ordered = new List<Shield>(shields);
        ordered.Sort((a, b) => a.SourceStatusApplyOrder.CompareTo(b.SourceStatusApplyOrder));
        foreach (var shield in ordered)
        {
            var owner = shield.SourceStatus.Caster;
            double demand = 0;
            var covered = new bool[hits.Count];
            for (int i = 0; i < hits.Count; i++)
            {
                var hit = hits[i];
                if (hit.Bypass || hit.Damage <= 0 || !GetProtectingShields(hit.Target).Contains(shield)) continue;
                float factor = DamageProperties.IsDirect(hit.Element) ? 1f
                    : BattleEntity.GetShieldAbsorptionMultiplier(shield.Element, hit.Element)
                        * owner.GetShieldStrengthMultiplier(hit.Element) * shield.Strength;
                if (factor <= 0 || float.IsNaN(factor) || float.IsInfinity(factor)) continue;
                demand += (double)hit.Damage / factor;
                covered[i] = true;
            }
            if (demand <= 0) continue;
            float fraction = (float)Math.Min(1d, shield.Value / demand);
            for (int i = 0; i < hits.Count; i++)
                if (covered[i])
                {
                    fractions[i] = Mathf.Max(fractions[i], fraction);
                    hits[i].Protections.Add(new OverallShieldProtection(shield, hits[i].Damage, hits[i].TargetMaxHP));
                }
            payments.Add(new Payment { Owner = owner, Shield = shield, Loss = (float)Math.Min(shield.Value, demand) });
        }
        // 先算完所有份再提交，避免先破盾的来源改变后面盾的护盾强效。
        foreach (var payment in payments)
        {
            using var changes = ShieldEventScope.Begin(payment.Owner, "OverallAbsorption");
            payment.Shield.Value = Mathf.Max(0f, payment.Shield.Value - payment.Loss);
        }
        var remaining = new float[hits.Count];
        for (int i = 0; i < hits.Count; i++) remaining[i] = hits[i].Damage * (1f - fractions[i]);
        return remaining;
    }

    internal static void RemoveBroken(IReadOnlyList<Payment> payments)
    {
        foreach (var payment in payments)
            if (payment.Shield.Value <= 0f)
                payment.Owner.RemoveStatusInstance(payment.Shield.SourceStatus, "ShieldBroken");
    }
}
