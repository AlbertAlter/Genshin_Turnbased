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
    }
}
