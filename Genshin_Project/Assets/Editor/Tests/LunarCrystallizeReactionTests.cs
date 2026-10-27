using System.Collections.Generic;
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    /// <summary>仅编写，不自动运行Unity。所有真实玩法用BattleManager公开技能/切换/回合入口。</summary>
    public sealed class LunarCrystallizeReactionTests
    {
        [Test]
        public void SingleEnemy_GeneratesOnItsSlotThenFreeAdjacentSlots_WithoutRefreshingOrCountingCreation()
        {
            using (var env = CreateEnv())
            {
                Start(env, 3);
                var random = new SequenceBattleRandomSource();
                BattleRandom.SetSource(random);
                Use(env); var original = env.Manager.TeamBonuses.MoonCages.ActiveCages[0];
                Use(env); Use(env);
                var handler = env.Manager.TeamBonuses.MoonCages;
                Assert.That(handler.ActiveCages, Has.Count.EqualTo(3));
                Assert.That(handler.ActiveCages[0], Is.SameAs(original));
                Assert.That(original.Status.RemainingPhaseCount, Is.EqualTo(2));
                Assert.That(handler.ActiveCages[0].Position.SlotIndex, Is.EqualTo(3));
                Assert.That(handler.ActiveCages[1].Position.SlotIndex, Is.EqualTo(2));
                Assert.That(handler.ActiveCages[2].Position.SlotIndex, Is.EqualTo(4));
                Assert.That(handler.ActiveCages[1].Position.IsOccupied, Is.False, "空格也承载月笼，不制造隐藏敌人");
                Assert.That(handler.PendingReactionCount, Is.Zero);
                Assert.That(env.Ally.Entity.Shields, Is.Empty, "月结晶不生成普通结晶盾");
                foreach (var cage in handler.ActiveCages)
                {
                    Assert.That(cage.Status.StatusID2, Is.EqualTo("MoonCage"));
                    Assert.That(cage.Status.Host, Is.SameAs(cage.Position));
                    Assert.That(cage.Status.StackCount, Is.EqualTo(1));
                    Assert.That(cage.IsGeoConstruct, Is.True);
                    Assert.That(cage.CountsTowardGeoConstructLimit, Is.False);
                    Assert.That(CountStatusSystem.IsCount(cage.Status.MainData), Is.False);
                }
                Assert.That(random.IntCallCount, Is.EqualTo(2), "只有两次补邻格抽位置，原地和满额不抽");
                Assert.That(random.FloatCallCount, Is.EqualTo(3), "创建反应只有三次主hit暴击，没有协奏暴击");
            }
        }

        [Test]
        public void BoundaryWithNoFreeNeighbour_DoesNotJumpOrRefresh_ButNonCreationReactionCounts()
        {
            using (var env = CreateEnv())
            {
                Start(env, 1);
                var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
                Use(env); Use(env); Use(env);
                var handler = env.Manager.TeamBonuses.MoonCages;
                Assert.That(handler.ActiveCages, Has.Count.EqualTo(2));
                Assert.That(handler.ActiveCages[0].Position.SlotIndex, Is.EqualTo(1));
                Assert.That(handler.ActiveCages[1].Position.SlotIndex, Is.EqualTo(2));
                Assert.That(handler.PendingReactionCount, Is.EqualTo(1));
                Assert.That(random.IntCallCount, Is.EqualTo(1), "不递归穿过格2寻找格3");
            }
        }

        [TestCase(0, 2)]
        [TestCase(1, 4)]
        public void AdjacentCreation_UsesOneDeterministicDraw_FromBothFreeNeighbours(int draw, int expectedSlot)
        {
            using (var env = CreateEnv())
            {
                Start(env, 3); Use(env);
                var random = new SequenceBattleRandomSource(intValues: new[] { draw });
                BattleRandom.SetSource(random);
                Use(env);
                Assert.That(env.Manager.TeamBonuses.MoonCages.ActiveCages[1].Position.SlotIndex, Is.EqualTo(expectedSlot));
                Assert.That(random.IntCallCount, Is.EqualTo(1));
                Assert.That(random.FloatCallCount, Is.EqualTo(1));
            }
        }

        [Test]
        public void Concerto_KillAndDamageBelongToTriggerer_NotCageCreatorOrHighestContributor()
        {
            using (var env = CreateEnv())
            {
                var later = env.CreateAlly(2);
                Start(env, 3);
                var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
                for (int i = 0; i < 5; i++) Use(env);
                env.Ally.Entity.TotalEM = 10000f; // 前两次仅记录身份，第三次才读取当前属性。
                Assert.That(env.Manager.TeamBonuses.MoonCages.PendingReactionCount, Is.EqualTo(2));
                Assert.That(env.Manager.TrySwitchActiveAlly(1), Is.True);
                env.Enemy.Entity.CurrentHP = 200f;
                var damageHits = new List<DamageEventData>();
                var ids = new HashSet<long>();
                MoonCageConcertoEventData concerto = null;
                DamageSourceInfo killer = null;
                using (BattleEventHub.Subscribe<MoonCageConcertoEventData>(BattleEventKind.MoonCageConcerto,
                    (h, fact) => { concerto = fact; Assert.That(h.CauseEventIds, Has.Count.EqualTo(3)); }, 0))
                using (BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (h, fact) =>
                {
                    if (fact.Source.ReactionType != ReactionType.LunarCrystallize) return;
                    damageHits.Add(fact); ids.Add(h.HitId);
                    Assert.That(h.CauseEventIds, Has.Count.EqualTo(1), "直接原因是本次真实协奏声明");
                }, 0))
                using (BattleEventHub.Subscribe<DeathEventData>(BattleEventKind.Death,
                    (h, fact) => { if (ReferenceEquals(fact.Subject, env.Enemy.Entity)) killer = fact.Source; }, 0))
                {
                    Use(env, 1);
                    Assert.That(concerto, Is.Not.Null);
                    Assert.That(concerto.Actor, Is.SameAs(later.Entity));
                    Assert.That(concerto.Contributors, Has.Count.EqualTo(2));
                    Assert.That(concerto.Attacks, Has.Count.EqualTo(3));
                    Assert.That(damageHits, Has.Count.EqualTo(3), "整次协奏先确定全部出手，不因同批死亡重选目标");
                    Assert.That(damageHits[0].DamageBeforeShield,
                        Is.EqualTo(17.2f * 1.6f * (6f * .6f + .3f)).Within(.01f));
                    Assert.That(ids, Has.Count.EqualTo(3), "每个月笼独立出手有独立hit身份");
                    foreach (var hit in damageHits)
                    {
                        Assert.That(hit.Source.SourceEntity, Is.SameAs(later.Entity));
                        Assert.That(hit.Source.SourceStatus.Caster, Is.SameAs(env.Ally.Entity));
                        Assert.That(hit.Source.LunarContributions[0].Source, Is.SameAs(env.Ally.Entity));
                        Assert.That(hit.Source.LunarContributions, Has.Count.EqualTo(2), "同配置ID的两个角色不能合并");
                        Assert.That(hit.Element, Is.EqualTo("Geo"));
                        Assert.That(hit.HitLanded, Is.False);
                        Assert.That(hit.Properties.IsLunar, Is.True);
                        Assert.That(hit.ElementAmount, Is.Zero);
                    }
                    Assert.That(killer, Is.Not.Null);
                    Assert.That(killer.SourceEntity, Is.SameAs(later.Entity));
                    Assert.That(random.IntCallCount, Is.EqualTo(5), "2次生成位置+3个月笼目标");
                    Assert.That(random.FloatCallCount, Is.EqualTo(12), "6次主hit+每个笼2名贡献者");
                }
            }
        }

        [Test]
        public void EachCage_ExpiresIndependentlyAfterTwoRoundsWithoutDamage_AndKeepsCreatorFactUntilLastRemoval()
        {
            using (var env = CreateEnv())
            {
                Start(env, 3); Use(env);
                var handler = env.Manager.TeamBonuses.MoonCages;
                var first = handler.ActiveCages[0];
                NextRound(env);
                Assert.That(first.Status.RemainingPhaseCount, Is.EqualTo(1));
                Use(env); var second = handler.ActiveCages[1];
                Assert.That(second.Status.RemainingPhaseCount, Is.EqualTo(2));
                NextRound(env);
                Assert.That(first.IsActive, Is.False);
                Assert.That(second.IsActive, Is.True);
                Assert.That(second.Status.RemainingPhaseCount, Is.EqualTo(1));
                Assert.That(env.Manager.TeamBonuses.Resonance.HasEnemyFieldMoonCage, Is.True);
                NextRound(env);
                Assert.That(handler.ActiveCages, Is.Empty);
                Assert.That(env.Manager.TeamBonuses.Resonance.HasEnemyFieldMoonCage, Is.False);
            }
        }

        [Test]
        public void CageThatDealsDamage_Persists_AndEmptyRangesNeitherHitNorRollCritical()
        {
            using (var env = CreateEnv())
            {
                Start(env, 3);
                var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
                Use(env); Use(env); Use(env);
                MoveEnemy(env, 5); // 格2/3的攻击范围内无活目标；格4能打格5。
                MoonCageConcertoEventData concerto = null;
                using (BattleEventHub.Subscribe<MoonCageConcertoEventData>(BattleEventKind.MoonCageConcerto,
                    (h, fact) => concerto = fact, 0))
                {
                    Use(env); Use(env); Use(env);
                    Assert.That(concerto.Attacks[0].Target, Is.Null);
                    Assert.That(concerto.Attacks[1].Target, Is.Null);
                    Assert.That(concerto.Attacks[2].Target, Is.SameAs(env.Enemy.Entity));
                    Assert.That(random.IntCallCount, Is.EqualTo(3));
                    Assert.That(random.FloatCallCount, Is.EqualTo(7), "6次主hit+唯一可出手月笼的一次暴击");
                    NextRound(env); NextRound(env);
                    var remaining = env.Manager.TeamBonuses.MoonCages.ActiveCages;
                    Assert.That(remaining, Has.Count.EqualTo(1));
                    Assert.That(remaining[0].Position.SlotIndex, Is.EqualTo(4));
                    Assert.That(remaining[0].HasDealtDamage, Is.True);
                    Assert.That(remaining[0].Status.IsPermanent, Is.True);
                    NextRound(env);
                    Assert.That(remaining[0].IsActive, Is.True, "已造成伤害的月笼不进入新的闲置倒计时");
                }
            }
        }

        [Test]
        public void RealMultiTargetGeoHit_CountsActualContacts_NotOnePerSkillOrOnePerHit()
        {
            using (var env = CreateEnv())
            {
                var middle = env.CreateEnemy(3, 10000f).Entity;
                var last = env.CreateEnemy(5, 10000f).Entity;
                Start(env, 1);
                var targets = new[] { env.Enemy.Entity, middle, last };
                var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
                int concertos = 0;
                using (BattleEventHub.Subscribe<MoonCageConcertoEventData>(BattleEventKind.MoonCageConcerto,
                    (h, fact) => concertos++, 0))
                {
                    for (int pass = 0; pass < 2; pass++)
                    {
                        foreach (var target in targets) ApplyHydro(target, env.Ally.Entity);
                        env.Ally.ForcedTargetPositions.Clear();
                        env.Ally.ForcedTargetPositions.AddRange(new[] { 5, 1, 3 });
                        Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
                    }
                    Assert.That(env.Manager.TeamBonuses.MoonCages.ActiveCages, Has.Count.EqualTo(3));
                    Assert.That(env.Manager.TeamBonuses.MoonCages.PendingReactionCount, Is.Zero);
                    Assert.That(concertos, Is.EqualTo(1));
                    Assert.That(random.IntCallCount, Is.EqualTo(3));
                    Assert.That(random.FloatCallCount, Is.EqualTo(9));
                }
            }
        }

        [Test]
        public void ZeroDamageConcerto_DoesNotMakeCagesPermanent()
        {
            using (var env = CreateEnv())
            {
                Start(env, 3);
                env.DataManager.ReactionLevelCoefficientDict[1] = 0f;
                for (int i = 0; i < 6; i++) Use(env);
                var handler = env.Manager.TeamBonuses.MoonCages;
                foreach (var cage in handler.ActiveCages)
                {
                    Assert.That(cage.HasDealtDamage, Is.False);
                    Assert.That(cage.Status.IsPermanent, Is.False);
                }
                NextRound(env); NextRound(env);
                Assert.That(handler.ActiveCages, Is.Empty);
            }
        }

        [Test]
        public void PublicState_RemovalImmediatelyUpdatesExistence_AndRejectsEmptyManualMounts()
        {
            using (var env = CreateEnv())
            {
                Start(env, 3); Use(env); Use(env);
                var handler = env.Manager.TeamBonuses.MoonCages;
                var first = handler.ActiveCages[0]; var second = handler.ActiveCages[1];
                first.Position.RemoveStatusInstance(first.Status);
                Assert.That(handler.ActiveCages, Has.Count.EqualTo(1));
                Assert.That(env.Manager.TeamBonuses.Resonance.HasEnemyFieldMoonCage, Is.True);
                second.Position.RemoveStatusInstance(second.Status);
                Assert.That(handler.ActiveCages, Is.Empty);
                Assert.That(env.Manager.TeamBonuses.Resonance.HasEnemyFieldMoonCage, Is.False);
                var main = env.DataManager.StatusMainDict[TeamPublicStatuses.MoonCage];
                Assert.Throws<System.InvalidOperationException>(() => env.Ally.Entity.AddStatus(main.StatusID2, env.Ally.Entity, 2, 2, 0, main));
                Assert.Throws<System.InvalidOperationException>(() => first.Position.AddStatus(main.StatusID2, env.Ally.Entity, 2, 2, 0, main));
                Assert.Throws<System.InvalidOperationException>(() => env.Manager.EnemyTeam.AddStatus(main.StatusID2, env.Ally.Entity, 2, 2, 0, main));
            }
        }

        [Test]
        public void Reset_DiscardsPendingOldSessionContact_WithoutCreatingStatusOrDrawingRandom()
        {
            using (var env = CreateEnv())
            {
                Start(env, 3);
                ApplyHydro(env.Enemy.Entity, env.Ally.Entity);
                var result = ReactionResolver.Resolve(new ReactionContext { SourceEntity = env.Ally.Entity,
                    Target = env.Enemy.Entity, AttackElement = "Geo", AttackAmount = 2f,
                    SourceKind = ReactionSourceKind.CharacterSkill, SourceEffectID = "SE_Skill_MoonCage" });
                var old = env.Manager.TeamBonuses.MoonCages;
                env.Manager.ResetBattle();
                var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
                ReactionEffectExecutor.ExecuteDerivedHits(result);
                Assert.That(old.ActiveCages, Is.Empty);
                foreach (var slot in env.Manager.Field.EnemySlots)
                    Assert.That(slot.StatusList.Exists(s => s.StatusID2 == TeamPublicStatuses.MoonCage), Is.False);
                Assert.That(random.IntCallCount, Is.Zero);
                Assert.That(random.FloatCallCount, Is.Zero);
            }
        }

        private static BattleFlowTestEnv CreateEnv()
        {
            var env = new BattleFlowTestEnv();
            env.ConfigureAllyAction(2, "SK_Skill_MoonCage", "SE_Skill_MoonCage", "Geo", "1*TotalATK,2", 1);
            env.CreateMinimalBattle();
            env.Enemy.Entity.TotalHP = env.Enemy.Entity.CurrentHP = 10000f;
            var main = new StatusMainData { StatusID = 40024, StatusID2 = LunarReactionConversion.CrystallizeActive,
                MaxStack = 1, MaxCount = 1, WhenMax = "0", Display = 0 };
            env.DataManager.StatusMainDict[main.StatusID2] = main;
            env.Ally.Entity.AddStatus(main.StatusID2, env.Ally.Entity, 0, 2, 0, main);
            return env;
        }
        private static void Start(BattleFlowTestEnv env, int position)
        {
            env.Manager.StartBattle();
            env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            MoveEnemy(env, position);
            BattleRandom.SetSource(new SequenceBattleRandomSource());
        }
        private static void MoveEnemy(BattleFlowTestEnv env, int position)
        {
            var enemy = env.Enemy.Entity;
            if (enemy.Position != null && ReferenceEquals(enemy.Position.Occupant, enemy)) enemy.Position.Occupant = null;
            enemy.SlotPosition = position;
            enemy.Position = env.Manager.Field.GetSlot(BattleSide.Enemy, position);
            enemy.Position.Occupant = enemy;
        }
        private static void Use(BattleFlowTestEnv env, int actor = 0)
        {
            ApplyHydro(env.Enemy.Entity, env.Ally.Entity);
            var source = env.Manager.Allies[actor];
            source.ForcedTargetPositions.Clear();
            source.ForcedTargetPositions.Add(env.Enemy.Entity.SlotPosition);
            Assert.That(env.Manager.UseSkillBySlot(actor), Is.True);
        }
        private static void ApplyHydro(BattleEntity target, BattleEntity source)
            => target.ApplyAura("Hydro", 10f, source.EntityID, sourceEntity: source);
        private static void NextRound(BattleFlowTestEnv env)
        { env.Manager.EndAllyTurn(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction); }
    }
}
