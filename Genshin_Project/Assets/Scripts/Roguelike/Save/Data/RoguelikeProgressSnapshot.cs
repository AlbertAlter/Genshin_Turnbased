using System;
using System.Collections.Generic;

[Serializable]
public class RoguelikeProgressSnapshot
{
    public List<RoguelikeCharacterProgressData> Characters = new List<RoguelikeCharacterProgressData>();
    public List<RoguelikeCharacterVitalData> CharacterVitals = new List<RoguelikeCharacterVitalData>();
    public List<int> PartyCharacterIds = new List<int>();
    public int ActivePartyPreset = 1;
    public List<RoguelikePartyPreset> PartyPresets;
    public RoguelikeInventoryData Inventory = new RoguelikeInventoryData();
    public int CurrentChapterId;
    public int CurrentStageIndex;
    public string CurrentNodeId;
    public List<string> ClearedNodeIds = new List<string>();
    // 由路线进度写入。旧档留空，由养成入口保留档案已经拥有的突破阶段。
    public int? UnlockedAscensions;
    public List<string> SelectedRouteNodeIds = new List<string>();
    // 本章已抽走的内容。旧档未记录时从空池历史开始。
    public List<string> UsedNodeContentIds;
    public PendingRewardData PendingRewardData;
    public RoguelikePendingNodeReward PendingNodeReward;
    public int RunSeed;
    public int RandomStep;
    // 旧存档未记录祈愿时从零计数。
    public RoguelikeWishProgress WishProgress;
    public Dictionary<string, int> ShopPurchaseCounts;
    public Dictionary<int, int> CompletedChapterNodeCounts;
    // 可空以兼容旧快照；新流程由 RoguelikeFlowService 维护。
    public RoguelikeFlowData Flow;
    // Delivery journal only, not the global cosmetic state. Entry-slot replay cannot roll back cosmetic levels.
    public List<AppearanceBossAward> AppearanceAwards = new List<AppearanceBossAward>();
}

[Serializable]
public sealed class RoguelikePartyPreset
{
    public int Number;
    public List<int> Members;
}
