using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using UnityEngine;

namespace GenshinTurnBased.Tests.EditMode
{
    public class ManualBattleFlowSchedulerTests
    {
        [Test]
        public void BattleFlowTests_RunInEditMode()
        {
            Assert.That(Application.isPlaying, Is.False);
        }

        [Test]
        public void NestedEnumerators_ExecuteInUnityOrder()
        {
            var scheduler = new ManualBattleFlowScheduler();
            var order = new List<string>();
            scheduler.Start(NestedRoot(order));

            scheduler.Step();
            CollectionAssert.AreEqual(new[] { "root-before", "child-before" }, order);
            scheduler.Step();
            CollectionAssert.AreEqual(new[] { "root-before", "child-before", "child-after", "root-after" }, order);
            Assert.That(scheduler.IsRunning, Is.False);
        }

        [Test]
        public void NullYield_ResumesOnNextManualStep()
        {
            var scheduler = new ManualBattleFlowScheduler();
            var order = new List<string>();
            scheduler.Start(NullBoundary(order));

            scheduler.Step();
            CollectionAssert.AreEqual(new[] { "before" }, order);
            scheduler.Step();
            CollectionAssert.AreEqual(new[] { "before", "after" }, order);
        }

        [Test]
        public void WaitForSeconds_DoesNotUseWallClockButPreservesOrder()
        {
            var scheduler = new ManualBattleFlowScheduler();
            var order = new List<string>();
            scheduler.Start(WaitBoundary(order));
            var stopwatch = Stopwatch.StartNew();

            scheduler.Step();
            scheduler.Step();

            stopwatch.Stop();
            CollectionAssert.AreEqual(new[] { "before", "after" }, order);
            Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(1)));
        }

        [Test]
        public void Stop_ClearsRootAndNestedRoutines()
        {
            var scheduler = new ManualBattleFlowScheduler();
            var order = new List<string>();
            scheduler.Start(NestedRoot(order));
            scheduler.Step();

            scheduler.Stop();

            Assert.That(scheduler.IsRunning, Is.False);
            Assert.That(scheduler.Step(), Is.False);
            CollectionAssert.AreEqual(new[] { "root-before", "child-before" }, order);
        }

        [Test]
        public void AdvanceUntil_FailsWithDiagnosticsAtMaxSteps()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.Scheduler.Start(InfiniteRoutine());
                AssertionException error = Assert.Throws<AssertionException>(() =>
                    env.Flow.AdvanceUntil(env.Manager, () => false, "never true", 2));

                StringAssert.Contains("never true", error.Message);
                StringAssert.Contains("current phase=", error.Message);
                StringAssert.Contains("IsCurrentPhaseReady=", error.Message);
                StringAssert.Contains("IsBattleRunning=", error.Message);
                StringAssert.Contains("IsBattleOver=", error.Message);
                StringAssert.Contains("steps=2", error.Message);
            }
        }

        [Test]
        public void StartWhileRunning_DoesNotCreateSecondFlow()
        {
            var scheduler = new ManualBattleFlowScheduler();
            var first = new List<string>();
            var second = new List<string>();
            scheduler.Start(NullBoundary(first));

            Assert.Throws<InvalidOperationException>(() => scheduler.Start(NullBoundary(second)));
            scheduler.Step();
            scheduler.Step();

            CollectionAssert.AreEqual(new[] { "before", "after" }, first);
            Assert.That(second, Is.Empty);
        }

        private static IEnumerator NestedRoot(List<string> order)
        {
            order.Add("root-before");
            yield return NestedChild(order);
            order.Add("root-after");
        }

        private static IEnumerator NestedChild(List<string> order)
        {
            order.Add("child-before");
            yield return null;
            order.Add("child-after");
        }

        private static IEnumerator NullBoundary(List<string> order)
        {
            order.Add("before");
            yield return null;
            order.Add("after");
        }

        private static IEnumerator WaitBoundary(List<string> order)
        {
            order.Add("before");
            yield return new WaitForSeconds(60f);
            order.Add("after");
        }

        private static IEnumerator InfiniteRoutine()
        {
            while (true) yield return null;
        }
    }
}
