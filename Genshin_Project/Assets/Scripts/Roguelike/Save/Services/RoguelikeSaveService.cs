using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

public sealed class RoguelikeSaveService
{
    internal IDisposable BeginTransaction() => repository.BeginTransaction();
    internal RoguelikeSaveRepository Repository => repository;
    private readonly RoguelikeSaveRepository repository;
    private readonly RoguelikeSaveValidator validator;
    private readonly string gameVersion;
    private readonly Func<DateTimeOffset> nowProvider;
    private readonly Func<string> idProvider;
    private readonly RoguelikeWishRules wishRules;
    private readonly IBattleRandomSource wishRandom;

    public RoguelikeSaveService(
        RoguelikeSaveRepository repository,
        string gameVersion = null,
        Func<DateTimeOffset> nowProvider = null,
        Func<string> idProvider = null,
        RoguelikeWishRules wishRules = null,
        IBattleRandomSource wishRandom = null)
    {
        this.repository = repository ?? throw new ArgumentNullException("repository");
        validator = new RoguelikeSaveValidator();
        this.gameVersion = gameVersion ?? Application.version ?? string.Empty;
        this.nowProvider = nowProvider ?? (() => DateTimeOffset.Now);
        this.idProvider = idProvider ?? (() => Guid.NewGuid().ToString("N"));
        this.wishRules = wishRules;
        this.wishRandom = wishRandom;
    }

    public bool HasActiveRun()
    {
        return repository.HasActiveRunFiles();
    }

    public RoguelikeSaveResult<ActiveRunSaveData> LoadActiveRun()
    {
        return repository.LoadActiveRun();
    }

    public RoguelikeSaveResult<List<RoguelikeChapterSlotInfo>> ListChapterSlots(int entryChapterId)
    {
        using var transaction = BeginTransaction();
        try
        {
            RoguelikeSavePaths.EnsureValidEntryChapter(entryChapterId);
            var slots = new List<RoguelikeChapterSlotInfo>(3);
            for (int slotIndex = 1; slotIndex <= 3; slotIndex++)
            {
                bool exists = repository.HasChapterSlotFiles(entryChapterId, slotIndex);
                var summary = exists ? GetChapterSlotSummary(entryChapterId, slotIndex) : null;
                slots.Add(new RoguelikeChapterSlotInfo
                {
                    EntryChapterId = entryChapterId,
                    SlotIndex = slotIndex,
                    Exists = exists,
                    RequiresOverwriteConfirmation = exists,
                    Summary = summary?.Data,
                    ReadError = summary?.ErrorCode ?? RoguelikeSaveErrorCode.None,
                    Message = summary?.Message ?? string.Empty,
                });
            }
            return RoguelikeSaveResult<List<RoguelikeChapterSlotInfo>>.Success(slots);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return RoguelikeSaveResult<List<RoguelikeChapterSlotInfo>>.Failure(
                RoguelikeSaveErrorCode.ValidationFailed,
                exception.Message,
                exception);
        }
    }

    public RoguelikeSaveResult<ChapterEntrySaveData> LoadChapterSlot(int entryChapterId, int slotIndex)
    {
        return repository.LoadChapterSlot(entryChapterId, slotIndex);
    }

    public RoguelikeSaveResult<RoguelikeChapterSlotInfo> CanOverwriteChapterSlot(int entryChapterId, int slotIndex)
    {
        try
        {
            RoguelikeSavePaths.EnsureValidEntryChapter(entryChapterId);
            RoguelikeSavePaths.EnsureValidSlot(slotIndex);
            bool exists = repository.HasChapterSlotFiles(entryChapterId, slotIndex);
            return RoguelikeSaveResult<RoguelikeChapterSlotInfo>.Success(new RoguelikeChapterSlotInfo
            {
                EntryChapterId = entryChapterId,
                SlotIndex = slotIndex,
                Exists = exists,
                RequiresOverwriteConfirmation = exists,
            });
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return RoguelikeSaveResult<RoguelikeChapterSlotInfo>.Failure(
                RoguelikeSaveErrorCode.ValidationFailed,
                exception.Message,
                exception);
        }
    }

