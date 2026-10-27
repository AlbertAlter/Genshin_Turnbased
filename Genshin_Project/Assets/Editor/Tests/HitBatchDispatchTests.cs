using NUnit.Framework;
using System.Collections.Generic;

namespace GenshinTurnBased.Tests.EditMode
{
    /// <summary>Hit/OnDamage 语义、批次元数据与状态来源的回归测试。</summary>
    [Category("Unit")]
    public sealed class HitBatchDispatchTests
    {
        private BattleFlowTestEnv _env;
        private DataManager _dm;
        private BattleEntity _ally;
        private BattleEntity _enemy;
        private readonly List<BattleEntity> _otherTargets = new List<BattleEntity>();

        [SetUp]
        public void SetUp()
        {
            _env = new BattleFlowTestEnv();
            _env.CreateMinimalBattle();
            _dm = _env.DataManager;
            _ally = _env.Ally.Entity;
            _ally.TotalHP = 100f;
            _ally.CurrentHP = 100f;
            _enemy = _env.Enemy.Entity;
            _enemy.TotalHP = _enemy.CurrentHP = 100f;
        }

        [TearDown]
        public void TearDown()
        {
            try { _env?.Dispose(); }
            finally { _otherTargets.Clear(); _env = null; }
        }

        [Test]
        public void ShieldFullyAbsorbs_AttackStillOnHit_ButNotOnDamage()
        {
            StatusInstance onHit = AttachHook("ST_TEST_ON_HIT", "OnHit", "All");
            StatusInstance onDamage = AttachHook("ST_TEST_ON_DAMAGE", "OnDamage", "All");
            _ally.AddShield(100f, "None", 3f);

            HitResolution resolution;
            using (var batch = new HitBatch())
            {
                resolution = batch.Resolve(new HitRequest
                {
                    Target = _ally,
                    DamageBeforeShield = 25f,
                    Element = "Physical",
                    Source = DamageSourceInfo.Create(
                        _enemy, ReactionSourceKind.EnemySkill, "SK_TEST", "SE_TEST"),
                    HitIndex = 2,
                    TargetIndex = 3
                });
                batch.Flush();
            }

            Assert.That(_ally.CurrentHP, Is.EqualTo(100f));
            Assert.That(resolution.DamageEvent.HitLanded, Is.True);
            Assert.That(resolution.DamageEvent.ActualHPDamage, Is.Zero);
            Assert.That(resolution.DamageEvent.HitIndex, Is.EqualTo(3));
            Assert.That(resolution.DamageEvent.TargetIndex, Is.EqualTo(4));
            Assert.That(onHit.LifeTriggerCount, Is.EqualTo(1));
            Assert.That(onDamage.LifeTriggerCount, Is.Zero);
        }

        [Test]
        public void ReactionDamage_TriggersOnDamage_ButNotOnHit()
        {
            StatusInstance onHit = AttachHook("ST_TEST_REACTION_HIT", "OnHit", "感电");
            StatusInstance onDamage = AttachHook("ST_TEST_REACTION_DAMAGE", "OnDamage", "感电");
            var snapshot = new ReactionSourceSnapshot
            {
                SourceEntity = _enemy,
                SourceEntityID = _enemy.EntityID,
                SourceSide = _enemy.Side,
                SourceKind = ReactionSourceKind.EnemySkill,
                SourceSkillID = "SK_REACTION_OWNER",
                SourceEffectID = "SE_REACTION_OWNER"
            };
            DamageSourceInfo source = DamageSourceInfo.FromReactionSnapshot(
                snapshot, ReactionType.ElectroCharged);

            using (var batch = new HitBatch())
            {
                batch.Resolve(new HitRequest
                {
                    Target = _ally,
                    DamageBeforeShield = 10f,
                    Element = "Electro",
                    Source = source,
                    HitIndex = 0,
                    TargetIndex = 0
                });
                batch.Flush();
            }

            Assert.That(_ally.CurrentHP, Is.EqualTo(90f));
            Assert.That(source.CountsAsHit, Is.False);
            Assert.That(source.HasSourceEntity, Is.True);
            Assert.That(source.SourceEntityID, Is.EqualTo(_enemy.EntityID));
            Assert.That(source.SourceSide, Is.EqualTo(BattleSide.Enemy));
            Assert.That(onHit.LifeTriggerCount, Is.Zero);
            Assert.That(onDamage.LifeTriggerCount, Is.EqualTo(1));
        }

