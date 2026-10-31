using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class EnemyActionExecutionTests
    {
        [UnityTest]
        public IEnumerator ZeroTotalAllyHitWeight_SkipsDamageWithoutChangingSkillWeightOrRetrying()
            => HookTestEnv.AwaitTask(ZeroTotalAllyHitWeightAsync);

        private async Task ZeroTotalAllyHitWeightAsync()
        {
            using var env = new BattleFlowTestEnv();
            env.ConfigureEnemyDamageSkill();
            env.DataManager.EnemySkillEffectDict["SE_Enemy_BattleFlow"].TargetConsecutive = 1;
            env.CreateMinimalBattle();
            env.Ally.Entity.HitWeight = 0f;
            env.CreateAlly(2).Entity.HitWeight = 0f;
            var rule = env.DataManager.EnemyAIRuleDict[BattleFlowTestEnv.EnemyID][0];
            int originalWeight = rule.Weight;
            Assert.That(originalWeight, Is.GreaterThan(0));
            int hits = 0;
            using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved,
                (_, hit) => hits++, 0);
            var random = new SequenceBattleRandomSource();
            BattleRandom.SetSource(random);
            await BattleExecution.Run(env.Enemy.TakeEnemyActionAsync);
            Assert.That(hits, Is.Zero);
            Assert.That(rule.Weight, Is.EqualTo(originalWeight));
            Assert.That(random.IntCallCount, Is.EqualTo(1), "仅选择一次技能，不重抽技能或目标");
            Assert.That(random.FloatCallCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator HilichurlTwoBasicSkills_EachHaveOneBoundaryAndOneIndependentIdentity()
            => HookTestEnv.AwaitTask(HilichurlAsync);
        private async Task HilichurlAsync()
        {
            long previous=0;
            foreach(var entry in new[] { (200102,"SK_Hilichurl_Hit",.75f), (200103,"SK_Hilichurl_ThrowStone",.5f) })
            {
                using var env=new BattleFlowTestEnv(); env.ConfigureEnemyDamageSkill(entry.Item3);
                var main=env.DataManager.EnemySkillMainDict[990201]; env.DataManager.EnemySkillMainDict.Clear();
                main.EnemySkillID=entry.Item1;main.EnemySkillID2=entry.Item2;main.UsePerTurn=0;
                env.DataManager.EnemySkillMainDict[entry.Item1]=main;
                var effect=env.DataManager.EnemySkillEffectDict["SE_Enemy_BattleFlow"];effect.SkillEffectID=entry.Item1*100+1;
                env.DataManager.EnemyAIRuleDict[BattleFlowTestEnv.EnemyID][0].SkillID=entry.Item1;
                env.CreateMinimalBattle();var random=new SequenceBattleRandomSource();BattleRandom.SetSource(random);
                var events=new List<string>();long started=0,completed=0;int hits=0;
                using var a=BattleEventHub.Subscribe<ActionEventData>(BattleEventKind.ActionStarted,(h,f)=>
                { Assert.That(f.ActionType,Is.EqualTo("EnemySkill"));Assert.That(f.SkillId,Is.EqualTo(entry.Item2)); started=h.ActionExecutionId;events.Add("start"); },0);
                using var b=BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved,(h,f)=>
                { Assert.That(h.ActionExecutionId,Is.EqualTo(started));Assert.That(f.Source.SourceSkillID,Is.EqualTo(entry.Item2));hits++;events.Add("hit"); },0);
                using var c=BattleEventHub.Subscribe<ActionEventData>(BattleEventKind.ActionCompleted,(h,f)=>
                { completed=h.ActionExecutionId;events.Add("complete"); },0);
                await BattleExecution.Run(env.Enemy.TakeEnemyActionAsync);
                Assert.That(events,Is.EqualTo(new[]{"start","hit","complete"}));Assert.That(hits,Is.EqualTo(1));
                Assert.That(started,Is.Not.Zero.And.Not.EqualTo(previous));Assert.That(completed,Is.EqualTo(started));previous=started;
                Assert.That(random.IntCallCount,Is.EqualTo(1));Assert.That(random.FloatCallCount,Is.Zero);
            }
        }

        [UnityTest]
        public IEnumerator NoUsableSkillHasNoAction_InterruptedStartHasNoCompletionOrDamage()
            => HookTestEnv.AwaitTask(InterruptedAsync);
        private async Task InterruptedAsync()
        {
            using var env=new BattleFlowTestEnv();env.ConfigureEnemyDamageSkill();env.CreateMinimalBattle();
            int starts=0,ends=0,hits=0;BattleRandom.SetSource(new SequenceBattleRandomSource());
            using var a=BattleEventHub.Subscribe<ActionEventData>(BattleEventKind.ActionStarted,(_,f)=>
            { starts++;env.Enemy.Entity.CurrentHP=0; },0);
            using var b=BattleEventHub.Subscribe<ActionEventData>(BattleEventKind.ActionCompleted,(_,f)=>ends++,0);
            using var c=BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved,(_,f)=>hits++,0);
            var skill=env.DataManager.EnemySkillMainDict[990201];skill.UsePerTurn=0;
            env.DataManager.EnemySkillMainDict.Clear();
            await BattleExecution.Run(env.Enemy.TakeEnemyActionAsync);Assert.That(starts,Is.Zero);
            env.DataManager.EnemySkillMainDict[990201]=skill;
            await BattleExecution.Run(env.Enemy.TakeEnemyActionAsync);
            Assert.That(starts,Is.EqualTo(1));Assert.That(ends,Is.Zero);Assert.That(hits,Is.Zero);
        }

        [UnityTest]
        public IEnumerator NestedActionIdentity_IsIndependentFromSharedQuota_AndRestoresAfterAwaitAndException()
            => HookTestEnv.AwaitTask(NestedAsync);
        private async Task NestedAsync()
        {
            using var env=new BattleFlowTestEnv();env.CreateMinimalBattle();
            BattleDeclaration Fact()=>BattleEventHub.Record(null,new ActionEventData(env.Enemy.Entity,"sameSkill","EnemySkill",false));
            using(BattleResolutionContext.EnterAction(new BattleResolutionContext.CounterScope()))
            using(BattleEventHub.BeginActionExecution())
            {
                var first=Fact();Assert.That(first.Header.ActionExecutionId,Is.Not.Zero);
                try
                {
                    using(BattleResolutionContext.EnterAction(new BattleResolutionContext.CounterScope()))
                    using(BattleEventHub.BeginActionExecution())
                    {
                        var child=Fact();Assert.That(child.Header.ActionScopeId,Is.EqualTo(first.Header.ActionScopeId));
                        Assert.That(child.Header.ActionExecutionId,Is.Not.EqualTo(first.Header.ActionExecutionId));
                        await Task.Yield();Assert.That(Fact().Header.ActionExecutionId,Is.EqualTo(child.Header.ActionExecutionId));
                        throw new InvalidOperationException("nested test");
                    }
                }
                catch(InvalidOperationException) { }
                Assert.That(Fact().Header.ActionExecutionId,Is.EqualTo(first.Header.ActionExecutionId));
                using(BattleEventHub.BeginActionExecution(false)) Assert.That(Fact().Header.ActionExecutionId,Is.Zero);
                using(BattleEventHub.Enter(first))Assert.That(Fact().Header.ActionExecutionId,Is.EqualTo(first.Header.ActionExecutionId));
                Assert.That(Fact().Header.ActionExecutionId,Is.EqualTo(first.Header.ActionExecutionId));
            }
            Assert.That(Fact().Header.ActionExecutionId,Is.Zero);
        }

        [Test]
        public void LethalEnemyAction_CompletesBeforeTheDeferredBattleEndCheck()
        {
            using var env=new BattleFlowTestEnv();env.ConfigureEnemyDamageSkill(20f);env.CreateMinimalBattle();
            int completed=0;var order=new List<string>();
            using var listener=BattleEventHub.Subscribe<ActionEventData>(BattleEventKind.ActionCompleted,(_,f)=>
            { completed++;Assert.That(env.Manager.IsBattleOver,Is.False);order.Add("complete"); },0);
            using var ended=BattleEventHub.Subscribe<BattleEndedEventData>(BattleEventKind.BattleEnded,(_,f)=>order.Add("battleEnd"),0);
            env.Manager.StartBattle();env.Flow.AdvanceToPhase(env.Manager,TurnPhase.AllyAction);
            BattleRandom.SetSource(new SystemBattleRandomSource(10132001));
            env.Manager.EndAllyTurn();env.Flow.AdvanceUntil(env.Manager,()=>env.Manager.IsBattleOver,"enemy lethal action");
            // 新规则：HP立即归零，但完整行为及复活窗口结清后才确定胜负，不再将全灭本身视为中断。
            Assert.That(completed,Is.EqualTo(1));
            Assert.That(order,Is.EqualTo(new[]{"complete","battleEnd"}));
        }
    }
}
