using System;
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    internal sealed class BattleFlowTestDriver
    {
        private readonly ManualBattleFlowScheduler _scheduler;

        public BattleFlowTestDriver(ManualBattleFlowScheduler scheduler)
        {
            _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        }

        public void AdvanceToPhase(BattleManager manager, TurnPhase phase, int maxSteps = 10000)
        {
            AdvanceUntil(
                manager,
                () => manager.CurrentPhase == phase && manager.IsCurrentPhaseReady,
                $"target phase={phase}",
                maxSteps);
        }

        public void AdvanceUntil(
            BattleManager manager,
            Func<bool> predicate,
            string expectation,
            int maxSteps = 10000)
        {
            if (manager == null) throw new ArgumentNullException(nameof(manager));
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            if (maxSteps <= 0) throw new ArgumentOutOfRangeException(nameof(maxSteps));

            int steps = 0;
            while (!predicate() && steps < maxSteps)
            {
                _scheduler.Step();
                steps++;
            }

            if (!predicate())
            {
                Assert.Fail(
                    $"Battle flow did not reach {expectation}; " +
                    $"current phase={manager.CurrentPhase}, " +
                    $"IsCurrentPhaseReady={manager.IsCurrentPhaseReady}, " +
                    $"IsBattleRunning={manager.IsBattleRunning}, " +
                    $"IsBattleOver={manager.IsBattleOver}, steps={steps}.");
            }
        }
    }
}
