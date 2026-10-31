using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class DionaScriptHooksTests
    {
        private const string S = "ST_Diona_", E = "STE_Diona_";
        private static void Main(BattleFlowTestEnv env, int index, string name, string type = "", string part = "")
        {
            env.DataManager.StatusMainDict[S + name] = new StatusMainData { StatusID = 4103100 + index,
                StatusID2 = S + name, StatusType = type, MultiplierPart = part, MaxCount = 1, MaxStack = 1, WhenMax = "0" };
        }
        private static StatusEffectData Effect(BattleFlowTestEnv env, int parent, int index, string name, string type, string value)
        {
            var effect = new StatusEffectData { StatusEffectID = (4103100 + parent) * 100 + index,
                StatusEffectID2 = E + name, EffectIndex = index, EffectType = type, Param1 = value, TargetType = "Self" };
            env.DataManager.StatusEffectDict[effect.StatusEffectID2] = effect;
            return effect;
        }
        private static StatusInstance Add(BattleFlowTestEnv env, string name, BattleEntity recipient = null)
            => (recipient ?? env.Ally.Entity).AddStatus(S + name, env.Ally.Entity, 3, 2, 0, env.DataManager.StatusMainDict[S + name]);
        private static void Setup(BattleFlowTestEnv env)
        {
            Main(env, 11, "Rules"); Main(env, 3, "Shield", "Shield");
            Main(env, 9, "C6Healing", "Buff", "BeHealedBonus"); Main(env, 10, "C6Mastery", "Buff", "EMBonus");
            Effect(env, 9, 1, "C6Healing", "Buff", ".3").ScriptHook = "Diona_LowHP()";
            Effect(env, 10, 1, "C6Mastery", "Buff", "200").ScriptHook = "Diona_HighHP()";
            var cost = Effect(env, 3, 2, "SwitchCost", "APCost", "-2");
            cost.Param2 = "SwitchOut"; cost.ScriptHook = "Diona_Shielded()";
            env.DataManager.TalentDict[1031] = new List<TalentData> { new TalentData { CharacterID = 1031, TalentIndex = 1, UnlockAfter = 0 } };
            env.ConfigureAllyAction(2, "SK_Skill_Diona", "SE_Skill_DionaPaws", "Cryo", ".4192*TotalATK,1", 30);
            var paws = env.DataManager.SkillEffectDict["SE_Skill_DionaPaws"];
            paws.ExecuteCount = 5; paws.TargetNumber = 1; paws.TargetConsecutive = 3;
            var energy = Effect(env, 11, 1, "PawEnergy", "GainEnergy", "2");
            energy.Element = "Cryo"; energy.Param2 = "Based"; energy.TargetType = "Allies"; energy.TargetSelect = "-1";
            env.DataManager.StatusActionDict[S + "Rules"] = new List<StatusActionData> { new StatusActionData
            { StatusID = 4103111, StatusID2 = S + "Rules", ActionType = "OnTrigger", Param1 = E + "PawEnergy",
                ScriptHook = "Diona_PawHit()", OutgoingHit = "Skill", MaxTimePerHit = 1 } };
            env.CreateMinimalBattle(); env.Ally.Entity.EntityID = 1031;
            env.CreateEnemy(2, 10000); env.Enemy.Entity.CurrentHP = env.Enemy.Entity.TotalHP = 10000;
            env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            env.Ally.Entity.CritRate = 0;
            env.Ally.Entity.CurrentEnergy = 0; env.Ally.Entity.MaxEnergy = 80;
        }

        [UnityTest] public IEnumerator FivePaws_EachRandomHitGeneratesEnergyOnce() => HookTestEnv.AwaitTask(FivePaws);
        private static async Task FivePaws()
        {
            using var env = new BattleFlowTestEnv(); Setup(env);
            var random = new SequenceBattleRandomSource(intValues: new[] { 0, 1, 0, 1, 0 }); BattleRandom.SetSource(random);
            var positions = new List<int>(); int gains = 0;
            using var damage = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, fact) =>
            { if (fact.Source.SourceEffectID == "SE_Skill_DionaPaws") positions.Add(fact.Subject.SlotPosition); }, 0);
            using var resource = BattleEventHub.Subscribe<ResourceEventData>(BattleEventKind.ResourceChanged, (_, fact) =>
            { if (fact.Subject == env.Ally.Entity && fact.Resource == "Energy" && fact.RequestedDelta > 0) gains++; }, 0);
            await BattleExecution.Run(() => env.Ally.ExecuteSkillByIDAsync("SK_Skill_Diona", 2, true));
            Assert.That(positions, Is.EqualTo(new[] { 1, 2, 1, 2, 1 }));
            Assert.That(gains, Is.EqualTo(5));
            Assert.That(random.IntCallCount, Is.EqualTo(5));
            Assert.That(random.FloatCallCount, Is.EqualTo(5), "每枚猫爪独立暴击抽取，不为日志重复抽取。");
        }

        [Test] public void SixthConstellation_UsesRecipientsOwnLiveHP_AtFiftyPercentBoundary()
        {
            using var env = new BattleFlowTestEnv(); Setup(env);
            var recipient = env.CreateAlly(2).Entity;
            env.Ally.Entity.CurrentHP = env.Ally.Entity.GetFinalHP();
            Add(env, "C6Healing", recipient); Add(env, "C6Mastery", recipient);
            recipient.CurrentHP = recipient.GetFinalHP() * .5f;
            float baseEM = recipient.TotalEM;
            Assert.That(recipient.GetFinalEM(), Is.EqualTo(baseEM));
            Assert.That(recipient.GetStatusHealingBonus("BeHealedBonus", "", "", -1, "", recipient), Is.EqualTo(.3f).Within(.0001f));
            recipient.CurrentHP += 1;
            Assert.That(recipient.GetFinalEM(), Is.EqualTo(baseEM + 200));
            Assert.That(recipient.GetStatusHealingBonus("BeHealedBonus", "", "", -1, "", recipient), Is.Zero);
            env.Ally.Entity.CurrentHP = 1;
            Assert.That(recipient.GetFinalEM(), Is.EqualTo(baseEM + 200), "来源生命变化不影响受益者门槛。");
        }

        [Test] public void BrokenPawShield_RemovesOnlyItsState_AndStopsOutgoingSwitchDiscount()
        {
            using var env = new BattleFlowTestEnv(); Setup(env);
            var recipient = env.CreateAlly(2).Entity;
            var shield = Add(env, "Shield", recipient);
            recipient.AddShield(100, "Cryo", 3, sourceStatus: shield);
            recipient.AddShield(1000, "None", 3);
            Assert.That(APCostResolver.ForSwitch(recipient, env.Ally.Entity, 5, false), Is.EqualTo(3));
            recipient.AbsorbDirectDamageWithShield(100);
            Assert.That(recipient.HasStatusInstance(shield), Is.False);
            Assert.That(recipient.Shields.Any(x => x.Value > 0), Is.True);
            Assert.That(APCostResolver.ForSwitch(recipient, env.Ally.Entity, 5, false), Is.EqualTo(5));
        }

        [UnityTest] public IEnumerator ShieldBonus_AppliesOnCreatorToAllPartyShields_AndOnlyToPawShield()
            => HookTestEnv.AwaitTask(ShieldBonus);
        private static async Task ShieldBonus()
        {
            using var env = new BattleFlowTestEnv(); Setup(env);
            var recipient = env.CreateAlly(2).Entity;
            Main(env, 12, "T1ShieldBonus", "Buff", "ShieldBonus");
            Main(env, 13, "C2ShieldBonus", "Buff", "ShieldBonus");
            foreach (var key in new[] { "T1ShieldBonus", "C2ShieldBonus" })
                env.DataManager.StatusMainDict[S + key].ApplyString = E + "CreateShield";
            Effect(env, 12, 1, "T1ShieldBonus", "Buff", ".75");
            Effect(env, 13, 1, "C2ShieldBonus", "Buff", ".15");
            var shield = Effect(env, 3, 1, "CreateShield", "Shield", "0.072*TotalHP+692.8");
            shield.Element = "Cryo"; shield.Duration = 3;
            env.DataManager.StatusActionDict[S + "Shield"] = new List<StatusActionData>
            { new StatusActionData { StatusID = 4103103, StatusID2 = S + "Shield", ActionType = "OnApply", Param1 = E + "CreateShield" } };
            Add(env, "T1ShieldBonus"); Add(env, "C2ShieldBonus");
            env.Ally.Entity.TotalHP = 1000; recipient.TotalHP = 5000;
            await BattleExecution.Run(async () =>
            {
                Add(env, "Shield", recipient);
                await BattleExecution.Flush();
            });
            Assert.That(recipient.Shields.Single().Value, Is.EqualTo((72 + 692.8f) * 1.9f).Within(.001f));
            Assert.That(env.Ally.Entity.GetStatusShieldBonus("ShieldBonus", "STE_OtherShield", "", "Cryo", recipient), Is.Zero);
            env.Ally.Entity.RemoveStatus(S + "T1ShieldBonus");
            Assert.That(recipient.Shields.Single().Value, Is.EqualTo(1453.12f).Within(.001f), "既有盾量不随来源增益改变");
        }

        [UnityTest] public IEnumerator FirstConstellation_NaturalEndOnly_ReplacementAndRemovalDoNotPay()
            => HookTestEnv.AwaitTask(FirstConstellation);
        private static async Task FirstConstellation()
        {
            using var env = new BattleFlowTestEnv(); Setup(env);
            env.Ally.Entity.ConstellationLevel = 1;
            Main(env, 4, "Mist");
            var energy = Effect(env, 4, 7, "C1Energy", "GainEnergy", "15"); energy.Param2 = "Flat";
            var actions = new List<StatusActionData> { new StatusActionData
            { StatusID = 4103104, StatusID2 = S + "Mist", ActionType = "OnEnd", Param1 = E + "C1Energy", ScriptHook = "Check(1031_C1)" } };
            env.DataManager.StatusActionDict[S + "Mist"] = actions;
            await BattleExecution.Run(async () =>
            {
                Add(env, "Mist");
                var replacement = Add(env, "Mist");
                env.Ally.Entity.RemoveStatusInstance(replacement, "Removed");
                await BattleExecution.Flush();
            });
            Assert.That(env.Ally.Entity.CurrentEnergy, Is.Zero);
            var natural = Add(env, "Mist");
            await BattleExecution.Run(async () =>
            {
                await env.Ally.ExecuteStatusActionsTask(natural, actions, env.Ally.Entity, "OnEnd");
                env.Ally.Entity.RemoveStatusInstance(natural, "Expired");
                await BattleExecution.Flush();
            });
            Assert.That(env.Ally.Entity.CurrentEnergy, Is.EqualTo(15));
            await BattleExecution.Run(async () =>
            {
                var death = Add(env, "Mist");
                env.Ally.Entity.RemoveStatusInstance(death, "SourceDeath");
                await BattleExecution.Flush();
            });
            Assert.That(env.Ally.Entity.CurrentEnergy, Is.EqualTo(15));
        }
    }
}