        [Test]
        public void ReapplyingCharacterStatus_PreservesEachInstanceOwnerAndDamageSourceSnapshot()
        {
            const string statusID = "ST_TEST_OWNER";
            var main = new StatusMainData { StatusID = 9909001, StatusID2 = statusID, MaxStack = 2 };
            _dm.StatusMainDict[statusID] = main;
            StatusInstance status = _ally.AddStatus(statusID, _enemy, 2, 2, 0, main);
            DamageSourceInfo originalSource = DamageSourceInfo.FromStatus(status, "STE_TEST_OWNER", ReactionType.None);
            Assert.That(status.HasSourceEntity, Is.True);
            Assert.That(status.SourceEntityID, Is.EqualTo(_enemy.EntityID));
            Assert.That(status.SourceSide, Is.EqualTo(BattleSide.Enemy));

            StatusInstance reapplied = _ally.AddStatus(statusID, _ally, 2, 2, 0, main);
            DamageSourceInfo source = DamageSourceInfo.FromStatus(
                reapplied, "STE_TEST_OWNER", ReactionType.None);

            Assert.That(reapplied, Is.Not.SameAs(status));
            CollectionAssert.AreEqual(new[] { status, reapplied }, _ally.GetStatusInstances(statusID));
            Assert.That(status.Caster, Is.SameAs(_enemy));
            Assert.That(status.SourceEntityID, Is.EqualTo(_enemy.EntityID));
            Assert.That(status.SourceSide, Is.EqualTo(BattleSide.Enemy));
            Assert.That(originalSource.SourceStatus, Is.SameAs(status));
            Assert.That(originalSource.SourceEntityID, Is.EqualTo(_enemy.EntityID));
            Assert.That(originalSource.SourceSide, Is.EqualTo(BattleSide.Enemy));
            Assert.That(originalSource.SourceStatusApplyOrder, Is.EqualTo(status.ApplyOrder));
            Assert.That(reapplied.Caster, Is.SameAs(_ally));
            Assert.That(reapplied.SourceEntityID, Is.EqualTo(_ally.EntityID));
            Assert.That(reapplied.SourceSide, Is.EqualTo(BattleSide.Ally));
            Assert.That(source.SourceStatus, Is.SameAs(reapplied));
            Assert.That(source.SourceStatusID, Is.EqualTo(statusID));
            Assert.That(source.SourceStatusApplyOrder, Is.EqualTo(reapplied.ApplyOrder));
            Assert.That(source.SourceStatusApplyOrder, Is.GreaterThan(originalSource.SourceStatusApplyOrder));
            Assert.That(source.HasSourceEntity, Is.True);
            Assert.That(source.CountsAsHit, Is.True);
        }

        [Test]
        public void SameHitLayer_UsesGlobalStatusApplyOrder()
        {
            // 故意先插入 high，再用 ApplyOrder 把 low 调到它之前，确认不依赖字典遍历顺序。
            StatusInstance high = HookTestEnv.AttachStatus(
                _ally, "ST_TEST_ORDER_HIGH", _ally, 3);
            StatusInstance low = HookTestEnv.AttachStatus(
                _ally, "ST_TEST_ORDER_LOW", _ally, 3);
            high.ApplyOrder = 200;
            low.ApplyOrder = 100;

            HookTestEnv.InjectStatusAction(
                _dm, high.StatusID2, "OnHit", string.Empty, string.Empty, hitSource: "All");
            HookTestEnv.InjectStatusEffect(
                _dm, "STE_TEST_REMOVE_HIGH", "RemoveStatus", high.StatusID2,
                targetType: "Self");
            HookTestEnv.InjectStatusAction(
                _dm, low.StatusID2, "OnHit", "STE_TEST_REMOVE_HIGH", string.Empty,
                hitSource: "All");

            using (var batch = new HitBatch())
            {
                batch.Resolve(new HitRequest
                {
                    Target = _ally,
                    DamageBeforeShield = 1f,
                    Element = "Physical",
                    Source = DamageSourceInfo.Create(
                        _enemy, ReactionSourceKind.EnemySkill, "SK_TEST", "SE_TEST"),
                    HitIndex = 0,
                    TargetIndex = 0
                });
                batch.Flush();
            }

            Assert.That(low.LifeTriggerCount, Is.EqualTo(1));
            Assert.That(_ally.GetStatus(high.StatusID2), Is.Null);
            Assert.That(high.LifeTriggerCount, Is.Zero,
                "高 ApplyOrder 候选应在低 ApplyOrder 移除它后失效");
        }

