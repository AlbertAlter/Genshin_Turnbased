using System;
using System.Collections.Generic;

public sealed partial class RoguelikeSaveRepository
{
    private RoguelikeSaveResult<PlayerAppearanceData> ReadAppearanceFile()
    {
        var read = LoadWithBackup<PlayerAppearanceData>(paths.AppearancePath, PlayerAppearanceRules.Validate);
        return read.ErrorCode == RoguelikeSaveErrorCode.NotFound
            ? RoguelikeSaveResult<PlayerAppearanceData>.Success(new PlayerAppearanceData()) : read;
    }
    public RoguelikeSaveResult<PlayerAppearanceData> LoadAppearance()
    {
        using var transaction = BeginTransaction();
        // Recover committed awards after a crash between victory-save and cosmetic-save. Invalid run backups are not auto-replayed.
        var active = LoadWithBackup<ActiveRunSaveData>(paths.ActiveRunPath, validator.Validate);
        if (active.Succeeded)
        {
            var applied = ApplyAppearanceAwards(active.Data.Snapshot.AppearanceAwards);
            if (!applied.Succeeded) return RoguelikeSaveResult<PlayerAppearanceData>.Failure(applied.ErrorCode, applied.Message, applied.Exception);
        }
        return ReadAppearanceFile();
    }
    internal RoguelikeSaveResult ApplyAppearanceAwards(IReadOnlyList<AppearanceBossAward> awards)
    {
        if (awards == null || awards.Count == 0) return RoguelikeSaveResult.Success();
        var read = ReadAppearanceFile(); if (!read.Succeeded) return read;
        try
        {
            bool changed = false;
            foreach (var award in awards) changed |= PlayerAppearanceRules.Apply(read.Data, award);
            return changed ? SaveAtomically(paths.AppearancePath, read.Data, PlayerAppearanceRules.Validate) : RoguelikeSaveResult.Success();
        }
        catch (Exception error) { return RoguelikeSaveResult.Failure(RoguelikeSaveErrorCode.ValidationFailed, error.Message, error); }
    }
    public RoguelikeSaveResult RecordAppearanceBossVictory(AppearanceBossAward award)
    {
        using var transaction = BeginTransaction();
        return ApplyAppearanceAwards(new[] { award });
    }
    public RoguelikeSaveResult SaveSelectedCardBack(string id)
    {
        using var transaction = BeginTransaction();
        var read = LoadAppearance(); if (!read.Succeeded) return read;
        if (!string.IsNullOrEmpty(id) && !read.Data.UnlockedCardBackIds.Contains(id))
            return RoguelikeSaveResult.Failure(RoguelikeSaveErrorCode.InvalidOperation, "卡背尚未解锁。");
        read.Data.SelectedCardBackId = id ?? "";
        return SaveAtomically(paths.AppearancePath, read.Data, PlayerAppearanceRules.Validate);
    }
    public RoguelikeSaveResult UnlockCardBack(string id)
    {
        using var transaction = BeginTransaction();
        var read = LoadAppearance(); if (!read.Succeeded) return read;
        if (string.IsNullOrWhiteSpace(id) || read.Data.UnlockedCardBackIds.Contains(id) || read.Data.AvailableCardBackChoices < 1)
            return RoguelikeSaveResult.Failure(RoguelikeSaveErrorCode.InvalidOperation, "卡背已解锁或没有可用的通关选择次数。");
        read.Data.UnlockedCardBackIds.Add(id); read.Data.SpentCardBackChoices++;
        read.Data.SelectedCardBackId = id;
        return SaveAtomically(paths.AppearancePath, read.Data, PlayerAppearanceRules.Validate);
    }
    public RoguelikeSaveResult RestoreAppearanceBackup(bool confirmed)
        => RestoreBackup<PlayerAppearanceData>(paths.AppearancePath, PlayerAppearanceRules.Validate, confirmed);
}
