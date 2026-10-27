using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace GenshinTurnBased.Tests.EditMode
{
    /// <summary>养成记录到实际角色面板的桥接；内存快照，不访问用户存档或测试配表文件。</summary>
    public sealed class CharacterProgressBattleBridgeTests
    {
        [Test]
        public void ProgressQuery_ReturnsLatestIndependentRecord_AndMissingCharacterIsNotInvented()
        {
            var runtime = Runtime();
            Assert.That(runtime.TryGetCharacterProgress(999999, out var missing), Is.False);
            Assert.That(missing, Is.Null);
            Assert.Throws<KeyNotFoundException>(() => runtime.GetCharacterProgress(999999));
            var first = runtime.GetCharacterProgress(BattleFlowTestEnv.AllyID);
            first.Level = 90; first.Ascension = 6; first.SkillLevels[0] = 15;
            var unchanged = runtime.GetCharacterProgress(BattleFlowTestEnv.AllyID);
            Assert.That(unchanged.Level, Is.EqualTo(1)); Assert.That(unchanged.Ascension, Is.Zero);
            Assert.That(unchanged.SkillLevels[0], Is.EqualTo(1));
            runtime.UpdateCharacterProgress(BattleFlowTestEnv.AllyID, 40, 17, 2, 3, new[] { 2, 3, 4, 5 });
            Assert.That(runtime.TryGetCharacterProgress(BattleFlowTestEnv.AllyID, out var latest), Is.True);
            Assert.That(latest.Level, Is.EqualTo(40)); Assert.That(latest.Ascension, Is.EqualTo(2));
            Assert.That(latest.Experience, Is.EqualTo(17));
            Assert.That(latest.SkillLevels, Is.EqualTo(new[] { 2, 3, 4, 5 }));
        }

        [TestCase(20, 0, 1000f, 100f)]
        [TestCase(20, 1, 1100f, 120f)]
        [TestCase(40, 1, 1100f, 120f)]
        [TestCase(40, 2, 1300f, 160f)]
        public void StageEntry_QueriesSavedLevelAndIntegerAscension_InsteadOfHandFilledStageValues(
            int level, int ascension, float hp, float attack)
        {
            using var env = new BattleFlowTestEnv();
            env.DataManager.CharacterAscensionDict[BattleFlowTestEnv.AllyID] = new List<CharacterAscensionData>
            { new CharacterAscensionData { AscensionLevel = 1 },
              new CharacterAscensionData { AscensionLevel = 20, BaseHPFlat = 100f, ATKBonus = .2f },
              new CharacterAscensionData { AscensionLevel = 40, BaseHPFlat = 200f, ATKBonus = .4f },
              new CharacterAscensionData { AscensionLevel = 80, BaseHPFlat = 999f, ATKBonus = 9f } };
            var runtime = Runtime();
            runtime.UpdateCharacterProgress(BattleFlowTestEnv.AllyID, level, 17, ascension, 2, new[] { 2, 3, 4, 5 });
            var config = new StageSetupData();
            config.Allies.Add(new StageAllySetup { CharacterID = BattleFlowTestEnv.AllyID, Level = 90,
                IsAscended = true, Constellation = 6 });
            config.Enemies.Add(new StageEnemySetup { EnemyID = BattleFlowTestEnv.EnemyID, Level = 1, Slot = 1 });
            var objects = new List<GameObject>(); var runner = new GameObject("CharacterProgressBattleBridgeTest");
            try
            {
                runner.AddComponent<BattleTester>().ReloadStage(config, runtime);
                var actor = env.Manager.Allies[0];
                Assert.That(actor.Level, Is.EqualTo(level)); Assert.That(actor.Ascension, Is.EqualTo(ascension));
                Assert.That(actor.Entity.ConstellationLevel, Is.EqualTo(2));
                Assert.That(actor.SkillLevels, Is.EqualTo(new[] { 2, 3, 4, 5 }));
                Assert.That(actor.Entity.GetFinalHP(), Is.EqualTo(hp).Within(.001f));
                Assert.That(actor.Entity.CurrentHP, Is.EqualTo(hp).Within(.001f));
                Assert.That(actor.Entity.GetFinalATK("", "", -1, ""), Is.EqualTo(attack).Within(.001f));
                Assert.That(BattleAttributeInitialization.IsComplete(actor.Entity), Is.True);
                Assert.That(runtime.GetCharacterProgress(BattleFlowTestEnv.AllyID).Experience, Is.EqualTo(17));
                Assert.That(config.Allies[0].Level, Is.EqualTo(90), "读取养成不能篡改测试配置或存档");
            }
            finally
            {
                foreach (var actor in env.Manager.Allies) if (actor != null) objects.Add(actor.gameObject);
                foreach (var enemy in env.Manager.Enemies) if (enemy != null) objects.Add(enemy.gameObject);
                env.Manager.ResetBattle();
                foreach (var actor in objects) if (actor != null) Object.DestroyImmediate(actor);
                Object.DestroyImmediate(runner);
            }
        }

        [Test]
        public void MissingFormalRecord_FailsBeforeReplacingExistingRoster()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            var original = env.Ally;
            var empty = new RoguelikeChapterRuntime(new RoguelikeProgressSnapshot { CurrentChapterId = 1 });
            var config = new StageSetupData();
            config.Allies.Add(new StageAllySetup { CharacterID = BattleFlowTestEnv.AllyID, Level = 90 });
            var runner = new GameObject("MissingCharacterProgressTest");
            try
            {
                Assert.Throws<KeyNotFoundException>(() => runner.AddComponent<BattleTester>().ReloadStage(config, empty));
                Assert.That(env.Manager.Allies, Has.Count.EqualTo(1)); Assert.That(env.Manager.Allies[0], Is.SameAs(original));
            }
            finally { Object.DestroyImmediate(runner); }
        }

        [Test]
        public void PreparedBuildProfile_RetainsExactStage_ClonesSkillsAndWeapon_AndConstructsPanel()
        {
            using var env = new BattleFlowTestEnv();
            env.DataManager.CharacterAscensionDict[BattleFlowTestEnv.AllyID] = new List<CharacterAscensionData>
            { new CharacterAscensionData { AscensionLevel = 20, BaseHPFlat = 100f },
              new CharacterAscensionData { AscensionLevel = 40, BaseHPFlat = 200f } };
            var runtime = Runtime(); runtime.UpdateCharacterProgress(BattleFlowTestEnv.AllyID, 40, 0, 1, 0, new[] { 2, 3, 4, 5 });
            var build = new RoguelikeBattleAllyBuild { Character = runtime.GetCharacterProgress(BattleFlowTestEnv.AllyID),
                Weapon = new RoguelikeWeaponInstanceData { Weapon = new WeaponLoadout { WeaponID = 3001, Level = 20, Ascension = 1 } } };
            var profile = build.CreateCharacterProfile();
            Assert.That(profile.Ascension, Is.EqualTo(1)); Assert.That(profile.IsAscended, Is.False);
            profile.SkillLevels[0] = 9; profile.Weapon.Level = 30;
            Assert.That(build.Character.SkillLevels[0], Is.EqualTo(2)); Assert.That(build.Weapon.Weapon.Level, Is.EqualTo(20));
            env.CreateMinimalBattle(); CharacterBuildApplier.InitializePanel(env.Ally, profile);
            Assert.That(env.Ally.Level, Is.EqualTo(40)); Assert.That(env.Ally.Ascension, Is.EqualTo(1));
            Assert.That(env.Ally.Entity.GetFinalHP(), Is.EqualTo(1100f), "40级阶段1只含20级行，不能把>0当作40级也已突破");
            Assert.Throws<System.ArgumentOutOfRangeException>(() => env.Ally.Init(BattleFlowTestEnv.AllyID, 40, 7));
            Assert.That(env.Ally.Ascension, Is.EqualTo(1), "无效输入不能先覆盖已构筑身份");
        }

        [TestCase(0, 1, 0, true)]
        [TestCase(20, 19, 0, false)]
        [TestCase(20, 20, 0, false)]
        [TestCase(20, 20, 1, true)]
        [TestCase(20, 21, 0, false)]
        [TestCase(20, 40, 1, true)]
        [TestCase(60, 60, 3, false)]
        [TestCase(60, 60, 4, true)]
        [TestCase(60, 61, 3, false)]
        [TestCase(60, 90, 1, false)]
        [TestCase(60, 90, 6, true)]
        public void TalentUnlock_RequiresTheCompletedThresholdStage(
            int threshold, int level, int ascension, bool expected)
            => Assert.That(CharacterAscensionRules.IsTalentUnlocked(threshold, level, ascension), Is.EqualTo(expected));

        [TestCase(20, 0, false, false)]
        [TestCase(20, 1, true, false)]
        [TestCase(60, 3, true, false)]
        [TestCase(60, 4, true, true)]
        [TestCase(61, 3, true, false)]
        [TestCase(90, 1, true, false)]
        [TestCase(90, 6, true, true)]
        public void TalentBuildAndCheck_UseTheSameActualAscensionRecord(
            int level, int ascension, bool first, bool second)
        {
            using var env = new BattleFlowTestEnv();
            env.ConfigureStatus("ST_Talent_First"); env.ConfigureStatus("ST_Talent_Second");
            env.ConfigureStatus("ST_Talent_Default");
            env.DataManager.TalentDict[BattleFlowTestEnv.AllyID] = new List<TalentData>
            {
                new TalentData { CharacterID = BattleFlowTestEnv.AllyID, TalentIndex = 1,
                    UnlockAfter = 20, Modifiers = "InitiateStatus(ST_Talent_First)" },
                new TalentData { CharacterID = BattleFlowTestEnv.AllyID, TalentIndex = 2,
                    UnlockAfter = 60, Modifiers = "InitiateStatus(ST_Talent_Second)" },
                new TalentData { CharacterID = BattleFlowTestEnv.AllyID, TalentIndex = 3,
                    UnlockAfter = 0, Modifiers = "InitiateStatus(ST_Talent_Default)" }
            };
            env.CreateMinimalBattle();
            CharacterBuildApplier.ApplyBuild(env.Ally, new CharacterProfile
            { CharacterID = BattleFlowTestEnv.AllyID, Level = level, Ascension = ascension });
            Assert.That(env.Ally.Entity.HasStatus("ST_Talent_First"), Is.EqualTo(first));
            Assert.That(env.Ally.Entity.HasStatus("ST_Talent_Second"), Is.EqualTo(second));
            Assert.That(env.Ally.Entity.HasStatus("ST_Talent_Default"), Is.True);
            Assert.That(ScriptHookEvaluator.Evaluate($"Check({BattleFlowTestEnv.AllyID}_T1)", env.Ally.Entity), Is.EqualTo(first));
            Assert.That(ScriptHookEvaluator.Evaluate($"Check({BattleFlowTestEnv.AllyID}_T2)", env.Ally.Entity), Is.EqualTo(second));
            Assert.That(ScriptHookEvaluator.Evaluate($"Check({BattleFlowTestEnv.AllyID}_T3)", env.Ally.Entity), Is.True);
        }

        [TestCase(false, 0, false)]
        [TestCase(true, 1, true)]
        public void LegacyTalentBuild_AtThresholdRequiresTheAscendedFlag(bool flag, int stage, bool unlocked)
        {
            using var env = new BattleFlowTestEnv();
            env.ConfigureStatus("ST_Legacy_Talent");
            env.DataManager.TalentDict[BattleFlowTestEnv.AllyID] = new List<TalentData>
            { new TalentData { CharacterID = BattleFlowTestEnv.AllyID, TalentIndex = 1,
                UnlockAfter = 20, Modifiers = "InitiateStatus(ST_Legacy_Talent)" } };
            env.CreateMinimalBattle();
            CharacterBuildApplier.ApplyBuild(env.Ally, new CharacterProfile
            { CharacterID = BattleFlowTestEnv.AllyID, Level = 20, Ascension = -1, IsAscended = flag });
            Assert.That(env.Ally.Ascension, Is.EqualTo(stage));
            Assert.That(env.Ally.Entity.HasStatus("ST_Legacy_Talent"), Is.EqualTo(unlocked));
            Assert.That(ScriptHookEvaluator.Evaluate($"Check({BattleFlowTestEnv.AllyID}_T1)", env.Ally.Entity), Is.EqualTo(unlocked));
        }

        private static RoguelikeChapterRuntime Runtime()
        {
            var runtime = new RoguelikeChapterRuntime(new RoguelikeProgressSnapshot { CurrentChapterId = 1 });
            runtime.AddCharacter(BattleFlowTestEnv.AllyID, 1000f, 60f); return runtime;
        }
    }
}
