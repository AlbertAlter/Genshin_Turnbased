using System.Collections;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GenshinTurnBased.Tests.EditMode
{
    public class RuinCoreIntegrationTests
    {
        [UnityTest]
        public IEnumerator SharedCore_MultiHitAreaCostsOnePerUnit_AndOnlyLandingRestartsFlightClock()
            => HookTestEnv.AwaitTask(RunCoreScenario);

        private async Task RunCoreScenario()
        {
            using var env = new BattleFlowTestEnv();
            env.CreateMinimalBattle();
            env.DataManager.ReloadAllData();
            await BattleExecution.Run(async () => {
                var guard = env.Manager.SummonEnemy(2062, 1, 2);
                var hunter = env.Manager.SummonEnemy(2063, 1, 3);
                await BattleExecution.Flush();
                var g = guard.Entity;
                var h = hunter.Entity;
                string core = Enemy2061To2063Hooks.Core;
                var gs = g.GetStatus(core);
                var hs = h.GetStatus(core);
                var initialWait = h.GetStatus("ST_RuinHunter_FlightWait");
                Assert.That(gs.RuntimeIntValue, Is.EqualTo(10));
                Assert.That(hs.RuntimeIntValue, Is.EqualTo(10));
                CountEffect.Execute(core, "1", g, new[] { g }, targetType: "Self");
                Assert.That(gs.RuntimeIntValue, Is.EqualTo(11));

                async Task HitBoth(int hits)
                {
                    using var scope = BattleResolutionContext.EnterAction(new BattleResolutionContext.CounterScope());
                    using var execution = BattleEventHub.BeginActionExecution();
                    for (int i = 0; i < hits; i++)
                    {
                        using (var batch = new HitBatch())
                            foreach (var target in new[] { g, h })
                                batch.Resolve(new HitRequest { Target = target, DamageBeforeShield = 0f,
                                    Element = "Physical", Source = DamageSourceInfo.Create(env.Ally.Entity,
                                        ReactionSourceKind.CharacterSkill, "SK_TestArea", "SE_TestArea") });
                        await BattleExecution.Flush();
                    }
                }

                await HitBoth(4);
                Assert.That(gs.RuntimeIntValue, Is.EqualTo(10));
                Assert.That(hs.RuntimeIntValue, Is.EqualTo(9));
                for (int i = 0; i < 9; i++) await HitBoth(1);
                Assert.That(gs.RuntimeIntValue, Is.EqualTo(1));
                Assert.That(hs.RuntimeIntValue, Is.Zero);
                Assert.That(h.HasStatus(PublicControlStates.EnemyStun), Is.True);
                Assert.That(h.GetStatus("ST_RuinHunter_FlightWait"), Is.SameAs(initialWait));
                guard.SwitchPattern(3);
                g.AddStatus("ST_RuinGuard_Charge", g, 1, 5, 0, env.DataManager.StatusMainDict["ST_RuinGuard_Charge"]);
                await HitBoth(2);
                Assert.That(gs.RuntimeIntValue, Is.Zero);
                Assert.That(guard.CurrentPatternID, Is.EqualTo(1));
                Assert.That(g.HasStatus("ST_RuinGuard_Charge"), Is.False);
                g.RemoveStatus(PublicControlStates.EnemyStun);
                await BattleExecution.Flush();
                Assert.That(g.GetStatus(core), Is.SameAs(gs));
                Assert.That(gs.RuntimeIntValue, Is.EqualTo(10));

                h.RemoveStatus(PublicControlStates.EnemyStun);
                h.RemoveStatus("ST_RuinHunter_FlightWait");
                await BattleExecution.Flush();
                const string flight = "ST_RuinHunter_Flight";
                h.AddStatus(flight, h, 0, 5, 0, env.DataManager.StatusMainDict[flight]);
                await BattleExecution.Flush();
                Assert.That(hunter.CurrentPatternID, Is.EqualTo(2));
                CountEffect.Execute(core, "-9", h, new[] { h }, targetType: "Self");
                await HitBoth(1);
                Assert.That(hunter.CurrentPatternID, Is.EqualTo(1));
                Assert.That(h.HasStatus(flight), Is.False);
                Assert.That(h.GetStatus("ST_RuinHunter_FlightWait").RemainingPhaseCount, Is.EqualTo(5));
            });
        }
    }
}