    // Replacing a confirmed run writes through the normal atomic repository operation.
    // Never delete the old run before the new snapshot has passed validation and been saved.
    private RoguelikeSaveResult ValidateRunReplacement(string expectedRunId)
    {
        if (!HasActiveRun()) return string.IsNullOrEmpty(expectedRunId) ? RoguelikeSaveResult.Success()
            : RoguelikeSaveResult.Failure(RoguelikeSaveErrorCode.InvalidOperation, "存档已变化，请重新确认。");
        if (string.IsNullOrEmpty(expectedRunId)) return RoguelikeSaveResult.Failure(
            RoguelikeSaveErrorCode.ActiveRunAlreadyExists, "已有临时档，需要明确确认后再开始。");
        var current = LoadActiveRun();
        if (!current.Succeeded) return current;
        return current.Data.RunId == expectedRunId ? RoguelikeSaveResult.Success()
            : RoguelikeSaveResult.Failure(RoguelikeSaveErrorCode.InvalidOperation, "存档已变化，请重新确认。");
    }

    public RoguelikeSaveResult StartNewGame(RoguelikeProgressSnapshot initialSnapshot, string replaceRunId = null)
    {
        using var transaction = BeginTransaction();
        var replace = ValidateRunReplacement(replaceRunId);
        if (!replace.Succeeded) return replace;
        if (initialSnapshot == null || initialSnapshot.CurrentChapterId != 1)
            return RoguelikeSaveResult.Failure(
                RoguelikeSaveErrorCode.ValidationFailed,
                "新游戏必须从第一章开始。");

        var snapshot = RoguelikeSaveJson.DeepClone(initialSnapshot);
        snapshot.WishProgress = new RoguelikeWishProgress();
        snapshot.ShopPurchaseCounts = null;
        snapshot.CompletedChapterNodeCounts = null;
        snapshot.UsedNodeContentIds = null;
        var snapshotValidation = validator.Validate(snapshot);
        if (!snapshotValidation.Succeeded) return snapshotValidation;
        if (wishRules != null)
        {
            var planned = repository.GetOrCreateWishPlanning(wishRules, wishRandom);
            if (!planned.Succeeded) return planned;
            snapshot.WishProgress.FivePlanSeed = planned.Data.Seed;
            snapshot.WishProgress.CharacterFivePlan = planned.Data.Character;
            snapshot.WishProgress.WeaponFivePlan = planned.Data.Weapon;
        }

        var activeRun = new ActiveRunSaveData
        {
            Metadata = CreateMetadata(),
            RunId = idProvider(),
            StartType = RoguelikeRunStartType.NewGame,
            SourceChapterId = 0,
            SourceSlotIndex = 0,
            CurrentChapterId = 1,
            ResumePoint = RoguelikeResumePoint.ChapterRoute,
            PendingBattleId = string.Empty,
            LastSaveReason = RoguelikeSaveReason.NewGame,
            Snapshot = snapshot,
        };
        return repository.SaveActiveRun(activeRun);
    }

