/// <summary>击飞的行动限制。施加、计时和移除走普通状态流程。</summary>
public static class AirborneStatusHandler
{
    public const string EnemyStatusID = "ST_Fall_Enemy";

    internal static void Register()
        => BattleSpecialRules.RegisterEnemyActionBlocker(BlocksEnemyAction);

    public static bool BlocksEnemyAction(BattleEntity entity)
        => entity != null && entity.HasStatus(EnemyStatusID);
}
