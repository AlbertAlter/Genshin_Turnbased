using System;
using System.Collections.Generic;

/// <summary>
/// 每轮开始先固定整轮名单，再逐个执行；新增共享次数、恢复资格均从下一轮参与分配。
/// 控制/死亡扣除原贡献一次，解除控制不退还贡献；离场保留贡献但本池不重入。
/// 新增敌人由下一次建池纳入。无可用技能仍消耗一次分配机会。
/// </summary>
public sealed class EnemyTurnActionPool
{
    private sealed class Entry
    {
        public EnemyBattleController Enemy;
        public int RemainingContribution;
        public bool CanRejoin;
    }

    private sealed class Allocation
    {
        public Entry Entry;
        public bool Guaranteed;
        public bool Cancelled;
    }

    private readonly List<Entry> _entries = new List<Entry>();
    private readonly List<Entry> _unallocated = new List<Entry>();
    private readonly List<Entry> _candidates = new List<Entry>();
    private readonly List<Allocation> _round = new List<Allocation>();
    private int _roundIndex;
    private long _remaining;
    private long _pendingActionDelta;

    // 包含本轮已预分配但尚未执行的机会；预分配不扣次数，实际取出才扣。
    public long RemainingActions => _remaining;
    public long PendingActionDelta => _pendingActionDelta;
    public bool LastSelectionIsLastGuaranteedAction { get; private set; }

    public EnemyTurnActionPool(IEnumerable<EnemyBattleController> enemies)
    {
        var seen = new HashSet<EnemyBattleController>();
        foreach (var enemy in enemies)
        {
            if (enemy == null || enemy.Entity == null || !enemy.Entity.IsAlive || !seen.Add(enemy)) continue;
            int contribution = enemy.CanContributeActions ? enemy.ActionContribution : 0;
            _remaining += contribution;
            // 保留受控单位的登记，解除控制后可以参加后续轮次；离场不享受此规则。
            _entries.Add(new Entry {
                Enemy = enemy, RemainingContribution = contribution, CanRejoin = !enemy.Entity.IsOffField
            });
        }
    }

    /// <summary>记录有符号变动，下一轮分配前求和结算；不影响本轮名单和余额。</summary>
    public void QueueActionChange(int delta)
    {
        _pendingActionDelta = checked(_pendingActionDelta + delta);
    }

    public bool TryTakeNext(out EnemyBattleController enemy)
    {
        enemy = null;
        LastSelectionIsLastGuaranteedAction = false;
        RefreshEligibility();
        while (true)
        {
            // 死亡/控制扣光余额时，取消旧轮尚未执行的机会，再结算下一轮变动。
            if (_remaining == 0) _roundIndex = _round.Count;
            if (_roundIndex >= _round.Count && !BuildRound()) return false;
            var allocation = _round[_roundIndex++];
            // 已排入的单位死亡/受控/离场时跳过，不把空位当场补给其他单位。
            if (!CanExecute(allocation)) continue;
            _remaining--;
            enemy = allocation.Entry.Enemy;
            int pending = 0, aheadOrTied = 0;
            for (int i = _roundIndex; i < _round.Count; i++)
                if (CanExecute(_round[i])) pending++;
            foreach (var entry in _entries)
                if (CanAllocate(entry) && entry.Enemy.Entity.Threat >= enemy.Entity.Threat) aheadOrTied++;
            // 只根据当前预算、剩余名单和资格预测；尚未发生的加次数/控制变化不能预知。
            LastSelectionIsLastGuaranteedAction = allocation.Guaranteed
                && checked(_remaining + _pendingActionDelta) < (long)pending + aheadOrTied;
            return true;
        }
    }

    private static bool CanAllocate(Entry entry) => entry.CanRejoin
        && entry.Enemy != null && entry.Enemy.CanReceiveOrdinaryAction;

    private static bool CanExecute(Allocation allocation) => !allocation.Cancelled && CanAllocate(allocation.Entry)
        && (allocation.Guaranteed || allocation.Entry.Enemy.CanReceiveExtraAction);

    private void RefreshEligibility()
    {
        foreach (var entry in _entries)
        {
            var enemy = entry.Enemy;
            if (enemy == null || enemy.Entity == null || !enemy.Entity.IsAlive || enemy.Entity.IsOffField)
                entry.CanRejoin = false;
            if (entry.RemainingContribution == 0 || (enemy != null && enemy.CanContributeActions)) continue;
            int removed = entry.RemainingContribution;
            entry.RemainingContribution = 0;
            // 已执行不回滚，余额最低为0；不留下负债去抵消后来新增的共享次数。
            _remaining = Math.Max(0L, _remaining - removed);
            LogManager.Log(LogCategory.Enemy, $"敌方单位失去贡献资格，扣除原贡献={removed}，剩余次数={_remaining}");
        }
        // 已撤销的本轮机会不能随随后解除控制而复活，只能参加下一轮。
        for (int i = _roundIndex; i < _round.Count; i++)
            if (!CanExecute(_round[i])) _round[i].Cancelled = true;
    }

    private bool BuildRound()
    {
        long delta = _pendingActionDelta;
        _remaining = Math.Max(0L, checked(_remaining + delta));
        _pendingActionDelta = 0;
        if (delta != 0)
            LogManager.Log(LogCategory.Enemy, $"敌方轮次行动次数结算：变动={delta}，剩余次数={_remaining}");
        _round.Clear();
        _roundIndex = 0;
        _unallocated.Clear();
        foreach (var entry in _entries)
            if (CanAllocate(entry)) _unallocated.Add(entry);
        long available = _remaining;
        while (available > 0 && _unallocated.Count > 0)
        {
            _candidates.Clear();
            int highestThreat = int.MinValue;
            foreach (var entry in _unallocated)
            {
                int threat = entry.Enemy.Entity.Threat;
                if (threat < highestThreat) continue;
                if (threat > highestThreat) { _candidates.Clear(); highestThreat = threat; }
                _candidates.Add(entry);
            }
            // 沿用威胁度分层：覆盖完整同威胁组才是保证行动，之后才过滤Extra。
            bool guaranteed = available >= _candidates.Count;
            if (!guaranteed)
            {
                _unallocated.RemoveAll(e => e.Enemy.Entity.Threat == highestThreat && !e.Enemy.CanReceiveExtraAction);
                _candidates.RemoveAll(e => !e.Enemy.CanReceiveExtraAction);
            }
            // 一次处理完整组，避免过滤Extra后将同组余下的随机机会误判为保证行动。
            while (available > 0 && _candidates.Count > 0)
            {
                int index = _candidates.Count == 1 ? 0 : BattleRandom.NextInt(0, _candidates.Count);
                var selected = _candidates[index];
                if (_candidates.Count > 1)
                    LogManager.Log(LogCategory.AI,
                        $"敌方轮次预分配：候选数={_candidates.Count} roll={index}，选中位置={selected.Enemy.Entity.SlotPosition}");
                _round.Add(new Allocation { Entry = selected, Guaranteed = guaranteed });
                _candidates.RemoveAt(index);
                _unallocated.Remove(selected);
                available--;
            }
        }
        return _round.Count > 0;
    }
}
