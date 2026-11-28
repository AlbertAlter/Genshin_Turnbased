using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Only enemy arrival order. No player resources, global phase, timers, statuses or random draws.</summary>
public sealed class BattleEnemyWavePlan
{
    private readonly IReadOnlyList<StageWaveSetup> waves;
    private readonly SortedDictionary<int, Queue<StageEnemySetup>> pending = new();
    private int index;
    public int CurrentWave => index + 1;
    public int WaveCount => waves.Count;
    public bool HasNextWave => index + 1 < waves.Count;
    public bool HasRequiredPending => pending.Values.Any(q => q.Any(e => !e.IgnoreForVictory))
        || waves.Skip(index + 1).Any(w => w.Enemies.Any(e => !e.IgnoreForVictory));
    public bool HasCurrentRequiredPending => pending.Values.Any(q => q.Any(e => !e.IgnoreForVictory));
    public int PendingEnemyCount => pending.Values.Sum(q => q.Count)
        + waves.Skip(index + 1).Sum(w => w.Enemies.Count);
    public BattleEnemyWavePlan(IReadOnlyList<StageWaveSetup> waves)
    {
        if (waves == null || waves.Count == 0) throw new ArgumentException("敌人波次不能为空。");
        for (int i = 0; i < waves.Count; i++)
            if (waves[i]?.Wave != i + 1 || waves[i].Enemies == null || waves[i].Enemies.Count == 0
                || waves[i].Enemies.Any(e => e == null || e.Slot < 1 || e.Slot > 5 || e.EnemyID <= 0))
                throw new ArgumentException("敌人波次须连续，名单和位置必须有效。");
        this.waves = waves;
        Fill();
    }
    private void Fill()
    {
        pending.Clear();
        foreach (var enemy in waves[index].Enemies)
        {
            if (!pending.TryGetValue(enemy.Slot, out var queue)) pending.Add(enemy.Slot, queue = new Queue<StageEnemySetup>());
            queue.Enqueue(enemy);
        }
    }
    public bool HasVacantArrivals(Func<int, bool> vacant)
        => pending.Any(p => p.Value.Count > 0 && vacant(p.Key));
    public List<StageEnemySetup> TakeVacantArrivals(Func<int, bool> vacant)
    {
        var result = new List<StageEnemySetup>();
        foreach (var pair in pending)
            if (pair.Value.Count > 0 && vacant(pair.Key)) result.Add(pair.Value.Dequeue());
        return result;
    }
    public bool Advance(bool allRequiredDefeated)
    {
        if (!allRequiredDefeated || HasCurrentRequiredPending || !HasNextWave) return false;
        index++; Fill(); return true;
    }
}
