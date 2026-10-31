using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class EnemyFifthTenIntegrationTests
    {
        [Test]
        public void HealingEligibility_IsPerCaster_AndIncludesOffFieldExistingBubbles()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            var caster = env.Enemy.Entity;
            Assert.That(Enemy2041To2050Hooks.CanCreateHealingBubble(env.Manager, caster), Is.False);
            var other = env.CreateEnemy(2).Entity;
            var recipient = env.CreateEnemy(3).Entity;
            var main = new StatusMainData { StatusID = 4204105, StatusID2 = Enemy2041To2050Hooks.HealingBubble, MaxStack = 0 };
            env.DataManager.StatusMainDict[main.StatusID2] = main;
            recipient.AddStatus(main.StatusID2, other, 3, 6, 6, main);
            Assert.That(Enemy2041To2050Hooks.CanCreateHealingBubble(env.Manager, caster), Is.True);
            var own = recipient.AddStatus(main.StatusID2, caster, 3, 6, 6, main);
            Assert.That(recipient.GetStatusInstances(main.StatusID2), Has.Count.EqualTo(2));
            Assert.That(Enemy2041To2050Hooks.CanCreateHealingBubble(env.Manager, caster), Is.False);
            env.ConfigureStatus(PublicControlStates.OffField);
            recipient.AddStatus(PublicControlStates.OffField, recipient, 0, 5, 0, env.DataManager.StatusMainDict[PublicControlStates.OffField]);
            Assert.That(Enemy2041To2050Hooks.CanCreateHealingBubble(env.Manager, caster), Is.False);
            recipient.RemoveStatusInstance(own);
            Assert.That(Enemy2041To2050Hooks.CanCreateHealingBubble(env.Manager, caster), Is.True);
        }

        [TestCase(2041, 204104, "ST_Hydrogunner_Unlocked")]
        [TestCase(2042, 204204, "ST_Pyroslinger_Unlocked")]
        public void NewSkirmisherArmor_UsesItsOwnUnlockAndCooldown(int enemy, int skill, string unlocked)
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            var caster = env.Enemy.Entity; caster.EntityID = enemy;
            env.DataManager.EnemySkillMainDict[skill] = new EnemySkillMainData { EnemySkillID = skill, EnemySkillID2 = "SK_TestArmor", SkillTags = new List<string> { "Armor" } };
            env.DataManager.EnemyAIRuleDict[env.Enemy.EnemyID] = new List<EnemyAIRuleData> { new EnemyAIRuleData { SkillID = skill } };
            env.ConfigureStatus(unlocked);
            caster.AddStatus(unlocked, caster, 0, 5, 0, env.DataManager.StatusMainDict[unlocked]);
            Assert.That(Enemy2031To2040Hooks.ArmorReady(caster), Is.True);
            Assert.That(env.Enemy.ChangeSkillCooldown("Tag(Armor)", 3), Is.True);
            Assert.That(Enemy2031To2040Hooks.ArmorReady(caster), Is.False);
            Assert.That(ScriptHookEvaluator.Evaluate("FatuiNormalReady()", caster), Is.True);
        }

        [UnityTest]
        public IEnumerator HealingOnAnotherUnit_ContinuesAfterSourceDies_UsesSourceMaximumHP()
            => HookTestEnv.AwaitTask(HealingAfterDeathAsync);
        private async Task HealingAfterDeathAsync()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            var source = env.Enemy.Entity;
            var target = env.CreateEnemy(2, 10000).Entity; target.CurrentHP = 1;
            var main = new StatusMainData { StatusID = 4204105, StatusID2 = Enemy2041To2050Hooks.HealingBubble, MaxStack = 0 };
            var action = new StatusActionData { StatusID2 = main.StatusID2, ActionType = "OnItsTurn", Param1 = "STE_TestBubbleHeal" };
            env.DataManager.StatusMainDict[main.StatusID2] = main;
            env.DataManager.StatusEffectDict[action.Param1] = new StatusEffectData {
                StatusEffectID = 420410501, StatusEffectID2 = action.Param1, EffectType = "Heal", Param1 = "0.1*TotalHP", TargetType = "Self" };
            var status = target.AddStatus(main.StatusID2, source, 3, 6, 6, main);
            float expected = 1 + source.GetFinalHP() * .1f;
            source.CurrentHP = 0;
            await BattleExecution.Run(async () => {
                env.Enemy.TryExecuteEventStatusAction(status, action, target, new ScriptHookContext());
                await BattleExecution.Flush();
            });
            Assert.That(target.CurrentHP, Is.EqualTo(expected).Within(.01f));
            Assert.That(target.HasStatusInstance(status), Is.True);
        }

        [UnityTest]
        public IEnumerator ChargedStatus_HitsOriginalTargetTwice_WithoutAnEnemySkillAction()
            => HookTestEnv.AwaitTask(ChargeAsync);
        private async Task ChargeAsync()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            env.CreateAlly(2);
            var main = new StatusMainData { StatusID = 4204301, StatusID2 = "ST_Crusher_Charge", MaxStack = 1 };
            env.DataManager.StatusMainDict[main.StatusID2] = main;
            var effects = new[] { "STE_TestCrusherFirst", "STE_TestCrusherSecond" };
            for (int i = 0; i < 2; i++)
                env.DataManager.StatusEffectDict[effects[i]] = new StatusEffectData {
                    StatusEffectID = 420430102 + i, StatusEffectID2 = effects[i], EffectType = "Damage", Element = "Physical",
                    Param1 = "2*ATK,0", TargetType = "Enemy", TargetSelect = "1", TargetConsecutive = i == 0 ? 1 : 0, TargetOverride = i == 0 ? null : "0,0" };
            var status = env.Enemy.Entity.AddStatus(main.StatusID2, env.Enemy.Entity, 1, 4, 0, main);
            var action = new StatusActionData { StatusID2 = main.StatusID2, ActionType = "OnEnd", Param1 = string.Join(";", effects) };
            var targets = new List<BattleEntity>(); int actions = 0;
            using var damage = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, hit) => targets.Add(hit.Subject), 0);
            using var started = BattleEventHub.Subscribe<ActionEventData>(BattleEventKind.ActionStarted, (_, hit) => actions++, 0);
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            await BattleExecution.Run(async () => {
                env.Enemy.TryExecuteEventStatusAction(status, action, env.Enemy.Entity, new ScriptHookContext());
                await BattleExecution.Flush();
            });
            Assert.That(targets, Has.Count.EqualTo(2));
            Assert.That(targets[1], Is.SameAs(targets[0]));
            Assert.That(actions, Is.Zero);
            Assert.That(random.FloatCallCount, Is.EqualTo(1), "仅第一击按受击权重选目标，第二击继承");
        }

        [Test]
        public void PotionTargets_KeepAdjacentEmptyFields_AndExcludeOffFieldOccupantsFromDamage()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            var result = BattleTargetResolver.Resolve(env.Manager, env.Enemy.Entity, env.Enemy.Entity,
                "EnemyField", 2, 2, null, null, null, null);
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Positions, Is.EqualTo(new[] { 1, 2 }));
            Assert.That(BattleTargetResolver.GetFields(env.Manager, result), Has.Count.EqualTo(2));
            Assert.That(BattleTargetResolver.GetEntities(env.Manager, result), Has.Count.EqualTo(1));
            env.ConfigureStatus(PublicControlStates.OffField);
            env.Ally.Entity.AddStatus(PublicControlStates.OffField, env.Ally.Entity, 0, 5, 0, env.DataManager.StatusMainDict[PublicControlStates.OffField]);
            Assert.That(BattleTargetResolver.GetEntities(env.Manager, result), Is.Empty);
            Assert.That(random.IntCallCount, Is.EqualTo(1));
        }
    }
}
