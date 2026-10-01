using System;
using System.Collections.Generic;

public sealed class RoguelikeSaveValidator
{
    public const int CurrentSchemaVersion = 2;

    public RoguelikeSaveResult Validate(ChapterEntrySaveData save)
    {
        if (save == null)
            return Invalid("永久入口档为空。");

        var metadataResult = ValidateMetadata(save.Metadata);
        if (!metadataResult.Succeeded) return metadataResult;
        if (save.EntryChapterId < 2)
            return Invalid("永久入口章节必须大于等于 2。第一章不能拥有永久入口档。");
        if (save.SlotIndex < 1 || save.SlotIndex > 3)
            return Invalid("永久入口槽位只能是 1、2、3。");
        if (save.SourceCompletedChapterId != save.EntryChapterId - 1)
            return Invalid("来源完成章节必须等于入口章节减 1。");

        var snapshotResult = Validate(save.Snapshot);
        if (!snapshotResult.Succeeded) return snapshotResult;
        if (save.Snapshot.CurrentChapterId != save.SourceCompletedChapterId)
            return Invalid("永久入口档快照必须记录来源章节的通关状态。");
        return RoguelikeSaveResult.Success();
    }

    public RoguelikeSaveResult Validate(ActiveRunSaveData save)
    {
        if (save == null)
            return Invalid("临时档为空。");

        var metadataResult = ValidateMetadata(save.Metadata);
        if (!metadataResult.Succeeded) return metadataResult;
        if (string.IsNullOrWhiteSpace(save.RunId))
            return Invalid("临时档 RunId 不能为空。");
        if (!Enum.IsDefined(typeof(RoguelikeRunStartType), save.StartType))
            return Invalid("临时档 StartType 无效。");
        if (!Enum.IsDefined(typeof(RoguelikeResumePoint), save.ResumePoint))
            return Invalid("临时档 ResumePoint 无效。");
        if (!Enum.IsDefined(typeof(RoguelikeSaveReason), save.LastSaveReason))
            return Invalid("临时档 LastSaveReason 无效。");
        if (save.CurrentChapterId <= 0)
            return Invalid("当前章节必须大于 0。");

        if (save.StartType == RoguelikeRunStartType.NewGame)
        {
            if (save.SourceChapterId != 0 || save.SourceSlotIndex != 0)
                return Invalid("新游戏临时档不能带有永久槽来源。");
        }
        else
        {
            if (save.SourceChapterId < 2)
                return Invalid("永久槽开局的来源章节必须大于等于 2。");
            if (save.SourceSlotIndex < 1 || save.SourceSlotIndex > 3)
                return Invalid("永久槽开局的来源槽位只能是 1、2、3。");
        }

        if (save.ResumePoint == RoguelikeResumePoint.BeforeBattle && string.IsNullOrWhiteSpace(save.PendingBattleId))
            return Invalid("战前恢复点必须包含 PendingBattleId。");

        var snapshotResult = Validate(save.Snapshot);
        if (!snapshotResult.Succeeded) return snapshotResult;
        if (save.Snapshot.CurrentChapterId != save.CurrentChapterId)
            return Invalid("临时档章节与快照章节不一致。");
        return RoguelikeSaveResult.Success();
    }

