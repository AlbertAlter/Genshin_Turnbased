using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class EulaCountTests
    {
        private static void Setup(BattleFlowTestEnv env)
        {
            env.ConfigureAllyAction(0, "SK_Normal_Eula", "SE_Normal_EulaDamage", "Physical", "", 20,
                param1: ".1*TotalATK,0");
            env.ConfigureStatus(EulaScriptHooks.Root, 1, "1", "OnTrigger", "STE_Eula_TestGain");
            env.DataManager.StatusActionDict[EulaScriptHooks.Root][0].ScriptHook = "Eula_ChargeHit()";
            env.DataManager.StatusEffectDict["STE_Eula_TestGain"] = new StatusEffectData {
                StatusEffectID2 = "STE_Eula_TestGain", EffectType = "Count", Param1 = EulaScriptHooks.Sword,
                Param2 = "1", TargetType = "Self", ExecuteCountHandler = "Eula_HitTargets" };
            env.ConfigureStatus(EulaScriptHooks.Sword, 0, "0");
            env.ConfigureStatus(EulaScriptHooks.Heart, 1, "1");
            env.DataManager.StatusMainDict[EulaScriptHooks.Sword].StatusType = "Count";
            env.DataManager.StatusMainDict[EulaScriptHooks.Heart].StatusType = "Count";
            env.CreateMinimalBattle(); env.Ally.Entity.EntityID = 1040;
            env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            Add(env, EulaScriptHooks.Root, 0);
        }
        private static StatusInstance Add(BattleFlowTestEnv env, string id, int duration = 2)
        {
            var owner = env.Ally.Entity;
            return owner.AddStatus(id, owner, duration, 2, 0, env.DataManager.StatusMainDict[id]);
        }
        private static void Change(BattleFlowTestEnv env, string amount)
            => CountEffect.Execute(EulaScriptHooks.Sword, amount, env.Ally.Entity, new[] { env.Ally.Entity });

        [Test]
        public void NewSwords_CoexistWithIndependentValuesAndTime_AllCountChangesRetainInstances()
        {
            using var env = new BattleFlowTestEnv(); Setup(env);
            var first = Add(env, EulaScriptHooks.Sword); Change(env, "10"); first.RemainingPhaseCount = 1;
            var second = Add(env, EulaScriptHooks.Sword);
            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(first.RuntimeIntValue, Is.EqualTo(10)); Assert.That(second.RuntimeIntValue, Is.Zero);
            Change(env, "2");
            Assert.That(first.RuntimeIntValue, Is.EqualTo(12)); Assert.That(second.RuntimeIntValue, Is.EqualTo(2));
            Assert.That(first.RemainingPhaseCount, Is.EqualTo(1)); Assert.That(second.RemainingPhaseCount, Is.EqualTo(2));
            Change(env, "full");
            Assert.That(first.RuntimeIntValue, Is.EqualTo(30)); Assert.That(second.RuntimeIntValue, Is.EqualTo(30));
            Change(env, "clear");
            Assert.That(first.RuntimeIntValue, Is.Zero); Assert.That(second.RuntimeIntValue, Is.Zero);
            Assert.That(env.Ally.Entity.GetStatusInstances(EulaScriptHooks.Sword).Count, Is.EqualTo(2));
        }

        [Test]
        public void NewCount_UsesExistingWhenMaxRules_AndRemovalPreservesDetonationValue()
        {
            using var env = new BattleFlowTestEnv(); Setup(env);
            var definition = env.DataManager.StatusMainDict[EulaScriptHooks.Sword];
            definition.MaxStack = 1; definition.WhenMax = "1";
            var first = Add(env, EulaScriptHooks.Sword); Change(env, "8"); first.RemainingPhaseCount = 1;
            Assert.That(Add(env, EulaScriptHooks.Sword), Is.SameAs(first));
            Assert.That(first.RuntimeIntValue, Is.EqualTo(8)); Assert.That(first.RemainingPhaseCount, Is.EqualTo(1));
            definition.WhenMax = "0";
            var second = Add(env, EulaScriptHooks.Sword);
            Assert.That(first.IsActive, Is.False); Assert.That(first.RuntimeIntValue, Is.EqualTo(8));
            Assert.That(second.RuntimeIntValue, Is.Zero); Assert.That(second.RemainingPhaseCount, Is.EqualTo(2));
        }

        [Test]
        public void C6_InitialEnergyIsFivePerNewSword_AndColdHeartRefreshPreservesItsValue()
        {
            using var env = new BattleFlowTestEnv(); Setup(env);
            env.Ally.Entity.ConstellationLevel = 6;
            var first = Add(env, EulaScriptHooks.Sword); var second = Add(env, EulaScriptHooks.Sword);
            Assert.That(first.RuntimeIntValue, Is.EqualTo(5)); Assert.That(second.RuntimeIntValue, Is.EqualTo(5));
            var heart = Add(env, EulaScriptHooks.Heart, 5);
            CountEffect.Execute(EulaScriptHooks.Heart, "1", env.Ally.Entity, new[] { env.Ally.Entity });
            heart.RemainingPhaseCount = 1;
            Assert.That(ScriptHookEvaluator.Evaluate("Eula_HeartTime()", new ScriptHookContext { Caster = env.Ally.Entity }), Is.True);
            Assert.That(Add(env, EulaScriptHooks.Heart, 5), Is.SameAs(heart));
            Assert.That(heart.RuntimeIntValue, Is.EqualTo(1)); Assert.That(heart.RemainingPhaseCount, Is.EqualTo(5));
        }

        [UnityTest]
        public IEnumerator OneSharedTrigger_ChargesTwoSwordsOnceEach()
            => HookTestEnv.AwaitTask(SharedTriggerAsync);
        private static async Task SharedTriggerAsync()
        {
            using var env = new BattleFlowTestEnv(); Setup(env);
            var first = Add(env, EulaScriptHooks.Sword); var second = Add(env, EulaScriptHooks.Sword);
            var changes = new List<ResourceEventData>();
            using var listener = BattleEventHub.Subscribe<ResourceEventData>(BattleEventKind.ResourceChanged,
                (_, fact) => changes.Add(fact), 0);
            await BattleExecution.Run(() => env.Ally.ExecuteSkillByIDAsync("SK_Normal_Eula", 0, true));
            Assert.That(first.RuntimeIntValue, Is.EqualTo(1)); Assert.That(second.RuntimeIntValue, Is.EqualTo(1));
            Assert.That(changes.Count(f => f.Subject == env.Ally.Entity && f.Added == 1), Is.EqualTo(2));
        }
    }
}
