using System;
using System.Collections.Generic;

/// <summary>UI-only save header. No scene references or second persistent copy of player data.</summary>
public sealed class RoguelikeSaveSummary
{
    public string SaveId, RunId, UpdatedAt, GameVersion, CurrentNodeId;
    public int ChapterId, StageIndex, EntryChapterId, SlotIndex;
    public RoguelikeResumePoint ResumePoint;
    public int CharacterCount, WeaponCount, ArtifactCount, ItemStackCount;
    public List<int> PartyCharacterIds;
    public RoguelikeSaveErrorCode ReadError;
    public string Message;

    internal static RoguelikeSaveSummary Create(SaveMetadata metadata, RoguelikeProgressSnapshot snapshot)
    {
        return new RoguelikeSaveSummary
        {
            SaveId = metadata.SaveId, UpdatedAt = metadata.UpdatedAt, GameVersion = metadata.GameVersion,
            ChapterId = snapshot.CurrentChapterId, StageIndex = snapshot.CurrentStageIndex, CurrentNodeId = snapshot.CurrentNodeId,
            CharacterCount = snapshot.Characters.Count, WeaponCount = snapshot.Inventory.Weapons.Count,
            ArtifactCount = snapshot.Inventory.Artifacts.Count, ItemStackCount = snapshot.Inventory.Items.Count,
            PartyCharacterIds = new List<int>(snapshot.PartyCharacterIds),
        };
    }
}
