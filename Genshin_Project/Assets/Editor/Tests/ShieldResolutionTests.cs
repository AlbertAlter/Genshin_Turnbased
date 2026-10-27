using NUnit.Framework;
using UnityEngine;

namespace GenshinTurnBased.Tests.EditMode
{
    public class ShieldResolutionTests
    {
        private GameObject _entityObject;
        private BattleEntity _entity;

        [SetUp]
        public void SetUp()
        {
            _entityObject = new GameObject("ShieldResolution_Entity");
            _entity = _entityObject.AddComponent<BattleEntity>();
            _entity.TotalHP = _entity.CurrentHP = 1000f;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_entityObject);
        }

        [TestCase("Pyro", "Pyro", 2.5f)]
        [TestCase("Pyro", "Hydro", 1.5f)]
        [TestCase("Pyro", "Physical", 1.5f)]
        [TestCase("Geo", "Pyro", 2.5f)]
        [TestCase("Geo", "Physical", 2.5f)]
        [TestCase("", "Pyro", 1f)]
        public void AbsorbDamage_UsesDocumentMultiplier(
            string shieldElement,
            string damageElement,
            float multiplier)
        {
            _entity.AddShield(20f, shieldElement, 2f);

            float remaining = _entity.AbsorbDamageWithShield(100f, damageElement);

            Assert.That(remaining, Is.EqualTo(100f - 20f * multiplier).Within(0.001f));
            Assert.That(_entity.Shields, Is.Empty);
        }

        [Test]
        public void SkillAndCrystallizeShield_TakeSameHitWithoutSerialStacking()
        {
            _entity.AddShield(1000f, string.Empty, 2f);
            _entity.AddOrReplaceCrystallizeShield(200f, "Hydro", 2, 2);

            float remaining = _entity.AbsorbDamageWithShield(300f, "Physical");

            Assert.That(remaining, Is.Zero);
            Assert.That(_entity.Shields, Has.Count.EqualTo(1));
            Assert.That(_entity.Shields[0].Kind, Is.EqualTo(ShieldKind.Skill));
            Assert.That(_entity.Shields[0].Value, Is.EqualTo(700f).Within(0.001f));
        }

        [Test]
        public void GetTotalShieldHP_ReturnsStrongestSingleShieldInsteadOfSum()
        {
            _entity.ShieldStrength = 0.5f;
            _entity.AddShield(10f, "Pyro", 2f);
            _entity.AddShield(20f, string.Empty, 2f);

            Assert.That(_entity.GetTotalShieldHP(), Is.EqualTo(30f).Within(0.001f));
            Assert.That(_entity.GetTotalShieldHP("Pyro"), Is.EqualTo(37.5f).Within(0.001f));
            Assert.That(_entity.GetTotalShieldHP("Hydro"), Is.EqualTo(30f).Within(0.001f));
        }

        [Test]
        public void TwoSmallShields_BlockByLargestSingleShieldRatherThanSum()
        {
            _entity.AddShield(10f, string.Empty, 2f);
            _entity.AddShield(20f, string.Empty, 2f);

            float remaining = _entity.AbsorbDamageWithShield(50f, "Physical");

            Assert.That(remaining, Is.EqualTo(30f).Within(0.001f));
            Assert.That(_entity.Shields, Is.Empty);
        }

        [Test]
        public void ShieldStrengthAndSingleShieldStrength_AreEachAppliedOnce()
        {
            _entity.ShieldStrength = 0.5f;
            _entity.AddShield(10f, "Pyro", 2f, strength: 2f);

            float remaining = _entity.AbsorbDamageWithShield(30f, "Hydro");

            Assert.That(remaining, Is.Zero);
            Assert.That(_entity.Shields[0].Value, Is.EqualTo(10f - 30f / 4.5f).Within(0.001f));
        }

