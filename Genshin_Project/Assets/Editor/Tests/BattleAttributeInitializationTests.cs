using System.Collections.Generic;
using NUnit.Framework;
using OfficeOpenXml;

namespace GenshinTurnBased.Tests.EditMode
{
    /// <summary>只定义内存回归；完整构筑经StartBattle统一收尾，不启动Unity测试。</summary>
    public sealed class BattleAttributeInitializationTests
    {
        [Test]
        public void FullRosterAndLateBindingsFinishBeforeHPCommit_AndFirstDamageReadsCompletedLiveAttributes()
        {
            using var env = new BattleFlowTestEnv();
            Configure(env, 3196);
            env.ConfigureAllyAction(0, "SK_InitialAttributes", "SE_InitialAttributes", "Physical", "1*TotalATK,0", 0);
            env.CreateMinimalBattle();
            var owner = env.Ally.Entity;
            var adventurer = env.CreateAlly(2, false);
            var wounded = env.CreateAlly(3, false).Entity; wounded.CurrentHP = 550f;
            var dead = env.CreateAlly(4, false).Entity; dead.CurrentHP = 0f;
            var woundedEnemy = env.CreateEnemy(2).Entity; woundedEnemy.CurrentHP = 200f;
            var enemyHP = Main(env, 990680, "HPBonus"); Effect(env, 99068001, "Buff", ".2");
            foreach (var enemy in new[] { env.Enemy.Entity, woundedEnemy })
                enemy.AddStatus(enemyHP.StatusID2, enemy, 0, 2, 0, enemyHP);
            Assert.That(env.Ally.EquipWeapon(new WeaponLoadout { WeaponID = 3196 }), Is.True);
            env.Ally.InitializeArtifactSetStatuses(new Dictionary<int, int> { [510] = 2, [534] = 2 });
            adventurer.InitializeArtifactSetStatuses(new Dictionary<int, int> { [510] = 4 });
            int lateBindings = 0, heals = 0, hpChanges = 0;
            using var status = BattleEventHub.Subscribe<StatusEventData>(BattleEventKind.StatusAdded, (_, fact) =>
            {
                if (fact.StatusId != AdventurerArtifactHooks.SharedID) return;
                lateBindings++;
                Assert.That(BattleAttributeInitialization.IsComplete(owner), Is.False);
                Assert.That(owner.CurrentHP, Is.EqualTo(1000f), "后置队伍绑定完成前仍是初始资源，不按单个加成补血");
                Assert.That(adventurer.Entity.CurrentHP, Is.EqualTo(1000f));
                Assert.That(wounded.CurrentHP, Is.EqualTo(550f));
            }, 0);
            using var healing = BattleEventHub.Subscribe<HealEventData>(BattleEventKind.HealResolved, (_, fact) => heals++, 0);
            using var changed = BattleEventHub.Subscribe<HPChangedEventData>(BattleEventKind.HPChanged, (_, fact) => hpChanges++, 0);
            Assert.That(owner.CurrentHP, Is.EqualTo(1000f));
            env.Manager.StartBattle();
            Assert.That(lateBindings, Is.EqualTo(3), "只有存活编队成员收到冒险家后置绑定");
            Assert.That(owner.GetFinalHP(), Is.EqualTo(2850f).Within(.001f));
            Assert.That(owner.CurrentHP, Is.EqualTo(2850f).Within(.001f));
            Assert.That(adventurer.Entity.CurrentHP, Is.EqualTo(2450f).Within(.001f));
            Assert.That(wounded.GetFinalHP(), Is.EqualTo(1450f).Within(.001f));
            Assert.That(wounded.CurrentHP, Is.EqualTo(550f)); Assert.That(dead.CurrentHP, Is.Zero);
            Assert.That(env.Enemy.Entity.CurrentHP, Is.EqualTo(1200f));
            Assert.That(woundedEnemy.CurrentHP, Is.EqualTo(200f));
            foreach (var actor in new[] { owner, adventurer.Entity, wounded, dead, env.Enemy.Entity, woundedEnemy })
                Assert.That(BattleAttributeInitialization.IsComplete(actor), Is.True);
            Assert.That(heals, Is.Zero); Assert.That(hpChanges, Is.Zero);
            // 若把护摩固定攻击提前缓存，初始1000/2850会误触发半血额外项；完成后只应有2850*.008。
            Assert.That(owner.GetFinalATK("query", "", -1, "Physical"), Is.EqualTo(122.8f).Within(.001f));
            env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            float firstDamage = -1f;
            using var damage = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, fact) =>
            { if (fact.Source.SourceEffectID == "SE_InitialAttributes") firstDamage = fact.DamageBeforeShield; }, 0);
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True);
            Assert.That(firstDamage, Is.EqualTo(61.4f).Within(.001f));
            Assert.That(random.FloatCallCount, Is.EqualTo(1)); Assert.That(random.IntCallCount, Is.Zero);
            var temporary = Main(env, 990681, "HPBonus"); Effect(env, 99068101, "Buff", ".1");
            owner.AddStatus(temporary.StatusID2, owner, 2, 2, 0, temporary);
            Assert.That(owner.GetFinalHP(), Is.EqualTo(2950f).Within(.001f));
            Assert.That(owner.CurrentHP, Is.EqualTo(2850f).Within(.001f));
            Assert.That(owner.GetFinalATK("query", "", -1, "Physical"), Is.EqualTo(123.6f).Within(.001f),
                "统一初始化不冻结后续动态属性");
            env.Manager.ResetBattle();
            Assert.That(BattleAttributeInitialization.IsComplete(owner), Is.False);
        }

        [TestCase(3196, true)]
        [TestCase(3196, false)]
        [TestCase(3217, true)]
        [TestCase(3217, false)]
        public void WeaponAndArtifactSetupOrder_DoesNotChangeFinalOpeningHP(int weapon, bool weaponFirst)
        {
            using var env = new BattleFlowTestEnv(); Configure(env, weapon); env.CreateMinimalBattle(); env.CreateAlly(2, false);
            if (weaponFirst) Assert.That(env.Ally.EquipWeapon(new WeaponLoadout { WeaponID = weapon }), Is.True);
            env.Ally.InitializeArtifactSetStatuses(new Dictionary<int, int> { [534] = 2 });
            if (!weaponFirst) Assert.That(env.Ally.EquipWeapon(new WeaponLoadout { WeaponID = weapon }), Is.True);
            Assert.That(env.Ally.Entity.CurrentHP, Is.EqualTo(1000f));
            Assert.That(BattleAttributeInitialization.IsComplete(env.Ally.Entity), Is.False);
            env.Manager.StartBattle();
            Assert.That(env.Ally.Entity.GetFinalHP(), Is.EqualTo(1650f).Within(.001f));
            Assert.That(env.Ally.Entity.CurrentHP, Is.EqualTo(1650f).Within(.001f));
            Assert.That(BattleAttributeInitialization.IsComplete(env.Ally.Entity), Is.True);
            env.Ally.Entity.CurrentHP = 100f;
            BattleSpecialRules.OnBattleReady(env.Manager); env.Manager.StartBattle();
            Assert.That(env.Ally.Entity.CurrentHP, Is.EqualTo(100f), "重入初始化/重复开始不得再次补满血");
            env.Ally.Entity.CurrentHP = 1000f; // 恰好等于首次裸面板满血，但相对完整构筑仍为伤残。
            env.Manager.StopBattle(); env.Manager.StartBattle();
            Assert.That(env.Ally.Entity.GetFinalHP(), Is.EqualTo(1650f).Within(.001f));
            Assert.That(env.Ally.Entity.CurrentHP, Is.EqualTo(1000f), "停止后再开始不得重用首次满血资格");
        }

        [Test]
        public void ExtraBaseHP_ReceivesWaterWeaponAndBakedArtifactPercentagesExactlyOnce()
        {
            using var env = new BattleFlowTestEnv(); Configure(env, 3217); env.CreateMinimalBattle(); env.CreateAlly(2, false);
            env.Ally.EquipWeapon(new WeaponLoadout { WeaponID = 3217 });
            env.Ally.InitializeArtifactSetStatuses(new Dictionary<int, int> { [534] = 2 });
            var main = Main(env, 990682, "ExtraBaseHP"); Effect(env, 99068201, "Buff", "100");
            var owner = env.Ally.Entity;
            owner.AddStatus(main.StatusID2, owner, 0, 2, 0, main);
            env.Manager.StartBattle();
            Assert.That(owner.GetFinalHP(), Is.EqualTo(1815f).Within(.001f), "(1000+100)*(1+.25+.2+.2)");
            Assert.That(owner.CurrentHP, Is.EqualTo(1815f).Within(.001f));
            BattleSpecialRules.OnBattleReady(env.Manager);
            Assert.That(owner.GetFinalHP(), Is.EqualTo(1815f).Within(.001f), "重入初始化不重复登记烘入百分比");
            owner.RemoveStatus(main.StatusID2);
            Assert.That(owner.GetFinalHP(), Is.EqualTo(1650f).Within(.001f));
            Assert.That(owner.CurrentHP, Is.EqualTo(1650f).Within(.001f));
        }

        private static void Configure(BattleFlowTestEnv env, int weapon)
        {
            env.DataManager.CharacterAttributesDict[BattleFlowTestEnv.AllyID].Element = "Hydro";
            env.DataManager.CharacterAttributesDict[BattleFlowTestEnv.AllyID].WeaponType = weapon == 3196 ? 3 : 4;
            using var package = new ExcelPackage(); var sheet = package.Workbook.Worksheets.Add("Resonance");
            sheet.Cells[1, 1].Value = "Key"; sheet.Cells[1, 2].Value = "Value";
            sheet.Cells[2, 1].Value = "Enabled"; sheet.Cells[2, 2].Value = 1;
            sheet.Cells[3, 1].Value = "PairCount"; sheet.Cells[3, 2].Value = 2;
            sheet.Cells[4, 1].Value = "ProtectTypes"; sheet.Cells[4, 2].Value = 4;
            sheet.Cells[5, 1].Value = "HydroHP"; sheet.Cells[5, 2].Value = .25f;
            env.DataManager.TeamBonusRules.Resonance = TeamBonusTableLoader.Read(sheet);
            env.DataManager.WeaponAttributesDict[weapon] = new WeaponAttributesData
            { WeaponID = weapon, WeaponType = weapon == 3196 ? 3 : 4, BaseATK = 0f };
            env.DataManager.WeaponParamDict[weapon] = new Dictionary<int, WeaponParamData>();
            foreach (var pair in new[] { (1, .2f), (2, .008f), (3, .01f) })
                env.DataManager.WeaponParamDict[weapon][pair.Item1] = new WeaponParamData { WeaponID = weapon, Index = pair.Item1, Param = pair.Item2 };
            int hpID = weapon == 3196 ? 4319601 : 4321701;
            var hp = Main(env, hpID, "HPBonus", true); Effect(env, hpID*100+1, "Buff", "Index(1)");
            env.DataManager.WeaponInitiateStatusDict[weapon] = new List<string> { hp.StatusID2 };
            if (weapon == 3196)
            {
                foreach (int id in new[] { 4319602, 4319603 })
                {
                    var main = Main(env, id, "ATKBonusFlat", true);
                    var effect = Effect(env, id*100+1, "Buff", "Index("+(id == 4319602 ? 2 : 3)+")");
                    effect.DamageHook = "Hook("+effect.StatusEffectID+")";
                    if (id == 4319603) main.ScriptHook = "Hook(4319603)";
                    env.DataManager.WeaponInitiateStatusDict[weapon].Add(main.StatusID2);
                }
            }
            Main(env, 453401, "HPBonus"); Effect(env, 45340101, "Buff", ".2");
            Main(env, 451001, "HPBonusFlat"); Effect(env, 45100101, "Buff", "1000");
            Main(env, 451002, null); var shared = Main(env, 451003, "HPBonusFlat"); shared.MaxStack = 0;
            Effect(env, 45100301, "Buff", "200");
            var bind = Effect(env, 45100201, "BindStatus", shared.StatusID2);
            bind.ScriptHook = "Hook(45100201)"; bind.TargetType = "Allies"; bind.TargetSelect = "-1";
            env.DataManager.ArtifactInitiateStatusDict[534] = new Dictionary<int, List<string>> { [2] = new List<string> { "ST_Artifact_453401" } };
            env.DataManager.ArtifactInitiateStatusDict[510] = new Dictionary<int, List<string>>
            { [2] = new List<string> { "ST_Artifact_451001" }, [4] = new List<string> { "ST_Artifact_451002" } };
        }

        private static StatusMainData Main(BattleFlowTestEnv env, int id, string part, bool weapon = false)
        {
            var main = new StatusMainData { StatusID = id, StatusID2 = (weapon ? "ST_Weapon_" : "ST_Artifact_")+id,
                StatusType = "Buff", MultiplierPart1 = part, MaxStack = 1, MaxCount = 1 };
            env.DataManager.StatusMainDict[main.StatusID2] = main; return main;
        }
        private static StatusEffectData Effect(BattleFlowTestEnv env, int id, string type, string formula)
        {
            var effect = new StatusEffectData { StatusEffectID = id, StatusEffectID2 = "STE_InitialAttributes_"+id,
                EffectType = type, Param1 = formula, TargetType = "Self" };
            env.DataManager.StatusEffectDict[effect.StatusEffectID2] = effect; return effect;
        }
    }
}
