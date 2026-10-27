using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// 角色能量获取：统一处理技能效果与状态效果的 Based / Flat 充能，保留float小数。
/// </summary>
public partial class CharacterBattleController
{
    void ExecuteGainEnergy(SkillEffectData effect)
    {
        if (effect == null || !float.TryParse(effect.Param1, NumberStyles.Float,
            CultureInfo.InvariantCulture, out float gainedEnergy)) return;
        List<BattleEntity> targets = ResolveTargets(effect);
        if (targets == null) return;

        foreach (BattleEntity target in targets)
        {
            if (target == null) continue;
            float totalEnergy = ApplyEnergyGain(
                target, gainedEnergy, effect.Param2, effect.Element);
            LogManager.Log(LogCategory.GainEnergy,
                $"{target.EntityID} +{totalEnergy:F1} energy (mode={effect.Param2})");
        }
    }

    void ExecuteGainEnergy(
        StatusEffectData effect,
        StatusInstance sourceStatus,
        object host)
    {
        if (effect == null) return;
        if (!WeaponHooks.TryResolveEnergy(effect, sourceStatus, out float gainedEnergy)
            && !float.TryParse(effect.Param1, NumberStyles.Float, CultureInfo.InvariantCulture, out gainedEnergy)) return;
        List<BattleEntity> targets = ResolveStatusTargets(effect, sourceStatus, host);
        if (targets == null) return;

        foreach (BattleEntity target in targets)
        {
            if (target == null) continue;
            float totalEnergy = ApplyEnergyGain(
                target, gainedEnergy, effect.Param2, effect.Element);
            WeaponStackTransferHooks.EnergyCommitted(effect, sourceStatus, target);
            LogManager.Log(LogCategory.StatusEnergy,
                $"{target.EntityID} +{totalEnergy:F1}");
        }
    }

    // TotalEnergy = GainedEnergy × OnstageStatus × ElementStatus × TotalRechargeRate
    public static float ApplyEnergyGain(
        BattleEntity target,
        float gainedEnergy,
        string gainMode,
        string energyElement)
    {
        if (target == null) return 0f;

        float totalEnergy = gainedEnergy;
        if (TableValidator.NormalizeEnergyMode(gainMode) == "Based")
        {
            float onstageStatus = GetEnergyOnstageStatus(target);
            float elementStatus = GetEnergyElementStatus(target, energyElement);
            float totalRechargeRate = target.GetFinalRechargeRate();
            totalEnergy = gainedEnergy * onstageStatus * elementStatus * totalRechargeRate;
        }

        if (float.IsNaN(totalEnergy) || float.IsInfinity(totalEnergy))
            throw new System.InvalidOperationException("GainEnergy计算结果必须为有限数值。");
        float before = target.CurrentEnergy;
        target.CurrentEnergy = Mathf.Min(
            before + totalEnergy,
            target.MaxEnergy);
        if (totalEnergy != 0f) BattleEventHub.Publish(new ResourceEventData("Energy", "Energy", target,
            before, target.CurrentEnergy, totalEnergy, ResourceChangeReason.Gain, false));
        return totalEnergy;
    }

    private static float GetEnergyOnstageStatus(BattleEntity target)
    {
        if (target.Type != BattleEntity.EntityType.Character
            || target.CharacterCtrl == null
            || target.CharacterCtrl.IsActive)
            return 1f;

        // 队伍人数按本场战斗注册的完整我方编队计算；阵亡不会改变编队人数。
        IReadOnlyList<CharacterBattleController> allies =
            BattleManager.Instance != null ? BattleManager.Instance.Allies : null;
        if (allies == null) return 1f;

        int partySize = 0;
        for (int i = 0; i < allies.Count; i++)
        {
            if (allies[i] != null)
                partySize++;
        }

        if (partySize >= 4) return 0.6f;
        if (partySize == 3) return 0.7f;
        if (partySize == 2) return 0.8f;
        return 1f;
    }

    private static float GetEnergyElementStatus(
        BattleEntity target,
        string energyElement)
    {
        if (string.IsNullOrEmpty(energyElement)) return 1f;

        return target.Type == BattleEntity.EntityType.Character
            && target.CharacterCtrl != null
            && target.CharacterCtrl._attrData != null
            && target.CharacterCtrl._attrData.Element == energyElement
                ? 1.5f
                : 0.5f;
    }
}
