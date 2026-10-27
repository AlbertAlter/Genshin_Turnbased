#if ROGUELIKE_SAVE_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using UnityEngine;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class BattlePresentationTimelineTests
    {
        private readonly List<GameObject> roots = new List<GameObject>();
        private BattleEntity Target(int slot)
        {
            var go = new GameObject("PresentationTarget"); roots.Add(go);
            var entity = go.AddComponent<BattleEntity>(); entity.Side = BattleSide.Enemy; entity.SlotPosition = slot;
            return entity;
        }
        [TearDown] public void Cleanup()
        {
            foreach (var root in roots) UnityEngine.Object.DestroyImmediate(root);
            roots.Clear();
        }
        private static void Add(BattlePresentationSequence sequence, BattleDeclaration declaration)
            => typeof(BattlePresentationSequence).GetMethod("Record", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(sequence, new object[] { declaration, null });
        private static void Seal(BattlePresentationSequence sequence)
            => typeof(BattlePresentationSequence).GetMethod("Seal", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(sequence, null);
        private static BattleDeclaration Damage(BattleEntity target, long execution, long hit, string effect = "SE_Test", long[] causes = null)
        {
            using (BattleEventHub.BeginEffectExecution(execution))
                return BattleEventHub.Record(new object(), new DamageEventData(new DamageResolvedEvent
                {
                    Target = target, Source = new DamageSourceInfo { SourceEffectID = effect }, DamageBeforeShield = 10, ActualHPDamage = 10,
                }), hit: hit, causes: causes);
        }

        [Test]
        public void ArrowRain_AllThreePerTargetAndCanCrossHitWithoutReorderingOwnTarget()
        {
            var a = Target(1); var b = Target(2); var c = Target(3);
            var sequence = new BattlePresentationSequence();
            for (int hit = 1; hit <= 3; hit++)
                foreach (var target in new[] { a, b, c }) Add(sequence, Damage(target, 100, 1000 + hit));
            Seal(sequence);
            var cues = BattlePresentationTimeline.Build(sequence, _ => new BattlePresentationTiming(.1, 1));
            Assert.That(cues.Count, Is.EqualTo(9));
            foreach (int slot in new[] { 1, 2, 3 })
            {
                var target = cues.Where(cue => cue.Result.TargetSlot == slot).ToArray();
                Assert.That(target.Length, Is.EqualTo(3));
                Assert.That(target.Select(cue => cue.Result.Order), Is.Ordered);
            }
            Assert.That(cues.Select(cue => cue.Result.Header.EventId).Distinct().Count(), Is.EqualTo(9));
            CollectionAssert.AreEqual(cues.Select(cue => cue.Time), BattlePresentationTimeline.Build(sequence,
                _ => new BattlePresentationTiming(.1, 1)).Select(cue => cue.Time));
        }

        [Test]
        public void UIParameters_UseSharedMediumScale_AndEffectIntervalCanOverrideZero()
        {
            var target = Target(1); var sequence = new BattlePresentationSequence();
            Add(sequence, Damage(target, 10, 1)); Seal(sequence);
            var result = sequence.Results[0];
            var values = new Dictionary<string, string>
            {
                ["TimeBetweenHits"] = ".2", ["HitTimeRandomVariant"] = ".1", ["MediumRandomScale"] = ".5",
                ["HitIntervalSeconds"] = "9", ["FullTimeJitterSeconds"] = "9", ["MediumTimeJitterSeconds"] = "8",
            };
            foreach (int mode in new[] { 0, 1, 2 })
            {
                var effects = new Dictionary<string, BattlePresentationConfiguration.EffectOverride>
                { ["SE_Test"] = new BattlePresentationConfiguration.EffectOverride { X = mode, Y = mode, Time = mode } };
                var config = BattlePresentationConfiguration.Parse(values, effects);
                var timing = config.For(result);
                Assert.That(timing.HitIntervalSeconds, Is.EqualTo(.2));
                Assert.That(timing.JitterSeconds, Is.EqualTo(mode == 0 ? 0 : mode == 1 ? .05 : .1).Within(1e-9));
                Assert.That(timing.MediumRandomScale, Is.EqualTo(.5f));
                Assert.That(timing.XRandomMode, Is.EqualTo(mode));
                effects["SE_Test"].HitInterval = 0;
                Assert.That(BattlePresentationConfiguration.Parse(values, effects).For(result).HitIntervalSeconds, Is.Zero);
            }
            values["MediumRandomScale"] = ".25";
            Assert.That(BattlePresentationConfiguration.Parse(values).MediumJitter, Is.EqualTo(.025).Within(1e-9));
            foreach (string key in new[] { "TimeBetweenHits", "HitTimeRandomVariant", "MediumRandomScale" })
            {
                string original = values[key];
                foreach (string invalid in new[] { "-1", "NaN", "Infinity" })
                {
                    values[key] = invalid;
                    Assert.Throws<System.IO.InvalidDataException>(() => BattlePresentationConfiguration.Parse(values));
                }
                values[key] = original;
            }
            values["MediumRandomScale"] = "1.01";
            Assert.Throws<System.IO.InvalidDataException>(() => BattlePresentationConfiguration.Parse(values));
        }

        [Test]
        public void PublishedUIParameters_LoadCurrentTimingAndMediumScale()
        {
            var config = BattlePresentationConfiguration.Load();
            Assert.That(config.HitInterval, Is.EqualTo(.2));
            Assert.That(config.FullJitter, Is.EqualTo(.1));
            Assert.That(config.MediumJitter, Is.EqualTo(.05).Within(1e-9));
            Assert.That(config.MediumRandomScale, Is.EqualTo(.5f));
            Assert.DoesNotThrow(() => BattlefieldDamagePopupStyle.Load());
        }

        [Test]
        public void TimeJitter_CanAdvanceOrDelay_AndMediumOffsetsAreHalf()
        {
            var target = Target(1); var sequence = new BattlePresentationSequence();
            for (int hit = 0; hit < 64; hit++) Add(sequence, Damage(target, 10, 1000 + hit));
            Seal(sequence);
            var full = BattlePresentationTimeline.Build(sequence, _ => new BattlePresentationTiming(.2, .1));
            var medium = BattlePresentationTimeline.Build(sequence, _ => new BattlePresentationTiming(.2, .05));
            Assert.That(full.Select(cue => cue.Result.Order), Is.Ordered);
            bool early = false, late = false;
            for (int i = 0; i < full.Count; i++)
            {
                double baseline = i * .2, offset = full[i].Time - baseline;
                Assert.That(offset, Is.InRange(-.100000001, .100000001));
                Assert.That(full[i].Time, Is.GreaterThanOrEqualTo(0));
                Assert.That(medium[i].Time - baseline, Is.EqualTo(offset * .5).Within(1e-9));
                early |= offset < -.001; late |= offset > .001;
            }
            Assert.That(early && late, Is.True);
        }

        [Test]
        public void ChildEffectAndStatus_AreBarriers_ParentDoesNotJumpOverChild()
        {
            var target = Target(1); var sequence = new BattlePresentationSequence();
            Add(sequence, Damage(target, 10, 1, "Parent"));
            Add(sequence, Damage(target, 20, 2, "Child"));
            Add(sequence, Damage(target, 10, 3, "Parent"));
            Add(sequence, BattleEventHub.Record(new object(), new StatusEventData(new StatusInstance(), target, true, "Applied")));
            Add(sequence, Damage(target, 10, 4, "Parent")); Seal(sequence);
            var cues = BattlePresentationTimeline.Build(sequence, _ => new BattlePresentationTiming(.1, .5));
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4 }, cues.Select(cue => cue.Result.Order));
            Assert.That(cues.Select(cue => cue.Block), Is.Ordered);
        }

        [Test]
        public void HPChangeFollowsItsOwnDamage_AndDuplicatesAreNotPlayedTwice()
        {
            var target = Target(1); var sequence = new BattlePresentationSequence();
            var damage = Damage(target, 10, 1);
            Add(sequence, damage); Add(sequence, damage);
            var hp = BattleEventHub.Record(new object(), new HPChangedEventData(target, 20, 10, "Damage"), causes: new[] { damage.Header.EventId });
            Add(sequence, hp); Seal(sequence);
            var cues = BattlePresentationTimeline.Build(sequence, _ => new BattlePresentationTiming(0, .2));
            Assert.That(cues.Count, Is.EqualTo(2));
            Assert.That(cues[1].Time, Is.EqualTo(cues[0].Time));
            Assert.That(cues[1].Block, Is.EqualTo(cues[0].Block));
        }

        [Test]
        public void Playback_WaitsForRequiredTailButIgnoresDuplicateAndLateCallbacks()
        {
            var sequence = new BattlePresentationSequence(); var target = Target(1);
            Add(sequence, Damage(target, 10, 1, "A")); Add(sequence, Damage(target, 20, 2, "B")); Seal(sequence);
            var cues = BattlePresentationTimeline.Build(sequence);
            var shown = new List<int>(); var callbacks = new List<Action<Exception>>();
            var playback = new BattlePresentationPlayback((id, cue, done) => { shown.Add(cue.Result.Order); callbacks.Add(done); }, _ => { });
            var first = playback.Enqueue(cues);
            CollectionAssert.AreEqual(new[] { 0 }, shown);
            callbacks[0](null); callbacks[0](null);
            CollectionAssert.AreEqual(new[] { 0, 1 }, shown);
            Assert.That(first.IsCompleted, Is.False);
            playback.Cancel(); Assert.That(first.IsCanceled, Is.True);
            callbacks[1](null); Assert.That(playback.IsBusy, Is.False);
        }

        [Test]
        public void SkipReleasesWait_CancelTokenDoesNotAffectNextPlayback()
        {
            var sequence = new BattlePresentationSequence(); var target = Target(1);
            Add(sequence, Damage(target, 10, 1)); Add(sequence, Damage(target, 10, 2)); Seal(sequence);
            var cues = BattlePresentationTimeline.Build(sequence, _ => new BattlePresentationTiming(10, 0));
            var playback = new BattlePresentationPlayback((id, cue, done) => done(null), _ => { });
            var source = new CancellationTokenSource();
            var first = playback.Enqueue(cues, source.Token);
            source.Cancel(); Assert.That(first.IsCanceled, Is.True);
            var second = playback.Enqueue(cues); playback.Skip();
            Assert.That(second.IsCompleted && !second.IsCanceled, Is.True);
            playback.Advance(100); Assert.That(playback.IsBusy, Is.False);
            source.Dispose();
        }
    }
}
#endif
