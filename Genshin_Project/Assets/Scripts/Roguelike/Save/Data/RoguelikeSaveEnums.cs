public enum RoguelikeRunStartType
{
    NewGame = 0,
    ChapterSlot = 1,
}

public enum RoguelikeResumePoint
{
    ChapterRoute = 0,
    BeforeBattle = 1,
    BattleVictory = 2,
    RewardSelection = 3,
    Event = 4,
    ChapterCompleted = 5,
}

public enum RoguelikeSaveReason
{
    NewGame = 0,
    ChapterSlotLoaded = 1,
    BattleVictory = 2,
    RewardChosen = 3,
    GachaCompleted = 4,
    CharacterUpgraded = 5,
    EquipmentChanged = 6,
    RouteChosen = 7,
    EventCompleted = 8,
    BeforeBattle = 9,
    ManualExit = 10,
    PendingRewardGenerated = 11,
}
