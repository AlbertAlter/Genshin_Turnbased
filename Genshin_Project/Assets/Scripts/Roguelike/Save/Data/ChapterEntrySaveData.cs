using System;

[Serializable]
public class ChapterEntrySaveData
{
    public SaveMetadata Metadata;
    public int EntryChapterId;
    public int SlotIndex;
    public int SourceCompletedChapterId;
    public RoguelikeProgressSnapshot Snapshot;
}
