using System;
using System.Collections.Generic;
using System.IO;

public sealed class RoguelikeSaveRepository
{
    private readonly RoguelikeSavePaths paths;
    private readonly RoguelikeSaveValidator validator;
    private readonly IRoguelikeSaveFileSystem fileSystem;

    public RoguelikeSavePaths Paths { get { return paths; } }

    public RoguelikeSaveRepository(
        RoguelikeSavePaths paths,
        RoguelikeSaveValidator validator = null,
        IRoguelikeSaveFileSystem fileSystem = null)
    {
        this.paths = paths ?? throw new ArgumentNullException("paths");
        this.validator = validator ?? new RoguelikeSaveValidator();
        this.fileSystem = fileSystem ?? new SystemRoguelikeSaveFileSystem();
    }

    public bool HasActiveRunFiles()
    {
        return HasAnyVersion(paths.ActiveRunPath);
    }

    public bool HasChapterSlotFiles(int entryChapterId, int slotIndex)
    {
        return HasAnyVersion(paths.GetChapterSlotPath(entryChapterId, slotIndex));
    }

    public RoguelikeSaveResult<ActiveRunSaveData> LoadActiveRun()
    {
        return LoadWithBackup<ActiveRunSaveData>(paths.ActiveRunPath, validator.Validate);
    }

    public RoguelikeSaveResult<ChapterEntrySaveData> LoadChapterSlot(int entryChapterId, int slotIndex)
    {
        try
        {
            return LoadWithBackup<ChapterEntrySaveData>(paths.GetChapterSlotPath(entryChapterId, slotIndex), validator.Validate);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return RoguelikeSaveResult<ChapterEntrySaveData>.Failure(
                RoguelikeSaveErrorCode.ValidationFailed,
                exception.Message,
                exception);
        }
    }

    public RoguelikeSaveResult SaveActiveRun(ActiveRunSaveData save)
    {
        return SaveAtomically(paths.ActiveRunPath, save, validator.Validate);
    }

    public RoguelikeSaveResult SaveChapterSlot(ChapterEntrySaveData save)
    {
        if (save == null)
            return RoguelikeSaveResult.Failure(RoguelikeSaveErrorCode.ValidationFailed, "永久入口档为空。");
        try
        {
            return SaveAtomically(paths.GetChapterSlotPath(save.EntryChapterId, save.SlotIndex), save, validator.Validate);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return RoguelikeSaveResult.Failure(RoguelikeSaveErrorCode.ValidationFailed, exception.Message, exception);
        }
    }

    public RoguelikeSaveResult DeleteActiveRun()
    {
        try
        {
            DeleteIfPresent(paths.ActiveRunPath);
            DeleteIfPresent(RoguelikeSavePaths.GetBackupPath(paths.ActiveRunPath));
            DeleteIfPresent(RoguelikeSavePaths.GetTemporaryPath(paths.ActiveRunPath));
            return RoguelikeSaveResult.Success();
        }
        catch (Exception exception)
        {
            return RoguelikeSaveResult.Failure(
                RoguelikeSaveErrorCode.IoFailure,
                "删除临时档失败：" + exception.Message,
                exception);
        }
    }

    private bool HasAnyVersion(string formalPath)
    {
        return fileSystem.FileExists(formalPath) ||
               fileSystem.FileExists(RoguelikeSavePaths.GetBackupPath(formalPath));
    }

