using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GenshinTurnBased.Tests.EditMode
{
    public class EnemySixthTenIntegrationTests
    {
        [Test]
        public void HiddenEnemy_SkippedBySingleTargetIncludingTaunt_StillOnFieldAndInArea()
        {
            using var env = new BattleFlowTestEnv();
            env.CreateMinimalBattle();
            var hidden = env.Enemy.Entity;
            var ally = env.Ally.Entity;
            var visible = env.CreateEnemy(2).Entity;
            var id = PublicControlStates.EnemyStealth;
            env.ConfigureStatus(id);
            var main = env.DataManager.StatusMainDict[id];
            Assert.That(ally.AddStatus(id, hidden, 1, 5, 0, main), Is.Null);
            Assert.That(hidden.AddStatus(id, ally, 1, 5, 0, main), Is.Null);
            Assert.Throws<InvalidOperationException>(() => hidden.Position.AddStatus(id, hidden, 1, 5, 0, main));
            env.ConfigureStatus("Taunt");
            hidden.AddStatus("Taunt", hidden, 1, 5, 0, env.DataManager.StatusMainDict["Taunt"]);
            hidden.AddStatus(id, hidden, 1, 5, 0, main);
            Assert.That(hidden.IsOnField && env.Enemy.CanContributeActions && env.Enemy.CanReceiveOrdinaryAction, Is.True);
            BattleRandom.SetSource(new SystemBattleRandomSource(2060));
            TargetResolutionResult Select(int count, int mode, string type = "Enemy", IList<int> forced = null)
                => BattleTargetResolver.Resolve(env.Manager, ally, ally, type, count, mode, null, null, forced, null, respectTaunt: true);
            for (int mode = 0; mode <= 4; mode++)
                Assert.That(Select(1, mode).Positions, Is.EqualTo(new[] { visible.SlotPosition }));
            Assert.That(Select(1, 0, forced: new[] { hidden.SlotPosition }).IsValid, Is.False);
            Assert.That(BattleTargetResolver.GetEntities(env.Manager, Select(-1, 0)), Does.Contain(hidden));
            Assert.That(BattleTargetResolver.GetEntities(env.Manager, Select(2, 2)), Does.Contain(hidden));
            Assert.That(BattleTargetResolver.GetEntities(env.Manager, Select(1, 0, "EnemyField", new[] { hidden.SlotPosition })), Does.Contain(hidden));
            visible.CurrentHP = 0;
            Assert.That(Select(1, 1).IsValid, Is.False);
        }

        [Test]
        public void Reveal_OnlySuspendsOwnedStealth_AndCannotRestoreAnExpiredParent()
        {
            using var env = new BattleFlowTestEnv();
            env.CreateMinimalBattle();
            var agent = env.Enemy.Entity;
            var other = env.CreateEnemy(2).Entity;
            var marker = new StatusMainData { StatusID = 40042, StatusID2 = PublicControlStates.EnemyStealth, MaxStack = 0 };
            var window = new StatusMainData { StatusID = 4206002, StatusID2 = "ST_PyroAgent_Stealth", MaxStack = 1 };
            var reveal = new StatusMainData { StatusID = 4206003, StatusID2 = "ST_PyroAgent_Revealed", MaxStack = 1 };
            foreach (var def in new[] { marker, window, reveal }) env.DataManager.StatusMainDict[def.StatusID2] = def;
            env.DataManager.StatusEffectDict["STE_TestAgentBind"] = new StatusEffectData {
                StatusEffectID = 420600204, StatusEffectID2 = "STE_TestAgentBind", EffectType = "BindStatus",
                TargetType = "Self", Param1 = marker.StatusID2 };
            var external = agent.AddStatus(marker.StatusID2, other, 9, 5, 0, marker);
            var parent = agent.AddStatus(window.StatusID2, agent, 5, 5, 0, window);
            Assert.That(agent.GetStatusInstances(marker.StatusID2), Has.Count.EqualTo(2));
            var visible = agent.AddStatus(reveal.StatusID2, agent, 1, 2, 0, reveal);
            Assert.That(agent.GetStatusInstances(marker.StatusID2), Is.EqualTo(new[] { external }));
            parent.RemainingPhaseCount = 2;
            agent.RemoveStatusInstance(visible);
            Assert.That(parent.BoundStatuses, Has.Count.EqualTo(1));
            Assert.That(parent.BoundStatuses[0].RemainingPhaseCount, Is.EqualTo(2));
            visible = agent.AddStatus(reveal.StatusID2, agent, 1, 2, 0, reveal);
            agent.RemoveStatusInstance(parent);
            agent.RemoveStatusInstance(visible);
            Assert.That(agent.GetStatusInstances(marker.StatusID2), Is.EqualTo(new[] { external }));
        }

        [UnityTest]
        public IEnumerator ClonesFromDifferentAgents_ShareThreshold_OnlyOneRemovedPerAction()
            => HookTestEnv.AwaitTask(ClonesAsync);
        private async Task ClonesAsync()
        {
            using var env = new BattleFlowTestEnv();
            env.CreateMinimalBattle();
            var a = env.Enemy.Entity; var b = env.CreateEnemy(2).Entity;
            var field = env.Ally.Entity.Position;
            env.ConfigureStatus(Enemy2051To2060Hooks.Clone, maxStack: 0);
            env.ConfigureStatus(PublicControlStates.EnemyStealth);
            a.AddStatus(PublicControlStates.EnemyStealth, a, 5, 5, 0, env.DataManager.StatusMainDict[PublicControlStates.EnemyStealth]);
            var main = env.DataManager.StatusMainDict[Enemy2051To2060Hooks.Clone];
            var first = field.AddStatus(main.StatusID2, a, 0, 5, 0, main);
            foreach (var caster in new[] { b, a, b }) field.AddStatus(main.StatusID2, caster, 0, 5, 0, main);
            Assert.That(ScriptHookEvaluator.Evaluate("AgentBurstDue()", a), Is.True);
            await BattleExecution.Run(async () => {
                BattleEventHub.Publish(new ActionEventData(env.Ally.Entity, "SK_Test", "Normal", true));
                await BattleExecution.Flush();
            });
            Assert.That(first.IsActive, Is.False);
            Assert.That(field.StatusList.FindAll(s => s.StatusID2 == main.StatusID2), Has.Count.EqualTo(3));
            Assert.That(ScriptHookEvaluator.Evaluate("AgentBurstDue()", a), Is.False);
        }
    }
}
