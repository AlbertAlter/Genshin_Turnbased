using System;
using System.Collections.Generic;
using UnityEngine;

public partial class BattleEntity
{
    // ========== 元素附着操作 ==========
    public void ApplyAura(string element, float amount, int sourceEntityID, string sourceEffectID = "", string sourceSkillID = "",
        BattleEntity sourceEntity = null)
    {
        if (ElementQuery.IntrinsicStatusID(element) == null || amount <= 0f)
            return;
        // 固有元素由真实状态承载；普通附着不能覆盖它，也不另存一份同元素有限池。
        if (ElementQuery.ReadIntrinsic(this, element) != null) return;
        using var declarations = ElementEventScope.Begin("Apply", this);
        declarations.Refresh(this, ReactionPoolKind.NormalAura, element);

        // 同元素再次附着时，无论新元素量高低，均由新附着完整覆盖。
        var existing = ElementalAuras.Find(a => a.Element == element);
        int originPhase = BattleManager.Instance != null ? (int)BattleManager.Instance.CurrentPhase : -1;
        if (existing != null)
        {
            existing.AuraAmount = amount;
            existing.SourceEntityID = sourceEntityID;
            existing.SourceEntity = sourceEntity;
            existing.SourceSkillID = sourceSkillID;
            existing.SourceEffectID = sourceEffectID;
            existing.OriginPhase = originPhase;
            existing.SkipNextOriginDecay = false;
        }
        else
        {
            ElementalAuras.Add(new ElementalAura
            {
                Element = element,
                AuraAmount = amount,
                SourceEntityID = sourceEntityID,
                SourceEntity = sourceEntity,
                SourceSkillID = sourceSkillID,
                SourceEffectID = sourceEffectID,
                OriginPhase = originPhase
            });
        }
    }

    /// <summary>底层存储兼容入口，返回可变实例；只读业务请使用ElementQuery，扣量仍调用ConsumeAura。</summary>
    public ElementalAura GetAura(string element)
    {
        return ElementalAuras.Find(a => a.Element == element);
    }

    public void RemoveAura(string element)
    {
        using var declarations = ElementEventScope.Begin("Remove", this);
        ElementalAuras.RemoveAll(a => a.Element == element);
    }

    public void ConsumeAura(string element, float amount)
    {
        using var declarations = ElementEventScope.Begin("ReactionConsumption", this);
        var aura = GetAura(element);
        if (aura != null)
        {
            aura.AuraAmount -= amount;
            if (aura.AuraAmount <= 0) RemoveAura(element);
        }
    }

    // ================================================================
    //  元素量回合递减
    //  设计文档：元素附着每回合递减（1元素量持续2回合，每回合-0.5，最低至0）
    //  调用时机：每个阶段结算的"元素量回合递减"步骤（时间线第4步）
    // ================================================================
    public void TickAuras()
    {
        using var declarations = ElementEventScope.Begin("Decay", this);
        // 元素量每回合-0.5（2026-08-14）：根据施加的阶段定原点——
        // 附着只在"自己的原点阶段"（施加/刷新时的阶段）衰减，一回合只减一次
        int curPhase = BattleManager.Instance != null ? (int)BattleManager.Instance.CurrentPhase : -1;
        for (int i = ElementalAuras.Count - 1; i >= 0; i--)
        {
            var aura = ElementalAuras[i];
            if (curPhase >= 0 && aura.OriginPhase >= 0 && aura.OriginPhase != curPhase)
                continue;
            if (aura.SkipNextOriginDecay)
            {
                aura.SkipNextOriginDecay = false;
                continue;
            }
            aura.AuraAmount -= 0.5f;
            if (aura.AuraAmount <= 0f)
                ElementalAuras.RemoveAt(i);
        }
    }
}
