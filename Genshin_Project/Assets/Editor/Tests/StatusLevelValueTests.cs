using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class StatusLevelValueTests
    {
        private static void Levels(BattleFlowTestEnv env, string id, int type, string one, string two)
        {
            env.DataManager.SkillLevelDict[$"{BattleFlowTestEnv.AllyID}_{type}_{id}"] = new Dictionary<int, SkillLevelData>
            {
                [1] = new SkillLevelData { CharacterID = BattleFlowTestEnv.AllyID, ParamID = id, SkillType = type, SkillLevel = 1, Hits1 = one },
                [2] = new SkillLevelData { CharacterID = BattleFlowTestEnv.AllyID, ParamID = id, SkillType = type, SkillLevel = 2, Hits1 = two }
            };
        }

        [Test]
        public void EmptyBuffParam_ReadsOwningTalentLevel_AndFeedsDamageTaken()
        {
            using var env = new BattleFlowTestEnv(); const string id = "STE_LevelReduction";
            Levels(env, id, 3, "-0.2", "-0.21"); env.CreateMinimalBattle();
            env.Ally.SkillLevels[3] = 2;
            var main = new StatusMainData { StatusID = 4999001, StatusID2 = "ST_LevelReduction", StatusType = "Buff",
                MultiplierPart = "DMGTaken", MaxCount = 1, MaxStack = 1 };
            env.DataManager.StatusMainDict[main.StatusID2] = main;
            var effect = new StatusEffectData { StatusEffectID = 499900101, StatusEffectID2 = id, EffectType = "Buff" };
            env.DataManager.StatusEffectDict[id] = effect;
            env.Ally.Entity.AddStatus(main.StatusID2, env.Ally.Entity, 4, 2, 0, main);
            Assert.That(env.Ally.TryResolveStatusLevelScalar(effect, out float scalar), Is.True);
            Assert.That(scalar, Is.EqualTo(-.21f).Within(.000001));
            Assert.That(env.Ally.Entity.PrepareDMGTaken("x", "x", "Normal", "Physical").GetMultiplier(ReactionType.None),
                Is.EqualTo(.79f).Within(.000001));
            effect.Param1 = "-0.3";
            Assert.That(env.Ally.TryResolveStatusLevelScalar(effect, out _), Is.False, "显式参数不被等级表覆盖");
        }

        [UnityTest]
        public IEnumerator Shield_UsesLevelFormulaIncludingFlatAmount_AndSnapshotsOnCreation()
            => HookTestEnv.AwaitTask(ShieldAsync);
        private async Task ShieldAsync()
        {
            using var env = new BattleFlowTestEnv(); const string id = "STE_LevelShield";
            Levels(env, id, 2, "0.144*TotalHP+1386", "0.1548*TotalHP+1525");
            env.ConfigureStatus("ST_LevelShield", actionType: "OnApply", actionEffectID2: id);
            var main = env.DataManager.StatusMainDict["ST_LevelShield"]; main.StatusType = "Shield"; main.StatusID = 4999002;
            env.DataManager.StatusEffectDict[id] = new StatusEffectData
            { StatusEffectID = main.StatusID * 100 + 1, StatusEffectID2 = id, EffectType = "Shield", Element = "Electro", TargetType = "Self" };
            env.CreateMinimalBattle(); env.Ally.SkillLevels[2] = 2;
            await BattleExecution.Run(async () =>
            {
                env.Ally.Entity.AddStatus(main.StatusID2, env.Ally.Entity, 1, 6, 0, main);
                await BattleExecution.Flush();
            });
            Assert.That(env.Ally.Entity.Shields.Count, Is.EqualTo(1));
            Assert.That(env.Ally.Entity.Shields[0].Value, Is.EqualTo(.1548f * 1000 + 1525).Within(.001));
            env.Ally.Entity.TotalHP = 2000;
            Assert.That(env.Ally.Entity.Shields[0].Value, Is.EqualTo(1679.8f).Within(.001));
        }

        [TestCase("ShieldBonus")]
        [TestCase("ShieldBonusFlat")]
        public void SingleMultiplierPart_IsAccepted(string part)
            => Assert.That(StatusMultiplierParts.Parse(part, "test"), Is.EqualTo(part));

        [TestCase("ATKBonus;DMGBonus")]
        [TestCase("ShieldBouns")]
        public void InvalidMultiplierPart_IsRejected(string part)
            => Assert.Throws<TableValidationException>(() => StatusMultiplierParts.Parse(part, "test"));

        [Test]
        public void ShieldAmount_ClampsOnlyFinalValue_AndRejectsNonFinite()
        {
            Assert.That(BattleEntity.CalculateShieldAmount(100, -2, 150), Is.EqualTo(50));
            Assert.That(BattleEntity.CalculateShieldAmount(100, -.5f, -80), Is.Zero);
            Assert.Throws<System.InvalidOperationException>(() => BattleEntity.CalculateShieldAmount(100, float.NaN, 0));
        }

        [UnityTest]
        public IEnumerator ShieldBonuses_ReadCreator_UseAdditivePercent_AndLeaveExistingShieldsFixed()
            => HookTestEnv.AwaitTask(ShieldBonusesAsync);
        private async Task ShieldBonusesAsync()
        {
            using var e = new BattleFlowTestEnv();
            const string id = "STE_ShieldBonuses", root = "ST_ShieldBonuses";
            e.ConfigureStatus(root, actionType: "OnApply", actionEffectID2: id);
            var main = e.DataManager.StatusMainDict[root]; main.StatusType = "Shield"; main.StatusID = 4999100;
            e.DataManager.StatusEffectDict[id] = new StatusEffectData
            { StatusEffectID = main.StatusID * 100 + 1, StatusEffectID2 = id, EffectType = "Shield",
                Element = "Cryo", Param1 = "1000", Duration = 3, TargetType = "Allies", TargetSelect = "-1" };
            e.CreateMinimalBattle(); var other = e.CreateAlly(2).Entity;
            StatusInstance Buff(int n, BattleEntity recipient, string part, string value, string scope = null)
            {
                var data = new StatusMainData { StatusID = n, StatusID2 = "ST_ShieldBonus_" + n,
                    StatusType = "Buff", MultiplierPart = part, ApplyString = scope, MaxCount = 1 };
                e.DataManager.StatusMainDict[data.StatusID2] = data;
                e.DataManager.StatusEffectDict[data.StatusID2] = new StatusEffectData
                { StatusEffectID = n * 100 + 1, StatusEffectID2 = data.StatusID2, EffectType = "Buff", Param1 = value };
                return recipient.AddStatus(data.StatusID2, recipient, 3, 2, 0, data);
            }
            var percent = Buff(4999101, e.Ally.Entity, "ShieldBonus", ".75");
            Buff(4999102, e.Ally.Entity, "ShieldBonus", ".15");
            Buff(4999103, e.Ally.Entity, "ShieldBonusFlat", "100");
            Buff(4999104, other, "ShieldBonus", "10");
            Buff(4999105, e.Ally.Entity, "ShieldBonus", "99", "STE_AnotherShield");
            e.Ally.Entity.ShieldStrength = 2; other.ShieldStrength = 4;
            await BattleExecution.Run(async () =>
            {
                e.Ally.Entity.AddStatus(root, e.Ally.Entity, 3, 2, 0, main);
                await BattleExecution.Flush();
            });
            Assert.That(e.Ally.Entity.Shields[0].Value, Is.EqualTo(2000).Within(.001));
            Assert.That(other.Shields[0].Value, Is.EqualTo(2000).Within(.001), "受盾者的造盾加成不能替代来源加成，盾强不加入原始盾量");
            e.Ally.Entity.RemoveStatusInstance(percent);
            Assert.That(other.Shields[0].Value, Is.EqualTo(2000).Within(.001), "旧盾不随来源状态变化重算");
        }
    }
}
