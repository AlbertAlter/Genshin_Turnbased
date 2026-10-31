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
        public void CalculateAmplifying_BaseBonusAddsAlongsideMastery_NotAsAnotherFinalMultiplier()
        {
            float baseline = ReactionDamageCalculator.CalculateCharacterAmplifying(100f, 1.5f, 200f);
            float actual = ReactionDamageCalculator.CalculateCharacterAmplifying(100f, 1.5f, 200f, .15f);
            Assert.That(actual, Is.EqualTo(100f * 1.5f * (1f + 2.78f * 200f / 1600f + .15f)).Within(.001f));
            Assert.That(actual - baseline, Is.EqualTo(100f * 1.5f * .15f).Within(.001f));
            Assert.That(actual, Is.Not.EqualTo(baseline * 1.15f).Within(.001f));
        }

        [TestCase(ReactionType.Aggravate, 1.15f)]
        [TestCase(ReactionType.Spread, 1.25f)]
        public void CalculateQuicken_BaseBonusOnlyRaisesReactionIncrement_NotSkillBase(
            ReactionType reaction, float coefficient)
        {
            float baseline = ReactionDamageCalculator.CalculateCharacterQuicken(200f, 100f, reaction, 200f, 1.2f, .5f, .8f);
            float actual = ReactionDamageCalculator.CalculateCharacterQuicken(200f, 100f, reaction, 200f, 1.2f, .5f, .8f, .2f);
            float expected = (200f + coefficient * 100f * (1f + 5f * 200f / 1400f + .2f)) * 1.2f * .5f * .8f;
            Assert.That(actual, Is.EqualTo(expected).Within(.001f));
            Assert.That(actual - baseline, Is.EqualTo(coefficient * 100f * .2f * 1.2f * .5f * .8f).Within(.001f));
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

        [TestCase("月曜", ReactionType.LunarElectroCharged, true)]
        [TestCase("Lunar", ReactionType.LunarBloom, true)]
        [TestCase("月曜", ReactionType.LunarCrystallize, true)]
        [TestCase("星烁", ReactionType.StellarSuperconduct, true)]
        [TestCase("Stellar", ReactionType.StellarSuperconduct, true)]
        [TestCase("月曜", ReactionType.StellarSuperconduct, false)]
        [TestCase("星烁", ReactionType.Superconduct, false)]
        [TestCase("Lunar", ReactionType.ElectroCharged, false)]
        [TestCase("Stellar", ReactionType.Swirl, false)]
        [TestCase("Swirl", ReactionType.Swirl, true)]
        [TestCase("Superconduct", ReactionType.Superconduct, true)]
        [TestCase("Superconduct", ReactionType.StellarSuperconduct, false)]
        [TestCase("Electro-Charged", ReactionType.ElectroCharged, true)]
        [TestCase("ElectroCharged", ReactionType.ElectroCharged, true)]
        [TestCase("月曜;星烁", ReactionType.StellarSuperconduct, true)]
        [TestCase("All", ReactionType.LunarBloom, true)]
        [TestCase("all", ReactionType.Superconduct, true)]
        [TestCase("All", ReactionType.None, false)]
        [TestCase("Physical", ReactionType.None, false)]
        [TestCase("999", (ReactionType)999, false)]
        [TestCase("月曜", ReactionType.None, false)]
        public void ReactionFilter_RecognizesDeclaredFamiliesAndCanonicalNames_NotOrdinaryLookalikes(
            string filter, ReactionType reaction, bool expected)
        {
            Assert.That(ReactionFilterMatcher.Matches(filter, reaction), Is.EqualTo(expected));
        }

        [TestCase(ReactionType.LunarElectroCharged, "Electro", .37f, 0f, .2f)]
        [TestCase(ReactionType.LunarBloom, "Dendro", .02f, 0f, .2f)]
        [TestCase(ReactionType.LunarCrystallize, "Geo", .47f, 0f, .2f)]
        [TestCase(ReactionType.StellarSuperconduct, "Cryo", 0f, .53f, .2f)]
        [TestCase(ReactionType.StellarSuperconduct, "Electro", 0f, .03f, .2f)]
        [TestCase(ReactionType.Superconduct, "Cryo", 0f, 0f, .2f)]
        [TestCase(ReactionType.ElectroCharged, "Electro", 0f, 0f, .2f)]
        [TestCase(ReactionType.Swirl, "Geo", 0f, 0f, .2f)]
        public void SpecialReactionParts_KeepIndependentTotals_AndFilterFamilyAndActualDamageElement(
            ReactionType reaction, string element, float lunar, float stellar, float baseBonus)
        {
            var statuses = new[]
            {
                NewStatus(1, "DMGBonus", "", "", 1),
                NewStatus(2, "LunarDMGBonus", "月曜", "Electro", 1),
                NewStatus(3, "LunarDMGBonus", "Lunar", "Geo", 1),
                NewStatus(4, "StellarDMGBonus", "星烁", "Cryo", 1),
                NewStatus(5, "BaseDMGBonus", "", "", 1),
                NewStatus(6, "BaseDMGBonusFlat", "", "", 1),
                NewStatus(7, "LunarDMGBonus", "", "", 1),
                NewStatus(8, "StellarDMGBonus", "", "", 1),
                NewStatus(9, "LunarDMGBonus", "月曜", "Electro;Geo", 1)
            };
            var effects = new[]
            {
                NewBuffEffect(1, .1f), NewBuffEffect(2, .3f), NewBuffEffect(3, .4f),
                NewBuffEffect(4, .5f), NewBuffEffect(5, .2f), NewBuffEffect(6, 10f),
                NewBuffEffect(7, .02f), NewBuffEffect(8, .03f), NewBuffEffect(9, .05f)
            };
            ReactionBuffTotals totals = ReactionDamageCalculator.CollectReactionBonuses(statuses, effects, reaction, element);
            Assert.That(totals.DMGBonus, Is.EqualTo(.1f).Within(.00001f));
            Assert.That(totals.BaseDMGBonusFlat, Is.EqualTo(10f));
            Assert.That(totals.BaseDMGBonus, Is.EqualTo(baseBonus).Within(.00001f));
            Assert.That(totals.LunarDMGBonus, Is.EqualTo(lunar).Within(.00001f));
            Assert.That(totals.StellarDMGBonus, Is.EqualTo(stellar).Within(.00001f));
            Assert.That(ReactionDamageCalculator.CollectExclusiveReactionDamageBonus(statuses, effects, reaction, element), Is.Zero,
                "专用乘区不能混进普通反应专属DMGBonus返回值");
        }

        [TestCase(ReactionType.Aggravate, "Aggravate")]
        [TestCase(ReactionType.Vaporize, "Vaporize;Melt")]
        [TestCase(ReactionType.Melt, "Vaporize;Melt")]
        public void OrdinaryReactionBaseBonus_IsCollectedSeparatelyFromOtherParts(
            ReactionType reaction, string filter)
        {
            var status = NewStatus(1, "BaseDMGBonus", filter, "", 1);
            var totals = ReactionDamageCalculator.CollectReactionBonuses(new[] { status },
                new[] { NewBuffEffect(1, .15f) }, reaction, "Pyro");
            Assert.That(totals.BaseDMGBonus, Is.EqualTo(.15f).Within(.00001f));
            Assert.That(totals.DMGBonus, Is.Zero);
            Assert.That(totals.BaseDMGBonusFlat, Is.Zero);
            Assert.That(totals.LunarDMGBonus, Is.Zero);
            Assert.That(totals.StellarDMGBonus, Is.Zero);
        }

        [Test]
        public void ReactionCollectors_RespectEffectConditionsAndSemicolonElementFilters()
        {
            var status = NewStatus(1, "DMGBonus", "超导", "Cryo;Electro", 1);
            var gated = NewBuffEffect(1, .8f);
            gated.ScriptHook = "Hook(456302)";
            var stellarOnly = NewBuffEffect(1, 10f);
            stellarOnly.ScriptHook = "Check(999999_C6)";
            var statuses = new[] { status };
            var effects = new[] { gated, stellarOnly };
            Assert.That(ReactionDamageCalculator.CollectReactionBonuses(statuses, effects,
                ReactionType.Superconduct, "Cryo").DMGBonus, Is.EqualTo(.8f).Within(.00001f));
            Assert.That(ReactionDamageCalculator.CollectExclusiveReactionDamageBonus(statuses, effects,
                ReactionType.Superconduct, "Cryo"), Is.EqualTo(.8f).Within(.00001f));
            Assert.That(ReactionDamageCalculator.CollectReactionBonuses(statuses, effects,
                ReactionType.StellarSuperconduct, "Cryo").DMGBonus, Is.Zero);
            status.MainData.ApplyReactionType = "All";
            status.MainData.ApplyElementType = "all";
            Assert.That(ReactionDamageCalculator.CollectReactionBonuses(statuses, effects,
                ReactionType.Superconduct, "Cryo").DMGBonus, Is.EqualTo(.8f).Within(.00001f));
        }

        [Test]
        public void ReactionCollectors_IneligibleEffectDoesNotConsumeAnotherSourcesMaxCountSlot()
        {
            var first = NewStatus(1, "DMGBonus", "超导", "Cryo", 1);
            var second = NewStatus(1, "DMGBonus", "超导", "Cryo", 1);
            first.MainData.MaxCount = second.MainData.MaxCount = 1;
            _target.EntityID = 999999;
            _target.ConstellationLevel = 6;
            second.Caster = _target;
            var effect = NewBuffEffect(1, .8f);
            effect.ScriptHook = "Check(999999_C6)";
            var statuses = new[] { first, second };
            var effects = new[] { effect };
            Assert.That(ReactionDamageCalculator.CollectReactionBonuses(statuses, effects,
                ReactionType.Superconduct, "Cryo").DMGBonus, Is.EqualTo(.8f).Within(.00001f));
            Assert.That(ReactionDamageCalculator.CollectExclusiveReactionDamageBonus(statuses, effects,
                ReactionType.Superconduct, "Cryo"), Is.EqualTo(.8f).Within(.00001f));
            first.Caster = _target;
            Assert.That(ReactionDamageCalculator.CollectReactionBonuses(statuses, effects,
                ReactionType.Superconduct, "Cryo").DMGBonus, Is.EqualTo(.8f).Within(.00001f), "都适用时仍只计一次");
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
                    MultiplierPart = multiplierPart,
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
