using UnityEngine;

public partial class BattleEntity
{
    public float GetStatusDEFBonus(string effectID = "", string skillID = "", int skillType = 0,
        string element = "", BattleEntity target = null)
    {
        var dm = DataManager.Instance;
        if (dm == null) return 0f;
        float total = 0f;
        foreach (StatusInstance status in GetEffectiveStatusList())
        {
            if (status?.MainData == null || !status.IsActive || status.MainData.GetMultiplierPart() != "DEFBonus"
                || !StatusAppliesTo(status, effectID, skillID, skillType, element, target)) continue;
            int stacks = Mathf.Max(1, status.StackCount);
            if (status.MainData.MaxCount > 0) stacks = Mathf.Min(stacks, status.MainData.MaxCount);
            foreach (var effect in dm.StatusEffectDict.Values)
            {
                if (effect == null || effect.StatusEffectID / 100 != status.MainData.StatusID
                    || (effect.EffectType != "Buff" && effect.EffectType != "Debuff")) continue;
                if (BuffExpressionEvaluator.TryResolveScalar(effect.Param1, status, out float value))
                    total += value * stacks;
            }
        }
        return total;
    }

    /// <summary>保留已有面板与武器加成，状态百分比以基础防御加算，不回写 TotalDEF。</summary>
    public float GetFinalDEF(string effectID = "", string skillID = "", int skillType = 0,
        string element = "", BattleEntity target = null)
    {
        float basis = CharacterCtrl != null ? CharacterCtrl.GetDEFBeforePercentBonuses() : TotalDEF;
        if (basis <= 0f) basis = TotalDEF;
        return Mathf.Max(0f, TotalDEF + basis * GetStatusDEFBonus(effectID, skillID, skillType, element, target));
    }
}
