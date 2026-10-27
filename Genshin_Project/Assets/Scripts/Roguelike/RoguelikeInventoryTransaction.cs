using System;
using System.Collections.Generic;

/// <summary>
/// 独立背包草稿；复用章节运行时维护实例与装备关系。任何一次写操作失败，整个草稿即不可提交。
/// 生成器应先生成圣遗物结果，再传入 AddArtifact；背包不会生成或重新抽取词条。
/// 奖励/升级模块可导出完整快照，与该步骤其他结果一起提交，不先单独扣材料或保存背包。
/// </summary>
public sealed class RoguelikeInventoryTransaction
{
    private readonly RoguelikeChapterRuntime runtime;
    private readonly IRoguelikeEquipmentCatalog equipment;
    private readonly IRoguelikeItemCatalog items;
    private bool closed;
    private bool failed;

    public RoguelikeInventoryTransaction(RoguelikeProgressSnapshot source, IRoguelikeEquipmentCatalog equipment,
        IRoguelikeItemCatalog items = null)
    {
        runtime = new RoguelikeChapterRuntime(source);
        this.equipment = equipment ?? throw new ArgumentNullException(nameof(equipment));
        this.items = items;
    }

    public RoguelikeInventoryView Read()
    {
        EnsureOpen();
        return new RoguelikeInventoryView(runtime.ExportSnapshot(), equipment, items);
    }

    /// <summary>获得角色的统一入口：首次建立档案；重复获得变为1个对应命之座，不自动激活。</summary>
    public void AddCharacter(int characterId, float maxHealth, float maxEnergy)
    {
        Mutate(() =>
        {
            if (!equipment.TryGetCharacterWeaponType(characterId, out int type) || type < 0 || type > 4)
                throw new InvalidOperationException("角色模板未配置：" + characterId);
            if (runtime.TryGetCharacterProgress(characterId, out _))
            {
                string itemId = RoguelikeItemIds.Constellation(characterId);
                if (items != null && (!items.TryGetItem(itemId, out var item) || item == null ||
                    item.Category != "Constellation" || item.ReferenceID != characterId ||
                    item.ItemType != RoguelikeItemType.Stack || !item.Stackable))
                    throw new InvalidOperationException("角色对应的命之座道具未正确配置：" + itemId);
                AddItems(new[] { new RoguelikeItemStackData { ItemId = itemId, Quantity = 1 } });
            }
            else runtime.AddCharacter(characterId, maxHealth, maxEnergy);
        });
    }

    public void SetParty(IReadOnlyList<int> characterIds) { Mutate(() => runtime.SetParty(characterIds)); }

    /// <summary>明确确认后才从他人卸下；目标原装备留在背包，不销毁也不自动交换。</summary>
    public void TransferWeapon(int characterId, string instanceId)
    {
        Mutate(() =>
        {
            int owner = Read().GetWeaponOwner(instanceId);
            if (owner != 0 && owner != characterId) runtime.UnequipWeapon(owner);
            EquipWeapon(characterId, instanceId);
        });
    }

    public void TransferArtifact(int characterId, string instanceId)
    {
        Mutate(() =>
        {
            var view = Read(); var artifact = view.FindArtifact(instanceId);
            if (artifact == null) throw new InvalidOperationException("找不到圣遗物实例：" + instanceId);
            int owner = view.GetArtifactOwner(instanceId);
            if (owner != 0 && owner != characterId) runtime.UnequipArtifact(owner, artifact.Artifact.Slot);
            runtime.EquipArtifact(characterId, instanceId);
        });
    }

    public void AddItems(IReadOnlyList<RoguelikeItemStackData> additions)
    {
        Mutate(() =>
        {
            var totals = SumQuantities(additions);
            foreach (var item in totals) RequireStackDefinition(item.Key);
            var view = Read();
            foreach (var item in totals)
                if ((long)view.GetItemCount(item.Key) + item.Value > int.MaxValue)
                    throw new InvalidOperationException("物品数量超过可存储范围：" + item.Key);
            foreach (var item in totals) runtime.AdjustItem(item.Key, item.Value);
        });
    }

    public void ConsumeItems(IReadOnlyList<RoguelikeItemStackData> costs)
    {
        Mutate(() =>
        {
            var totals = SumQuantities(costs);
            foreach (var item in totals) RequireStackDefinition(item.Key);
            var view = Read();
            // 重复 ID 合并后一次检查；任意材料不足都不扣除其他材料。
            foreach (var item in totals)
                if (view.GetItemCount(item.Key) < item.Value)
                    throw new InvalidOperationException("物品数量不足：" + item.Key);
            foreach (var item in totals) runtime.AdjustItem(item.Key, -item.Value);
        });
    }

    /// <summary>按现有武器模板创建 1 级、未突破、精炼 1 的独立实例。</summary>
    public string AddWeapon(int weaponId)
    {
        string instanceId = Guid.NewGuid().ToString("N");
        AddWeapon(instanceId, new WeaponLoadout { WeaponID = weaponId });
        return instanceId;
    }

    public void AddWeapon(string instanceId, WeaponLoadout weapon, int experience = 0)
    {
        Mutate(() =>
        {
            if (weapon == null) throw new ArgumentNullException(nameof(weapon));
            RequireWeaponDefinition(weapon.WeaponID);
            RequireEquipmentItem(RoguelikeItemIds.Weapon(weapon.WeaponID), RoguelikeItemType.Weapon);
            runtime.AddWeapon(instanceId, weapon, experience);
        });
    }

