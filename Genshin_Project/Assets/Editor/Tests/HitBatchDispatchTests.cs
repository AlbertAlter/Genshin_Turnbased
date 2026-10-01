using NUnit.Framework;
using UnityEngine;

namespace GenshinTurnBased.Tests.EditMode
{
    /// <summary>Hit/OnDamage 语义、批次元数据与状态来源的回归测试。</summary>
    public sealed class HitBatchDispatchTests
    {
        private HookTestDataManager _dm;
        private BattleManager _battleManager;
        private CharacterBattleController _allyController;
        private BattleEntity _ally;
        private BattleEntity _enemy;

        [SetUp]
        public void SetUp()
        {
            HookTestEnv.ResetStaticState();
            _dm = HookTestEnv.CreateDataManager();
            _battleManager = HookTestEnv.CreateBattleManager();
            _allyController = HookTestEnv.CreateCharacter(1010, 80, 1, _battleManager);
            _ally = _allyController.Entity;
            _ally.TotalHP = 100f;
            _ally.CurrentHP = 100f;
            _enemy = HookTestEnv.CreateEnemy(20000, 1, 1, _battleManager, 100f);
        }

        [TearDown]
        public void TearDown()
        {
            HookTestEnv.ResetStaticState();
            HookTestEnv.ClearBattleManagerSingleton();
            if (_enemy != null) Object.DestroyImmediate(_enemy.gameObject);
            if (_allyController != null) Object.DestroyImmediate(_allyController.gameObject);
            if (_battleManager != null) Object.DestroyImmediate(_battleManager.gameObject);
            HookTestEnv.DestroyDataManager(_dm);
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
        public void RefreshingCharacterStatus_UpdatesOwnerAndDamageSourceSnapshot()
        {
            StatusInstance status = _ally.AddStatus("ST_TEST_OWNER", _enemy, 2);
            Assert.That(status.HasSourceEntity, Is.True);
            Assert.That(status.SourceEntityID, Is.EqualTo(_enemy.EntityID));
            Assert.That(status.SourceSide, Is.EqualTo(BattleSide.Enemy));

            StatusInstance refreshed = _ally.AddStatus("ST_TEST_OWNER", _ally, 2);
            DamageSourceInfo source = DamageSourceInfo.FromStatus(
                refreshed, "STE_TEST_OWNER", ReactionType.None);

            Assert.That(refreshed, Is.SameAs(status));
            Assert.That(refreshed.Caster, Is.SameAs(_ally));
            Assert.That(refreshed.SourceEntityID, Is.EqualTo(_ally.EntityID));
            Assert.That(refreshed.SourceSide, Is.EqualTo(BattleSide.Ally));
            Assert.That(source.SourceStatus, Is.SameAs(refreshed));
            Assert.That(source.SourceStatusID, Is.EqualTo("ST_TEST_OWNER"));
            Assert.That(source.SourceStatusApplyOrder, Is.EqualTo(refreshed.ApplyOrder));
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
    }
}
