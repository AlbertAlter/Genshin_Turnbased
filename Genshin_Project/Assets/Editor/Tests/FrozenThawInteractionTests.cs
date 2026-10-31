using NUnit.Framework;
using UnityEngine;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class FrozenThawInteractionTests
    {
        private GameObject root;
        private BattleEntity entity;
        private ActionPointManager ap;
        [SetUp] public void SetUp()
        {
            ReactionResolver.ResetSession();
            root = new GameObject("ThawInteractionTest"); entity = root.AddComponent<BattleEntity>();
            entity.Type = BattleEntity.EntityType.Character; entity.Side = BattleSide.Ally;
            entity.EntityID = 1009; entity.TotalHP = entity.CurrentHP = 1000;
            ap = new ActionPointManager(); ap.ResetAP();
        }
        [TearDown] public void TearDown() { ReactionResolver.ResetSession(); Object.DestroyImmediate(root); }
        private ReactionStateInstance Freeze() => ReactionStateSystem.SetEntityState(entity, ReactionType.Frozen,
            3, new ReactionSourceSnapshot(), 2, false, 2.5f);

        [Test] public void FirstTwoClicks_DoNotSpendAPOrConsumeFrozenAura_ThirdPaysOnceAndRemovesFreeze()
        {
            var state = Freeze();
            Assert.That(FrozenThawInteraction.Click(entity, ap), Is.EqualTo(FrozenThawClickResult.Cracked));
            Assert.That(state.ThawClicks, Is.EqualTo(1));
            Assert.That(FrozenThawInteraction.Click(entity, ap), Is.EqualTo(FrozenThawClickResult.Cracked));
            Assert.That(state.ThawClicks, Is.EqualTo(2)); Assert.That(ap.CurrentAP, Is.EqualTo(100));
            Assert.That(state.ReactionElementAmount, Is.EqualTo(2.5f)); Assert.That(FrozenReactionHandler.IsFrozen(entity), Is.True);
            Assert.That(FrozenThawInteraction.Click(entity, ap), Is.EqualTo(FrozenThawClickResult.Thawed));
            Assert.That(ap.CurrentAP, Is.EqualTo(90)); Assert.That(state.ThawClicks, Is.EqualTo(3));
            Assert.That(FrozenReactionHandler.IsFrozen(entity), Is.False);
            Assert.That(FrozenThawInteraction.Click(entity, ap), Is.EqualTo(FrozenThawClickResult.Ignored));
            Assert.That(ap.CurrentAP, Is.EqualTo(90));
        }
        [Test] public void InsufficientAP_KeepsSecondCrack_AndCanCompleteAfterAPIsRestored()
        {
            var state = Freeze(); ap.ConsumeAP(95);
            FrozenThawInteraction.Click(entity, ap); FrozenThawInteraction.Click(entity, ap);
            Assert.That(FrozenThawInteraction.Click(entity, ap), Is.EqualTo(FrozenThawClickResult.InsufficientAP));
            Assert.That(state.ThawClicks, Is.EqualTo(2)); Assert.That(ap.CurrentAP, Is.EqualTo(5));
            Assert.That(FrozenReactionHandler.IsFrozen(entity), Is.True);
            ap.ResetAP(); Assert.That(FrozenThawInteraction.Click(entity, ap), Is.EqualTo(FrozenThawClickResult.Thawed));
            Assert.That(ap.CurrentAP, Is.EqualTo(90));
        }
        [Test] public void ReplacingFreeze_StartsANewInteraction_WithoutCarryingOldCracks()
        {
            var first = Freeze(); FrozenThawInteraction.Click(entity, ap); FrozenThawInteraction.Click(entity, ap);
            var next = Freeze(); Assert.That(next, Is.Not.SameAs(first)); Assert.That(next.ThawClicks, Is.Zero);
            Assert.That(FrozenThawInteraction.Click(entity, ap), Is.EqualTo(FrozenThawClickResult.Cracked));
            Assert.That(next.ThawClicks, Is.EqualTo(1)); Assert.That(ap.CurrentAP, Is.EqualTo(100));
        }
        [TestCase(true, false)] [TestCase(false, true)] [TestCase(false, false)]
        public void UnavailableOrDeadTargets_DoNotAdvanceProgress(bool enemy, bool dead)
        {
            var state = Freeze();
            if (enemy) entity.Side = BattleSide.Enemy;
            if (dead) entity.CurrentHP = 0;
            var result = FrozenThawInteraction.Click(entity, !enemy && !dead ? null : ap);
            Assert.That(result, Is.EqualTo(FrozenThawClickResult.Ignored));
            Assert.That(state.ThawClicks, Is.Zero); Assert.That(ap.CurrentAP, Is.EqualTo(100));
        }
    }
}
