using NUnit.Framework;
using UnityEngine;

namespace GenshinTurnBased.Tests.EditMode
{
    public class EnemyElementShieldSystemTests
    {
        private GameObject _targetObject;
        private BattleEntity _target;
        private StatusInstance _status;

        [SetUp]
        public void SetUp()
        {
            _targetObject = new GameObject("EnemyElementShield_Target");
            _target = _targetObject.AddComponent<BattleEntity>();
            _target.Type = BattleEntity.EntityType.Enemy;
            var main = new StatusMainData { StatusID2 = "ST_TestShield", StatusType = "Shield" };
            _status = _target.AddStatus("ST_TestShield", null, 3, 1, 1, main);
        }

        [TearDown]
        public void TearDown()
        {
            ReactionStateSystem.ClearAll();
            Object.DestroyImmediate(_targetObject);
        }

        [Test]
        public void ResolveHit_UsesElementColumnAndCommitsClampedLoss()
        {
            _target.AddOrReplaceEnemyElementalShield(3f, "Pyro", _status);
            var table = new EnemyShieldRuleTable();
            table.Add(EnemyShieldRuleParser.Parse("2", "Pyro", "Hydro"));

            EnemyShieldHitResult result = EnemyElementShieldSystem.ResolveHit(
                _target, "Hydro", 1f, 0f, rules: table);

            Assert.That(result.HadShieldAtStart, Is.True);
            Assert.That(result.ShieldRow, Is.EqualTo("Pyro"));
            Assert.That(result.HitColumn, Is.EqualTo("Hydro"));
            Assert.That(result.AppliedLoss, Is.EqualTo(2f));
            Assert.That(result.RemainingShield, Is.EqualTo(1f));
            Assert.That(result.BrokeShield, Is.False);
        }

        [Test]
        public void ResolveHit_ExplicitDerivedKindOverridesDamageElementColumn()
        {
            _target.AddOrReplaceEnemyElementalShield(5f, "Geo", _status);
            var table = new EnemyShieldRuleTable();
            table.Add(EnemyShieldRuleParser.Parse("0.01U*PoiseDMG", "Geo", "Overloaded"));

            EnemyShieldHitResult result = EnemyElementShieldSystem.ResolveHit(
                _target, "Pyro", 0f, 100f, "Overloaded", table);

            Assert.That(result.HitColumn, Is.EqualTo("Overloaded"));
            Assert.That(result.AppliedLoss, Is.EqualTo(1f));
            Assert.That(result.RemainingShield, Is.EqualTo(4f));
        }

        [Test]
        public void ResolveHit_FrozenHydroShieldUsesFreezeRowWithoutChangingBaseElement()
        {
            Shield shield = _target.AddOrReplaceEnemyElementalShield(5f, "Hydro", _status);
            ReactionStateSystem.SetEntityState(
                _target, ReactionType.Frozen, 2, null, reactionElementAmount: 1f);
            var table = new EnemyShieldRuleTable();
            table.Add(EnemyShieldRuleParser.Parse("2", "Freeze", "Cryo"));

            EnemyShieldHitResult result = EnemyElementShieldSystem.ResolveHit(
                _target, "Cryo", 1f, 0f, rules: table);

            Assert.That(result.ShieldRow, Is.EqualTo("Freeze"));
            Assert.That(result.AppliedLoss, Is.EqualTo(2f));
            Assert.That(shield.Element, Is.EqualTo("Hydro"));
        }

        [Test]
        public void ResolveHit_BreaksShieldAndRemovesItsStatus()
        {
            _target.AddOrReplaceEnemyElementalShield(1f, "Electro", _status);
            var table = new EnemyShieldRuleTable();
            table.Add(EnemyShieldRuleParser.Parse("1U", "Electro", "Hydro"));

            EnemyShieldHitResult result = EnemyElementShieldSystem.ResolveHit(
                _target, "Hydro", 2f, 0f, rules: table);

            Assert.That(result.BrokeShield, Is.True);
            Assert.That(result.RemainingShield, Is.Zero);
            Assert.That(_target.HasStatus("ST_TestShield"), Is.False);
        }

        [Test]
        public void ResolveHit_BlankRuleKeepsShieldButStillReportsStartedWithShield()
        {
            _target.AddOrReplaceEnemyElementalShield(5f, "Dendro", _status);

            EnemyShieldHitResult result = EnemyElementShieldSystem.ResolveHit(
                _target, "Geo", 1f, 100f, rules: new EnemyShieldRuleTable());

            Assert.That(result.HadShieldAtStart, Is.True);
            Assert.That(result.AppliedLoss, Is.Zero);
            Assert.That(result.RemainingShield, Is.EqualTo(5f));
        }
    }
}
