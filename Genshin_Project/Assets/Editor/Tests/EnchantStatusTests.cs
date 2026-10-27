using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class EnchantStatusTests
    {
        private static StatusMainData Main(BattleFlowTestEnv env, int number, int maxStack = 0)
        {
            var main = new StatusMainData { StatusID = 9912000 + number, StatusID2 = "ST_Enchant_Test" + number,
                StatusType = "Enchant", MaxStack = maxStack, WhenMax = "0" };
            env.DataManager.StatusMainDict[main.StatusID2] = main;
            return main;
        }
        private static StatusInstance Add(BattleEntity owner, StatusMainData main, string element,
            string amount = "1", string flag = null)
            => owner.AddStatus(main.StatusID2, owner, 3, 2, 0, main, null, amount,
                enchantElement: element, enchantOverride: flag);

        [TestCase(null, true), TestCase("", true), TestCase("0", true), TestCase("1", false)]
        public void InstanceData_UsesElementFloatAmountAndExplicitOverrideFlag(string flag, bool expected)
        {
            var main = new StatusMainData { StatusID2 = "ST_Enchant", StatusType = "enchant" };
            var value = EnchantStatusSystem.Parse(main, "pyro", "1.25", flag);
            Assert.That(value.Element, Is.EqualTo("Pyro"));
            Assert.That(value.ElementAmount, Is.EqualTo(1.25f));
            Assert.That(value.CanBeOverridden, Is.EqualTo(expected));
            Assert.That(EnchantStatusSystem.Parse(main, "Hydro", "0", "0").ElementAmount, Is.Zero);
        }

        [Test]
        public void Parameters_RejectMissingAmountNonFiniteInvalidFlagsAndOverridableWindGrassGeo()
        {
            var main = new StatusMainData { StatusID2 = "ST_Enchant", StatusType = "Enchant" };
            foreach (string amount in new[] { "", "-1", "NaN", "Infinity", "1*TotalATK" })
                Assert.Throws<TableValidationException>(() => EnchantStatusSystem.Parse(main, "Pyro", amount, "0"));
            foreach (string flag in new[] { "2", "true", "-1" })
                Assert.Throws<TableValidationException>(() => EnchantStatusSystem.Parse(main, "Pyro", "1", flag));
            foreach (string element in new[] { "Anemo", "Dendro", "Geo" })
            {
                Assert.Throws<TableValidationException>(() => EnchantStatusSystem.Parse(main, element, "1", "0"));
                Assert.That(EnchantStatusSystem.Parse(main, element, "1", "1").CanBeOverridden, Is.False);
            }
            foreach (string element in new[] { "", "None", "Physical", "True", "Pyro;Hydro" })
                Assert.Throws<FormatException>(() => EnchantStatusSystem.Parse(main, element, "1", "0"));
        }

        [Test]
        public void PriorityAndLocks_ReadOnlySelectRealInstance_AndFallBackAfterRemoval()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            var owner = env.Ally.Entity;
            var cryo = Add(owner, Main(env, 1), "Cryo", ".5");
            var pyro = Add(owner, Main(env, 2), "Pyro", "2");
            var electro = Add(owner, Main(env, 3), "Electro", "1.5");
            var hydro = Add(owner, Main(env, 4), "Hydro", "1.25");
            Assert.That(EnchantStatusSystem.GetActive(owner), Is.SameAs(pyro));
            owner.RemoveStatusInstance(pyro);
            Assert.That(EnchantStatusSystem.GetActive(owner), Is.SameAs(hydro));
            owner.RemoveStatusInstance(hydro);
            Assert.That(EnchantStatusSystem.GetActive(owner), Is.SameAs(electro));
            owner.RemoveStatusInstance(electro);
            Assert.That(EnchantStatusSystem.GetActive(owner), Is.SameAs(cryo));
            var fixedWind = Add(owner, Main(env, 5), "Anemo", "3", "1");
            var laterFire = Add(owner, Main(env, 6), "Pyro", "2");
            var laterFixedGeo = Add(owner, Main(env, 7), "Geo", "4", "1");
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            for (int i = 0; i < 5; i++) Assert.That(EnchantStatusSystem.GetActive(owner), Is.SameAs(fixedWind));
            Assert.That(fixedWind.RemainingPhaseCount, Is.EqualTo(3));
            Assert.That(owner.HasStatusInstance(laterFire), Is.True);
            Assert.That(random.IntCallCount + random.FloatCallCount, Is.Zero);
            owner.RemoveStatusInstance(fixedWind);
            Assert.That(EnchantStatusSystem.GetActive(owner), Is.SameAs(laterFixedGeo));
            owner.RemoveStatusInstance(laterFixedGeo);
            Assert.That(EnchantStatusSystem.GetActive(owner), Is.SameAs(laterFire));
        }

        [Test]
        public void SameIDInstances_DoNotShareParameters_AndOnlyNormalHeavyPhysicalQualify()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            var main = Main(env, 1); var owner = env.Ally.Entity;
            var first = Add(owner, main, "Pyro", ".25"); var second = Add(owner, main, "Pyro", "2");
            Assert.That(EnchantStatusSystem.GetActive(owner), Is.SameAs(second));
            Assert.That(first.Enchant.ElementAmount, Is.EqualTo(.25f));
            Assert.That(second.Enchant.ElementAmount, Is.EqualTo(2f));
            Assert.That(second.HasIntegerRuntimeValue, Is.False); Assert.That(second.RuntimeValue, Is.Zero);
            foreach (string type in new[] { "Normal", "Heavy" })
                Assert.That(EnchantStatusSystem.Resolve(owner, type, "Physical").Value.Element, Is.EqualTo("Pyro"));
            foreach (string type in new[] { "", "Skill", "Burst" })
                Assert.That(EnchantStatusSystem.Resolve(owner, type, "Physical"), Is.Null);
            foreach (string element in new[] { "None", "Pyro", "Hydro", "Electro", "Cryo", "Anemo", "Dendro", "Geo", "True" })
                Assert.That(EnchantStatusSystem.Resolve(owner, "Normal", element), Is.Null);
            owner.RemoveStatusInstance(second);
            Assert.That(EnchantStatusSystem.GetActive(owner), Is.SameAs(first));
        }

        [Test]
        public void BindStatus_CarriesPayloadAndCleansSpecificChild_EnchantCanAlsoOwnBindings()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            var child = Main(env, 1); var owner = env.Ally.Entity;
            var parent = new StatusMainData { StatusID = 9912100, StatusID2 = "ST_Enchant_Parent", MaxStack = 1 };
            var marker = new StatusMainData { StatusID = 9912101, StatusID2 = "ST_Enchant_ChildMarker", MaxStack = 0 };
            env.DataManager.StatusMainDict[parent.StatusID2] = parent; env.DataManager.StatusMainDict[marker.StatusID2] = marker;
            env.DataManager.StatusEffectDict["STE_Enchant_Bind"] = new StatusEffectData
            { StatusEffectID = parent.StatusID * 100 + 1, StatusEffectID2 = "STE_Enchant_Bind", EffectType = "BindStatus",
                Param1 = child.StatusID2, Element = "Cryo", Param2 = ".75", Param3 = "1", TargetType = "Self" };
            env.DataManager.StatusEffectDict["STE_Enchant_ChildBind"] = new StatusEffectData
            { StatusEffectID = child.StatusID * 100 + 1, StatusEffectID2 = "STE_Enchant_ChildBind", EffectType = "BindStatus",
                Param1 = marker.StatusID2, TargetType = "Self" };
            var status = owner.AddStatus(parent.StatusID2, owner, 2, 2, 0, parent);
            var enchant = owner.GetStatus(child.StatusID2);
            Assert.That(enchant.Enchant.Element, Is.EqualTo("Cryo"));
            Assert.That(enchant.Enchant.ElementAmount, Is.EqualTo(.75f));
            Assert.That(enchant.Enchant.CanBeOverridden, Is.False);
            Assert.That(status.BoundStatuses, Does.Contain(enchant));
            Assert.That(enchant.BoundStatuses.Count, Is.EqualTo(1));
            owner.RemoveStatusInstance(status);
            Assert.That(owner.HasStatus(child.StatusID2), Is.False); Assert.That(owner.HasStatus(marker.StatusID2), Is.False);
        }

        [Test]
        public void ApplyStatus_FromSkillStoresAllThreeFieldsWithoutAddingAnAura()
        {
            using var env = new BattleFlowTestEnv(); var main = Main(env, 1);
            env.ConfigureAllyAction(2, "SK_Enchant", "SE_Enchant", "Hydro", null, 10,
                effectType: "ApplyStatus", param1: main.StatusID2, duration: 2, targetType: "Self");
            var effect = env.DataManager.SkillEffectDict["SE_Enchant"]; effect.Param2 = "1.25"; effect.Param3 = "1";
            env.CreateMinimalBattle(); env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
            var status = env.Ally.Entity.GetStatus(main.StatusID2);
            Assert.That(status.Enchant.Element, Is.EqualTo("Hydro")); Assert.That(status.Enchant.ElementAmount, Is.EqualTo(1.25f));
            Assert.That(status.Enchant.CanBeOverridden, Is.False); Assert.That(env.Ally.Entity.GetAura("Hydro"), Is.Null);
            Assert.That(random.FloatCallCount + random.IntCallCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator RealDamage_UsesDamageTypeNotActionType_AndReselectsBetweenHitsNotTargets()
            => HookTestEnv.AwaitTask(RealDamageAsync);

        private static async Task RealDamageAsync()
        {
            using var env = new BattleFlowTestEnv();
            env.ConfigureAllyAction(2, "SK_EnchantDamage", "SE_EnchantDamage", "Physical", ".1*TotalATK,0", 10);
            var authored = env.DataManager.SkillEffectDict["SE_EnchantDamage"];
            authored.DamageType = "Normal"; authored.Param2 = "Splash(.5;0,1)";
            env.DataManager.SkillLevelDict[$"{BattleFlowTestEnv.AllyID}_2"][1].Hits2 = ".1*TotalATK,0";
            env.CreateMinimalBattle(); env.CreateEnemy(2); env.Manager.StartBattle();
            env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            var owner = env.Ally.Entity;
            Add(owner, Main(env, 1), "Hydro", ".75"); var fire = Add(owner, Main(env, 2), "Pyro", ".25");
            var facts = new List<DamageEventData>(); var actions = new List<string>();
            using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, fact) =>
            {
                if (fact.Source.SourceEffectID != authored.SkillEffectID2) return;
                facts.Add(fact); if (facts.Count == 1) owner.RemoveStatusInstance(fire);
            }, 0);
            using var actionListener = BattleEventHub.Subscribe<ActionEventData>(BattleEventKind.ActionStarted, (_, fact) => actions.Add(fact.ActionType), 0);
            var random = new SequenceBattleRandomSource(new[] { .9f, .9f, .9f, .9f }); BattleRandom.SetSource(random);
            await BattleExecution.Run(() => env.Ally.ExecuteSkillByIDAsync("SK_EnchantDamage", 2, true));
            Assert.That(facts.Count, Is.EqualTo(4));
            for (int i = 0; i < facts.Count; i++)
            {
                Assert.That(facts[i].Element, Is.EqualTo(i < 2 ? "Pyro" : "Hydro"));
                Assert.That(facts[i].ElementAmount, Is.EqualTo(i < 2 ? .25f : .75f));
                Assert.That(facts[i].DamageType, Is.EqualTo("Normal"));
                Assert.That(facts[i].Source.SourceKind, Is.EqualTo(ReactionSourceKind.CharacterSkill));
                Assert.That(facts[i].Source.SourceEntity, Is.SameAs(owner));
            }
            Assert.That(actions, Is.EqualTo(new[] { "Skill" })); Assert.That(authored.Element, Is.EqualTo("Physical"));
            Assert.That(random.FloatCallCount, Is.EqualTo(4)); Assert.That(random.IntCallCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator AnemoInfusion_ReusesSwirlPipelineAndKeepsOriginalHitNumbers()
            => HookTestEnv.AwaitTask(AnemoAsync);

        private static async Task AnemoAsync()
        {
            using var env = new BattleFlowTestEnv();
            env.ConfigureAllyAction(0, "SK_EnchantAnemo", "SE_EnchantAnemo", "Physical", ".1*TotalATK,0", 10);
            var level = env.DataManager.SkillLevelDict[$"{BattleFlowTestEnv.AllyID}_0"][1]; level.Hits2 = ".1*TotalATK,0";
            env.CreateMinimalBattle(); env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            Add(env.Ally.Entity, Main(env, 1), "Anemo", "1", "1");
            env.Enemy.Entity.ApplyAura("Pyro", 2f, env.Ally.Entity.EntityID);
            var facts = new List<DamageEventData>(); int swirls = 0;
            using var hits = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, fact) =>
            { if (fact.Source.SourceEffectID == "SE_EnchantAnemo" && fact.Source.CountsAsHit) facts.Add(fact); }, 0);
            using var reactions = BattleEventHub.Subscribe<ReactionEventData>(BattleEventKind.ReactionOccurred, (_, fact) =>
            { if (fact.Reaction == ReactionType.Swirl) swirls++; }, 0);
            var random = new SequenceBattleRandomSource(new[] { .9f, .9f }); BattleRandom.SetSource(random);
            await BattleExecution.Run(() => env.Ally.ExecuteSkillByIDAsync("SK_EnchantAnemo", 0, true));
            Assert.That(facts.Count, Is.EqualTo(2)); Assert.That(swirls, Is.EqualTo(2));
            Assert.That(facts[1].HitIndex, Is.EqualTo(facts[0].HitIndex + 1));
            foreach (var fact in facts)
            { Assert.That(fact.Element, Is.EqualTo("Anemo")); Assert.That(fact.ElementAmount, Is.EqualTo(1f)); }
            Assert.That(random.FloatCallCount, Is.EqualTo(2)); Assert.That(random.IntCallCount, Is.Zero);
        }

        [Test]
        public void InvalidReapplyDoesNotRemoveOldInstance_AndDefinitionValidatorChecksAllApplicationSources()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            var main = Main(env, 1, 1); var owner = env.Ally.Entity; var old = Add(owner, main, "Pyro");
            Assert.Throws<TableValidationException>(() => Add(owner, main, "Cryo", "-1"));
            Assert.That(owner.GetStatus(main.StatusID2), Is.SameAs(old));
            env.DataManager.SkillEffectDict["SE_EnchantInvalid"] = new SkillEffectData
            { SkillEffectID2 = "SE_EnchantInvalid", EffectType = "ApplyStatus", Param1 = main.StatusID2, Element = "Pyro", Param2 = "1", Param3 = "2" };
            Assert.Throws<TableValidationException>(() => EnchantStatusSystem.ValidateDefinitions(env.DataManager));
            env.DataManager.SkillEffectDict.Remove("SE_EnchantInvalid");
            env.DataManager.StatusEffectDict["STE_EnchantInvalid"] = new StatusEffectData
            { StatusEffectID2 = "STE_EnchantInvalid", EffectType = "BindStatus", Param1 = main.StatusID2, Element = "Anemo", Param2 = "1" };
            Assert.Throws<TableValidationException>(() => EnchantStatusSystem.ValidateDefinitions(env.DataManager));
            env.DataManager.StatusEffectDict.Remove("STE_EnchantInvalid");
            env.DataManager.EnemySkillEffectDict["SE_EnchantInvalid"] = new EnemySkillEffectData
            { SkillEffectID2 = "SE_EnchantInvalid", EffectType = "ApplyStatus", Param1 = main.StatusID2, Element = "Pyro", Param2 = "1", Param3 = "1" };
            Assert.DoesNotThrow(() => EnchantStatusSystem.ValidateDefinitions(env.DataManager));
        }

        [UnityTest]
        public IEnumerator DelayedStatusHeavyAndEnemyNormal_UseTheSameInfusionWithoutInventingActions()
            => HookTestEnv.AwaitTask(StatusAndEnemyAsync);

        private static async Task StatusAndEnemyAsync()
        {
            using var env = new BattleFlowTestEnv(); env.ConfigureEnemyDamageSkill(.1f);
            env.ConfigureStatus("ST_EnchantAttack", actionType: "OnTrigger", actionEffectID2: "STE_EnchantAttack");
            var effect = new StatusEffectData { StatusEffectID2 = "STE_EnchantAttack", EffectType = "Damage",
                Element = "Physical", DamageType = "Heavy", Param1 = ".1*TotalATK,0", TargetType = "Enemy", TargetSelect = "1" };
            env.DataManager.StatusEffectDict[effect.StatusEffectID2] = effect;
            env.CreateMinimalBattle(); env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            Add(env.Ally.Entity, Main(env, 1), "Geo", "1", "1");
            Add(env.Enemy.Entity, Main(env, 2), "Dendro", "1.5", "1");
            var source = env.Ally.Entity.AddStatus("ST_EnchantAttack", env.Ally.Entity, 2, 2, 0,
                env.DataManager.StatusMainDict["ST_EnchantAttack"]);
            var facts = new List<DamageEventData>(); int actions = 0;
            using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, fact) => facts.Add(fact), 0);
            using var actionListener = BattleEventHub.Subscribe<ActionEventData>(BattleEventKind.ActionStarted, (_, fact) => actions++, 0);
            var random = new SequenceBattleRandomSource(new[] { .9f }, new[] { 0 }); BattleRandom.SetSource(random);
            await BattleExecution.Run(() => env.Ally.ExecuteStatusActionsTask(source,
                env.DataManager.StatusActionDict["ST_EnchantAttack"], env.Ally.Entity, "OnTrigger"));
            Assert.That(facts.Count, Is.EqualTo(1)); Assert.That(facts[0].Element, Is.EqualTo("Geo"));
            Assert.That(facts[0].DamageType, Is.EqualTo("Heavy")); Assert.That(facts[0].Source.SourceStatus, Is.SameAs(source));
            Assert.That(actions, Is.Zero);
            await BattleExecution.Run(env.Enemy.TakeEnemyActionAsync);
            Assert.That(facts.Count, Is.EqualTo(2)); Assert.That(facts[1].Element, Is.EqualTo("Dendro"));
            Assert.That(facts[1].ElementAmount, Is.EqualTo(1.5f)); Assert.That(facts[1].DamageType, Is.EqualTo("Normal"));
            Assert.That(effect.Element, Is.EqualTo("Physical"));
        }
    }
}
