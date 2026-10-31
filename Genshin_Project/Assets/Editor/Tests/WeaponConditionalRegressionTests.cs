using System;
using System.Collections.Generic;
using NUnit.Framework;
using OfficeOpenXml;

namespace GenshinTurnBased.Tests.EditMode
{
    /// <summary>武器独立条件、付款与基础加算：内存配表/真实技能入口，未运行Unity。</summary>
    public sealed class WeaponConditionalRegressionTests
    {
        private static StatusMainData Main(BattleFlowTestEnv env, int id, string part = null,
            string hook = null, string damageType = null, int maxStack = 1)
        {
            var main = new StatusMainData { StatusID = id, StatusID2 = DataManager.WeaponStatusKey(id),
                StatusName = "WeaponConditionalTest", StatusType = "Buff", MultiplierPart = part,
                ScriptHook = hook, ApplyDamageType = damageType, MaxStack = maxStack,
                MaxCount = maxStack, WhenMax = "1" };
            env.DataManager.StatusMainDict[main.StatusID2] = main;
            return main;
        }
        private static StatusEffectData Effect(BattleFlowTestEnv env, int id, string type, string param)
        {
            var effect = new StatusEffectData { StatusEffectID = id, StatusEffectID2 = "STE_Weapon_" + id,
                EffectType = type, Param1 = param, TargetType = "Self" };
            env.DataManager.StatusEffectDict[effect.StatusEffectID2] = effect;
            return effect;
        }
        private static WeaponRuntimeContext Context(BattleEntity owner, int weapon, float parameter)
        {
            var context = new WeaponRuntimeContext { Equipper = owner, WeaponID = weapon, Refinement = 1,
                SourceInstanceID = "WeaponConditional:" + weapon };
            context.ResolvedParams[1] = parameter;
            return context;
        }
        private static void Equip(BattleFlowTestEnv env, int id, WeaponRuntimeContext context)
            => env.Ally.Entity.AddStatus(DataManager.WeaponStatusKey(id), env.Ally.Entity, 0, 2, 0,
                env.DataManager.StatusMainDict[DataManager.WeaponStatusKey(id)], context);
        private static SequenceBattleRandomSource Start(BattleFlowTestEnv env)
        {
            env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random); return random;
        }

