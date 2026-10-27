using System;
using System.Collections.Generic;

[Serializable]
public sealed class RoguelikeCharacterProgressData
{
    public int CharacterID;
    public int Level = 1;
    public int Experience;
    public int Ascension;
    public int ConstellationLevel;
    // 四项可培养的基础等级（1～10）。自身加级由GetSkillLevelInfo只读解析，不写回、不影响材料消耗。
    public int[] SkillLevels = { 1, 1, 1, 1 };
    public string EquippedWeaponInstanceId;
    public List<RoguelikeArtifactEquipmentData> EquippedArtifacts =
        new List<RoguelikeArtifactEquipmentData>();

    /// <summary>背包、养成与战斗输入共用的自身等级查询；返回独立结果，不包含队伍加级。</summary>
    public CharacterSkillLevelInfo GetSkillLevelInfo(DataManager data = null)
        => CharacterSkillLevelInfo.Resolve(CharacterID, Level, Ascension, ConstellationLevel,
            SkillLevels, data ?? DataManager.Instance);
}

[Serializable]
public sealed class RoguelikeCharacterVitalData
{
    public int CharacterID;
    public float CurrentHealth;
    public float MaxHealth;
    public float CurrentEnergy;
    public float MaxEnergy;
}

[Serializable]
public sealed class RoguelikeArtifactEquipmentData
{
    public ArtifactSlot Slot;
    public string ArtifactInstanceId;
}

[Serializable]
public sealed class RoguelikeItemStackData
{
    public string ItemId;
    public int Quantity;
}

[Serializable]
public sealed class RoguelikeWeaponInstanceData
{
    public string InstanceId;
    public int Experience;
    public WeaponLoadout Weapon = new WeaponLoadout();
}

[Serializable]
public sealed class RoguelikeArtifactInstanceData
{
    public string InstanceId;
    // 0 只用于旧档中尚未指定套装的实例；不按词条猜测套装。
    public int ArtifactSetID;
    public int Experience;
    public GeneratedArtifact Artifact = new GeneratedArtifact();
}

[Serializable]
public sealed class RoguelikeInventoryData
{
    public List<RoguelikeItemStackData> Items = new List<RoguelikeItemStackData>();
    public List<RoguelikeWeaponInstanceData> Weapons = new List<RoguelikeWeaponInstanceData>();
    public List<RoguelikeArtifactInstanceData> Artifacts = new List<RoguelikeArtifactInstanceData>();
}
