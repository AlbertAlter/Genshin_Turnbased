using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class CooldownEffectTests
    {
        [TestCase("+2", 2)]
        [TestCase("2", 2)]
        [TestCase("-2", -2)]
        [TestCase("0", 0)]
        [TestCase("-2147483648", int.MinValue)]
        public void SignedDelta_IsAccepted(string text, int expected)
        {
            Assert.That(CooldownParameters.TryParseAmount(text, out int actual), Is.True);
            Assert.That(actual, Is.EqualTo(expected));
            Assert.DoesNotThrow(() => TableValidator.ValidateCooldown("test", 1, "Skill", text));
        }

        [TestCase("")]
        [TestCase("1.5")]
        [TestCase("+n")]
        [TestCase("2147483648")]
        public void UndefinedOrOutOfRangeDelta_IsRejected(string text)
        {
            Assert.That(CooldownParameters.TryParseAmount(text, out _), Is.False);
            Assert.Throws<TableValidationException>(() => TableValidator.ValidateCooldown("test", 1, "Skill", text));
        }

        private static void Setup(BattleFlowTestEnv e, bool charges = false)
        {
            e.ConfigureAllyAction(2, "SK_Skill_CooldownTest", "SE_Skill_CooldownTest", "Physical", "0.1*TotalATK,0", 0, 3);
            if (charges) e.DataManager.CharacterSkillDict[BattleFlowTestEnv.AllyID].Last().MaxCharge = 2;
            e.ConfigureAllyAction(0, "SK_Normal_CooldownTest", "SE_Normal_CooldownTest", "", "", 0,
                effectType: "Cooldown", param1: "Skill", targetType: "Self");
            e.DataManager.SkillEffectDict["SE_Normal_CooldownTest"].Param2 = "+2";
            e.ConfigureStatus("ST_CooldownTest", actionType: "OnApply", actionEffectID2: "STE_CooldownTest");
            e.DataManager.StatusEffectDict["STE_CooldownTest"] = new StatusEffectData
            {
                StatusEffectID = 990901, StatusEffectID2 = "STE_CooldownTest", EffectType = "Cooldown",
                Param1 = "Skill", Param2 = "-1", TargetType = "Self"
            };
            e.CreateMinimalBattle();
            e.Ally.Entity.CritRate = 0;
            BattleRandom.SetSource(new SequenceBattleRandomSource());
            e.Manager.StartBattle();
            e.Flow.AdvanceToPhase(e.Manager, TurnPhase.AllyAction);
        }

        private static Task Cast(BattleFlowTestEnv e, string kind, int type)
            => BattleExecution.Run(() => e.Ally.ExecuteSkillByIDAsync("SK_" + kind + "_CooldownTest", type, true));

        [UnityTest]
        public IEnumerator SkillAndStatusEffects_UseSignedDelta_WithoutReapplyingCastModifiers()
            => HookTestEnv.AwaitTask(EffectsAsync);

        private static async Task EffectsAsync()
        {
            using var e = new BattleFlowTestEnv(); Setup(e);
            int starts = 0;
            // Editor是独立程序集；测试监听不扩大运行时internal接口，且结束后必须解除。
            var modifiers = (System.Collections.Generic.List<System.Func<BattleEntity, SkillMainData, int, int>>)
                typeof(BattleSpecialRules).GetField("CastCooldownModifiers", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).GetValue(null);
            System.Func<BattleEntity, SkillMainData, int, int> observer = (actor, skill, rounds) => { if (actor == e.Ally.Entity) starts++; return rounds; };
            modifiers.Add(observer);
            try
            {
            await Cast(e, "Skill", 2);
            Assert.That(e.Ally.GetCooldownRemaining(2), Is.EqualTo(3));
            await Cast(e, "Normal", 0);
            Assert.That(e.Ally.GetCooldownRemaining(2), Is.EqualTo(5));
            await BattleExecution.Run(async () =>
            {
                e.Ally.Entity.AddStatus("ST_CooldownTest", e.Ally.Entity, 1, 2, 0,
                    e.DataManager.StatusMainDict["ST_CooldownTest"]);
                await BattleExecution.Flush();
            });
            Assert.That(e.Ally.GetCooldownRemaining(2), Is.EqualTo(4));
            Assert.That(starts, Is.EqualTo(1), "调整当前冷却不再进入施放冷却修正。");
            Assert.That(e.Ally.ChangeSkillCooldown("Skill", int.MaxValue), Is.False);
            Assert.That(e.Ally.GetCooldownRemaining(2), Is.EqualTo(4));
            Assert.That(e.Ally.ChangeSkillCooldown("Skill", int.MinValue), Is.True);
            Assert.That(e.Ally.GetCooldownRemaining(2), Is.Zero);
            Assert.That(e.Ally.ChangeSkillCooldown("Skill", -1), Is.True);
            Assert.That(e.Ally.GetCooldownRemaining(2), Is.Zero);
            Assert.That(e.Ally.ChangeSkillCooldown("Skill", 2), Is.True);
            Assert.That(e.Ally.GetCooldownRemaining(2), Is.EqualTo(2), "普通技能允许从0增加冷却。");
            Assert.That(starts, Is.EqualTo(1), "从0增加也不重新进入施放冷却修正。");
            }
            finally { modifiers.Remove(observer); }
        }

        [UnityTest]
        public IEnumerator ExcessReduction_RestoresOnlyOneCharge_AndNextCycleStartsFull()
            => HookTestEnv.AwaitTask(ChargesAsync);

        private static async Task ChargesAsync()
        {
            using var e = new BattleFlowTestEnv(); Setup(e, true);
            Assert.That(e.Ally.ChangeSkillCooldown("Skill", 1), Is.True);
            Assert.That(e.Ally.GetCooldownRemaining(2), Is.EqualTo(1));
            Assert.That(e.Ally.GetBoundSkillCurrentCharge(2), Is.EqualTo(1), "满次数时只将最后一次转回充能。");
            Assert.That(e.Ally.ChangeSkillCooldown("Skill", 1), Is.True);
            Assert.That(e.Ally.GetCooldownRemaining(2), Is.EqualTo(2));
            Assert.That(e.Ally.GetBoundSkillCurrentCharge(2), Is.EqualTo(1), "继续增加当前充能不能再次扣次数。");
            e.Ally.ResetSkillCooldown("Skill");
            Assert.That(e.Ally.GetBoundSkillCurrentCharge(2), Is.EqualTo(2));
            await Cast(e, "Skill", 2);
            await Cast(e, "Skill", 2);
            Assert.That(e.Ally.GetBoundSkillCurrentCharge(2), Is.Zero);
            Assert.That(e.Ally.ChangeSkillCooldown("Skill", 2), Is.True);
            Assert.That(e.Ally.GetCooldownRemaining(2), Is.EqualTo(5));
            Assert.That(e.Ally.GetBoundSkillCurrentCharge(2), Is.Zero);
            Assert.That(e.Ally.ChangeSkillCooldown("Skill", -100), Is.True);
            Assert.That(e.Ally.GetBoundSkillCurrentCharge(2), Is.EqualTo(1));
            Assert.That(e.Ally.GetCooldownRemaining(2), Is.EqualTo(3));
            Assert.That(e.Ally.ChangeSkillCooldown("Skill", 1), Is.True);
            Assert.That(e.Ally.GetCooldownRemaining(2), Is.EqualTo(4), "延长的是第二轮充能。");
            Assert.That(e.Ally.GetBoundSkillCurrentCharge(2), Is.EqualTo(1), "第一轮恢复的次数仍可使用。");
            e.Ally.ResetSkillCooldown("Skill");
            Assert.That(e.Ally.GetBoundSkillCurrentCharge(2), Is.EqualTo(2));
            Assert.That(e.Ally.ChangeSkillCooldown("Skill", 1), Is.True);
            Assert.That(e.Ally.GetCooldownRemaining(2), Is.EqualTo(1));
            Assert.That(e.Ally.GetBoundSkillCurrentCharge(2), Is.EqualTo(1));
            Assert.That(e.Ally.ChangeSkillCooldown("Skill", -1), Is.True);
            Assert.That(e.Ally.GetCooldownRemaining(2), Is.Zero);
            Assert.That(e.Ally.GetBoundSkillCurrentCharge(2), Is.EqualTo(2), "这轮完成后恢复最后一次。");
        }
    }
}