    public RoguelikeSaveResult StartFromChapterSlot(int entryChapterId, int slotIndex, string replaceRunId = null)
    {
        using var transaction = BeginTransaction();
        var replace = ValidateRunReplacement(replaceRunId);
        if (!replace.Succeeded) return replace;

        var entryResult = repository.LoadChapterSlot(entryChapterId, slotIndex);
        if (!entryResult.Succeeded)
            return CopyFailure(entryResult);

        var snapshot = RoguelikeSaveJson.DeepClone(entryResult.Data.Snapshot);
        snapshot.CurrentChapterId = entryChapterId;
        snapshot.UsedNodeContentIds = null;
        if (snapshot.Flow != null)
        {
            // 新流程入口沿用存档的养成、背包、祈愿历史和保底，并建立本章的路线步骤。
            snapshot.Flow = new RoguelikeFlowData
            {
                Step = RoguelikeFlowStep.Route,
                StepId = Guid.NewGuid().ToString("N"),
            };
            snapshot.CurrentNodeId = string.Empty;
            snapshot.CurrentStageIndex = 0;
            snapshot.ClearedNodeIds.Clear();
            snapshot.SelectedRouteNodeIds.Clear();
        }
        for (int i = 0; i < snapshot.CharacterVitals.Count; i++)
        {
            snapshot.CharacterVitals[i].CurrentHealth = snapshot.CharacterVitals[i].MaxHealth;
            snapshot.CharacterVitals[i].CurrentEnergy = snapshot.CharacterVitals[i].MaxEnergy;
        }

        var activeRun = new ActiveRunSaveData
        {
            Metadata = CreateMetadata(),
            RunId = idProvider(),
            StartType = RoguelikeRunStartType.ChapterSlot,
            SourceChapterId = entryChapterId,
            SourceSlotIndex = slotIndex,
            CurrentChapterId = entryChapterId,
            ResumePoint = RoguelikeResumePoint.ChapterRoute,
            PendingBattleId = string.Empty,
            LastSaveReason = RoguelikeSaveReason.ChapterSlotLoaded,
            Snapshot = snapshot,
        };
        return repository.SaveActiveRun(activeRun);
    }

    public RoguelikeSaveResult SaveActiveRun(
        RoguelikeProgressSnapshot snapshot,
        RoguelikeResumePoint resumePoint,
        RoguelikeSaveReason saveReason)
    {
        using var transaction = BeginTransaction();
        var activeResult = repository.LoadActiveRun();
        if (!activeResult.Succeeded)
            return ActiveRunFailure(activeResult);
        return SaveActiveRun(activeResult.Data, snapshot, resumePoint, saveReason);
    }

    // The caller must hold the save transaction and supply the active run read within it.
    internal RoguelikeSaveResult SaveActiveRun(ActiveRunSaveData activeRun, RoguelikeProgressSnapshot snapshot,
        RoguelikeResumePoint resumePoint, RoguelikeSaveReason saveReason)
    {
        if (snapshot == null)
            return RoguelikeSaveResult.Failure(RoguelikeSaveErrorCode.ValidationFailed, "进度快照不能为空。");
        if (snapshot.CurrentChapterId != activeRun.CurrentChapterId)
            return RoguelikeSaveResult.Failure(
                RoguelikeSaveErrorCode.InvalidOperation,
                "普通临时保存不能直接切换当前章节。");
        if (activeRun.ResumePoint == RoguelikeResumePoint.BeforeBattle &&
            (resumePoint != RoguelikeResumePoint.BeforeBattle || saveReason != RoguelikeSaveReason.BeforeBattle))
        {
            return RoguelikeSaveResult.Failure(
                RoguelikeSaveErrorCode.InvalidOperation,
                "战斗过程中不能用普通临时保存覆盖战前恢复点。");
        }

        activeRun.Snapshot = RoguelikeSaveJson.DeepClone(snapshot);
        activeRun.CurrentChapterId = activeRun.Snapshot.CurrentChapterId;
        activeRun.ResumePoint = resumePoint;
        activeRun.LastSaveReason = saveReason;
        if (resumePoint != RoguelikeResumePoint.BeforeBattle && resumePoint != RoguelikeResumePoint.BattleVictory)
            activeRun.PendingBattleId = string.Empty;
        TouchMetadata(activeRun.Metadata);
        return repository.SaveActiveRun(activeRun);
    }

