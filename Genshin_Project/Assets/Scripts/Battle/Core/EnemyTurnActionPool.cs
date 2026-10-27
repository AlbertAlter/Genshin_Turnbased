using System;
using System.Collections.Generic;

/// <summary>
/// 单个敌方回合的共享行动池：每轮每人最多一次，威胁度高者优先，同威胁度随机。
/// 贡献在建池时快照；受控/死亡单位不贡献也不参与。执行间再次检查控制与存活，
/// 若失去资格则扣除该单位整份贡献（已执行的不回滚，剩余最少为零），本回合不重入。
/// 新增敌人由下一回合纳入。无可用技能仍消耗一次分配机会，防止无限空转。
/// </summary>
public sealed class EnemyTurnActionPool
{
    private sealed class Entry
    {
        public EnemyBattleController Enemy;
        public int Contribution;
        public bool ActedThisRound;
    }

    private readonly List<Entry> _entries = new List<Entry>();
    private readonly List<Entry> _candidates = new List<Entry>();
    private long _budget;
    private long _spent;

    public long RemainingActions => Math.Max(0L, _budget - _spent);
    public bool LastSelectionIsLastGuaranteedAction { get; private set; }

    public EnemyTurnActionPool(IEnumerable<EnemyBattleController> enemies)
    {
        var seen = new HashSet<EnemyBattleController>();
        foreach (var enemy in enemies)
        {
            if (enemy == null || !enemy.CanAct || !seen.Add(enemy)) continue;
            int contribution = enemy.ActionContribution;
            _entries.Add(new Entry { Enemy = enemy, Contribution = contribution });
            _budget += contribution;
        }
    }

    public bool TryTakeNext(out EnemyBattleController enemy)
    {
        enemy = null;
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            var entry = _entries[i];
            if (entry.Enemy != null && entry.Enemy.CanAct) continue;
            _budget -= entry.Contribution;
            _entries.RemoveAt(i);
            LogManager.Log(LogCategory.Enemy,
                $"敌方单位死亡或受控，扣除贡献={entry.Contribution}，剩余次数={RemainingActions}");
        }
        if (RemainingActions == 0 || _entries.Count == 0) return false;

        CollectHighestThreat();
        if (_candidates.Count == 0)
        {
            foreach (var entry in _entries) entry.ActedThisRound = false;
            CollectHighestThreat();
        }
        if (_candidates.Count == 0) return false;

        // 剩余预算覆盖整个最高威胁组时，本次分配才属于保证行动；部分覆盖属于随机补分。
        bool currentIsGuaranteed = RemainingActions >= _candidates.Count;
        // 只为真正需要打破平局的本次分配抽取一次；日志不重复抽取。
        int index = _candidates.Count == 1 ? 0 : BattleRandom.NextInt(0, _candidates.Count);
        var selected = _candidates[index];
        if (_candidates.Count > 1)
            LogManager.Log(LogCategory.AI,
                $"敌方同威胁度分配：候选数={_candidates.Count} roll={index}，选中位置={selected.Enemy.Entity.SlotPosition}");
        selected.ActedThisRound = true;
        _spent++;
        enemy = selected.Enemy;
        int aheadOrTied = 0, unacted = 0;
        foreach (var entry in _entries)
        {
            if (!entry.Enemy.CanReceiveOrdinaryAction) continue;
            if (entry.Enemy.Entity.Threat >= selected.Enemy.Entity.Threat) aheadOrTied++;
            if (!entry.ActedThisRound) unacted++;
        }
        // 当前轮其余单位分完后，下一轮须覆盖更高威胁度及整个同威胁组，才能保证本单位再行动。
        // 不提前抽未来的随机数；同威胁度时等价于是否足够完整覆盖下一轮。
        LastSelectionIsLastGuaranteedAction = currentIsGuaranteed
            && RemainingActions < (long)unacted + aheadOrTied;
        return true;
    }

    private void CollectHighestThreat()
    {
        _candidates.Clear();
        int highestThreat = int.MinValue;
        foreach (var entry in _entries)
        {
            if (entry.ActedThisRound || !entry.Enemy.CanReceiveOrdinaryAction) continue;
            int threat = entry.Enemy.Entity.Threat;
            if (threat < highestThreat) continue;
            if (threat > highestThreat)
            {
                _candidates.Clear();
                highestThreat = threat;
            }
            _candidates.Add(entry);
        }
    }
}
