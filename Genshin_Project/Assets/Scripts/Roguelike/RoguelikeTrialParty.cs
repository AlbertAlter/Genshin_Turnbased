using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using OfficeOpenXml;

/// <summary>试用构筑的配表快照。仅属于本场节点，不成为已拥有角色或背包物品。</summary>
[Serializable]
public sealed class StageTrialSetup
{
    public string ConfigID;
    public int CharacterID, WeaponID;
    public int Constellation, Refinement = 1, ArtifactSetID;
    public string Sands = "ATKBonus", Goblet = "ATKBonus", Circlet = "CritRate";
}

/// <summary>战斗、编队共用试用配置解析及构筑。Tryout与TryoutConfig按分号一一对应。</summary>
public static class RoguelikeTrialParty
{
    public static List<StageTrialSetup> Read(ExcelWorkbook book, string characters, string configurations)
    {
        string[] Split(string value) => string.IsNullOrWhiteSpace(value) ? Array.Empty<string>() : value.Split(';').Select(v => v.Trim()).ToArray();
        var ids = Split(characters); var keys = Split(configurations);
        if (ids.Length != keys.Length) throw new InvalidDataException("Tryout与TryoutConfig的数量必须一致。");
        var result = new List<StageTrialSetup>();
        if (ids.Length == 0) return result;
        var sheet = book.Worksheets["Tryout_Config"] ?? throw new InvalidDataException("缺少Tryout_Config试用构筑子表。");
        var h = ExcelTableHeaders.For(sheet);
        h.Require("TryoutConfigID", "CharacterID", "WeaponID", "ArtifactSetID");
        var rows = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int r = h.DataStartRow; r <= sheet.Dimension.End.Row; r++)
        {
            string key = h.Cell(r, "TryoutConfigID").Text.Trim();
            if (key.Length > 0 && !rows.TryAdd(key, r)) throw new InvalidDataException("试用构筑编号重复：" + key);
        }
        for (int i = 0; i < ids.Length; i++)
        {
            int id = Number(ids[i], 1, int.MaxValue, "Tryout");
            if (result.Any(t => t.CharacterID == id)) throw new InvalidDataException("同场试用角色重复：" + id);
            if (!rows.TryGetValue(keys[i], out int row)) throw new InvalidDataException("试用构筑不存在：" + keys[i]);
            int Value(string key, int fallback, int min, int max) => h.Optional(key) < 1 || h.Cell(row, key).Text.Trim().Length == 0
                ? fallback : Number(h.Cell(row, key).Text.Trim(), min, max, "Tryout_Config/" + key);
            if (Value("CharacterID", 0, 1, int.MaxValue) != id) throw new InvalidDataException("试用角色与构筑不匹配：" + keys[i]);
            string Text(string key, string fallback) => h.Optional(key) > 0 && h.Cell(row, key).Text.Trim().Length > 0 ? h.Cell(row, key).Text.Trim() : fallback;
            result.Add(new StageTrialSetup { ConfigID = keys[i], CharacterID = id,
                WeaponID = Value("WeaponID", 0, 1, int.MaxValue), ArtifactSetID = Value("ArtifactSetID", 0, 1, int.MaxValue),
                Constellation = Value("Constellation", 0, 0, 6), Refinement = Value("Refinement", 1, 1, 5),
                Sands = Text("Sands", "ATKBonus"), Goblet = Text("Goblet", "ATKBonus"), Circlet = Text("Circlet", "CritRate") });
        }
        return result;
    }

    public static List<RoguelikeBattleAllyBuild> Build(IEnumerable<StageTrialSetup> definitions,
        RoguelikeProgressSnapshot snapshot, IRoguelikeEquipmentCatalog equipment, ArtifactGenerationConfig artifactConfig = null)
    {
        var limits = new RoguelikeUpgradeLimits(new RoguelikeSavedUpgradeGate().GetUnlockedAscensions(snapshot));
        var result = new List<RoguelikeBattleAllyBuild>();
        foreach (var d in definitions ?? Enumerable.Empty<StageTrialSetup>())
        {
            if (!equipment.TryGetCharacterWeaponType(d.CharacterID, out int type) ||
                !equipment.TryGetWeapon(d.WeaponID, out var weapon) || weapon.WeaponType != type)
                throw new InvalidDataException("试用角色或武器模板不存在，或类型不匹配：" + d.ConfigID);
            int level = limits.CharacterAndWeaponLevel;
            string instance = "trial:" + d.ConfigID;
            var build = new RoguelikeBattleAllyBuild { IsTrial = true,
                Character = new RoguelikeCharacterProgressData { CharacterID = d.CharacterID, Level = level,
                    Ascension = limits.GetAscension(level), ConstellationLevel = d.Constellation,
                    SkillLevels = Enumerable.Repeat(limits.SkillBaseLevel, 4).ToArray(), EquippedWeaponInstanceId = instance },
                Weapon = new RoguelikeWeaponInstanceData { InstanceId = instance,
                    Weapon = new WeaponLoadout { WeaponID = d.WeaponID, Level = level,
                        Ascension = limits.GetAscension(level), Refinement = d.Refinement } } };
            artifactConfig ??= DataManager.Instance.GetArtifactConfig();
            string[] mains = { "HPBonusFlat", "ATKBonusFlat", d.Sands, d.Goblet, d.Circlet };
            for (int slot = 1; slot <= 5; slot++)
            {
                string main = mains[slot - 1];
                if (!artifactConfig.GetMainWeights((ArtifactSlot)slot).Any(w => w.AttributeType == main))
                    throw new InvalidDataException("试用圣遗物主属性不适用于部位：" + main);
                var growth = artifactConfig.GetMainGrowth(main, 5);
                int artifactLevel = Math.Min(limits.ArtifactLevel, artifactConfig.GetMaxLevel(5));
                var artifact = new GeneratedArtifact { Star = 5, Slot = (ArtifactSlot)slot, Level = artifactLevel,
                    MaxLevel = artifactConfig.GetMaxLevel(5), MainAttributeType = main,
                    MainAttributeValue = growth.InitialStat + growth.LevelBonus * artifactLevel };
                // 固定四词条及均匀强化，反复打开预览不抽随机、不重掷试用装备。
                var secondary = new[] { "CritRate", "CritDMG", "ATKBonus", "RechargeBonus", "EMBonus", "HPBonus" }
                    .Where(k => k != main).Select(k => artifactConfig.SecondaryRules.FirstOrDefault(r => r.AttributeType == k))
                    .Where(r => r != null).Take(4).ToArray();
                if (secondary.Length != 4) throw new InvalidDataException("试用圣遗物缺少副属性配置。");
                for (int i = 0; i < 4; i++)
                {
                    int rolls = 1 + artifactLevel / 4 / 4 + (i < artifactLevel / 4 % 4 ? 1 : 0);
                    artifact.SecondaryAttributes.Add(new ArtifactSecondaryAttribute { AttributeType = secondary[i].AttributeType,
                        Value = secondary[i].Stat * rolls, RollCount = rolls });
                }
                string artifactId = instance + ":" + slot;
                build.Artifacts.Add(new RoguelikeArtifactInstanceData { InstanceId = artifactId, ArtifactSetID = d.ArtifactSetID, Artifact = artifact });
                build.Character.EquippedArtifacts.Add(new RoguelikeArtifactEquipmentData { Slot = (ArtifactSlot)slot, ArtifactInstanceId = artifactId });
            }
            result.Add(build);
        }
        return result;
    }

    private static int Number(string value, int min, int max, string field)
        => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n >= min && n <= max ? n
            : throw new InvalidDataException(field + "无效：" + value);
}
