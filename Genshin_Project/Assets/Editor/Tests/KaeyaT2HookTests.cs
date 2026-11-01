using System;
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class KaeyaT2HookTests
    {
        private static void ConfigureSkill(BattleFlowTestEnv env)
        {
            env.ConfigureAllyAction(2, "SK_Skill_Kaeya", "SE_Skill_Kaeya1", "Cryo", "0.1*TotalATK,2", 0);
            env.DataManager.CharacterSkillDict[BattleFlowTestEnv.AllyID]
                .Find(skill => skill.SkillID2 == "SK_Skill_Kaeya").SkillID = 101003;
            env.DataManager.SkillEffectDict["SE_Skill_Kaeya1"].SkillEffectID = 10100301;
            env.DataManager.SkillEffectDict["SE_Kaeya_T2Energy"] = new SkillEffectData
            {
                SkillEffectID = 10100302, SkillEffectID2 = "SE_Kaeya_T2Energy", EffectIndex = 1,
                EffectType = "GainEnergy", Param1 = "4", Param2 = "Flat", TargetType = "Self", ScriptHook = "Kaeya_T2"
            };
        }

        [TestCase("Hydro", false, 4)]
        [TestCase("Cryo", false, 0)]
        [TestCase(null, true, 0)]
        [TestCase(null, false, 0)]
        public void SkillRefund_RequiresFrozenReactionFromThisSkill(string aura, bool alreadyFrozen, int expectedEnergy)
        {
            using var env = new BattleFlowTestEnv();
            ConfigureSkill(env);
            env.CreateMinimalBattle();
            var owner = env.Ally.Entity; owner.EntityID = 1010;
            var target = env.Enemy.Entity;
            env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            owner.CurrentEnergy = 0;
            if (aura != null) target.ApplyAura(aura, 1f, owner.EntityID);
            if (alreadyFrozen)
                ReactionStateSystem.SetEntityState(target, ReactionType.Frozen, 2,
                    ReactionSourceSnapshot.Capture(new ReactionContext
                    {
                        SourceEntity = owner, Target = target, SourceKind = ReactionSourceKind.CharacterSkill,
                        SourceSkillID = "SK_Skill_Kaeya", SourceEffectID = "SE_Skill_Kaeya1", AttackElement = "Cryo", AttackAmount = 1f
                    }));
            var random = new SequenceBattleRandomSource(new[] { 0f }); BattleRandom.SetSource(random);
            env.Ally.ForcedTargetPositions.Add(1);

            BattleExecution.Run(() => env.Ally.ExecuteSkillByIDAsync("SK_Skill_Kaeya", 2, true)).GetAwaiter().GetResult();

            Assert.That(owner.CurrentEnergy, Is.EqualTo(expectedEnergy));
            Assert.That(random.FloatCallCount, Is.EqualTo(1));
            Assert.That(random.IntCallCount, Is.Zero);
            Assert.That(ScriptHookEvaluator.Evaluate("Kaeya_T2", owner), Is.False, "结束后不能借用上次战技记录");
        }

        [Test]
        public void FrozenFact_IsScopedToOwnerAndSkill_AndRestoredAfterNestedException()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            var owner = env.Ally.Entity; owner.EntityID = 1010;
            var target = env.Enemy.Entity;
            var skill = new SkillMainData { SkillID2 = "SK_Skill_Kaeya" };
            using (BattleSpecialRules.BeginSkillExecution(owner, skill))
            {
                target.ApplyAura("Hydro", 1f, owner.EntityID);
                ReactionResolver.Resolve(new ReactionContext
                {
                    SourceEntity = owner, Target = target, SourceKind = ReactionSourceKind.CharacterSkill,
                    SourceSkillID = skill.SkillID2, SourceEffectID = "SE_Skill_Kaeya1",
                    AttackElement = "Cryo", AttackAmount = 1f
                });
                Assert.That(ScriptHookEvaluator.Evaluate("Kaeya_T2", owner), Is.True);
                Assert.Throws<InvalidOperationException>(() =>
                {
                    using (BattleSpecialRules.BeginSkillExecution(owner, skill))
                    {
                        Assert.That(ScriptHookEvaluator.Evaluate("Kaeya_T2", owner), Is.False);
                        throw new InvalidOperationException("nested skill interrupted");
                    }
                });
                Assert.That(ScriptHookEvaluator.Evaluate("Kaeya_T2", owner), Is.True);
                using (BattleSpecialRules.BeginSkillExecution(owner, new SkillMainData { SkillID2 = "SK_Normal_Kaeya" }))
                    Assert.That(ScriptHookEvaluator.Evaluate("Kaeya_T2", owner), Is.False);
                Assert.That(ScriptHookEvaluator.Evaluate("Kaeya_T2", target), Is.False);
            }
            Assert.That(ScriptHookEvaluator.Evaluate("Kaeya_T2", owner), Is.False);
        }
    }
}