    private RoguelikeSaveResult<T> LoadWithBackup<T>(string formalPath, Func<T, RoguelikeSaveResult> validate)
        where T : class
    {
        string backupPath = RoguelikeSavePaths.GetBackupPath(formalPath);
        bool formalExists = fileSystem.FileExists(formalPath);
        bool backupExists = fileSystem.FileExists(backupPath);
        if (!formalExists && !backupExists)
            return RoguelikeSaveResult<T>.Failure(RoguelikeSaveErrorCode.NotFound, "存档不存在：" + formalPath);

        RoguelikeSaveResult<T> formalResult = formalExists
            ? ReadSingle(formalPath, validate)
            : RoguelikeSaveResult<T>.Failure(RoguelikeSaveErrorCode.NotFound, "正式档不存在。");
        if (formalResult.Succeeded)
            return formalResult;
        if (formalResult.ErrorCode == RoguelikeSaveErrorCode.UnsupportedSchema)
            return formalResult;

        RoguelikeSaveResult<T> backupResult = backupExists
            ? ReadSingle(backupPath, validate)
            : RoguelikeSaveResult<T>.Failure(RoguelikeSaveErrorCode.NotFound, "备份档不存在。");
        if (backupResult.Succeeded)
        {
            return RoguelikeSaveResult<T>.Failure(
                RoguelikeSaveErrorCode.BackupAvailable,
                "正式档无效，可由备份恢复。底层不会自动覆盖正式档。",
                data: backupResult.Data);
        }

        if (formalExists && backupExists)
        {
            return RoguelikeSaveResult<T>.Failure(
                RoguelikeSaveErrorCode.BothCopiesCorrupted,
                "正式档和备份档都无法读取，原文件已保留。");
        }
        return formalResult;
    }

    private RoguelikeSaveResult<T> ReadSingle<T>(string path, Func<T, RoguelikeSaveResult> validate)
        where T : class
    {
        try
        {
            string json = fileSystem.ReadAllText(path);
            T value = RoguelikeSaveJson.Deserialize<T>(json);
            if (value == null)
                return RoguelikeSaveResult<T>.Failure(RoguelikeSaveErrorCode.ValidationFailed, "JSON 内容为空或无法反序列化。");
            var validation = validate(value);
            if (!validation.Succeeded)
                return RoguelikeSaveResult<T>.Failure(validation.ErrorCode, validation.Message, validation.Exception);
            return RoguelikeSaveResult<T>.Success(value);
        }
        catch (Exception exception)
        {
            return RoguelikeSaveResult<T>.Failure(
                RoguelikeSaveErrorCode.ValidationFailed,
                "读取存档失败：" + exception.Message,
                exception);
        }
    }

    private RoguelikeSaveResult SaveAtomically<T>(string formalPath, T value, Func<T, RoguelikeSaveResult> validate)
        where T : class
    {
        var validation = validate(value);
        if (!validation.Succeeded)
            return validation;

        string temporaryPath = RoguelikeSavePaths.GetTemporaryPath(formalPath);
        string backupPath = RoguelikeSavePaths.GetBackupPath(formalPath);
        try
        {
            string directory = Path.GetDirectoryName(formalPath);
            fileSystem.CreateDirectory(directory);
            fileSystem.WriteAllText(temporaryPath, RoguelikeSaveJson.Serialize(value));

            var temporaryResult = ReadSingle(temporaryPath, validate);
            if (!temporaryResult.Succeeded)
                return RoguelikeSaveResult.Failure(temporaryResult.ErrorCode, "临时档回读校验失败：" + temporaryResult.Message);

            if (fileSystem.FileExists(formalPath))
                fileSystem.ReplaceFile(temporaryPath, formalPath, backupPath);
            else
                fileSystem.MoveFile(temporaryPath, formalPath);

            var committedResult = ReadSingle(formalPath, validate);
            if (!committedResult.Succeeded)
            {
                return RoguelikeSaveResult.Failure(
                    RoguelikeSaveErrorCode.BackupAvailable,
                    "正式档提交后校验失败，旧档备份已保留：" + committedResult.Message);
            }
            return RoguelikeSaveResult.Success();
        }
        catch (Exception exception)
        {
            return RoguelikeSaveResult.Failure(
                RoguelikeSaveErrorCode.IoFailure,
                "保存失败，旧正式档保持不变：" + exception.Message,
                exception);
        }
        finally
        {
            try { DeleteIfPresent(temporaryPath); }
            catch { /* 清理失败不覆盖主要保存结果，残留 tmp 下次会被重写。 */ }
        }
    }

    private void DeleteIfPresent(string path)
    {
        if (fileSystem.FileExists(path))
            fileSystem.DeleteFile(path);
    }
}
