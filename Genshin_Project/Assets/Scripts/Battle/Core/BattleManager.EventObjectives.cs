using System;
using System.Linq;

public partial class BattleManager
{
    public int MaxRounds { get; private set; }
    public bool EndedByRoundLimit { get; private set; }
    public int RemainingRounds => MaxRounds <= 0 || EndedByRoundLimit ? 0 : Math.Max(0, MaxRounds - TurnCount + 1);
    public int EventEnemyKills => eventObjective?.TotalKills ?? 0;
    public int EventEscapedEnemies => eventObjective?.Escaped ?? 0;
    public int EventCharacterKills(int characterId) => eventObjective?.Kills(characterId) ?? 0;
    private BattleEventObjective eventObjective;
    public void ConfigureEventObjectives(int maxRound, string winCondition, string loseCondition)
    {
        if (IsBattleRunning || maxRound < 0) throw new InvalidOperationException("须在开战前配置有效事件条件。");
        MaxRounds = maxRound; EndedByRoundLimit = false;
        eventObjective = maxRound == 0 && string.IsNullOrWhiteSpace(winCondition) && string.IsNullOrWhiteSpace(loseCondition)
            ? null : new BattleEventObjective(winCondition, loseCondition, maxRound);
    }
    private int LivingAllyCount => _allies.Count(a => a?.Entity != null && !a.Entity.IsDead && !a.Entity.HasWithdrawn);
    internal void RecordEventObjective(BattleDeclaration declaration)
    {
        if (eventObjective == null || IsBattleOver) return;
        if (declaration.Data is DamageEventData damage && damage.CausedDeath && damage.Subject?.Side == BattleSide.Enemy)
        {
            var source = damage.Source;
            eventObjective.RecordKill(source?.HasSourceEntity == true && source.SourceSide == BattleSide.Ally
                ? source.SourceEntityID : 0, damage.Subject);
        }
        else if (declaration.Data is WithdrawnEventData withdrawn && withdrawn.Subject?.Side == BattleSide.Enemy)
            eventObjective.RecordEscape(withdrawn.Subject);
    }
    private bool TryFinishRoundLimit()
    {
        if (MaxRounds <= 0 || TurnCount < MaxRounds || CurrentPhase != TurnPhase.EnemyPostTurn || IsBattleOver) return false;
        EndedByRoundLimit = true;
        bool cleared = AllEnemiesDefeated;
        CompleteBattle((eventObjective?.Win(cleared, LivingAllyCount, TurnCount, true) ?? cleared)
            && !(eventObjective?.Lose(cleared, LivingAllyCount, TurnCount, true) ?? LivingAllyCount == 0));
        return true;
    }
}
