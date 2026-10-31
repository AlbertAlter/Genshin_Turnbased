using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class GanyuScriptHooksTests
    {
        private const string S = "ST_Ganyu_";
        private static void Main(BattleFlowTestEnv env, int id, string key, string type = null)
            => env.DataManager.StatusMainDict[S + key] = new StatusMainData { StatusID = 4103500 + id,
                StatusID2 = S + key, StatusType = type, MaxCount = 1, MaxStack = 1, WhenMax = "0" };
        private static void Setup(BattleFlowTestEnv env)
        {
            Main(env, 2, "FrostCharge"); Main(env, 3, "ChargeTarget"); Main(env, 5, "Lotus");
            Main(env, 7, "Shower"); Main(env, 12, "C6Ready"); Main(env, 13, "C4", "Count");
            Main(env, 14, "C4Bonus", "Buff");
            var bonus = env.DataManager.StatusMainDict[S + "C4Bonus"];
            bonus.MaxStack = 0; bonus.MultiplierPart = "DMGBonus"; bonus.ScriptHook = "Ganyu_C4Target()";
            env.DataManager.StatusEffectDict["STE_Ganyu_C4Bind"] = new StatusEffectData
            { StatusEffectID = 410351302, StatusEffectID2 = "STE_Ganyu_C4Bind", EffectType = "BindStatus", Param1 = S + "C4Bonus", TargetType = "AllyTeam" };
            env.DataManager.StatusEffectDict["STE_Ganyu_C4Bonus"] = new StatusEffectData
            { StatusEffectID = 410351401, StatusEffectID2 = "STE_Ganyu_C4Bonus", EffectType = "Buff", Param1 = ".05", DamageHook = "Ganyu_C4Value()", TargetType = "Self" };
            env.ConfigureAllyAction(1, "SK_Heavy_Ganyu2", "SE_Heavy_Ganyu2Target", "", "", 50,
                effectType: "ApplyStatus", param1: S + "ChargeTarget", duration: 1, addInPhase: 1, targetType: "EnemyField");
            env.DataManager.CharacterSkillDict[BattleFlowTestEnv.AllyID].Last().ActionType = "";
            env.DataManager.SkillEffectDict["SE_Heavy_Ganyu2Charge"] = new SkillEffectData
            { SkillEffectID = 10350503, SkillEffectID2 = "SE_Heavy_Ganyu2Charge", EffectIndex = 3,
                EffectType = "ApplyStatus", Param1 = S + "FrostCharge", Duration = 1, AddInPhase = 1, TriggerPhase = 1, TargetType = "Self" };
            env.ConfigureAllyAction(1, "SK_Ganyu_FrostflakeAttack", "SE_Ganyu_FrostflakeAttackArrow", "Cryo", "2.56*TotalATK,0", 0);
            var arrow = env.DataManager.SkillEffectDict["SE_Ganyu_FrostflakeAttackArrow"];
            arrow.TargetNumber = 0; arrow.TargetType = "EnemyField"; arrow.TargetOverride = S + "ChargeTarget";
            env.DataManager.SkillEffectDict["SE_Ganyu_FrostflakeAttackBloom"] = new SkillEffectData
            { SkillEffectID = 10350702, SkillEffectID2 = "SE_Ganyu_FrostflakeAttackBloom", EffectIndex = 1,
                EffectType = "Damage", Element = "Cryo", DamageType = "Heavy", Param1 = "4.352*TotalATK,0",
                TargetType = "EnemyField", TargetOverride = "Ganyu_BloomTargets()" };
            foreach (var entry in new[] { ("Ready", "C6Ready"), ("End", "FrostCharge") })
                env.DataManager.SkillEffectDict["SE_Ganyu_FrostflakeAttack" + entry.Item1] = new SkillEffectData
                { SkillEffectID2 = "SE_Ganyu_FrostflakeAttack" + entry.Item1, EffectIndex = entry.Item1 == "Ready" ? 4 : 5,
                    EffectType = "RemoveStatus", Param1 = S + entry.Item2, TargetType = "Self" };
            env.DataManager.CharacterAttributesDict[BattleFlowTestEnv.AllyID].HeavyAttackID = "SK_Heavy_Ganyu2";
            env.DataManager.StatusActionDict[S + "FrostCharge"] = new List<StatusActionData>
            {
                new StatusActionData { StatusID2 = S + "FrostCharge", ActionType = "OnApply", Param1 = "SK_Ganyu_FrostflakeAttack", ScriptHook = "Ganyu_C6Ready()" },
                new StatusActionData { StatusID2 = S + "FrostCharge", ActionType = "OnItsTurn", Param1 = "SK_Ganyu_FrostflakeAttack" }
            };
            env.CreateMinimalBattle(); env.Ally.Entity.EntityID = 1035;
            env.CreateEnemy(2, 10000); env.Enemy.Entity.TotalHP = env.Enemy.Entity.CurrentHP = 10000;
            env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            env.Ally.Entity.TotalATK = 100; env.Ally.Entity.CritRate = 0;
            BattleRandom.SetSource(new SequenceBattleRandomSource());
        }
        private static StatusInstance Add(BattleFlowTestEnv env, string key, BattleEntity target = null, int duration = 5)
            => (target ?? env.Ally.Entity).AddStatus(S + key, env.Ally.Entity, duration, 2, 0, env.DataManager.StatusMainDict[S + key]);

        [UnityTest] public IEnumerator DelayedAndInstantShots_OneHeavyAction_NoSecondPayment()
            => HookTestEnv.AwaitTask(Shots);
        private static async Task Shots()
        {
            foreach (bool instant in new[] { false, true })
            {
                using var env = new BattleFlowTestEnv(); Setup(env);
                var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
                int starts = 0, ends = 0, paid = 0; var positions = new List<int>();
                using var a = BattleEventHub.Subscribe<ActionEventData>(BattleEventKind.ActionStarted, (_, f) => { if (f.Subject == env.Ally.Entity && f.ActionType == "Heavy") starts++; }, 0);
                using var b = BattleEventHub.Subscribe<ActionEventData>(BattleEventKind.ActionCompleted, (_, f) => { if (f.Subject == env.Ally.Entity && f.ActionType == "Heavy") ends++; }, 0);
                using var p = BattleEventHub.Subscribe<APSpentEventData>(BattleEventKind.APSpent, (_, f) => { if (f.Subject == env.Ally.Entity) paid += f.Amount; }, 0);
                using var d = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, f) =>
                { if (f.Source.SourceEffectID.StartsWith("SE_Ganyu_FrostflakeAttack")) positions.Add(f.Subject.SlotPosition); }, 0);
                if (instant) Add(env, "C6Ready");
                env.Ally.ForcedTargetPositions.Add(1);
                await BattleExecution.Run(() => env.Ally.ExecuteSkillByIDAsync("SK_Heavy_Ganyu2", 1, true));
                Assert.That(paid, Is.EqualTo(50));
                if (!instant)
                {
                    Assert.That(starts, Is.Zero); Assert.That(positions, Is.Empty);
                    var charge = env.Ally.Entity.GetStatus(S + "FrostCharge");
                    await BattleExecution.Run(() => env.Ally.ExecuteStatusActionsTask(charge,
                        env.DataManager.StatusActionDict[S + "FrostCharge"], env.Ally.Entity, "OnItsTurn"));
                }
                Assert.That(starts, Is.EqualTo(1)); Assert.That(ends, Is.EqualTo(1));
                Assert.That(paid, Is.EqualTo(50)); Assert.That(positions, Is.EqualTo(new[] { 1, 1, 2 }));
                Assert.That(random.FloatCallCount, Is.EqualTo(3)); Assert.That(random.IntCallCount, Is.Zero);
                Assert.That(env.Ally.Entity.HasStatus(S + "FrostCharge"), Is.False);
                Assert.That(env.Ally.Entity.HasStatus(S + "C6Ready"), Is.False);
                Assert.That(env.Manager.Field.EnemySlots.SelectMany(x => x.StatusList).Any(x => x.StatusID2 == S + "ChargeTarget"), Is.False);
            }
        }

        [Test] public void C4_TargetsKeepSeparateCounts_RecastRefreshesWithoutReset_ZeroDoesNotRemove()
        {
            using var env = new BattleFlowTestEnv(); Setup(env);
            var first = env.Enemy.Entity; var second = env.Manager.GetEntityByPosition(BattleSide.Enemy, 2);
            var count1 = Add(env, "C4", first); var count2 = Add(env, "C4", second);
            CountEffect.Execute(S + "C4", "3", env.Ally.Entity, new[] { first });
            float Bonus(BattleEntity t) => env.Ally.Entity.GetStatusDMGBonus("SE_Normal_Test", "", 0, "Cryo", t);
            using var query = DamageQueryContext.Enter("SE_Normal_Test", "Normal");
            Assert.That(Bonus(first), Is.EqualTo(.2f).Within(.0001f)); Assert.That(Bonus(second), Is.EqualTo(.05f).Within(.0001f));
            count1.RemainingPhaseCount = 1;
            foreach (var child in count1.BoundStatuses) child.RemainingPhaseCount = 1;
            Assert.That(Add(env, "C4", first), Is.SameAs(count1));
            Assert.That(count1.RuntimeIntValue, Is.EqualTo(4)); Assert.That(count1.RemainingPhaseCount, Is.EqualTo(5));
            Assert.That(count1.BoundStatuses.All(child => child.RemainingPhaseCount == 5), Is.True);
            CountEffect.Execute(S + "C4", "full", env.Ally.Entity, new[] { first });
            Assert.That(Bonus(first), Is.EqualTo(.25f).Within(.0001f));
            CountEffect.Execute(S + "C4", "clear", env.Ally.Entity, new[] { first });
            Assert.That(first.HasStatusInstance(count1), Is.True); Assert.That(count1.RuntimeIntValue, Is.Zero);
            first.RemoveStatusInstance(count1);
            Assert.That(Bonus(second), Is.EqualTo(.05f).Within(.0001f)); Assert.That(count2.IsActive, Is.True);
        }

        [Test] public void Lotus_RecastOnAnotherSlotReplacesOnlyOldLotus()
        {
            using var env = new BattleFlowTestEnv(); Setup(env);
            StatusInstance Lotus(int slot) => env.Manager.Field.GetSlot(BattleSide.Enemy, slot).AddStatus(S + "Lotus", env.Ally.Entity, 2, 2, 0, env.DataManager.StatusMainDict[S + "Lotus"]);
            var old = Lotus(1); var current = Lotus(5);
            Assert.That(old.IsActive, Is.False); Assert.That(current.IsActive, Is.True);
            Assert.That(env.Manager.Field.EnemySlots.SelectMany(x => x.StatusList).Count(x => x.StatusID2 == S + "Lotus"), Is.EqualTo(1));
        }
    }
}
