using System;
using System.Collections.Generic;

[Serializable]
public class RoguelikeProgressSnapshot
{
    public List<RoguelikeCharacterProgressData> Characters = new List<RoguelikeCharacterProgressData>();
    public List<RoguelikeCharacterVitalData> CharacterVitals = new List<RoguelikeCharacterVitalData>();
    public List<int> PartyCharacterIds = new List<int>();
    public RoguelikeInventoryData Inventory = new RoguelikeInventoryData();
    public int CurrentChapterId;
    public int CurrentStageIndex;
    public string CurrentNodeId;
    public List<string> ClearedNodeIds = new List<string>();
    public List<string> SelectedRouteNodeIds = new List<string>();
    public PendingRewardData PendingRewardData;
    public int RunSeed;
    public int RandomStep;
}
