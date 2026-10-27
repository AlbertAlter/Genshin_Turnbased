using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

/// <summary>510固定生命、534/547百分比生命的开战常驻属性；不替代临时生命Buff机制。</summary>
public static class ArtifactMaxHPBonuses
{
    private static readonly string[] IDs =
    {
        AdventurerArtifactHooks.PersonalID, AdventurerArtifactHooks.SharedID,
        "ST_Artifact_453401", "ST_Artifact_454701"
    };
    private sealed class AppliedBonus { internal float Value, Percent; }
    private static readonly ConditionalWeakTable<BattleEntity, AppliedBonus> Applied
        = new ConditionalWeakTable<BattleEntity, AppliedBonus>();

    internal static void Register()
    {
        // 注册在冒险家绑定初始化之后，全部生命加成只应用一次净差。
        BattleSpecialRules.RegisterBattleReady(Initialize);
        BattleSpecialRules.RegisterEntityReset(Reset);
        foreach (string id in IDs) BattleSpecialRules.RegisterStatusLifecycle(id, null, OnRemoved);
    }

    private static void Initialize(BattleManager battle)
    {
        if (battle?.TeamBonuses == null || battle.TeamBonuses.IsDisposed) return;
        foreach (var ally in battle.Allies)
            if (ally?.Entity != null) Recalculate(ally.Entity, true);
    }

    private static void OnRemoved(StatusInstance status, object host)
    {
        if (host is BattleEntity owner && Applied.TryGetValue(owner, out _)) Recalculate(owner, false);
    }

    private static void Reset(BattleEntity owner)
    {
        if (!Applied.TryGetValue(owner, out var applied)) return;
        owner.TotalHP = Mathf.Max(0f, owner.TotalHP - applied.Value);
        owner.BakedHPBonus -= applied.Percent;
        owner.CurrentHP = Mathf.Min(owner.CurrentHP, owner.GetFinalHP());
        Applied.Remove(owner);
    }

    private static void Recalculate(BattleEntity owner, bool initializing)
    {
        var statuses = new List<StatusInstance>();
        foreach (var status in owner.GetEffectiveStatusList())
            foreach (string id in IDs)
                if (status.StatusID2 == id) { statuses.Add(status); break; }
        float flat = owner.GetInitialArtifactHPBonusFlat(statuses);
        float percent = owner.GetInitialArtifactHPBonus(statuses);
        float basis = percent == 0f ? 0f : owner.CharacterCtrl != null
            ? owner.CharacterCtrl.GetHPBeforePercentBonuses()
            : throw new InvalidOperationException("圣遗物百分比生命缺少角色基础生命数据。");
        // 与已有武器生命百分比使用同一基础，不放大突破/武器百分比或510固定生命。
        float value = flat + basis * percent;
        if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
            throw new InvalidOperationException("圣遗物初始生命加成必须为有限非负数。");
        var applied = Applied.GetValue(owner, _ => new AppliedBonus());
        owner.BakedHPBonus += percent - applied.Percent;
        applied.Percent = percent;
        foreach (var status in statuses) status.HPBonusIncludedInPanel = true;
        float delta = value - applied.Value;
        if (delta == 0f) return;
        owner.TotalHP = Mathf.Max(0f, owner.TotalHP + delta);
        applied.Value = value;
        // 初始化只提交属性，当前HP交给完整开战收尾；战斗内来源失效仍沿真实上限裁切入口。
        if (!initializing) owner.ClampCurrentHPToMaximum();
    }
}
