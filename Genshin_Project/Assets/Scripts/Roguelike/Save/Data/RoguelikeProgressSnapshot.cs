using System;
using System.Collections.Generic;

[Serializable]
public class RoguelikeProgressSnapshot
{
    public List<CharacterProfile> Characters = new List<CharacterProfile>();
    public int CurrentChapterId;
    public int CurrentStageIndex;
    public string CurrentNodeId;
    public List<string> ClearedNodeIds = new List<string>();
    public List<string> SelectedRouteNodeIds = new List<string>();
    public PendingRewardData PendingRewardData;
    public int RunSeed;
    public int RandomStep;
}
