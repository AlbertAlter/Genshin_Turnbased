using NUnit.Framework;
using UnityEngine;

namespace GenshinTurnBased.Tests.EditMode
{
    /// <summary>
    /// Kaeya_C1 钩子测试（2026-08-19，规格第 11 项）：
    /// 普攻/重击攻击带冰附着或冻结的敌人时暴击率 +15%（真实配表 ST_Kaeya_C1 = CritRate 0.15）。
    /// </summary>
    public class KaeyaC1HookTests
    {
        private const float ExpectedBonus = 0.15f;

        private GameObject _dmObject;
        private HookTestDataManager _dm;
        private GameObject _kaeyaObject;
        private BattleEntity _kaeya;
        private GameObject _icyObject;
        private BattleEntity _icyTarget;
        private GameObject _cleanObject;
        private BattleEntity _cleanTarget;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _dmObject = new GameObject("KaeyaC1_DM");
            _dm = _dmObject.AddComponent<HookTestDataManager>();
            _dm.ReloadAllData();
            HookTestEnv.SetDataManagerSingleton(_dm);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            HookTestEnv.DestroyDataManager(_dm);
            _dmObject = null;
        }

        [SetUp]
        public void SetUp()
        {
            HookTestEnv.ResetStaticState();

            _kaeyaObject = new GameObject("KaeyaC1_Kaeya");
            _kaeya = _kaeyaObject.AddComponent<BattleEntity>();
            _kaeya.EntityID = 1010;
            _kaeya.Type = BattleEntity.EntityType.Character;
            HookTestEnv.AttachStatus(_kaeya, "ST_Kaeya_C1", _kaeya, 3);

            _icyObject = new GameObject("KaeyaC1_IcyTarget");
            _icyTarget = _icyObject.AddComponent<BattleEntity>();
            _icyTarget.EntityID = 20001;

            _cleanObject = new GameObject("KaeyaC1_CleanTarget");
            _cleanTarget = _cleanObject.AddComponent<BattleEntity>();
            _cleanTarget.EntityID = 20002;
        }

        [TearDown]
        public void TearDown()
        {
            HookTestEnv.ResetStaticState();
            Object.DestroyImmediate(_icyObject);
            Object.DestroyImmediate(_cleanObject);
            Object.DestroyImmediate(_kaeyaObject);
        }

        [Test]
        public void NormalAttack_OnCryoAuraTarget_CritRateIncreases()
        {
            _icyTarget.ApplyAura("Cryo", 1f, 1010);

            float bonus = _kaeya.GetStatusCritRate("SE_Normal_Kaeya", "SK_Normal_Kaeya", 0, "None", _icyTarget);

            Assert.That(bonus, Is.EqualTo(ExpectedBonus).Within(0.0001f), "普攻攻击冰附着目标应获得 15% 暴击率");
        }

        [Test]
        public void HeavyAttack_OnFrozenTarget_CritRateIncreases()
        {
            var snapshot = ReactionSourceSnapshot.Capture(new ReactionContext
            {
                SourceEntity = _kaeya,
                Target = _icyTarget,
                SourceKind = ReactionSourceKind.CharacterSkill,
                SourceSkillID = "SK_Heavy_Kaeya",
                SourceEffectID = "SE_Heavy_Kaeya",
                AttackElement = "Cryo",
                AttackAmount = 1f
            });
            ReactionStateSystem.SetEntityState(_icyTarget, ReactionType.Frozen, 1, snapshot);

            float bonus = _kaeya.GetStatusCritRate("SE_Heavy_Kaeya", "SK_Heavy_Kaeya", 1, "None", _icyTarget);

            Assert.That(bonus, Is.EqualTo(ExpectedBonus).Within(0.0001f), "重击攻击冻结目标应获得 15% 暴击率");
        }

        [Test]
        public void CleanTarget_NoAuraNoFrozen_NoBonus()
        {
            float bonus = _kaeya.GetStatusCritRate("SE_Normal_Kaeya", "SK_Normal_Kaeya", 0, "None", _cleanTarget);

            Assert.That(bonus, Is.Zero, "无冰附着无冻结的目标不应享受加成");
        }

        [Test]
        public void CryoElementAttackOnCleanTarget_NoBonus()
        {
            // 攻击本身为冰元素，但目标攻击前没有冰附着/冻结，本次不享受加成
            float bonus = _kaeya.GetStatusCritRate("SE_Normal_Kaeya", "SK_Normal_Kaeya", 0, "Cryo", _cleanTarget);

            Assert.That(bonus, Is.Zero);
        }

        [Test]
        public void SkillAndBurst_DoNotBenefit()
        {
            _icyTarget.ApplyAura("Cryo", 1f, 1010);

            float skillBonus = _kaeya.GetStatusCritRate("SE_Skill_Kaeya1", "SK_Skill_Kaeya", 2, "Cryo", _icyTarget);
            float burstBonus = _kaeya.GetStatusCritRate("SE_Kaeya_BurstTrigger", "SK_Burst_Kaeya", 3, "Cryo", _icyTarget);

            Assert.That(skillBonus, Is.Zero, "战技不享受 C1 暴击率加成");
            Assert.That(burstBonus, Is.Zero, "爆发不享受 C1 暴击率加成");
        }

        [Test]
        public void DifferentTargets_JudgedIndependently()
        {
            _icyTarget.ApplyAura("Cryo", 1f, 1010);

            float icyBonus = _kaeya.GetStatusCritRate("SE_Normal_Kaeya", "SK_Normal_Kaeya", 0, "None", _icyTarget);
            float cleanBonus = _kaeya.GetStatusCritRate("SE_Normal_Kaeya", "SK_Normal_Kaeya", 0, "None", _cleanTarget);

            Assert.That(icyBonus, Is.EqualTo(ExpectedBonus).Within(0.0001f));
            Assert.That(cleanBonus, Is.Zero, "不同目标应分别判断");
        }
    }
}
