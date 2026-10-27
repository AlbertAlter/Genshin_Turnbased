using System;

/// <summary>决定持有数据格式；业务分类（例如命座道具）另由 Category 表达。</summary>
public enum RoguelikeItemType
{
    Stack = 1,
    Weapon = 2,
    Artifact = 3,
}

/// <summary>Items.xlsx 的静态物品定义，不保存强化数据，也不复制进存档。</summary>
[Serializable]
public sealed class RoguelikeItemDefinition
{
    public string ItemId;
    public string Name;
    public string Description;
    public string Icon;
    public string Category;
    public RoguelikeItemType ItemType = RoguelikeItemType.Stack;
    public int ReferenceID;
    public int Slot;
    public bool Stackable = true;
    public int Rarity;
    public string UsageType;
    public string UsageReference;
    // 经验材料每件提供的经验；天赋材料按种类计件，两者不能互相换算。
    public int ExperienceValue;
    public int TalentMaterialType;
}

/// <summary>未知 ID 返回 false；物品分类、数据格式和使用效果以配表为准。</summary>
public interface IRoguelikeItemCatalog
{
    bool TryGetItem(string itemId, out RoguelikeItemDefinition definition);
}

/// <summary>统一背包列表条目。装备一件一条，详细强化数据通过 InstanceId 查询原实例。</summary>
public sealed class RoguelikeInventoryEntry
{
    public string ItemId;
    public RoguelikeItemType ItemType;
    public string InstanceId;
    public int Quantity;
}

/// <summary>复用已加载的武器/角色模板，不由背包读取策划源文件。</summary>
public interface IRoguelikeEquipmentCatalog
{
    bool TryGetWeapon(int weaponId, out WeaponAttributesData definition);
    bool TryGetCharacterWeaponType(int characterId, out int weaponType);
}

/// <summary>正式接入适配器，调用前先由现有启动流程初始化 DataManager。</summary>
public sealed class DataManagerRoguelikeEquipmentCatalog : IRoguelikeEquipmentCatalog
{
    private readonly DataManager data;

    public DataManagerRoguelikeEquipmentCatalog(DataManager data)
    {
        this.data = data ?? throw new ArgumentNullException(nameof(data));
    }

    public bool TryGetWeapon(int weaponId, out WeaponAttributesData definition)
    {
        definition = null;
        if (!data.IsLoaded || !data.WeaponAttributesDict.TryGetValue(weaponId, out var found)) return false;
        definition = RoguelikeSaveJson.DeepClone(found);
        return definition != null;
    }

    public bool TryGetCharacterWeaponType(int characterId, out int weaponType)
    {
        weaponType = -1;
        if (!data.IsLoaded || !data.CharacterAttributesDict.TryGetValue(characterId, out var found) || found == null)
            return false;
        weaponType = found.WeaponType;
        return weaponType >= 0 && weaponType <= 4;
    }
}
