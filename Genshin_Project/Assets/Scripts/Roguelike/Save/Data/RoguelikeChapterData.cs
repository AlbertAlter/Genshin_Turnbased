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
    public int[] SkillLevels = { 1, 1, 1, 1 };
    public string EquippedWeaponInstanceId;
    public List<RoguelikeArtifactEquipmentData> EquippedArtifacts =
        new List<RoguelikeArtifactEquipmentData>();
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
