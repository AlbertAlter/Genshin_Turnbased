using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class ZhongliC6HealingTests
    {
        private const string Heal = "STE_Zhongli_C6Heal";
        private static StatusInstance Setup(BattleFlowTestEnv env)
        {
            var owner = env.Ally.Entity; owner.EntityID = 1032; owner.ConstellationLevel = 6;
            env.DataManager.StatusMainDict[ZhongliC6Healing.StatusID] = new StatusMainData
            { StatusID = 4103209, StatusID2 = ZhongliC6Healing.StatusID, Display = 0, MaxStack = 1 };
            env.DataManager.StatusActionDict[ZhongliC6Healing.StatusID] = new List<StatusActionData>
            { new StatusActionData { StatusID = 4103209, StatusID2 = ZhongliC6Healing.StatusID,
                ActionType = "OnTrigger", ScriptHook = ZhongliC6Healing.Trigger, Param1 = Heal, ExecuteCount = 1,
                MaxTimePerAction = 1 } };
            env.DataManager.StatusEffectDict[Heal] = new StatusEffectData
            { StatusEffectID = 410320901, StatusEffectID2 = Heal, EffectType = "Heal", TargetType = "Self",
                DamageHook = ZhongliC6Healing.HealHook, ExecuteCount = 1 };
            env.DataManager.CharacterStatusIDs[1032] = new HashSet<string>
                { ZhongliC6Healing.StatusID, ZhongliC6Healing.JadeShieldID };
            return owner.AddStatus(ZhongliC6Healing.StatusID, owner, 0, 2, 0,
                env.DataManager.StatusMainDict[ZhongliC6Healing.StatusID]);
        }
        private static StatusInstance Shield(BattleFlowTestEnv env, float value)
        {
            var owner = env.Ally.Entity;
            var main = new StatusMainData { StatusID2 = ZhongliC6Healing.JadeShieldID, StatusType = "OverallShield", MaxStack = 1 };
            env.DataManager.StatusMainDict[main.StatusID2] = main;
            var state = owner.AddStatus(main.StatusID2, owner, 5, 2, 0, main);
            owner.AddShield(value, "None", 5, sourceStatus: state);
            return state;
        }
        private static void Hit(BattleFlowTestEnv env, BattleEntity target, float amount, string element = "Physical")
        {
            using var batch = new HitBatch();
            batch.Resolve(new HitRequest { Target = target, DamageBeforeShield = amount, Element = element,
                Source = DamageSourceInfo.Create(env.Enemy.Entity, ReactionSourceKind.EnemySkill, "SK_Test", "SE_Test", ReactionType.None) });
        }
        private static void Start(BattleFlowTestEnv env) => BattleEventHub.Publish(new ActionEventData(env.Enemy.Entity, "SK_Test", "EnemySkill", false));
        private static void End(BattleFlowTestEnv env) => BattleEventHub.Publish(new ActionEventData(env.Enemy.Entity, "SK_Test", "EnemySkill", true));

        [Test] public void BrokenShield_LeavesPendingRecordsOnHiddenState_HealsEachHitAfterAction()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            var listener = Setup(env); var target = env.CreateAlly(2).Entity; target.CurrentHP = 500;
            var shield = Shield(env, 150); int heals = 0;
            using var subscription = BattleEventHub.Subscribe<HealEventData>(BattleEventKind.HealResolved,
                (_, __) => heals++, 0);
            BattleExecution.Run(async () =>
            {
                using var action = BattleEventHub.BeginActionExecution(); Start(env);
                Hit(env, target, 100); await BattleExecution.Flush();
                Assert.That(target.CurrentHP, Is.EqualTo(500)); Assert.That(heals, Is.Zero);
                Hit(env, target, 100); await BattleExecution.Flush();
                Assert.That(shield.IsActive, Is.False); Assert.That(listener.IsActive, Is.True);
                Assert.That(ZhongliC6Healing.PendingRecordCount(listener), Is.EqualTo(2));
                Assert.That(target.CurrentHP, Is.EqualTo(450)); Assert.That(heals, Is.Zero);
                End(env); await BattleExecution.Flush();
                Assert.That(target.CurrentHP, Is.EqualTo(530)); Assert.That(heals, Is.EqualTo(2));
                Assert.That(ZhongliC6Healing.PendingRecordCount(listener), Is.Zero);
                End(env); await BattleExecution.Flush(); Assert.That(heals, Is.EqualTo(2));
            }).GetAwaiter().GetResult();
        }

        [Test] public void DeathAndReinitialization_CannotCarryOldRequestsToNewListener()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            var listener = Setup(env); var target = env.CreateAlly(2).Entity; target.CurrentHP = 500; Shield(env, 50);
            BattleExecution.Run(async () =>
            {
                using var action = BattleEventHub.BeginActionExecution(); Start(env);
                Hit(env, target, 100); await BattleExecution.Flush();
                Assert.That(ZhongliC6Healing.PendingRecordCount(listener), Is.EqualTo(1));
                Hit(env, env.Ally.Entity, 10000, "True"); await BattleExecution.Flush();
                Assert.That(listener.IsActive, Is.False); Assert.That(listener.ScriptRuntimeState, Is.Null);
                env.Ally.Entity.CurrentHP = 500; var replacement = Setup(env);
                End(env); await BattleExecution.Flush();
                Assert.That(target.CurrentHP, Is.EqualTo(450));
                Assert.That(ZhongliC6Healing.PendingRecordCount(replacement), Is.Zero);
            }).GetAwaiter().GetResult();
        }

        [Test] public void PerHitCapAndActionIdentity_AreIndependentOfShieldReplacement()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            var listener = Setup(env); var target = env.CreateAlly(2).Entity; target.CurrentHP = 200;
            env.Ally.Entity.HealBonus = 1; Shield(env, 500);
            BattleExecution.Run(async () =>
            {
                using var outer = BattleEventHub.BeginActionExecution(); Start(env);
                Hit(env, target, 300); await BattleExecution.Flush(); Shield(env, 500);
                using (BattleEventHub.BeginActionExecution()) { Start(env); End(env); await BattleExecution.Flush(); }
                Assert.That(target.CurrentHP, Is.EqualTo(200));
                End(env); await BattleExecution.Flush();
                Assert.That(target.CurrentHP, Is.EqualTo(280), "治疗加成后仍不超过该hit的8%上限");
                Assert.That(ZhongliC6Healing.PendingRecordCount(listener), Is.Zero);
            }).GetAwaiter().GetResult();
        }

        [Test] public void TrueBypassAndUnprotectedHits_DoNotGenerateHealing()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            var listener = Setup(env); var target = env.CreateAlly(2).Entity; target.CurrentHP = 500;
            var shield = Shield(env, 500);
            BattleExecution.Run(async () =>
            {
                using var action = BattleEventHub.BeginActionExecution(); Start(env);
                Hit(env, target, 100, "True"); await BattleExecution.Flush();
                env.Ally.Entity.RemoveStatusInstance(shield);
                Hit(env, target, 100); await BattleExecution.Flush(); End(env); await BattleExecution.Flush();
                Assert.That(target.CurrentHP, Is.EqualTo(300));
                Assert.That(ZhongliC6Healing.PendingRecordCount(listener), Is.Zero);
            }).GetAwaiter().GetResult();
        }
    }
}
