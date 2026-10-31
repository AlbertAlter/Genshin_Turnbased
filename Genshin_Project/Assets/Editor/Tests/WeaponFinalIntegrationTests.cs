using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    /// <summary>剩余武器的关键公共入口回归定义；只编写，不自动运行Unity。</summary>
    public sealed class WeaponFinalIntegrationTests
    {
        private static string Key(int id) => DataManager.WeaponStatusKey(id);
        private static string Eff(int id) => "STE_Weapon_" + id;
        private static StatusMainData Main(BattleFlowTestEnv env, int id, string part = null, bool count = false)
        {
            var main = new StatusMainData { StatusID = id, StatusID2 = Key(id), StatusType = count ? "Count" : "Buff",
                MultiplierPart = part, MaxStack = 1, MaxCount = 1, WhenMax = "0" };
            env.DataManager.StatusMainDict[Key(id)] = main; return main;
        }
        private static StatusEffectData Effect(BattleFlowTestEnv env, int id, string type, string formula, bool hook = false)
        {
            var effect = new StatusEffectData { StatusEffectID = id, StatusEffectID2 = Eff(id), EffectType = type,
                Param1 = formula, TargetType = "Self", DamageHook = hook ? "Hook(" + id + ")" : null };
            env.DataManager.StatusEffectDict[Eff(id)] = effect; return effect;
        }
        private static StatusActionData Action(BattleFlowTestEnv env, int id, params int[] effects)
        {
            if (!env.DataManager.StatusActionDict.TryGetValue(Key(id), out var actions)) env.DataManager.StatusActionDict[Key(id)] = actions = new List<StatusActionData>();
            var action = new StatusActionData { StatusID = id, StatusID2 = Key(id), ActionType = "OnTrigger", Param1 = string.Join(";", effects.Select(Eff)) };
            actions.Add(action); return action;
        }
        private static WeaponRuntimeContext Weapon(BattleEntity owner, int id, params float[] parameters)
        {
            var context = new WeaponRuntimeContext { Equipper = owner, WeaponID = id, Refinement = 1,
                SourceInstanceID = "FinalWeapon:" + id + ":" + owner.SlotPosition };
            for (int i = 0; i < parameters.Length; i++) context.ResolvedParams[i + 1] = parameters[i]; return context;
        }
        private static StatusInstance Add(BattleFlowTestEnv env, BattleEntity owner, int id, WeaponRuntimeContext context)
            => owner.AddStatus(Key(id), owner, 0, 2, 0, env.DataManager.StatusMainDict[Key(id)], context);
        private static void Start(BattleFlowTestEnv env)
        { BattleRandom.SetSource(new SequenceBattleRandomSource()); env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction); }
        private static void Next(BattleFlowTestEnv env)
        { env.Manager.EndAllyTurn(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction); }
        private static float DamageBonus(BattleEntity owner, string type, string element, BattleEntity target)
        { using (DamageQueryContext.Enter("query", type)) return owner.GetStatusDMGBonus("query", "", -1, element, target); }

        [TestCase(1000f, 0f, 0f, 1200f)]
        [TestCase(550f, 0f, 0f, 550f)]
        [TestCase(1000f, .1f, 0f, 1300f)]
        [TestCase(1100f, .1f, .3f, 1100f)]
        [TestCase(1300f, .1f, .3f, 1600f)]
        public void EquipWeapon_InitialPassiveIsPermanent_AndHPIsCommittedOnlyAfterFullBattleSetup(
            float currentHP, float secondaryHPBonus, float otherHPBonus, float expectedHP)
        {
            using var env = new BattleFlowTestEnv();
            env.DataManager.CharacterAttributesDict[BattleFlowTestEnv.AllyID].WeaponType = 3;
            env.DataManager.WeaponAttributesDict[3196] = new WeaponAttributesData
            { WeaponID = 3196, WeaponType = 3, BaseATK = 40f,
                SecondaryAttribute = "HPBonus", BaseSecondaryAttribute = secondaryHPBonus };
            env.DataManager.WeaponParamDict[3196] = new Dictionary<int, WeaponParamData>
            { [1] = new WeaponParamData { WeaponID = 3196, Index = 1, Param = .2f } };
            env.DataManager.WeaponInitiateStatusDict[3196] = new List<string> { Key(4319601) };
            Main(env, 4319601, "HPBonus"); Effect(env, 431960101, "Buff", "Index(1)");
            Main(env, 990620, "HPBonus"); Effect(env, 99062001, "Buff", otherHPBonus.ToString(System.Globalization.CultureInfo.InvariantCulture));
            env.CreateMinimalBattle(); var owner = env.Ally.Entity;
            owner.AddStatus(Key(990620), owner, 0, 2, 0, env.DataManager.StatusMainDict[Key(990620)]);
            owner.CurrentHP = currentHP; int heals = 0, changes = 0;
            using var heal = BattleEventHub.Subscribe<HealEventData>(BattleEventKind.HealResolved, (_, fact) => heals++, 0);
            using var changed = BattleEventHub.Subscribe<HPChangedEventData>(BattleEventKind.HPChanged, (_, fact) => changes++, 0);
            Assert.That(env.Ally.EquipWeapon(new WeaponLoadout { WeaponID = 3196 }), Is.True);
            var passive = owner.GetStatus(Key(4319601));
            Assert.That(passive.IsPermanent, Is.True); Assert.That(passive.RemainingPhaseCount, Is.Zero);
            Assert.That(owner.TotalHP, Is.EqualTo(1000f+1000f*secondaryHPBonus).Within(.001f));
            Assert.That(owner.GetFinalHP(), Is.EqualTo(1200f+1000f*(secondaryHPBonus+otherHPBonus)).Within(.001f));
            Assert.That(owner.CurrentHP, Is.EqualTo(currentHP), "装备登记不逐项同步当前血量");
            Assert.That(BattleAttributeInitialization.IsComplete(owner), Is.False);
            env.Manager.StartBattle();
            Assert.That(owner.CurrentHP, Is.EqualTo(expectedHP).Within(.001f));
            Assert.That(BattleAttributeInitialization.IsComplete(owner), Is.True);
            Assert.That(heals, Is.Zero); Assert.That(changes, Is.Zero);
        }

        [TestCase(1000f, 0f, 1450f, 1200f)]
        [TestCase(1150f, 0f, 1150f, 1150f)]
        [TestCase(1000f, .2f, 1650f, 1400f)]
        [TestCase(1150f, .2f, 1150f, 1150f)]
        public void HydroResonance_DoesNotDropOrFillWoundedHPFromExistingWeaponLifeBonus(
            float before, float initialArtifactHPBonus, float afterStart, float afterDispose)
        {
            using var env = new BattleFlowTestEnv();
            using var parameters = new OfficeOpenXml.ExcelPackage();
            var sheet = parameters.Workbook.Worksheets.Add("Resonance");
            sheet.Cells[1, 1].Value = "Key"; sheet.Cells[1, 2].Value = "Value";
            sheet.Cells[2, 1].Value = "Enabled"; sheet.Cells[2, 2].Value = 1;
            sheet.Cells[3, 1].Value = "PairCount"; sheet.Cells[3, 2].Value = 2;
            sheet.Cells[4, 1].Value = "ProtectTypes"; sheet.Cells[4, 2].Value = 4;
            sheet.Cells[5, 1].Value = "HydroHP"; sheet.Cells[5, 2].Value = .25f;
            env.DataManager.TeamBonusRules.Resonance = TeamBonusTableLoader.Read(sheet);
            env.DataManager.CharacterAttributesDict[BattleFlowTestEnv.AllyID].Element = "Hydro";
            Main(env, 4319601, "HPBonus"); Effect(env, 431960101, "Buff", "Index(1)");
            env.CreateMinimalBattle(); env.CreateAlly(2, false); var owner = env.Ally.Entity;
            Add(env, owner, 4319601, Weapon(owner, 3196, .2f)); owner.CurrentHP = before;
            if (initialArtifactHPBonus > 0f)
            {
                var artifact = new StatusMainData { StatusID = 453401, StatusID2 = "ST_Artifact_453401",
                    StatusType = "Buff", MultiplierPart = "HPBonus", MaxStack = 1, MaxCount = 1 };
                env.DataManager.StatusMainDict[artifact.StatusID2] = artifact;
                env.DataManager.StatusEffectDict["STE_Artifact_45340101"] = new StatusEffectData
                { StatusEffectID = 45340101, EffectType = "Buff", Param1 = initialArtifactHPBonus.ToString(System.Globalization.CultureInfo.InvariantCulture) };
                owner.AddStatus(artifact.StatusID2, owner, 0, 2, 0, artifact);
            }
            int heals = 0, changes = 0;
            using var heal = BattleEventHub.Subscribe<HealEventData>(BattleEventKind.HealResolved, (_, fact) => heals++, 0);
            using var changed = BattleEventHub.Subscribe<HPChangedEventData>(BattleEventKind.HPChanged, (_, fact) => changes++, 0);
            env.Manager.StartBattle();
            Assert.That(owner.TotalHP, Is.EqualTo(1250f + 1000f*initialArtifactHPBonus).Within(.001f));
            Assert.That(owner.GetFinalHP(), Is.EqualTo(1450f + 1000f*initialArtifactHPBonus).Within(.001f));
            Assert.That(owner.CurrentHP, Is.EqualTo(afterStart).Within(.001f));
            env.Manager.TeamBonuses.Dispose();
            Assert.That(owner.TotalHP, Is.EqualTo(1000f + 1000f*initialArtifactHPBonus).Within(.001f));
            Assert.That(owner.GetFinalHP(), Is.EqualTo(1200f + 1000f*initialArtifactHPBonus).Within(.001f));
            Assert.That(owner.CurrentHP, Is.EqualTo(afterDispose).Within(.001f));
            Assert.That(heals, Is.Zero); Assert.That(changes, Is.Zero,
                "开战构筑及队伍释放不冒充治疗或生命变化事件");
        }

        [Test, Timeout(10000)]
        public void Absolution_OnlyActualOwnBondIncrease_AddsAndRefreshes_QueriesNeverAward()
        {
            using var env = new BattleFlowTestEnv(); Main(env, 4316501, "CritDMG"); Main(env, 4316502); Main(env, 4316503, "DMGBonus", true);
            Effect(env, 431650101, "Buff", "Index(1)"); Effect(env, 431650301, "Buff", "Index(2)*Count(ST_Weapon_4316503)");
            Action(env, 4316502, 431650201).ScriptHook = "ResourceChanged(Equipper);Hook(4316502)";
            Effect(env, 431650201, "ApplyStatus", Key(4316503)).Duration = 2;
            env.CreateMinimalBattle(); var owner = env.Ally.Entity; var other = env.CreateAlly(2).Entity;
            var weapon = Weapon(owner, 3165, .2f, .16f); Add(env, owner, 4316501, weapon); Add(env, owner, 4316502, weapon); Start(env);
            Assert.That(owner.HasStatus(Key(4316503)), Is.False); BondOfLifeStatusHandler.AddValue(other, 10f);
            Assert.That(owner.HasStatus(Key(4316503)), Is.False);
            for (int i = 0; i < 3; i++) BondOfLifeStatusHandler.AddValue(owner, 1.25f);
            var count = owner.GetStatus(Key(4316503)); Assert.That(count.RuntimeValue, Is.EqualTo(3f));
            Assert.That(DamageBonus(owner, "Skill", "Pyro", env.Enemy.Entity), Is.EqualTo(.48f).Within(.0001f));
            Assert.That(owner.GetStatusCritDMG("", "", -1, "", null), Is.EqualTo(.2f).Within(.0001f));
            Next(env); Assert.That(count.RemainingPhaseCount, Is.EqualTo(1));
            BondOfLifeStatusHandler.AddValue(owner, 0f); BondOfLifeStatusHandler.ReduceValue(owner, 1f);
            Assert.That(count.RemainingPhaseCount, Is.EqualTo(1)); BondOfLifeStatusHandler.AddValue(owner, .25f);
            Assert.That(count.RemainingPhaseCount, Is.EqualTo(2)); Assert.That(count.StackCount, Is.EqualTo(1));
        }

        [Test, Timeout(10000)]
        public void RedhornAndStatusCriticalDamage_AreInOriginalHitFormula_NotSeparateDamage_AndDoNotLeakToReaction()
        {
            using var env = new BattleFlowTestEnv(); Main(env, 4317401, "DEFBonus"); Main(env, 4317402, "BaseDMGBonusFlat").ApplyDamageType = "Normal;Heavy";
            Effect(env, 431740101, "Buff", "Index(1)"); Effect(env, 431740201, "Buff", "Index(2)", true);
            Main(env, 990610, "CritDMG"); Effect(env, 99061001, "Buff", ".4");
            var lunar = Main(env, 990611, "CritDMG"); lunar.ApplyReactionType = "月结晶"; Effect(env, 99061101, "Buff", ".9");
            env.ConfigureAllyAction(0, "SK_Redhorn", "SE_Redhorn", "Physical", ".1*TotalATK,0", 0);
            env.DataManager.SkillLevelDict[$"{BattleFlowTestEnv.AllyID}_0"][1].Hits2 = ".1*TotalATK,0";
            env.CreateMinimalBattle(); var owner = env.Ally.Entity; env.CreateEnemy(2);
            var weapon = Weapon(owner, 3174, .28f, .4f); Add(env, owner, 4317401, weapon); Add(env, owner, 4317402, weapon);
            owner.AddStatus(Key(990610), owner, 0, 2, 0, env.DataManager.StatusMainDict[Key(990610)]);
            owner.AddStatus(Key(990611), owner, 0, 2, 0, lunar); Start(env); owner.CritRate = 1f;
            env.Ally.ForcedTargetPositions.AddRange(new[] { 1, 2 }); var hits = new List<DamageEventData>();
            using var events = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, fact) => hits.Add(fact), 0);
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True); Assert.That(hits, Has.Count.EqualTo(4));
            Assert.That(hits.Select(x => x.Source.SourceEffectID), Is.All.EqualTo("SE_Redhorn"));
            foreach (var hit in hits)
            {
                Assert.That(hit.Critical.Value.DamageBeforeCritical, Is.EqualTo(30.6f).Within(.001f)); // (10+100*1.28*.4)*.5
                Assert.That(hit.Critical.Value.Multiplier, Is.EqualTo(1.4f).Within(.0001f));
                Assert.That(hit.ActualHPDamage, Is.EqualTo(42.84f).Within(.001f));
            }
            Assert.That(random.FloatCallCount, Is.EqualTo(4)); Assert.That(random.IntCallCount, Is.Zero);
            Assert.That(owner.GetStatusCritDMG("", "", -1, "Geo", null, ReactionType.LunarCrystallize), Is.EqualTo(1.3f).Within(.0001f));
            Assert.That(ReactionDamageCalculator.CollectReactionBonuses(owner, ReactionType.Overloaded, "Pyro").BaseDMGBonusFlat, Is.Zero);
            using (DamageQueryContext.Enter("q", "Skill")) Assert.That(owner.GetStatusBaseDMGBonusFlat("q", "", 0, "Geo", env.Enemy.Entity), Is.Zero);
        }

        [Test, Timeout(10000)]
        public void WolfsGravestone_UsesEachTargetsPreHitHP_ExactlyThirtyFails_LaterLowTargetCanTriggerEvenWhenKilled()
        {
            using var env = new BattleFlowTestEnv(); Main(env, 4317001, "ATKBonus"); Main(env, 4317002); Main(env, 4317003, "ATKBonus");
            Effect(env, 431700101, "Buff", "Index(1)"); Effect(env, 431700301, "Buff", "Index(2)");
            var action = Action(env, 4317002, 431700201); action.OutgoingHit = "All"; action.ScriptHook = "Hook(4317002)"; action.Cooldown = 7;
            var apply = Effect(env, 431700201, "ApplyStatus", Key(4317003)); apply.Duration = 3; apply.TargetType = "Allies"; apply.TargetSelect = "-1";
            env.ConfigureAllyAction(0, "SK_Wolf", "SE_Wolf", "Physical", ".1*TotalATK,0", 0); env.CreateMinimalBattle();
            var owner = env.Ally.Entity; var second = env.CreateEnemy(2).Entity; var weapon = Weapon(owner, 3170, .2f, .4f);
            Add(env, owner, 4317001, weapon); var listen = Add(env, owner, 4317002, weapon); Start(env);
            env.Enemy.Entity.CurrentHP = 300f; Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True);
            Assert.That(owner.HasStatus(Key(4317003)), Is.False, "命中后294不能反推为命中前已低于30%");
            env.Enemy.Entity.CurrentHP = 1000f; second.CurrentHP = 1f; env.Ally.ForcedTargetPositions.AddRange(new[] { 1, 2 });
            Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True); Assert.That(second.IsDead, Is.True);
            Assert.That(owner.GetStatus(Key(4317003)), Is.Not.Null); Assert.That(listen.LifeTriggerCount, Is.EqualTo(1));
            Assert.That(owner.GetStatusATKBonus("", "", -1, ""), Is.EqualTo(.6f).Within(.0001f));
        }

        [Test, Timeout(10000)]
        public void SplendorResourceWindows_UseFractionalEnergyAndLiveLifeMasteryRecharge_ThresholdsAreStrict()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle(); var owner = env.Ally.Entity;
            Main(env, 4319601, "HPBonus"); Main(env, 4319602, "ATKBonusFlat"); Main(env, 4319603, "ATKBonusFlat").ScriptHook = "Hook(4319603)";
            Effect(env, 431960101, "Buff", "Index(1)"); Effect(env, 431960201, "Buff", "Index(2)", true); Effect(env, 431960301, "Buff", "Index(3)", true);
            var homa = Weapon(owner, 3196, .2f, .008f, .01f); for (int id = 4319601; id <= 4319603; id++) Add(env, owner, id, homa);
            Main(env, 4319701, "ATKBonus"); Effect(env, 431970101, "Buff", "Index(1)", true); Add(env, owner, 4319701, Weapon(owner, 3197, .28f, .8f));
            Main(env, 4320002, "DMGBonus").ScriptHook = "Hook(4320002)"; Main(env, 4320003, "DMGBonus").ScriptHook = "Hook(4320003)";
            Effect(env, 432000201, "Buff", "Index(1)"); Effect(env, 432000301, "Buff", "Index(2)");
            var crimson = Weapon(owner, 3200, .12f, .24f); Add(env, owner, 4320002, crimson); Add(env, owner, 4320003, crimson); Start(env);
            owner.CurrentHP = 600f; Assert.That(owner.GetStatusATKBonusFlat("", "", -1, ""), Is.EqualTo(9.6f).Within(.001f));
            owner.CurrentHP = 599.5f; Assert.That(owner.GetStatusATKBonusFlat("", "", -1, ""), Is.EqualTo(21.6f).Within(.001f));
            owner.TotalRechargeRate = .9f; Assert.That(owner.GetStatusATKBonus("", "", -1, ""), Is.Zero);
            owner.TotalRechargeRate = 2.5f; Assert.That(owner.GetStatusATKBonus("", "", -1, ""), Is.EqualTo(.42f).Within(.0001f));
            owner.TotalRechargeRate = 10f; Assert.That(owner.GetStatusATKBonus("", "", -1, ""), Is.EqualTo(.8f).Within(.0001f));
            BondOfLifeStatusHandler.AddValue(owner, 360f); Assert.That(DamageBonus(owner, "Normal", "Pyro", env.Enemy.Entity), Is.EqualTo(.36f).Within(.0001f));
            BondOfLifeStatusHandler.ReduceValue(owner, .5f); Assert.That(DamageBonus(owner, "Normal", "Pyro", env.Enemy.Entity), Is.EqualTo(.12f).Within(.0001f));
        }

        [Test, Timeout(10000)]
        public void Frostbranch_ExplicitLunarCrystallizeSkillDamageQualifies_PublicNonAttackConcertoDoesNot()
        {
            using var env = new BattleFlowTestEnv(); Main(env, 4319202); Main(env, 4319203, "DMGBonus").ApplyElementType = "Geo";
            Action(env, 4319202, 431920201).ScriptHook = "Hook(4319202)"; env.DataManager.StatusActionDict[Key(4319202)][0].MaxTimePerHit = 1;
            Effect(env, 431920201, "ApplyStatus", Key(4319203)).Duration = 2; Effect(env, 431920301, "Buff", "Index(2)");
            env.ConfigureAllyAction(0, "SK_Frost", "SE_Frost", "Geo", ".1*TotalATK,0", 0); env.DataManager.SkillEffectDict["SE_Frost"].Param3 = "lunar";
            env.CreateMinimalBattle(); var owner = env.Ally.Entity; var weapon = Weapon(owner, 3192, .28f, .48f); Add(env, owner, 4319202, weapon); Start(env);
            env.Enemy.Entity.TakeDamage(1f, DamageSourceInfo.Create(owner, ReactionSourceKind.DerivedReaction, "", "MoonCage", ReactionType.LunarCrystallize, false));
            Assert.That(owner.HasStatus(Key(4319203)), Is.False);
            Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True); Assert.That(owner.HasStatus(Key(4319203)), Is.True,
                "倍率/Action为Normal，但Damage的正式月结晶身份满足OR另一分支");
        }

        [Test, Timeout(10000)]
        public void Tome_AwardsEnergyOnlyAfterThirdRealGain_AtCapRefreshesDamageWithoutBypassingEnergyCooldown()
        {
            using var env = new BattleFlowTestEnv(); Main(env, 4321402); Main(env, 4321403, "DMGBonus", true).ApplyDamageType = "Heavy";
            var gain = Action(env, 4321402, 432140201); gain.ScriptHook = "Hook(4321402)"; gain.MaxTimePerEffect = 1;
            var award = Action(env, 4321402, 432140202); award.ScriptHook = "Hook(432140202)"; award.Cooldown = 3;
            Effect(env, 432140201, "ApplyStatus", Key(4321403)).Duration = 1;
            Effect(env, 432140301, "Buff", "Index(2)*Count(ST_Weapon_4321403)"); var energy = Effect(env, 432140202, "GainEnergy", "Index(3)", true); energy.Param2 = "Flat";
            env.CreateMinimalBattle(); var owner = env.Ally.Entity; Add(env, owner, 4321402, Weapon(owner, 3214, .16f, .14f, 8f)); Start(env);
            owner.CurrentHP = 500f; owner.CurrentEnergy = 0f;
            owner.Heal(1f, owner); owner.Heal(1f, owner); Assert.That(owner.CurrentEnergy, Is.Zero);
            owner.Heal(1f, owner); var count = owner.GetStatus(Key(4321403)); Assert.That(count.RuntimeValue, Is.EqualTo(3f)); Assert.That(owner.CurrentEnergy, Is.EqualTo(8f));
            owner.Heal(1f, owner); Assert.That(owner.CurrentEnergy, Is.EqualTo(8f)); Assert.That(count.RuntimeValue, Is.EqualTo(3f));
            for (int i = 0; i < 3; i++) Assert.That(DamageBonus(owner, "Heavy", "Hydro", env.Enemy.Entity), Is.EqualTo(.42f).Within(.0001f));
            Assert.That(owner.CurrentEnergy, Is.EqualTo(8f)); Assert.That(DamageBonus(owner, "Normal", "Hydro", env.Enemy.Entity), Is.Zero);
            for (int i = 0; i < 3; i++) Next(env); owner.Heal(1f, owner); owner.Heal(1f, owner); owner.Heal(1f, owner);
            Assert.That(owner.CurrentEnergy, Is.EqualTo(16f));
        }

        [Test, Timeout(10000)]
        public void Lumidouce_BurningAndOldBurningDendroDamageShareOneHitQuota_AwardHasSeparateCooldown()
        {
            using var env = new BattleFlowTestEnv(); Main(env, 4320102); Main(env, 4320103, "DMGBonus", true);
            var gain = Action(env, 4320102, 432010201); gain.ScriptHook = "Hook(4320102)"; gain.MaxTimePerHit = 1;
            var award = Action(env, 4320102, 432010202); award.ScriptHook = "Hook(432010202)"; award.Cooldown = 3;
            Effect(env, 432010201, "ApplyStatus", Key(4320103)).Duration = 2;
            Effect(env, 432010301, "Buff", "Index(2)*Count(ST_Weapon_4320103)"); var energy = Effect(env, 432010202, "GainEnergy", "Index(3)", true); energy.Param2 = "Flat";
            env.ConfigureAllyAction(2, "SK_Lumidouce", "SE_Lumidouce", "Dendro", ".01*TotalATK,1", 0);
            env.CreateMinimalBattle(); var owner = env.Ally.Entity; Add(env, owner, 4320102, Weapon(owner, 3201, .15f, .18f, 12f)); Start(env);
            env.Enemy.Entity.TotalHP = env.Enemy.Entity.CurrentHP = 10000f; owner.CurrentEnergy = 0f;
            env.Enemy.Entity.ApplyAura("Pyro", 1f, owner.EntityID); Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
            var count = owner.GetStatus(Key(4320103)); Assert.That(count.RuntimeValue, Is.EqualTo(1f)); Assert.That(owner.CurrentEnergy, Is.Zero);
            Assert.That(env.Manager.UseSkillBySlot(0), Is.True); Assert.That(count.RuntimeValue, Is.EqualTo(2f)); Assert.That(owner.CurrentEnergy, Is.EqualTo(12f));
            Assert.That(env.Manager.UseSkillBySlot(0), Is.True); Assert.That(count.RuntimeValue, Is.EqualTo(2f)); Assert.That(owner.CurrentEnergy, Is.EqualTo(12f));
        }

        [Test, Timeout(10000)]
        public void ShieldCreator_IsNotReceiver_OnePartyGrantBatchTriggersOnce_AndTeamWindowOutlivesAttackWindow()
        {
            using var env = new BattleFlowTestEnv(); Main(env, 4320302); Main(env, 4320303, "LunarDMGBonus").ApplyReactionType = "月感电";
            Action(env, 4320302, 432030202).ScriptHook = "Hook(4320302)";
            var apply = Effect(env, 432030202, "ApplyStatus", Key(4320303)); apply.Duration = 5; apply.TargetType = "Allies"; apply.TargetSelect = "-1";
            Effect(env, 432030301, "Buff", "Index(2)"); env.CreateMinimalBattle(); var owner = env.Ally.Entity; var other = env.CreateAlly(2).Entity;
            var weapon = Weapon(owner, 3203, .2f, .3f); Add(env, owner, 4320302, weapon); Start(env); int added = 0;
            using var observe = BattleEventHub.Subscribe<StatusEventData>(BattleEventKind.StatusAdded, (_, fact) => { if (fact.StatusId == Key(4320303)) added++; }, 0);
            owner.AddShield(10f, "", 5f, creator: other); Assert.That(added, Is.Zero, "自己收到别人盾不是自己创造");
            CrystallizeShieldSystem.ApplyPartyShield(env.Manager, "Pyro", 10f, 2, owner);
            Assert.That(added, Is.EqualTo(2), "同一次全队授盾只派发一次武器行动，再各给两名队员一份");
            var reward = other.GetStatus(Key(4320303)); owner.RemoveStatus(Key(4320302)); Assert.That(other.GetStatus(Key(4320303)), Is.SameAs(reward));
            Assert.That(ReactionDamageCalculator.CollectReactionBonuses(other, ReactionType.LunarElectroCharged, "Electro").LunarDMGBonus, Is.EqualTo(.3f).Within(.0001f));
            Assert.That(ReactionDamageCalculator.CollectReactionBonuses(other, ReactionType.ElectroCharged, "Electro").LunarDMGBonus, Is.Zero);
        }

        [Test, Timeout(10000)]
        public void StargazerAndSevenEdicts_TrackCurrentWearerTarget_NotDamageTarget_AndKeepCreationSnapshot()
        {
            using var env = new BattleFlowTestEnv(); Main(env, 4322401, "ATKBonus"); Main(env, 4322402); Main(env, 4322403);
            foreach (int id in new[] { 4322404, 4322405 }) { var main = Main(env, id, "DMGBonus"); main.MaxCount = main.MaxStack = 0; main.ScriptHook = "Hook(" + id + ")"; }
            Effect(env, 432240101, "Buff", "Index(1)"); var action = Action(env, 4322402, 432240201); action.ScriptHook = "Hook(4322402)";
            Effect(env, 432240201, "ApplyStatus", Key(4322403), true).Duration = 5;
            var full = Effect(env, 432240302, "BindStatus", Key(4322404)); full.TargetType = "Allies"; full.TargetSelect = "-1";
            var half = Effect(env, 432240303, "BindStatus", Key(4322405)); half.TargetType = "AlliesOnly"; half.TargetSelect = "-1";
            Effect(env, 432240401, "Buff", "min(Index(2)/1000*TotalATK,Index(3))", true);
            Effect(env, 432240501, "Buff", "0.5*min(Index(2)/1000*TotalATK,Index(3))", true);
            env.CreateMinimalBattle(); var owner = env.Ally.Entity; var other = env.CreateAlly(2).Entity;
            var weapon = Weapon(owner, 3224, .12f, .1f, .26f, 14f); Add(env, owner, 4322401, weapon); Add(env, owner, 4322402, weapon); Start(env);
            owner.TotalATK = 2000f; other.AddShield(10f, "", 5f, creator: owner);
            var parent = owner.GetStatus(Key(4322403)); Assert.That(parent.BoundStatuses, Has.Count.EqualTo(3));
            Assert.That(DamageBonus(owner, "Normal", "Pyro", env.Enemy.Entity), Is.EqualTo(.224f).Within(.0001f));
            Assert.That(DamageBonus(other, "Normal", "Pyro", env.Enemy.Entity), Is.Zero);
            owner.TotalATK = 3000f; Assert.That(env.Manager.TrySwitchActiveAlly(1), Is.True);
            Assert.That(DamageBonus(other, "Normal", "Pyro", env.Enemy.Entity), Is.EqualTo(.224f).Within(.0001f));
            Assert.That(DamageBonus(owner, "Normal", "Pyro", env.Enemy.Entity), Is.Zero);
            other.AddShield(10f, "", 5f, creator: owner); Assert.That(DamageBonus(other, "Normal", "Pyro", env.Enemy.Entity), Is.EqualTo(.26f).Within(.0001f));
            Assert.That(parent.IsActive, Is.False); owner.TakeDamage(10000f, DamageSourceInfo.CreateUnknown()); Assert.That(DamageBonus(other, "Normal", "Pyro", env.Enemy.Entity), Is.Zero);
        }

        [Test, Timeout(10000)]
        public void FragrantHealer_UsesThisHealsRecipientIncludingOverhealAndBond_SelfHealDeduplicates()
        {
            using var env = new BattleFlowTestEnv(); Main(env, 4320203); Main(env, 4320204, "ATKBonus");
            Action(env, 4320203, 432020301).WhenHealing = "All";
            var apply = Effect(env, 432020301, "ApplyStatus", Key(4320204)); apply.Duration = 1; apply.TargetType = "Allies"; apply.TargetSelect = "-1"; apply.TargetOverride = "Hook(432020301)";
            Effect(env, 432020401, "Buff", "Index(3)"); env.CreateMinimalBattle(); var owner = env.Ally.Entity;
            var second = env.CreateAlly(2).Entity; var untouched = env.CreateAlly(3).Entity;
            Add(env, owner, 4320203, Weapon(owner, 3202, .12f, .12f, .32f)); Start(env);
            second.Heal(10f, owner); Assert.That(owner.HasStatus(Key(4320204)), Is.True); Assert.That(second.HasStatus(Key(4320204)), Is.True);
            Assert.That(untouched.HasStatus(Key(4320204)), Is.False); owner.Heal(1f, owner);
            Assert.That(owner.GetStatusInstances(Key(4320204)), Has.Count.EqualTo(1)); Assert.That(owner.GetStatusATKBonus("", "", -1, ""), Is.EqualTo(.32f).Within(.0001f));
        }

        [Test, Timeout(10000)]
        public void BlackEclipse_BindingTargetReadsNewParent_NotOldBurstListener_AndExcludesWearer()
        {
            using var env = new BattleFlowTestEnv(); Main(env, 4316802); Main(env, 4316803, "ATKBonus");
            Main(env, 4316804, "ATKBonus"); Main(env, 4316805, "ATKBonus").ScriptHook = "TeamBonus(Hexerei)";
            Main(env, 4316806, "ATKBonus").ScriptHook = "TeamBonus(Hexerei)";
            Action(env, 4316802, 431680201).OutgoingHit = "Burst"; Effect(env, 431680201, "ApplyStatus", Key(4316803)).Duration = 1;
            Effect(env, 431680301, "Buff", "Index(2)"); Effect(env, 431680401, "Buff", "Index(3)");
            Effect(env, 431680501, "Buff", ".75*Index(2)"); Effect(env, 431680601, "Buff", ".75*Index(3)");
            var share = Effect(env, 431680302, "BindStatus", Key(4316804)); share.TargetType = "Allies"; share.TargetSelect = "-1"; share.TargetOverride = "Hook(431680302)";
            Effect(env, 431680303, "BindStatus", Key(4316805));
            var extra = Effect(env, 431680304, "BindStatus", Key(4316806)); extra.TargetType = "Allies"; extra.TargetSelect = "-1"; extra.TargetOverride = "Hook(431680302)";
            env.ConfigureAllyAction(3, "SK_Eclipse", "SE_Eclipse", "Physical", ".01*TotalATK,0", 0);
            env.CreateMinimalBattle(); var owner = env.Ally.Entity; var other = env.CreateAlly(2).Entity;
            Add(env, owner, 4316802, Weapon(owner, 3168, .2f, .2f, .16f)); Start(env);
            Assert.That(env.Manager.UseBurstBySlot(0), Is.True); var parent = owner.GetStatus(Key(4316803));
            Assert.That(parent.BoundStatuses, Has.Count.EqualTo(3)); Assert.That(owner.HasStatus(Key(4316804)), Is.False);
            Assert.That(owner.GetStatusATKBonus("", "", -1, ""), Is.EqualTo(.2f).Within(.0001f));
            Assert.That(other.GetStatusATKBonus("", "", -1, ""), Is.EqualTo(.16f).Within(.0001f));
        }

        [Test, Timeout(10000)]
        public void ThousandDreams_ExcludesSelfFromCounts_CompletesEarlyBindingsOnce_AndPreservesOtherSourcesOnDeath()
        {
            using var env = new BattleFlowTestEnv(); Main(env, 4321101); var share = Main(env, 4321102, "EMBonus"); share.MaxCount = share.MaxStack = 0;
            Main(env, 4321103, "EMBonus").ScriptHook = "Hook(4321103)"; Main(env, 4321104, "DMGBonus").ScriptHook = "Hook(4321104)";
            var bind = Effect(env, 432110101, "BindStatus", Key(4321102)); bind.TargetType = "AlliesOnly"; bind.TargetSelect = "-1";
            Effect(env, 432110201, "Buff", "Index(1)"); Effect(env, 432110301, "Buff", "Index(2)", true); Effect(env, 432110401, "Buff", "Index(3)", true);
            env.CreateMinimalBattle(); var owner = env.Ally.Entity;
            var first = Weapon(owner, 3211, 40f, 32f, .1f); Add(env, owner, 4321101, first); // 此刻编队只有本人，尚无受益者。
            var second = env.CreateAlly(2).Entity; var third = env.CreateAlly(3).Entity; var fourth = env.CreateAlly(4).Entity;
            var members = new[] { owner, second, third, fourth };
            for (int i = 0; i < members.Length; i++)
            {
                members[i].EntityID = 990100 + i;
                env.DataManager.CharacterOverviewList.Add(new CharacterOverviewData { CharacterID = members[i].EntityID,
                    Nation = "须弥", Element = i == 3 ? "Hydro" : "Dendro" });
            }
            Add(env, owner, 4321103, first); Add(env, owner, 4321104, first); Start(env);
            Assert.That(owner.GetStatusEMBonus(), Is.EqualTo(64f)); Assert.That(second.GetStatusEMBonus(), Is.EqualTo(40f));
            Assert.That(DamageBonus(owner, "Normal", "Dendro", env.Enemy.Entity), Is.EqualTo(.1f).Within(.0001f));
            Assert.That(DamageBonus(owner, "Normal", "Physical", env.Enemy.Entity), Is.Zero);
            StatusBindingSystem.ApplyBindings(owner.GetStatus(Key(4321101)), owner);
            Assert.That(second.GetStatusInstances(Key(4321102)), Has.Count.EqualTo(1));
            Add(env, second, 4321101, Weapon(second, 3211, 48f, 64f, .26f)); Assert.That(third.GetStatusEMBonus(), Is.EqualTo(88f));
            owner.TakeDamage(10000f, DamageSourceInfo.CreateUnknown()); Assert.That(third.GetStatusEMBonus(), Is.EqualTo(48f));
        }

        [TestCase(ReactionType.LunarElectroCharged), TestCase(ReactionType.LunarBloom), TestCase(ReactionType.LunarCrystallize), Timeout(10000)]
        public void Nocturne_LunarDamageRefreshesWithoutBypassingEnergyCooldown_AndCriticalDamageDoesNotLeak(ReactionType reaction)
        {
            using var env = new BattleFlowTestEnv(); Main(env, 4322302); Main(env, 4322303, "HPBonus");
            var critical = Main(env, 4322304, "CritDMG"); critical.ApplyReactionType = "月感电;月绽放;月结晶"; critical.ScriptHook = "Hook(4322304)";
            var wine = Action(env, 4322302, 432230201); wine.ScriptHook = "Hook(4322302)"; wine.MaxTimePerHit = 1;
            var energyAction = Action(env, 4322302, 432230202); energyAction.ScriptHook = "Hook(4322302)"; energyAction.Cooldown = 5;
            Effect(env, 432230201, "ApplyStatus", Key(4322303)).Duration = 3;
            var energy = Effect(env, 432230202, "GainEnergy", "Index(2)", true); energy.Param2 = "Flat";
            Effect(env, 432230301, "Buff", "Index(3)"); Effect(env, 432230302, "BindStatus", Key(4322304)); Effect(env, 432230401, "Buff", "Index(4)");
            env.CreateMinimalBattle(); var owner = env.Ally.Entity; Add(env, owner, 4322302, Weapon(owner, 3223, .1f, 14f, .14f, .6f)); Start(env); owner.CurrentEnergy = 0f;
            var source = DamageSourceInfo.Create(owner, ReactionSourceKind.DerivedReaction, "", "SE_Lunar_Nocturne", reaction, false);
            env.Enemy.Entity.TakeDamage(1f, source); var parent = owner.GetStatus(Key(4322303)); Assert.That(parent, Is.Not.Null); Assert.That(owner.CurrentEnergy, Is.EqualTo(14f));
            Assert.That(owner.GetStatusCritDMG("", "", -1, "Geo", env.Enemy.Entity, reaction), Is.EqualTo(.6f).Within(.0001f));
            Assert.That(owner.GetStatusCritDMG("", "", -1, "Geo", env.Enemy.Entity), Is.Zero);
            env.Enemy.Entity.TakeDamage(1f, source); Assert.That(owner.GetStatus(Key(4322303)), Is.Not.SameAs(parent)); Assert.That(owner.CurrentEnergy, Is.EqualTo(14f));
            var current = owner.GetStatus(Key(4322303)); env.Enemy.Entity.TakeDamage(1f, DamageSourceInfo.Create(owner, ReactionSourceKind.DerivedReaction, "", "SE_NonLunar", ReactionType.Overloaded, false));
            Assert.That(owner.GetStatus(Key(4322303)), Is.SameAs(current));
        }

        [Test, Timeout(10000)]
        public void VividNotions_WholeChargedExecutionKeepsBothBonusesForAllHitsThenClearsBoth()
        {
            using var env = new BattleFlowTestEnv(); Main(env, 4322002);
            Main(env, 4322003, "CritDMG").ApplyDamageType = "Heavy"; Main(env, 4322004, "CritDMG").ApplyDamageType = "Heavy";
            Action(env, 4322002, 432200201).OnAction = "Heavy"; Action(env, 4322002, 432200202).OnAction = "Skill;Burst";
            Action(env, 4322002, 432200203, 432200204).ScriptHook = "Hook(432200203)";
            Effect(env, 432200201, "ApplyStatus", Key(4322003)).Duration = 4; Effect(env, 432200202, "ApplyStatus", Key(4322004)).Duration = 4;
            Effect(env, 432200203, "RemoveStatus", Key(4322003)); Effect(env, 432200204, "RemoveStatus", Key(4322004));
            Effect(env, 432200301, "Buff", "Index(2)"); Effect(env, 432200401, "Buff", "Index(3)");
            env.ConfigureAllyAction(2, "SK_VividSkill", "SE_VividSkill", "Physical", ".01*TotalATK,0", 0);
            env.ConfigureAllyAction(1, "SK_VividHeavy", "SE_VividHeavy", "Physical", ".1*TotalATK,0", 0);
            env.DataManager.SkillLevelDict[$"{BattleFlowTestEnv.AllyID}_1"][1].Hits2 = ".1*TotalATK,0";
            env.CreateMinimalBattle(); var owner = env.Ally.Entity; env.CreateEnemy(2); Add(env, owner, 4322002, Weapon(owner, 3220, .28f, .28f, .4f)); Start(env); owner.CritRate = 1f;
            Assert.That(env.Manager.UseSkillBySlot(0), Is.True); Assert.That(owner.HasStatus(Key(4322004)), Is.True);
            var hits = new List<DamageEventData>(); using var events = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, fact) =>
            {
                if (fact.Source.SourceEffectID != "SE_VividHeavy") return;
                Assert.That(owner.HasStatus(Key(4322003)), Is.True); Assert.That(owner.HasStatus(Key(4322004)), Is.True); hits.Add(fact);
            }, 0);
            env.Ally.ForcedTargetPositions.AddRange(new[] { 1, 2 }); Assert.That(env.Manager.UseHeavyAttackBySlot(0), Is.True);
            Assert.That(hits, Has.Count.EqualTo(4)); foreach (var hit in hits) Assert.That(hit.Critical.Value.Multiplier, Is.EqualTo(1.68f).Within(.0001f));
            Assert.That(owner.HasStatus(Key(4322003)), Is.False); Assert.That(owner.HasStatus(Key(4322004)), Is.False);
        }
    }
}
