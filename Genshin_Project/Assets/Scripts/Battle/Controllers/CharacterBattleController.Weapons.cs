using System.Collections.Generic;
using UnityEngine;

public partial class CharacterBattleController
{
    public WeaponLoadout EquippedWeapon { get; private set; }
    public WeaponRuntimeContext EquippedWeaponContext { get; private set; }

    /// <summary>开战前登记装备构筑；先设站位并注册完整编队，避免初始化子效果找不到Self或队友。</summary>
    public bool EquipWeapon(WeaponLoadout loadout)
    {
        if (loadout == null || loadout.WeaponID <= 0 || Entity == null) return false;
        var dm = DataManager.Instance;
        if (dm == null || !dm.WeaponAttributesDict.TryGetValue(loadout.WeaponID, out var attributes))
        {
            LogManager.LogWarning(LogCategory.Build, $"武器不存在: {loadout.WeaponID}");
            return false;
        }
        if (_attrData != null && attributes.WeaponType != _attrData.WeaponType)
        {
            LogManager.LogWarning(LogCategory.Build,
                $"武器类型不匹配: character={CharacterID} requires {_attrData.WeaponType}, weapon={loadout.WeaponID} type={attributes.WeaponType}");
            return false;
        }

        BattleAttributeInitialization.BeginBuild(Entity);
        UnequipWeapon();
        int level = Mathf.Clamp(loadout.Level, 1, 90);
        int refinement = Mathf.Clamp(loadout.Refinement, 1, 5);
        int ascension = Mathf.Clamp(loadout.Ascension, 0, 6);
        WeaponPanel panel = dm.GetWeaponPanel(loadout.WeaponID, level, ascension);
        if (panel == null) return false;

        EquippedWeapon = new WeaponLoadout
        {
            WeaponID = loadout.WeaponID, Level = level, Ascension = ascension, Refinement = refinement
        };
        EquippedWeaponContext = dm.CreateWeaponContext(loadout.WeaponID, refinement, Entity);
        ApplyWeaponPanel(panel, 1f);

        if (dm.WeaponInitiateStatusDict.TryGetValue(loadout.WeaponID, out List<string> statuses))
        {
            foreach (string statusID2 in statuses)
            {
                if (!dm.StatusMainDict.TryGetValue(statusID2, out var main)) continue;
                // 初始化常驻监听/属性永久存在；后续子状态或专用Count仍按各自配表/处理器计时。
                StatusInstance status = Entity.AddStatus(statusID2, Entity, 0, 2, 0, main, EquippedWeaponContext);
                RecordInitializationStatus(status, 0, 2, InitialStatusKind.Weapon);
            }
        }
        // 这里只登记构筑输入；当前HP由StartBattle在全队/装备/绑定全部初始化后统一提交。
        LogManager.Log(LogCategory.Build, $"装备武器 {loadout.WeaponID} Lv{level} R{refinement}");
        return true;
    }

    public void UnequipWeapon()
    {
        if (Entity == null || EquippedWeapon == null) return;
        _initialStatuses.RemoveAll(entry => entry.Kind == InitialStatusKind.Weapon);
        string sourceInstanceID = EquippedWeaponContext?.SourceInstanceID;
        foreach (StatusInstance status in Entity.GetStatusList())
        {
            if (status?.WeaponContext == null) continue;
            if (status.WeaponContext.SourceInstanceID == sourceInstanceID)
                Entity.RemoveStatusInstance(status);
        }
        WeaponPanel panel = DataManager.Instance?.GetWeaponPanel(
            EquippedWeapon.WeaponID, EquippedWeapon.Level, EquippedWeapon.Ascension);
        if (panel != null) ApplyWeaponPanel(panel, -1f);
        EquippedWeapon = null;
        EquippedWeaponContext = null;
    }

    void ApplyWeaponPanel(WeaponPanel panel, float sign)
    {
        Entity.WeaponATK += sign * panel.BaseATK;
        float value = sign * panel.SecondaryValue;
        switch (panel.SecondaryAttribute)
        {
            case "ATKBonus": Entity.WeaponATKBonus += value; break;
            case "HPBonus":
                Entity.WeaponHPBonus += value;
                Entity.BakedHPBonus += value;
                ApplyWeaponHPBonus(value);
                break;
            case "DEFBonus":
                Entity.WeaponDEFBonus += value;
                ApplyWeaponDEFBonus(value);
                break;
            case "RechargeBonus": Entity.WeaponRechargeBonus += value; Entity.TotalRechargeRate += value; break;
            case "CritRate": Entity.WeaponCritRate += value; break;
            case "CritDMG": Entity.WeaponCritDMG += value; break;
            case "WeaponEM": Entity.WeaponEM += value; Entity.TotalEM += value; break;
            case "PhysicalDMGBonus": Entity.WeaponPhysicalDmgBonus += value; break;
            case "PyroDmgBonus": Entity.WeaponPyroDmgBonus += value; break;
            case "HydroDmgBonus": Entity.WeaponHydroDmgBonus += value; break;
            case "ElectroDmgBonus": Entity.WeaponElectroDmgBonus += value; break;
            case "CryoDmgBonus": Entity.WeaponCryoDmgBonus += value; break;
            case "AnemoDmgBonus": Entity.WeaponAnemoDmgBonus += value; break;
            case "DendroDmgBonus": Entity.WeaponDendroDmgBonus += value; break;
            case "GeoDmgBonus": Entity.WeaponGeoDmgBonus += value; break;
        }
    }

    void ApplyWeaponHPBonus(float bonusDelta)
    {
        float baseValue = GetHPBeforePercentBonuses();
        if (baseValue <= 0f) return;
        Entity.TotalHP = Mathf.Max(0f, Entity.TotalHP + baseValue * bonusDelta);
    }

    void ApplyWeaponDEFBonus(float bonusDelta)
    {
        float baseValue = GetDEFBeforePercentBonuses();
        if (baseValue <= 0f) return;
        Entity.TotalDEF = Mathf.Max(0f, Entity.TotalDEF + baseValue * bonusDelta);
    }

    internal float GetHPBeforePercentBonuses()
    {
        if (_attrData == null || DataManager.Instance == null) return 0f;
        float value = _attrData.BaseHP * DataManager.Instance.GetGrowthCurve(_attrData.GrowthCurveID, Level);
        if (_ascDataList != null)
            foreach (CharacterAscensionData ascension in _ascDataList)
                value += ascension.BaseHPFlat;
        return value;
    }

    internal float GetDEFBeforePercentBonuses()
    {
        if (_attrData == null || DataManager.Instance == null) return 0f;
        float value = _attrData.BaseDEF * DataManager.Instance.GetGrowthCurve(_attrData.GrowthCurveID, Level);
        if (_ascDataList != null)
            foreach (CharacterAscensionData ascension in _ascDataList)
                value += ascension.BaseDEFFlat;
        return value;
    }
}