        [TestCase(15, "0", "Normal", .36f, 15)]
        [TestCase(16, "0", "Normal", -.10f, 16)]
        [TestCase(20, "-5", "Normal", .36f, 15)]
        [TestCase(15, "1", "Normal", -.10f, 16)]
        [TestCase(25, "Free", "Normal", .36f, 0)]
        [TestCase(10, "0", "Heavy", .36f, 10)]
        [Timeout(7000)]
        public void Slingshot_ReadsActualPaidAP_NotListedCostOrRemainingAP(int listedAP, string modifier,
            string damageType, float bonus, int paid)
        {
            using var env = new BattleFlowTestEnv();
            env.ConfigureAllyAction(0, "SK_Slingshot", "SE_Slingshot", "Physical", ".1*TotalATK,0", listedAP);
            env.DataManager.SkillEffectDict["SE_Slingshot"].DamageType = damageType;
            Main(env, 4301201, "DMGBonus", "Hook(4301201)", "Normal;Heavy");
            Main(env, 4301202, "DMGBonus", "Hook(4301202)", "Normal;Heavy");
            Main(env, 4301203).StatusType = "Count";
            Effect(env, 430120101, "Buff", "Index(1)"); Effect(env, 430120201, "Debuff", "-0.1");
            Main(env, 9901101);
            Effect(env, 990110101, "APCost", modifier).Param2 = "Normal";
            env.CreateMinimalBattle(); var random = Start(env);
            var owner = env.Ally.Entity; var context = Context(owner, 3012, .36f);
            Equip(env, 4301201, context); Equip(env, 4301202, context);
            Equip(env, 4301203, context);
            owner.AddStatus(DataManager.WeaponStatusKey(9901101), owner, 0, 2, 0,
                env.DataManager.StatusMainDict[DataManager.WeaponStatusKey(9901101)]);
            var observed = new List<float>();
            using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, hit) =>
            {
                if (hit.Source.SourceEffectID != "SE_Slingshot") return;
                observed.Add(owner.GetStatusDMGBonus("SE_Slingshot", "SK_Slingshot", 3, "Physical", hit.Subject));
            }, 0);
            Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True);
            Assert.That(observed, Has.Count.EqualTo(1));
            Assert.That(observed[0], Is.EqualTo(bonus).Within(.00001f));
            Assert.That(env.Manager.APManager.CurrentAP, Is.EqualTo(100 - paid));
            Assert.That(owner.GetStatusDMGBonus("SE_Slingshot", "SK_Slingshot", 0, "Physical", env.Enemy.Entity), Is.Zero,
                "调用结束后不能保留上次付款；显式0AP与未知付款不同");
            Assert.That(random.FloatCallCount, Is.EqualTo(1)); Assert.That(random.IntCallCount, Is.Zero);
        }

        private static string APTables(BattleFlowTestEnv env, int weapon)
        {
            if (weapon == 3012)
            {
                Main(env, 4301201, "DMGBonus", "Hook(4301201)", "Normal;Heavy");
                Main(env, 4301202, "DMGBonus", "Hook(4301202)", "Normal;Heavy");
                Effect(env, 430120101, "Buff", "Index(1)"); Effect(env, 430120201, "Debuff", "-0.1");
                Main(env, 4301203).StatusType = "Count";
                return "ST_Weapon_4301203";
            }
            Main(env, 4318101, "DMGBonus", damageType: "Normal;Heavy");
            Main(env, 4318102, "DMGBonus", "Hook(4318102)", "Normal;Heavy");
            Effect(env, 431810101, "Buff", "Index(1)");
            Effect(env, 431810201, "Buff", "Index(2)").DamageHook = "Hook(431810201)";
            Main(env, 4318103).StatusType = "Count";
            return "ST_Weapon_4318103";
        }
        private static void EquipAP(BattleFlowTestEnv env, int weapon)
        {
            var context = Context(env.Ally.Entity, weapon, weapon == 3012 ? .36f : .12f);
            context.ResolvedParams[2] = .04f;
            foreach (int id in weapon == 3012 ? new[] { 4301201, 4301202, 4301203 } : new[] { 4318101, 4318102, 4318103 })
                Equip(env, id, context);
        }

        [TestCase(3012, -.1f), TestCase(3181, .36f), Timeout(10000)]
        public void WeaponAP_CarriesAcrossPhases_FireRainDoesNotClear_WholeDelayedHeavySharesThenConsumes(int weapon, float expected)
        {
            using var env = new BattleFlowTestEnv(); string countID = APTables(env, weapon);
            const string charge = "ST_APCharge", rain = "ST_APRain";
            env.ConfigureAllyAction(1, "SK_APCharge", "SE_APCharge", "", "", 30,
                effectType: "ApplyStatus", param1: charge, duration: 1, addInPhase: 1, triggerPhase: 1, targetType: "Self");
            env.DataManager.CharacterSkillDict[BattleFlowTestEnv.AllyID][0].ActionType = "";
            env.ConfigureStatus(charge, actionType: "OnItsTurn", actionEffectID2: "STE_APHeavy1;STE_APHeavy2");
            env.ConfigureStatus(rain, actionType: "OnItsTurn", actionEffectID2: "STE_APRain");
            foreach (string id in new[] { "STE_APHeavy1", "STE_APHeavy2", "STE_APRain" })
                env.DataManager.StatusEffectDict[id] = new StatusEffectData { StatusEffectID2 = id, EffectType = "Damage",
                    DamageType = id == "STE_APRain" ? "Skill" : "Heavy", Element = "Physical",
                    Param1 = ".1*TotalATK,0", TargetType = "Enemy", TargetSelect = "1" };
            env.CreateMinimalBattle(); Start(env); EquipAP(env, weapon); var owner = env.Ally.Entity;
            var count = owner.GetStatus(countID); var bonuses = new List<float>(); int rainHits = 0;
            using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, hit) =>
            {
                if (hit.Source.SourceEffectID == "STE_APRain")
                { rainHits++; Assert.That(count.RuntimeValue, Is.EqualTo(30f)); return; }
                if (!hit.Source.SourceEffectID.StartsWith("STE_APHeavy")) return;
                Assert.That(count.RuntimeValue, Is.EqualTo(30f), "全部输出结清前Count不提前消费");
                bonuses.Add(owner.GetStatusDMGBonus(hit.Source.SourceEffectID, "", 2, "Physical", hit.Subject));
            }, 0);
            Assert.That(env.Manager.UseHeavyAttackBySlot(0), Is.True); Assert.That(count.RuntimeValue, Is.EqualTo(30f));
            owner.AddStatus(rain, owner, 1, 3, 3, env.DataManager.StatusMainDict[rain]);
            env.Manager.EndAllyTurn(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            Assert.That(rainHits, Is.EqualTo(1)); Assert.That(bonuses, Has.Count.EqualTo(2));
            Assert.That(bonuses, Is.All.EqualTo(expected).Within(.00001f)); Assert.That(count.RuntimeValue, Is.Zero);
        }

        [TestCase(3012, .36f), TestCase(3181, .2f), Timeout(7000)]
        public void WeaponAP_OtherActionClearsOldAndItsOwnFee_BeforeNextNormalPayment(int weapon, float expected)
        {
            using var env = new BattleFlowTestEnv(); string countID = APTables(env, weapon);
            env.ConfigureAllyAction(1, "SK_APSave", "SE_APSave", "", "", 30, effectType: "ApplyStatus", param1: "ST_APSave", targetType: "Self");
            env.DataManager.CharacterSkillDict[BattleFlowTestEnv.AllyID][0].ActionType = "";
            env.ConfigureStatus("ST_APSave");
            env.ConfigureAllyAction(2, "SK_APOther", "SE_APOther", "Physical", ".1*TotalATK,0", 20);
            env.ConfigureAllyAction(0, "SK_APNormal", "SE_APNormal", "Physical", ".1*TotalATK,0", 10);
            env.CreateMinimalBattle(); Start(env); EquipAP(env, weapon); var owner = env.Ally.Entity;
            var count = owner.GetStatus(countID); float? bonus = null;
            using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, hit) =>
            {
                if (hit.Source.SourceEffectID == "SE_APOther") Assert.That(count.RuntimeValue, Is.Zero);
                if (hit.Source.SourceEffectID == "SE_APNormal")
                    bonus = owner.GetStatusDMGBonus(hit.Source.SourceEffectID, "SK_APNormal", 0, "Physical", hit.Subject);
            }, 0);
            Assert.That(env.Manager.UseHeavyAttackBySlot(0), Is.True); Assert.That(count.RuntimeValue, Is.EqualTo(30f));
            Assert.That(env.Manager.UseSkillBySlot(0), Is.True); Assert.That(count.RuntimeValue, Is.Zero);
            Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True);
            Assert.That(bonus, Is.EqualTo(expected).Within(.00001f)); Assert.That(count.RuntimeValue, Is.Zero);
        }

        private static void CinnabarTables(BattleFlowTestEnv env)
        {
            Main(env, 4303801, "BaseDMGBonusFlat", "Hook(4303801)", "Skill");
            Main(env, 4303802, maxStack: 2);
            Effect(env, 430380101, "Buff", "Index(1)*TotalDEF").DamageHook = "Hook(430380101)";
            var count = Effect(env, 430380102, "ApplyStatus", "ST_Weapon_4303802");
            count.Duration = 1; count.AddInPhase = 1;
            env.DataManager.StatusActionDict["ST_Weapon_4303801"] = new List<StatusActionData>
            { new StatusActionData { StatusID2 = "ST_Weapon_4303801", ActionType = "OnTrigger",
                OutgoingHit = "Skill", ScriptHook = "PostSelfDamage(4303801);Hook(4303802)",
                Param1 = "STE_Weapon_430380102", MaxTimePerTurn = 2 } };
        }

        [TestCase(0, 0f, .16f)]
        [TestCase(60, 59.99f, .16f)]
        [TestCase(60, 60f, .32f)]
        public void Hamayumi_OnlyDoublesItsOwnBonusAtPositiveFullEnergy(int maximum, float current, float expected)
        {
            using var env = new BattleFlowTestEnv();
            env.ConfigureAllyAction(0, "SK_Hamayumi", "SE_Hamayumi", "Physical", ".1*TotalATK,0", 0);
            Main(env, 4309001, "DMGBonus", damageType: "Normal");
            Main(env, 4309003, "DMGBonus", "Hook(4309003)", "Normal");
            Effect(env, 430900101, "Buff", "Index(1)"); Effect(env, 430900301, "Buff", "Index(1)");
            env.CreateMinimalBattle(); var owner = env.Ally.Entity;
            var context = Context(owner, 3090, .16f); Equip(env, 4309001, context); Equip(env, 4309003, context);
            owner.MaxEnergy = maximum; owner.CurrentEnergy = current;
            Assert.That(owner.GetStatusDMGBonus("SE_Hamayumi", "SK_Hamayumi", 3, "Physical", env.Enemy.Entity), Is.EqualTo(expected).Within(.00001f));
            Assert.That(owner.CurrentEnergy, Is.EqualTo(current));
        }

        [Test]
        public void ScionOfBlazingSun_OnlyOwnTargetMarkQualifies_NotOtherWearerOrWholeAOE()
        {
            using var env = new BattleFlowTestEnv();
            env.ConfigureAllyAction(1, "SK_Scion", "SE_Scion", "Physical", ".1*TotalATK,0", 0);
            Main(env, 4309702, "DMGBonus", "Hook(4309702)", "Heavy"); Main(env, 4309703, maxStack: 0);
            Effect(env, 430970201, "Buff", "Index(2)"); env.CreateMinimalBattle();
            var secondEnemy = env.CreateEnemy(2); var other = env.CreateAlly(2).Entity; var owner = env.Ally.Entity;
            var own = Context(owner, 3097, .6f); own.ResolvedParams[2] = .28f; Equip(env, 4309702, own);
            var foreign = Context(other, 3097, .6f); foreign.ResolvedParams[2] = .56f;
            var main = env.DataManager.StatusMainDict["ST_Weapon_4309703"];
            env.Enemy.Entity.AddStatus(main.StatusID2, other, 3, 2, 0, main, foreign);
            secondEnemy.Entity.AddStatus(main.StatusID2, owner, 3, 2, 0, main, own);
            Assert.That(owner.GetStatusDMGBonus("SE_Scion", "SK_Scion", 0, "Physical", env.Enemy.Entity), Is.Zero);
            Assert.That(owner.GetStatusDMGBonus("SE_Scion", "SK_Scion", 0, "Physical", secondEnemy.Entity), Is.EqualTo(.28f).Within(.00001f));
            secondEnemy.Entity.RemoveStatus(main.StatusID2, -1);
            Assert.That(owner.GetStatusDMGBonus("SE_Scion", "SK_Scion", 0, "Physical", secondEnemy.Entity), Is.Zero);
        }

        [Test]
        public void Deathmatch_CountsOnlyLivingOccupiedEnemies_AndSwitchesExclusiveATKAndDEF()
        {
            using var env = new BattleFlowTestEnv();
            Main(env, 4311001, "ATKBonus", "Hook(4311001)"); Main(env, 4311002, "DEFBonus", "Hook(4311001)");
            Main(env, 4311003, "ATKBonus", "Hook(4311003)");
            Effect(env, 431100101, "Buff", "Index(1)"); Effect(env, 431100201, "Buff", "Index(2)");
            Effect(env, 431100301, "Buff", "Index(3)"); env.CreateMinimalBattle(); var owner = env.Ally.Entity;
            var context = Context(owner, 3110, .16f); context.ResolvedParams[2] = .16f; context.ResolvedParams[3] = .24f;
            foreach (int id in new[] { 4311001, 4311002, 4311003 }) Equip(env, id, context);
            Assert.That(owner.GetStatusATKBonus("", "", 0, ""), Is.EqualTo(.24f).Within(.00001f));
            Assert.That(owner.GetStatusDEFBonus(), Is.Zero);
            var second = env.CreateEnemy(2).Entity;
            Assert.That(owner.GetStatusATKBonus("", "", 0, ""), Is.EqualTo(.16f).Within(.00001f));
            Assert.That(owner.GetStatusDEFBonus(), Is.EqualTo(.16f).Within(.00001f));
            second.CurrentHP = 0f;
            Assert.That(owner.GetStatusATKBonus("", "", 0, ""), Is.EqualTo(.24f).Within(.00001f));
            Assert.That(owner.GetStatusDEFBonus(), Is.Zero);
            second.CurrentHP = second.TotalHP; second.SlotPosition = 0;
            Assert.That(owner.GetStatusATKBonus("", "", 0, ""), Is.EqualTo(.24f).Within(.00001f));
        }

        [TestCase(40, .28f)]
        [TestCase(41, .20f)]
        [TestCase(60, .20f)]
        [TestCase(61, .12f)]
        public void MoonweaversDawn_UsesExclusiveMaxEnergyTiers_NotCurrentEnergy(int maximum, float expected)
        {
            using var env = new BattleFlowTestEnv();
            env.ConfigureAllyAction(3, "SK_Moonweaver", "SE_Moonweaver", "Physical", ".1*TotalATK,0", 0);
            Main(env, 4305201, "DMGBonus", damageType: "Burst");
            Main(env, 4305202, "DMGBonus", "Hook(4305202)", "Burst");
            Main(env, 4305203, "DMGBonus", "Hook(4305203)", "Burst");
            Effect(env, 430520101, "Buff", "Index(1)"); Effect(env, 430520201, "Buff", "Index(2)");
            Effect(env, 430520301, "Buff", "Index(3)");
            env.CreateMinimalBattle(); var owner = env.Ally.Entity; var context = Context(owner, 3052, .12f);
            context.ResolvedParams[2] = .08f; context.ResolvedParams[3] = .16f;
            foreach (int id in new[] { 4305201, 4305202, 4305203 }) Equip(env, id, context);
            owner.MaxEnergy = maximum;
            foreach (float energy in new[] { 0f, maximum })
            {
                owner.CurrentEnergy = energy;
                Assert.That(owner.GetStatusDMGBonus("SE_Moonweaver", "SK_Moonweaver", 0, "Physical", env.Enemy.Entity),
                    Is.EqualTo(expected).Within(.00001f));
            }
            using (DamageQueryContext.Enter("SE_Moonweaver", "Normal", "SK_Moonweaver"))
                Assert.That(owner.GetStatusDMGBonus("SE_Moonweaver", "SK_Moonweaver", 3, "Physical", env.Enemy.Entity), Is.Zero);
        }

        [TestCase(ShieldKind.Skill)]
        [TestCase(ShieldKind.Crystallize)]
        public void CalamityOfEshu_DamageAndCritShareRealShieldQualification(ShieldKind kind)
        {
            using var env = new BattleFlowTestEnv();
            env.ConfigureAllyAction(0, "SK_Eshu", "SE_Eshu", "Physical", ".1*TotalATK,0", 0);
            Main(env, 4305001, "DMGBonus", "Hook(4305001)", "Normal;Heavy");
            Main(env, 4305002, "CritRate", "Hook(4305001)", "Normal;Heavy");
            Effect(env, 430500101, "Buff", "Index(1)"); Effect(env, 430500201, "Buff", "Index(2)");
            env.CreateMinimalBattle(); var owner = env.Ally.Entity; var context = Context(owner, 3050, .2f);
            context.ResolvedParams[2] = .1f; Equip(env, 4305001, context); Equip(env, 4305002, context);
            float Bonus() => owner.GetStatusDMGBonus("SE_Eshu", "SK_Eshu", 2, "Physical", env.Enemy.Entity);
            Assert.That(Bonus(), Is.Zero);
            owner.AddShield(100f, "Pyro", 2f, kind: kind);
            Assert.That(Bonus(), Is.EqualTo(.2f).Within(.00001f));
            Assert.That(owner.GetStatusCritRate("SE_Eshu", "SK_Eshu", 2, "Physical", env.Enemy.Entity), Is.EqualTo(.1f).Within(.00001f));
            owner.Shields[0].Value = 0f; Assert.That(Bonus(), Is.Zero);
            Assert.That(owner.GetStatusCritRate("SE_Eshu", "SK_Eshu", 2, "Physical", env.Enemy.Entity), Is.Zero);
        }

        [TestCase(false, .1f)]
        [TestCase(true, .2f)]
        public void SereneWhistle_UsesCommittedTeamMoonsign_NotFrontField(bool full, float expected)
        {
            using var env = new BattleFlowTestEnv();
            using (var package = new ExcelPackage())
            {
                var sheet = package.Workbook.Worksheets.Add("Moonsign");
                sheet.Cells[1, 1].Value = "Key"; sheet.Cells[1, 2].Value = "Value";
                int row = 2;
                foreach (var pair in new[] { ("Enabled", "1"), ("CharacterIDs", BattleFlowTestEnv.AllyID.ToString()),
                    ("GleamCount", "1"), ("AscendantCount", "2") })
                { sheet.Cells[row, 1].Value = pair.Item1; sheet.Cells[row++, 2].Value = pair.Item2; }
                env.DataManager.TeamBonusRules.Moonsign = TeamBonusTableLoader.Read(sheet);
            }
            Main(env, 4305102, "HPBonus"); Main(env, 4305103, "HPBonus", "Hook(4305103)");
            Effect(env, 430510201, "Buff", "Index(1)"); Effect(env, 430510301, "Buff", "Index(2)");
            env.CreateMinimalBattle(); if (full) env.CreateAlly(2);
            Start(env); var owner = env.Ally.Entity; owner.CharacterCtrl.IsActive = false;
            var context = Context(owner, 3051, .1f); context.ResolvedParams[2] = .1f;
            Equip(env, 4305102, context); Equip(env, 4305103, context);
            Assert.That(owner.GetFinalHP(), Is.EqualTo(owner.TotalHP * (1f + expected)).Within(.001f));
            Main(env, 4307702, "EMBonus"); Main(env, 4307703, "EMBonus", "Hook(4307703)");
            Effect(env, 430770201, "Buff", "Index(1)"); Effect(env, 430770301, "Buff", "Index(2)");
            var keyContext = Context(owner, 3077, 60f); keyContext.ResolvedParams[2] = 60f;
            Equip(env, 4307702, keyContext); Equip(env, 4307703, keyContext);
            Assert.That(owner.GetFinalEM(), Is.EqualTo(owner.TotalEM + (full ? 120f : 60f)).Within(.001f));
        }

        [TestCase(3055, 4305501, "Electro"), TestCase(3106, 4310601, "Pyro"), Timeout(7000)]
        public void InfluenceBuff_SnapshotsAuraBeforeHit_PerTarget_NotFreshAttachment(int weapon, int statusID, string element)
        {
            using var env = new BattleFlowTestEnv();
            env.ConfigureAllyAction(2, "SK_Rainslasher", "SE_Rainslasher", element, ".1*TotalATK,1", 0);
            Main(env, statusID, "DMGBonus", "Hook(" + statusID + ")"); Effect(env, statusID * 100 + 1, "Buff", "Index(1)");
            env.CreateMinimalBattle(); var second = env.CreateEnemy(2); var random = Start(env);
            var owner = env.Ally.Entity; Equip(env, statusID, Context(owner, weapon, .2f));
            env.Enemy.Entity.ElementalAuras.Add(new ElementalAura { Element = "Hydro", AuraAmount = 1f });
            var bonuses = new Dictionary<int, float>();
            using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, hit) =>
            {
                if (hit.Source.SourceEffectID != "SE_Rainslasher" || hit.Source.SourceKind != ReactionSourceKind.CharacterSkill) return;
                bonuses[hit.Subject.SlotPosition] = owner.GetStatusDMGBonus("SE_Rainslasher", "SK_Rainslasher", 2, element, hit.Subject);
            }, 0);
            env.Ally.ForcedTargetPositions.AddRange(new[] { 1, 2 });
            Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
            Assert.That(bonuses[1], Is.EqualTo(.2f).Within(.00001f)); Assert.That(bonuses[2], Is.Zero);
            Assert.That(ElementQuery.HasAura(second.Entity, element), Is.True, "第二目标虽新附着，本次伤害不能倒算增伤");
            Assert.That(random.FloatCallCount, Is.EqualTo(2)); Assert.That(random.IntCallCount, Is.Zero);
        }

        [TestCase(.20f), TestCase(.32f), Timeout(7000)]
        public void Bell_ExpandsRefinementAgainstFinalHP_AndDoesNotRetroactivelyAbsorbTriggerDamage(float ratio)
        {
            using var env = new BattleFlowTestEnv();
            env.ConfigureAllyAction(0, "SK_Bell", "SE_Bell", "Physical", ".1*TotalATK,0", 0);
            Main(env, 4305701); Main(env, 4305702, "DMGBonus", "Hook(4305702)");
            var shield = Effect(env, 430570101, "Shield", "Index(1)*TotalHP");
            shield.Element = "None"; shield.Duration = 3; shield.DamageHook = "Hook(430570101)";
            Effect(env, 430570201, "Buff", "Index(2)");
            env.DataManager.StatusActionDict["ST_Weapon_4305701"] = new List<StatusActionData>
            { new StatusActionData { StatusID2 = "ST_Weapon_4305701", ActionType = "OnTrigger", OnDamage = "All",
                Param1 = "STE_Weapon_430570101", Cooldown = 10 } };
            Main(env, 9901102, "HPBonus"); Effect(env, 990110201, "Buff", ".2");
            env.CreateMinimalBattle(); Start(env); var owner = env.Ally.Entity;
            owner.AddStatus(DataManager.WeaponStatusKey(9901102), owner, 0, 2, 0,
                env.DataManager.StatusMainDict[DataManager.WeaponStatusKey(9901102)]);
            var context = Context(owner, 3057, ratio); context.ResolvedParams[2] = .12f;
            Equip(env, 4305701, context); Equip(env, 4305702, context);
            float hp = owner.CurrentHP;
            var source = new DamageSourceInfo { SourceEntity = env.Enemy.Entity, SourceKind = ReactionSourceKind.EnemySkill,
                CountsAsHit = true, SourceEffectID = "SE_Enemy_Bell" };
            owner.TakeDamage(1f, source);
            Assert.That(owner.CurrentHP, Is.EqualTo(hp - 1f).Within(.001f));
            Assert.That(owner.Shields, Has.Count.EqualTo(1));
            Assert.That(owner.Shields[0].Value, Is.EqualTo(ratio * owner.GetFinalHP()).Within(.001f));
            Assert.That(owner.Shields[0].Duration, Is.EqualTo(3f));
            Assert.That(owner.GetStatusDMGBonus("SE_Bell", "SK_Bell", 0, "Physical", env.Enemy.Entity), Is.EqualTo(.12f).Within(.00001f));
            owner.TakeDamage(1f, source); Assert.That(owner.Shields, Has.Count.EqualTo(1), "冷却中不重复造盾");
        }

        [Test, Timeout(7000)]
        public void AlleyFlash_OnlyHPDamageDisables_AndExpiryRestoresWithoutReapplyingBuff()
        {
            using var env = new BattleFlowTestEnv();
            env.ConfigureAllyAction(0, "SK_AlleyFlash", "SE_AlleyFlash", "Physical", ".1*TotalATK,0", 0);
            Main(env, 4303601, "DMGBonus", "Hook(4303601)"); Main(env, 4303602);
            Main(env, 4303603).WhenMax = "0";
            Effect(env, 430360101, "Buff", "Index(1)");
            var disabled = Effect(env, 430360201, "ApplyStatus", "ST_Weapon_4303603");
            disabled.Duration = 1; disabled.AddInPhase = 2;
            env.DataManager.StatusActionDict["ST_Weapon_4303602"] = new List<StatusActionData>
            { new StatusActionData { StatusID2 = "ST_Weapon_4303602", ActionType = "OnTrigger",
                OnDamage = "All", Param1 = "STE_Weapon_430360201" } };
            env.CreateMinimalBattle(); Start(env); var owner = env.Ally.Entity;
            var context = Context(owner, 3036, .12f); Equip(env, 4303601, context); Equip(env, 4303602, context);
            var original = owner.GetStatus("ST_Weapon_4303601");
            float Bonus() => owner.GetStatusDMGBonus("SE_AlleyFlash", "SK_AlleyFlash", 0, "Physical", env.Enemy.Entity);
            Assert.That(Bonus(), Is.EqualTo(.12f).Within(.00001f));
            owner.AddShield(1f, "", 4f);
            var source = new DamageSourceInfo { SourceEntity = env.Enemy.Entity, SourceKind = ReactionSourceKind.EnemySkill,
                SourceEffectID = "SE_Enemy_AlleyFlash", CountsAsHit = true };
            using (var batch = new HitBatch()) batch.Resolve(new HitRequest { Target = owner, Source = source,
                DamageBeforeShield = .5f, DamageType = "Skill", Element = "Physical" });
            Assert.That(owner.HasStatus("ST_Weapon_4303603"), Is.False);
            Assert.That(Bonus(), Is.EqualTo(.12f).Within(.00001f));
            using (var batch = new HitBatch()) batch.Resolve(new HitRequest { Target = owner, Source = source,
                DamageBeforeShield = 10f, DamageType = "Skill", Element = "Physical" });
            Assert.That(owner.HasStatus("ST_Weapon_4303603"), Is.True); Assert.That(Bonus(), Is.Zero);
            env.Manager.EndAllyTurn(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            Assert.That(owner.HasStatus("ST_Weapon_4303603"), Is.False);
            Assert.That(Bonus(), Is.EqualTo(.12f).Within(.00001f));
            Assert.That(owner.GetStatus("ST_Weapon_4303601"), Is.SameAs(original));
        }

        [TestCase(1), TestCase(2), Timeout(10000)]
        public void Cinnabar_WholeMultiHitMultiTargetEffectsUseTwoInstances_ThenResetNextRound(int repeats)
        {
            using var env = new BattleFlowTestEnv(); CinnabarTables(env);
            env.ConfigureAllyAction(2, "SK_Cinnabar", "SE_Cinnabar", "Physical", ".1*TotalATK,0", 0);
            env.DataManager.SkillLevelDict[$"{BattleFlowTestEnv.AllyID}_2"][1].Hits2 = ".1*TotalATK,0";
            env.DataManager.SkillEffectDict["SE_Cinnabar"].ExecuteCount = repeats;
            env.CreateMinimalBattle(); env.CreateEnemy(2); var random = Start(env);
            var owner = env.Ally.Entity; Equip(env, 4303801, Context(owner, 3038, .4f));
            var damages = new List<float>(); var counts = new List<int>();
            using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, hit) =>
            {
                if (hit.Source.SourceEffectID != "SE_Cinnabar") return;
                damages.Add(hit.DamageBeforeShield); counts.Add(owner.GetStatusStackCount("ST_Weapon_4303802"));
            }, 0);
            for (int use = 0; use < 3; use++)
            {
                env.Ally.ForcedTargetPositions.AddRange(new[] { 1, 2 });
                Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
                Assert.That(owner.GetStatusStackCount("ST_Weapon_4303802"), Is.EqualTo(Math.Min(2, use + 1)));
            }
            int block = 4 * repeats;
            Assert.That(damages, Has.Count.EqualTo(3 * block));
            for (int i = 0; i < 2 * block; i++) Assert.That(damages[i], Is.EqualTo(damages[2 * block] * 5f).Within(.001f));
            Assert.That(counts.GetRange(0, block), Is.All.EqualTo(0));
            Assert.That(counts.GetRange(block, block), Is.All.EqualTo(1));
            Assert.That(counts.GetRange(2 * block, block), Is.All.EqualTo(2));
            Assert.That(random.FloatCallCount, Is.EqualTo(12 * repeats)); Assert.That(random.IntCallCount, Is.Zero);
            env.Manager.EndAllyTurn(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            Assert.That(owner.GetStatusStackCount("ST_Weapon_4303802"), Is.Zero);
            int floatCalls = random.FloatCallCount, intCalls = random.IntCallCount; // 敌方行动分配可另耗整数随机，不计入武器抽取。
            Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
            Assert.That(damages[3 * block], Is.EqualTo(damages[0]).Within(.001f));
            Assert.That(random.FloatCallCount - floatCalls, Is.EqualTo(2 * repeats));
            Assert.That(random.IntCallCount, Is.EqualTo(intCalls));
        }

        [Test, Timeout(7000)]
        public void Cinnabar_ThreeDamageEffectsInOneSkill_FlushEachCompletionBeforeNextEffect()
        {
            using var env = new BattleFlowTestEnv(); CinnabarTables(env);
            env.ConfigureAllyAction(2, "SK_Cinnabar_Three", "SE_Cinnabar_One", "Physical", ".1*TotalATK,0", 0);
            var effects = new List<SkillEffectData>();
            foreach (string id in new[] { "SE_Cinnabar_One", "SE_Cinnabar_Two", "SE_Cinnabar_Three" })
            {
                var effect = new SkillEffectData { SkillEffectID2 = id, EffectType = "Damage", DamageType = "Skill",
                    Element = "Physical", Param1 = ".1*TotalATK,0", TargetType = "Enemy", TargetNumber = 1,
                    TargetConsecutive = 1, TargetConsecutiveSet = true, ExecuteCount = 2 };
                effects.Add(effect); env.DataManager.SkillEffectDict[id] = effect;
            }
            env.CreateMinimalBattle(); var random = Start(env); var owner = env.Ally.Entity;
            env.Ally.SetSkillEffects("SK_Cinnabar_Three", effects); Equip(env, 4303801, Context(owner, 3038, .4f));
            var damages = new List<float>(); var counts = new List<int>();
            using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, hit) =>
            {
                if (hit.Source.SourceSkillID != "SK_Cinnabar_Three") return;
                damages.Add(hit.DamageBeforeShield); counts.Add(owner.GetStatusStackCount("ST_Weapon_4303802"));
            }, 0);
            Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
            Assert.That(damages, Has.Count.EqualTo(6));
            Assert.That(counts, Is.EqualTo(new[] { 0, 0, 1, 1, 2, 2 }));
            for (int i = 0; i < 4; i++) Assert.That(damages[i], Is.EqualTo(damages[4] * 5f).Within(.001f));
            Assert.That(random.FloatCallCount, Is.EqualTo(6)); Assert.That(random.IntCallCount, Is.Zero);
        }

        [Test, Timeout(7000)]
        public void Cinnabar_OffFieldOrdinaryStatusSkillAndShieldHitUseTheSameQuota()
        {
            using var env = new BattleFlowTestEnv(); CinnabarTables(env);
            const string state = "ST_Cinnabar_Delayed";
            env.ConfigureStatus(state, actionType: "OnItsTurn", actionEffectID2: "STE_Cinnabar_A;STE_Cinnabar_B;STE_Cinnabar_C");
            foreach (string id in new[] { "STE_Cinnabar_A", "STE_Cinnabar_B", "STE_Cinnabar_C" })
                env.DataManager.StatusEffectDict[id] = new StatusEffectData { StatusEffectID2 = id,
                    EffectType = "Damage", DamageType = "Skill", Element = "Physical", Param1 = ".1*TotalATK,0",
                    TargetType = "Enemy", TargetSelect = "1" };
            env.CreateMinimalBattle(); Start(env); var owner = env.Ally.Entity;
            Equip(env, 4303801, Context(owner, 3038, .4f)); owner.CharacterCtrl.IsActive = false;
            env.Enemy.Entity.AddShield(10000f, "", 4f);
            owner.AddStatus(state, owner, 1, 3, 3, env.DataManager.StatusMainDict[state]);
            var damages = new List<float>();
            using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, hit) =>
            {
                if (!hit.Source.SourceEffectID.StartsWith("STE_Cinnabar_")) return;
                damages.Add(hit.DamageBeforeShield); Assert.That(hit.ActualHPDamage, Is.Zero);
            }, 0);
            env.Manager.EndAllyTurn(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.EnemyPreTurn);
            Assert.That(damages, Has.Count.EqualTo(3));
            Assert.That(damages[0], Is.EqualTo(damages[2] * 5f).Within(.001f));
            Assert.That(damages[1], Is.EqualTo(damages[0]).Within(.001f));
            Assert.That(owner.GetStatusStackCount("ST_Weapon_4303802"), Is.EqualTo(2));
        }
    }
}