    public RoguelikeSaveResult Validate(RoguelikeProgressSnapshot snapshot)
    {
        if (snapshot == null)
            return Invalid("进度快照为空。");
        if (snapshot.CurrentChapterId <= 0)
            return Invalid("快照当前章节必须大于 0。");
        if (snapshot.CurrentStageIndex < 0)
            return Invalid("当前阶段索引不能小于 0。");
        if (snapshot.RandomStep < 0)
            return Invalid("随机步数不能小于 0。");
        if (snapshot.Characters == null || snapshot.CharacterVitals == null || snapshot.PartyCharacterIds == null)
            return Invalid("角色、生命能量和队伍列表不能为 null。");
        if (snapshot.Inventory == null || snapshot.Inventory.Items == null ||
            snapshot.Inventory.Weapons == null || snapshot.Inventory.Artifacts == null)
            return Invalid("背包及其物品、武器和圣遗物列表不能为 null。");
        if (snapshot.ClearedNodeIds == null || snapshot.SelectedRouteNodeIds == null)
            return Invalid("节点进度列表不能为 null。");
        if (snapshot.PendingRewardData != null && string.IsNullOrWhiteSpace(snapshot.PendingRewardData.RewardId))
            return Invalid("待选奖励存在时 RewardId 不能为空。");

        var characterIds = new HashSet<int>();
        for (int i = 0; i < snapshot.Characters.Count; i++)
        {
            var character = snapshot.Characters[i];
            if (character == null)
                return Invalid("角色列表中不能包含空项。");
            if (character.CharacterID <= 0)
                return Invalid("CharacterID 必须大于 0。");
            if (!characterIds.Add(character.CharacterID))
                return Invalid("CharacterID 不能重复：" + character.CharacterID + "。");
            if (character.Level < 1 || character.Level > 90)
                return Invalid("角色等级必须在 1～90 之间。");
            if (character.Experience < 0)
                return Invalid("角色经验不能小于 0。");
            if (character.Ascension < 0 || character.Ascension > 6)
                return Invalid("角色突破等级必须在 0～6 之间。");
            if (character.ConstellationLevel < 0 || character.ConstellationLevel > 6)
                return Invalid("角色命座必须在 0～6 之间。");
            if (character.SkillLevels == null || character.SkillLevels.Length != 4)
                return Invalid("SkillLevels 必须正好包含四项。");
            for (int skillIndex = 0; skillIndex < character.SkillLevels.Length; skillIndex++)
            {
                if (character.SkillLevels[skillIndex] < 1 || character.SkillLevels[skillIndex] > 15)
                    return Invalid("技能等级必须在 1～15 之间。");
            }
            if (character.EquippedArtifacts == null)
                return Invalid("角色圣遗物装备列表不能为 null。");
        }

        var vitalCharacterIds = new HashSet<int>();
        for (int i = 0; i < snapshot.CharacterVitals.Count; i++)
        {
            RoguelikeCharacterVitalData vital = snapshot.CharacterVitals[i];
            if (vital == null)
                return Invalid("生命能量列表中不能包含空项。");
            if (!characterIds.Contains(vital.CharacterID))
                return Invalid("生命能量引用了未拥有角色：" + vital.CharacterID + "。");
            if (!vitalCharacterIds.Add(vital.CharacterID))
                return Invalid("同一角色不能存在多份生命能量数据：" + vital.CharacterID + "。");
            if (!IsFinite(vital.MaxHealth) || vital.MaxHealth <= 0f ||
                !IsFinite(vital.CurrentHealth) || vital.CurrentHealth < 0f || vital.CurrentHealth > vital.MaxHealth)
                return Invalid("角色生命值必须处于 0 到最大生命值之间。");
            if (!IsFinite(vital.MaxEnergy) || vital.MaxEnergy < 0f ||
                !IsFinite(vital.CurrentEnergy) || vital.CurrentEnergy < 0f || vital.CurrentEnergy > vital.MaxEnergy)
                return Invalid("角色能量必须处于 0 到最大能量之间。");
        }
        if (vitalCharacterIds.Count != characterIds.Count)
            return Invalid("每个已拥有角色都必须有且仅有一份生命能量数据。");

        if (snapshot.PartyCharacterIds.Count > 4)
            return Invalid("出战队伍最多包含 4 名角色。");
        var partyIds = new HashSet<int>();
        for (int i = 0; i < snapshot.PartyCharacterIds.Count; i++)
        {
            int characterId = snapshot.PartyCharacterIds[i];
            if (!characterIds.Contains(characterId))
                return Invalid("队伍成员必须是已拥有角色：" + characterId + "。");
            if (!partyIds.Add(characterId))
                return Invalid("队伍成员不能重复：" + characterId + "。");
        }

        var itemIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < snapshot.Inventory.Items.Count; i++)
        {
            RoguelikeItemStackData item = snapshot.Inventory.Items[i];
            if (item == null || string.IsNullOrWhiteSpace(item.ItemId))
                return Invalid("普通物品堆叠必须包含有效 ItemId。");
            if (!itemIds.Add(item.ItemId))
                return Invalid("普通物品堆叠不能重复：" + item.ItemId + "。");
            if (item.Quantity <= 0)
                return Invalid("普通物品数量必须大于 0。");
        }

