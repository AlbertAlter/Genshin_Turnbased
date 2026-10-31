/// <summary>公共单位标记：禁锢仅作用于角色；眩晕仅由敌方给敌人施加；OffField支持双方。</summary>
public static class PublicControlStates
{
    public const string Imprisoned = "ST_Imprisoned";
    public const string OffField = "OffField";
    public const string EnemyStun = "ST_Stun_Enemy";
    public static bool CanApply(BattleEntity target, string id, BattleEntity caster = null)
    {
        if (id == EnemyStun)
            return target?.Type == BattleEntity.EntityType.Enemy && target.Side == BattleSide.Enemy
                && caster?.Type == BattleEntity.EntityType.Enemy && caster.Side == BattleSide.Enemy;
        return id != Imprisoned || target?.Type == BattleEntity.EntityType.Character;
    }

    internal static void Register()
    {
        BattleSpecialRules.RegisterEnemyActionBlocker(enemy => enemy?.HasStatus(EnemyStun) == true);
        BattleSpecialRules.RegisterStatusLifecycle(EnemyStun, (status, host) => {
            var battle = BattleManager.Instance;
            if (battle != null && status.AddInPhase == 6 && (int)battle.CurrentPhase == 6)
                status.EarliestTimerTurn = battle.TurnCount + 1;
        }, null);
    }
}
