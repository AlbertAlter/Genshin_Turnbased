using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    /// <summary>仅内存构造配表；由用户在Unity中运行，不读取策划源表。</summary>
    public sealed class DamagePropertiesTests
    {
        [TestCase("physical", "Physical")]
        [TestCase("none", "None")]
        [TestCase("TRUE", "True")]
        public void DamageElement_IsExplicitAndCanonical(string raw, string expected)
            => Assert.That(DamageProperties.NormalizeElement(raw), Is.EqualTo(expected));

        [TestCase(null)] [TestCase("")] [TestCase("0")] [TestCase("All")]
        public void DamageElement_DoesNotDefaultMissingValuesToPhysical(string raw)
            => Assert.Throws<FormatException>(() => DamageProperties.Parse(raw, ""));

        [Test]
        public void None_RealSkillIgnoresDamageRegionsAndCrit_AbsorbsRawShield_LeavesEnemyGU()
        {
            using var env = new BattleFlowTestEnv();
            env.ConfigureAllyAction(2, "SK_None", "SE_None", "None", "2*ATK,1", 10, param1: "25");
            env.DataManager.SkillLevelDict[$"{BattleFlowTestEnv.AllyID}_2"][1].Hits2 = "0.5*ATK,1";
            env.CreateMinimalBattle();
            var actor = env.Ally.Entity; var target = env.Enemy.Entity;
            actor.CritRate = 1; actor.CritDMG = 9; actor.DMGBonus = 99;
            target.TotalDEF = 99999; target.PhysicalRes = .99f; target.ShieldStrength = 9;
            env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            target.AddShield(30, "Geo", 3, 9);
            target.AddShield(12, "Hydro", 3, kind: ShieldKind.EnemyElemental);
            target.ApplyAura("Hydro", 1, actor.EntityID);
            var main = new StatusMainData { StatusID = 459990, StatusID2 = "ST_NoneReduction", StatusType = "Buff",
                MultiplierPart = "DMGTaken", ScriptHook = "Chance(0.5)", MaxStack = 1 };
            env.DataManager.StatusMainDict[main.StatusID2] = main;
            env.DataManager.StatusEffectDict["STE_NoneReduction"] = new StatusEffectData
                { StatusEffectID = 45999001, StatusEffectID2 = "STE_NoneReduction", EffectType = "Buff", Param1 = "-1" };
            target.AddStatus(main.StatusID2, target, 3, 1, 0, main);
            Assert.That(target.GetTotalShieldHP("None"), Is.EqualTo(30));
            var facts = new List<DamageEventData>(); int contacts = 0;
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            using (BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, fact) => facts.Add(fact), 0))
            using (BattleEventHub.Subscribe<ElementContactEventData>(BattleEventKind.ElementContactResolved, (_, fact) => contacts++, 0))
                Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
            Assert.That(target.CurrentHP, Is.EqualTo(967.5f));
            Assert.That(target.GetEnemyElementalShieldHP(), Is.EqualTo(12));
            Assert.That(target.GetAura("Hydro").AuraAmount, Is.EqualTo(1));
            Assert.That(facts.Count, Is.EqualTo(2));
            Assert.That(facts[0].ShieldAbsorbed, Is.EqualTo(30));
            Assert.That(facts[0].ActualHPDamage, Is.EqualTo(20));
            Assert.That(facts[1].ActualHPDamage, Is.EqualTo(12.5f));
            foreach (var fact in facts)
            {
                Assert.That(fact.Element, Is.EqualTo("None")); Assert.That(fact.ElementAmount, Is.Zero);
                Assert.That(fact.Critical, Is.Null); Assert.That(fact.Source.ReactionType, Is.EqualTo(ReactionType.None));
                Assert.That(fact.DamageType, Is.EqualTo("Skill"));
            }
            Assert.That(contacts, Is.Zero); Assert.That(random.FloatCallCount, Is.Zero); Assert.That(random.IntCallCount, Is.Zero);
        }

        [Test]
        public void None_MultipleOrdinaryShieldsStillAbsorbInParallelAtOneToOne()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            var target = env.Ally.Entity;
            target.AddShield(60, "Geo", 3, 10); target.AddShield(20, "Pyro", 3, 10);
            Assert.That(target.AbsorbDirectDamageWithShield(100), Is.EqualTo(40));
            Assert.That(target.Shields, Is.Empty);
            Assert.That(EnchantStatusSystem.CanConvert("Normal", "None"), Is.False);
            Assert.That(EnchantStatusSystem.CanConvert("Normal", "Physical"), Is.True);
        }

        [TestCase("Dendro", "Lunar", ReactionType.LunarBloom)]
        [TestCase("Electro", "lunar;NightSoul", ReactionType.LunarElectroCharged)]
        [TestCase("Geo", "月曜;夜魂", ReactionType.LunarCrystallize)]
        [TestCase("Cryo", "Stellar", ReactionType.StellarSuperconduct)]
        [TestCase("Electro", "星烁;夜魂", ReactionType.StellarSuperconduct)]
        public void Properties_MapElementWithoutReplacingDamageType(string element, string tag, ReactionType reaction)
        {
            DamageProperties parsed = DamageProperties.Parse(element, tag);
            Assert.That(parsed.Reaction, Is.EqualTo(reaction));
            Assert.That(parsed.UsesSpecialFormula, Is.True);
        }

        [TestCase("Pyro", "Lunar")]
        [TestCase("Hydro", "Lunar")]
        [TestCase("Cryo", "Lunar")]
        [TestCase("Anemo", "Lunar")]
        [TestCase("Dendro", "Stellar")]
        [TestCase("Geo", "Stellar")]
        [TestCase("Hydro", "Stellar")]
        [TestCase("Pyro", "Stellar")]
        [TestCase("None", "NightSoul")]
        [TestCase("Physical", "NightSoul")]
        [TestCase("", "NightSoul")]
        [TestCase("True", "NightSoul")]
        [TestCase("True", "Lunar")]
        [TestCase("True", "Pneuma")]
        [TestCase("True", "Ousia")]
        [TestCase("Electro", "Lunar;Stellar")]
        [TestCase("Electro", "Lunar;")]
        [TestCase("Electro", "Typo")]
        public void Loader_RejectsInvalidElementOrTag(string element, string tag)
        {
            Assert.Throws<TableValidationException>(() => TableValidator.ValidateDamageProperties(
                "SkillsEffect", 7, "Damage", element, "M*ATK", "", tag));
        }

        [TestCase("Pyro")]
        [TestCase("Hydro")]
        [TestCase("Electro")]
        [TestCase("Cryo")]
        [TestCase("Anemo")]
        [TestCase("Dendro")]
        [TestCase("Geo")]
        public void NightSoul_AllSevenElementsAreLegal(string element)
        {
            var parsed = DamageProperties.Parse(element, "NightSoul");
            Assert.That(parsed.IsNightSoul, Is.True);
            Assert.That(parsed.UsesSpecialFormula, Is.False);
        }

        [TestCase("Pyro", "Pneuma", true, false, false, false)]
        [TestCase("Hydro", "荒", false, true, false, false)]
        [TestCase("None", "芒;荒", true, true, false, false)]
        [TestCase("Physical", " PNEUMA ; ousia ", true, true, false, false)]
        [TestCase("Electro", "Lunar;夜魂;芒;荒", true, true, true, false)]
        [TestCase("Cryo", "Stellar;Pneuma;Ousia", true, true, false, true)]
        public void ArkheProperties_CanCoexistAndMixWithoutChoosingAnotherFormula(string element, string tags,
            bool pneuma, bool ousia, bool lunar, bool stellar)
        {
            var parsed = DamageProperties.Parse(element, tags);
            Assert.That(parsed.IsPneuma, Is.EqualTo(pneuma)); Assert.That(parsed.IsOusia, Is.EqualTo(ousia));
            Assert.That(parsed.IsLunar, Is.EqualTo(lunar)); Assert.That(parsed.IsStellar, Is.EqualTo(stellar));
            Assert.That(parsed.UsesSpecialFormula, Is.EqualTo(lunar || stellar));
            Assert.DoesNotThrow(() => TableValidator.ValidateDamageProperties("Status_Effect", 2, "Damage", element, "25", "", tags));
            foreach (string invalid in new[] { "Pneuma;", "Pneuma;;Ousia", "Pneuma;Unknown" })
                Assert.Throws<TableValidationException>(() => TableValidator.ValidateDamageProperties("SkillsEffect", 2, "Damage", "Pyro", "25", "", invalid));
        }

        [Test, Timeout(7000)]
        public void ArkheProperties_FollowRealSkillHitAndSnapshot_ButDoNotInitializeOwnerOrLeakIntoDerivedReaction()
        {
            using var env = new BattleFlowTestEnv();
            env.ConfigureAllyAction(0, "SK_Arkhe", "SE_Arkhe", "Pyro", ".1*TotalATK,1", 0);
            var definition = env.DataManager.SkillEffectDict["SE_Arkhe"]; definition.Param3 = "NightSoul;芒;荒";
            env.CreateMinimalBattle(); env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            env.Enemy.Entity.ElementalAuras.Add(new ElementalAura { Element = "Electro", AuraAmount = 1f });
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            DamageEventData direct = null; var derived = new List<DamageEventData>();
            using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, fact) =>
            {
                if (fact.Source.SourceKind == ReactionSourceKind.DerivedReaction) derived.Add(fact);
                else if (fact.Source.SourceEffectID == "SE_Arkhe") direct = fact;
            }, 0);
            Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True);
            Assert.That(direct, Is.Not.Null); Assert.That(direct.IsPneumaDamage && direct.IsOusiaDamage, Is.True);
            Assert.That(direct.IsNightSoulDamage, Is.True); Assert.That(direct.DamageType, Is.EqualTo("Normal"));
            Assert.That(direct.ElementAmount, Is.EqualTo(1f), "芒荒标签不改原附着量");
            Assert.That(derived, Is.Not.Empty);
            foreach (var fact in derived)
            { Assert.That(fact.IsPneumaDamage || fact.IsOusiaDamage || fact.IsNightSoulDamage, Is.False); }
            definition.Param3 = "";
            Assert.That(direct.IsPneumaDamage && direct.IsOusiaDamage, Is.True, "声明保存本次值类型性质快照");
            Assert.That(ArkheStatuses.HasAny(env.Ally.Entity), Is.False, "伤害标签不代表角色能力标记");
            Assert.That(random.FloatCallCount, Is.EqualTo(1)); Assert.That(random.IntCallCount, Is.Zero);
        }

        [Test, Timeout(7000)]
        public void ArkheProperties_EnemySkillUsesSameParam3AndCommittedDamageInterface()
        {
            using var env = new BattleFlowTestEnv();
            env.ConfigureEnemyDamageSkill(.1f, "Hydro", 0f);
            env.DataManager.EnemySkillEffectDict["SE_Enemy_BattleFlow"].Param3 = "Pneuma;荒";
            env.CreateMinimalBattle(); env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            var random = new SequenceBattleRandomSource(intValues: new[] { 0 }); BattleRandom.SetSource(random);
            DamageEventData actual = null;
            using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, fact) =>
            { if (fact.Source.SourceEffectID == "SE_Enemy_BattleFlow") actual = fact; }, 0);
            env.Manager.EndAllyTurn(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.EnemyPostTurn);
            Assert.That(actual, Is.Not.Null); Assert.That(actual.IsPneumaDamage && actual.IsOusiaDamage, Is.True);
            Assert.That(actual.Properties.UsesSpecialFormula, Is.False); Assert.That(actual.DamageType, Is.EqualTo("Normal"));
            Assert.That(ArkheStatuses.HasAny(env.Enemy.Entity), Is.False);
            Assert.That(random.IntCallCount, Is.EqualTo(1));
        }

        [Test]
        public void StellarWind_ClassificationIsLegal_ButCannotUseOrdinaryOrSuperconductFormula()
        {
            var parsed = DamageProperties.Parse("Anemo", "Stellar;NightSoul");
            Assert.That(parsed.IsStellar, Is.True);
            Assert.That(parsed.IsNightSoul, Is.True);
            Assert.That(parsed.Reaction, Is.EqualTo(ReactionType.None));
            Assert.Throws<NotSupportedException>(() => parsed.RequireImplementedFormula());
        }

        [Test]
        public void SpecialFormula_UsesIndependentRegionsAndOnlyItsOwnFamily()
        {
            var bonuses = new ReactionBuffTotals
            { DMGBonus = 999f, BaseDMGBonus = .2f, BaseDMGBonusFlat = 10f, LunarDMGBonus = .4f, StellarDMGBonus = 88f };
            float damage = SpecialSkillDamageCalculator.CalculateBeforeCritical(100f, 6f, 0f, bonuses, true, .8f, .1f);
            Assert.That(damage, Is.EqualTo(895.84f).Within(.001f));
        }

        [Test]
        public void True_RejectsNegativeNonFiniteAndUnrelatedParameters_WithoutChangingOtherEffects()
        {
            foreach (string number in new[] { "-1", "NaN", "Infinity" })
                Assert.Throws<TableValidationException>(() => TableValidator.ValidateDamageProperties(
                    "Status_Effect", 4, "Damage", "true", number, "", ""));
            Assert.Throws<TableValidationException>(() => TableValidator.ValidateDamageProperties(
                "SkillsEffect", 2, "Damage", "True", "25", "Splash(1;1,1)", ""));
            Assert.Throws<TableValidationException>(() => TableValidator.ValidateDamageProperties(
                "SkillsEffect", 2, "Damage", "True", "25", "", "", "Hook(1)"));
            Assert.DoesNotThrow(() => TableValidator.ValidateDamageProperties(
                "SkillsEffect", 2, "ChangeControl", "", "", "", "1"));
        }

        [Test]
        public void SpecialFormula_CollectsElevationOnlyWithinItsConfiguredScope()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                var actor = env.Ally.Entity;
                actor.Elevation = .1f;
                var main = new StatusMainData
                {
                    StatusID = 459988, StatusID2 = "ST_SpecialElevation", StatusType = "Buff",
                    MultiplierPart = "Elevation", ApplyDamageType = "Burst", ApplyReactionType = "Lunar",
                    MaxStack = 1, MaxCount = 1
                };
                env.DataManager.StatusMainDict[main.StatusID2] = main;
                env.DataManager.StatusEffectDict["STE_SpecialElevation"] = new StatusEffectData
                { StatusEffectID = 45998801, StatusEffectID2 = "STE_SpecialElevation", EffectType = "Buff", Param1 = ".2" };
                actor.AddStatus(main.StatusID2, actor, 0, 1, 0, main);
                var properties = DamageProperties.Parse("Electro", "Lunar");
                var bonuses = ReactionDamageCalculator.CollectSpecialSkillBonuses(actor, env.Enemy.Entity,
                    properties, "Electro", "SE_Test", "SK_Test", "Burst");
                Assert.That(bonuses.Elevation, Is.EqualTo(.3f).Within(.0001f));
                bonuses = ReactionDamageCalculator.CollectSpecialSkillBonuses(actor, env.Enemy.Entity,
                    properties, "Electro", "SE_Test", "SK_Test", "Skill");
                Assert.That(bonuses.Elevation, Is.EqualTo(.1f));
            }
        }

        [Test]
        public void True_FromRealSkill_ReadsLevelMultipliers_ButNeverCritsReactsOrConsumesShield()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.ConfigureAllyAction(2, "SK_True", "SE_True", "True", "2*ATK,1", 10, param1: "25");
                env.DataManager.SkillLevelDict[$"{BattleFlowTestEnv.AllyID}_2"][1].Hits2 = "0.5*ATK,1";
                env.CreateMinimalBattle();
                env.Ally.Entity.CritRate = 1f;
                env.Ally.Entity.CritDMG = 9f;
                env.Ally.Entity.DMGBonus = 99f;
                env.Enemy.Entity.TotalDEF = 99999f;
                env.Enemy.Entity.PhysicalRes = .99f;
                env.Manager.StartBattle();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                env.Enemy.Entity.AddShield(500f, "Geo", 3f);
                var reduction = new StatusMainData
                {
                    StatusID = 459989, StatusID2 = "ST_TrueReduction", StatusType = "Buff",
                    MultiplierPart = "DMGTaken", ScriptHook = "Chance(0.5)", MaxStack = 1
                };
                env.DataManager.StatusMainDict[reduction.StatusID2] = reduction;
                env.DataManager.StatusEffectDict["STE_TrueReduction"] = new StatusEffectData
                { StatusEffectID = 45998901, StatusEffectID2 = "STE_TrueReduction", EffectType = "Buff", Param1 = "-1" };
                env.Enemy.Entity.AddStatus(reduction.StatusID2, env.Enemy.Entity, 3, 1, 0, reduction);
                float shieldBefore = env.Enemy.Entity.GetTotalShieldHP();
                var facts = new List<DamageEventData>();
                int contacts = 0;
                var random = new SequenceBattleRandomSource(new[] { .5f });
                BattleRandom.SetSource(random);
                using (BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (h, f) => facts.Add(f), 0))
                using (BattleEventHub.Subscribe<ElementContactEventData>(BattleEventKind.ElementContactResolved, (h, f) => contacts++, 0))
                    Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
                Assert.That(env.Enemy.Entity.CurrentHP, Is.EqualTo(937.5f));
                Assert.That(env.Enemy.Entity.GetTotalShieldHP(), Is.EqualTo(shieldBefore));
                Assert.That(facts.Count, Is.EqualTo(2));
                Assert.That(facts[0].ActualHPDamage, Is.EqualTo(50f));
                Assert.That(facts[1].ActualHPDamage, Is.EqualTo(12.5f));
                foreach (var fact in facts)
                {
                    Assert.That(fact.Critical, Is.Null);
                    Assert.That(fact.ShieldAbsorbed, Is.Zero);
                    Assert.That(fact.ElementAmount, Is.Zero);
                    Assert.That(fact.DamageType, Is.EqualTo("Skill"));
                    Assert.That(fact.IsNightSoulDamage, Is.False);
                }
                Assert.That(contacts, Is.Zero);
                Assert.That(random.FloatCallCount, Is.Zero);
                Assert.That(random.IntCallCount, Is.Zero);
            }
        }

        [TestCase("True")]
        [TestCase("None")]
        public void Fixed_WithoutSkillLevelUsesOneFixedHit_AndZeroDoesNotBecomeAttackDamage(string element)
        {
            foreach (string amount in new[] { "25.5", "0" })
                using (var env = new BattleFlowTestEnv())
                {
                    env.ConfigureAllyAction(2, "SK_True", "SE_True", element, "", 10, param1: amount);
                    env.CreateMinimalBattle();
                    env.Manager.StartBattle();
                    env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                    var random = new SequenceBattleRandomSource();
                    BattleRandom.SetSource(random);
                    Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
                    Assert.That(env.Enemy.Entity.CurrentHP, Is.EqualTo(amount == "0" ? 1000f : 974.5f));
                    Assert.That(random.FloatCallCount, Is.Zero);
                    Assert.That(random.IntCallCount, Is.Zero);
                }
        }

        [Test]
        public void LunarNightSoul_RealSkillUsesSpecialFormulaAndPublishesBothProperties()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.ConfigureAllyAction(2, "SK_Lunar", "SE_Lunar", "Electro", "1*ATK,0", 10, param1: "M*ATK");
                env.DataManager.SkillEffectDict["SE_Lunar"].Param3 = "Lunar;NightSoul";
                env.CreateMinimalBattle();
                env.Ally.Entity.DMGBonus = 99f;
                env.Manager.StartBattle();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                DamageEventData fact = null;
                var random = new SequenceBattleRandomSource(new[] { .5f });
                BattleRandom.SetSource(random);
                using (BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (h, f) => fact = f, 0))
                    Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
                Assert.That(env.Enemy.Entity.CurrentHP, Is.EqualTo(400f).Within(.01f));
                Assert.That(fact, Is.Not.Null);
                Assert.That(fact.Properties.IsLunar, Is.True);
                Assert.That(fact.IsNightSoulDamage, Is.True);
                Assert.That(fact.Source.ReactionType, Is.EqualTo(ReactionType.LunarElectroCharged));
                Assert.That(fact.DamageType, Is.EqualTo("Skill"));
                Assert.That(random.FloatCallCount, Is.EqualTo(1));
                Assert.That(random.IntCallCount, Is.Zero);
            }
        }

        [TestCase("True")]
        [TestCase("None")]
        public void Fixed_StatusDamageUsesSameFixedBaseAndLevelMultiplier(string element)
        {
            using (var env = new BattleFlowTestEnv())
            {
                const string statusID = "ST_TrueTick", effectID = "STE_TrueTick";
                env.ConfigureStatus(statusID, actionType: "OnTrigger", actionEffectID2: effectID);
                env.DataManager.StatusEffectDict[effectID] = new StatusEffectData
                {
                    StatusEffectID2 = effectID, EffectType = "Damage", Element = element,
                    DamageType = "Burst", Param1 = "12.5", TargetType = "Self"
                };
                env.DataManager.SkillLevelDict[$"{BattleFlowTestEnv.AllyID}_2"] = new Dictionary<int, SkillLevelData>
                {
                    [1] = new SkillLevelData { CharacterID = BattleFlowTestEnv.AllyID, SkillType = 2,
                        SkillLevel = 1, ParamID = effectID, Hits1 = "2*ATK,1" }
                };
                env.CreateMinimalBattle();
                var actor = env.Ally.Entity;
                var status = actor.AddStatus(statusID, actor, 0, 1, 0, env.DataManager.StatusMainDict[statusID]);
                var random = new SequenceBattleRandomSource();
                BattleRandom.SetSource(random);
                env.Ally.ExecuteStatusActions(status, env.DataManager.StatusActionDict[statusID], actor, "OnTrigger");
                Assert.That(actor.CurrentHP, Is.EqualTo(975f));
                Assert.That(random.FloatCallCount, Is.Zero);
                Assert.That(random.IntCallCount, Is.Zero);
            }
        }

        [TestCase("True")]
        [TestCase("None")]
        public void Fixed_EnemyReadsEnemySkillParam_AndZeroMultiplierStaysZero(string element)
        {
            foreach (float multiplier in new[] { 2f, 0f })
                using (var env = new BattleFlowTestEnv())
                {
                    env.ConfigureEnemyDamageSkill(multiplier, element, 1f);
                    env.DataManager.EnemySkillEffectDict["SE_Enemy_BattleFlow"].Param1 = "25";
                    env.CreateMinimalBattle();
                    env.Manager.StartBattle();
                    env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                    env.Ally.Entity.AddShield(500f, "Geo", 3f);
                    var random = new SequenceBattleRandomSource(intValues: new[] { 0 });
                    BattleRandom.SetSource(random);
                    env.Manager.EndAllyTurn();
                    env.Flow.AdvanceToPhase(env.Manager, TurnPhase.EnemyPostTurn);
                    Assert.That(env.Ally.Entity.CurrentHP, Is.EqualTo(multiplier == 0f || element == "None" ? 1000f : 950f));
                    Assert.That(env.Ally.Entity.GetTotalShieldHP(), Is.EqualTo(element == "None" && multiplier != 0f ? 450f : 500f));
                    Assert.That(random.FloatCallCount, Is.Zero, "True不判暴击");
                    Assert.That(random.IntCallCount, Is.EqualTo(1), "只保留原敌人技能权重选择");
                }
        }
    }
}