        private StatusInstance AttachHook(string statusID, string actionType, string filter)
        {
            StatusInstance status = HookTestEnv.AttachStatus(_ally, statusID, _ally, 3);
            if (actionType == "OnHit")
                HookTestEnv.InjectStatusAction(
                    _dm, statusID, actionType, string.Empty, string.Empty, hitSource: filter);
            else
                HookTestEnv.InjectStatusAction(
                    _dm, statusID, actionType, string.Empty, string.Empty, damageSource: filter);
            return status;
        }

        [TestCase(1, 1)]
        [TestCase(2, 2)]
        [TestCase(0, 3)]
        public void OutgoingHit_FilterAndTargetDispatchStayUnchanged_OnlyConfiguredLimitCapsResponses(int limit, int expected)
        {
            StatusInstance capped = HookTestEnv.AttachStatus(_ally, "ST_TEST_HIT_CAP", _ally, 3);
            StatusActionData action = HookTestEnv.InjectStatusAction(_dm, capped.StatusID2,
                "OnTrigger", "", "", maxTimePerLife: 0, outgoingHitSource: "All");
            action.SetTriggerLimits("Hit", limit.ToString());
            StatusInstance unchanged = HookTestEnv.AttachStatus(_ally, "ST_TEST_TARGET_DISPATCH", _ally, 3);
            HookTestEnv.InjectStatusAction(_dm, unchanged.StatusID2, "OnTrigger", "", "",
                maxTimePerLife: 0, outgoingHitSource: "All");

            using (BattleResolutionContext.BeginEffect())
            {
                SubmitOutgoingTargets(3);
                Assert.That(capped.LifeTriggerCount, Is.EqualTo(expected));
                Assert.That(unchanged.LifeTriggerCount, Is.EqualTo(3), "未配置行仍收到每个目标的命中");
                SubmitOutgoingTargets(3);
                Assert.That(capped.LifeTriggerCount, Is.EqualTo(expected * 2), "下一hit拥有自己的额度");
            }
        }

        [Test]
        public void HitLimit_FailedConditionDoesNotConsumeQuota_AndSuccessfulReservationPreventsExtraRolls()
        {
            StatusInstance status = HookTestEnv.AttachStatus(_ally, "ST_TEST_HIT_CHANCE", _ally, 3);
            StatusActionData action = HookTestEnv.InjectStatusAction(_dm, status.StatusID2,
                "OnTrigger", "", "Chance(0.5)", maxTimePerLife: 0, outgoingHitSource: "All");
            action.SetTriggerLimits("Hit", "1");
            var random = new SequenceBattleRandomSource(new[] { 0.9f, 0.1f });
            BattleRandom.SetSource(random);
            try
            {
                SubmitOutgoingTargets(3);
                Assert.That(status.LifeTriggerCount, Is.EqualTo(1));
                Assert.That(random.FloatCallCount, Is.EqualTo(2),
                    "首目标失败不占次数，第二目标成功预留后，第三目标不再重复判概率");
            }
            finally { BattleRandom.ResetSource(); }
        }

        private void SubmitOutgoingTargets(int count)
        {
            while (_otherTargets.Count < count - 1)
                _otherTargets.Add(_env.CreateEnemy(_otherTargets.Count + 2, 100f).Entity);
            using (var batch = new HitBatch())
            {
                for (int index = 0; index < count; index++)
                    batch.Resolve(new HitRequest
                    {
                        Target = index == 0 ? _enemy : _otherTargets[index - 1],
                        DamageBeforeShield = 1f, Element = "Physical",
                        Source = DamageSourceInfo.Create(_ally, ReactionSourceKind.CharacterSkill, "SK_TEST", "SE_TEST"),
                        HitIndex = 0, TargetIndex = index
                    });
                batch.Flush();
            }
        }
    }
}