        [Test]
        public void ZeroOrInvalidFactor_DoesNotDivideOrProduceNaN()
        {
            _entity.AddShield(10f, "Pyro", 2f, strength: 0f);

            float remaining = _entity.AbsorbDamageWithShield(30f, "Hydro");

            Assert.That(remaining, Is.EqualTo(30f));
            Assert.That(float.IsNaN(_entity.Shields[0].Value), Is.False);
            Assert.That(_entity.Shields[0].Value, Is.EqualTo(10f));
        }

        [Test]
        public void NewCrystallizeShield_ReplacesOnlyCrystallizeAndRefreshesAllFields()
        {
            _entity.AddShield(50f, string.Empty, 9f);
            Shield oldCrystal = _entity.AddOrReplaceCrystallizeShield(20f, "Hydro", 2, 2);

            Shield current = _entity.AddOrReplaceCrystallizeShield(30f, "Pyro", 2, 5);

            Assert.That(current, Is.SameAs(oldCrystal));
            Assert.That(_entity.Shields, Has.Count.EqualTo(2));
            Assert.That(current.Value, Is.EqualTo(30f));
            Assert.That(current.Element, Is.EqualTo("Pyro"));
            Assert.That(current.Duration, Is.EqualTo(2f));
            Assert.That(current.Strength, Is.EqualTo(1f));
            Assert.That(current.OriginPhase, Is.EqualTo(5));
            Assert.That(_entity.Shields.Exists(shield => shield.Kind == ShieldKind.Skill), Is.True);
        }

        [Test]
        public void CrystallizeShield_TicksOnlyAtOriginPhaseFromTwoToOneToRemoved()
        {
            Shield crystal = _entity.AddOrReplaceCrystallizeShield(20f, "Hydro", 2, 2);

            _entity.TickShields(3);
            Assert.That(crystal.Duration, Is.EqualTo(2f));
            _entity.TickShields(2);
            Assert.That(crystal.Duration, Is.EqualTo(1f));
            _entity.TickShields(4);
            Assert.That(crystal.Duration, Is.EqualTo(1f));
            _entity.TickShields(2);
            Assert.That(_entity.Shields, Is.Empty);
        }

        [Test]
        public void EnemyShieldStatus_ReplacesPreviousStatusAndBoundShield()
        {
            _entity.Type = BattleEntity.EntityType.Enemy;
            var shieldMain = new StatusMainData { StatusID2 = "ST_Shield_A", StatusType = "Shield" };
            StatusInstance first = _entity.AddStatus("ST_Shield_A", null, 3, 1, 1, shieldMain);
            _entity.AddOrReplaceEnemyElementalShield(12f, "Pyro", first);

            var nextMain = new StatusMainData { StatusID2 = "ST_Shield_B", StatusType = "Shield" };
            StatusInstance second = _entity.AddStatus("ST_Shield_B", null, 4, 1, 1, nextMain);

            Assert.That(_entity.HasStatus("ST_Shield_A"), Is.False);
            Assert.That(_entity.GetEnemyElementalShield(), Is.Null);
            Assert.That(_entity.HasStatus("ST_Shield_B"), Is.True);

            Shield secondShield = _entity.AddOrReplaceEnemyElementalShield(20f, "Hydro", second);
            Assert.That(secondShield.SourceStatus, Is.SameAs(second));
            Assert.That(secondShield.SourceStatusApplyOrder, Is.EqualTo(second.ApplyOrder));
        }

        [Test]
        public void EnemyShieldStatus_SameIdReapplyCreatesNewInstanceInsteadOfStacking()
        {
            _entity.Type = BattleEntity.EntityType.Enemy;
            var shieldMain = new StatusMainData { StatusID2 = "ST_Shield", StatusType = "Shield" };
            StatusInstance first = _entity.AddStatus("ST_Shield", null, 3, 1, 1, shieldMain);
            _entity.AddOrReplaceEnemyElementalShield(12f, "Pyro", first);

            StatusInstance second = _entity.AddStatus("ST_Shield", null, 5, 1, 1, shieldMain);

            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(second.ApplyOrder, Is.GreaterThan(first.ApplyOrder));
            Assert.That(second.StackCount, Is.EqualTo(1));
            Assert.That(_entity.GetEnemyElementalShield(), Is.Null);
        }

