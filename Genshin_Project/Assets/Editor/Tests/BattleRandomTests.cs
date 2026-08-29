using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    internal sealed class SequenceBattleRandomSource : IBattleRandomSource
    {
        private readonly Queue<float> _floatValues;
        private readonly Queue<int> _intValues;

        public int FloatCallCount { get; private set; }
        public int IntCallCount { get; private set; }

        public SequenceBattleRandomSource(float[] floatValues = null, int[] intValues = null)
        {
            _floatValues = new Queue<float>(floatValues ?? Array.Empty<float>());
            _intValues = new Queue<int>(intValues ?? Array.Empty<int>());
        }

        public int NextInt(int minInclusive, int maxExclusive)
        {
            IntCallCount++;
            int value = _intValues.Count > 0 ? _intValues.Dequeue() : minInclusive;
            if (value < minInclusive || value >= maxExclusive)
                throw new InvalidOperationException(
                    $"测试随机整数 {value} 不在 [{minInclusive}, {maxExclusive}) 内。");
            return value;
        }

        public float NextFloat01()
        {
            FloatCallCount++;
            float value = _floatValues.Count > 0 ? _floatValues.Dequeue() : 0f;
            if (value < 0f || value >= 1f)
                throw new InvalidOperationException($"测试随机小数 {value} 不在 [0, 1) 内。");
            return value;
        }
    }

    public class BattleRandomTests
    {
        [SetUp]
        public void SetUp()
        {
            BattleRandom.ResetSource();
        }

        [TearDown]
        public void TearDown()
        {
            BattleRandom.ResetSource();
        }

        [Test]
        public void SystemSource_SameSeedProducesSameSequence()
        {
            var first = new SystemBattleRandomSource(123456);
            var second = new SystemBattleRandomSource(123456);

            for (int index = 0; index < 8; index++)
            {
                Assert.That(first.NextInt(0, 1000), Is.EqualTo(second.NextInt(0, 1000)));
                Assert.That(first.NextFloat01(), Is.EqualTo(second.NextFloat01()));
            }
            Assert.That(first.Step, Is.EqualTo(16));
            Assert.That(second.Step, Is.EqualTo(16));
        }

        [Test]
        public void CriticalResolver_UsesExactlyOneInjectedRollPerTargetHit()
        {
            var random = new SequenceBattleRandomSource(new[] { 0.2f, 0.8f });
            BattleRandom.SetSource(random);

            CriticalHitResult critical = CriticalHitResolver.Resolve(50f, 0.5f, 1f);
            CriticalHitResult normal = CriticalHitResolver.Resolve(50f, 0.5f, 1f);

            Assert.That(critical.IsCritical, Is.True);
            Assert.That(critical.DamageAfterCritical, Is.EqualTo(100f));
            Assert.That(normal.IsCritical, Is.False);
            Assert.That(normal.DamageAfterCritical, Is.EqualTo(50f));
            Assert.That(random.FloatCallCount, Is.EqualTo(2));
        }
    }
}
