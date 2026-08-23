using System;
using System.Collections.Generic;

public sealed class RoguelikeSaveValidator
{
    public const int CurrentSchemaVersion = 1;

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
        if (snapshot.Characters == null)
            return Invalid("角色列表不能为 null。");
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
            if (character.ConstellationLevel < 0 || character.ConstellationLevel > 6)
                return Invalid("角色命座必须在 0～6 之间。");
            if (character.SkillLevels == null || character.SkillLevels.Length != 4)
                return Invalid("SkillLevels 必须正好包含四项。");
            for (int skillIndex = 0; skillIndex < character.SkillLevels.Length; skillIndex++)
            {
                if (character.SkillLevels[skillIndex] < 1 || character.SkillLevels[skillIndex] > 15)
                    return Invalid("技能等级必须在 1～15 之间。");
            }
        }

        return RoguelikeSaveResult.Success();
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
