using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace GenshinTurnBased.Tests.EditMode
{
    public class ReactionDamageCalculatorTests
    {
        private GameObject _targetObject;
        private BattleEntity _target;

        [SetUp]
        public void SetUp()
        {
            ReactionResolver.ResetSession();
            ReactionStateSystem.SetDisplayAdapter(null);
            _targetObject = new GameObject("ReactionCalculator_Target");
            _target = _targetObject.AddComponent<BattleEntity>();
        }

        [TearDown]
        public void TearDown()
        {
            ReactionResolver.ResetSession();
            ReactionStateSystem.SetDisplayAdapter(null);
            Object.DestroyImmediate(_targetObject);
        }

        [Test]
        public void GetLevelCoefficient_ClampsToConfiguredLevelRange()
        {
            var coefficients = new Dictionary<int, float>
            {
                [1] = 17.2f,
                [100] = 330.8f
            };

            Assert.That(ReactionDamageCalculator.GetLevelCoefficient(coefficients, 0), Is.EqualTo(17.2f));
            Assert.That(ReactionDamageCalculator.GetLevelCoefficient(coefficients, 101), Is.EqualTo(330.8f));
        }

        [Test]
        public void CalculateAmplifying_UsesSeparateCharacterAndEnemyFormulas()
        {
            float characterDamage = ReactionDamageCalculator.CalculateCharacterAmplifying(100f, 1.5f, 200f);
            float enemyDamage = ReactionDamageCalculator.CalculateEnemyAmplifying(100f, 1.5f);

            Assert.That(characterDamage, Is.EqualTo(202.125f).Within(0.001f));
            Assert.That(enemyDamage, Is.EqualTo(150f).Within(0.001f));
        }

        [Test]
        public void CalculateTransformative_UsesCharacterAndEnemyDedicatedFormulas()
        {
            float characterDamage = ReactionDamageCalculator.CalculateCharacterTransformative(
                100f, 4f, 200f, 0.2f, 10f, 0.9f);
            float enemyDamage = ReactionDamageCalculator.CalculateEnemyTransformative(
                100f, 4f, 0.2f, 0.9f);

            float expectedCharacter = (100f * 4f * (1f + 16f * 200f / 2200f + 0.2f) + 10f) * 0.9f;
            Assert.That(characterDamage, Is.EqualTo(expectedCharacter).Within(0.001f));
            Assert.That(enemyDamage, Is.EqualTo(432f).Within(0.001f));
        }

        [Test]
        public void CalculateEnemyQuicken_HasNoElementalMasteryInput()
        {
            float damage = ReactionDamageCalculator.CalculateEnemyQuicken(
                100f, 200f, ReactionType.Aggravate, 1.2f, 0.5f, 0.9f);
            float expected = (100f + 1.15f * 200f) * 1.2f * 0.5f * 0.9f;

            Assert.That(damage, Is.EqualTo(expected).Within(0.001f));
        }

        [Test]
        public void CalculateCharacterCrystalShield_UsesCharacterElementalMastery()
        {
            float shield = ReactionDamageCalculator.CalculateCharacterCrystalShield(100f, 200f);
            float expected = 100f * 4.5f * (1f + 16f * 200f / 2200f);

            Assert.That(shield, Is.EqualTo(expected).Within(0.001f));
        }

        [Test]
        public void CollectReactionBonuses_FiltersReactionAndDamageElement()
        {
            var statuses = new List<StatusInstance>
            {
                NewStatus(1, "DMGBonus", "超载", "Pyro", 2),
                NewStatus(2, "DMGBonus", "感电", "Pyro", 1),
                NewStatus(3, "DMGBonus", "超载", "Electro", 1),
                NewStatus(4, "BaseDMGBonusFlat", "超载", "Pyro", 1)
            };
            var effects = new List<StatusEffectData>
            {
                NewBuffEffect(1, 0.25f),
                NewBuffEffect(2, 0.5f),
                NewBuffEffect(3, 0.75f),
                NewBuffEffect(4, 10f)
            };

            ReactionBuffTotals totals = ReactionDamageCalculator.CollectReactionBonuses(
                statuses, effects, ReactionType.Overloaded, "Pyro");

            Assert.That(totals.DMGBonus, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(totals.BaseDMGBonusFlat, Is.EqualTo(10f).Within(0.001f));
        }

        [Test]
        public void ReactionStateDisplayAdapter_ReceivesRefreshAndRemovalWithoutOwningState()
        {
            var adapter = new RecordingDisplayAdapter();
            ReactionStateSystem.SetDisplayAdapter(adapter);
            var snapshot = new ReactionSourceSnapshot();

            ReactionStateSystem.SetEntityState(_target, ReactionType.Frozen, 1, snapshot);
            ReactionStateSystem.SetEntityState(_target, ReactionType.Frozen, 3, snapshot);
            bool removed = ReactionStateSystem.RemoveEntityState(_target, ReactionType.Frozen);

            Assert.That(adapter.ApplyCount, Is.EqualTo(2));
            Assert.That(adapter.LastDuration, Is.EqualTo(3));
            Assert.That(adapter.RemoveCount, Is.EqualTo(1));
            Assert.That(removed, Is.True);
            Assert.That(ReactionStateSystem.ActiveStates, Is.Empty);
        }

        private static StatusInstance NewStatus(
            int statusID,
            string multiplierPart,
            string reactionType,
            string elementType,
            int stackCount)
        {
            return new StatusInstance
            {
                StatusID2 = $"ST_Test_{statusID}",
                StackCount = stackCount,
                IsActive = true,
                MainData = new StatusMainData
                {
                    StatusID = statusID,
                    StatusID2 = $"ST_Test_{statusID}",
                    MultiplierPart1 = multiplierPart,
                    ApplyReactionType = reactionType,
                    ApplyElementType = elementType
                }
            };
        }

        private static StatusEffectData NewBuffEffect(int statusID, float value)
        {
            return new StatusEffectData
            {
                StatusEffectID = statusID * 100 + 1,
                EffectType = "Buff",
                Param1 = value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            };
        }

        private sealed class RecordingDisplayAdapter : IReactionStateDisplayAdapter
        {
            public int ApplyCount;
            public int RemoveCount;
            public int LastDuration;

            public void ApplyOrRefresh(ReactionStateInstance state)
            {
                ApplyCount++;
                LastDuration = state.RemainingRounds;
            }

            public void Remove(BattleEntity target, ReactionType type)
            {
                RemoveCount++;
            }
        }
    }
}
