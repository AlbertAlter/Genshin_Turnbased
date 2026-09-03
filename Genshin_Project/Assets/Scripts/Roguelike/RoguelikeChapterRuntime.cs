using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 章节内数据的纯 C# 入口。它只维护内存状态并导出快照，不直接接触场景、UI 或存档时机。
/// </summary>
public sealed class RoguelikeChapterRuntime
{
    private readonly RoguelikeSaveValidator validator = new RoguelikeSaveValidator();
    private readonly RoguelikeProgressSnapshot snapshot;

    public RoguelikeChapterRuntime(RoguelikeProgressSnapshot source)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        RoguelikeSaveResult validation = validator.Validate(source);
        if (!validation.Succeeded)
            throw new ArgumentException("章节快照无效：" + validation.Message, nameof(source));
        snapshot = RoguelikeSaveJson.DeepClone(source);
    }

    public void AddCharacter(int characterId, float maxHealth, float maxEnergy)
    {
        if (characterId <= 0) throw new ArgumentOutOfRangeException(nameof(characterId));
        ValidateVitalLimits(maxHealth, maxEnergy);
        if (FindCharacter(characterId) != null)
            throw new InvalidOperationException("角色已经拥有：" + characterId + "。");

        snapshot.Characters.Add(new RoguelikeCharacterProgressData { CharacterID = characterId });
        snapshot.CharacterVitals.Add(new RoguelikeCharacterVitalData
        {
            CharacterID = characterId,
            CurrentHealth = maxHealth,
            MaxHealth = maxHealth,
            CurrentEnergy = maxEnergy,
            MaxEnergy = maxEnergy,
        });
    }

    public void UpdateCharacterProgress(int characterId, int level, int experience, int ascension,
        int constellationLevel, IReadOnlyList<int> skillLevels)
    {
        RoguelikeCharacterProgressData character = RequireCharacter(characterId);
        if (level < 1 || level > 90) throw new ArgumentOutOfRangeException(nameof(level));
        if (experience < 0) throw new ArgumentOutOfRangeException(nameof(experience));
        if (ascension < 0 || ascension > 6) throw new ArgumentOutOfRangeException(nameof(ascension));
        if (constellationLevel < 0 || constellationLevel > 6)
            throw new ArgumentOutOfRangeException(nameof(constellationLevel));
        if (skillLevels == null || skillLevels.Count != 4 || skillLevels.Any(value => value < 1 || value > 15))
            throw new ArgumentException("技能等级必须正好包含四个 1～15 的值。", nameof(skillLevels));

        character.Level = level;
        character.Experience = experience;
        character.Ascension = ascension;
        character.ConstellationLevel = constellationLevel;
        character.SkillLevels = skillLevels.ToArray();
    }

    public void SetParty(IEnumerable<int> characterIds)
    {
        if (characterIds == null) throw new ArgumentNullException(nameof(characterIds));
        List<int> party = characterIds.ToList();
        if (party.Count > 4) throw new ArgumentException("出战队伍最多包含 4 名角色。", nameof(characterIds));
        if (party.Distinct().Count() != party.Count)
            throw new ArgumentException("出战队伍不能包含重复角色。", nameof(characterIds));
        for (int i = 0; i < party.Count; i++)
            RequireCharacter(party[i]);
        snapshot.PartyCharacterIds = party;
    }

    public void UpdateVitals(int characterId, float currentHealth, float currentEnergy)
    {
        RoguelikeCharacterVitalData vital = RequireVital(characterId);
        ValidateFinite(currentHealth, nameof(currentHealth));
        ValidateFinite(currentEnergy, nameof(currentEnergy));
        if (currentHealth < 0f || currentHealth > vital.MaxHealth)
            throw new ArgumentOutOfRangeException(nameof(currentHealth));
        if (currentEnergy < 0f || currentEnergy > vital.MaxEnergy)
            throw new ArgumentOutOfRangeException(nameof(currentEnergy));
        vital.CurrentHealth = currentHealth;
        vital.CurrentEnergy = currentEnergy;
    }

    public void UpdateVitalLimits(int characterId, float maxHealth, float maxEnergy)
    {
        ValidateVitalLimits(maxHealth, maxEnergy);
        RoguelikeCharacterVitalData vital = RequireVital(characterId);
        bool wasFullHealth = Math.Abs(vital.CurrentHealth - vital.MaxHealth) <= 0.0001f;
        vital.CurrentHealth = wasFullHealth ? maxHealth : Math.Min(vital.CurrentHealth, maxHealth);
        vital.CurrentEnergy = Math.Min(vital.CurrentEnergy, maxEnergy);
        vital.MaxHealth = maxHealth;
        vital.MaxEnergy = maxEnergy;
    }

    public int AdjustItem(string itemId, int delta)
    {
        if (string.IsNullOrWhiteSpace(itemId)) throw new ArgumentException("ItemId 不能为空。", nameof(itemId));
        RoguelikeItemStackData item = snapshot.Inventory.Items
            .FirstOrDefault(entry => string.Equals(entry.ItemId, itemId, StringComparison.Ordinal));
        long current = item == null ? 0L : item.Quantity;
        long result = current + delta;
        if (result < 0L || result > int.MaxValue)
            throw new InvalidOperationException("物品数量调整后超出有效范围：" + itemId + "。");
        if (result == 0L)
        {
            if (item != null) snapshot.Inventory.Items.Remove(item);
            return 0;
        }
        if (item == null)
        {
            item = new RoguelikeItemStackData { ItemId = itemId };
            snapshot.Inventory.Items.Add(item);
        }
        item.Quantity = (int)result;
        return item.Quantity;
    }

    public void AddWeapon(string instanceId, WeaponLoadout weapon, int experience = 0)
    {
        RequireNewInstanceId(instanceId);
        if (weapon == null) throw new ArgumentNullException(nameof(weapon));
        if (experience < 0) throw new ArgumentOutOfRangeException(nameof(experience));
        var instance = new RoguelikeWeaponInstanceData
        {
            InstanceId = instanceId,
            Experience = experience,
            Weapon = RoguelikeSaveJson.DeepClone(weapon),
        };
        RoguelikeProgressSnapshot candidate = RoguelikeSaveJson.DeepClone(snapshot);
        candidate.Inventory.Weapons.Add(instance);
        EnsureValid(candidate);
        snapshot.Inventory.Weapons.Add(instance);
    }

    public void EquipWeapon(int characterId, string instanceId)
    {
        RoguelikeCharacterProgressData character = RequireCharacter(characterId);
        RequireWeapon(instanceId);
        RoguelikeCharacterProgressData owner = snapshot.Characters.FirstOrDefault(item =>
            item.CharacterID != characterId &&
            string.Equals(item.EquippedWeaponInstanceId, instanceId, StringComparison.Ordinal));
        if (owner != null)
            throw new InvalidOperationException("武器实例已装备给角色 " + owner.CharacterID + "。");
        character.EquippedWeaponInstanceId = instanceId;
    }

    public void UnequipWeapon(int characterId)
    {
        RequireCharacter(characterId).EquippedWeaponInstanceId = null;
    }

    public void AddArtifact(string instanceId, GeneratedArtifact artifact, int experience = 0)
    {
        RequireNewInstanceId(instanceId);
        if (artifact == null) throw new ArgumentNullException(nameof(artifact));
        if (experience < 0) throw new ArgumentOutOfRangeException(nameof(experience));
        var instance = new RoguelikeArtifactInstanceData
        {
            InstanceId = instanceId,
            Experience = experience,
            Artifact = RoguelikeSaveJson.DeepClone(artifact),
        };
        RoguelikeProgressSnapshot candidate = RoguelikeSaveJson.DeepClone(snapshot);
        candidate.Inventory.Artifacts.Add(instance);
        EnsureValid(candidate);
        snapshot.Inventory.Artifacts.Add(instance);
    }

    public void EquipArtifact(int characterId, string instanceId)
    {
        RoguelikeCharacterProgressData character = RequireCharacter(characterId);
        RoguelikeArtifactInstanceData instance = RequireArtifact(instanceId);
        RoguelikeCharacterProgressData owner = snapshot.Characters.FirstOrDefault(item =>
            item.CharacterID != characterId && item.EquippedArtifacts.Any(equipment =>
                string.Equals(equipment.ArtifactInstanceId, instanceId, StringComparison.Ordinal)));
        if (owner != null)
            throw new InvalidOperationException("圣遗物实例已装备给角色 " + owner.CharacterID + "。");

        character.EquippedArtifacts.RemoveAll(equipment => equipment.Slot == instance.Artifact.Slot);
        character.EquippedArtifacts.Add(new RoguelikeArtifactEquipmentData
        {
            Slot = instance.Artifact.Slot,
            ArtifactInstanceId = instanceId,
        });
    }

    public void UnequipArtifact(int characterId, ArtifactSlot slot)
    {
        if (!Enum.IsDefined(typeof(ArtifactSlot), slot)) throw new ArgumentOutOfRangeException(nameof(slot));
        RequireCharacter(characterId).EquippedArtifacts.RemoveAll(equipment => equipment.Slot == slot);
    }

    public RoguelikeProgressSnapshot ExportSnapshot()
    {
        EnsureValid(snapshot);
        return RoguelikeSaveJson.DeepClone(snapshot);
    }

    private RoguelikeCharacterProgressData FindCharacter(int characterId)
    {
        return snapshot.Characters.FirstOrDefault(character => character.CharacterID == characterId);
    }

    private RoguelikeCharacterProgressData RequireCharacter(int characterId)
    {
        RoguelikeCharacterProgressData character = FindCharacter(characterId);
        if (character == null) throw new KeyNotFoundException("未拥有角色：" + characterId + "。");
        return character;
    }

    private RoguelikeCharacterVitalData RequireVital(int characterId)
    {
        RoguelikeCharacterVitalData vital = snapshot.CharacterVitals
            .FirstOrDefault(item => item.CharacterID == characterId);
        if (vital == null) throw new KeyNotFoundException("角色缺少生命能量数据：" + characterId + "。");
        return vital;
    }

    private RoguelikeWeaponInstanceData RequireWeapon(string instanceId)
    {
        RoguelikeWeaponInstanceData instance = snapshot.Inventory.Weapons.FirstOrDefault(item =>
            string.Equals(item.InstanceId, instanceId, StringComparison.Ordinal));
        if (instance == null) throw new KeyNotFoundException("找不到武器实例：" + instanceId + "。");
        return instance;
    }

    private RoguelikeArtifactInstanceData RequireArtifact(string instanceId)
    {
        RoguelikeArtifactInstanceData instance = snapshot.Inventory.Artifacts.FirstOrDefault(item =>
            string.Equals(item.InstanceId, instanceId, StringComparison.Ordinal));
        if (instance == null) throw new KeyNotFoundException("找不到圣遗物实例：" + instanceId + "。");
        return instance;
    }

    private void RequireNewInstanceId(string instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
            throw new ArgumentException("实例 ID 不能为空。", nameof(instanceId));
        if (snapshot.Inventory.Weapons.Any(item => string.Equals(item.InstanceId, instanceId, StringComparison.Ordinal)) ||
            snapshot.Inventory.Artifacts.Any(item => string.Equals(item.InstanceId, instanceId, StringComparison.Ordinal)))
            throw new InvalidOperationException("实例 ID 已存在：" + instanceId + "。");
    }

    private void EnsureValid(RoguelikeProgressSnapshot value)
    {
        RoguelikeSaveResult validation = validator.Validate(value);
        if (!validation.Succeeded) throw new InvalidOperationException("章节状态无效：" + validation.Message);
    }

    private static void ValidateVitalLimits(float maxHealth, float maxEnergy)
    {
        ValidateFinite(maxHealth, nameof(maxHealth));
        ValidateFinite(maxEnergy, nameof(maxEnergy));
        if (maxHealth <= 0f) throw new ArgumentOutOfRangeException(nameof(maxHealth));
        if (maxEnergy < 0f) throw new ArgumentOutOfRangeException(nameof(maxEnergy));
    }

    private static void ValidateFinite(float value, string parameterName)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            throw new ArgumentOutOfRangeException(parameterName);
    }
}
