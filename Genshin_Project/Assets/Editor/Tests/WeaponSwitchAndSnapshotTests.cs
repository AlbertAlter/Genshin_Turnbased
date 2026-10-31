using System.Collections.Generic;
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    /// <summary>3020/3041/3044真实入口回归定义；按项目规则不自动运行Unity。</summary>
    public sealed class WeaponSwitchAndSnapshotTests
    {
        private static string Key(int id) => DataManager.WeaponStatusKey(id);
        private static string Eff(int id) => "STE_Weapon_" + id;
        private static void Main(BattleFlowTestEnv env, int id, string part = null)
            => env.DataManager.StatusMainDict[Key(id)] = new StatusMainData
            { StatusID = id, StatusID2 = Key(id), StatusType = "Buff", MultiplierPart = part, MaxStack = 1, WhenMax = "0" };
        private static StatusEffectData Effect(BattleFlowTestEnv env, int id, string type, string parameter)
        {
            var effect = new StatusEffectData { StatusEffectID = id, StatusEffectID2 = Eff(id), EffectType = type,
                Param1 = parameter, TargetType = "Self" };
            env.DataManager.StatusEffectDict[effect.StatusEffectID2] = effect; return effect;
        }
        private static StatusActionData Action(BattleFlowTestEnv env, int id, string type, string effects)
        {
            var action = new StatusActionData { StatusID = id, StatusID2 = Key(id), ActionType = type, Param1 = effects };
            if (!env.DataManager.StatusActionDict.TryGetValue(Key(id), out var actions))
                env.DataManager.StatusActionDict[Key(id)] = actions = new List<StatusActionData>();
            actions.Add(action); return action;
        }
        private static WeaponRuntimeContext Weapon(BattleEntity owner, int weapon, params float[] values)
        {
            var result = new WeaponRuntimeContext { Equipper = owner, WeaponID = weapon, Refinement = 1,
                SourceInstanceID = "SwitchSnapshot:" + weapon + ":" + owner.EntityID };
            for (int i = 0; i < values.Length; i++) result.ResolvedParams[i + 1] = values[i];
            return result;
        }
        private static StatusInstance Add(BattleFlowTestEnv env, BattleEntity owner, int id, WeaponRuntimeContext weapon)
            => owner.AddStatus(Key(id), owner, 0, 2, 0, env.DataManager.StatusMainDict[Key(id)], weapon);
        private static void Start(BattleFlowTestEnv env)
        {
            BattleRandom.SetSource(new SequenceBattleRandomSource());
            env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
        }
        private static void Next(BattleFlowTestEnv env)
        { env.Manager.EndAllyTurn(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction); }

        [Test, Timeout(7000)]
        public void ThrillingTales_OffFieldListener_ForcedSwitchDoesNotConsumeCooldown_ManualTargetsIncomingAndKeepsSource()
        {
            using var env = new BattleFlowTestEnv(); Main(env, 4302001); Main(env, 4302002, "ATKBonus");
            Effect(env, 430200201, "Buff", "Index(1)");
            var apply = Effect(env, 430200101, "ApplyStatus", Key(4302002));
            apply.TargetType = "Allies"; apply.TargetOverride = "Hook(4302001)"; apply.Duration = 3; apply.AddInPhase = 2;
            var action = Action(env, 4302001, "OnTrigger", Eff(430200101)); action.ScriptHook = "Hook(4302001)"; action.Cooldown = 5;
            env.CreateMinimalBattle(); var owner = env.CreateAlly(2).Entity; owner.EntityID = 990012;
            var third = env.CreateAlly(3).Entity; third.EntityID = 990013;
            var weapon = Weapon(owner, 3020, .24f); Add(env, owner, 4302001, weapon); Start(env);
            var reasons = new List<BattleSwitchReason>();
            using var observe = BattleEventHub.Subscribe<SwitchEventData>(BattleEventKind.SwitchResolved, (_, change) => reasons.Add(change.Reason), 0);
            Assert.That(env.Manager.TrySwitchActiveAlly(0), Is.False, "失败不应声明成功或占冷却");
            env.Ally.Entity.TakeDamage(2000f, DamageSourceInfo.CreateUnknown());
            Assert.That(env.Manager.TrySwitchActiveAllyWithAPCost(1), Is.True);
            Assert.That(reasons, Is.EqualTo(new[] { BattleSwitchReason.DeathForced }));
            Assert.That(owner.HasStatus(Key(4302002)), Is.False, "阵亡换人不触发");
            Assert.That(env.Manager.TrySwitchActiveAllyWithAPCost(2), Is.True);
            var buff = third.GetStatus(Key(4302002)); Assert.That(buff, Is.Not.Null);
            Assert.That(buff.Caster, Is.SameAs(owner)); Assert.That(buff.WeaponContext.SourceInstanceID, Is.EqualTo(weapon.SourceInstanceID));
            Assert.That(third.GetStatusATKBonus("", "", -1, ""), Is.EqualTo(.24f));
            Assert.That(env.Manager.TrySwitchActiveAlly(1), Is.True);
            Assert.That(owner.HasStatus(Key(4302002)), Is.False, "后续主动切人仍受同一5回合冷却");
            owner.TakeDamage(2000f, DamageSourceInfo.CreateUnknown());
            Assert.That(third.HasStatus(Key(4302002)), Is.False, "增益虽在队友身上，仍随原装备者死亡清理");
        }

        [Test, Timeout(7000)]
        public void ThrillingTales_AnyManualAllySwitchCanTrigger_WhileWearerStaysOffField()
        {
            using var env = new BattleFlowTestEnv(); Main(env, 4302001); Main(env, 4302002, "ATKBonus");
            Effect(env, 430200201, "Buff", "Index(1)");
            var apply = Effect(env, 430200101, "ApplyStatus", Key(4302002));
            apply.TargetType = "Allies"; apply.TargetOverride = "Hook(4302001)"; apply.Duration = 3; apply.AddInPhase = 2;
            var action = Action(env, 4302001, "OnTrigger", Eff(430200101)); action.ScriptHook = "Hook(4302001)"; action.Cooldown = 5;
            env.CreateMinimalBattle(); var owner = env.CreateAlly(2).Entity; owner.EntityID = 990012;
            var incoming = env.CreateAlly(3).Entity; incoming.EntityID = 990013;
            Add(env, owner, 4302001, Weapon(owner, 3020, .24f)); Start(env);
            Assert.That(env.Manager.TrySwitchActiveAlly(2), Is.True, "免费调试入口也明确声明Manual，不以AP金额猜原因");
            Assert.That(owner.CharacterCtrl.IsActive, Is.False);
            Assert.That(incoming.GetStatusATKBonus("", "", -1, ""), Is.EqualTo(.24f));
            Assert.That(owner.HasStatus(Key(4302002)), Is.False);
        }

        private static void Moonlight(BattleFlowTestEnv env)
        {
            Main(env, 4304101); Main(env, 4304102); Main(env, 4304103, "RechargeBonus"); Main(env, 4304104, "RechargeBonus");
            var initialize = Effect(env, 430410101, "ApplyStatus", Key(4304102)); initialize.AddInPhase = initialize.TriggerPhase = 1;
            Action(env, 4304101, "OnApply", Eff(430410101));
            var cycle = Action(env, 4304102, "OnItsTurn", Eff(430410201) + ";" + Eff(430410202)); cycle.Cooldown = 3;
            for (int i = 1; i <= 2; i++)
            {
                var apply = Effect(env, 430410200 + i, "ApplyStatus", Key(4304102 + i));
                apply.Duration = 3; apply.AddInPhase = 6; apply.DamageHook = "Hook(" + apply.StatusEffectID + ")";
                if (i == 2) { apply.TargetType = "Allies"; apply.TargetSelect = "-1"; apply.TargetOverride = "Hook(430410202)"; }
            }
            foreach (int id in new[] { 430410301, 430410401 })
            { var buff = Effect(env, id, "Buff", "Index(1)"); buff.DamageHook = "Hook(" + id + ")"; }
        }

        [Test, Timeout(7000)]
        public void Xiphos_SharesOneSnapshot_ThreeRoundCycle_DifferentOwnersStack_DeathOnlyRemovesItsSource()
        {
            using var env = new BattleFlowTestEnv(); Moonlight(env); env.CreateMinimalBattle();
            var first = env.Ally.Entity; var second = env.CreateAlly(2).Entity; second.EntityID = 990012;
            var recipient = env.CreateAlly(3).Entity; recipient.EntityID = 990013;
            first.TotalEM = 100f; second.TotalEM = 200f; recipient.TotalEM = 1000f;
            Add(env, first, 4304101, Weapon(first, 3041, .00036f));
            var other = Weapon(second, 3041, .00045f); other.Refinement = 2; Add(env, second, 4304101, other); Start(env);
            Assert.That(first.GetStatusRechargeBonus(), Is.EqualTo(.063f).Within(.000001f));
            Assert.That(second.GetStatusRechargeBonus(), Is.EqualTo(.1008f).Within(.000001f));
            Assert.That(recipient.GetStatusRechargeBonus(), Is.EqualTo(.0378f).Within(.000001f));
            Assert.That(recipient.GetStatusInstances(Key(4304104)), Has.Count.EqualTo(2), "不同来源不能被MaxStack=1互相覆盖");
            var old = first.GetStatus(Key(4304103)); first.TotalEM = 300f;
            for (int i = 0; i < 2; i++)
            {
                Next(env);
                Assert.That(first.GetStatusRechargeBonus(), Is.EqualTo(.063f).Within(.000001f), "周期之间不实时重读精通");
            }
            Next(env);
            Assert.That(first.GetStatusRechargeBonus(), Is.EqualTo(.135f).Within(.000001f));
            Assert.That(second.GetStatusRechargeBonus(), Is.EqualTo(.1224f).Within(.000001f));
            Assert.That(recipient.GetStatusRechargeBonus(), Is.EqualTo(.0594f).Within(.000001f));
            Assert.That(old.IsActive, Is.False); Assert.That(first.GetStatusInstances(Key(4304103)), Has.Count.EqualTo(1));
            Assert.That(recipient.GetStatusInstances(Key(4304104)), Has.Count.EqualTo(2));
            first.TakeDamage(2000f, DamageSourceInfo.CreateUnknown());
            Assert.That(recipient.GetStatusRechargeBonus(), Is.EqualTo(.027f).Within(.000001f));
            Assert.That(recipient.GetStatusInstances(Key(4304104)), Has.Count.EqualTo(1));
        }

        [Test, Timeout(7000)]
        public void Finale_TracksAllActualReductions_UntilZero_ResetsEachSettlement_CapsAndKeepsFlatATKSnapshot()
        {
            using var env = new BattleFlowTestEnv();
            env.ConfigureAllyAction(2, "SK_Finale", "SE_Finale", "Physical", ".01*TotalATK,0", 0);
            Main(env, 4304401); Main(env, 4304402, "ATKBonus"); Main(env, 4304403); Main(env, 4304404, "ATKBonusFlat");
            var skill = Action(env, 4304401, "OnTrigger", Eff(430440101) + ";" + Eff(430440102)); skill.OnAction = "Skill"; skill.Cooldown = 3;
            Action(env, 4304403, "OnTrigger", Eff(430440301)).ScriptHook = "Hook(4304403)";
            var percent = Effect(env, 430440101, "ApplyStatus", Key(4304402)); percent.Duration = 4; percent.AddInPhase = 2;
            Effect(env, 430440102, "BondOfLife", "0.25*TotalHP"); Effect(env, 430440201, "Buff", "Index(1)");
            var apply = Effect(env, 430440301, "ApplyStatus", Key(4304404)); apply.Duration = 4; apply.AddInPhase = 2; apply.DamageHook = "Hook(430440301)";
            Effect(env, 430440401, "Buff", "Index(2)").DamageHook = "Hook(430440401)";
            env.CreateMinimalBattle(); var owner = env.Ally.Entity; owner.CurrentHP = 500f;
            var weapon = Weapon(owner, 3044, .12f, .024f, 150f); Add(env, owner, 4304401, weapon); Add(env, owner, 4304403, weapon); Start(env);
            Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
            Assert.That(owner.GetStatusATKBonus("", "", -1, ""), Is.EqualTo(.12f));
            Assert.That(BondOfLifeStatusHandler.GetValue(owner), Is.EqualTo(250f));
            BondOfLifeStatusHandler.ReduceValue(owner, 50f); owner.Heal(80f, owner);
            Assert.That(owner.CurrentHP, Is.EqualTo(500f)); Assert.That(BondOfLifeStatusHandler.GetValue(owner), Is.EqualTo(120f));
            Assert.That(owner.HasStatus(Key(4304404)), Is.False, "部分抵扣不发放奖励");
            BondOfLifeStatusHandler.AddValue(owner, 30f); BondOfLifeStatusHandler.ClearValue(owner);
            var first = owner.GetStatus(Key(4304404)); Assert.That(first, Is.Not.Null);
            Assert.That(owner.GetStatusATKBonusFlat("", "", -1, ""), Is.EqualTo(6.72f).Within(.00001f), "C=50+80+150，不是只取最后150");
            Assert.That(env.Ally.GetFinalATK("SE_Finale", 2, "Physical"), Is.EqualTo(118.72f).Within(.0001f), "100*1.12+6.72，固定项不能再乘1.12");
            BondOfLifeStatusHandler.AddValue(owner, 25000f); BondOfLifeStatusHandler.ClearValue(owner);
            Assert.That(first.IsActive, Is.False);
            Assert.That(owner.GetStatusATKBonusFlat("", "", -1, ""), Is.EqualTo(150f));
            BondOfLifeStatusHandler.AddValue(owner, 10f); BondOfLifeStatusHandler.ReduceValue(owner, 4f); BondOfLifeStatusHandler.ClearValue(owner);
            Assert.That(owner.GetStatusATKBonusFlat("", "", -1, ""), Is.EqualTo(.24f).Within(.00001f), "本轮累计已重置，不携带上轮消契量");
            Assert.That(owner.GetStatusInstances(Key(4304404)), Has.Count.EqualTo(1));
        }
    }
}
