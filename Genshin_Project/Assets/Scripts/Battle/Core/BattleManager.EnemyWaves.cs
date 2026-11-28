using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public partial class BattleManager
{
    public BattleFlowPresentationChannel FlowPresentation { get; } = new BattleFlowPresentationChannel();
    public int CurrentWave => enemyWaves?.CurrentWave ?? 1;
    public int WaveCount => enemyWaves?.WaveCount ?? 1;
    // 包含当前占位者、同槽后备和后续波次；死亡/逃跑不再计数，实际召唤成功的敌人计入。
    public int RemainingEnemyCount => _enemies.Count(e => e?.Entity != null && !e.Entity.IsDead && !e.HasWithdrawn)
        + (enemyWaves?.PendingEnemyCount ?? 0);
    public bool IsEnemyArrivalPending { get; private set; }
    private BattleEnemyWavePlan enemyWaves;
    private Func<StageEnemySetup, EnemyBattleController> spawnWaveEnemy;
    private Action<EnemyBattleController> retireWaveEnemy;
    private readonly Dictionary<EnemyBattleController, StageEnemySetup> waveInstances = new();
    private readonly List<EnemyBattleController> retiredWaveEnemies = new();

    /// <summary>Called after resetting the previous battle. Registers only the first enemy in each first-wave slot.</summary>
    public void ConfigureEnemyWaves(IReadOnlyList<StageWaveSetup> waves,
        Func<StageEnemySetup, EnemyBattleController> spawn, Action<EnemyBattleController> retire)
    {
        if (IsBattleRunning) throw new InvalidOperationException("不能在战斗中替换敌人波次配置。");
        enemyWaves = new BattleEnemyWavePlan(waves);
        spawnWaveEnemy = spawn ?? throw new ArgumentNullException(nameof(spawn));
        retireWaveEnemy = retire ?? throw new ArgumentNullException(nameof(retire));
        waveInstances.Clear();
        SpawnArrivals(enemyWaves.TakeVacantArrivals(_ => true));
    }
    private List<BattleAttributeInitialization.OpeningHealth> SpawnArrivals(List<StageEnemySetup> arrivals)
    {
        var created = new List<EnemyBattleController>();
        foreach (var setup in arrivals)
        {
            var controller = spawnWaveEnemy(setup);
            waveInstances.Add(controller, setup); created.Add(controller);
        }
        // The complete new enemy cohort is registered before any initialization targets are resolved.
        var opening = IsBattleRunning ? created.Select(c => BattleAttributeInitialization.Capture(c.Entity)).ToList()
            : new List<BattleAttributeInitialization.OpeningHealth>();
        if (IsBattleRunning)
            foreach (var controller in created) ApplyWaveEntry(controller, waveInstances[controller]);
        return opening;
    }
    internal void ApplyWaveEntryStatuses()
    {
        foreach (var pair in waveInstances.ToArray()) ApplyWaveEntry(pair.Key, pair.Value);
    }
    private void ApplyWaveEntry(EnemyBattleController controller, StageEnemySetup setup)
    {
        controller.ApplyInitiateStatuses();
        if (setup.AIProfileID > 0) controller.SwitchPattern(setup.AIProfileID);
        foreach (var entry in setup.InitialStatuses ?? new List<StageEnemyStatusSetup>())
        {
            string id = CountStatusSystem.ResolvePublicStatusReference(entry.StatusID);
            if (!DataManager.Instance.StatusMainDict.TryGetValue(id, out var definition))
                throw new InvalidOperationException("关卡初始状态不存在：" + entry.StatusID);
            controller.Entity.AddStatus(id, controller.Entity, entry.Turns, 1, 0, definition);
        }
    }
    private bool RequiredForVictory(EnemyBattleController enemy)
        => !waveInstances.TryGetValue(enemy, out var setup) || !setup.IgnoreForVictory;
    private bool AllRequiredEnemiesDefeated()
        => !_enemies.Any(e => e?.Entity != null && !e.Entity.IsDead && !e.HasWithdrawn && RequiredForVictory(e));
    private bool VacantEnemySlot(int slot)
        => !_enemies.Any(e => e?.Entity != null && e.Entity.SlotPosition == slot && !e.Entity.IsDead && !e.HasWithdrawn);
    private void ScheduleEnemyArrivals()
    {
        if (enemyWaves == null || IsEnemyArrivalPending || !IsBattleRunning || IsBattleOver) return;
        if (MaxRounds > 0 && TurnCount >= MaxRounds && CurrentPhase == TurnPhase.EnemyPostTurn) return;
        if (!enemyWaves.HasVacantArrivals(VacantEnemySlot)
            && !(enemyWaves.HasNextWave && !enemyWaves.HasCurrentRequiredPending && AllRequiredEnemiesDefeated())) return;
        IsEnemyArrivalPending = true;
        int generation = FlowPresentation.Generation;
        // This boundary includes the top-level action/status and all its child responses and presentations.
        BattleExecution.AfterRoot(() => BattleExecution.Observe(DeployEnemyArrivals(generation)),
            () => { if (generation == FlowPresentation.Generation) IsEnemyArrivalPending = false; });
    }
    private async Task DeployEnemyArrivals(int generation)
    {
        try
        {
            await FlowPresentation.PresentAsync(new BattleFlowNotice(BattleFlowNoticeKind.EnemyRefresh, TurnCount, CurrentWave, WaveCount));
            if (generation != FlowPresentation.Generation || !IsBattleRunning || IsBattleOver) return;
            bool advanced = enemyWaves.Advance(AllRequiredEnemiesDefeated());
            await BattleExecution.Run(async () => {
                using (StatusRemovalActions.SuppressForCleanup())
                    foreach (var enemy in _enemies.Concat(waveInstances.Keys).Concat(summonedEnemies).Distinct().ToArray())
                    {
                        if (enemy?.Entity == null || !(advanced || enemy.Entity.IsDead || enemy.HasWithdrawn)) continue;
                        // Instance cleanup only; do not clear field/team/player statuses or publish a fake kill.
                        foreach (var status in enemy.Entity.StatusDict.Values.ToArray())
                            StatusHostAccess.Remove(enemy.Entity, status, "EnemyRosterChanged");
                        if (enemy.Entity.Position?.Occupant == enemy.Entity) enemy.Entity.Position.Occupant = null;
                        enemy.Entity.Position = null;
                        enemy.Entity.HasWithdrawn = true; // Administrative removal, not an escape declaration.
                        _enemies.Remove(enemy); waveInstances.Remove(enemy); summonedEnemies.Remove(enemy);
                        // Persistent field/team statuses still dispatch through their original caster's controller.
                        // Keep that source alive until battle teardown, outside all targeting/action registries.
                        retiredWaveEnemies.Add(enemy);
                    }
                var opening = SpawnArrivals(enemyWaves.TakeVacantArrivals(VacantEnemySlot));
                await BattleExecution.Flush();
                BattleAttributeInitialization.Complete(opening);
            });
            if (advanced && generation == FlowPresentation.Generation && !IsBattleOver)
                await FlowPresentation.PresentAsync(new BattleFlowNotice(BattleFlowNoticeKind.WaveStart, TurnCount, CurrentWave, WaveCount));
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            UnityEngine.Debug.LogException(error);
            StopBattle(); // Keep the saved pre-battle recovery point; never convert a broken deployment into victory.
        }
        finally
        {
            if (generation == FlowPresentation.Generation)
            {
                IsEnemyArrivalPending = false;
                if (IsBattleRunning && !IsBattleOver) { ScheduleEnemyArrivals(); CheckBattleEnd(); }
            }
        }
    }
    private void ClearEnemyWaves()
    {
        FlowPresentation.Cancel(); IsEnemyArrivalPending = false;
        ReleaseRetiredWaveEnemies();
        enemyWaves = null; spawnWaveEnemy = null; retireWaveEnemy = null; waveInstances.Clear();
    }
    private void ReleaseRetiredWaveEnemies()
    {
        foreach (var enemy in retiredWaveEnemies.Concat(waveInstances.Keys.Where(e => !_enemies.Contains(e))).Distinct().ToArray())
            if (enemy != null) { ClearEntityRuntimeState(enemy.Entity); retireWaveEnemy?.Invoke(enemy); }
        retiredWaveEnemies.Clear();
    }
}