    public RoguelikeSaveResult SaveBeforeBattle(RoguelikeProgressSnapshot snapshot, string battleId)
    {
        using var transaction = BeginTransaction();
        if (string.IsNullOrWhiteSpace(battleId))
            return RoguelikeSaveResult.Failure(RoguelikeSaveErrorCode.ValidationFailed, "battleId 不能为空。");
        var activeResult = repository.LoadActiveRun();
        if (!activeResult.Succeeded) return ActiveRunFailure(activeResult);

        var activeRun = activeResult.Data;
        activeRun.Snapshot = RoguelikeSaveJson.DeepClone(snapshot);
        if (activeRun.Snapshot == null)
            return RoguelikeSaveResult.Failure(RoguelikeSaveErrorCode.ValidationFailed, "进度快照不能为空。");
        if (activeRun.Snapshot.CurrentChapterId != activeRun.CurrentChapterId)
            return RoguelikeSaveResult.Failure(
                RoguelikeSaveErrorCode.InvalidOperation,
                "战前快照章节与当前临时档章节不一致。");
        activeRun.CurrentChapterId = activeRun.Snapshot.CurrentChapterId;
        activeRun.ResumePoint = RoguelikeResumePoint.BeforeBattle;
        activeRun.PendingBattleId = battleId;
        activeRun.LastSaveReason = RoguelikeSaveReason.BeforeBattle;
        TouchMetadata(activeRun.Metadata);
        return repository.SaveActiveRun(activeRun);
    }

    public RoguelikeSaveResult SaveAfterBattleVictory(RoguelikeProgressSnapshot snapshot, string battleId)
    {
        using var transaction = BeginTransaction();
        if (string.IsNullOrWhiteSpace(battleId))
            return RoguelikeSaveResult.Failure(RoguelikeSaveErrorCode.ValidationFailed, "battleId 不能为空。");
        var activeResult = repository.LoadActiveRun();
        if (!activeResult.Succeeded) return ActiveRunFailure(activeResult);
        var activeRun = activeResult.Data;
        if (activeRun.ResumePoint != RoguelikeResumePoint.BeforeBattle || activeRun.PendingBattleId != battleId)
            return RoguelikeSaveResult.Failure(
                RoguelikeSaveErrorCode.InvalidOperation,
                "战斗胜利必须对应当前已保存的战前节点。");
        if (snapshot == null)
            return RoguelikeSaveResult.Failure(RoguelikeSaveErrorCode.ValidationFailed, "进度快照不能为空。");
        if (snapshot.CurrentChapterId != activeRun.CurrentChapterId)
            return RoguelikeSaveResult.Failure(
                RoguelikeSaveErrorCode.InvalidOperation,
                "战斗胜利快照章节与当前临时档章节不一致。");

        activeRun.Snapshot = RoguelikeSaveJson.DeepClone(snapshot);
        activeRun.CurrentChapterId = activeRun.Snapshot.CurrentChapterId;
        activeRun.ResumePoint = RoguelikeResumePoint.BattleVictory;
        activeRun.PendingBattleId = battleId;
        activeRun.LastSaveReason = RoguelikeSaveReason.BattleVictory;
        TouchMetadata(activeRun.Metadata);
        return repository.SaveActiveRun(activeRun);
    }

    public RoguelikeSaveResult SaveAfterProgressAction(
        RoguelikeProgressSnapshot snapshot,
        RoguelikeSaveReason saveReason)
    {
        using var transaction = BeginTransaction();
        if (!IsProgressActionReason(saveReason))
            return RoguelikeSaveResult.Failure(
                RoguelikeSaveErrorCode.InvalidOperation,
                "该 SaveReason 不是行动后实时存档原因：" + saveReason + "。");

        var activeResult = repository.LoadActiveRun();
        if (!activeResult.Succeeded) return ActiveRunFailure(activeResult);
        if (activeResult.Data.ResumePoint == RoguelikeResumePoint.BeforeBattle)
            return RoguelikeSaveResult.Failure(
                RoguelikeSaveErrorCode.InvalidOperation,
                "战斗过程中不能写入进度行动结果。");

        RoguelikeResumePoint resumePoint = activeResult.Data.ResumePoint;
        if (saveReason == RoguelikeSaveReason.RouteChosen)
            resumePoint = RoguelikeResumePoint.ChapterRoute;
        else if (saveReason == RoguelikeSaveReason.EventCompleted)
            resumePoint = RoguelikeResumePoint.Event;
        else if (saveReason == RoguelikeSaveReason.RewardChosen || saveReason == RoguelikeSaveReason.GachaCompleted)
            resumePoint = RoguelikeResumePoint.RewardSelection;

        return SaveActiveRun(snapshot, resumePoint, saveReason);
    }

