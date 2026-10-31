using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    /// <summary>SkillType查倍率、DamageType过滤属性、ActionType触发行为；只编写，未运行Unity。</summary>
    public sealed class DamageTypeSeparationTests
    {
        private static void Buff(BattleFlowTestEnv env, BattleEntity host, int id,
            string part, string type, string value)
        {
            string key = "ST_TypeSplit_" + id, effect = "STE_TypeSplit_" + id;
            var main = new StatusMainData { StatusID = id, StatusID2 = key, StatusType = "Buff",
                MultiplierPart = part, ApplyDamageType = type, MaxCount = 1, MaxStack = 1, WhenMax = "0" };
            env.DataManager.StatusMainDict[key] = main;
            env.DataManager.StatusEffectDict[effect] = new StatusEffectData
            { StatusEffectID = id * 100 + 1, StatusEffectID2 = effect, EffectType = "Buff", Param1 = value };
            host.AddStatus(key, host, 0, 2, 0, main);
        }

        [TestCase(0, "Heavy")]
        [TestCase(1, "Skill")]
        [TestCase(2, "Burst")]
        [TestCase(3, "Normal")]
        public void DefinitionQueries_UseDamageTag_NotLevelType(int levelType, string damageType)
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                var owner = env.Ally.Entity;
                Buff(env, owner, 900710, "DMGBonus", damageType, ".4");
                env.DataManager.SkillEffectDict["SE_TypeSplit_Query"] = new SkillEffectData
                { SkillEffectID2 = "SE_TypeSplit_Query", EffectType = "Damage", Element = "Physical", DamageType = damageType };
                Assert.That(owner.GetStatusDMGBonus("SE_TypeSplit_Query", "", levelType, "Physical", env.Enemy.Entity), Is.EqualTo(.4f));
                Assert.That(owner.GetStatusDMGBonus("SE_Unknown", "", levelType, "Physical", env.Enemy.Entity), Is.Zero);
                Assert.That(owner.GetStatusDMGBonus("", "", levelType, "Physical", env.Enemy.Entity), Is.Zero);
            }
        }

        [Test]
        public void RuntimeTag_OverridesSharedDefinition_BlankIsNotInferred_AndNestedExceptionRestoresCaller()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle(); var owner = env.Ally.Entity;
                Buff(env, owner, 900710, "DMGBonus", "Heavy", ".4");
                Buff(env, owner, 900711, "DMGBonus", "Normal", ".1");
                env.DataManager.StatusEffectDict["STE_TypeSplit_Query"] = new StatusEffectData
                { StatusEffectID2 = "STE_TypeSplit_Query", EffectType = "Damage", Element = "Physical", DamageType = "Normal" };
                using (DamageQueryContext.Enter("STE_TypeSplit_Query", "Heavy", "SK_Actual"))
                {
                    Assert.That(owner.GetStatusDMGBonus("STE_TypeSplit_Query", "", 0, "Physical", env.Enemy.Entity), Is.EqualTo(.4f));
                    Assert.That(DamageQueryContext.ResolveSkillID("STE_TypeSplit_Query", "SK_Wrong"), Is.EqualTo("SK_Actual"));
                    Assert.Throws<InvalidOperationException>(() =>
                    {
                        using (DamageQueryContext.Enter("STE_TypeSplit_Query", "", ""))
                        {
                            Assert.That(owner.GetStatusDMGBonus("STE_TypeSplit_Query", "", 1, "Physical", env.Enemy.Entity), Is.Zero);
                            Assert.That(DamageQueryContext.ResolveSkillID("STE_TypeSplit_Query", "SK_Wrong"), Is.Empty);
                            throw new InvalidOperationException("test nested cleanup");
                        }
                    });
                    Assert.That(owner.GetStatusDMGBonus("STE_TypeSplit_Query", "", 0, "Physical", env.Enemy.Entity), Is.EqualTo(.4f));
                }
                Assert.That(owner.GetStatusDMGBonus("STE_TypeSplit_Query", "", 1, "Physical", env.Enemy.Entity), Is.EqualTo(.1f));
            }
        }

        private static void EnergyListener(BattleFlowTestEnv env, int id, string onAction, string onHit, string energy)
        {
            string key = "ST_TypeSplit_" + id, effect = "STE_TypeSplit_" + id;
            var main = new StatusMainData { StatusID = id, StatusID2 = key, MaxStack = 1, WhenMax = "0" };
            env.DataManager.StatusMainDict[key] = main;
            env.DataManager.StatusEffectDict[effect] = new StatusEffectData
            { StatusEffectID = id * 100 + 1, StatusEffectID2 = effect, EffectType = "GainEnergy",
                Param1 = energy, Param2 = "Flat", TargetType = "Self" };
            env.DataManager.StatusActionDict[key] = new List<StatusActionData>
            { new StatusActionData { StatusID2 = key, ActionType = "OnTrigger", Param1 = effect,
                OnAction = onAction, OutgoingHit = onHit } };
            env.Ally.Entity.AddStatus(key, env.Ally.Entity, 0, 2, 0, main);
        }

        [Test, Timeout(8000)]
        public void SkillLevelTwo_HeavyRuntimeDamage_StillEmitsSkillAction_AndHeavyHitNotHeavyAction()
        {
            using (var env = new BattleFlowTestEnv())
            {
                const string skill = "SK_TypeSplit", effect = "SE_TypeSplit";
                env.ConfigureAllyAction(2, skill, effect, "Physical", "1*TotalATK,0", 0);
                env.DataManager.SkillEffectDict[effect].DamageType = "Normal"; // 共享定义刻意与实际克隆不一致。
                env.CreateMinimalBattle(); env.Manager.StartBattle();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
                var owner = env.Ally.Entity; owner.CurrentEnergy = 0f;
                Buff(env, owner, 900710, "DMGBonus", "Heavy", ".4");
                Buff(env, owner, 900711, "DMGBonus", "Normal;Skill", "9");
                Buff(env, owner, 900712, "CritRate", "Heavy", "1");
                EnergyListener(env, 900713, "Skill", null, "1");
                EnergyListener(env, 900714, "Heavy", null, "100");
                EnergyListener(env, 900715, null, "Heavy", "10");
                env.Ally.SetSkillEffects(skill, new List<SkillEffectData>
                { new SkillEffectData { SkillEffectID2 = effect, EffectType = "Damage", DamageType = "Heavy",
                    Element = "Physical", Param1 = "", TargetType = "Enemy", TargetNumber = 1,
                    TargetConsecutive = 1, TargetConsecutiveSet = true } });
                var actions = new List<string>(); int hits = 0;
                using var actionListener = BattleEventHub.Subscribe<ActionEventData>(BattleEventKind.ActionStarted,
                    (_, action) => actions.Add(action.ActionType), 0);
                using var hitListener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, hit) =>
                {
                    if (hit.Source.SourceEffectID != effect) return;
                    hits++;
                    Assert.That(hit.DamageType, Is.EqualTo("Heavy"));
                    Assert.That(owner.GetStatusDMGBonus(effect, skill, 2, "Physical", hit.Subject), Is.EqualTo(.4f));
                    Assert.That(hit.Critical?.IsCritical, Is.True);
                }, 0);
                Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
                Assert.That(actions, Is.EqualTo(new[] { "Skill" }));
                Assert.That(hits, Is.EqualTo(1));
                Assert.That(owner.CurrentEnergy, Is.EqualTo(11f));
                Assert.That(random.FloatCallCount, Is.EqualTo(1)); Assert.That(random.IntCallCount, Is.Zero);
            }
        }

        [Test]
        public void ReactionMastery_UsesActualDamageTag_WithoutConvertingItToLevelType()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle(); var owner = env.Ally.Entity; owner.TotalEM = 2f;
                Buff(env, owner, 900710, "EMBonus", "Heavy", "7");
                Buff(env, owner, 900711, "EMBonus", "Normal", "100");
                env.DataManager.SkillEffectDict["SE_Reaction_TypeSplit"] = new SkillEffectData
                { SkillEffectID2 = "SE_Reaction_TypeSplit", EffectType = "Damage", Element = "Pyro", DamageType = "Normal" };
                Assert.That(ReactionDamageCalculator.GetSourceMastery(new ReactionContext
                { SourceEntity = owner, Target = env.Enemy.Entity, SourceEffectID = "SE_Reaction_TypeSplit",
                    SourceSkillID = "SK_Reaction_TypeSplit", DamageType = "Heavy", AttackElement = "Pyro" }), Is.EqualTo(9f));
            }
        }

        [Test, Timeout(8000)]
        public void ReferencedNormalMultiplier_DoesNotTurnHeavyChildAttributesIntoNormal()
        {
            using (var env = new BattleFlowTestEnv())
            {
                const string referenced = "SE_Reference_Normal", effect = "SE_Reference_Heavy";
                env.ConfigureAllyAction(0, "SK_Reference_Normal", referenced, "Physical", "2*TotalATK,0", 0);
                env.ConfigureAllyAction(2, "SK_Reference_Heavy", effect, "Physical", null, 0,
                    param1: "%" + referenced + ".Hits1.M*0.5,0");
                env.DataManager.SkillEffectDict[effect].DamageType = "Heavy";
                env.CreateMinimalBattle(); var owner = env.Ally.Entity; var target = env.Enemy.Entity;
                owner.CritRate = owner.WeaponCritRate = 0f;
                Buff(env, owner, 900710, "ATKBonus", "Heavy", ".5");
                Buff(env, owner, 900711, "ATKBonus", "Normal", "9");
                var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
                env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                float attack = (owner.TotalATK + owner.WeaponATK) * (1f + owner.WeaponATKBonus + .5f);
                float defense = (owner.Level + 100f) / ((owner.Level + 100f)
                    + (target.Level + 100f) * (1f - owner.DEFReduction) * (1f - owner.DEFIgnored));
                float expected = attack * 2f * .5f * defense;
                int hits = 0;
                using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, hit) =>
                {
                    if (hit.Source.SourceEffectID != effect) return;
                    hits++;
                    Assert.That(hit.DamageBeforeShield, Is.EqualTo(expected).Within(.001f));
                }, 0);
                Assert.That(env.Manager.UseSkillBySlot(0), Is.True); Assert.That(hits, Is.EqualTo(1));
                Assert.That(random.FloatCallCount, Is.EqualTo(1)); Assert.That(random.IntCallCount, Is.Zero);
            }
        }

        [Test, Timeout(8000)]
        public void EnemyDamage_AppliesOnlyMatchingDamageTypeBonus()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.ConfigureEnemyDamageSkill(.1f, "Physical", 0f); env.CreateMinimalBattle();
                var target = env.Ally.Entity; var enemy = env.Enemy.Entity;
                Buff(env, enemy, 900710, "DMGBonus", "Normal", ".5");
                Buff(env, enemy, 900711, "DMGBonus", "Burst", "9");
                BattleRandom.SetSource(new SequenceBattleRandomSource());
                env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                float expected = enemy.TotalATK * .1f * 1.5f
                    * (1f - target.GetFinalDEF() / (target.GetFinalDEF() + 5f * enemy.Level + 500f));
                int hits = 0;
                using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, hit) =>
                {
                    if (hit.Source.SourceEntity != enemy) return;
                    hits++;
                    Assert.That(hit.DamageBeforeShield, Is.EqualTo(expected).Within(.001f));
                }, 0);
                env.Manager.EndAllyTurn(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.EnemyPostTurn);
                Assert.That(hits, Is.EqualTo(1));
            }
        }
    }
}
