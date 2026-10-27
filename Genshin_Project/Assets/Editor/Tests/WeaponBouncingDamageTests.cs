using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class WeaponBouncingDamageTests
    {
        private const string Status = "ST_Weapon_4313701", Effect = "STE_Weapon_431370101";
        private static void Configure(BattleFlowTestEnv env)
        {
            env.ConfigureAllyAction(0, "SK_Eye", "SE_Eye", "Physical", ".01*TotalATK,0", 0);
            env.DataManager.StatusMainDict[Status] = new StatusMainData { StatusID = 4313701, StatusID2 = Status,
                StatusType = "Buff", MaxStack = 1, MaxCount = 1, WhenMax = "0" };
            env.DataManager.StatusActionDict[Status] = new List<StatusActionData> { new StatusActionData {
                StatusID = 4313701, StatusID2 = Status, ActionType = "OnTrigger", Param1 = Effect,
                ScriptHook = "PostSelfDamage(4313701);Chance(0.5)", OutgoingHit = "Normal;Heavy", MaxTimePerEffect = 1, Cooldown = 2 } };
            env.DataManager.StatusEffectDict[Effect] = new StatusEffectData { StatusEffectID = 431370101, StatusEffectID2 = Effect,
                EffectType = "Damage", Element = "Physical", Param1 = "Index(1)*TotalATK,0", TargetType = "Enemy",
                TargetSelect = "1", TargetConsecutive = 4, TargetOverride = "PostSelfDamage(4313701)", DamageHook = "Hook(431370101)" };
            env.CreateMinimalBattle(); var owner = env.Ally.Entity;
            var weapon = new WeaponRuntimeContext { WeaponID = 3137, Equipper = owner, Refinement = 1, SourceInstanceID = "Eye:1" };
            weapon.ResolvedParams[1] = 2.4f;
            owner.AddStatus(Status, owner, 0, 2, 0, env.DataManager.StatusMainDict[Status], weapon);
            BattleRandom.SetSource(new SequenceBattleRandomSource()); env.Manager.StartBattle();
            env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
        }

        [Test, Timeout(7000)]
        public void Eye_SingleEnemyCanReceiveAllFiveSegments_WithDistinctHitsAndOneExecution()
        {
            using var env = new BattleFlowTestEnv(); Configure(env);
            var damage = new List<DamageEventData>(); var executions = new List<long>(); var hits = new List<long>();
            using var observer = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (header, fact) =>
            { if (fact.Source.SourceEffectID == Effect) { damage.Add(fact); executions.Add(header.EffectExecutionId); hits.Add(header.HitId); } }, 0);
            var random = new SequenceBattleRandomSource(new[] { .9f, .1f, .9f, .9f, .9f, .9f, .9f }); BattleRandom.SetSource(random);
            Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True);
            Assert.That(damage, Has.Count.EqualTo(5)); Assert.That(damage.Select(x => x.Subject), Is.All.SameAs(env.Enemy.Entity));
            Assert.That(damage.Select(x => x.DamageBeforeShield), Is.All.EqualTo(120f).Within(.001f));
            Assert.That(damage.Select(x => x.ElementAmount), Is.All.EqualTo(0f));
            Assert.That(damage.All(x => string.IsNullOrEmpty(x.DamageType)), Is.True);
            Assert.That(executions.Distinct().Count(), Is.EqualTo(1)); Assert.That(hits.Distinct().Count(), Is.EqualTo(5));
            Assert.That(random.FloatCallCount, Is.EqualTo(7)); Assert.That(random.IntCallCount, Is.Zero);
        }

        [Test, Timeout(7000)]
        public void Eye_RebuildsCandidatePoolAfterEachDeath_DoesNotHitCorpseOrPreselectFiveTargets()
        {
            using var env = new BattleFlowTestEnv(); Configure(env); env.CreateEnemy(2); env.CreateEnemy(3);
            foreach (var enemy in env.Manager.Enemies) enemy.Entity.CurrentHP = 60f;
            var slots = new List<int>();
            using var observer = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, fact) =>
            { if (fact.Source.SourceEffectID == Effect) slots.Add(fact.Subject.SlotPosition); }, 0);
            var random = new SequenceBattleRandomSource(new[] { .9f, .1f, .9f, .9f, .9f }, new[] { 1 }); BattleRandom.SetSource(random);
            Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True);
            Assert.That(slots, Is.EqualTo(new[] { 1, 3, 2 })); Assert.That(env.Manager.IsBattleOver, Is.True);
            Assert.That(random.FloatCallCount, Is.EqualTo(5)); Assert.That(random.IntCallCount, Is.EqualTo(1));
        }

        [Test, Timeout(7000)]
        public void Eye_SourceDeathAfterFirstSegmentCancelsRemainingBounces()
        {
            using var env = new BattleFlowTestEnv(); Configure(env); env.CreateAlly(2);
            int hits = 0;
            using var observer = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, fact) =>
            {
                if (fact.Source.SourceEffectID != Effect) return;
                hits++; env.Ally.Entity.TakeDamage(2000f, DamageSourceInfo.CreateUnknown());
            }, 0);
            BattleRandom.SetSource(new SequenceBattleRandomSource(new[] { .9f, .1f, .9f }));
            Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True); Assert.That(hits, Is.EqualTo(1));
            Assert.That(env.Ally.Entity.HasStatus(Status), Is.False);
        }

        [Test, Timeout(7000)]
        public void Eye_FailedChanceDoesNotRerollForExecuteCountRepeats()
        {
            using var env = new BattleFlowTestEnv(); Configure(env); env.DataManager.SkillEffectDict["SE_Eye"].ExecuteCount = 2;
            int hits = 0;
            using var observer = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, fact) =>
            { if (fact.Source.SourceEffectID == Effect) hits++; }, 0);
            var random = new SequenceBattleRandomSource(new[] { .9f, .8f, .9f }); BattleRandom.SetSource(random);
            Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True); Assert.That(hits, Is.Zero);
            Assert.That(random.FloatCallCount, Is.EqualTo(3)); Assert.That(random.IntCallCount, Is.Zero);
        }
    }
}
