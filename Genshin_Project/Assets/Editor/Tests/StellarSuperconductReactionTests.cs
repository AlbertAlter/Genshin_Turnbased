using System.Collections.Generic;
using NUnit.Framework;
using OfficeOpenXml;

namespace GenshinTurnBased.Tests.EditMode
{
    /// <summary>只编写，不自动运行Unity。流程用公开技能/回合入口；来源筛选和延迟声明另做定向回归。</summary>
    public sealed class StellarSuperconductReactionTests
    {
        [Test]
        public void RealMultiTargetHit_CreatesOnlyDomain_NoOrdinarySuperconductOrSameHitBonus_RefreshCountsOnce()
        {
            using (var env = CreateEnv())
            {
                var neighbour = env.CreateEnemy(2, 100000f).Entity;
                env.DataManager.SkillEffectDict["SE_Normal_Stellar"].TargetNumber = 2;
                env.DataManager.SkillEffectDict["SE_Normal_Stellar"].TargetConsecutive = 0;
                Start(env);
                Apply(env.Enemy.Entity, "Electro", 2f, env.Ally.Entity);
                Apply(neighbour, "Electro", 2f, env.Ally.Entity);
                env.Ally.ForcedTargetPositions.AddRange(new[] { 1, 2 });
                var damage = new List<DamageEventData>();
                var reactions = new List<long>();
                BattleEventHeader added = null;
                using (BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (h, d) => damage.Add(d), 0))
                using (BattleEventHub.Subscribe<ReactionEventData>(BattleEventKind.ReactionOccurred,
                    (h, r) => { Assert.That(r.Reaction, Is.EqualTo(ReactionType.StellarSuperconduct)); reactions.Add(h.EventId); }, 0))
                using (BattleEventHub.Subscribe<StatusEventData>(BattleEventKind.StatusAdded,
                    (h, s) => { if (s.StatusId == TeamPublicStatuses.PolarDomain) added = h; }, 0))
                {
                    var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
                    Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True);
                    var handler = env.Manager.TeamBonuses.Stellar;
                    var domain = handler.Domain.Status;
                    Assert.That(damage, Has.Count.EqualTo(2), "没有普通超导的主目标/邻位冰伤");
                    foreach (var d in damage) Assert.That(d.DamageBeforeShield, Is.EqualTo(50f).Within(.001f));
                    Assert.That(handler.PendingCount, Is.Zero, "创建领域的整个hit排除，包括后续目标刷新");
                    Assert.That(added, Is.Not.Null);
                    Assert.That(added.CauseEventIds, Has.Count.EqualTo(1));
                    Assert.That(reactions, Does.Contain(added.CauseEventIds[0]));
                    Assert.That(domain.Host, Is.SameAs(env.Manager.EnemyTeam));
                    Assert.That(env.Enemy.Entity.GetAura("Electro").AuraAmount, Is.EqualTo(1f));
                    Assert.That(ReactionStateSystem.TryGetEntityState(neighbour, ReactionType.Superconduct, out _), Is.False);

                    damage.Clear();
                    Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True);
                    Assert.That(handler.Domain.Status, Is.SameAs(domain));
                    Assert.That(handler.PendingCount, Is.EqualTo(1), "已有领域的刷新hit计入，但同hit多目标只计1");
                    foreach (var d in damage) Assert.That(d.DamageBeforeShield, Is.EqualTo(60f).Within(.001f));
                    Assert.That(random.FloatCallCount, Is.EqualTo(4));
                    Assert.That(random.IntCallCount, Is.Zero);
                }
            }
        }

        [Test]
        public void ThreeHitsInOneSkill_OnlyCreationHitExcluded_AndEachLaterHitGetsExistingDomain()
        {
            using (var env = CreateEnv())
            {
                var level = env.DataManager.SkillLevelDict[$"{BattleFlowTestEnv.AllyID}_0"][1];
                level.Hits2 = level.Hits3 = "1*TotalATK,1";
                Start(env);
                Apply(env.Enemy.Entity, "Electro", 10f, env.Ally.Entity);
                var damage = new List<float>();
                using (BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved,
                    (h, d) => damage.Add(d.DamageBeforeShield), 0))
                {
                    var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
                    Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True);
                    Assert.That(damage, Is.EqualTo(new[] { 50f, 60f, 60f }).Within(.001f));
                    Assert.That(env.Manager.TeamBonuses.Stellar.PendingCount, Is.EqualTo(2));
                    Assert.That(random.FloatCallCount, Is.EqualTo(3));
                    Assert.That(random.IntCallCount, Is.Zero);
                }
            }
        }

        [Test]
        public void ShieldHitsCountToTwelve_ZeroAuraAndStellarDamageDoNotCount_AndExpiryRemovesBothBonuses()
        {
            using (var env = CreateEnv())
            {
                Start(env);
                env.Enemy.Entity.AddShield(100000f, "None", 10, originPhase: 2);
                Apply(env.Enemy.Entity, "Electro", 30f, env.Ally.Entity);
                Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True);
                var handler = env.Manager.TeamBonuses.Stellar;
                for (int i = 0; i < 14; i++) Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True);
                Assert.That(handler.PendingCount, Is.EqualTo(12));
                Assert.That(env.Enemy.Entity.CurrentHP, Is.EqualTo(100000f), "完全命中护盾也算攻击次数");
                NextRound(env);
                Assert.That(handler.EffectiveCount, Is.EqualTo(12));
                Assert.That(handler.PendingCount, Is.Zero);
                Assert.That(handler.ElementDamageBonus, Is.EqualTo(.4f).Within(.0001f));
                Assert.That(handler.ReactionMultiplier, Is.EqualTo(2f).Within(.0001f));
                Assert.That(env.Ally.Entity.GetTeamDamageBonus("Cryo"), Is.EqualTo(.4f).Within(.0001f));
                Assert.That(env.Ally.Entity.GetTeamDamageBonus("Pyro"), Is.Zero);
                Assert.That(env.Enemy.Entity.GetTeamResistanceBonus("Physical"), Is.EqualTo(-.4f));
                env.Enemy.Entity.RemoveAura("Electro"); // 下两次仅测伤害性质筛选，不以新反应刷新领域。
                Assert.That(env.Manager.UseHeavyAttackBySlot(0), Is.True, "无附着的雷伤");
                Assert.That(env.Manager.UseBurstBySlot(0), Is.True, "Stellar冰伤不计数，即使自身带附着");
                Assert.That(handler.PendingCount, Is.Zero);
                // 最后一击也刷新过领域；首个同原点已扣至1，再过一轮到期。
                NextRound(env);
                Assert.That(handler.IsActive, Is.False);
                Assert.That(env.Manager.EnemyTeam.GetStatus(TeamPublicStatuses.PolarDomain), Is.Null);
                Assert.That(env.Ally.Entity.GetTeamDamageBonus("Cryo"), Is.Zero);
                Assert.That(env.Enemy.Entity.GetTeamResistanceBonus("Physical"), Is.Zero);
            }
        }

        [Test]
        public void RealSwirlStageTwo_ConvertsIceElectroWithoutOrdinarySuperconductDamageOrState()
        {
            using (var env = CreateEnv())
            {
                var neighbour = env.CreateEnemy(2, 100000f).Entity;
                Start(env);
                Apply(env.Enemy.Entity, "Electro", 1f, env.Ally.Entity);
                Apply(neighbour, "Cryo", 1f, env.Enemy.Entity);
                var types = new List<ReactionType>();
                using (BattleEventHub.Subscribe<ReactionEventData>(BattleEventKind.ReactionOccurred,
                    (h, r) => types.Add(r.Reaction), 0))
                {
                    var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
                    Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
                    Assert.That(types, Has.Member(ReactionType.StellarSuperconduct));
                    Assert.That(types, Has.No.Member(ReactionType.Superconduct));
                    Assert.That(neighbour.CurrentHP, Is.EqualTo(100000f));
                    Assert.That(neighbour.GetAura("Cryo"), Is.Null);
                    Assert.That(ReactionStateSystem.TryGetEntityState(neighbour, ReactionType.Superconduct, out _), Is.False);
                    Assert.That(env.Manager.TeamBonuses.Stellar.PendingCount, Is.Zero);
                    Assert.That(random.FloatCallCount, Is.EqualTo(1), "只有主风伤暴击，不为星超导生成反应伤害");
                    Assert.That(random.IntCallCount, Is.Zero);
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RealSwirlResidualIceDendroElectro_UsesActualPoolsAndDeclaresActualSuperconductType(bool friendlyCryo)
        {
            using (var env = CreateEnv())
            {
                var neighbour = env.CreateEnemy(2, 100000f).Entity;
                Start(env);
                Apply(env.Enemy.Entity, "Pyro", 1f, env.Ally.Entity);
                Apply(neighbour, "Cryo", 10f, friendlyCryo ? env.Ally.Entity : neighbour);
                Apply(neighbour, "Electro", 10f, neighbour);
                Apply(neighbour, "Dendro", 10f, neighbour);
                var types = new List<ReactionType>();
                var damageTypes = new List<ReactionType>();
                using (BattleEventHub.Subscribe<ReactionEventData>(BattleEventKind.ReactionOccurred,
                    (h, r) => types.Add(r.Reaction), 0))
                using (BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved,
                    (h, d) => damageTypes.Add(d.Source.ReactionType), 0))
                {
                    Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
                    var expected = friendlyCryo ? ReactionType.StellarSuperconduct : ReactionType.Superconduct;
                    var excluded = friendlyCryo ? ReactionType.Superconduct : ReactionType.StellarSuperconduct;
                    Assert.That(types, Has.Member(expected));
                    Assert.That(types, Has.No.Member(excluded));
                    Assert.That(types, Has.Member(ReactionType.Quicken));
                    Assert.That(env.Manager.TeamBonuses.Stellar.IsActive, Is.EqualTo(friendlyCryo));
                    Assert.That(damageTypes, Has.No.Member(expected), "第三阶段没有即时超导/星超导伤害");
                }
            }
        }

        [Test]
        public void ActualSourceReferenceBeatsSameNumericID_AndLegacyDuplicateIDCannotQualify()
        {
            using (var env = CreateEnv())
            {
                var other = env.CreateAlly(2).Entity;
                var target = env.CreateEnemy(2).Entity;
                env.Enemy.Entity.EntityID = env.Ally.Entity.EntityID; // 敌人与我方同配置ID，仍是不同实例。
                Start(env);
                var context = new ReactionContext { SourceEntity = env.Enemy.Entity, Target = target,
                    AttackElement = "Electro", AttackAmount = 1f, SourceKind = ReactionSourceKind.EnemySkill };
                Apply(target, "Cryo", 1f, env.Enemy.Entity);
                Assert.That(env.Manager.TeamBonuses.Stellar.TryPrepareSuperconduct(context, out _), Is.False);
                target.ApplyAura("Cryo", 1f, other.EntityID);
                Assert.That(env.Manager.TeamBonuses.Stellar.TryPrepareSuperconduct(context, out _), Is.False);
                Apply(target, "Cryo", 1f, other);
                Assert.That(env.Manager.TeamBonuses.Stellar.TryPrepareSuperconduct(context, out _), Is.True);
                Assert.That(env.Manager.TeamBonuses.Stellar.IsActive, Is.False, "纯规划不创建领域");
            }
        }

        [Test]
        public void FrozenSourceOnlyQualifiesWhenActuallyConsumed_AndRestrictedNormalPoolCannotBorrowIt()
        {
            using (var env = CreateEnv())
            {
                Start(env);
                var target = env.Enemy.Entity;
                Apply(target, "Cryo", 2f, target);
                ReactionStateSystem.SetEntityState(target, ReactionType.Frozen, 2,
                    new ReactionSourceSnapshot { SourceEntity = env.Ally.Entity, SourceEntityID = env.Ally.Entity.EntityID },
                    2, true, 2f);
                var context = new ReactionContext { SourceEntity = target, Target = target,
                    AttackElement = "Electro", AttackAmount = 1f, SourceKind = ReactionSourceKind.EnemySkill };
                var handler = env.Manager.TeamBonuses.Stellar;
                Assert.That(handler.TryPrepareSuperconduct(context, out _), Is.False, "1雷只反应敌方普通冰，未用友方冻元素");
                context.AttackAmount = 3f;
                Assert.That(handler.TryPrepareSuperconduct(context, out _), Is.True);
                context.RestrictedAuraElement = "Cryo"; context.RestrictedAuraKind = ReactionPoolKind.NormalAura;
                Assert.That(handler.TryPrepareSuperconduct(context, out _), Is.False);
                context.RestrictedAuraKind = ReactionPoolKind.Frozen;
                Assert.That(handler.TryPrepareSuperconduct(context, out _), Is.True);
            }
        }

        [Test]
        public void LivingReserveProviderQualifies_DeathStopsNewConversionsButDoesNotEraseExistingDomain()
        {
            using (var env = CreateEnv())
            {
                var provider = env.CreateAlly(2).Entity;
                provider.EntityID = 777001;
                env.DataManager.TeamBonusRules.Stellar = Parameters(provider.EntityID);
                Start(env);
                Apply(env.Enemy.Entity, "Electro", 2f, env.Ally.Entity);
                Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True);
                var handler = env.Manager.TeamBonuses.Stellar;
                Assert.That(handler.IsActive, Is.True);
                provider.CurrentHP = 0f;
                var context = new ReactionContext { SourceEntity = env.Ally.Entity, Target = env.Enemy.Entity,
                    AttackElement = "Cryo", AttackAmount = 1f };
                Assert.That(handler.TryPrepareSuperconduct(context, out _), Is.False);
                Assert.That(handler.IsActive, Is.True, "公共领域仍按自己的持续时间，不绑定提供者死亡清理");
            }
        }

        [Test]
        public void DamageRecordedBeforeDomainOrBeforeReplacement_CannotBeClaimedByNewInstance()
        {
            using (var env = CreateEnv())
            {
                Start(env);
                var old = RecordDamage(env, 70001);
                Apply(env.Enemy.Entity, "Electro", 3f, env.Ally.Entity);
                Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True);
                BattleEventHub.Dispatch(old);
                var handler = env.Manager.TeamBonuses.Stellar;
                Assert.That(handler.PendingCount, Is.Zero);
                var recordedForFirst = RecordDamage(env, 70002);
                env.Manager.EnemyTeam.RemoveStatusInstance(handler.Domain.Status);
                Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True);
                BattleEventHub.Dispatch(recordedForFirst);
                Assert.That(handler.PendingCount, Is.Zero);
            }
        }

        [Test]
        public void MissingPublicDefinitionFailsBeforeConsumption_AndResetDoesNotKeepOldDomain()
        {
            using (var env = CreateEnv())
            {
                Start(env);
                Apply(env.Enemy.Entity, "Electro", 2f, env.Ally.Entity);
                var main = env.DataManager.StatusMainDict[TeamPublicStatuses.PolarDomain];
                env.DataManager.StatusMainDict.Remove(main.StatusID2);
                Assert.Throws<System.InvalidOperationException>(() => ReactionResolver.Resolve(new ReactionContext
                { SourceEntity = env.Ally.Entity, Target = env.Enemy.Entity, AttackElement = "Cryo", AttackAmount = 1f }));
                Assert.That(env.Enemy.Entity.GetAura("Electro").AuraAmount, Is.EqualTo(2f));
                env.DataManager.StatusMainDict[main.StatusID2] = main;
                Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True);
                var handler = env.Manager.TeamBonuses.Stellar;
                var domain = handler.Domain.Status;
                env.Manager.ResetBattle();
                Assert.That(handler.IsActive, Is.False);
                Assert.That(domain.IsActive, Is.False);
                Assert.That(env.Manager.EnemyTeam.GetStatus(main.StatusID2), Is.Null);
            }
        }

        private static BattleFlowTestEnv CreateEnv()
        {
            var env = new BattleFlowTestEnv();
            env.DataManager.TeamBonusRules.Stellar = Parameters(BattleFlowTestEnv.AllyID);
            env.DataManager.CharacterAttributesDict[BattleFlowTestEnv.AllyID].Element = "Cryo";
            env.ConfigureAllyAction(0, "SK_Normal_Stellar", "SE_Normal_Stellar", "Cryo", "1*TotalATK,1", 1);
            env.ConfigureAllyAction(1, "SK_Heavy_Stellar", "SE_Heavy_Stellar", "Electro", "1*TotalATK,0", 1);
            env.ConfigureAllyAction(2, "SK_Skill_Stellar", "SE_Skill_Stellar", "Anemo", "1*TotalATK,1", 1);
            env.ConfigureAllyAction(3, "SK_Burst_Stellar", "SE_Burst_Stellar", "Cryo", "1*TotalATK,1", 1);
            env.DataManager.SkillEffectDict["SE_Burst_Stellar"].Param3 = "Stellar";
            env.CreateMinimalBattle();
            env.Enemy.Entity.TotalHP = env.Enemy.Entity.CurrentHP = 100000f;
            return env;
        }
        private static TeamBonusParameters Parameters(int providerID)
        {
            using (var book = new ExcelPackage())
            {
                var sheet = book.Workbook.Worksheets.Add("Stellar");
                sheet.Cells[1, 1].Value = "Key"; sheet.Cells[1, 2].Value = "Value";
                var entries = new List<(string key, object value)> { ("Enabled", 1), ("CharacterIDs", providerID),
                    ("Duration", 2), ("CountCap", 12), ("PhysicalResDown", .4f) };
                for (int n = 0; n <= 12; n++)
                {
                    entries.Add(("ElementDMG" + n, n == 0 ? .2f : n == 1 ? .29f : .28f + .01f * n));
                    entries.Add(("ReactionRate" + n, n == 0 ? 1f : 1.4f + .05f * n));
                }
                for (int row = 0; row < entries.Count; row++)
                { sheet.Cells[row + 2, 1].Value = entries[row].key; sheet.Cells[row + 2, 2].Value = entries[row].value; }
                return TeamBonusTableLoader.Read(sheet);
            }
        }
        private static BattleDeclaration RecordDamage(BattleFlowTestEnv env, long hitID)
            => BattleEventHub.Record(new object(), new DamageEventData(new DamageResolvedEvent
            { Source = DamageSourceInfo.Create(env.Ally.Entity, ReactionSourceKind.CharacterSkill, "SK", "SE"),
                Target = env.Enemy.Entity, HitLanded = true, Element = "Cryo", ElementAmount = 1f }), hitID);
        private static void Start(BattleFlowTestEnv env)
        { env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction); BattleRandom.SetSource(new SequenceBattleRandomSource()); }
        private static void Apply(BattleEntity target, string element, float amount, BattleEntity source)
            => target.ApplyAura(element, amount, source.EntityID, sourceEntity: source);
        private static void NextRound(BattleFlowTestEnv env)
        { env.Manager.EndAllyTurn(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction); }
    }
}
