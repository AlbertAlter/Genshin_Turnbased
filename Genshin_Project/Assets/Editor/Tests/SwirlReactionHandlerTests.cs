using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace GenshinTurnBased.Tests.EditMode
{
    public class SwirlReactionHandlerTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();
        private BattleManager _battle;
        private HookTestDataManager _dataManager;
        private BattleEntity _source;

        [SetUp]
        public void SetUp()
        {
            ReactionResolver.ResetSession();
            HookTestEnv.SetDataManagerSingleton(null);
            _dataManager = NewObject("Swirl_DataManager").AddComponent<HookTestDataManager>();
            _dataManager.ReactionLevelCoefficientDict[80] = 100f;
            HookTestEnv.SetDataManagerSingleton(_dataManager);
            _battle = NewObject("Swirl_BattleManager").AddComponent<BattleManager>();
            _source = NewEntity("Swirl_Source", BattleSide.Ally, 1);
            _source.Type = BattleEntity.EntityType.Character;
            _source.Level = 80;
            _source.TotalEM = 200f;
        }

        [TearDown]
        public void TearDown()
        {
            ReactionResolver.ResetSession();
            HookTestEnv.SetDataManagerSingleton(null);
            for (int index = _objects.Count - 1; index >= 0; index--)
                Object.DestroyImmediate(_objects[index]);
            _objects.Clear();
        }

        [Test]
        public void ResolvePreparedBatch_EqualSplitRedistributesUnusedAnemo()
        {
            BattleEntity target = NewEntity("Swirl_Target", BattleSide.Enemy, 2);
            target.ApplyAura("Pyro", 0.5f, 1);
            target.ApplyAura("Hydro", 2f, 1);

            SwirlPreparedBatch prepared = SwirlReactionHandler.PrepareBatch(new[]
            {
                NewContext(target, 2f, 10)
            });
            SwirlReactionHandler.ResolvePreparedBatch(prepared);

            Assert.That(target.GetAura("Pyro"), Is.Null);
            Assert.That(target.GetAura("Hydro").AuraAmount, Is.EqualTo(0.5f).Within(0.0001f));
        }

        [Test]
        public void ResolvePreparedBatch_MergesSameOutgoingElementAtDestination()
        {
            BattleEntity left = NewEntity("Swirl_Left", BattleSide.Enemy, 1);
            BattleEntity middle = NewEntity("Swirl_Middle", BattleSide.Enemy, 2);
            BattleEntity right = NewEntity("Swirl_Right", BattleSide.Enemy, 3);
            left.ApplyAura("Pyro", 1f, 1);
            right.ApplyAura("Pyro", 1f, 1);

            SwirlPreparedBatch prepared = SwirlReactionHandler.PrepareBatch(new[]
            {
                NewContext(left, 1f, 20), NewContext(right, 1f, 20)
            });
            SwirlReactionHandler.ResolvePreparedBatch(prepared);

            Assert.That(middle.GetAura("Pyro").AuraAmount, Is.EqualTo(2f).Within(0.0001f));
        }

        [Test]
        public void ResolvePreparedBatch_EmptyAdjacentSlotDoesNotJump()
        {
            BattleEntity origin = NewEntity("Swirl_Origin", BattleSide.Enemy, 1);
            origin.ApplyAura("Pyro", 1f, 1);
            BattleEntity distant = NewEntity("Swirl_Distant", BattleSide.Enemy, 3);

            SwirlPreparedBatch prepared = SwirlReactionHandler.PrepareBatch(new[]
            {
                NewContext(origin, 1f, 30)
            });
            SwirlReactionHandler.ResolvePreparedBatch(prepared);

            Assert.That(distant.GetAura("Pyro"), Is.Null);
        }

        [Test]
        public void ResolvePreparedBatch_DoubleSplitUsesInitialSnapshotForAllPairs()
        {
            BattleEntity left = NewEntity("Swirl_Hydro", BattleSide.Enemy, 1);
            BattleEntity middle = NewEntity("Swirl_Dendro", BattleSide.Enemy, 2);
            BattleEntity right = NewEntity("Swirl_Pyro", BattleSide.Enemy, 3);
            left.ApplyAura("Hydro", 1f, 1);
            right.ApplyAura("Pyro", 1f, 1);
            middle.ApplyAura("Dendro", 2f, 1);

            SwirlPreparedBatch prepared = SwirlReactionHandler.PrepareBatch(new[]
            {
                NewContext(left, 1f, 40), NewContext(right, 1f, 40)
            });
            SwirlReactionHandler.ResolvePreparedBatch(prepared);

            Assert.That(middle.GetAura("Dendro").AuraAmount, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(BloomCoreSystem.ActiveCores, Has.Count.EqualTo(1));
            Assert.That(BurningReactionHandler.IsBurning(middle), Is.True);
        }

        [Test]
        public void ResolvePreparedBatch_NormalAndVirtualCryoAreIndependentSources()
        {
            BattleEntity target = NewEntity("Swirl_Frozen", BattleSide.Enemy, 2);
            target.ApplyAura("Cryo", 1f, 1);
            ReactionStateSystem.SetEntityState(target, ReactionType.Frozen, 1,
                ReactionSourceSnapshot.Capture(NewContext(target, 1f, 50)), 1, true, 1f);

            SwirlPreparedBatch prepared = SwirlReactionHandler.PrepareBatch(new[]
            {
                NewContext(target, 1f, 50)
            });
            SwirlReactionHandler.ResolvePreparedBatch(prepared);

            Assert.That(target.GetAura("Cryo").AuraAmount, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(FrozenReactionHandler.GetFrozenAuraAsCryo(target), Is.EqualTo(0.5f).Within(0.0001f));
        }

        [Test]
        public void ResolvePreparedBatch_DealsOneTransformativeHitPerSwirledAura()
        {
            BattleEntity target = NewEntity("Swirl_Damage", BattleSide.Enemy, 2);
            target.ApplyAura("Pyro", 1f, 1);

            SwirlBatchResult result = SwirlReactionHandler.ResolvePreparedBatch(
                SwirlReactionHandler.PrepareBatch(new[] { NewContext(target, 1f, 60) }));

            float expectedDamage = 100f * 1.2f * (1f + 16f * 200f / 2200f);
            Assert.That(target.CurrentHP, Is.EqualTo(10000f - expectedDamage).Within(0.001f));
            Assert.That(result.Reaction.TriggeredReactions, Has.Count.EqualTo(1));
            Assert.That(result.Reaction.TriggeredReactions[0].Type, Is.EqualTo(ReactionType.Swirl));
            Assert.That(target.GetAura("Pyro"), Is.Null);
        }

        [Test]
        public void ResolvePreparedBatch_EnemySwirlIgnoresElementalMastery()
        {
            _source.Type = BattleEntity.EntityType.Enemy;
            _source.TotalEM = 9999f;
            BattleEntity target = NewEntity("Enemy_Swirl_Damage", BattleSide.Ally, 2);
            target.ApplyAura("Hydro", 1f, 1);
            ReactionContext context = NewContext(target, 1f, 70);
            context.SourceKind = ReactionSourceKind.EnemySkill;

            SwirlReactionHandler.ResolvePreparedBatch(
                SwirlReactionHandler.PrepareBatch(new[] { context }));

            Assert.That(target.CurrentHP, Is.EqualTo(10000f - 120f).Within(0.001f));
        }

        [Test]
        public void ResolvePreparedBatch_NormalAndVirtualPyroAreIndependentSources()
        {
            BattleEntity target = NewEntity("Swirl_Burning", BattleSide.Enemy, 2);
            target.ApplyAura("Pyro", 1f, 1);
            ReactionStateSystem.SetEntityState(target, ReactionType.Burning, 1,
                ReactionSourceSnapshot.Capture(NewContext(target, 1f, 80)), 1, true, 1f);

            SwirlReactionHandler.ResolvePreparedBatch(
                SwirlReactionHandler.PrepareBatch(new[] { NewContext(target, 1f, 80) }));

            Assert.That(target.GetAura("Pyro").AuraAmount, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(BurningReactionHandler.GetBurningAuraAsPyro(target), Is.EqualTo(0.5f).Within(0.0001f));
        }

        [Test]
        public void ResolvePreparedBatch_NoReactableAuraProducesNoSwirl()
        {
            BattleEntity target = NewEntity("Swirl_Empty", BattleSide.Enemy, 2);

            SwirlBatchResult result = SwirlReactionHandler.ResolvePreparedBatch(
                SwirlReactionHandler.PrepareBatch(new[] { NewContext(target, 1f, 90) }));

            Assert.That(result.Reaction.HasReaction, Is.False);
            Assert.That(target.CurrentHP, Is.EqualTo(10000f));
            Assert.That(target.GetAura("Anemo"), Is.Null);
        }

        [Test]
        public void ResolveStageThree_SplitsElectroBetweenSuperconductAndQuickenSimultaneously()
        {
            BattleEntity target = NewEntity("Swirl_StageThree", BattleSide.Enemy, 2);
            target.ApplyAura("Cryo", 2f, 1);
            target.ApplyAura("Dendro", 2f, 1);
            target.ApplyAura("Electro", 2f, 1);
            ReactionContext source = NewContext(target, 1f, 100);
            var aggregate = new ReactionResult();

            typeof(SwirlReactionHandler)
                .GetMethod("ResolveStageThree", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { target, source, aggregate, null });

            Assert.That(target.GetAura("Electro"), Is.Null);
            Assert.That(target.GetAura("Cryo").AuraAmount, Is.EqualTo(1f).Within(0.001f));
            Assert.That(target.GetAura("Dendro").AuraAmount, Is.EqualTo(1f).Within(0.001f));
            Assert.That(aggregate.TriggeredReactions.Exists(x => x.Type == ReactionType.Superconduct), Is.True);
            Assert.That(aggregate.TriggeredReactions.Exists(x => x.Type == ReactionType.Quicken), Is.True);
            Assert.That(target.CurrentHP, Is.EqualTo(10000f));
        }

        private ReactionContext NewContext(BattleEntity target, float amount, long effectID)
        {
            return new ReactionContext
            {
                SourceEntity = _source, Target = target,
                SourceKind = ReactionSourceKind.CharacterSkill,
                SourceEffectID = "SE_Swirl_Test", EffectExecutionID = effectID,
                ApplicationPhase = 1, AttackElement = "Anemo", AttackAmount = amount,
                PreReactionDamage = 0f, DamageType = "Skill"
            };
        }

        [Test]
        public void ReverseSwirl_ConductsThroughIntrinsicAnemo_WithoutReturnOrRepeatedSwirlDamage()
        {
            var left = NewEntity("Chain_Left", BattleSide.Enemy, 1);
            var origin = NewEntity("Chain_Origin", BattleSide.Enemy, 2);
            var right = NewEntity("Chain_Right", BattleSide.Enemy, 3);
            AddIntrinsic(left, "Anemo"); AddIntrinsic(origin, "Anemo"); AddIntrinsic(right, "Anemo");
            var context = NewContext(origin, 1.5f, 101);
            context.AttackElement = "Pyro";
            var prepared = SwirlReactionHandler.PrepareBatch(new[] { context });
            var result = SwirlReactionHandler.ResolvePreparedBatch(prepared);
            float expected = 100f * 1.2f * (1f + 16f * 200f / 2200f);
            Assert.That(origin.CurrentHP, Is.EqualTo(10000f - expected).Within(.001f));
            Assert.That(left.CurrentHP, Is.EqualTo(10000f));
            Assert.That(right.CurrentHP, Is.EqualTo(10000f));
            Assert.That(result.Reaction.TriggeredReactions.FindAll(r => r.Type == ReactionType.Swirl), Has.Count.EqualTo(3));
            foreach (var target in new[] { left, origin, right })
            {
                Assert.That(result.Reaction.TriggeredReactions.FindAll(r => r.Type == ReactionType.Swirl && ReferenceEquals(r.Target, target)),
                    Has.Count.EqualTo(1));
                Assert.That(ElementQuery.ReadIntrinsic(target, "Anemo"), Is.Not.Null);
                Assert.That(ElementQuery.ReadNormal(target, "Pyro"), Is.Null);
            }
            Assert.That(SwirlReactionHandler.ResolvePreparedBatch(prepared).Reaction.HasReaction, Is.False,
                "同一已提交计划不能再次伤害、声明或传播");
        }

        [Test]
        public void IntrinsicAuraSwirl_SuppliesOnlyTheFiniteAnemoQuota_AndIndependentHitsCanTriggerAgain()
        {
            var origin = NewEntity("Intrinsic_Hydro", BattleSide.Enemy, 2);
            var adjacent = NewEntity("Intrinsic_Adjacent", BattleSide.Enemy, 1);
            AddIntrinsic(origin, "Hydro");
            var result = SwirlReactionHandler.ResolveBatch(new[] { NewContext(origin, .375f, 102) });
            Assert.That(result.Reaction.HasReaction, Is.True);
            Assert.That(ElementQuery.GetAmount(adjacent, "Hydro"), Is.EqualTo(.375f));
            Assert.That(ElementQuery.ReadIntrinsic(origin, "Hydro"), Is.Not.Null);
            Assert.That(ElementQuery.ReadNormal(origin, "Hydro"), Is.Null);
            float before = origin.CurrentHP;
            Assert.That(SwirlReactionHandler.ResolveBatch(new[] { NewContext(origin, .375f, 103) }).Reaction.HasReaction, Is.True);
            Assert.That(origin.CurrentHP, Is.LessThan(before), "新hit不沿用上一次来源链的访问集合");
        }

        private static void AddIntrinsic(BattleEntity target, string element)
        {
            string id = ElementQuery.IntrinsicStatusID(element);
            target.AddStatus(id, target, 0, 1, 0,
                new StatusMainData { StatusID2 = id, MaxStack = 1, MaxCount = 1, WhenMax = "1" });
        }

        private BattleEntity NewEntity(string name, BattleSide side, int position)
        {
            GameObject gameObject = NewObject(name);
            BattleEntity entity = gameObject.AddComponent<BattleEntity>();
            entity.Side = side;
            entity.SlotPosition = position;
            entity.TotalHP = 10000f;
            entity.CurrentHP = 10000f;
            FieldPosition slot = _battle.Field.GetSlot(side, position);
            slot.Occupant = entity;
            entity.Position = slot;
            return entity;
        }

        private GameObject NewObject(string name)
        {
            var gameObject = new GameObject(name);
            _objects.Add(gameObject);
            return gameObject;
        }
    }
}
