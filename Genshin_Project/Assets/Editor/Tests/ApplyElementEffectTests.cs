using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class ApplyElementEffectTests
    {
        [TestCase("Pyro", "Pyro"), TestCase("水", "Hydro"), TestCase("electro", "Electro")]
        [TestCase("Cryo", "Cryo"), TestCase("风", "Anemo"), TestCase("草", "Dendro"), TestCase("Geo", "Geo")]
        public void Element_UsesCanonicalSingleElement(string input, string expected)
            => Assert.That(ApplyElementEffect.ParseElement(input), Is.EqualTo(expected));

        [TestCase(""), TestCase("None"), TestCase("Physical"), TestCase("True"), TestCase("Pyro;Hydro")]
        public void Element_RejectsNonElementsAndLists(string value)
            => Assert.Throws<FormatException>(() => ApplyElementEffect.ParseElement(value));

        [TestCase(""), TestCase("0"), TestCase("-1"), TestCase("NaN"), TestCase("Infinity")]
        [TestCase("1e99"), TestCase("1*TotalATK"), TestCase("Index(1)")]
        public void Amount_RejectsNonPositiveNonFiniteAndExpressions(string value)
            => Assert.Throws<TableValidationException>(() => TableValidator.ValidateApplyElement(
                "SkillsEffect", 3, "Hydro", value, "", "", "", ""));

        [TestCase("SkillsEffect", false, true)]
        [TestCase("Status_Effect", false, false)]
        [TestCase("EnemySkill_Effect", true, true)]
        public void Tables_RequireEntityTargetsAndRejectDamageOnlyFields(string table, bool enemy, bool number)
        {
            Assert.DoesNotThrow(() => TableValidator.ValidateApplyElement(table, 3, "Hydro", "1.25", "", "", "", ""));
            Assert.DoesNotThrow(() => TableValidator.ValidateEffectRow(table, 3,
                "ApplyElement", "Hydro", 0, "", "Self", 0, 0, "", enemy, number));
            foreach (string target in new[] { "EnemyField", "AllyField", "AllyTeam", "EnemyTeam" })
                Assert.Throws<TableValidationException>(() => TableValidator.ValidateEffectRow(table, 3,
                    "ApplyElement", "Hydro", 0, "", target, -1, 3, "", enemy, number));
            Assert.Throws<TableValidationException>(() => TableValidator.ValidateEffectRow(table, 3,
                "ApplyElement", "Hydro", 1, "", "Self", 0, 0, "", enemy, number));
            foreach (int field in new[] { 0, 1, 2, 3 })
            {
                string[] values = { "", "", "", "" }; values[field] = "Unexpected";
                Assert.Throws<TableValidationException>(() => TableValidator.ValidateApplyElement(table, 3,
                    "Hydro", "1", values[0], values[1], values[2], values[3]));
            }
        }

        [Test]
        public void RealSkill_RefreshesAuraWithoutDamageHitHPOrCrit_AndPreservesOrigin()
        {
            using var env = new BattleFlowTestEnv();
            env.ConfigureAllyAction(2, "SK_Apply", "SE_Apply", "Hydro", "", 10,
                effectType: "ApplyElement", param1: "1.25");
            env.DataManager.SkillEffectDict["SE_Apply"].DamageType = "";
            env.CreateMinimalBattle(); env.Manager.StartBattle();
            env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            int damage = 0, hp = 0, contacts = 0, changes = 0;
            using var d = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, fact) => damage++, 0);
            using var h = BattleEventHub.Subscribe<HPChangedEventData>(BattleEventKind.HPChanged, (_, fact) => hp++, 0);
            using var c = BattleEventHub.Subscribe<ElementContactEventData>(BattleEventKind.ElementContactResolved, (_, fact) =>
            {
                if (fact.EffectId != "SE_Apply") return;
                contacts++; Assert.That(fact.Source, Is.SameAs(env.Ally.Entity));
                Assert.That(fact.RequestedAmount, Is.EqualTo(1.25f));
            }, 0);
            using var a = BattleEventHub.Subscribe<AuraChangedEventData>(BattleEventKind.AuraChanged, (_, fact) =>
            { if (fact.Subject == env.Enemy.Entity) changes++; }, 0);
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
            Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
            var aura = env.Enemy.Entity.GetAura("Hydro");
            Assert.That(aura.AuraAmount, Is.EqualTo(1.25f), "重施覆盖而非相加");
            Assert.That(aura.SourceEntity, Is.SameAs(env.Ally.Entity));
            Assert.That(aura.SourceSkillID, Is.EqualTo("SK_Apply"));
            Assert.That(aura.SourceEffectID, Is.EqualTo("SE_Apply"));
            Assert.That(contacts, Is.EqualTo(2)); Assert.That(changes, Is.EqualTo(2));
            Assert.That(damage + hp, Is.Zero); Assert.That(env.Enemy.Entity.CurrentHP, Is.EqualTo(1000f));
            Assert.That(env.Manager.APManager.CurrentAP, Is.EqualTo(80));
            Assert.That(random.FloatCallCount + random.IntCallCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator DirectApplication_ReactsNormallyButDoesNotCreateAPrimaryHit()
            => HookTestEnv.AwaitTask(DirectApplication_ReactsNormallyButDoesNotCreateAPrimaryHitAsync);

        private static async Task DirectApplication_ReactsNormallyButDoesNotCreateAPrimaryHitAsync()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            var reactions = new List<ReactionType>(); var damages = new List<DamageEventData>();
            using var r = BattleEventHub.Subscribe<ReactionEventData>(BattleEventKind.ReactionOccurred,
                (_, fact) => reactions.Add(fact.Reaction), 0);
            using var d = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved,
                (_, fact) => damages.Add(fact), 0);
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            env.Enemy.Entity.ApplyAura("Hydro", 2f, env.Ally.Entity.EntityID);
            await BattleExecution.Run(async () =>
            {
                ApplyElementEffect.Execute("Pyro", "1", env.Ally.Entity, new[] { env.Enemy.Entity },
                    ReactionSourceKind.CharacterSkill, "SK_Apply", "SE_Apply", ReactionResolver.BeginEffectExecution());
                await BattleExecution.Flush();
            });
            Assert.That(reactions, Does.Contain(ReactionType.Vaporize));
            Assert.That(damages, Is.Empty);
            Assert.That(env.Enemy.Entity.GetAura("Hydro").AuraAmount, Is.EqualTo(1.5f).Within(.001f));
            reactions.Clear();
            await BattleExecution.Run(async () =>
            {
                ApplyElementEffect.Execute("Anemo", "1", env.Ally.Entity, new[] { env.Enemy.Entity },
                    ReactionSourceKind.StatusEffect, "", "STE_Apply", ReactionResolver.BeginEffectExecution());
                await BattleExecution.Flush();
            });
            Assert.That(reactions, Does.Contain(ReactionType.Swirl)); Assert.That(damages, Is.Not.Empty);
            foreach (var fact in damages)
            {
                Assert.That(fact.Source.SourceKind, Is.EqualTo(ReactionSourceKind.StatusEffect), "反应保留原施加来源，不改写归功");
                Assert.That(fact.Source.ReactionType, Is.EqualTo(ReactionType.Swirl));
                Assert.That(fact.DamageType, Is.EqualTo("Reaction"));
                Assert.That(fact.Source.SourceEffectID, Is.EqualTo("STE_Apply"));
                Assert.That(fact.Source.CountsAsHit, Is.False); Assert.That(fact.Critical.HasValue, Is.False);
            }
            Assert.That(env.Enemy.Entity.GetAura("Anemo"), Is.Null);
            Assert.That(random.FloatCallCount + random.IntCallCount, Is.Zero);
        }

        [Test]
        public void StatusAction_UsesItsRealCasterAndEffectWithoutCreatingDamage()
        {
            using var env = new BattleFlowTestEnv();
            env.ConfigureStatus("ST_Apply", actionType: "OnTrigger", actionEffectID2: "STE_Apply");
            env.DataManager.StatusEffectDict["STE_Apply"] = new StatusEffectData
            { StatusEffectID2 = "STE_Apply", EffectType = "ApplyElement", Element = "Hydro", Param1 = ".5", TargetType = "Self" };
            env.CreateMinimalBattle();
            var status = env.Ally.Entity.AddStatus("ST_Apply", env.Ally.Entity, 2, 2, 0,
                env.DataManager.StatusMainDict["ST_Apply"]);
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            env.Ally.ExecuteStatusActions(status, env.DataManager.StatusActionDict["ST_Apply"], env.Ally.Entity, "OnTrigger");
            var aura = env.Ally.Entity.GetAura("Hydro");
            Assert.That(aura.AuraAmount, Is.EqualTo(.5f)); Assert.That(aura.SourceEntity, Is.SameAs(env.Ally.Entity));
            Assert.That(aura.SourceSkillID, Is.Empty); Assert.That(aura.SourceEffectID, Is.EqualTo("STE_Apply"));
            Assert.That(env.Ally.Entity.CurrentHP, Is.EqualTo(1000f));
            Assert.That(random.FloatCallCount + random.IntCallCount, Is.Zero);
        }

        [Test]
        public void PureGeo_UsesFrozenCryoForCrystallizeWithoutTriggeringShatter()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            env.Enemy.Entity.ApplyAura("Hydro", 1f, env.Ally.Entity.EntityID);
            ReactionResolver.Resolve(new ReactionContext { SourceEntity = env.Ally.Entity, Target = env.Enemy.Entity,
                SourceKind = ReactionSourceKind.CharacterSkill, AttackElement = "Cryo", AttackAmount = 1f,
                PreReactionDamage = 0f, SourceEffectID = "SE_Freeze" });
            Assert.That(FrozenReactionHandler.GetFrozenAuraAsCryo(env.Enemy.Entity), Is.EqualTo(1f));
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            ApplyElementEffect.Execute("Geo", "1", env.Ally.Entity, new[] { env.Enemy.Entity },
                ReactionSourceKind.CharacterSkill, "SK_Apply", "SE_Apply", ReactionResolver.BeginEffectExecution());
            Assert.That(FrozenReactionHandler.GetFrozenAuraAsCryo(env.Enemy.Entity), Is.EqualTo(.5f).Within(.001f));
            Assert.That(env.Enemy.Entity.CurrentHP, Is.EqualTo(1000f));
            Assert.That(random.FloatCallCount + random.IntCallCount, Is.Zero);
        }

        [Test]
        public void EnemySkill_UsesSameEffectWithoutDealingDirectDamage()
        {
            using var env = new BattleFlowTestEnv(); env.ConfigureEnemyDamageSkill();
            var effect = env.DataManager.EnemySkillEffectDict["SE_Enemy_BattleFlow"];
            effect.EffectType = "ApplyElement"; effect.Element = "Cryo"; effect.Param1 = "1"; effect.DamageType = "";
            env.CreateMinimalBattle(); env.Manager.StartBattle();
            env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            int damage = 0;
            using var d = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, fact) => damage++, 0);
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            env.Manager.EndAllyTurn(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.EnemyPostTurn);
            Assert.That(env.Ally.Entity.GetAura("Cryo").SourceEntity, Is.SameAs(env.Enemy.Entity));
            Assert.That(env.Ally.Entity.CurrentHP, Is.EqualTo(1000f)); Assert.That(damage, Is.Zero);
            Assert.That(random.FloatCallCount, Is.Zero); Assert.That(random.IntCallCount, Is.EqualTo(1));
        }
    }
}
