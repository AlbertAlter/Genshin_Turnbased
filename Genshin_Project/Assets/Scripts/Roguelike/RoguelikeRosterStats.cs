using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>将档案与正式属性配置整理为养成界面的常驻属性快照。</summary>
public static class RoguelikeRosterStats
{
    public static Dictionary<string, float> Read(RoguelikeRosterSession session, RoguelikeCharacterProgressData character,
        int level, int ascension)
        => Read(session.Data, character.CharacterID, level, ascension, session, character);

    internal static Dictionary<string, float> ReadBase(DataManager data, int characterId, int level, int ascension)
        => Read(data, characterId, level, ascension, null, null);

    private static Dictionary<string, float> Read(DataManager data, int characterId, int level, int ascension,
        RoguelikeRosterSession session, RoguelikeCharacterProgressData character)
    {
        var attr = data.CharacterAttributesDict[characterId];
        var bonus = new Dictionary<string, float>(StringComparer.Ordinal);
        float Get(string key) => bonus.TryGetValue(key, out float value) ? value : 0;
        void Add(string key, float value)
        {
            if (!string.IsNullOrEmpty(key)) bonus[key] = Get(key) + value;
        }
        float growth = data.GetGrowthCurve(attr.GrowthCurveID, level);
        float hp = attr.BaseHP * growth, atk = attr.BaseATK * growth, def = attr.BaseDEF * growth;
        Add("EMBonus", attr.BaseEM);
        Add("CritRate", attr.CritRate);
        Add("CritDMG", attr.CritDMG);
        Add("RechargeBonus", attr.BaseRechargeRate);
        string[] elements = { "Pyro", "Hydro", "Electro", "Cryo", "Anemo", "Geo", "Dendro", "Physical" };
        float[] resistance = { attr.PyroRes, attr.HydroRes, attr.ElectroRes, attr.CryoRes,
            attr.AnemoRes, attr.GeoRes, attr.DendroRes, attr.PhysicalRes };
        float[] damage = { attr.PyroDmgBonus, attr.HydroDmgBonus, attr.ElectroDmgBonus, attr.CryoDmgBonus,
            attr.AnemoDmgBonus, attr.GeoDmgBonus, attr.DendroDmgBonus, attr.PhysicalDmgBonus };
        for (int i = 0; i < elements.Length; i++) Add(elements[i] + "RES", resistance[i]);
        if (data.CharacterAscensionDict.TryGetValue(characterId, out var rows))
            foreach (var row in rows)
            {
                if (!CharacterAscensionRules.IncludesRow(row.AscensionLevel, level, ascension)) continue;
                hp += row.BaseHPFlat; atk += row.BaseATKFlat; def += row.BaseDEFFlat;
                Add("HPBonus", row.HPBonus); Add("ATKBonus", row.ATKBonus); Add("DEFBonus", row.DEFBonus);
                Add("CritRate", row.CritRate); Add("CritDMG", row.CritDMG); Add("EMBonus", row.EM);
                Add("RechargeBonus", row.EnergyRechargeRate); Add("HealBonus", row.HealBonus);
                damage[0] += row.PyroDmgBonus; damage[1] += row.HydroDmgBonus; damage[2] += row.ElectroDmgBonus;
                damage[3] += row.CryoDmgBonus; damage[4] += row.AnemoDmgBonus; damage[5] += row.GeoDmgBonus;
                damage[6] += row.DendroDmgBonus; damage[7] += row.PhysicalDmgBonus;
            }
        for (int i = 0; i < elements.Length; i++) Add(elements[i] + "DMGBonus", damage[i]);
        if (session != null)
        {
            var weapon = session.Weapon(character.EquippedWeaponInstanceId)?.Weapon;
            if (weapon != null)
            {
                var panel = data.GetWeaponPanel(weapon.WeaponID, weapon.Level, weapon.Ascension);
                if (panel != null) { atk += panel.BaseATK; Add(panel.SecondaryAttribute, panel.SecondaryValue); }
            }
            foreach (var equipped in character.EquippedArtifacts)
            {
                var artifact = session.Artifact(equipped.ArtifactInstanceId)?.Artifact;
                if (artifact == null) continue;
                Add(artifact.MainAttributeType, artifact.MainAttributeValue);
                foreach (var stat in artifact.SecondaryAttributes) Add(stat.AttributeType, stat.Value);
            }
        }
        var output = new Dictionary<string, float>
        {
            ["HP"] = hp * (1 + Get("HPBonus")) + Get("HPBonusFlat"),
            ["ATK"] = atk * (1 + Get("ATKBonus")) + Get("ATKBonusFlat"),
            ["DEF"] = def * (1 + Get("DEFBonus")) + Get("DEFBonusFlat"),
            ["EMBonus"] = Get("EMBonus") + Get("WeaponEM"),
            ["CritRate"] = Get("CritRate"), ["CritDMG"] = Get("CritDMG"), ["HealBonus"] = Get("HealBonus"),
            ["BeHealedBonus"] = Get("BeHealedBonus"),
            ["RechargeBonus"] = Get("RechargeBonus"), ["ShieldStrength"] = Get("ShieldStrength"),
        };
        foreach (string element in elements)
        {
            output[element + "DMGBonus"] = Get(element + "DMGBonus");
            output[element + "RES"] = Get(element + "RES");
        }
        return output;
    }

    internal static string Name(string key, bool english)
    {
        string T(string zh, string en) => english ? en : zh;
        switch (key)
        {
            case "HP": case "HPBonusFlat": return T("生命值", "HP");
            case "ATK": case "ATKBonusFlat": return T("攻击力", "ATK");
            case "DEF": case "DEFBonusFlat": return T("防御力", "DEF");
            case "HPBonus": return T("生命值加成", "HP Bonus");
            case "ATKBonus": return T("攻击力加成", "ATK Bonus");
            case "DEFBonus": return T("防御力加成", "DEF Bonus");
            case "WeaponEM": case "EMBonus": return T("元素精通", "Elemental Mastery");
            case "CritRate": return T("暴击率", "CRIT Rate");
            case "CritDMG": return T("暴击伤害", "CRIT DMG");
            case "RechargeBonus": return T("元素充能效率", "Energy Recharge");
            case "HealBonus": return T("治疗加成", "Healing Bonus");
            case "BeHealedBonus": return T("受治疗加成", "Incoming Healing Bonus");
            case "ShieldStrength": return T("护盾强效", "Shield Strength");
        }
        if (key == null) return "—";
        string[] elements = { "Pyro", "Hydro", "Electro", "Cryo", "Anemo", "Geo", "Dendro", "Physical" };
        string[] chinese = { "火元素", "水元素", "雷元素", "冰元素", "风元素", "岩元素", "草元素", "物理" };
        for (int i = 0; i < elements.Length; i++)
        {
            if (key == elements[i] + "DMGBonus") return T(chinese[i] + "伤害加成", elements[i] + " DMG Bonus");
            if (key == elements[i] + "RES") return T(chinese[i] + "抗性", elements[i] + " RES");
        }
        return key;
    }

    internal static string FormatValue(string key, float value)
    {
        bool flat = key == "HP" || key == "ATK" || key == "DEF" || key == "WeaponEM" ||
                    key == "EMBonus" || key?.EndsWith("Flat", StringComparison.Ordinal) == true;
        return value.ToString(flat ? "N0" : "P1", CultureInfo.InvariantCulture);
    }
}
