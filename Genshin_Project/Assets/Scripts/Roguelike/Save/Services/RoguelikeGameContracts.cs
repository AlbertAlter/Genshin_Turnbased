using System;
using System.Collections.Generic;

/// <summary>Award contents must be resolved before committing. Equipment carries actual generated data, never a request to reroll.</summary>
[Serializable]
public sealed class RoguelikeAcquisition
{
    public List<int> Characters = new List<int>();
    public List<RoguelikeItemStackData> Items = new List<RoguelikeItemStackData>();
    public List<RoguelikeWeaponInstanceData> Weapons = new List<RoguelikeWeaponInstanceData>();
    public List<RoguelikeArtifactInstanceData> Artifacts = new List<RoguelikeArtifactInstanceData>();
}

public interface IRoguelikeCharacterAcquisitionCatalog
{
    bool TryGetInitialLimits(int characterId, out float maxHealth, out float maxEnergy);
}

public sealed class DataManagerRoguelikeCharacterAcquisitionCatalog : IRoguelikeCharacterAcquisitionCatalog
{
    private readonly DataManager data;
    public DataManagerRoguelikeCharacterAcquisitionCatalog(DataManager data) { this.data = data ?? throw new ArgumentNullException(nameof(data)); }
    public bool TryGetInitialLimits(int characterId, out float health, out float energy)
    {
        health = energy = 0;
        if (!data.IsLoaded || !data.CharacterAttributesDict.TryGetValue(characterId, out var character) || character == null ||
            !data.GrowthCurveDict.TryGetValue(character.GrowthCurveID, out var curve) || !curve.TryGetValue(1, out float growth)) return false;
        health = character.BaseHP * growth; energy = character.MaxEnergy;
        return health > 0 && !float.IsNaN(health) && !float.IsInfinity(health) && energy >= 0;
    }
}

/// <summary>UI-facing operations: get current StepId from Continue; only refresh UI after Succeeded or Changed.</summary>
public interface IRoguelikeGameService
{
    event Action Changed;
    RoguelikeSaveResult<RoguelikeFlowContext> Continue();
    RoguelikeSaveResult<RoguelikeInventoryView> ReadInventory();
    RoguelikeSaveResult<RoguelikeSaveSummary> ReadActiveSummary();
    RoguelikeSaveResult<List<RoguelikeChapterSlotInfo>> ListChapterSlots(int chapter);
    RoguelikeSaveResult StartNewGame(RoguelikeProgressSnapshot initial);
    RoguelikeSaveResult StartFromChapterSlot(int chapter, int slot);
    RoguelikeSaveResult SaveCurrentProgress();
    RoguelikeSaveResult Abandon(string expectedRunId, bool confirmed);
    RoguelikeSaveResult RestoreActiveBackup(bool confirmed);
    RoguelikeSaveResult RestoreChapterBackup(int chapter, int slot, bool confirmed);
    RoguelikeSaveResult Acquire(string stepId, RoguelikeAcquisition acquisition);
    RoguelikeSaveResult AcquireCharacter(string stepId, int characterId);
    RoguelikeSaveResult AcquireWeapon(string stepId, int weaponId);
    RoguelikeSaveResult AcquireArtifact(string stepId, int setId, GeneratedArtifact artifact);
    RoguelikeSaveResult AcquireItem(string stepId, string itemId, int quantity);
    RoguelikeSaveResult ConsumeItems(string stepId, IReadOnlyList<RoguelikeItemStackData> costs);
    RoguelikeSaveResult EquipWeapon(string stepId, int characterId, string instanceId, bool transferConfirmed = false);
    RoguelikeSaveResult EquipArtifact(string stepId, int characterId, string instanceId, bool transferConfirmed = false);
    RoguelikeSaveResult UnequipWeapon(string stepId, int characterId);
    RoguelikeSaveResult UnequipArtifact(string stepId, int characterId, ArtifactSlot slot);
    RoguelikeSaveResult SetParty(string stepId, IReadOnlyList<int> characterIds);
    RoguelikeSaveResult<RoguelikeFlowContext> RetryCurrentBattle();
    RoguelikeSaveResult EnterNode(string stepId, RoguelikeNodeSelection node);
    RoguelikeSaveResult BeginBattle(string stepId, string battleId);
    RoguelikeSaveResult RecordBattleVictory(string stepId);
    RoguelikeSaveResult SavePendingReward(string stepId, PendingRewardData pending);
    RoguelikeSaveResult CompleteNode(string stepId, RoguelikeAcquisition outcome);
    RoguelikeSaveResult CompleteEvent(string stepId, RoguelikeAcquisition outcome);
    RoguelikeSaveResult CompleteChapter(string stepId);
    RoguelikeSaveResult SaveCompletedChapterToSlot(string stepId, int slot, bool overwriteConfirmed = false);
    RoguelikeSaveResult CommitAcquisition(string stepId, IReadOnlyList<RoguelikeItemStackData> costs,
        RoguelikeAcquisition outcome, RoguelikeSaveReason reason = RoguelikeSaveReason.InventoryChanged);
}