        [Test]
        public void OrdinaryDamageAbsorption_IgnoresEnemyElementalShield()
        {
            _entity.Type = BattleEntity.EntityType.Enemy;
            var shieldMain = new StatusMainData { StatusID2 = "ST_Shield", StatusType = "Shield" };
            StatusInstance status = _entity.AddStatus("ST_Shield", null, 3, 1, 1, shieldMain);
            Shield enemyShield = _entity.AddOrReplaceEnemyElementalShield(12f, "Pyro", status);

            float remaining = _entity.AbsorbDamageWithShield(30f, "Hydro");

            Assert.That(remaining, Is.EqualTo(30f));
            Assert.That(enemyShield.Value, Is.EqualTo(12f));
            Assert.That(_entity.GetTotalShieldHP(), Is.Zero);
            Assert.That(_entity.GetEnemyElementalShieldHP(), Is.EqualTo(12f));
        }

        [Test]
        public void RemovingEnemyShieldStatus_RemovesOnlyItsBoundShield()
        {
            _entity.Type = BattleEntity.EntityType.Enemy;
            var shieldMain = new StatusMainData { StatusID2 = "ST_Shield", StatusType = "Shield" };
            StatusInstance status = _entity.AddStatus("ST_Shield", null, 3, 1, 1, shieldMain);
            _entity.AddShield(8f, string.Empty, 2f);
            _entity.AddOrReplaceEnemyElementalShield(12f, "Pyro", status);

            Assert.That(_entity.RemoveStatus("ST_Shield"), Is.True);

            Assert.That(_entity.GetEnemyElementalShield(), Is.Null);
            Assert.That(_entity.Shields.Exists(shield => shield.Kind == ShieldKind.Skill), Is.True);
        }

        [Test]
        public void BreakingEnemyElementalShield_RemovesOwningStatusThroughUnifiedPath()
        {
            _entity.Type = BattleEntity.EntityType.Enemy;
            var shieldMain = new StatusMainData { StatusID2 = "ST_Shield", StatusType = "Shield" };
            StatusInstance status = _entity.AddStatus("ST_Shield", null, 3, 1, 1, shieldMain);
            _entity.AddShield(8f, string.Empty, 2f);
            _entity.AddOrReplaceEnemyElementalShield(12f, "Pyro", status);

            float applied = _entity.ApplyEnemyElementalShieldLoss(20f, out bool broken);

            Assert.That(applied, Is.EqualTo(12f));
            Assert.That(broken, Is.True);
            Assert.That(_entity.HasStatus("ST_Shield"), Is.False);
            Assert.That(_entity.GetEnemyElementalShield(), Is.Null);
            Assert.That(_entity.Shields.Exists(shield => shield.Kind == ShieldKind.Skill), Is.True);
        }

