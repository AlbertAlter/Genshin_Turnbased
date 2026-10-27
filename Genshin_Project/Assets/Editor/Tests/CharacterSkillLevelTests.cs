using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class CharacterSkillLevelTests
    {
        private static void Constellations(BattleFlowTestEnv env)
        {
            env.DataManager.ConstellationDict[BattleFlowTestEnv.AllyID] = new List<ConstellationData>
            {
                new ConstellationData { ConstellationIndex = 3, Modifiers = "SkillLevelUp(2,3)" },
                new ConstellationData { ConstellationIndex = 5, Modifiers = "SkillLevelUp(3,3)" }
            };
        }

        [TestCase(0, 7, 8)]
        [TestCase(2, 7, 8)]
        [TestCase(3, 10, 8)]
        [TestCase(5, 10, 11)]
        [TestCase(6, 10, 11)]
        public void OwnedCharacter_ExposesBaseBonusAndOwnLevels_WithoutMutatingOrAccumulating(int constellation, int skill, int burst)
        {
            using var env = new BattleFlowTestEnv(); Constellations(env);
            var character = new RoguelikeCharacterProgressData
            { CharacterID = BattleFlowTestEnv.AllyID, SkillLevels = new[] { 5, 6, 7, 8 }, ConstellationLevel = constellation };
            var first = character.GetSkillLevelInfo(env.DataManager);
            Assert.That(first.GetBaseLevel(2), Is.EqualTo(7));
            Assert.That(first.GetBonus(2), Is.EqualTo(skill - 7));
            Assert.That(first.GetOwnLevel(2), Is.EqualTo(skill)); Assert.That(first.GetOwnLevel(3), Is.EqualTo(burst));
            var copy = first.CopyOwnLevels(); copy[2] = 1;
            Assert.That(first.GetOwnLevel(2), Is.EqualTo(skill));
            Assert.That(character.GetSkillLevelInfo().GetOwnLevel(2), Is.EqualTo(skill));
            Assert.That(character.SkillLevels, Is.EqualTo(new[] { 5, 6, 7, 8 }));
            character.ConstellationLevel = 0;
            Assert.That(character.GetSkillLevelInfo().GetOwnLevel(2), Is.EqualTo(7));
            Assert.That(first.GetOwnLevel(2), Is.EqualTo(skill), "已返回的只读结果不随后续养成变化被改写");
        }

        [Test]
        public void InventoryProfileAndBattleShareOwnLevelResult_AndRepeatedBuildDoesNotAddAgain()
        {
            using var env = new BattleFlowTestEnv(); Constellations(env);
            var runtime = new RoguelikeChapterRuntime(new RoguelikeProgressSnapshot { CurrentChapterId = 1 });
            runtime.AddCharacter(BattleFlowTestEnv.AllyID, 1000, 60);
            runtime.UpdateCharacterProgress(BattleFlowTestEnv.AllyID, 1, 0, 0, 5, new[] { 5, 6, 7, 8 });
            var view = new RoguelikeInventoryView(runtime.ExportSnapshot());
            Assert.That(view.GetCharacterSkillLevels(123456), Is.Null);
            var inventory = view.GetCharacterSkillLevels(BattleFlowTestEnv.AllyID);
            var profile = CharacterProfile.FromProgress(runtime.GetCharacterProgress(BattleFlowTestEnv.AllyID));
            env.CreateMinimalBattle();
            CharacterBuildApplier.InitializePanel(env.Ally, profile);
            Assert.That(env.Ally.GetEffectiveSkillLevel(2), Is.EqualTo(10), "挂初始化状态之前已经加载命座后的本体等级");
            CharacterBuildApplier.ApplyBuild(env.Ally, profile); CharacterBuildApplier.ApplyBuild(env.Ally, profile);
            Assert.That(env.Ally.SkillLevels, Is.EqualTo(inventory.CopyOwnLevels()));
            Assert.That(env.Ally.GetEffectiveSkillLevel(3), Is.EqualTo(11));
            Assert.That(runtime.GetCharacterProgress(BattleFlowTestEnv.AllyID).SkillLevels, Is.EqualTo(new[] { 5, 6, 7, 8 }));
            Assert.That(profile.SkillLevels, Is.EqualTo(new[] { 5, 6, 7, 8 }));
        }

        [TestCase(20, 0, 10)]
        [TestCase(20, 1, 13)]
        public void OwnTalentBonus_UsesActualAscension_AndFinalLevelIsClampedOnce(int level, int ascension, int expected)
        {
            using var env = new BattleFlowTestEnv();
            env.DataManager.TalentDict[BattleFlowTestEnv.AllyID] = new List<TalentData>
            { new TalentData { UnlockAfter = 20, Modifiers = "SkillLevelUp(0,3)" } };
            var character = new RoguelikeCharacterProgressData
            { CharacterID = BattleFlowTestEnv.AllyID, Level = level, Ascension = ascension, SkillLevels = new[] { 10, 1, 1, 1 } };
            Assert.That(character.GetSkillLevelInfo().GetOwnLevel(0), Is.EqualTo(expected));
            env.DataManager.ConstellationDict[BattleFlowTestEnv.AllyID] = new List<ConstellationData>
            { new ConstellationData { ConstellationIndex = 1, Modifiers = "SkillLevelUp(0,3)" } };
            character.ConstellationLevel = 1; character.Ascension = 1;
            var capped = character.GetSkillLevelInfo();
            Assert.That(capped.GetBaseLevel(0), Is.EqualTo(10)); Assert.That(capped.GetBonus(0), Is.EqualTo(6));
            Assert.That(capped.GetOwnLevel(0), Is.EqualTo(15));
        }

        [TestCase("SkillLevelUp(4,3)")]
        [TestCase("SkillLevelUp(2,no)")]
        [TestCase("SkillLevelUp(2,3,1)")]
        public void InvalidUnlockedBonus_FailsBeforeChangingTheBattleController(string command)
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            env.DataManager.ConstellationDict[BattleFlowTestEnv.AllyID] = new List<ConstellationData>
            { new ConstellationData { ConstellationIndex = 3, Modifiers = command } };
            var profile = new CharacterProfile { CharacterID = BattleFlowTestEnv.AllyID, Level = 1, ConstellationLevel = 3 };
            Assert.Throws<FormatException>(() => CharacterBuildApplier.InitializePanel(env.Ally, profile));
            Assert.That(env.Ally.GetEffectiveSkillLevel(2), Is.EqualTo(1));
            Assert.That(env.Ally.Entity.ConstellationLevel, Is.Zero);
        }

        private static void Levels(BattleFlowTestEnv env, string id, int type, string first, string fourth)
        {
            env.DataManager.SkillLevelDict[$"{BattleFlowTestEnv.AllyID}_{type}_{id}"] = new Dictionary<int, SkillLevelData>
            {
                [1] = new SkillLevelData { CharacterID = BattleFlowTestEnv.AllyID, SkillType = type, SkillLevel = 1, ParamID = id, Hits1 = first },
                [4] = new SkillLevelData { CharacterID = BattleFlowTestEnv.AllyID, SkillType = type, SkillLevel = 4, ParamID = id, Hits1 = fourth }
            };
        }

        [Test]
        public void UnifiedLevel_ReadsDirectStatusReferenceHealingShieldAndScalar_ByTalentOwnership()
        {
            using var env = new BattleFlowTestEnv(); Constellations(env);
            Levels(env, "SE_OwnLevelDamage", 2, "1*TotalATK,1", "2.75");
            Levels(env, "STE_OwnLevelDamage", 3, "1*TotalATK,1", "4.25");
            Levels(env, "STE_OwnLevelScalar", 3, "0.1", "0.42");
            Levels(env, "STE_OwnLevelHealing", 3, "0.1*TotalHP+10", "0.25*TotalHP+11");
            env.CreateMinimalBattle();
            CharacterBuildApplier.ApplyBuild(env.Ally, new CharacterProfile
            { CharacterID = BattleFlowTestEnv.AllyID, Level = 1, ConstellationLevel = 5 });
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            object Read(string method, params object[] args)
                => typeof(CharacterBattleController).GetMethod(method, flags).Invoke(env.Ally, args);
            var direct = (List<HitData>)Read("GetHitDataList",
                new SkillEffectData { SkillEffectID2 = "SE_OwnLevelDamage", DamageType = "Normal" }, 2);
            var status = (List<HitData>)Read("GetHitDataFromStatusEffectList",
                new StatusEffectData { StatusEffectID2 = "STE_OwnLevelDamage", DamageType = "Normal" });
            Assert.That(direct[0].Multiplier, Is.EqualTo(2.75f)); Assert.That(status[0].Multiplier, Is.EqualTo(4.25f));
            float reference = (float)Read("ResolveReferenceBaseValue", "%SE_OwnLevelDamage.Hits1.M*2,E", "SE_Other", 0, "Physical");
            Assert.That(reference, Is.EqualTo(550f).Within(.001f), "引用读取来源战技等级，不能借调用者普攻等级");
            Assert.That(env.Ally.TryResolveStatusLevelScalar(new StatusEffectData { StatusEffectID2 = "STE_OwnLevelScalar" }, out float scalar), Is.True);
            Assert.That(scalar, Is.EqualTo(.42f));
            var healing = (List<string>)Read("GetHealingSegments", "STE_OwnLevelHealing", null, 3);
            Assert.That(healing, Is.EqualTo(new[] { "0.25*TotalHP+11" }));
            Assert.That((float)Read("ResolveShieldAmount", null, "STE_OwnLevelHealing", 3, "None"), Is.EqualTo(261f));
            Assert.That(env.Ally.GetEffectiveSkillLevel(-1), Is.EqualTo(1));
            Assert.That(env.Ally.GetEffectiveSkillLevel(4), Is.EqualTo(1));
        }
    }
}
