using System.Collections.Generic;
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    public class TargetResolverTests
    {
        [Test]
        public void ConsecutiveRandom_PrioritizesMaximumLiveCoverage()
        {
            var result = TargetResolver.Resolve(UnitRequest(
                targetNumber: 3,
                consecutive: 2,
                alivePositions: new[] { 1, 4, 5 },
                randomIndex: _ => 0));

            Assert.That(result.IsValid, Is.True, result.Error);
            Assert.That(result.Positions, Is.EqualTo(new[] { 3, 4, 5 }));
        }

        [Test]
        public void ConsecutiveRandom_RandomizesOnlyBetweenEqualCoverageWindows()
        {
            var result = TargetResolver.Resolve(UnitRequest(
                targetNumber: 2,
                consecutive: 2,
                alivePositions: new[] { 1, 3, 5 },
                randomIndex: count => count - 1));

            Assert.That(result.IsValid, Is.True, result.Error);
            Assert.That(result.Positions, Is.EqualTo(new[] { 4, 5 }));
        }

        [Test]
        public void NonConsecutiveRandom_Mode3AllowsRepeatedTargets()
        {
            var result = TargetResolver.Resolve(UnitRequest(
                targetNumber: 3,
                consecutive: 3,
                alivePositions: new[] { 2, 4 },
                randomIndex: _ => 0));

            Assert.That(result.IsValid, Is.True, result.Error);
            Assert.That(result.Positions, Is.EqualTo(new[] { 2, 2, 2 }));
        }

        [Test]
        public void NonConsecutiveRandom_Mode4NeverRepeatsTargets()
        {
            var result = TargetResolver.Resolve(UnitRequest(
                targetNumber: 3,
                consecutive: 4,
                alivePositions: new[] { 2, 4, 5 },
                randomIndex: _ => 0));

            Assert.That(result.IsValid, Is.True, result.Error);
            Assert.That(result.Positions, Is.EqualTo(new[] { 2, 4, 5 }));
        }

        [Test]
        public void FieldTarget_KeepsEmptyPositionsAsValidTargets()
        {
            var request = new TargetResolutionRequest
            {
                Side = BattleSide.Enemy,
                Domain = BattleTargetDomain.Field,
                MaxPosition = 5,
                TargetNumber = 2,
                TargetConsecutive = 2,
                IsEligible = _ => false,
                RandomIndex = _ => 0
            };

            var result = TargetResolver.Resolve(request);

            Assert.That(result.IsValid, Is.True, result.Error);
            Assert.That(result.Positions, Is.EqualTo(new[] { 1, 2 }));
        }

        [Test]
        public void UnitTarget_RejectsAnAllEmptyForcedWindow()
        {
            var request = UnitRequest(2, 1, new int[0], _ => 0);
            request.ForcedPositions = new List<int> { 2, 3 };

            var result = TargetResolver.Resolve(request);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Positions, Is.EqualTo(new[] { 2, 3 }));
        }

        [Test]
        public void TargetSelect_UsesEachStatusHostAsItsOwnOrigin()
        {
            var request = UnitRequest(0, 0, new[] { 2, 3, 4 }, _ => 0);
            request.OriginPosition = 3;
            request.TargetSelect = "1,1";

            var result = TargetResolver.Resolve(request);

            Assert.That(result.IsValid, Is.True, result.Error);
            Assert.That(result.Positions, Is.EqualTo(new[] { 2, 3, 4 }));
        }

        [Test]
        public void TargetSelect_AllSelectsEligibleUnitsWithoutDrawingOrChangingRequest()
        {
            var request = UnitRequest(0, 0, new[] { 1, 3, 5 }, _ =>
            {
                Assert.Fail("Selecting all must not draw randomness.");
                return 0;
            });
            request.TargetSelect = "-1";
            request.OriginPosition = 2;

            var result = TargetResolver.Resolve(request);

            Assert.That(result.IsValid, Is.True, result.Error);
            Assert.That(result.Positions, Is.EqualTo(new[] { 1, 3, 5 }));
            Assert.That(request.TargetNumber, Is.Zero);
            Assert.That(request.TargetSelect, Is.EqualTo("-1"));
        }

        [TestCase(2, 1, new[] { 1, 2, 3 })]
        [TestCase(3, 3, new[] { 1, 1, 1 })]
        [TestCase(4, 3, new[] { 1, 2, 3 })]
        public void TargetSelect_CountUsesSelectionMode(int mode, int expectedDraws, int[] expected)
        {
            int draws = 0;
            var request = UnitRequest(0, mode, new[] { 1, 2, 3, 4, 5 }, _ =>
            {
                draws++;
                return 0;
            });
            request.TargetSelect = "3";

            var result = TargetResolver.Resolve(request);

            Assert.That(result.IsValid, Is.True, result.Error);
            Assert.That(result.Positions, Is.EqualTo(expected));
            Assert.That(draws, Is.EqualTo(expectedDraws));
        }

        [TestCase("0", 3)]
        [TestCase("-2", 3)]
        [TestCase("2", 0)]
        [TestCase("2", 1)]
        [TestCase("2", 5)]
        public void TargetSelect_RejectsInvalidCountOrMissingRandomMode(string select, int mode)
        {
            var request = UnitRequest(0, mode, new[] { 1, 2, 3 }, _ => 0);
            request.TargetSelect = select;
            Assert.That(TargetResolver.Resolve(request).IsValid, Is.False);
        }

        [TestCase("-1", 0, new[] { 2, 4 })]
        [TestCase("2", 3, new[] { 2, 2 })]
        public void TargetSelect_RespectsExternalOverridePool(string select, int mode, int[] expected)
        {
            var request = UnitRequest(0, mode, new[] { 1, 2, 3, 4, 5 }, _ => 0);
            request.TargetSelect = select;
            request.TargetOverride = "TestStatus";
            request.OverridePositions = new List<int> { 2, 4 };

            var result = TargetResolver.Resolve(request);

            Assert.That(result.IsValid, Is.True, result.Error);
            Assert.That(result.Positions, Is.EqualTo(expected));
        }

        [Test]
        public void TargetSelect_AllFieldPositionsIncludesEmptySlots()
        {
            var request = UnitRequest(0, 0, new int[0], _ => 0);
            request.Domain = BattleTargetDomain.Field;
            request.TargetSelect = "-1";
            var result = TargetResolver.Resolve(request);
            Assert.That(result.IsValid, Is.True, result.Error);
            Assert.That(result.Positions, Is.EqualTo(new[] { 1, 2, 3, 4, 5 }));
        }

        [Test]
        public void TargetOverride_ExpandsAndShrinksPreviousRange()
        {
            Assert.That(
                TargetResolver.ApplyPreviousRange(new[] { 2, 3, 4 }, 1, 1, true, 5),
                Is.EqualTo(new[] { 1, 2, 3, 4, 5 }));
            Assert.That(
                TargetResolver.ApplyPreviousRange(new[] { 2, 3, 4 }, -1, -1, true, 5),
                Is.EqualTo(new[] { 3 }));
        }

        [Test]
        public void TargetOverride_ZeroZeroKeepsNonContiguousTargetsExactly()
        {
            var result = TargetResolver.ApplyPreviousRange(
                new[] { 2, 4 },
                0,
                0,
                true,
                5);

            Assert.That(result, Is.EqualTo(new[] { 2, 4 }));
        }

        [Test]
        public void TargetOverride_ThreePartFormExcludesPreviousTargets()
        {
            var result = TargetResolver.ApplyPreviousRange(
                new[] { 2, 3, 4 },
                1,
                1,
                false,
                5);

            Assert.That(result, Is.EqualTo(new[] { 1, 5 }));
        }

        [Test]
        public void EnemyCaster_EnemyTargetPointsToAllySide()
        {
            BattleTargetResolver.DescribeTarget(
                BattleSide.Enemy,
                null,
                "Enemy",
                out BattleSide side,
                out BattleTargetDomain domain);

            Assert.That(side, Is.EqualTo(BattleSide.Ally));
            Assert.That(domain, Is.EqualTo(BattleTargetDomain.Unit));
        }

        private static TargetResolutionRequest UnitRequest(
            int targetNumber,
            int consecutive,
            IEnumerable<int> alivePositions,
            System.Func<int, int> randomIndex)
        {
            var alive = new HashSet<int>(alivePositions);
            return new TargetResolutionRequest
            {
                Side = BattleSide.Enemy,
                Domain = BattleTargetDomain.Unit,
                MaxPosition = 5,
                TargetNumber = targetNumber,
                TargetConsecutive = consecutive,
                IsEligible = alive.Contains,
                Score = position => alive.Contains(position) ? position : 0f,
                RandomIndex = randomIndex
            };
        }
    }
}
