using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    /// <summary>公共开关、全队存活资格、真实月绽放与草露。仅编写，须在Unity人工运行。</summary>
    public sealed class LunarConversionAndBloomTests
    {
        [TestCase(40022, "LunarConductActive", ReactionType.ElectroCharged, ReactionType.LunarElectroCharged, "Hydro", "Electro")]
        [TestCase(40022, "LunarConductActive", ReactionType.ElectroCharged, ReactionType.LunarElectroCharged, "Electro", "Hydro")]
        [TestCase(40023, "LunarBloomActive", ReactionType.Bloom, ReactionType.LunarBloom, "Hydro", "Dendro")]
        [TestCase(40023, "LunarBloomActive", ReactionType.Bloom, ReactionType.LunarBloom, "Dendro", "Hydro")]
        [TestCase(40024, "LunarCrystallizeActive", ReactionType.Crystallize, ReactionType.LunarCrystallize, "Geo", "Hydro")]
        [TestCase(40024, "LunarCrystallizeActive", ReactionType.Crystallize, ReactionType.LunarCrystallize, "Hydro", "Geo")]
        public void Selection_UsesAllLivingMembersIncludingReserve_AndRechecksStatusAndLife(
            int id, string markerID, ReactionType ordinary, ReactionType lunar, string attack, string other)
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                var reserve = env.CreateAlly(2, false);
                var marker = AddMarker(env, reserve.Entity, id, markerID);
                env.Manager.StartBattle();
                var context = Context(env, attack);
                var random = new SequenceBattleRandomSource(new[] { .5f });
                BattleRandom.SetSource(random);
                Assert.That(LunarReactionConversion.HasActiveTalent(lunar), Is.True);
                Assert.That(LunarReactionConversion.SelectReaction(context, ordinary, other), Is.EqualTo(lunar));
                reserve.IsActive = true;
                Assert.That(LunarReactionConversion.SelectReaction(context, ordinary, other), Is.EqualTo(lunar));
                reserve.Entity.CurrentHP = 0f;
                Assert.That(LunarReactionConversion.SelectReaction(context, ordinary, other), Is.EqualTo(ordinary));
                reserve.Entity.CurrentHP = 1000f;
                marker.IsActive = false;
                Assert.That(LunarReactionConversion.SelectReaction(context, ordinary, other), Is.EqualTo(ordinary));
                marker.IsActive = true;
                reserve.Entity.RemoveStatusInstance(marker);
                Assert.That(LunarReactionConversion.SelectReaction(context, ordinary, other), Is.EqualTo(ordinary));
                Assert.That(random.FloatCallCount, Is.Zero, "资格查询不抽概率");
                Assert.That(random.IntCallCount, Is.Zero);
            }
        }

        [Test]
        public void TalentModifiers_InitializeOnlyUnlockedSelectedMarker_AsPermanentHiddenStatus()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                var seed = AddMarker(env, env.Ally.Entity, 40023, LunarReactionConversion.BloomActive);
                env.Ally.Entity.RemoveStatusInstance(seed);
                env.DataManager.TalentDict[BattleFlowTestEnv.AllyID] = new List<TalentData>
                {
                    new TalentData { CharacterID = BattleFlowTestEnv.AllyID, TalentIndex = 3,
                        UnlockAfter = 20, Modifiers = "InitiateStatus(LunarBloomActive)" }
                };
                var profile = new CharacterProfile { CharacterID = BattleFlowTestEnv.AllyID, Level = 1 };
                CharacterBuildApplier.ApplyBuild(env.Ally, profile);
                Assert.That(env.Ally.Entity.HasStatus(LunarReactionConversion.BloomActive), Is.False);
                profile.Level = 20; profile.Ascension = 0;
                CharacterBuildApplier.ApplyBuild(env.Ally, profile);
                Assert.That(env.Ally.Entity.HasStatus(LunarReactionConversion.BloomActive), Is.False,
                    "达到等级但未突破不解锁");
                profile.Ascension = 1;
                CharacterBuildApplier.ApplyBuild(env.Ally, profile);
                var marker = env.Ally.Entity.GetStatus(LunarReactionConversion.BloomActive);
                Assert.That(marker, Is.Not.Null);
                Assert.That(marker.IsPermanent, Is.True);
                Assert.That(marker.MainData.Display, Is.Zero);
                Assert.That(env.Ally.Entity.HasStatus(LunarReactionConversion.ConductActive), Is.False);
                Assert.That(env.Ally.Entity.HasStatus(LunarReactionConversion.CrystallizeActive), Is.False);
            }
        }

        [Test]
        public void Selection_DoesNotConvertEnemiesWrongPairsOrAlliedTargets_AndOtherProvidersRemain()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                var first = env.CreateAlly(2, false).Entity;
                var second = env.CreateAlly(3, false).Entity;
                AddMarker(env, first, 40024, LunarReactionConversion.CrystallizeActive);
                AddMarker(env, second, 40024, LunarReactionConversion.CrystallizeActive);
                env.Manager.StartBattle();
                var context = Context(env, "Geo");
                Assert.That(LunarReactionConversion.SelectReaction(context, ReactionType.Crystallize, "Pyro"),
                    Is.EqualTo(ReactionType.Crystallize));
                first.CurrentHP = 0;
                Assert.That(LunarReactionConversion.SelectReaction(context, ReactionType.Crystallize, "Hydro"),
                    Is.EqualTo(ReactionType.LunarCrystallize));
                context.SourceEntity = env.Enemy.Entity;
                context.SourceKind = ReactionSourceKind.EnemySkill;
                Assert.That(LunarReactionConversion.SelectReaction(context, ReactionType.Crystallize, "Hydro"),
                    Is.EqualTo(ReactionType.Crystallize));
                context = Context(env, "Geo");
                context.Target = first;
                Assert.That(LunarReactionConversion.SelectReaction(context, ReactionType.Crystallize, "Hydro"),
                    Is.EqualTo(ReactionType.Crystallize));
                context = Context(env, "Geo");
                context.CanTriggerReaction = false;
                Assert.That(LunarReactionConversion.SelectReaction(context, ReactionType.Crystallize, "Hydro"),
                    Is.EqualTo(ReactionType.Crystallize));
            }
        }

        [Test]
        public void MissingMoonCageDefinition_RejectsBeforeOrdinaryConsumptionOrEffects()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                AddMarker(env, env.Ally.Entity, 40024, LunarReactionConversion.CrystallizeActive);
                env.Manager.StartBattle();
                var target = env.Enemy.Entity;
                env.DataManager.StatusMainDict.Remove(TeamPublicStatuses.MoonCage);
                target.ApplyAura("Hydro", 1f, env.Ally.Entity.EntityID);
                Assert.Throws<InvalidOperationException>(() => CrystallizeReactionHandler.TryResolve(
                    Context(env, "Geo"), out _));
                Assert.That(target.GetAura("Hydro").AuraAmount, Is.EqualTo(1f));
                Assert.That(env.Ally.Entity.Shields, Is.Empty);
            }
        }

        [Test]
        public void RealSkill_LunarBloomGrantsOneTeamDewWithReactionCause_AndCoreDamageRemainsOrdinary()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.ConfigureAllyAction(2, "SK_Skill_LunarBloom", "SE_Skill_LunarBloom", "Hydro", "1*TotalATK,2", 10);
                env.CreateMinimalBattle();
                var reserve = env.CreateAlly(2, false).Entity;
                AddMarker(env, reserve, 40023, LunarReactionConversion.BloomActive);
                env.Ally.Entity.LunarDMGBonus = 999f;
                env.Manager.StartBattle();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                env.Enemy.Entity.ApplyAura("Dendro", 1f, env.Ally.Entity.EntityID);
                var random = new SequenceBattleRandomSource(new[] { .8f });
                BattleRandom.SetSource(random);
                long reactionID = 0;
                var changes = new List<ResourceEventData>();
                var causes = new List<long>();
                using (BattleEventHub.Subscribe<ReactionEventData>(BattleEventKind.ReactionOccurred, (header, fact) =>
                { if (fact.Reaction == ReactionType.LunarBloom) reactionID = header.EventId; }, 0))
                using (BattleEventHub.Subscribe<ResourceEventData>(BattleEventKind.ResourceChanged, (header, fact) =>
                {
                    if (fact.Resource != VerdantDewPool.ResourceID) return;
                    changes.Add(fact); causes.AddRange(header.CauseEventIds);
                }, 0))
                {
                    Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
                    Assert.That(env.Manager.TeamBonuses.VerdantDew.Value, Is.EqualTo(1));
                    Assert.That(changes.Count, Is.EqualTo(1));
                    Assert.That(changes[0].Subject, Is.Null, "全队资源不挂在触发者身上");
                    Assert.That(changes[0].TeamOwner, Is.SameAs(env.Manager.TeamBonuses));
                    Assert.That(changes[0].Actor, Is.SameAs(env.Ally.Entity));
                    Assert.That(changes[0].Added, Is.EqualTo(1d));
                    Assert.That(changes[0].IsInteger, Is.True);
                    Assert.That(causes, Does.Contain(reactionID));
                    Assert.That(reactionID, Is.GreaterThan(0));
                    Assert.That(BloomCoreSystem.ActiveCores.Count, Is.EqualTo(1));
                    var core = BloomCoreSystem.ActiveCores[0];
                    Assert.That(env.Enemy.Entity.GetAura("Dendro"), Is.Null);
                    Assert.That(env.Enemy.Entity.GetAura("Hydro"), Is.Null);
                    Assert.That(env.Enemy.Entity.CurrentHP, Is.EqualTo(950f).Within(.001f));
                    var hits = BloomCoreSystem.Detonate(core);
                    Assert.That(hits[0].ReactionType, Is.EqualTo(ReactionType.Bloom));
                    Assert.That(env.Enemy.Entity.CurrentHP, Is.EqualTo(915.6f).Within(.001f));
                    Assert.That(changes.Count, Is.EqualTo(1), "核心爆炸不能再次获得草露");
                    Assert.That(random.FloatCallCount, Is.EqualTo(1));
                    Assert.That(random.IntCallCount, Is.Zero);
                }
            }
        }

        [Test]
        public void SwirlPropagation_CanCreateLunarBloom_AndKeepsWindAttackerAsSource()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.ConfigureAllyAction(2, "SK_Skill_LunarSwirl", "SE_Skill_LunarSwirl", "Anemo", "1*TotalATK,1", 10);
                env.CreateMinimalBattle();
                var neighbour = env.CreateEnemy(2).Entity;
                AddMarker(env, env.CreateAlly(2, false).Entity, 40023, LunarReactionConversion.BloomActive);
                env.Manager.StartBattle();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                env.Enemy.Entity.ApplyAura("Hydro", 1f, env.Ally.Entity.EntityID);
                neighbour.ApplyAura("Dendro", 1f, env.Ally.Entity.EntityID);
                var random = new SequenceBattleRandomSource(new[] { .8f });
                BattleRandom.SetSource(random);
                ReactionEventData lunar = null;
                using (BattleEventHub.Subscribe<ReactionEventData>(BattleEventKind.ReactionOccurred, (header, fact) =>
                { if (fact.Reaction == ReactionType.LunarBloom) lunar = fact; }, 0))
                {
                    Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
                    Assert.That(lunar, Is.Not.Null);
                    Assert.That(lunar.Source, Is.SameAs(env.Ally.Entity));
                    Assert.That(lunar.Subject, Is.SameAs(neighbour));
                    Assert.That(env.Manager.TeamBonuses.VerdantDew.Value, Is.EqualTo(1));
                    Assert.That(BloomCoreSystem.ActiveCores.Count, Is.EqualTo(1));
                    Assert.That(BloomCoreSystem.ActiveCores[0].Position.SlotIndex, Is.EqualTo(2));
                    Assert.That(neighbour.CurrentHP, Is.EqualTo(1000f), "传播本身不额外造成月曜技能伤害");
                    Assert.That(random.FloatCallCount, Is.EqualTo(1));
                    Assert.That(random.IntCallCount, Is.Zero);
                }
            }
        }

        [Test]
        public void DirectLunarDamageTag_DoesNotProvideConversionTalentOrTeamDew()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.ConfigureAllyAction(2, "SK_Skill_DirectLunar", "SE_Skill_DirectLunar", "Dendro", "1*TotalATK,1", 10);
                env.DataManager.SkillEffectDict["SE_Skill_DirectLunar"].Param3 = "Lunar";
                env.CreateMinimalBattle();
                env.Manager.StartBattle();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                env.Enemy.Entity.ApplyAura("Hydro", 2f, env.Ally.Entity.EntityID);
                var random = new SequenceBattleRandomSource(new[] { .8f });
                BattleRandom.SetSource(random);
                var reactions = new List<ReactionType>();
                using (BattleEventHub.Subscribe<ReactionEventData>(BattleEventKind.ReactionOccurred,
                    (header, fact) => reactions.Add(fact.Reaction), 0))
                {
                    Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
                    Assert.That(reactions, Has.Member(ReactionType.Bloom));
                    Assert.That(reactions, Has.No.Member(ReactionType.LunarBloom));
                    Assert.That(env.Manager.TeamBonuses.VerdantDew.Value, Is.Zero);
                    Assert.That(random.FloatCallCount, Is.EqualTo(1));
                }
            }
        }

        [Test]
        public void DewPool_CapsAtThree_InsufficientSpendIsAtomic_AndResetDoesNotCarryResources()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                env.Manager.StartBattle();
                var pool = env.Manager.TeamBonuses.VerdantDew;
                var changes = new List<ResourceEventData>();
                using (BattleEventHub.Subscribe<ResourceEventData>(BattleEventKind.ResourceChanged, (h, fact) =>
                { if (fact.Resource == VerdantDewPool.ResourceID) changes.Add(fact); }, 0))
                {
                    Assert.That(pool.Gain(int.MaxValue, env.Ally.Entity), Is.EqualTo(3));
                    Assert.That(pool.Gain(1, env.Ally.Entity), Is.Zero);
                    Assert.That(changes[1].RequestedDelta, Is.EqualTo(1d));
                    Assert.That(changes[1].Added, Is.Zero);
                    Assert.That(pool.TrySpend(4, env.Ally.Entity), Is.False);
                    Assert.That(changes.Count, Is.EqualTo(2));
                    Assert.That(pool.Value, Is.EqualTo(3));
                    Assert.That(pool.TrySpend(2, env.Ally.Entity), Is.True);
                    Assert.That(pool.Value, Is.EqualTo(1));
                    Assert.That(changes[2].Reason, Is.EqualTo(ResourceChangeReason.Spend));
                    Assert.That(changes[2].Removed, Is.EqualTo(2d));
                    Assert.That(pool.TrySpend(0, env.Ally.Entity), Is.True);
                    Assert.That(pool.Gain(0, env.Ally.Entity), Is.Zero);
                    Assert.That(changes.Count, Is.EqualTo(3));
                    Assert.Throws<ArgumentOutOfRangeException>(() => pool.Gain(-1, env.Ally.Entity));
                    env.Manager.ResetBattle();
                    Assert.That(pool.Value, Is.Zero);
                    Assert.That(changes.Count, Is.EqualTo(3), "重置不伪造清空资源声明");
                    Assert.Throws<InvalidOperationException>(() => pool.Gain(1, env.Ally.Entity));
                }
            }
        }

        private static ReactionContext Context(BattleFlowTestEnv env, string element) => new ReactionContext
        {
            SourceEntity = env.Ally.Entity, Target = env.Enemy.Entity,
            SourceKind = ReactionSourceKind.CharacterSkill, AttackElement = element, AttackAmount = 1f
        };

        private static StatusInstance AddMarker(BattleFlowTestEnv env, BattleEntity host, int id, string id2)
        {
            var main = new StatusMainData { StatusID = id, StatusID2 = id2, Display = 0,
                MaxCount = 1, MaxStack = 1, WhenMax = "0" };
            env.DataManager.StatusMainDict[id2] = main;
            return host.AddStatus(id2, host, 0, 2, 0, main);
        }
    }
}