    public RoguelikeSaveResult SavePendingReward(
        RoguelikeProgressSnapshot snapshot,
        PendingRewardData pendingReward)
    {
        using var transaction = BeginTransaction();
        if (pendingReward == null || string.IsNullOrWhiteSpace(pendingReward.RewardId))
            return RoguelikeSaveResult.Failure(
                RoguelikeSaveErrorCode.ValidationFailed,
                "待选奖励存在时必须提供最小 RewardId。");
        if (snapshot == null)
            return RoguelikeSaveResult.Failure(RoguelikeSaveErrorCode.ValidationFailed, "进度快照不能为空。");

        var activeResult = repository.LoadActiveRun();
        if (!activeResult.Succeeded) return ActiveRunFailure(activeResult);
        if (activeResult.Data.ResumePoint == RoguelikeResumePoint.BeforeBattle)
            return RoguelikeSaveResult.Failure(
                RoguelikeSaveErrorCode.InvalidOperation,
                "战斗过程中不能生成待选奖励并覆盖战前恢复点。");

        var clone = RoguelikeSaveJson.DeepClone(snapshot);
        clone.PendingRewardData = RoguelikeSaveJson.DeepClone(pendingReward);
        return SaveActiveRun(
            clone,
            RoguelikeResumePoint.RewardSelection,
            RoguelikeSaveReason.PendingRewardGenerated);
    }

    public RoguelikeSaveResult<ActiveRunSaveData> RetryCurrentBattle()
    {
        var activeResult = repository.LoadActiveRun();
        if (!activeResult.Succeeded) return activeResult;
        if (activeResult.Data.ResumePoint != RoguelikeResumePoint.BeforeBattle ||
            string.IsNullOrWhiteSpace(activeResult.Data.PendingBattleId))
        {
            return RoguelikeSaveResult<ActiveRunSaveData>.Failure(
                RoguelikeSaveErrorCode.InvalidOperation,
                "当前临时档不在战前恢复点。");
        }
        return RoguelikeSaveResult<ActiveRunSaveData>.Success(RoguelikeSaveJson.DeepClone(activeResult.Data));
    }

    public RoguelikeSaveResult<ActiveRunSaveData> ExitBattleToMenu()
    {
        var activeResult = repository.LoadActiveRun();
        if (!activeResult.Succeeded) return activeResult;
        if (activeResult.Data.ResumePoint != RoguelikeResumePoint.BeforeBattle)
        {
            return RoguelikeSaveResult<ActiveRunSaveData>.Failure(
                RoguelikeSaveErrorCode.InvalidOperation,
                "只有处于战前恢复点的流程才能按战斗退出规则返回菜单。");
        }
        return RoguelikeSaveResult<ActiveRunSaveData>.Success(RoguelikeSaveJson.DeepClone(activeResult.Data));
    }

    public RoguelikeSaveResult AbandonActiveRun()
    {
        using var transaction = BeginTransaction();
        if (!HasActiveRun())
            return RoguelikeSaveResult.Failure(RoguelikeSaveErrorCode.NotFound, "没有可放弃的临时档。");
        return repository.DeleteActiveRun();
    }

    public RoguelikeSaveResult CompleteChapterAndSaveToNextSlot(
        int completedChapterId,
        int targetSlotIndex,
        RoguelikeProgressSnapshot completedSnapshot)
    {
        return CompleteChapterAndSaveToNextSlot(completedChapterId, targetSlotIndex, completedSnapshot, false);
    }

