using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>只读背包视图。查询返回独立数据，不暴露可修改的章节快照或配表对象。</summary>
public sealed class RoguelikeInventoryView
{
    private readonly RoguelikeInventoryData inventory;
    private readonly Dictionary<string, int> counts;
    private readonly Dictionary<string, int> weaponOwners = new Dictionary<string, int>(StringComparer.Ordinal);
    private readonly Dictionary<string, int> artifactOwners = new Dictionary<string, int>(StringComparer.Ordinal);
    private readonly IRoguelikeEquipmentCatalog equipment;
    private readonly IRoguelikeItemCatalog items;
    private readonly Dictionary<int, RoguelikeCharacterProgressData> characters;

    public RoguelikeInventoryView(RoguelikeProgressSnapshot snapshot, IRoguelikeEquipmentCatalog equipment = null,
        IRoguelikeItemCatalog items = null)
    {
        var validation = new RoguelikeSaveValidator().Validate(snapshot);
        if (!validation.Succeeded) throw new ArgumentException(validation.Message, nameof(snapshot));
        inventory = RoguelikeSaveJson.DeepClone(snapshot.Inventory);
        counts = inventory.Items.ToDictionary(item => item.ItemId, item => item.Quantity, StringComparer.Ordinal);
        this.equipment = equipment;
        this.items = items;
        characters = snapshot.Characters.ToDictionary(item => item.CharacterID, item => RoguelikeSaveJson.DeepClone(item));
        foreach (var character in snapshot.Characters)
        {
            if (!string.IsNullOrWhiteSpace(character.EquippedWeaponInstanceId))
                weaponOwners.Add(character.EquippedWeaponInstanceId, character.CharacterID);
            foreach (var equipped in character.EquippedArtifacts)
                artifactOwners.Add(equipped.ArtifactInstanceId, character.CharacterID);
        }
    }

    public int GetItemCount(string itemId)
    {
        return !string.IsNullOrWhiteSpace(itemId) && counts.TryGetValue(itemId, out int count) ? count : 0;
    }

    public IReadOnlyList<RoguelikeCharacterProgressData> GetCharacters()
        => characters.Values.Select(item => RoguelikeSaveJson.DeepClone(item)).ToArray();
    public RoguelikeCharacterProgressData FindCharacter(int characterId)
        => characters.TryGetValue(characterId, out var value) ? RoguelikeSaveJson.DeepClone(value) : null;
    /// <summary>当前视图中角色的培养/加级/本体等级；不存在返回null，不伪造默认角色。</summary>
    public CharacterSkillLevelInfo GetCharacterSkillLevels(int characterId, DataManager data = null)
        => characters.TryGetValue(characterId, out var value) ? value.GetSkillLevelInfo(data) : null;
    public RoguelikeWeaponInstanceData GetEquippedWeapon(int characterId)
        => characters.TryGetValue(characterId, out var character) ? FindWeapon(character.EquippedWeaponInstanceId) : null;
    public IReadOnlyList<RoguelikeArtifactInstanceData> GetEquippedArtifacts(int characterId)
        => characters.TryGetValue(characterId, out var character)
            ? character.EquippedArtifacts.Select(item => FindArtifact(item.ArtifactInstanceId)).ToArray()
            : Array.Empty<RoguelikeArtifactInstanceData>();

    public IReadOnlyList<RoguelikeItemStackData> GetItems()
    {
        return inventory.Items.Select(item => RoguelikeSaveJson.DeepClone(item)).ToArray();
    }

    /// <summary>统一展示入口；不会把同模板的多件装备合并。旧档未知套装的 ItemId 留空。</summary>
    public IReadOnlyList<RoguelikeInventoryEntry> GetEntries()
    {
        var result = new List<RoguelikeInventoryEntry>();
        foreach (var item in inventory.Items)
            result.Add(new RoguelikeInventoryEntry
            { ItemId = item.ItemId, ItemType = RoguelikeItemType.Stack, Quantity = item.Quantity });
        foreach (var item in inventory.Weapons)
            result.Add(new RoguelikeInventoryEntry
            {
                ItemId = RoguelikeItemIds.Weapon(item.Weapon.WeaponID), ItemType = RoguelikeItemType.Weapon,
                InstanceId = item.InstanceId, Quantity = 1,
            });
        foreach (var item in inventory.Artifacts)
            result.Add(new RoguelikeInventoryEntry
            {
                ItemId = item.ArtifactSetID > 0 ? RoguelikeItemIds.Artifact(item.ArtifactSetID, item.Artifact.Slot) : null,
                ItemType = RoguelikeItemType.Artifact, InstanceId = item.InstanceId, Quantity = 1,
            });
        return result.ToArray();
    }

    public IReadOnlyList<RoguelikeWeaponInstanceData> GetWeapons(int? weaponId = null)
    {
        return inventory.Weapons.Where(item => !weaponId.HasValue || item.Weapon.WeaponID == weaponId.Value)
            .Select(item => RoguelikeSaveJson.DeepClone(item)).ToArray();
    }

    public IReadOnlyList<RoguelikeArtifactInstanceData> GetArtifacts(ArtifactSlot? slot = null)
    {
        return inventory.Artifacts.Where(item => !slot.HasValue || item.Artifact.Slot == slot.Value)
            .Select(item => RoguelikeSaveJson.DeepClone(item)).ToArray();
    }

    public RoguelikeWeaponInstanceData FindWeapon(string instanceId)
    {
        return RoguelikeSaveJson.DeepClone(inventory.Weapons.FirstOrDefault(item => item.InstanceId == instanceId));
    }

    public RoguelikeArtifactInstanceData FindArtifact(string instanceId)
    {
        return RoguelikeSaveJson.DeepClone(inventory.Artifacts.FirstOrDefault(item => item.InstanceId == instanceId));
    }

    /// <summary>返回 0 表示没有角色装备此实例；不单独保存装备归属。</summary>
    public int GetWeaponOwner(string instanceId)
    {
        return instanceId != null && weaponOwners.TryGetValue(instanceId, out int owner) ? owner : 0;
    }

    public int GetArtifactOwner(string instanceId)
    {
        return instanceId != null && artifactOwners.TryGetValue(instanceId, out int owner) ? owner : 0;
    }

    public bool TryGetItemDefinition(string itemId, out RoguelikeItemDefinition definition)
    {
        definition = null;
        if (items == null || string.IsNullOrWhiteSpace(itemId) ||
            !items.TryGetItem(itemId, out var found) || found == null) return false;
        definition = RoguelikeSaveJson.DeepClone(found);
        return true;
    }

    public bool TryGetWeaponDefinition(int weaponId, out WeaponAttributesData definition)
    {
        definition = null;
        if (equipment == null || !equipment.TryGetWeapon(weaponId, out var found) || found == null) return false;
        definition = RoguelikeSaveJson.DeepClone(found);
        return true;
    }
}
