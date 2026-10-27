using System.Collections.Generic;
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    /// <summary>只编写未运行：从真实付款和完整技能入口检查，不把单hit等同于技能结束。</summary>
    public sealed class WeaponQualificationRegressionTests
    {
        private const string ID = "ST_Weapon_4310201";
        private static StatusInstance Equip(BattleFlowTestEnv env)
        {
            var main = new StatusMainData { StatusID = 4310201, StatusID2 = ID,
                StatusName = "PlumageTest", StatusType = "Count", MultiplierPart2 = "DMGBonus",
                ScriptHook = "Hook(4310201)", ApplyDamageType = "Heavy", MaxCount = 1, MaxStack = 1, WhenMax = "0" };
            env.DataManager.StatusMainDict[ID] = main;
            env.DataManager.StatusEffectDict["STE_Weapon_431020101"] = new StatusEffectData
            { StatusEffectID = 431020101, StatusEffectID2 = "STE_Weapon_431020101", EffectType = "Buff",
                Param1 = "Index(1)*Count(" + ID + ")" };
            env.DataManager.StatusEffectDict["STE_Weapon_431020102"] = new StatusEffectData
            { StatusEffectID = 431020102, StatusEffectID2 = "STE_Weapon_431020102", EffectType = "ApplyStatus",
                Param1 = ID, Duration = 0, AddInPhase = 2, TargetType = "Self" };
            env.DataManager.StatusActionDict[ID] = new List<StatusActionData>
            { new StatusActionData { StatusID2 = ID, ActionType = "OnTrigger", APUsed = "All",
                Param1 = "STE_Weapon_431020102" } };
            var owner = env.Ally.Entity;
            var weapon = new WeaponRuntimeContext { Equipper = owner, WeaponID = 3102, Refinement = 1,
                SourceInstanceID = "PlumageTest:3102" };
            weapon.ResolvedParams[1] = .06f;
            return owner.AddStatus(ID, owner, 0, 2, 0, main, weapon);
        }
        private static SequenceBattleRandomSource Start(BattleFlowTestEnv env)
        {
            env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            return random;
        }

        [Test]
        [Timeout(7000)]
        public void Plumage_UsesCurrentHeavyPayment_AllHitsAndEffectsFinishBeforeClear_AndKeepsRemainder()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.ConfigureAllyAction(0, "SK_Normal_Plumage", "SE_Normal_Plumage", "Physical", ".1*TotalATK,0", 8);
                env.ConfigureAllyAction(1, "SK_Heavy_Plumage", "SE_Heavy_Plumage", "Physical", ".1*TotalATK,0", 25);
                env.DataManager.SkillLevelDict[$"{BattleFlowTestEnv.AllyID}_1"][1].Hits2 = ".1*TotalATK,0";
                env.DataManager.SkillEffectDict["SE_Heavy_Plumage_Second"] = new SkillEffectData
                { SkillEffectID2 = "SE_Heavy_Plumage_Second", EffectIndex = 1, EffectType = "Damage",
                    DamageType = "Heavy", Element = "Physical", Param1 = ".1*TotalATK,0", TargetType = "Enemy",
                    TargetNumber = 1, TargetConsecutive = 1, TargetConsecutiveSet = true };
                env.CreateMinimalBattle(); env.CreateEnemy(2); var random = Start(env);
                var owner = env.Ally.Entity; var count = Equip(env);
                Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True);
                Assert.That(count.RuntimeValue, Is.Zero);
                var values = new List<float>(); var bonuses = new List<float>(); var effects = new HashSet<string>();
                using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, hit) =>
                {
                    if (hit.Source.SourceSkillID != "SK_Heavy_Plumage") return;
                    values.Add(count.RuntimeValue); effects.Add(hit.Source.SourceEffectID);
                    bonuses.Add(owner.GetStatusDMGBonus(hit.Source.SourceEffectID, hit.Source.SourceSkillID, 1, "Physical", hit.Subject));
                }, 0);
                env.Ally.ForcedTargetPositions.AddRange(new[] { 1, 2 });
                Assert.That(env.Manager.UseHeavyAttackBySlot(0), Is.True);
                Assert.That(values, Has.Count.EqualTo(6));
                Assert.That(effects, Has.Count.EqualTo(2), "不能只支持该技能的第一条Damage效果");
                Assert.That(values, Is.All.EqualTo(3f), "8+25AP得到3枚，全部hit/目标结清前不能清空");
                Assert.That(bonuses, Is.All.EqualTo(.18f).Within(.00001f));
                Assert.That(owner.GetStatus(ID), Is.SameAs(count)); Assert.That(count.RuntimeValue, Is.Zero);
                Assert.That(owner.GetStatusDMGBonus("SE_Heavy_Plumage", "SK_Heavy_Plumage", 1, "Physical", env.Enemy.Entity), Is.Zero);
                env.DataManager.CharacterSkillDict[BattleFlowTestEnv.AllyID][0].APCost = 7;
                // 技能对象由初始化索引持有同一配表引用；再次真实付费，3点零头+7点恰好1枚。
                Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True);
                Assert.That(count.RuntimeValue, Is.EqualTo(1f));
                Assert.That(count.StackCount, Is.EqualTo(1)); Assert.That(count.IsPermanent, Is.True);
                Assert.That(random.FloatCallCount, Is.EqualTo(8), "正常8个命中各判一次暴击，属性查询不抽随机");
                Assert.That(random.IntCallCount, Is.Zero);
            }
        }

        [Test]
        [Timeout(7000)]
        public void Plumage_ShieldOnlyOrEmptyHeavyDoesNotConsume_AndOverflowIsNotBanked()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.ConfigureAllyAction(0, "SK_Normal_PlumageCap", "SE_Normal_PlumageCap", "Physical", ".1*TotalATK,0", 85);
                env.ConfigureAllyAction(1, "SK_Heavy_PlumageCap", "SE_Heavy_PlumageCap", "Physical", ".1*TotalATK,0", 0);
                env.CreateMinimalBattle(); var random = Start(env);
                var owner = env.Ally.Entity; var count = Equip(env);
                Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True); Assert.That(count.RuntimeValue, Is.EqualTo(6f));
                env.Enemy.Entity.AddShield(10000f, "", 4f);
                float hp = env.Enemy.Entity.CurrentHP;
                Assert.That(env.Manager.UseHeavyAttackBySlot(0), Is.True);
                Assert.That(env.Enemy.Entity.CurrentHP, Is.EqualTo(hp)); Assert.That(count.RuntimeValue, Is.EqualTo(6f));
                env.Ally.SetSkillEffects("SK_Heavy_PlumageCap", new List<SkillEffectData>());
                Assert.That(env.Manager.UseHeavyAttackBySlot(0), Is.True); Assert.That(count.RuntimeValue, Is.EqualTo(6f));
                // 私有零头仅5而非25；不靠直接写RuntimeValue冒充一次真实消费。
                env.Ally.SetSkillEffects("SK_Heavy_PlumageCap", new List<SkillEffectData>
                { new SkillEffectData { SkillEffectID2 = "SE_Heavy_PlumageCapTrue", EffectType = "Damage",
                    DamageType = "Heavy", Element = "True", Param1 = "1", TargetType = "Enemy",
                    TargetNumber = 1, TargetConsecutive = 1, TargetConsecutiveSet = true } });
                Assert.That(env.Manager.UseHeavyAttackBySlot(0), Is.True); Assert.That(count.RuntimeValue, Is.Zero);
                env.DataManager.CharacterSkillDict[BattleFlowTestEnv.AllyID][0].APCost = 5;
                Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True); Assert.That(count.RuntimeValue, Is.EqualTo(1f));
                Assert.That(random.FloatCallCount, Is.EqualTo(3), "True及空效果不判暴击，原攻击/盾命中各一次");
                Assert.That(random.IntCallCount, Is.Zero);
            }
        }

        [Test]
        [Timeout(7000)]
        public void Plumage_IndependentStatusDamageCannotMakeShieldOnlyHeavyConsume()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.ConfigureAllyAction(1, "SK_Heavy_PlumageExtra", "SE_Heavy_PlumageExtra", "Physical", ".1*TotalATK,0", 20);
                env.CreateMinimalBattle(); Start(env);
                var owner = env.Ally.Entity; var count = Equip(env);
                const string extra = "ST_Plumage_Extra";
                var main = new StatusMainData { StatusID = 900111, StatusID2 = extra, StatusName = extra,
                    StatusType = "Buff", MaxStack = 1, MaxCount = 1, WhenMax = "0" };
                env.DataManager.StatusMainDict[extra] = main;
                env.DataManager.StatusEffectDict["STE_Plumage_Extra"] = new StatusEffectData
                { StatusEffectID = 90011101, StatusEffectID2 = "STE_Plumage_Extra", EffectType = "Damage",
                    Param1 = "1", Element = "True", TargetType = "Enemy", TargetSelect = "1" };
                env.DataManager.StatusActionDict[extra] = new List<StatusActionData>
                { new StatusActionData { StatusID2 = extra, ActionType = "OnTrigger", OutgoingHit = "Heavy",
                    Param1 = "STE_Plumage_Extra", MaxTimePerHit = 1 } };
                owner.AddStatus(extra, owner, 0, 2, 0, main);
                env.Enemy.Entity.AddShield(10000f, "", 4f);
                float before = env.Enemy.Entity.CurrentHP;
                Assert.That(env.Manager.UseHeavyAttackBySlot(0), Is.True);
                Assert.That(env.Enemy.Entity.CurrentHP, Is.LessThan(before), "独立状态True确实造成生命伤害");
                Assert.That(count.RuntimeValue, Is.EqualTo(2f), "源重击只命中盾，不能借独立追加伤害消费");
            }
        }

        [TestCase("", "Heavy", .12f, 0f)]
        [TestCase("Normal", "Heavy", .12f, 0f)]
        [TestCase("Heavy", "Normal", 0f, 2f)]
        [Timeout(7000)]
        public void Plumage_OnlyDamageTypeControlsBonusAndConsumption_NotActionOrLevelType(
            string actionType, string damageType, float bonus, float remaining)
        {
            using (var env = new BattleFlowTestEnv())
            {
                const string skill = "SK_Plumage_TypeSplit", effect = "SE_Plumage_TypeSplit";
                env.ConfigureAllyAction(2, skill, effect, "Physical", ".1*TotalATK,0", 20);
                env.DataManager.CharacterSkillDict[BattleFlowTestEnv.AllyID].Find(s => s.SkillID2 == skill).ActionType = actionType;
                env.DataManager.SkillEffectDict[effect].DamageType = damageType;
                env.CreateMinimalBattle(); Start(env); var count = Equip(env); var owner = env.Ally.Entity;
                int hits = 0;
                using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, hit) =>
                {
                    if (hit.Source.SourceEffectID != effect) return;
                    hits++;
                    Assert.That(count.RuntimeValue, Is.EqualTo(2f));
                    Assert.That(owner.GetStatusDMGBonus(effect, skill, 2, "Physical", hit.Subject), Is.EqualTo(bonus).Within(.00001f));
                }, 0);
                Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
                Assert.That(hits, Is.EqualTo(1)); Assert.That(count.RuntimeValue, Is.EqualTo(remaining));
            }
        }

        [Test, Timeout(10000)]
        public void Plumage_DelayedHeavyContainerNeedsNoStartupActionOrOriginalPaymentInheritance()
        {
            using (var env = new BattleFlowTestEnv())
            {
                const string start = "SK_Plumage_Charge", release = "SK_Plumage_Release", stateID = "ST_Plumage_Charge";
                env.ConfigureAllyAction(2, release, "SE_Plumage_Release", "Physical", ".1*TotalATK,0", 0);
                env.DataManager.CharacterSkillDict[BattleFlowTestEnv.AllyID].Find(s => s.SkillID2 == release).ActionType = "";
                env.ConfigureAllyAction(1, start, "SE_Plumage_Charge", "Physical", null, 20,
                    effectType: "ApplyStatus", param1: stateID, duration: 1, addInPhase: 1, triggerPhase: 1, targetType: "Self");
                env.DataManager.CharacterSkillDict[BattleFlowTestEnv.AllyID].Find(s => s.SkillID2 == start).ActionType = "";
                env.ConfigureStatus(stateID, actionType: "OnItsTurn", actionEffectID2: release);
                env.CreateMinimalBattle();
                env.Ally.SetSkillEffects(release, new List<SkillEffectData>
                {
                    new SkillEffectData { SkillEffectID2 = "SE_Plumage_Release", EffectType = "Damage", DamageType = "Heavy",
                        Element = "Physical", Param1 = ".1*TotalATK,0", TargetType = "Enemy", TargetNumber = 1,
                        TargetConsecutive = 1, TargetConsecutiveSet = true },
                    new SkillEffectData { SkillEffectID2 = "SE_Plumage_Release2", EffectIndex = 1, EffectType = "Damage", DamageType = "Heavy",
                        Element = "Physical", Param1 = ".1*TotalATK,0", TargetType = "Enemy", TargetNumber = 1,
                        TargetConsecutive = 1, TargetConsecutiveSet = true }
                });
                Start(env); var count = Equip(env); var owner = env.Ally.Entity;
                int actions = 0, hits = 0;
                using var actionListener = BattleEventHub.Subscribe<ActionEventData>(BattleEventKind.ActionStarted, (_, __) => actions++, 0);
                using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, hit) =>
                {
                    if (!hit.Source.SourceEffectID.StartsWith("SE_Plumage_Release")) return;
                    hits++;
                    Assert.That(hit.Source.SourceKind, Is.EqualTo(ReactionSourceKind.StatusEffect));
                    Assert.That(count.RuntimeValue, Is.EqualTo(2f), "两条延迟Heavy结清前都保留增益");
                    Assert.That(owner.GetStatusDMGBonus(hit.Source.SourceEffectID, "", 2, "Physical", hit.Subject), Is.EqualTo(.12f).Within(.00001f));
                }, 0);
                Assert.That(env.Manager.UseHeavyAttackBySlot(0), Is.True);
                Assert.That(count.RuntimeValue, Is.EqualTo(2f)); Assert.That(hits, Is.Zero);
                env.Manager.EndAllyTurn(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                Assert.That(hits, Is.EqualTo(2)); Assert.That(actions, Is.Zero);
                Assert.That(count.RuntimeValue, Is.Zero); Assert.That(owner.GetStatus(ID), Is.SameAs(count));
            }
        }

        [Test, Timeout(10000)]
        public void Plumage_OrdinaryStatusHeavyEffectsShareWholeActionBoundary_NotOneEffectOrOneHit()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.ConfigureAllyAction(0, "SK_Plumage_Fund", "SE_Plumage_Fund", "Physical", ".1*TotalATK,0", 20);
                const string stateID = "ST_Plumage_StatusHeavy";
                env.ConfigureStatus(stateID, actionType: "OnItsTurn", actionEffectID2: "STE_Plumage_H1;STE_Plumage_H2");
                foreach (string effect in new[] { "STE_Plumage_H1", "STE_Plumage_H2" })
                    env.DataManager.StatusEffectDict[effect] = new StatusEffectData
                    { StatusEffectID2 = effect, EffectType = "Damage", DamageType = "Heavy", Element = "Physical",
                        Param1 = ".1*TotalATK,0", TargetType = "Enemy", TargetSelect = "1" };
                env.CreateMinimalBattle(); Start(env); var count = Equip(env); var owner = env.Ally.Entity;
                Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True);
                owner.AddStatus(stateID, owner, 1, 3, 3, env.DataManager.StatusMainDict[stateID]);
                int hits = 0;
                using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, hit) =>
                {
                    if (!hit.Source.SourceEffectID.StartsWith("STE_Plumage_H")) return;
                    hits++;
                    Assert.That(count.RuntimeValue, Is.EqualTo(2f));
                    Assert.That(owner.GetStatusDMGBonus(hit.Source.SourceEffectID, "", 2, "Physical", hit.Subject), Is.EqualTo(.12f).Within(.00001f));
                }, 0);
                env.Manager.EndAllyTurn(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.EnemyPreTurn);
                Assert.That(hits, Is.EqualTo(2)); Assert.That(count.RuntimeValue, Is.Zero);
            }
        }
    }
}
