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
                .Invoke(null, new object[] { target, source, aggregate });

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

        private BattleEntity NewEntity(string name, BattleSide side, int position)
        {
            GameObject gameObject = NewObject(name);
            BattleEntity entity = gameObject.AddComponent<BattleEntity>();
            entity.Side = side;
            entity.SlotPosition = position;
            entity.TotalHP = 10000f;
            entity.CurrentHP = 10000f;
            entity.MaxPoise = 500f;
            entity.Poise = 500f;
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
