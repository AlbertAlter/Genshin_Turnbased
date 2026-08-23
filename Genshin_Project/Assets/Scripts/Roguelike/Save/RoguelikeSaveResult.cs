using System;

public enum RoguelikeSaveErrorCode
{
    None = 0,
    NotFound,
    ValidationFailed,
    UnsupportedSchema,
    BackupAvailable,
    BothCopiesCorrupted,
    ActiveRunAlreadyExists,
    ActiveRunRequired,
    InvalidOperation,
    OverwriteConfirmationRequired,
    IoFailure,
}

public class RoguelikeSaveResult
{
    public bool Succeeded { get; protected set; }
    public RoguelikeSaveErrorCode ErrorCode { get; protected set; }
    public string Message { get; protected set; }
    public Exception Exception { get; protected set; }

    public static RoguelikeSaveResult Success(string message = "")
    {
        return new RoguelikeSaveResult
        {
            Succeeded = true,
            ErrorCode = RoguelikeSaveErrorCode.None,
            Message = message ?? string.Empty,
        };
    }

    public static RoguelikeSaveResult Failure(
        RoguelikeSaveErrorCode errorCode,
        string message,
        Exception exception = null)
    {
        return new RoguelikeSaveResult
        {
            Succeeded = false,
            ErrorCode = errorCode,
            Message = message ?? string.Empty,
            Exception = exception,
        };
    }
}

public sealed class RoguelikeSaveResult<T> : RoguelikeSaveResult
{
    public T Data { get; private set; }

    public static RoguelikeSaveResult<T> Success(T data, string message = "")
    {
        return new RoguelikeSaveResult<T>
        {
            Succeeded = true,
            ErrorCode = RoguelikeSaveErrorCode.None,
            Message = message ?? string.Empty,
            Data = data,
        };
    }

    public static RoguelikeSaveResult<T> Failure(
        RoguelikeSaveErrorCode errorCode,
        string message,
        Exception exception = null,
        T data = default(T))
    {
        return new RoguelikeSaveResult<T>
        {
            Succeeded = false,
            ErrorCode = errorCode,
            Message = message ?? string.Empty,
            Exception = exception,
            Data = data,
        };
    }
}

public sealed class RoguelikeChapterSlotInfo
{
    public int EntryChapterId { get; set; }
    public int SlotIndex { get; set; }
    public bool Exists { get; set; }
    public bool RequiresOverwriteConfirmation { get; set; }
}
