using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    /// <summary>只编写，不自动运行Unity。查询用例不需要启动战斗或读取真实配表。</summary>
    public sealed class ElementQueryTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void AllyAndEnemy_ReadSameContract_WithExactSourceAndIndependentSnapshot(bool enemyTarget)
        {
            using (var env = CreateEnv())
            {
                var target = enemyTarget ? env.Enemy.Entity : env.Ally.Entity;
                var first = env.Ally.Entity;
                var later = env.CreateAlly(2).Entity;
                Assert.That(first.EntityID, Is.EqualTo(later.EntityID), "同配置ID不能代替实例身份");
                target.ApplyAura("Hydro", 2f, first.EntityID, "SE_First", "SK_First", first);
                var reading = ElementQuery.ReadNormal(target, "Hydro");
                Assert.That(reading.Target, Is.SameAs(target));
                Assert.That(reading.Kind, Is.EqualTo(ReactionPoolKind.NormalAura));
                Assert.That(reading.Amount, Is.EqualTo(2f));
                Assert.That(reading.IsIntrinsic, Is.False);
                Assert.That(reading.SourceEntity, Is.SameAs(first));
                Assert.That(reading.SourceEffectID, Is.EqualTo("SE_First"));
                Assert.That(reading.SourceSkillID, Is.EqualTo("SK_First"));
                target.ApplyAura("Hydro", .25f, later.EntityID, "SE_Later", "SK_Later", later);
                Assert.That(reading.Amount, Is.EqualTo(2f), "旧查询结果不是原Aura的可变引用");
                Assert.That(reading.SourceEntity, Is.SameAs(first));
                var current = ElementQuery.ReadNormal(target, "Hydro");
                Assert.That(current.Amount, Is.EqualTo(.25f));
                Assert.That(current.SourceEntity, Is.SameAs(later));
            }
        }

        [Test]
        public void NormalAndVirtualPools_StaySeparate_WithTheirOwnOriginsAndSources()
        {
            using (var env = CreateEnv())
            {
                var target = env.Enemy.Entity;
                var second = env.CreateAlly(2).Entity;
                target.ApplyAura("Cryo", 2f, env.Ally.Entity.EntityID, sourceEntity: env.Ally.Entity);
                target.ApplyAura("Pyro", 4f, env.Ally.Entity.EntityID, sourceEntity: env.Ally.Entity);
                AddVirtual(target, second, ReactionType.Frozen, 3f, 2);
                AddVirtual(target, second, ReactionType.Burning, 5f, 6);
                Assert.That(ElementQuery.GetAmount(target, "Cryo"), Is.EqualTo(2f));
                Assert.That(ElementQuery.GetAmount(target, "Cryo", ReactionPoolKind.Frozen), Is.EqualTo(3f));
                Assert.That(ElementQuery.GetReactionAmount(target, "Cryo"), Is.EqualTo(5f));
                Assert.That(ElementQuery.GetReactionAmount(target, "Cryo", ignoreFrozen: true), Is.EqualTo(2f));
                Assert.That(ElementQuery.GetReactionAmount(target, "Pyro"), Is.EqualTo(9f));
                var frozen = ElementQuery.ReadPool(target, "Cryo", ReactionPoolKind.Frozen);
                Assert.That(frozen.SourceEntity, Is.SameAs(second));
                Assert.That(frozen.OriginPhase, Is.EqualTo(2));
                Assert.That(frozen.PoolApplyOrder, Is.GreaterThan(0));
                Assert.That(ElementQuery.ReadPool(target, "Pyro", ReactionPoolKind.Frozen), Is.Null);
                Assert.That(ElementQuery.ReadPool(target, "Cryo", ReactionPoolKind.Burning), Is.Null);
                var capture = ElementQuery.Capture(target);
                Assert.That(capture, Has.Count.EqualTo(4));
                Assert.That(capture[0].Element, Is.EqualTo("Cryo"));
                Assert.That(capture[0].Kind, Is.EqualTo(ReactionPoolKind.NormalAura));
                Assert.That(capture[2].Kind, Is.EqualTo(ReactionPoolKind.Frozen));
                Assert.That(capture[3].Kind, Is.EqualTo(ReactionPoolKind.Burning));
                Assert.Throws<NotSupportedException>(() => ((IList<ElementPoolReading>)capture).Add(frozen));
                target.RemoveAura("Cryo");
                Assert.That(ElementQuery.HasNormalAura(target, "Cryo"), Is.False,
                    "虚拟冻元素不能自动成为技能/装备的普通冰附着条件");
                Assert.That(ElementQuery.GetReactionAmount(target, "Cryo"), Is.EqualTo(3f));
            }
        }

        [Test]
        public void ReactionReading_RespectsRestrictedPoolAndIgnoredFrozenElement()
        {
            using (var env = CreateEnv())
            {
                var target = env.Enemy.Entity;
                target.ApplyAura("Cryo", 2f, 0);
                target.ApplyAura("Pyro", 4f, 0);
                AddVirtual(target, env.Ally.Entity, ReactionType.Frozen, 3f, 2);
                AddVirtual(target, env.Ally.Entity, ReactionType.Burning, 5f, 2);
                var context = new ReactionContext { Target = target, RestrictedAuraElement = "Cryo",
                    RestrictedAuraKind = ReactionPoolKind.NormalAura };
                Assert.That(ElementQuery.GetReactionAmount(context, "Cryo"), Is.EqualTo(2f));
                Assert.That(ElementQuery.GetReactionAmount(context, "Pyro"), Is.Zero);
                context.RestrictedAuraKind = ReactionPoolKind.Frozen;
                Assert.That(ElementQuery.GetReactionAmount(context, "Cryo"), Is.EqualTo(3f));
                context.IgnoreFrozenAura = true;
                Assert.That(ElementQuery.GetReactionAmount(context, "Cryo"), Is.Zero);
                context.RestrictedAuraElement = "Pyro";
                context.RestrictedAuraKind = ReactionPoolKind.Burning;
                Assert.That(ElementQuery.GetReactionAmount(context, "Pyro"), Is.EqualTo(5f));
                context.RestrictedAuraKind = ReactionPoolKind.Any;
                Assert.That(ElementQuery.GetReactionAmount(context, "Pyro"), Is.EqualTo(9f));
                Assert.Throws<ArgumentException>(() => ElementQuery.ReadPool(target, "Pyro", ReactionPoolKind.Any));
            }
        }

        [Test]
        public void Queries_DoNotChangeAmountsSourcesTimersDeclarationsOrRandomSequence()
        {
            using (var env = CreateEnv())
            {
                var target = env.Enemy.Entity;
                target.ApplyAura("Hydro", 2f, env.Ally.Entity.EntityID, sourceEntity: env.Ally.Entity);
                AddVirtual(target, env.Ally.Entity, ReactionType.Burning, 3f, 6);
                var aura = target.GetAura("Hydro");
                aura.SkipNextOriginDecay = true;
                int phase = aura.OriginPhase;
                ReactionStateSystem.TryGetEntityState(target, ReactionType.Burning, out var burning);
                int rounds = burning.RemainingRounds;
                var subscriptions = new List<IDisposable>();
                int declarations = 0;
                foreach (BattleEventKind kind in Enum.GetValues(typeof(BattleEventKind)))
                    subscriptions.Add(BattleEventHub.Subscribe<BattleEventData>(kind, (h, d) => declarations++, 0));
                var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
                try
                {
                    for (int i = 0; i < 3; i++)
                    {
                        ElementQuery.ReadNormal(target, "Hydro");
                        ElementQuery.GetAmount(target, "Hydro");
                        ElementQuery.GetReactionAmount(target, "Pyro");
                        ElementQuery.Capture(target);
                    }
                    Assert.That(aura.AuraAmount, Is.EqualTo(2f));
                    Assert.That(aura.SourceEntity, Is.SameAs(env.Ally.Entity));
                    Assert.That(aura.OriginPhase, Is.EqualTo(phase));
                    Assert.That(aura.SkipNextOriginDecay, Is.True);
                    Assert.That(burning.ReactionElementAmount, Is.EqualTo(3f));
                    Assert.That(burning.RemainingRounds, Is.EqualTo(rounds));
                    Assert.That(declarations, Is.Zero);
                    Assert.That(random.FloatCallCount, Is.Zero);
                    Assert.That(random.IntCallCount, Is.Zero);
                }
                finally { foreach (var subscription in subscriptions) subscription.Dispose(); }
            }
        }

        [Test]
        public void PositionReading_UsesCurrentOccupant_NotFieldStatusesOrPreviousOccupant()
        {
            using (var env = CreateEnv())
            {
                var position = new FieldPosition(new BattleField(), BattleSide.Enemy, 2);
                position.StatusList.Add(new StatusInstance { StatusID2 = TeamPublicStatuses.MoonCage, IsActive = true });
                Assert.That(ElementQuery.ReadAtPosition(position, "Geo"), Is.Null);
                env.Enemy.Entity.ApplyAura("Hydro", 2f, 0);
                env.Ally.Entity.ApplyAura("Pyro", 1f, 0);
                position.Occupant = env.Enemy.Entity;
                Assert.That(ElementQuery.ReadAtPosition(position, "Hydro").Amount, Is.EqualTo(2f));
                position.Occupant = env.Ally.Entity;
                Assert.That(ElementQuery.ReadAtPosition(position, "Hydro"), Is.Null);
                Assert.That(ElementQuery.ReadAtPosition(position, "Pyro").Amount, Is.EqualTo(1f));
                position.Occupant = null;
                Assert.That(ElementQuery.ReadAtPosition(position, "Pyro"), Is.Null);
            }
        }

        [Test]
        public void OrdinaryConsumptionAndVirtualConsumption_StillWriteThroughOriginalOwners()
        {
            using (var env = CreateEnv())
            {
                var target = env.Enemy.Entity;
                target.ApplyAura("Cryo", 2f, 0);
                AddVirtual(target, env.Ally.Entity, ReactionType.Frozen, 3f, 2);
                var before = ElementQuery.ReadNormal(target, "Cryo");
                target.ConsumeAura("Cryo", .5f);
                FrozenReactionHandler.ConsumeFrozenAura(target, 1f);
                Assert.That(before.Amount, Is.EqualTo(2f));
                Assert.That(ElementQuery.GetAmount(target, "Cryo"), Is.EqualTo(1.5f));
                Assert.That(ElementQuery.GetAmount(target, "Cryo", ReactionPoolKind.Frozen), Is.EqualTo(2f));
                target.RemoveAura("Cryo");
                Assert.That(ElementQuery.ReadNormal(target, "Cryo"), Is.Null);
                Assert.That(ElementQuery.GetAmount(target, "Cryo", ReactionPoolKind.Frozen), Is.EqualTo(2f));
            }
        }

        [Test]
        public void RealSkills_StillConsumeNormalThenFrozenPools_AndWriteRemainingIncomingAura()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.ConfigureAllyAction(0, "SK_Normal_ElementQuery", "SE_Normal_ElementQuery", "Hydro", "1*TotalATK,2", 1);
                env.ConfigureAllyAction(1, "SK_Heavy_ElementQuery", "SE_Heavy_ElementQuery", "Cryo", "1*TotalATK,1", 1);
                env.ConfigureAllyAction(2, "SK_Skill_ElementQuery", "SE_Skill_ElementQuery", "Pyro", "1*TotalATK,3", 1);
                env.CreateMinimalBattle();
                env.Manager.StartBattle();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
                Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True);
                Assert.That(env.Manager.UseHeavyAttackBySlot(0), Is.True);
                var target = env.Enemy.Entity;
                Assert.That(ElementQuery.GetAmount(target, "Hydro"), Is.EqualTo(1f));
                Assert.That(ElementQuery.GetAmount(target, "Cryo", ReactionPoolKind.Frozen), Is.EqualTo(1f));
                Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
                Assert.That(ElementQuery.GetAmount(target, "Hydro"), Is.Zero);
                Assert.That(ElementQuery.GetAmount(target, "Cryo", ReactionPoolKind.Frozen), Is.Zero);
                Assert.That(ElementQuery.GetAmount(target, "Pyro"), Is.EqualTo(.5f).Within(.0001f));
                Assert.That(ElementQuery.ReadNormal(target, "Pyro").SourceEntity, Is.SameAs(env.Ally.Entity));
                Assert.That(random.FloatCallCount, Is.EqualTo(3));
                Assert.That(random.IntCallCount, Is.Zero);
            }
        }

        [Test]
        public void IntrinsicRepresentation_HasNoNumericSentinel_AndDoesNotRegisterOrInferAnAura()
        {
            using (var env = CreateEnv())
            {
                var target = env.Enemy.Entity;
                var main = new StatusMainData { StatusID = 990801, StatusID2 = "ST_Test_IntrinsicRepresentation",
                    MaxStack = 1, MaxCount = 1, WhenMax = "0" };
                var status = target.AddStatus(main.StatusID2, target, 0, 2, 0, main);
                var reading = ElementPoolReading.FromIntrinsicStatus(target, "Hydro", status);
                Assert.That(reading.IsIntrinsic, Is.True);
                Assert.That(reading.IsPresent, Is.True);
                Assert.That(reading.Kind, Is.EqualTo(ReactionPoolKind.Intrinsic));
                Assert.That(reading.SourceEntity, Is.SameAs(target));
                Assert.That(reading.SourceStatusID, Is.EqualTo(main.StatusID2));
                Assert.Throws<InvalidOperationException>(() => { float amount = reading.Amount; });
                Assert.That(ElementQuery.ReadNormal(target, "Hydro"), Is.Null);
                Assert.That(ElementQuery.ReadPool(target, "Hydro", ReactionPoolKind.Intrinsic), Is.Null,
                    "没有擅自把任意普通状态当成已定义的固有状态");
                Assert.Throws<ArgumentException>(() => ElementPoolReading.FromIntrinsicStatus(target, "Physical", status));
                target.RemoveStatusInstance(status);
                Assert.Throws<ArgumentException>(() => ElementPoolReading.FromIntrinsicStatus(target, "Hydro", status));
            }
        }

        [Test]
        public void NullAndMissingTargets_AreEmpty_WithoutFabricatingSources()
        {
            Assert.That(ElementQuery.ReadNormal(null, "Hydro"), Is.Null);
            Assert.That(ElementQuery.GetAmount(null, "Hydro"), Is.Zero);
            Assert.That(ElementQuery.Capture(null), Is.Empty);
            Assert.That(ElementQuery.ReadAtPosition(null, "Hydro"), Is.Null);
            Assert.Throws<ArgumentOutOfRangeException>(() => ElementQuery.ReadPool(null, "Hydro", (ReactionPoolKind)999));
        }

        [TestCase("Pyro")]
        [TestCase("Hydro")]
        [TestCase("Cryo")]
        [TestCase("Electro")]
        [TestCase("Anemo")]
        [TestCase("Geo")]
        [TestCase("Dendro")]
        public void IntrinsicPublicState_IsReadFromItsActualHost_WithoutFiniteGauge(string element)
        {
            using (var env = CreateEnv())
            {
                var target = env.Enemy.Entity;
                target.ApplyAura(element, 2f, 0);
                var status = AddIntrinsic(target, element);
                var reading = ElementQuery.ReadAura(target, element);
                Assert.That(reading.IsIntrinsic, Is.True);
                Assert.That(reading.SourceEntity, Is.SameAs(target));
                Assert.That(reading.SourceStatusID, Is.EqualTo(status.StatusID2));
                Assert.That(ElementQuery.ReadIntrinsic(env.Ally.Entity, element), Is.Null);
                Assert.That(ElementQuery.ReadNormal(target, element), Is.Null);
                Assert.That(reading.AvailableFor(.375f), Is.EqualTo(.375f));
                Assert.Throws<InvalidOperationException>(() => { var unused = reading.Amount; });
                target.ApplyAura(element, 7f, env.Ally.Entity.EntityID);
                target.ConsumeAura(element, 10f);
                target.TickAuras();
                Assert.That(ElementQuery.ReadIntrinsic(target, element).PoolApplyOrder, Is.EqualTo(status.ApplyOrder));
                Assert.That(ElementQuery.ReadNormal(target, element), Is.Null);
                target.RemoveStatusInstance(status);
                Assert.That(ElementQuery.HasAura(target, element), Is.False);
                Assert.That(reading.IsPresent, Is.True, "已取得的历史快照不随移除改变");
            }
        }

        [TestCase("Hydro", "Pyro", ReactionType.Vaporize)]
        [TestCase("Pyro", "Hydro", ReactionType.Vaporize)]
        [TestCase("Cryo", "Pyro", ReactionType.Melt)]
        [TestCase("Pyro", "Cryo", ReactionType.Melt)]
        public void IntrinsicAmplifying_ConsumesTheFiniteAttack_NotThePublicState(string aura, string attack, ReactionType type)
        {
            using (var env = CreateEnv())
            {
                var status = AddIntrinsic(env.Enemy.Entity, aura);
                var result = ReactionResolver.Resolve(new ReactionContext { SourceEntity = env.Ally.Entity,
                    Target = env.Enemy.Entity, AttackElement = attack, AttackAmount = 2.75f,
                    ApplicationPhase = 2, PreReactionDamage = 100f });
                Assert.That(result.PrimaryDamageReactionType, Is.EqualTo(type));
                Assert.That(result.RemainingAttackAmount, Is.Zero);
                Assert.That(env.Enemy.Entity.HasStatusInstance(status), Is.True);
                Assert.That(ElementQuery.ReadNormal(env.Enemy.Entity, attack), Is.Null);
            }
        }

        [Test]
        public void IntrinsicAuraChanges_ExposeTypedSnapshots_AndQueriesDoNotPublish()
        {
            using (var env = CreateEnv())
            {
                var events = new List<AuraChangedEventData>();
                using (BattleEventHub.Subscribe<AuraChangedEventData>(BattleEventKind.AuraChanged, (h, d) => events.Add(d), 0))
                {
                    var status = AddIntrinsic(env.Enemy.Entity, "Hydro");
                    Assert.That(events, Has.Count.EqualTo(1));
                    Assert.That(events[0].After, Has.Count.EqualTo(1));
                    var pool = events[0].After[0];
                    Assert.That(pool.IsIntrinsic, Is.True);
                    Assert.That(pool.SourceStatusId, Is.EqualTo(status.StatusID2));
                    Assert.Throws<InvalidOperationException>(() => { var unused = pool.Amount; });
                    ElementQuery.Capture(env.Enemy.Entity);
                    ElementQuery.ReadAura(env.Enemy.Entity, "Hydro");
                    env.Enemy.Entity.ConsumeAura("Hydro", 5f);
                    Assert.That(events, Has.Count.EqualTo(1));
                    env.Enemy.Entity.RemoveStatusInstance(status);
                    Assert.That(events, Has.Count.EqualTo(2));
                    Assert.That(events[1].After, Is.Empty);
                }
            }
        }

        [Test]
        public void RealPyroSkillAgainstIntrinsicAnemo_UsesTheSharedPostHitSwirlPath()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.ConfigureAllyAction(0, "SK_IntrinsicSwirl", "SE_IntrinsicSwirl", "Pyro", "1*TotalATK,1", 10);
                env.CreateMinimalBattle();
                env.Manager.StartBattle();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                var status = AddIntrinsic(env.Enemy.Entity, "Anemo");
                var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
                var kinds = new List<ReactionType>();
                using (BattleEventHub.Subscribe<ReactionEventData>(BattleEventKind.ReactionOccurred,
                    (h, d) => kinds.Add(d.Reaction), 0))
                {
                    Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True);
                    Assert.That(kinds.FindAll(k => k == ReactionType.Swirl), Has.Count.EqualTo(1));
                    Assert.That(env.Enemy.Entity.HasStatusInstance(status), Is.True);
                    Assert.That(ElementQuery.ReadNormal(env.Enemy.Entity, "Pyro"), Is.Null);
                    Assert.That(random.FloatCallCount, Is.EqualTo(1), "只有原命中判定暴击，扩散不抽额外暴击");
                }
            }
        }

        [Test]
        public void IntrinsicFreezeAndBurning_CreateFiniteVirtualPoolsFromTheIncomingQuota()
        {
            using (var env = CreateEnv())
            {
                var target = env.Enemy.Entity;
                var cryo = AddIntrinsic(target, "Cryo");
                var freeze = ReactionResolver.Resolve(new ReactionContext { SourceEntity = env.Ally.Entity,
                    Target = target, AttackElement = "Hydro", AttackAmount = 1.25f, ApplicationPhase = 2 });
                Assert.That(freeze.TriggeredReactions.Exists(r => r.Type == ReactionType.Frozen), Is.True);
                Assert.That(ElementQuery.GetAmount(target, "Cryo", ReactionPoolKind.Frozen), Is.EqualTo(1.25f));
                ReactionStateSystem.TryGetEntityState(target, ReactionType.Frozen, out var state);
                Assert.That(state.RemainingRounds, Is.EqualTo(2));
                target.RemoveStatusInstance(cryo);
                ReactionStateSystem.RemoveEntityState(target, ReactionType.Frozen);
                AddIntrinsic(target, "Dendro");
                var burning = ReactionResolver.Resolve(new ReactionContext { SourceEntity = env.Ally.Entity,
                    Target = target, AttackElement = "Pyro", AttackAmount = .75f, ApplicationPhase = 2 });
                Assert.That(burning.TriggeredReactions.Exists(r => r.Type == ReactionType.Burning), Is.True);
                Assert.That(ElementQuery.GetAmount(target, "Pyro", ReactionPoolKind.Burning), Is.EqualTo(.75f));
                Assert.That(ElementQuery.ReadIntrinsic(target, "Dendro"), Is.Not.Null);
            }
        }

        [Test]
        public void IntrinsicHydro_ElectroChargedKeepsItsSingleTickCapAndFiniteElectroRemainder()
        {
            using (var env = CreateEnv())
            {
                var target = env.Enemy.Entity;
                var hydro = AddIntrinsic(target, "Hydro");
                var result = ReactionResolver.Resolve(new ReactionContext { SourceEntity = env.Ally.Entity,
                    Target = target, AttackElement = "Electro", AttackAmount = 2.5f, ApplicationPhase = 2 });
                Assert.That(result.TriggeredReactions.FindAll(r => r.Type == ReactionType.ElectroCharged), Has.Count.EqualTo(1));
                Assert.That(result.RemainingAttackAmount, Is.EqualTo(1.5f));
                Assert.That(ElementQuery.GetAmount(target, "Electro"), Is.EqualTo(1.5f));
                Assert.That(ElectroChargedReactionHandler.IsElectroCharged(target), Is.True);
                target.RemoveStatusInstance(hydro);
                ElectroChargedReactionHandler.CleanupInvalidStates();
                Assert.That(ElectroChargedReactionHandler.IsElectroCharged(target), Is.False);
            }
        }

        private static StatusInstance AddIntrinsic(BattleEntity target, string element)
        {
            string id = ElementQuery.IntrinsicStatusID(element);
            return target.AddStatus(id, target, 0, 2, 0,
                new StatusMainData { StatusID2 = id, MaxStack = 1, MaxCount = 1, WhenMax = "1" });
        }

        private static BattleFlowTestEnv CreateEnv()
        { var env = new BattleFlowTestEnv(); env.CreateMinimalBattle(); return env; }

        private static void AddVirtual(BattleEntity target, BattleEntity source, ReactionType reaction, float amount, int phase)
            => ReactionStateSystem.SetEntityState(target, reaction, (int)Math.Ceiling(amount),
                new ReactionSourceSnapshot { SourceEntity = source, SourceEntityID = source.EntityID,
                    SourceEffectID = "SE_Virtual", SourceSkillID = "SK_Virtual" }, phase, true, amount);
    }
}