    public RoguelikeSaveResult CompleteChapterAndSaveToNextSlot(
        int completedChapterId,
        int targetSlotIndex,
        RoguelikeProgressSnapshot completedSnapshot,
        bool overwriteConfirmed)
    {
        using var transaction = BeginTransaction();
        if (completedChapterId <= 0 || completedSnapshot == null || completedSnapshot.CurrentChapterId != completedChapterId)
            return RoguelikeSaveResult.Failure(
                RoguelikeSaveErrorCode.ValidationFailed,
                "通关快照必须对应 completedChapterId。");
        if (completedSnapshot.PendingRewardData != null || completedSnapshot.PendingNodeReward != null)
            return RoguelikeSaveResult.Failure(
                RoguelikeSaveErrorCode.InvalidOperation,
                "章节全部奖励结算完成后才能写入下一章永久入口档。");
        try
        {
            RoguelikeSavePaths.EnsureValidSlot(targetSlotIndex);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return RoguelikeSaveResult.Failure(RoguelikeSaveErrorCode.ValidationFailed, exception.Message, exception);
        }

        var activeResult = repository.LoadActiveRun();
        if (!activeResult.Succeeded) return ActiveRunFailure(activeResult);
        if (activeResult.Data.CurrentChapterId != completedChapterId)
            return RoguelikeSaveResult.Failure(
                RoguelikeSaveErrorCode.InvalidOperation,
                "当前临时档章节与通关章节不一致。");

        var markCompletedResult = SaveActiveRun(
            completedSnapshot,
            RoguelikeResumePoint.ChapterCompleted,
            activeResult.Data.LastSaveReason);
        if (!markCompletedResult.Succeeded)
            return markCompletedResult;

        int targetEntryChapterId = completedChapterId + 1;
        if (repository.HasChapterSlotFiles(targetEntryChapterId, targetSlotIndex) && !overwriteConfirmed)
        {
            return RoguelikeSaveResult.Failure(
                RoguelikeSaveErrorCode.OverwriteConfirmationRequired,
                "目标永久入口槽已存在，需要 UI 二次确认后才能覆盖。");
        }

        var chapterEntry = new ChapterEntrySaveData
        {
            Metadata = CreateMetadata(),
            EntryChapterId = targetEntryChapterId,
            SlotIndex = targetSlotIndex,
            SourceCompletedChapterId = completedChapterId,
            Snapshot = RoguelikeSaveJson.DeepClone(completedSnapshot),
        };
        var saveResult = repository.SaveChapterSlot(chapterEntry);
        if (!saveResult.Succeeded)
            return saveResult;

        var verifyResult = repository.LoadChapterSlot(targetEntryChapterId, targetSlotIndex);
        if (!verifyResult.Succeeded || verifyResult.Data.Metadata.SaveId != chapterEntry.Metadata.SaveId)
        {
            return RoguelikeSaveResult.Failure(
                verifyResult.Succeeded ? RoguelikeSaveErrorCode.ValidationFailed : verifyResult.ErrorCode,
                "永久入口档写入后重新读取校验失败，临时档已保留。");
        }

        return repository.DeleteActiveRun();
    }

    public RoguelikeSaveResult<RoguelikeSaveSummary> GetActiveRunSummary()
    {
        var loaded = repository.LoadActiveRun();
        if (loaded.Data == null) return RoguelikeSaveResult<RoguelikeSaveSummary>.Failure(loaded.ErrorCode, loaded.Message, loaded.Exception);
        var value = RoguelikeSaveSummary.Create(loaded.Data.Metadata, loaded.Data.Snapshot);
        value.RunId = loaded.Data.RunId; value.ResumePoint = loaded.Data.ResumePoint;
        value.ReadError = loaded.ErrorCode; value.Message = loaded.Message;
        return loaded.Succeeded ? RoguelikeSaveResult<RoguelikeSaveSummary>.Success(value)
            : RoguelikeSaveResult<RoguelikeSaveSummary>.Failure(loaded.ErrorCode, loaded.Message, loaded.Exception, value);
    }

