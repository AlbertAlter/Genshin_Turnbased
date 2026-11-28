using System;
using System.Collections;
using System.Collections.Generic;

/// <summary>管理一次敌方回合的共享行动池，并逐个等待分配的行动结清。</summary>
internal sealed class EnemyTurnRunner
{
    private EnemyTurnActionPool _activePool;

    internal bool TryGrantActions(int delta)
    {
        if (_activePool == null) return false;
        _activePool.QueueActionChange(delta);
        LogManager.Log(LogCategory.Enemy,
            $"敌方共享行动次数登记变动={delta}，待结算合计={_activePool.PendingActionDelta}");
        return true;
    }

    internal IEnumerator Execute(IReadOnlyList<EnemyBattleController> enemies, Func<bool> canContinue, Func<bool> waitingForArrival = null)
    {
        foreach (var enemy in enemies)
            if (enemy != null) enemy.BeginEnemyTurn();

        var pool = new EnemyTurnActionPool(enemies);
        _activePool = pool;
        try
        {
            LogManager.Log(LogCategory.Enemy, $"敌方本回合可分配行动次数={pool.RemainingActions}");
            while (_activePool == pool && canContinue())
            {
                while (BattlePause.IsPaused && _activePool == pool && canContinue()) yield return null;
                while (waitingForArrival?.Invoke() == true && _activePool == pool && canContinue()) yield return null;
                if (_activePool != pool || !canContinue() || !pool.TryTakeNext(out var actor)) break;
                LogManager.Log(LogCategory.Enemy,
                    $"敌方行动分配：{actor.EnemyID} 位置={actor.Entity.SlotPosition}，剩余次数={pool.RemainingActions}");
                var action = BattleExecution.Run(() => actor.TakeEnemyActionAsync(pool.LastSelectionIsLastGuaranteedAction));
                yield return BattleExecution.Wait(action);
                if (action.IsCanceled) yield break;
            }
        }
        finally
        {
            if (_activePool == pool) _activePool = null;
        }
    }

    internal void Clear() => _activePool = null;
}
