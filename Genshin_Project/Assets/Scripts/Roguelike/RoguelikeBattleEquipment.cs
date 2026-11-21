using System;
using System.Collections.Generic;
using System.Linq;

internal static class RoguelikeBattleEquipment
{
    internal static void Apply(CharacterBattleController controller, List<RoguelikeArtifactInstanceData> equipped)
    {
        var entity = controller.Entity;
        void Add(string key, float value)
        {
            switch (key)
            {
                case "HPBonusFlat": entity.TotalHP += value; break;
                case "HPBonus": entity.TotalHP += controller.GetHPBeforePercentBonuses() * value; entity.BakedHPBonus += value; break;
                case "ATKBonusFlat": entity.ArtifactFlatATK += value; break;
                case "ATKBonus": entity.CharacterATKBonus += value; break;
                case "DEFBonusFlat": entity.TotalDEF += value; break;
                case "DEFBonus": entity.TotalDEF += controller.GetDEFBeforePercentBonuses() * value; entity.CharacterDEFBonus += value; break;
                case "EMBonus": entity.EM += value; entity.TotalEM += value; break;
                case "CritRate": entity.CritRate += value; break;
                case "CritDMG": entity.CritDMG += value; break;
                case "RechargeBonus": entity.TotalRechargeRate += value; break;
                case "HealBonus": entity.HealBonus += value; break;
                case "PhysicalDMGBonus": entity.PhysicalDmgBonus += value; break;
                case "PyroDMGBonus": entity.PyroDmgBonus += value; break;
                case "CryoDMGBonus": entity.CryoDmgBonus += value; break;
                case "HydroDMGBonus": entity.HydroDmgBonus += value; break;
                case "GeoDMGBonus": entity.GeoDmgBonus += value; break;
                case "AnemoDMGBonus": entity.AnemoDmgBonus += value; break;
                case "DendroDMGBonus": entity.DendroDmgBonus += value; break;
                case "ElectroDMGBonus": entity.ElectroDmgBonus += value; break;
                default: throw new InvalidOperationException("战斗未支持圣遗物属性：" + key);
            }
        }
        foreach (var item in equipped)
        {
            Add(item.Artifact.MainAttributeType, item.Artifact.MainAttributeValue);
            foreach (var stat in item.Artifact.SecondaryAttributes) Add(stat.AttributeType, stat.Value);
        }
        if (!controller.InitializeArtifactSetStatuses(equipped.Where(item => item.ArtifactSetID > 0)
            .GroupBy(item => item.ArtifactSetID).ToDictionary(group => group.Key, group => group.Count())))
            throw new InvalidOperationException("圣遗物套装初始化失败。");
    }
}