    public RoguelikeSaveResult<RoguelikeSaveSummary> GetChapterSlotSummary(int chapter, int slot)
    {
        var loaded = repository.LoadChapterSlot(chapter, slot);
        if (loaded.Data == null) return RoguelikeSaveResult<RoguelikeSaveSummary>.Failure(loaded.ErrorCode, loaded.Message, loaded.Exception);
        var value = RoguelikeSaveSummary.Create(loaded.Data.Metadata, loaded.Data.Snapshot);
        value.EntryChapterId = chapter; value.SlotIndex = slot; value.ResumePoint = RoguelikeResumePoint.ChapterCompleted;
        value.ReadError = loaded.ErrorCode; value.Message = loaded.Message;
        return loaded.Succeeded ? RoguelikeSaveResult<RoguelikeSaveSummary>.Success(value)
            : RoguelikeSaveResult<RoguelikeSaveSummary>.Failure(loaded.ErrorCode, loaded.Message, loaded.Exception, value);
    }

    public RoguelikeSaveResult RestoreActiveRunBackup(bool confirmed) => repository.RestoreActiveRunBackup(confirmed);
    public RoguelikeSaveResult RestoreChapterSlotBackup(int chapter, int slot, bool confirmed)
        => repository.RestoreChapterSlotBackup(chapter, slot, confirmed);

    /// <summary>退出前可调用。战中只保留现有战前档，绝不从战斗实体反向采集数据。</summary>
    public RoguelikeSaveResult SaveCurrentProgress()
    {
        using var transaction = BeginTransaction();
        var loaded = repository.LoadActiveRun();
        if (!loaded.Succeeded) return CopyFailure(loaded);
        if (loaded.Data.ResumePoint == RoguelikeResumePoint.BeforeBattle)
            return RoguelikeSaveResult.Success("已保留本场战前恢复点。");
        return SaveActiveRun(loaded.Data.Snapshot, loaded.Data.ResumePoint, RoguelikeSaveReason.ManualExit);
    }

    private SaveMetadata CreateMetadata()
    {
        string timestamp = nowProvider().ToString("O", CultureInfo.InvariantCulture);
        return new SaveMetadata
        {
            SchemaVersion = RoguelikeSaveValidator.CurrentSchemaVersion,
            SaveId = idProvider(),
            CreatedAt = timestamp,
            UpdatedAt = timestamp,
            GameVersion = gameVersion,
        };
    }

    private void TouchMetadata(SaveMetadata metadata)
    {
        metadata.UpdatedAt = nowProvider().ToString("O", CultureInfo.InvariantCulture);
        metadata.GameVersion = gameVersion;
    }

    private static bool IsProgressActionReason(RoguelikeSaveReason reason)
    {
        return reason == RoguelikeSaveReason.RewardChosen ||
               reason == RoguelikeSaveReason.GachaCompleted ||
               reason == RoguelikeSaveReason.CharacterUpgraded ||
               reason == RoguelikeSaveReason.EquipmentChanged ||
               reason == RoguelikeSaveReason.InventoryChanged ||
               reason == RoguelikeSaveReason.ShopPurchased ||
               reason == RoguelikeSaveReason.RouteChosen ||
               reason == RoguelikeSaveReason.EventCompleted;
    }

    private static RoguelikeSaveResult ActiveRunFailure(RoguelikeSaveResult<ActiveRunSaveData> result)
    {
        return RoguelikeSaveResult.Failure(
            result.ErrorCode == RoguelikeSaveErrorCode.NotFound
                ? RoguelikeSaveErrorCode.ActiveRunRequired
                : result.ErrorCode,
            result.Message,
            result.Exception);
    }

    private static RoguelikeSaveResult CopyFailure<T>(RoguelikeSaveResult<T> result)
    {
        return RoguelikeSaveResult.Failure(result.ErrorCode, result.Message, result.Exception);
    }
}
