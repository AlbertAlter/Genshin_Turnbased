using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class BarbaraRevivalTests
    {
        private static StatusInstance Install(BattleFlowTestEnv env)
        {
            var owner = env.Ally.Entity;
            owner.EntityID = 1014; owner.ConstellationLevel = 6; owner.MaxEnergy = 80; owner.CurrentEnergy = 0;
            var main = new StatusMainData { StatusID = 4101406, StatusID2 = BarbaraRevivalHooks.StatusID, MaxStack = 1, WhenMax = "0" };
            env.DataManager.StatusMainDict[main.StatusID2] = main;
            env.DataManager.StatusActionDict[main.StatusID2] = new List<StatusActionData>
            { new StatusActionData { StatusID = main.StatusID, StatusID2 = main.StatusID2, ActionType = "OnTrigger",
                Param1 = "STE_Barbara_C6Revive", ScriptHook = "Barbara_C6()", ExecuteCount = 1 } };
            env.DataManager.StatusEffectDict["STE_Barbara_C6Revive"] = new StatusEffectData
            { StatusEffectID = 410140601, StatusEffectID2 = "STE_Barbara_C6Revive", EffectType = "Revive",
                Param1 = "1*Target.TotalHP", TargetType = "AlliesOnly", TargetOverride = "Barbara_C6Target()" };
            return owner.AddStatus(main.StatusID2, owner, 0, 0, 0, main);
        }

        private static void Kill(BattleFlowTestEnv env, params BattleEntity[] targets)
        {
            using var batch = new HitBatch();
            for (int i = 0; i < targets.Length; i++)
                batch.Resolve(new HitRequest { Target = targets[i], DamageBeforeShield = 100000,
                    Element = "Physical", DamageType = "Skill", TargetIndex = i,
                    Source = DamageSourceInfo.Create(env.Enemy.Entity, ReactionSourceKind.EnemySkill,
                        "SK_Enemy_BattleFlow", "SE_Enemy_BattleFlow") });
        }

        [UnityTest]
        public IEnumerator EarliestDeathWins_AndReapplyingControllerDoesNotResetBattleBudget()
            => HookTestEnv.AwaitTask(EarliestAsync);
        private async Task EarliestAsync()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle(); Install(env);
            var first = env.CreateAlly(2).Entity; var later = env.CreateAlly(3).Entity;
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            int heals = 0, revives = 0;
            using var h = BattleEventHub.Subscribe<HealEventData>(BattleEventKind.HealResolved, (_, f) => heals++, 0);
            using var r = BattleEventHub.Subscribe<HPChangedEventData>(BattleEventKind.HPChanged, (_, f) =>
            { if (f.Reason == "Revive") { Assert.That(f.Before, Is.Zero); revives++; } }, 0);
            await BattleExecution.Run(() => BattleDeathResolution.RunAsync(async () =>
            {
                Kill(env, first); await BattleExecution.Flush();
                Assert.That(first.IsDead, Is.True, "死亡立即成立，不能在第一段后立即复活");
                Kill(env, later); await BattleExecution.Flush();
                Assert.That(first.IsDead && later.IsDead, Is.True);
            }));
            Assert.That(first.CurrentHP, Is.EqualTo(first.GetFinalHP())); Assert.That(later.IsDead, Is.True);
            Assert.That(heals, Is.Zero); Assert.That(revives, Is.EqualTo(1)); Assert.That(random.IntCallCount, Is.Zero);
            env.Ally.Entity.RemoveStatus(BarbaraRevivalHooks.StatusID); Install(env);
            await BattleExecution.Run(() => BattleDeathResolution.RunAsync(async () => { Kill(env, first); await BattleExecution.Flush(); }));
            Assert.That(first.IsDead, Is.True, "同场替换状态不能再获得一次六命");
            Assert.That(revives, Is.EqualTo(1)); Assert.That(random.FloatCallCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator SimultaneousDeaths_UseOneUniformRandomDraw_NotTargetIterationOrder()
            => HookTestEnv.AwaitTask(SimultaneousAsync);
        private async Task SimultaneousAsync()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle(); Install(env);
            var left = env.CreateAlly(2).Entity; var right = env.CreateAlly(3).Entity;
            var random = new SequenceBattleRandomSource(intValues: new[] { 1 }); BattleRandom.SetSource(random);
            await BattleExecution.Run(() => BattleDeathResolution.RunAsync(async () =>
            { Kill(env, right, left); await BattleExecution.Flush(); Assert.That(left.IsDead && right.IsDead, Is.True); }));
            Assert.That(left.IsDead, Is.True); Assert.That(right.CurrentHP, Is.EqualTo(right.GetFinalHP()));
            Assert.That(random.IntCallCount, Is.EqualTo(1)); Assert.That(random.FloatCallCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator RevivedThenKilledAgain_DoesNotReuseItsPreviousDeathPriority()
            => HookTestEnv.AwaitTask(RepeatedDeathAsync);
        private async Task RepeatedDeathAsync()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle(); Install(env);
            var repeated = env.CreateAlly(2).Entity; var firstCurrentDeath = env.CreateAlly(3).Entity;
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            await BattleExecution.Run(() => BattleDeathResolution.RunAsync(async () =>
            {
                Kill(env, repeated); await BattleExecution.Flush();
                ReviveEffect.Execute("1*Target.TotalHP", env.Ally.Entity, new[] { repeated });
                await BattleExecution.Flush();
                Kill(env, firstCurrentDeath); await BattleExecution.Flush();
                Kill(env, repeated); await BattleExecution.Flush();
            }));
            Assert.That(firstCurrentDeath.IsAlive, Is.True, "旧的一次死亡已由别处复活，不能借旧优先级插队");
            Assert.That(repeated.IsDead, Is.True); Assert.That(random.IntCallCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator OwnerRevivedInSameWindow_DoesNotKeepCandidatesFromBeforeItsDeath()
            => HookTestEnv.AwaitTask(OwnerRevivedAsync);
        private async Task OwnerRevivedAsync()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle(); Install(env);
            var old = env.CreateAlly(2).Entity; var next = env.CreateAlly(3).Entity;
            await BattleExecution.Run(() => BattleDeathResolution.RunAsync(async () =>
            {
                Kill(env, old); await BattleExecution.Flush();
                Kill(env, env.Ally.Entity); await BattleExecution.Flush();
                ReviveEffect.Execute("1*Target.TotalHP", next, new[] { env.Ally.Entity });
                await BattleExecution.Flush();
                Kill(env, next); await BattleExecution.Flush();
            }));
            Assert.That(old.IsDead, Is.True, "本人曾倒下，旧请求不得在她复活后恢复");
            Assert.That(next.IsAlive, Is.True);
        }

        [UnityTest]
        public IEnumerator OwnerDyingLaterInSameWindow_CancelsPreviouslyRecordedRevival()
            => HookTestEnv.AwaitTask(OwnerDiesAsync);
        private async Task OwnerDiesAsync()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle(); Install(env);
            var target = env.CreateAlly(2).Entity;
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            await BattleExecution.Run(() => BattleDeathResolution.RunAsync(async () =>
            {
                Kill(env, target); await BattleExecution.Flush();
                Kill(env, env.Ally.Entity); await BattleExecution.Flush();
                Assert.That(env.Manager.IsBattleOver, Is.False, "窗口尚未结清，不提前确定败局");
            }));
            Assert.That(target.IsDead, Is.True); Assert.That(env.Ally.Entity.IsDead, Is.True);
            Assert.That(env.Manager.IsBattleOver, Is.True); Assert.That(env.Manager.Victory, Is.False);
            Assert.That(random.IntCallCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator RealEnemyMultihit_RevivesAfterCompleted_NotBetweenHits()
            => HookTestEnv.AwaitTask(EnemyActionAsync);
        private async Task EnemyActionAsync()
        {
            using var env = new BattleFlowTestEnv(); env.ConfigureEnemyDamageSkill(20f);
            env.DataManager.EnemySkillParamDict["SE_Enemy_BattleFlow"].Hits2 = "20,0";
            var effect = env.DataManager.EnemySkillEffectDict["SE_Enemy_BattleFlow"];
            effect.TargetNumber = -1; effect.TargetConsecutive = 2;
            env.CreateMinimalBattle(); Install(env);
            env.Ally.Entity.TotalHP = env.Ally.Entity.CurrentHP = 1000000;
            var target = env.CreateAlly(2).Entity;
            var sequence = new List<string>();
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            using var end = BattleEventHub.Subscribe<ActionEventData>(BattleEventKind.ActionCompleted, (_, f) =>
            { Assert.That(target.IsDead, Is.True); sequence.Add("complete"); }, 0);
            using var revived = BattleEventHub.Subscribe<HPChangedEventData>(BattleEventKind.HPChanged, (_, f) =>
            { if (f.Reason == "Revive") sequence.Add("revive"); }, 0);
            await BattleExecution.Run(env.Enemy.TakeEnemyActionAsync);
            Assert.That(target.CurrentHP, Is.EqualTo(target.GetFinalHP()));
            Assert.That(sequence, Is.EqualTo(new[] { "complete", "revive" }));
            Assert.That(random.IntCallCount, Is.EqualTo(1), "仅AI选技能一次，没有额外复活随机");
        }

        [UnityTest]
        public IEnumerator IndependentStatus_AllActionRowsAndTheirEffectsFinishBeforeRevival()
            => HookTestEnv.AwaitTask(StatusChainAsync);
        private async Task StatusChainAsync()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle(); Install(env);
            var target = env.CreateAlly(2).Entity;
            var main = new StatusMainData { StatusID = 4999901, StatusID2 = "ST_ReviveChain", MaxStack = 1, WhenMax = "0" };
            env.DataManager.StatusMainDict[main.StatusID2] = main;
            var actions = new List<StatusActionData>
            {
                new StatusActionData { StatusID2 = main.StatusID2, ActionType = "OnItsTurn", Param1 = "STE_ReviveChainDamage;STE_ReviveChainEnergy1" },
                new StatusActionData { StatusID2 = main.StatusID2, ActionType = "OnItsTurn", Param1 = "STE_ReviveChainEnergy2" }
            };
            env.DataManager.StatusEffectDict["STE_ReviveChainDamage"] = new StatusEffectData
            { StatusEffectID = 499990101, StatusEffectID2 = "STE_ReviveChainDamage", EffectType = "Damage", Element = "True",
                Param1 = "100000", TargetType = "AlliesOnly", TargetSelect = "-1" };
            for (int i = 1; i <= 2; i++)
                env.DataManager.StatusEffectDict["STE_ReviveChainEnergy" + i] = new StatusEffectData
                { StatusEffectID = 499990101 + i, StatusEffectID2 = "STE_ReviveChainEnergy" + i, EffectType = "GainEnergy",
                    Param1 = i.ToString(), Param2 = "Flat", TargetType = "Self" };
            var status = env.Ally.Entity.AddStatus(main.StatusID2, env.Ally.Entity, 0, 0, 0, main);
            int payouts = 0;
            using var energy = BattleEventHub.Subscribe<ResourceEventData>(BattleEventKind.ResourceChanged, (_, f) =>
            { if (f.Resource == "Energy") { payouts++; Assert.That(target.IsDead, Is.True, "整个状态而非单条效果结束才复活"); } }, 0);
            await BattleExecution.Run(() => env.Ally.ExecuteStatusActionsTask(status, actions, env.Ally.Entity, "OnItsTurn"));
            Assert.That(payouts, Is.EqualTo(2)); Assert.That(target.IsAlive, Is.True);
            Assert.That(env.Ally.Entity.CurrentEnergy, Is.EqualTo(3f));
        }

        [UnityTest]
        public IEnumerator DirectDamage_UsesFallbackWindow_AndDoesNotRestoreRemovedEquipmentStatus()
            => HookTestEnv.AwaitTask(DirectDamageAsync);
        private async Task DirectDamageAsync()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle(); Install(env);
            var target = env.CreateAlly(2).Entity;
            var gear = new StatusMainData { StatusID = 4999902, StatusID2 = "ST_Weapon_ReviveProbe", MaxStack = 1, WhenMax = "0" };
            env.DataManager.StatusMainDict[gear.StatusID2] = gear;
            target.AddStatus(gear.StatusID2, target, 0, 0, 0, gear);
            await BattleExecution.Run(() =>
            {
                target.TakeDamage(100000, DamageSourceInfo.Create(env.Enemy.Entity, ReactionSourceKind.EnemySkill,
                    "SK_Enemy_BattleFlow", "SE_Enemy_BattleFlow"));
                return Task.CompletedTask;
            });
            Assert.That(target.IsAlive, Is.True);
            Assert.That(target.HasStatus(gear.StatusID2), Is.False);
        }

        [TestCase("0")]
        [TestCase("-1")]
        [TestCase("NaN")]
        [TestCase("Infinity")]
        [TestCase("1*TargetTotalHP")]
        public void Revive_InvalidAmountIsRejected(string value)
            => Assert.Throws<FormatException>(() => ReviveEffect.ParseAmount(value, out _));

        [UnityTest]
        public IEnumerator CancelledWindow_DoesNotReviveOrLeakOldCandidatesIntoNextWindow()
            => HookTestEnv.AwaitTask(CancelledAsync);
        private async Task CancelledAsync()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle(); Install(env);
            var old = env.CreateAlly(2).Entity; var next = env.CreateAlly(3).Entity;
            try
            {
                await BattleExecution.Run(() => BattleDeathResolution.RunAsync(async () =>
                { Kill(env, old); await BattleExecution.Flush(); BattleExecution.Cancel(); }));
                Assert.Fail("Expected cancellation");
            }
            catch (OperationCanceledException) { }
            Assert.That(old.IsDead, Is.True);
            await BattleExecution.Run(() => BattleDeathResolution.RunAsync(async () => { Kill(env, next); await BattleExecution.Flush(); }));
            Assert.That(old.IsDead, Is.True); Assert.That(next.IsAlive, Is.True);
        }
    }
}
