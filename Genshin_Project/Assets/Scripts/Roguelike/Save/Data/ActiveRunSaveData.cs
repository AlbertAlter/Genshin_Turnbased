using System;

[Serializable]
public class ActiveRunSaveData
{
    public SaveMetadata Metadata;
    public string RunId;
    public RoguelikeRunStartType StartType;
    public int SourceChapterId;
    public int SourceSlotIndex;
    public int CurrentChapterId;
    public RoguelikeResumePoint ResumePoint;
    public string PendingBattleId;
    public RoguelikeSaveReason LastSaveReason;
    public RoguelikeProgressSnapshot Snapshot;
}
