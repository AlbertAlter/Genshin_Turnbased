using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class TargetScreenNavigationTests
    {
        [TestCase(BattleSide.Ally, false)]
        [TestCase(BattleSide.Ally, true)]
        [TestCase(BattleSide.Enemy, false)]
        [TestCase(BattleSide.Enemy, true)]
        public void DirectionFollowsVisualPosition_NotSideOrSlotOrder(BattleSide side, bool reversed)
        {
            var previous = BattleRandom.Source;
            try
            {
                var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
                var selector = new TargetSelector();
                selector.Init(side, 1, 0, p => p == 2 ? 100 : 0, _ => true);
                System.Func<int, float?> x = p => reversed ? -p * 100 : p * 100;
                selector.MoveScreen(-1, x);
                Assert.That(selector.CurrentSelection, Is.EqualTo(new[] { reversed ? 3 : 1 }));
                selector.MoveScreen(1, x);
                Assert.That(selector.CurrentSelection, Is.EqualTo(new[] { 2 }));
                Assert.That(random.IntCallCount, Is.EqualTo(1), "Navigation must not consume more battle randomness.");
            }
            finally { BattleRandom.SetSource(previous); }
        }

        [Test]
        public void ContinuousWindowFollowsCenter_AndKeepsTauntRestriction()
        {
            var previous = BattleRandom.Source;
            try
            {
                BattleRandom.SetSource(new SequenceBattleRandomSource());
                var selector = new TargetSelector();
                selector.Init(BattleSide.Enemy, 3, 1, p => 10-p, _ => true, tauntProvider: p => p == 2);
                selector.MoveScreen(1, p => p * 100);
                Assert.That(selector.CurrentSelection, Is.EqualTo(new[] { 2,3,4 }));
                selector.MoveScreen(1, p => p * 100);
                Assert.That(selector.LastMoveBlockedByTaunt, Is.True);
                Assert.That(selector.CurrentSelection, Is.EqualTo(new[] { 2,3,4 }));
            }
            finally { BattleRandom.SetSource(previous); }
        }

        [TestCase(new[] { 3 }, 1, new[] { 3 })]
        [TestCase(new[] { 3 }, 3, new[] { 2, 3, 4 })]
        [TestCase(new[] { 2, 3, 4 }, 1, new[] { 3 })]
        [TestCase(new[] { 1 }, 3, new[] { 1, 2, 3 })]
        [TestCase(new[] { 5 }, 3, new[] { 3, 4, 5 })]
        public void PhaseSelection_KeepsFocusAndCentersNewWindow(int[] previousPositions, int count, int[] expected)
        {
            var previous = BattleRandom.Source;
            try
            {
                var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
                var selector = new TargetSelector();
                selector.Init(BattleSide.Enemy, count, 1, p => p == 5 ? 1000 : 1, _ => true,
                    preferredPositions: previousPositions);
                Assert.That(selector.CurrentSelection, Is.EqualTo(expected));
                Assert.That(random.IntCallCount, Is.Zero, "Changing phases must not reroll the focus.");
            }
            finally { BattleRandom.SetSource(previous); }
        }

        [Test]
        public void PhaseSelection_RespectsDeadExcludedAndTauntingTargets()
        {
            var previous = BattleRandom.Source;
            try
            {
                var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
                var selector = new TargetSelector();
                selector.Init(BattleSide.Enemy, 1, 0, _ => 1, p => p != 3,
                    excludePositions: new System.Collections.Generic.List<int> { 2 },
                    preferredPositions: new[] { 3 });
                Assert.That(selector.CurrentSelection, Is.EqualTo(new[] { 4 }));
                selector.Init(BattleSide.Enemy, 3, 1, _ => 1, _ => true,
                    tauntProvider: p => p == 5, preferredPositions: new[] { 2 });
                Assert.That(selector.CurrentSelection, Is.EqualTo(new[] { 3, 4, 5 }));
                selector.Init(BattleSide.Enemy, 1, 0, _ => 1, _ => false,
                    preferredPositions: new[] { 3 });
                Assert.That(selector.IsSelectionValid(), Is.False);
                Assert.That(random.IntCallCount, Is.Zero);
            }
            finally { BattleRandom.SetSource(previous); }
        }
    }
}
