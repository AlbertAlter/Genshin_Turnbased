using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class AllyControlReleaseTests
    {
        private GameObject root;
        private BattleEntity ally;
        private ActionPointManager ap;

        [SetUp] public void SetUp()
        {
            ReactionResolver.ResetSession();
            root = new GameObject("AllyControlRelease");
            ally = root.AddComponent<BattleEntity>();
            ally.Type = BattleEntity.EntityType.Character;
            ally.Side = BattleSide.Ally;
            ally.TotalHP = ally.CurrentHP = 1000;
            ap = new ActionPointManager();
        }

        [TearDown] public void TearDown()
        {
            ReactionResolver.ResetSession();
            Object.DestroyImmediate(root);
        }

        private ReactionStateInstance Freeze() => ReactionStateSystem.SetEntityState(
            ally, ReactionType.Frozen, 2, new ReactionSourceSnapshot());
        private StatusInstance Fall() => ally.AddStatus(AirborneStatusHandler.CharacterStatusID, ally, 1, 2, 0, null);

        [TestCase(true)]
        [TestCase(false)]
        public void OldestInstanceOwnsProgressAndPayment_IncludingMultipleAirborne(bool frozenFirst)
        {
            var frozen = frozenFirst ? Freeze() : null;
            var firstFall = Fall();
            if (!frozenFirst) frozen = Freeze();
            var secondFall = Fall();
            var initial = AllyControlRelease.GetOldest(ally);
            Assert.That(initial.VisibleAirborne, Is.SameAs(firstFall), "击飞星星在冻结先施加时也必须可见。");
            Assert.That(initial.Frozen != null, Is.EqualTo(frozenFirst), "显示击飞不能改变当前解除顺序。");
            long[] orders = frozenFirst
                ? new[] { frozen.ApplyOrder, firstFall.ApplyOrder, secondFall.ApplyOrder }
                : new[] { firstFall.ApplyOrder, frozen.ApplyOrder, secondFall.ApplyOrder };
            for (int click = 0; click < 9; click++)
            {
                if (click == 2)
                {
                    Assert.That(AllyControlRelease.Click(ally, ap, out var rejected), Is.EqualTo(AllyControlClickResult.InsufficientAP));
                    Assert.That(rejected.Clicks, Is.EqualTo(2));
                    ap.ResetAP();
                }
                var expected = click % 3 == 2 ? AllyControlClickResult.Released : AllyControlClickResult.Progressed;
                Assert.That(AllyControlRelease.Click(ally, ap, out var handled), Is.EqualTo(expected));
                Assert.That(handled.Clicks, Is.EqualTo(click % 3 + 1));
                Assert.That(handled.Airborne?.ApplyOrder ?? handled.Frozen.ApplyOrder, Is.EqualTo(orders[click / 3]));
            }
            Assert.That(ap.CurrentAP, Is.EqualTo(70));
            Assert.That(AllyControlRelease.GetOldest(ally).Exists, Is.False);
            Assert.That(AllyControlRelease.GetOldest(ally).VisibleAirborne, Is.Null);
            Assert.That(AllyControlRelease.Click(ally, ap, out _), Is.EqualTo(AllyControlClickResult.Ignored));
        }

        [Test] public void NaturalRemovalAndReapplication_StartsNewProgress_AndOffFieldCannotClick()
        {
            ap.ResetAP();
            var first = Fall();
            AllyControlRelease.Click(ally, ap, out _);
            AllyControlRelease.Click(ally, ap, out _);
            ally.RemoveStatusInstance(first);
            var second = Fall();
            Assert.That(second.ControlReleaseClicks, Is.Zero);
            ally.AddStatus(PublicControlStates.OffField, ally, 1, 2, 0, null);
            Assert.That(AllyControlRelease.Click(ally, ap, out _), Is.EqualTo(AllyControlClickResult.Ignored));
            Assert.That(ap.CurrentAP, Is.EqualTo(100));
            Assert.That(second.ControlReleaseClicks, Is.Zero);
        }

        [Test] public void ShieldRejectsNewAirborne_WithoutRemovingEarlierControlOrDeclaringAdded()
        {
            const string fall = AirborneStatusHandler.CharacterStatusID;
            var existing = Fall();
            ally.AddShield(30, "Geo", 1);
            int resisted = 0, added = 0;
            using var immunity = BattleEventHub.Subscribe<ShieldResistedStatusEventData>(BattleEventKind.ShieldResistedStatus,
                (_, fact) => { Assert.That(fact.Subject, Is.SameAs(ally)); resisted++; }, 0);
            using var applied = BattleEventHub.Subscribe<StatusEventData>(BattleEventKind.StatusAdded, (_, fact) => added++, 0);
            Assert.That(ally.AddStatus(fall, ally, 1, 2, 0, null), Is.Null);
            Assert.That(ally.AddStatus(fall, ally, 1), Is.Null);
            Assert.That(ally.HasStatusInstance(existing), Is.True);
            Assert.That(resisted, Is.EqualTo(2));
            Assert.That(added, Is.Zero);
            Assert.That(ally.AddStatus(AirborneStatusHandler.EnemyStatusID, ally, 1, 2, 0, null), Is.Not.Null);
            Assert.That(resisted, Is.EqualTo(2));
        }

        [Test] public void PublicBattleEntryPaysOnce_AndLegacyThawCannotSkipOlderAirborne()
        {
            using var env = new BattleFlowTestEnv();
            env.CreateMinimalBattle();
            env.Manager.StartBattle();
            env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            var target = env.Ally.Entity;
            target.AddStatus(AirborneStatusHandler.CharacterStatusID, target, 1, 2, 0, null);
            ReactionStateSystem.SetEntityState(target, ReactionType.Frozen, 2, new ReactionSourceSnapshot());
            var costs = new List<APSpentEventData>();
            using var payments = BattleEventHub.Subscribe<APSpentEventData>(BattleEventKind.APSpent, (_, fact) => costs.Add(fact), 0);
            Assert.That(env.Manager.TryThawAlly(target, out _), Is.False);
            for (int click = 0; click < 3; click++)
                Assert.That(env.Manager.TryClickControlAlly(target, out _), Is.True);
            Assert.That(costs.Count, Is.EqualTo(1));
            Assert.That(costs[0].Amount, Is.EqualTo(10));
            Assert.That(costs[0].SpendKind, Is.EqualTo(APSpendKind.RecoverFromAirborne));
            Assert.That(FrozenReactionHandler.IsFrozen(target), Is.True);
            for (int click = 0; click < 3; click++)
                Assert.That(env.Manager.TryThawAlly(target, out _), Is.True);
            Assert.That(costs.Count, Is.EqualTo(2));
            Assert.That(costs[1].SpendKind, Is.EqualTo(APSpendKind.Thaw));
        }
    }
}
