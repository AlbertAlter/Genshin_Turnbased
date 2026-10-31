using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class EnemyThirdTenIntegrationTests
    {
        [Test]
        public void RuntimeTables_LoadThirdBatchAndCommonImprisonment()
        {
            using var env = new BattleFlowTestEnv(); env.DataManager.ReloadAllData();
            for (int id = 2021; id <= 2030; id++)
            {
                Assert.That(env.DataManager.EnemyMainDict[id].ActsGiven, Is.EqualTo(1));
                Assert.That(env.DataManager.EnemyAIRuleDict[id], Is.Not.Empty);
            }
            var prison = env.DataManager.StatusMainDict[PublicControlStates.Imprisoned];
            Assert.That(prison.StatusID, Is.EqualTo(40039));
            var bubble = env.DataManager.EnemySkillEffectDict["SE_LargeHydroSlime_Imprison"];
            Assert.That(bubble.Param1, Is.EqualTo(prison.StatusID2));
            Assert.That(bubble.Duration, Is.EqualTo(1)); Assert.That(bubble.AddInPhase, Is.EqualTo(3));
            Assert.That(bubble.TargetOverride, Is.EqualTo("0,0"));
            Assert.That(env.DataManager.EnemySkillEffectDict["SE_LargeAnemoSlime_Bump"].ExecuteCount, Is.EqualTo(2));
            Assert.That(env.DataManager.EnemySkillEffectDict["SE_LargeCryoSlime_Shards"].TargetConsecutive, Is.EqualTo(2));
            Assert.That(env.DataManager.EnemySkillParamDict["SE_CryoMage_Frostdrift"].Hits1, Is.EqualTo("0.8,2"));
            foreach (int id in new[] { 2016, 2023, 2030 })
                Assert.That(env.DataManager.EnemyMainDict[id].StatusImmunities, Does.Contain("冻结"));
        }

        [Test]
        public void Imprisonment_OnlyCharacter_AllButtonsFrozen_UntilAllyPostTurn_WithoutAPCost()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            var main = new StatusMainData { StatusID = 40039, StatusID2 = PublicControlStates.Imprisoned, MaxStack = 1 };
            env.DataManager.StatusMainDict[main.StatusID2] = main;
            env.DataManager.StatusEffectDict["STE_Imprisoned"] = new StatusEffectData {
                StatusEffectID = 4003901, StatusEffectID2 = "STE_Imprisoned", EffectType = "ChangeControl", Param1 = "All", Param2 = "Freeze" };
            int before = env.Ally.APManager.CurrentAP;
            Assert.That(env.Enemy.Entity.AddStatus(main.StatusID2, env.Ally.Entity, 1, 3, 0, main), Is.Null);
            Assert.That(env.Enemy.Entity.AddStatus(main.StatusID2, env.Ally.Entity, 1), Is.Null);
            env.Ally.Entity.AddStatus(main.StatusID2, env.Enemy.Entity, 1, 3, 0, main);
            for (int i = 0; i < 4; i++) Assert.That(env.Ally.IsButtonFrozen(i), Is.True);
            Tick(env.Manager, TurnPhase.AllyPreTurn);
            Assert.That(env.Ally.Entity.HasStatus(main.StatusID2), Is.True);
            Tick(env.Manager, TurnPhase.AllyPostTurn);
            Assert.That(env.Ally.Entity.HasStatus(main.StatusID2), Is.False);
            for (int i = 0; i < 4; i++) Assert.That(env.Ally.IsButtonFrozen(i), Is.False);
            Assert.That(env.Ally.APManager.CurrentAP, Is.EqualTo(before));
        }

        [Test]
        public void DeathExplosion_OnlyExactNeighbors_DoesNotFillGapsOrDrawRandom()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            var dead = env.CreateEnemy(2).Entity; dead.CurrentHP = 0;
            env.CreateEnemy(4);
            var random = new SystemBattleRandomSource(21); BattleRandom.SetSource(random);
            Assert.That(Enemy2021To2030Hooks.DeathNeighbors(env.Manager, dead), Is.EqualTo(new[] { 1 }));
            env.CreateEnemy(3);
            Assert.That(Enemy2021To2030Hooks.DeathNeighbors(env.Manager, dead), Is.EqualTo(new[] { 1, 3 }));
            Assert.That(random.Step, Is.Zero);
        }

        [Test]
        public void Summon_FullFieldSkipsRandom_DeadSlotReplaced_UsesSourceLevel()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            foreach (var slot in env.Manager.Field.EnemySlots)
                if (slot.Occupant == null) env.CreateEnemy(slot.SlotIndex);
            var random = new SystemBattleRandomSource(2024); BattleRandom.SetSource(random);
            Enemy2021To2030Hooks.Summon(env.Enemy.Entity, null, BattleFlowTestEnv.EnemyID.ToString());
            Assert.That(random.Step, Is.Zero);
            var old = env.Manager.GetEntityByPosition(BattleSide.Enemy, 2); old.CurrentHP = 0;
            Enemy2021To2030Hooks.Summon(env.Enemy.Entity, null, BattleFlowTestEnv.EnemyID.ToString());
            var replacement = env.Manager.GetEntityByPosition(BattleSide.Enemy, 2);
            Assert.That(replacement, Is.Not.SameAs(old));
            Assert.That(replacement.Level, Is.EqualTo(env.Enemy.Entity.Level));
            Assert.That(replacement.IsAlive, Is.True); Assert.That(old.Position, Is.Null);
            Assert.That(random.Step, Is.EqualTo(1));
        }

        [Test]
        public void Charge_CapsAtEight_ClearRetainsStateAndSavedDischargeCount()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            var owner = env.Enemy.Entity; var id = Enemy2021To2030Hooks.Charge;
            var main = new StatusMainData { StatusID = 4202902, StatusID2 = id, StatusType = "Count" };
            env.DataManager.StatusMainDict[id] = main;
            var state = owner.AddStatus(id, owner, 0, 5, 0, main);
            Assert.That(state.RuntimeIntValue, Is.Zero);
            CountEffect.Execute(id, "15", owner, new[] { owner }, targetType: "Self");
            Assert.That(state.RuntimeIntValue, Is.EqualTo(8));
            CountEffect.Execute(id, "clear", owner, new[] { owner }, Enemy2021To2030Hooks.DischargeClear, targetType: "Self");
            Assert.That(state.RuntimeIntValue, Is.Zero); Assert.That(owner.HasStatus(id), Is.True);
            Assert.That(ExecuteCountEvaluator.Evaluate(1, "MutantDischargeCount()", owner), Is.EqualTo(8));
            CountEffect.Execute(id, "1", owner, new[] { owner }, targetType: "Self");
            Assert.That(state.RuntimeIntValue, Is.EqualTo(1));
            Assert.That(ExecuteCountEvaluator.Evaluate(1, "MutantDischargeCount()", owner), Is.EqualTo(8));
        }

        [UnityEngine.TestTools.UnityTest]
        public IEnumerator Charge_UsesCompletedActions_AndActualSelfIdentity()
            => HookTestEnv.AwaitTask(ChargeActionsAsync);

        private async Task ChargeActionsAsync()
        {
            using var env = new BattleFlowTestEnv(); env.ConfigureEnemyDamageSkill(.01f); env.CreateMinimalBattle();
            env.DataManager.EnemySkillMainDict[990201].UsePerTurn = 0;
            var owner = env.Enemy.Entity; owner.EntityID = 2029;
            string id = Enemy2021To2030Hooks.Charge, effect = "STE_TestCharge";
            var main = new StatusMainData { StatusID = 4202902, StatusID2 = id, StatusType = "Count" };
            env.DataManager.StatusMainDict[id] = main;
            env.DataManager.StatusActionDict[id] = new List<StatusActionData> { new StatusActionData {
                StatusID2 = id, ActionType = "OnTrigger", ScriptHook = "MutantChargeAction()", Param1 = effect } };
            env.DataManager.StatusEffectDict[effect] = new StatusEffectData {
                StatusEffectID2 = effect, EffectType = "Count", Param1 = id, Param2 = "1", TargetType = "Self" };
            var state = owner.AddStatus(id, owner, 0, 5, 0, main);
            var other = env.CreateEnemy(2); other.Entity.EntityID = 2029;
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            await BattleExecution.Run(other.TakeEnemyActionAsync);
            Assert.That(state.RuntimeIntValue, Is.Zero); // 另一只同ID变异史莱姆不是自身。
            other.Entity.EntityID = 2018;
            await BattleExecution.Run(other.TakeEnemyActionAsync);
            Assert.That(state.RuntimeIntValue, Is.EqualTo(1));
            other.Entity.EntityID = 2025;
            await BattleExecution.Run(other.TakeEnemyActionAsync);
            Assert.That(state.RuntimeIntValue, Is.EqualTo(2));
            await BattleExecution.Run(env.Enemy.TakeEnemyActionAsync);
            Assert.That(state.RuntimeIntValue, Is.EqualTo(3));
            Assert.That(random.IntCallCount, Is.EqualTo(4));
            Assert.That(random.FloatCallCount, Is.Zero);
        }

        private static void Tick(BattleManager manager, TurnPhase phase)
        {
            var method = typeof(BattleManager).GetMethod("ExecuteTimerSet", BindingFlags.NonPublic | BindingFlags.Instance);
            Drain((IEnumerator)method.Invoke(manager, new object[] { phase }));
        }
        private static void Drain(IEnumerator sequence)
        { while (sequence.MoveNext()) if (sequence.Current is IEnumerator nested) Drain(nested); }
    }
}
