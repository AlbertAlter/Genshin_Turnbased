using System.Collections.Generic;
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    /// <summary>仅编写；按项目规则不自动启动Unity。公式、真实技能、多目标、计时与扩散共存回归。</summary>
    public sealed class LunarElectroChargedReactionTests
    {
        [TearDown]
        public void TearDown() => BattleRandom.ResetSource();

        [Test]
        public void Formula_RanksActualCriticalScores_AndDoesNotNormalizeWeights()
        {
            var random = new SequenceBattleRandomSource(new[] { .9f, .1f, .9f, .9f });
            BattleRandom.SetSource(random);
            var snapshots = new[]
            {
                Snapshot(100f), Snapshot(60f, 1f, 2f), Snapshot(50f), Snapshot(25f)
            };
            float damage = LunarElectroChargedReactionHandler.CalculateDamage(snapshots, 1f, out var detail);
            Assert.That(damage, Is.EqualTo(1080f * .6f + 600f * .3f + 300f * .05f + 150f * .05f).Within(.001f));
            Assert.That(detail[0].Critical.IsCritical, Is.True);
            Assert.That(detail[0].Score, Is.EqualTo(1080f).Within(.001f));
            Assert.That(detail[1].Score, Is.EqualTo(600f).Within(.001f));
            Assert.That(random.FloatCallCount, Is.EqualTo(4));
            float single = LunarElectroChargedReactionHandler.CalculateDamage(new[] { Snapshot(100f) }, 1f, out _);
            Assert.That(single, Is.EqualTo(360f).Within(.001f), "单来源也只取60%，不归一化");
            Assert.That(random.FloatCallCount, Is.EqualTo(5));
            Assert.That(random.IntCallCount, Is.Zero);
        }

        [Test]
        public void Formula_UsesLunarIndependentBrackets_NotOrdinaryDamageBonus()
        {
            BattleRandom.SetSource(new SequenceBattleRandomSource());
            var bonuses = new ReactionBuffTotals { BaseDMGBonus = .2f, BaseDMGBonusFlat = 30f,
                LunarDMGBonus = .4f, Elevation = .1f, DMGBonus = 999f, StellarDMGBonus = 999f };
            var snapshot = new LunarContributionSnapshot(null, 100f, 200f, bonuses, 0f, 0f);
            float damage = LunarElectroChargedReactionHandler.CalculateDamage(new[] { snapshot }, .8f, out _);
            float expected = (100f * 6f * 1.2f * (1f + 6f * 200f / 2200f + .4f) + 30f) * .8f * 1.1f * .6f;
            Assert.That(damage, Is.EqualTo(expected).Within(.001f));
        }

        [Test]
        public void RealMultiTargetHit_TracesDistinctInstancesWithSameID_AndCreatesOneCloud()
        {
            using (var env = CreateHydroBattle())
            {
                var second = env.CreateEnemy(2, 5000f).Entity;
                var firstSource = env.CreateAlly(2).Entity;
                var secondSource = env.CreateAlly(3).Entity;
                Start(env);
                Apply(env.Enemy.Entity, "Electro", 2f, firstSource);
                Apply(second, "Electro", 2f, secondSource);
                env.Ally.ForcedTargetPositions.Add(1);
                env.Ally.ForcedTargetPositions.Add(2);
                var random = new SequenceBattleRandomSource();
                BattleRandom.SetSource(random);
                int added = 0;
                var lunarHits = new List<DamageEventData>();
                using (BattleEventHub.Subscribe<StatusEventData>(BattleEventKind.StatusAdded, (h, fact) =>
                { if (fact.StatusId == TeamPublicStatuses.Thundercloud) added++; }, 0))
                using (BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (h, fact) =>
                { if (fact.Source.ReactionType == ReactionType.LunarElectroCharged) lunarHits.Add(fact); }, 0))
                {
                    Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
                    var cloud = env.Manager.TeamBonuses.Thundercloud;
                    Assert.That(added, Is.EqualTo(1));
                    Assert.That(cloud.Contributors, Has.Count.EqualTo(3));
                    Assert.That(cloud.Cloud.Host, Is.SameAs(env.Manager.EnemyTeam));
                    Assert.That(env.Manager.EnemyTeam.GetStatusInstances(TeamPublicStatuses.Thundercloud), Has.Count.EqualTo(1));
                    Assert.That(lunarHits, Has.Count.EqualTo(2));
                    foreach (var hit in lunarHits)
                    {
                        Assert.That(hit.Source.SourceEntity, Is.SameAs(env.Ally.Entity));
                        Assert.That(hit.Source.LunarContributions, Has.Count.EqualTo(3));
                        Assert.That(hit.Properties.IsLunar, Is.True);
                        Assert.That(hit.HitLanded, Is.False, "云伤害不冒充攻击命中");
                        Assert.That(hit.ElementAmount, Is.Zero);
                    }
                    Assert.That(env.Enemy.Entity.GetAura("Electro").AuraAmount, Is.EqualTo(1.6f).Within(.001f));
                    Assert.That(env.Enemy.Entity.GetAura("Hydro").AuraAmount, Is.EqualTo(1.6f).Within(.001f));
                    Assert.That(ElectroChargedReactionHandler.IsElectroCharged(env.Enemy.Entity), Is.False);
                    Assert.That(random.FloatCallCount, Is.EqualTo(8), "两次主命中 + 三贡献者乘两个目标");
                    Assert.That(random.IntCallCount, Is.Zero);
                }
            }
        }

        [Test]
        public void RealKill_BelongsToCreator_EvenWhenAnotherContributorScoresHigher()
        {
            using (var env = CreateHydroBattle())
            {
                var stronger = env.CreateAlly(2).Entity;
                stronger.TotalEM = 2000f;
                env.CreateEnemy(2, 5000f);
                Start(env);
                env.Enemy.Entity.CurrentHP = 80f;
                Apply(env.Enemy.Entity, "Electro", 2f, stronger);
                DamageSourceInfo killer = null;
                using (BattleEventHub.Subscribe<DeathEventData>(BattleEventKind.Death, (h, fact) =>
                { if (ReferenceEquals(fact.Subject, env.Enemy.Entity)) killer = fact.Source; }, 0))
                {
                    Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
                    Assert.That(killer, Is.Not.Null);
                    Assert.That(killer.SourceEntity, Is.SameAs(env.Ally.Entity));
                    Assert.That(killer.LunarContributions[0].Source, Is.SameAs(stronger));
                    Assert.That(killer.ReactionType, Is.EqualTo(ReactionType.LunarElectroCharged));
                }
            }
        }

        [Test]
        public void TurnEnds_HitAllCoexistingEnemies_KeepSnapshotAndReactionCauses_ThenExpireOnce()
        {
            using (var env = CreateHydroBattle())
            {
                var other = env.CreateEnemy(2, 5000f).Entity;
                Start(env);
                Apply(env.Enemy.Entity, "Electro", 2f, env.Ally.Entity);
                Apply(other, "Hydro", 2f, env.Ally.Entity);
                Apply(other, "Electro", 2f, env.Ally.Entity);
                long reactionID = 0;
                var hitIDs = new HashSet<long>();
                int reactions = 0, cloudHits = 0;
                using (BattleEventHub.Subscribe<ReactionEventData>(BattleEventKind.ReactionOccurred, (h, fact) =>
                { if (fact.Reaction == ReactionType.LunarElectroCharged) { reactions++; reactionID = h.EventId; } }, 0))
                using (BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (h, fact) =>
                {
                    if (fact.Source.ReactionType != ReactionType.LunarElectroCharged) return;
                    cloudHits++; hitIDs.Add(h.HitId);
                    // 即时伤害可先记录、后派发反应；原因仍应指向真实同场声明。
                    Assert.That(h.CauseEventIds, Is.Not.Empty);
                }, 0))
                {
                    Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
                    Assert.That(other.CurrentHP, Is.EqualTo(5000f), "新云仅即时打本次触发目标");
                    var cloud = env.Manager.TeamBonuses.Thundercloud;
                    var instance = cloud.Cloud;
                    float hp = env.Enemy.Entity.CurrentHP;
                    env.Ally.Entity.TotalEM = 9000f;
                    env.Ally.Entity.LunarDMGBonus = 99f;
                    env.Manager.EndAllyTurn();
                    env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                    Assert.That(env.Enemy.Entity.CurrentHP, Is.EqualTo(hp - 2f * 17.2f * 6f * .6f).Within(.01f));
                    Assert.That(other.CurrentHP, Is.EqualTo(5000f - 2f * 17.2f * 6f * .6f).Within(.01f));
                    Assert.That(cloudHits, Is.EqualTo(5));
                    Assert.That(hitIDs, Has.Count.EqualTo(3), "即时与两次回合末各有独立hit身份，同批目标共享身份");
                    Assert.That(reactions, Is.EqualTo(1), "周期伤害不再声明新反应");
                    Assert.That(reactionID, Is.GreaterThan(0));
                    Assert.That(instance.RemainingPhaseCount, Is.EqualTo(1));
                    Assert.That(env.Enemy.Entity.GetAura("Hydro").AuraAmount, Is.EqualTo(.3f).Within(.001f));
                    env.Manager.EndAllyTurn();
                    env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                    Assert.That(cloud.IsActive, Is.False);
                    Assert.That(env.Manager.EnemyTeam.HasStatus(TeamPublicStatuses.Thundercloud), Is.False);
                    env.Manager.ResetBattle();
                    Assert.That(cloud.IsActive, Is.False);
                }
            }
        }

        [Test]
        public void SmallAttachments_ConsumeOnlyAvailableAmount_ButStillCreateCloudAndDamage()
        {
            using (var env = CreateHydroBattle("1*TotalATK,0.2"))
            {
                Start(env);
                Apply(env.Enemy.Entity, "Electro", .3f, env.Ally.Entity);
                Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
                Assert.That(env.Enemy.Entity.GetAura("Hydro"), Is.Null);
                Assert.That(env.Enemy.Entity.GetAura("Electro"), Is.Null);
                Assert.That(env.Manager.TeamBonuses.Thundercloud.IsActive, Is.True);
                Assert.That(env.Enemy.Entity.CurrentHP, Is.EqualTo(5000f - 50f - 17.2f * 6f * .6f).Within(.01f));
            }
        }

        [Test]
        public void MissingPublicCloudDefinition_RejectsBeforeElementConsumption()
        {
            using (var env = CreateHydroBattle())
            {
                Start(env);
                Apply(env.Enemy.Entity, "Electro", 2f, env.Ally.Entity);
                env.DataManager.StatusMainDict.Remove(TeamPublicStatuses.Thundercloud);
                var context = new ReactionContext { SourceEntity = env.Ally.Entity, Target = env.Enemy.Entity,
                    SourceKind = ReactionSourceKind.CharacterSkill, AttackElement = "Hydro", AttackAmount = 2f };
                Assert.Throws<System.InvalidOperationException>(() => ElectroChargedReactionHandler.TryResolve(context, 17.2f, out _));
                Assert.That(env.Enemy.Entity.GetAura("Electro").AuraAmount, Is.EqualTo(2f));
                Assert.That(ElectroChargedReactionHandler.IsElectroCharged(env.Enemy.Entity), Is.False);
            }
        }

        [Test]
        public void NewTrigger_ReplacesWholeCloudAndContributorSnapshot_NotAccumulates()
        {
            using (var env = CreateHydroBattle())
            {
                var previous = env.CreateAlly(2).Entity;
                Start(env);
                Apply(env.Enemy.Entity, "Electro", 2f, previous);
                Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
                var handler = env.Manager.TeamBonuses.Thundercloud;
                var old = handler.Cloud;
                Assert.That(handler.Contributors, Has.Count.EqualTo(2));
                env.Enemy.Entity.RemoveAura("Hydro");
                Apply(env.Enemy.Entity, "Electro", 2f, env.Ally.Entity);
                Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
                Assert.That(handler.Cloud, Is.Not.SameAs(old));
                Assert.That(old.IsActive, Is.False);
                Assert.That(handler.Cloud.RemainingPhaseCount, Is.EqualTo(2));
                Assert.That(handler.Contributors, Has.Count.EqualTo(1));
                Assert.That(handler.Contributors[0].Source, Is.SameAs(env.Ally.Entity));
            }
        }

        [TestCase(false, 2f)]
        [TestCase(true, 2.6f)]
        public void RealSwirl_StageThreeReactsOnceWithoutInstantDamage_ThenPreservesWaterElectro(bool lunar, float residual)
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.ConfigureAllyAction(2, "SK_Skill_Residual", "SE_Skill_Residual", "Anemo", "1*TotalATK,6", 10);
                env.CreateMinimalBattle();
                env.Enemy.Entity.TotalHP = env.Enemy.Entity.CurrentHP = 5000f;
                var neighbour = env.CreateEnemy(2, 5000f).Entity;
                if (lunar) AddMarker(env);
                Start(env);
                Apply(env.Enemy.Entity, "Hydro", 3f, env.Ally.Entity);
                Apply(env.Enemy.Entity, "Electro", 3f, env.Ally.Entity);
                Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
                Assert.That(neighbour.GetAura("Hydro").AuraAmount, Is.EqualTo(residual).Within(.001f));
                Assert.That(neighbour.GetAura("Electro").AuraAmount, Is.EqualTo(residual).Within(.001f));
                Assert.That(neighbour.CurrentHP, Is.EqualTo(5000f), "第三阶段只建立机制，没有反应即时伤害");
                Assert.That(ElectroChargedReactionHandler.IsElectroCharged(neighbour), Is.EqualTo(!lunar));
                if (lunar)
                {
                    var handler = env.Manager.TeamBonuses.Thundercloud;
                    Assert.That(handler.IsActive, Is.True);
                    Assert.That(handler.Contributors, Has.Count.EqualTo(1), "同一风角色扩散水雷只算一个来源");
                    Assert.That(handler.Contributors[0].Source, Is.SameAs(env.Ally.Entity));
                }
            }
        }

        [Test]
        public void RealSwirl_StageTwoLunarHit_ConsumesOnlyPointFourAndDoesNotRepeatInStageThree()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.ConfigureAllyAction(2, "SK_Skill_Spread", "SE_Skill_Spread", "Anemo", "1*TotalATK,3", 10);
                env.CreateMinimalBattle();
                env.Enemy.Entity.TotalHP = env.Enemy.Entity.CurrentHP = 5000f;
                var neighbour = env.CreateEnemy(2, 5000f).Entity;
                var electroSource = env.CreateAlly(2).Entity;
                AddMarker(env);
                Start(env);
                Apply(env.Enemy.Entity, "Hydro", 3f, env.Ally.Entity);
                Apply(neighbour, "Electro", 3f, electroSource);
                Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
                Assert.That(neighbour.GetAura("Hydro").AuraAmount, Is.EqualTo(2.6f).Within(.001f));
                Assert.That(neighbour.GetAura("Electro").AuraAmount, Is.EqualTo(2.6f).Within(.001f));
                Assert.That(neighbour.CurrentHP, Is.EqualTo(5000f - 17.2f * 6f * .9f).Within(.01f));
                Assert.That(env.Manager.TeamBonuses.Thundercloud.Contributors, Has.Count.EqualTo(2));
            }
        }

        private static LunarContributionSnapshot Snapshot(float coefficient, float rate = 0f, float criticalDamage = 0f)
            => new LunarContributionSnapshot(null, coefficient, 0f, default, rate, criticalDamage);

        private static BattleFlowTestEnv CreateHydroBattle(string hitData = "1*TotalATK,2")
        {
            var env = new BattleFlowTestEnv();
            env.ConfigureAllyAction(2, "SK_Skill_Cloud", "SE_Skill_Cloud", "Hydro", hitData, 10);
            env.CreateMinimalBattle();
            env.Enemy.Entity.TotalHP = env.Enemy.Entity.CurrentHP = 5000f;
            AddMarker(env);
            return env;
        }
        private static void Start(BattleFlowTestEnv env)
        {
            env.Manager.StartBattle();
            env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            BattleRandom.SetSource(new SequenceBattleRandomSource());
        }
        private static void AddMarker(BattleFlowTestEnv env)
        {
            var main = new StatusMainData { StatusID = 40022, StatusID2 = LunarReactionConversion.ConductActive,
                MaxStack = 1, MaxCount = 1, WhenMax = "0", Display = 0 };
            env.DataManager.StatusMainDict[main.StatusID2] = main;
            env.Ally.Entity.AddStatus(main.StatusID2, env.Ally.Entity, 0, 2, 0, main);
        }
        private static void Apply(BattleEntity target, string element, float amount, BattleEntity source)
            => target.ApplyAura(element, amount, source.EntityID, sourceEntity: source);
    }
}