    public string AddArtifact(GeneratedArtifact artifact, int experience = 0)
    {
        string instanceId = Guid.NewGuid().ToString("N");
        AddArtifact(instanceId, artifact, experience);
        return instanceId;
    }

    public void AddArtifact(string instanceId, GeneratedArtifact artifact, int experience = 0)
    {
        AddArtifact(instanceId, 0, artifact, experience);
    }

    /// <summary>正式物品表接入后显式传入套装 ID，部位取生成结果的 Slot。</summary>
    public string AddArtifact(int artifactSetId, GeneratedArtifact artifact, int experience = 0)
    {
        string instanceId = Guid.NewGuid().ToString("N");
        AddArtifact(instanceId, artifactSetId, artifact, experience);
        return instanceId;
    }

    public void AddArtifact(string instanceId, int artifactSetId, GeneratedArtifact artifact, int experience = 0)
    {
        Mutate(() =>
        {
            if (artifact == null) throw new ArgumentNullException(nameof(artifact));
            if (artifactSetId != 0 || items != null)
                RequireEquipmentItem(RoguelikeItemIds.Artifact(artifactSetId, artifact.Slot), RoguelikeItemType.Artifact);
            runtime.AddArtifact(instanceId, artifact, experience, artifactSetId);
        });
    }

    public void EquipWeapon(int characterId, string instanceId)
    {
        Mutate(() =>
        {
            var weapon = Read().FindWeapon(instanceId);
            if (weapon == null) throw new InvalidOperationException("找不到武器实例：" + instanceId);
            var definition = RequireWeaponDefinition(weapon.Weapon.WeaponID);
            if (!equipment.TryGetCharacterWeaponType(characterId, out int weaponType) ||
                weaponType != definition.WeaponType)
                throw new InvalidOperationException("角色与武器类型不匹配，或角色武器类型未配置。");
            runtime.EquipWeapon(characterId, instanceId);
        });
    }

    public void UnequipWeapon(int characterId) { Mutate(() => runtime.UnequipWeapon(characterId)); }
    public void EquipArtifact(int characterId, string instanceId) { Mutate(() => runtime.EquipArtifact(characterId, instanceId)); }
    public void UnequipArtifact(int characterId, ArtifactSlot slot) { Mutate(() => runtime.UnequipArtifact(characterId, slot)); }
    public void RemoveWeapon(string instanceId) { Mutate(() => runtime.RemoveWeapon(instanceId)); }
    public void RemoveArtifact(string instanceId) { Mutate(() => runtime.RemoveArtifact(instanceId)); }

    /// <summary>只导出独立结果，不落盘；由对应业务步骤统一提交。服务的 Commit 会自动结束草稿。</summary>
    public RoguelikeProgressSnapshot ExportSnapshot()
    {
        EnsureOpen();
        return runtime.ExportSnapshot();
    }

    public void Close() { closed = true; }

    private WeaponAttributesData RequireWeaponDefinition(int weaponId)
    {
        if (!equipment.TryGetWeapon(weaponId, out var definition) || definition == null ||
            definition.WeaponID != weaponId || definition.WeaponType < 0 || definition.WeaponType > 4)
            throw new InvalidOperationException("武器模板不存在或武器类型未正确配置：" + weaponId);
        return definition;
    }

    private static Dictionary<string, int> SumQuantities(IReadOnlyList<RoguelikeItemStackData> entries)
    {
        if (entries == null) throw new ArgumentNullException(nameof(entries));
        var totals = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in entries)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.ItemId) || item.Quantity <= 0)
                throw new ArgumentException("每项物品必须包含非空 ID 和大于 0 的数量。", nameof(entries));
            totals.TryGetValue(item.ItemId, out int current);
            long total = (long)current + item.Quantity;
            if (total > int.MaxValue) throw new InvalidOperationException("同类物品合计数量超出范围：" + item.ItemId);
            totals[item.ItemId] = (int)total;
        }
        return totals;
    }

    private void RequireStackDefinition(string itemId)
    {
        // 不注入目录时保留原有框架行为；正式物品表模式不允许未知道具或将装备当数量保存。
        if (items == null) return;
        if (!items.TryGetItem(itemId, out var definition) || definition == null ||
            definition.ItemType != RoguelikeItemType.Stack || !definition.Stackable)
            throw new InvalidOperationException("此编号不是已配置的可堆叠道具：" + itemId);
    }

    private void RequireEquipmentItem(string itemId, RoguelikeItemType type)
    {
        if (items == null) return;
        if (!items.TryGetItem(itemId, out var definition) || definition == null ||
            definition.ItemType != type || definition.Stackable)
            throw new InvalidOperationException("装备物品未配置或数据类型错误：" + itemId);
    }

    private void Mutate(Action operation)
    {
        EnsureOpen();
        try { operation(); }
        catch { failed = true; throw; }
    }

    private void EnsureOpen()
    {
        if (closed) throw new InvalidOperationException("背包草稿已经结束，不能再次使用。");
        if (failed) throw new InvalidOperationException("背包草稿已有失败操作，必须重新开始，不能提交部分结果。");
    }
}