        var weaponIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < snapshot.Inventory.Weapons.Count; i++)
        {
            RoguelikeWeaponInstanceData instance = snapshot.Inventory.Weapons[i];
            if (instance == null || string.IsNullOrWhiteSpace(instance.InstanceId) || instance.Weapon == null)
                return Invalid("武器实例必须包含实例 ID 和生成结果。");
            if (!weaponIds.Add(instance.InstanceId))
                return Invalid("武器实例 ID 不能重复：" + instance.InstanceId + "。");
            if (instance.Experience < 0 || instance.Weapon.WeaponID <= 0 ||
                instance.Weapon.Level < 1 || instance.Weapon.Level > 90 ||
                instance.Weapon.Ascension < 0 || instance.Weapon.Ascension > 6 ||
                instance.Weapon.Refinement < 1 || instance.Weapon.Refinement > 5)
                return Invalid("武器实例成长数据无效：" + instance.InstanceId + "。");
        }

        var artifactIds = new HashSet<string>(StringComparer.Ordinal);
        var artifactSlots = new Dictionary<string, ArtifactSlot>(StringComparer.Ordinal);
        for (int i = 0; i < snapshot.Inventory.Artifacts.Count; i++)
        {
            RoguelikeArtifactInstanceData instance = snapshot.Inventory.Artifacts[i];
            if (instance == null || string.IsNullOrWhiteSpace(instance.InstanceId) || instance.Artifact == null)
                return Invalid("圣遗物实例必须包含实例 ID 和生成结果。");
            if (!artifactIds.Add(instance.InstanceId))
                return Invalid("圣遗物实例 ID 不能重复：" + instance.InstanceId + "。");
            if (!IsValidArtifact(instance) || instance.Experience < 0)
                return Invalid("圣遗物实例数据无效：" + instance.InstanceId + "。");
            artifactSlots.Add(instance.InstanceId, instance.Artifact.Slot);
        }

        var equippedWeaponIds = new HashSet<string>(StringComparer.Ordinal);
        var equippedArtifactIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < snapshot.Characters.Count; i++)
        {
            RoguelikeCharacterProgressData character = snapshot.Characters[i];
            if (!string.IsNullOrWhiteSpace(character.EquippedWeaponInstanceId))
            {
                if (!weaponIds.Contains(character.EquippedWeaponInstanceId))
                    return Invalid("角色装备引用了不存在的武器实例：" + character.EquippedWeaponInstanceId + "。");
                if (!equippedWeaponIds.Add(character.EquippedWeaponInstanceId))
                    return Invalid("同一武器实例不能同时装备给多个角色：" + character.EquippedWeaponInstanceId + "。");
            }

            var occupiedSlots = new HashSet<ArtifactSlot>();
            for (int equipmentIndex = 0; equipmentIndex < character.EquippedArtifacts.Count; equipmentIndex++)
            {
                RoguelikeArtifactEquipmentData equipment = character.EquippedArtifacts[equipmentIndex];
                if (equipment == null || string.IsNullOrWhiteSpace(equipment.ArtifactInstanceId))
                    return Invalid("角色圣遗物装备引用不能为空。");
                if (!artifactSlots.TryGetValue(equipment.ArtifactInstanceId, out ArtifactSlot actualSlot))
                    return Invalid("角色装备引用了不存在的圣遗物实例：" + equipment.ArtifactInstanceId + "。");
                if (equipment.Slot != actualSlot)
                    return Invalid("圣遗物装备槽位与实例部位不一致：" + equipment.ArtifactInstanceId + "。");
                if (!occupiedSlots.Add(equipment.Slot))
                    return Invalid("同一角色的同一圣遗物槽位只能装备一件实例。");
                if (!equippedArtifactIds.Add(equipment.ArtifactInstanceId))
                    return Invalid("同一圣遗物实例不能同时装备给多个角色：" + equipment.ArtifactInstanceId + "。");
            }
        }

        return RoguelikeSaveResult.Success();
    }

    private static bool IsValidArtifact(RoguelikeArtifactInstanceData instance)
    {
        GeneratedArtifact artifact = instance.Artifact;
        if (!Enum.IsDefined(typeof(ArtifactSlot), artifact.Slot) || artifact.Star <= 0 ||
            artifact.MaxLevel < 0 || artifact.Level < 0 || artifact.Level > artifact.MaxLevel ||
            string.IsNullOrWhiteSpace(artifact.MainAttributeType) || !IsFinite(artifact.MainAttributeValue) ||
            artifact.SecondaryAttributes == null)
            return false;

        for (int i = 0; i < artifact.SecondaryAttributes.Count; i++)
        {
            ArtifactSecondaryAttribute secondary = artifact.SecondaryAttributes[i];
            if (secondary == null || string.IsNullOrWhiteSpace(secondary.AttributeType) ||
                !IsFinite(secondary.Value) || secondary.RollCount < 1)
                return false;
        }
        return true;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private RoguelikeSaveResult ValidateMetadata(SaveMetadata metadata)
    {
        if (metadata == null)
            return Invalid("存档元数据为空。");
        if (metadata.SchemaVersion > CurrentSchemaVersion)
            return RoguelikeSaveResult.Failure(
                RoguelikeSaveErrorCode.UnsupportedSchema,
                "存档版本 " + metadata.SchemaVersion + " 高于当前支持版本 " + CurrentSchemaVersion + "。");
        if (metadata.SchemaVersion != CurrentSchemaVersion)
            return Invalid("不支持的存档版本：" + metadata.SchemaVersion + "。");
        if (string.IsNullOrWhiteSpace(metadata.SaveId))
            return Invalid("SaveId 不能为空。");
        if (!DateTimeOffset.TryParse(metadata.CreatedAt, out var createdAt))
            return Invalid("CreatedAt 不是有效的本地 ISO 8601 时间。");
        if (!DateTimeOffset.TryParse(metadata.UpdatedAt, out var updatedAt))
            return Invalid("UpdatedAt 不是有效的本地 ISO 8601 时间。");
        if (updatedAt < createdAt)
            return Invalid("UpdatedAt 不能早于 CreatedAt。");
        return RoguelikeSaveResult.Success();
    }

    private static RoguelikeSaveResult Invalid(string message)
    {
        return RoguelikeSaveResult.Failure(RoguelikeSaveErrorCode.ValidationFailed, message);
    }
}