        [Test]
        [Timeout(6000)]
        public void CharacterShieldStatus_ConfiguredReplacementRebuildsItsShieldAndPreservesOtherShields()
        {
            using (var env = new BattleFlowTestEnv())
            {
                const string statusID = "ST_PlayerShield";
                const string effectID = "STE_PlayerShield";
                // 文档规定同一角色的技能盾刷新；由单例状态配表完成覆盖，不复用旧实例。
                env.ConfigureStatus(statusID, maxStack: 1, whenMax: "0",
                    actionType: "OnApply", actionEffectID2: effectID);
                var main = env.DataManager.StatusMainDict[statusID];
                main.StatusID = 69021;
                main.StatusType = "Shield";
                var shieldEffect = new StatusEffectData
                {
                    StatusEffectID = 6902101, StatusEffectID2 = effectID, EffectType = "Shield",
                    Param1 = "15", Element = "Cryo", Duration = 2, TargetType = "Self"
                };
                env.DataManager.StatusEffectDict[effectID] = shieldEffect;
                env.ConfigureAllyAction(2, "SK_PlayerShield", "SE_PlayerShield", "Physical", null,
                    apCost: 10, effectType: "ApplyStatus", param1: statusID, duration: 2,
                    addInPhase: (int)TurnPhase.AllyAction, targetType: "Self");
                env.CreateMinimalBattle();
                env.Manager.StartBattle();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                var entity = env.Ally.Entity;
                _entity.EntityID = 990003;
                _entity.Side = BattleSide.Ally;
                var otherMain = new StatusMainData
                {
                    StatusID = 69022, StatusID2 = "ST_OtherPlayerShield", StatusType = "Shield",
                    MaxStack = 1, WhenMax = "0"
                };
                env.DataManager.StatusMainDict[otherMain.StatusID2] = otherMain;
                StatusInstance otherStatus = entity.AddStatus(otherMain.StatusID2, _entity, 2, 2, 0, otherMain);
                entity.AddShield(8f, "Hydro", 2f, sourceStatus: otherStatus);
                Shield otherSkillShield = entity.Shields[0];
                Shield crystallize = entity.AddOrReplaceCrystallizeShield(20f, "Geo", 2, 2);

                Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
                StatusInstance first = entity.GetStatus(statusID);
                Assert.That(first, Is.Not.Null);
                Shield oldShield = entity.Shields.Find(shield => shield.SourceStatus == first);
                Assert.That(oldShield, Is.Not.Null);
                Assert.That(oldShield.Value, Is.EqualTo(15f));
                Assert.That(oldShield.Duration, Is.EqualTo(2f));

                env.DataManager.SkillEffectDict["SE_PlayerShield"].Duration = 5;
                shieldEffect.Duration = 5;
                shieldEffect.Param1 = "25";
                Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
                StatusInstance replacement = entity.GetStatus(statusID);
                Assert.That(replacement, Is.Not.Null.And.Not.SameAs(first));
                Assert.That(first.IsActive, Is.False);
                Assert.That(entity.HasStatusInstance(first), Is.False);
                Assert.That(entity.GetStatusInstances(statusID), Has.Count.EqualTo(1));
                Assert.That(entity.Shields, Has.No.Member(oldShield));
                Assert.That(entity.Shields, Has.Count.EqualTo(3));
                Shield newShield = entity.Shields.Find(shield => shield.SourceStatus == replacement);
                Assert.That(newShield, Is.Not.Null);
                Assert.That(newShield.Value, Is.EqualTo(25f));
                Assert.That(newShield.Duration, Is.EqualTo(5f));
                Assert.That(entity.Shields, Has.Member(otherSkillShield));
                Assert.That(entity.Shields, Has.Member(crystallize));
                Assert.That(entity.HasStatusInstance(otherStatus), Is.True,
                    "覆盖本角色的盾不能移除另一角色的护盾状态");

                Assert.That(entity.RemoveStatus(statusID), Is.True);
                Assert.That(entity.Shields, Has.No.Member(newShield));
                CollectionAssert.AreEquivalent(new[] { otherSkillShield, crystallize }, entity.Shields);
                Assert.That(entity.HasStatusInstance(otherStatus), Is.True);
            }
        }

        [Test]
        public void ResetBattle_ClearsRegisteredEntityShieldsBeforeUnregistering()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                env.Ally.Entity.AddOrReplaceCrystallizeShield(20f, "Hydro", 2, 2);
                env.Enemy.Entity.AddShield(30f, "Pyro", 2f);

                env.Manager.ResetBattle();

                Assert.That(env.Ally.Entity.Shields, Is.Empty);
                Assert.That(env.Enemy.Entity.Shields, Is.Empty);
                Assert.That(env.Manager.Allies, Is.Empty);
                Assert.That(env.Manager.Enemies, Is.Empty);
            }
        }
    }
}
