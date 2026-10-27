using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    public class StatusActionCooldownPhaseTests
    {
        [TestCase(1)]
        [TestCase(3)]
        public void EveryTriggerPhase_WaitsUntilTheSamePhaseAfterTheFullCooldown(int cooldown)
        {
            for (int originPhase = 1; originPhase <= 6; originPhase++)
            {
                var state = new StatusActionRuntimeState
                {
                    LastTriggerTurn = 10,
                    LastTriggerPhase = originPhase
                };
                for (int phase = 1; phase <= 6; phase++)
                {
                    Assert.That(state.IsCoolingDown(cooldown, 10 + cooldown - 1, phase), Is.True);
                    Assert.That(state.IsCoolingDown(cooldown, 10 + cooldown, phase),
                        Is.EqualTo(phase < originPhase), $"origin={originPhase}, current={phase}");
                    Assert.That(state.IsCoolingDown(cooldown, 11 + cooldown, phase), Is.False);
                }
            }
        }

        [Test]
        public void EnemyActionTrigger_DoesNotBecomeReadyAtAllyPreTurn()
        {
            var state = new StatusActionRuntimeState { LastTriggerTurn = 2, LastTriggerPhase = 5 };
            Assert.That(state.IsCoolingDown(3, 5, 1), Is.True);
            Assert.That(state.IsCoolingDown(3, 5, 4), Is.True);
            Assert.That(state.IsCoolingDown(3, 5, 5), Is.False);
            Assert.That(state.IsCoolingDown(3, 5, 6), Is.False);
        }

        [Test]
        public void Query_DoesNotChangePerTurnLifeOrPendingCounters()
        {
            var state = new StatusActionRuntimeState
            {
                LastTriggerTurn = 2, LastTriggerPhase = 5, LastActionTurn = 3,
                TurnTriggerCount = 2, LifeTriggerCount = 7, PendingTriggerCount = 1
            };
            state.IsCoolingDown(3, 5, 4);
            Assert.That(state.LastTriggerTurn, Is.EqualTo(2));
            Assert.That(state.LastTriggerPhase, Is.EqualTo(5));
            Assert.That(state.LastActionTurn, Is.EqualTo(3));
            Assert.That(state.TurnTriggerCount, Is.EqualTo(2));
            Assert.That(state.LifeTriggerCount, Is.EqualTo(7));
            Assert.That(state.PendingTriggerCount, Is.EqualTo(1));
        }

        [Test]
        public void NeverTriggeredAndDisabledCooldown_AreReady()
        {
            Assert.That(new StatusActionRuntimeState().IsCoolingDown(3, 1, 1), Is.False);
            var state = new StatusActionRuntimeState { LastTriggerTurn = 10, LastTriggerPhase = 6 };
            Assert.That(state.IsCoolingDown(0, 10, 6), Is.False);
        }

        [Test]
        public void LegacyRecordWithoutPhase_RetainsTurnOnlyFallback()
        {
            var state = new StatusActionRuntimeState { LastTriggerTurn = 10 };
            Assert.That(state.IsCoolingDown(3, 12, 6), Is.True);
            Assert.That(state.IsCoolingDown(3, 13, 1), Is.False);
        }
    }
}
