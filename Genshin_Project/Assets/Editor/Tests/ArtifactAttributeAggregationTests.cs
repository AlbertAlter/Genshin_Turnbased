using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    /// <summary>只构造独立数据；不读源表或运行时工作簿。测试须由Unity Test Runner人工执行。</summary>
    public sealed class ArtifactAttributeAggregationTests
    {
        [Test]
        public void Mastery_ReadsLiveCountWithoutWritingPanelOrSpendingCount_MissingCountContributesZero()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                var owner = env.Ally.Entity;
                owner.TotalEM = 100f;
                var countMain = Count(env);
                var count = Add(owner, owner, countMain);
                count.RuntimeValue = 2f;
                Add(owner, owner, Buff(env, 452001, "EMBonus", "80"));
                Add(owner, owner, Buff(env, 459981, "EMBonus", "10 * Count(ST_Artifact_453805)"));
                var unrelated = Buff(env, 459982, "DMGBonus", "0.9");
                unrelated.ScriptHook = "Chance(0.5)";
                Add(owner, owner, unrelated);
                var random = new SequenceBattleRandomSource(new[] { .4f });
                BattleRandom.SetSource(random);

                Assert.That(owner.GetFinalEM(), Is.EqualTo(200f));
                Assert.That(owner.GetFinalEM(), Is.EqualTo(200f));
                Assert.That(owner.TotalEM, Is.EqualTo(100f));
                Assert.That(count.RuntimeValue, Is.EqualTo(2f));
                count.RuntimeValue = 1f;
                Assert.That(owner.GetFinalEM(), Is.EqualTo(190f));
                owner.RemoveStatusInstance(count, "Expired");
                Assert.That(owner.GetFinalEM(), Is.EqualTo(180f), "已定义但未施加的Count按0读取");
                Assert.That(random.FloatCallCount, Is.Zero, "属性查询不求值其他乘区的概率条件");
                Assert.That(random.IntCallCount, Is.Zero);
            }
        }

        [Test]
        public void SharedMastery_UsesEachCastersCount_AndFailedEffectsDoNotReserveMaxCount()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                var first = env.Ally.Entity;
                var second = env.CreateAlly(2, false).Entity;
                var recipient = env.CreateAlly(3, false).Entity;
                first.ConstellationLevel = 0;
                second.ConstellationLevel = 1;
                recipient.TotalEM = 20f;
                var countMain = Count(env);
                Add(first, first, countMain).RuntimeValue = 1f;
                Add(second, second, countMain).RuntimeValue = 3f;
                var main = Buff(env, 459981, "EMBonus", "10 * Count(ST_Artifact_453805)");
                main.MaxStack = 0;
                env.DataManager.StatusEffectDict[EffectID(main)].ScriptHook = "Check(990001_C1)";
                Add(recipient, first, main); // 不满足效果条件，不能占据MaxCount=1。
                Add(recipient, second, main);
                Add(recipient, second, main); // 仍只计一份，来源不是受益者。

                Assert.That(recipient.GetStatusEMBonus(), Is.EqualTo(30f));
                Assert.That(recipient.GetFinalEM(), Is.EqualTo(50f));
                Assert.That(recipient.TotalEM, Is.EqualTo(20f));
                first.ConstellationLevel = 1;
                first.GetStatus(countMain.StatusID2).RuntimeValue = 0f;
                Assert.That(recipient.GetFinalEM(), Is.EqualTo(20f), "合法0值仍占MaxCount，不退让给后来实例");
                first.ConstellationLevel = 0;
                first.TakeDamage(first.TotalHP + 1f,
                    DamageSourceInfo.Create(env.Enemy.Entity, ReactionSourceKind.EnemySkill, "test", "test"));
                Assert.That(recipient.GetFinalEM(), Is.EqualTo(50f), "其他来源阵亡不删除有效来源");
                second.TakeDamage(second.TotalHP + 1f,
                    DamageSourceInfo.Create(env.Enemy.Entity, ReactionSourceKind.EnemySkill, "test", "test"));
                Assert.That(recipient.GetFinalEM(), Is.EqualTo(20f), "装备者阵亡后跨角色加成一同清理");
            }
        }

        [Test]
        public void Mastery_WithoutDamageContextDoesNotPretendToBeBurstOrAnyReaction()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                var owner = env.Ally.Entity;
                owner.TotalEM = 10f;
                var burst = Buff(env, 459981, "EMBonus", "70");
                burst.ApplyDamageType = "Burst";
                Add(owner, owner, burst);
                var all = Buff(env, 459982, "EMBonus", "5");
                all.ApplyDamageType = "All";
                all.ApplyElementType = "All";
                Add(owner, owner, all);
                var reactionOnly = Buff(env, 459983, "EMBonus", "1000");
                reactionOnly.ApplyReactionType = "Swirl";
                Add(owner, owner, reactionOnly);

                Assert.That(owner.GetFinalEM(), Is.EqualTo(15f));
                Assert.That(owner.GetFinalEM("", "", 2, "Pyro"), Is.EqualTo(15f));
                Assert.That(owner.GetFinalEM("", "", 3, "Pyro"), Is.EqualTo(15f), "倍率类型不能伪造爆发伤害上下文");
                env.DataManager.SkillEffectDict["SE_Mastery_Burst"] = new SkillEffectData
                { SkillEffectID2 = "SE_Mastery_Burst", EffectType = "Damage", Element = "Pyro", DamageType = "Burst" };
                Assert.That(owner.GetFinalEM("SE_Mastery_Burst", "", 0, "Pyro"), Is.EqualTo(85f));
            }
        }

        [Test]
        public void Recharge_IsAdditiveAndReadOnly_BasedKeepsFractionsWhileFlatIgnoresIt()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                var owner = env.Ally.Entity;
                owner.TotalRechargeRate = 1.25f;
                owner.MaxEnergy = 100;
                owner.CurrentEnergy = 0f;
                var status = Add(owner, owner, Buff(env, 450901, "RechargeBonus", "0.2"));

                Assert.That(owner.GetFinalRechargeRate(), Is.EqualTo(1.45f).Within(.00001f));
                Assert.That(CharacterBattleController.ApplyEnergyGain(owner, 1.5f, "Based", ""),
                    Is.EqualTo(2.175f).Within(.00001f));
                Assert.That(CharacterBattleController.ApplyEnergyGain(owner, 1.5f, "Flat", ""),
                    Is.EqualTo(1.5f).Within(.00001f));
                Assert.That(owner.CurrentEnergy, Is.EqualTo(3.675f).Within(.00001f));
                Assert.That(owner.TotalRechargeRate, Is.EqualTo(1.25f));
                owner.RemoveStatusInstance(status, "Expired");
                Assert.That(CharacterBattleController.ApplyEnergyGain(owner, 1.5f, "Based", ""),
                    Is.EqualTo(1.875f).Within(.00001f));
            }
        }

        [Test]
        public void Recharge_KeepsBaseFallback_ButDoesNotTurnAZeroFinalMultiplierBackIntoOne()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                var owner = env.Ally.Entity;
                owner.TotalRechargeRate = 0f;
                Add(owner, owner, Buff(env, 453701, "RechargeBonus", "0.2"));
                Assert.That(owner.GetFinalRechargeRate(), Is.EqualTo(1.2f).Within(.00001f));
                var debuff = Buff(env, 459981, "RechargeBonus", "-2");
                env.DataManager.StatusEffectDict[EffectID(debuff)].EffectType = "Debuff";
                Add(owner, owner, debuff);
                Assert.That(owner.GetFinalRechargeRate(), Is.Zero);
                Assert.That(CharacterBattleController.ApplyEnergyGain(owner, 1.5f, "Based", ""), Is.Zero);
                Assert.That(owner.TotalRechargeRate, Is.Zero);
            }
        }

        [Test]
        public void SharedRecharge_UsesRecipientRateAndOriginalOffFieldAndElementCoefficients()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                var owner = env.Ally.Entity;
                var recipient = env.CreateAlly(2, false).Entity;
                owner.TotalRechargeRate = 3f;
                recipient.TotalRechargeRate = 1.25f;
                recipient.CurrentEnergy = 0f;
                recipient.MaxEnergy = 100;
                Add(recipient, owner, Buff(env, 450901, "RechargeBonus", "0.2"));
                string element = env.DataManager.CharacterAttributesDict[BattleFlowTestEnv.AllyID].Element;

                Assert.That(CharacterBattleController.ApplyEnergyGain(recipient, 2f, "Based", element),
                    Is.EqualTo(2f * .8f * 1.5f * 1.45f).Within(.00001f));
                Assert.That(recipient.TotalRechargeRate, Is.EqualTo(1.25f));
                Assert.That(owner.TotalRechargeRate, Is.EqualTo(3f), "来源的充能率不替代受益者充能率");
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AmplifyingAndQuicken_UseEffectiveMastery_AndHonorCapturedOverride(bool useSnapshot)
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                var owner = env.Ally.Entity;
                owner.TotalEM = 100f;
                var bonus = Add(owner, owner, Buff(env, 452001, "EMBonus", "80"));
                var context = Context(env, "Pyro");
                context.PreReactionDamage = 100f;
                if (useSnapshot)
                {
                    context.SourceSnapshotOverride = ReactionSourceSnapshot.Capture(context);
                    owner.RemoveStatusInstance(bonus, "Expired");
                    owner.TotalEM = 900f;
                }
                env.Enemy.Entity.ApplyAura("Hydro", 2f, owner.EntityID);
                Assert.That(AmplifyingReactionHandler.TryResolve(context, out var vaporize), Is.True);
                Assert.That(vaporize.FinalDamage,
                    Is.EqualTo(100f * 1.5f * (1f + 2.78f * 180f / 1580f)).Within(.001f));

                ReactionStateSystem.SetEntityState(env.Enemy.Entity, ReactionType.Quicken, 1,
                    new ReactionSourceSnapshot { SourceEntity = owner }, 2);
                context.AttackElement = "Electro";
                context.DamageComponents = new ReactionDamageComponents { SkillBaseDamage = 200f };
                Assert.That(QuickenDamageHandler.TryResolve(context, 100f, out var aggravate), Is.True);
                Assert.That(aggravate.FinalDamage,
                    Is.EqualTo(200f + 115f * (1f + 5f * 180f / 1380f)).Within(.001f));
            }
        }

        [Test]
        public void Snapshot_CapturesMasteryAndCountOnce_CloneAndOverrideDoNotReadLiveValuesAgain()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                var owner = env.Ally.Entity;
                owner.TotalEM = 100f;
                var count = Add(owner, owner, Count(env));
                count.RuntimeValue = 2f;
                Add(owner, owner, Buff(env, 459981, "EMBonus", "10 * Count(ST_Artifact_453805)"));
                var conditional = Buff(env, 452001, "EMBonus", "80");
                conditional.ScriptHook = "Chance(0.5)";
                Add(owner, owner, conditional);
                var random = new SequenceBattleRandomSource(new[] { .1f, .9f });
                BattleRandom.SetSource(random);
                var context = Context(env, "Pyro");
                var snapshot = ReactionSourceSnapshot.Capture(context, ReactionType.Overloaded, "Pyro");
                Assert.That(snapshot.TotalEM, Is.EqualTo(200f));
                Assert.That(random.FloatCallCount, Is.EqualTo(1));
                count.RuntimeValue = 0f;
                owner.TotalEM = 900f;
                context.SourceSnapshotOverride = snapshot;
                Assert.That(snapshot.Clone().TotalEM, Is.EqualTo(200f));
                Assert.That(ReactionSourceSnapshot.Capture(context, ReactionType.Overloaded, "Pyro").TotalEM,
                    Is.EqualTo(200f));
                Assert.That(ReactionDamageCalculator.GetSourceMastery(context), Is.EqualTo(200f));
                Assert.That(random.FloatCallCount, Is.EqualTo(1), "已有快照不再求值精通条件");
                Assert.That(random.IntCallCount, Is.Zero);
                Assert.That(count.RuntimeValue, Is.Zero);
            }
        }

        [Test]
        public void Superconduct_UsesOneMasterySnapshotForAllTargetsAndResistanceStates()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                env.CreateEnemy(2);
                var owner = env.Ally.Entity;
                owner.TotalEM = 100f;
                var bonus = Buff(env, 452001, "EMBonus", "80");
                bonus.ScriptHook = "Chance(0.5)";
                Add(owner, owner, bonus);
                var random = new SequenceBattleRandomSource(new[] { .1f, .9f });
                BattleRandom.SetSource(random);
                env.Enemy.Entity.ApplyAura("Cryo", 2f, owner.EntityID);

                Assert.That(SuperconductReactionHandler.TryResolve(Context(env, "Electro"), 100f, out var result), Is.True);
                Assert.That(result.DerivedHits.Count, Is.EqualTo(2));
                foreach (var hit in result.DerivedHits)
                {
                    float resistance = ReactionDamageCalculator.GetResistanceMultiplier(hit.Target, "Cryo");
                    Assert.That(hit.Damage,
                        Is.EqualTo(100f * (1f + 16f * 180f / 2180f) * resistance).Within(.001f));
                    Assert.That(ReactionStateSystem.TryGetEntityState(hit.Target, ReactionType.Superconduct, out var state), Is.True);
                    Assert.That(state.SourceSnapshot.TotalEM, Is.EqualTo(180f));
                }
                Assert.That(random.FloatCallCount, Is.EqualTo(1), "多目标伤害和减抗状态共用反应来源快照");
                Assert.That(random.IntCallCount, Is.Zero);
            }
        }

        [Test]
        public void CrystalShieldAndBondExpression_UseEffectiveMasteryWithoutWritingRawValue()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                var owner = env.Ally.Entity;
                owner.TotalEM = 100f;
                Add(owner, owner, Buff(env, 452001, "EMBonus", "80"));
                Assert.That(BondOfLifeEffect.Evaluate("0.5*TotalEM", owner, env.Enemy.Entity), Is.EqualTo(90f));
                env.Enemy.Entity.ApplyAura("Hydro", 2f, owner.EntityID);
                Assert.That(CrystallizeReactionHandler.TryResolve(Context(env, "Geo"), out var result), Is.True);
                Assert.That(result.ShieldValue, Is.EqualTo(ReactionDamageCalculator.CalculateCharacterCrystalShield(
                    ReactionDamageCalculator.GetLevelCoefficient(owner.Level), 180f)).Within(.001f));
                Assert.That(owner.TotalEM, Is.EqualTo(100f));
            }
        }

        [TestCase(3162, 4316203, "Normal", 28)]
        [TestCase(3187, 4318703, "Heavy", 12)]
        public void WeaponMasteryContribution_ReceivesArtifactBonusWithoutSpendingOnAttributeQueries(
            int weaponID, int statusID, string damageType, int uses)
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                var owner = env.Ally.Entity;
                owner.TotalEM = 100f;
                Add(owner, owner, Buff(env, 452001, "EMBonus", "80"));
                var weapon = new WeaponRuntimeContext { WeaponID = weaponID, Equipper = owner,
                    Refinement = 1, SourceInstanceID = "ArtifactMasteryTest:" + weaponID };
                weapon.ResolvedParams[1] = .08f;
                weapon.ResolvedParams[2] = 1.6f;
                var main = new StatusMainData { StatusID = statusID, StatusID2 = DataManager.WeaponStatusKey(statusID),
                    StatusType = "Count", MaxStack = 1, MaxCount = 1 };
                env.DataManager.StatusMainDict[main.StatusID2] = main;
                var count = owner.AddStatus(main.StatusID2, owner, 2, 2, 0, main, weapon);
                Assert.That(owner.GetFinalEM(), Is.EqualTo(180f));
                Assert.That(count.RuntimeValue, Is.EqualTo(uses));
                var context = new CountDamageContext { Attacker = owner, Target = env.Enemy.Entity,
                    SourceKind = ReactionSourceKind.CharacterSkill, DamageType = damageType,
                    SkillType = damageType == "Normal" ? 0 : 1, Element = "Dendro" };
                using (var batch = new CountDamageBatch())
                    Assert.That(batch.Prepare(context).BaseDamageBonus, Is.EqualTo(288f).Within(.001f));
                Assert.That(count.RuntimeValue, Is.EqualTo(uses), "取消命中归还预占，不重复扣点");
                using (var batch = new CountDamageBatch())
                {
                    var plan = batch.Prepare(context);
                    plan.Resolve(new DamageResolvedEvent { Target = env.Enemy.Entity, HitLanded = true });
                }
                Assert.That(count.RuntimeValue, Is.EqualTo(uses - 1));
                Assert.That(owner.TotalEM, Is.EqualTo(100f));
            }
        }

        [Test]
        public void ExtraBaseStats_ReadSourceCountAndReturnToZeroWhenThatCountIsMissing()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            var owner = env.Ally.Entity; var source = env.CreateAlly(2, false).Entity;
            var count = Add(source, source, Count(env)); count.RuntimeValue = 2f;
            Add(owner, source, Buff(env, 459981, "ExtraBaseATK", "10*Count(ST_Artifact_453805)"));
            Add(owner, source, Buff(env, 459982, "ExtraBaseHP", "10*Count(ST_Artifact_453805)"));
            Add(owner, source, Buff(env, 459983, "ExtraBaseDEF", "10*Count(ST_Artifact_453805)"));
            Assert.That(owner.GetTotalBaseATK(), Is.EqualTo(120f));
            Assert.That(owner.GetFinalHP(), Is.EqualTo(1020f));
            Assert.That(owner.GetFinalDEF(), Is.EqualTo(120f));
            Assert.That(count.RuntimeValue, Is.EqualTo(2f));
            count.RuntimeValue = 1f;
            Assert.That(owner.GetTotalBaseATK(), Is.EqualTo(110f));
            Assert.That(owner.GetFinalHP(), Is.EqualTo(1010f));
            Assert.That(owner.GetFinalDEF(), Is.EqualTo(110f));
            source.RemoveStatusInstance(count);
            Assert.That(owner.GetTotalBaseATK(), Is.EqualTo(100f));
            Assert.That(owner.GetFinalHP(), Is.EqualTo(1000f));
            Assert.That(owner.GetFinalDEF(), Is.EqualTo(100f));
        }

        [Test]
        public void LuckyDog_FlatDefenseIsAddedAfterPercentBonus_AndQueriesDoNotWriteThePanel()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                var owner = env.Ally.Entity;
                // 此面板已含50%常驻加成；基础防御仍是测试角色配置的100。
                owner.TotalDEF = 150f;
                var flat = Add(owner, owner, Buff(env, 451101, "DEFBonusFlat", "100"));
                Add(owner, owner, Buff(env, 459981, "DEFBonus", "0.2"));

                Assert.That(owner.GetStatusDEFBonusFlat(), Is.EqualTo(100f));
                Assert.That(owner.GetFinalDEF(), Is.EqualTo(270f), "150 + 100*0.2 + 100，固定值不再乘百分比");
                Assert.That(owner.GetFinalDEF(), Is.EqualTo(270f));
                Assert.That(BondOfLifeEffect.Evaluate("0.5*TotalDEF", owner, env.Enemy.Entity), Is.EqualTo(135f));
                Assert.That(owner.TotalDEF, Is.EqualTo(150f));
                owner.RemoveStatusInstance(flat, "Expired");
                Assert.That(owner.GetFinalDEF(), Is.EqualTo(170f));
            }
        }

        [Test]
        public void DefenseBonuses_ReadCurrentSourceCount_AndMissingCountContributesZero()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                var owner = env.Ally.Entity;
                owner.TotalDEF = 100f;
                var count = Add(owner, owner, Count(env));
                count.RuntimeValue = 2f;
                Add(owner, owner, Buff(env, 459981, "DEFBonusFlat", "10 * Count(ST_Artifact_453805)"));
                Add(owner, owner, Buff(env, 459982, "DEFBonus", "0.06 * Count(ST_Artifact_453805)"));
                var unrelated = Buff(env, 459983, "EMBonus", "1000");
                unrelated.ScriptHook = "Chance(0.5)";
                Add(owner, owner, unrelated);
                var random = new SequenceBattleRandomSource(new[] { .4f });
                BattleRandom.SetSource(random);

                Assert.That(owner.GetFinalDEF(), Is.EqualTo(132f).Within(.00001f));
                Assert.That(count.RuntimeValue, Is.EqualTo(2f));
                count.RuntimeValue = 1f;
                Assert.That(owner.GetFinalDEF(), Is.EqualTo(116f).Within(.00001f));
                owner.RemoveStatusInstance(count, "Expired");
                Assert.That(owner.GetFinalDEF(), Is.EqualTo(100f));
                Assert.That(owner.TotalDEF, Is.EqualTo(100f));
                Assert.That(random.FloatCallCount, Is.Zero, "防御查询不求值其他属性的概率条件");
                Assert.That(random.IntCallCount, Is.Zero);
            }
        }

        [TestCase("DEFBonusFlat", "100", 200f)]
        [TestCase("DEFBonus", "0.2", 120f)]
        public void DefenseBonuses_CheckEffectConditionsBeforeKeepingMaxCountReservation(
            string part, string value, float expectedDefense)
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                var first = env.Ally.Entity;
                var second = env.CreateAlly(2, false).Entity;
                var recipient = env.CreateAlly(3, false).Entity;
                recipient.TotalDEF = 100f;
                first.ConstellationLevel = 0;
                second.ConstellationLevel = 1;
                var main = Buff(env, 459981, part, value);
                main.MaxStack = 0;
                main.ApplyDamageType = "Skill";
                main.ApplyElementType = "Cryo";
                env.DataManager.StatusEffectDict[EffectID(main)].ScriptHook = "Check(990001_C1)";
                Add(recipient, first, main);
                Add(recipient, second, main);
                Add(recipient, second, main);
                env.DataManager.SkillEffectDict["SE_Defense_Skill"] = new SkillEffectData
                { SkillEffectID2 = "SE_Defense_Skill", EffectType = "Damage", Element = "Cryo", DamageType = "Skill" };

                Assert.That(recipient.GetFinalDEF("SE_Defense_Skill", "", 0, "Cryo"), Is.EqualTo(expectedDefense).Within(.00001f));
                Assert.That(recipient.GetFinalDEF("SE_Defense_Skill", "", 0, "Pyro"), Is.EqualTo(100f));
                Assert.That(recipient.GetFinalDEF(), Is.EqualTo(100f));
                second.TakeDamage(second.TotalHP + 1f,
                    DamageSourceInfo.Create(env.Enemy.Entity, ReactionSourceKind.EnemySkill, "test", "test"));
                Assert.That(recipient.GetFinalDEF("SE_Defense_Skill", "", 0, "Cryo"), Is.EqualTo(100f), "来源阵亡撤销跨角色防御加成");
            }
        }

        [TestCase("ATKBonus")]
        [TestCase("DMGBonus")]
        [TestCase("ResBonus")]
        [TestCase("HealBonus")]
        [TestCase("BeHealedBonus")]
        public void SharedScalarPaths_KeepEffectConditionsSourceCountAndMaxCount_WithoutExtraProbabilityCalls(string part)
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            var first = env.Ally.Entity; var second = env.CreateAlly(2, false).Entity;
            var recipient = env.CreateAlly(3, false).Entity;
            first.ConstellationLevel = 0; second.ConstellationLevel = 1;
            var countMain = Count(env);
            Add(first, first, countMain).RuntimeValue = 1f; Add(second, second, countMain).RuntimeValue = 3f;
            var main = Buff(env, 459981, part, ".1*Count(ST_Artifact_453805)"); main.MaxStack = 0;
            env.DataManager.StatusEffectDict[EffectID(main)].ScriptHook = "Check(990001_C1);Chance(1)";
            Add(recipient, first, main); Add(recipient, second, main); Add(recipient, second, main);
            var unrelated = Buff(env, 459982, "CritRate", ".9"); unrelated.ScriptHook = "Chance(.5)";
            Add(recipient, first, unrelated);
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            float Read()
            {
                switch (part)
                {
                    case "ATKBonus": return recipient.GetStatusATKBonus("query", "", -1, "Cryo");
                    case "DMGBonus": return recipient.GetStatusDMGBonus("query", "", -1, "Cryo", env.Enemy.Entity);
                    case "ResBonus": return recipient.GetStatusResBonus("Cryo");
                    default: return recipient.GetStatusHealingBonus(part, "query", "", -1, "Cryo", recipient);
                }
            }
            Assert.That(Read(), Is.EqualTo(.3f).Within(.00001f));
            Assert.That(random.FloatCallCount, Is.EqualTo(1), "失败来源归还额度，额度满后不求第三份或其他乘区的条件");
            Assert.That(Read(), Is.EqualTo(.3f).Within(.00001f));
            Assert.That(random.FloatCallCount, Is.EqualTo(2), "每次正式Chance求值仍独立，不为单次查询重复抽取");
            Assert.That(random.IntCallCount, Is.Zero);
            Assert.That(second.GetStatus(countMain.StatusID2).RuntimeValue, Is.EqualTo(3f));
        }

        [Test]
        public void AllReactionFilter_StillRequiresExplicitRealReaction_AndCannotLeakIntoOrdinaryDamage()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle(); var owner = env.Ally.Entity;
            var damage = Buff(env, 459981, "DMGBonus", ".5"); damage.ApplyReactionType = "All";
            var critical = Buff(env, 459982, "CritDMG", ".4"); critical.ApplyReactionType = "All";
            Add(owner, owner, damage); Add(owner, owner, critical);
            Assert.That(owner.GetStatusDMGBonus("query", "", -1, "Pyro", env.Enemy.Entity), Is.Zero);
            Assert.That(owner.GetStatusCritDMG("query", "", -1, "Pyro", env.Enemy.Entity), Is.Zero);
            Assert.That(owner.GetStatusCritDMG("query", "", -1, "Pyro", env.Enemy.Entity, ReactionType.None), Is.Zero);
            Assert.That(owner.GetStatusCritDMG("query", "", -1, "Pyro", env.Enemy.Entity, (ReactionType)999), Is.Zero);
            Assert.That(owner.GetStatusCritDMG("query", "", -1, "Geo", env.Enemy.Entity, ReactionType.LunarCrystallize),
                Is.EqualTo(.4f).Within(.00001f));
            Assert.That(ReactionDamageCalculator.CollectReactionBonuses(owner, ReactionType.Overloaded, "Pyro").DMGBonus,
                Is.EqualTo(.5f).Within(.00001f));
            Assert.That(ReactionDamageCalculator.CollectReactionBonuses(owner, ReactionType.None, "Pyro").DMGBonus, Is.Zero);
        }

        [Test]
        public void DamageTakenSnapshot_ReleasesFailedEffectQuota_AndDoesNotReevaluateAfterReaction()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            var first = env.Ally.Entity; var second = env.CreateAlly(2, false).Entity;
            var recipient = env.CreateAlly(3, false).Entity;
            first.ConstellationLevel = 0; second.ConstellationLevel = 1;
            var countMain = Count(env);
            Add(first, first, countMain).RuntimeValue = 0f;
            Add(second, second, countMain).RuntimeValue = 3f;
            var main = Buff(env, 459981, "DMGTaken", ".1*Count(ST_Artifact_453805)");
            main.MaxStack = 0; main.ApplyDamageType = "Skill"; main.ApplyElementType = "Cryo";
            main.ApplyReactionType = "All";
            env.DataManager.StatusEffectDict[EffectID(main)].ScriptHook = "Check(990001_C1);Chance(1)";
            Add(recipient, first, main); Add(recipient, second, main); Add(recipient, second, main);
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            var snapshot = recipient.PrepareDMGTaken("effect", "skill", "Skill", "Cryo");
            Assert.That(random.FloatCallCount, Is.EqualTo(1));
            second.GetStatus(countMain.StatusID2).RuntimeValue = 1f;
            second.ConstellationLevel = 0;
            Assert.That(snapshot.GetMultiplier(ReactionType.None), Is.EqualTo(1f));
            Assert.That(snapshot.GetMultiplier((ReactionType)999), Is.EqualTo(1f));
            Assert.That(snapshot.GetMultiplier(ReactionType.Melt), Is.EqualTo(1.3f).Within(.00001f));
            Assert.That(snapshot.GetMultiplier(ReactionType.LunarCrystallize), Is.EqualTo(1.3f).Within(.00001f));
            Assert.That(random.FloatCallCount, Is.EqualTo(1), "反应筛选只读命中前快照，不重求条件/Count/概率");
            first.ConstellationLevel = 1;
            var zero = recipient.PrepareDMGTaken("effect", "skill", "Skill", "Cryo");
            Assert.That(zero.GetMultiplier(ReactionType.Melt), Is.EqualTo(1f), "合法0值仍占MaxCount，不能退让");
            Assert.That(random.FloatCallCount, Is.EqualTo(2));
            Assert.That(recipient.PrepareDMGTaken("effect", "skill", "Normal", "Cryo").GetMultiplier(ReactionType.Melt), Is.EqualTo(1f));
            Assert.That(recipient.PrepareDMGTaken("effect", "skill", "Skill", "Pyro").GetMultiplier(ReactionType.Melt), Is.EqualTo(1f));
            Assert.That(random.FloatCallCount, Is.EqualTo(2));
            Assert.That(second.GetStatus(countMain.StatusID2).RuntimeValue, Is.EqualTo(1f));
        }

        [TestCase(452501, false)]
        [TestCase(452501, true)]
        [TestCase(453501, false)]
        [TestCase(453501, true)]
        [TestCase(453504, false)]
        [TestCase(453504, true)]
        public void PhysicalArtifactBonuses_RequirePhysicalInRealSkillAndStatusDamage_WithoutMatchingOtherElements(
            int id, bool statusDamage)
        {
            using var env = new BattleFlowTestEnv();
            env.ConfigureAllyAction(0, "SK_PhysicalProbe", "SE_PhysicalProbe", "Physical", "1*TotalATK,0", 0);
            env.CreateMinimalBattle(); var owner = env.Ally.Entity;
            var physical = Buff(env, id, "DMGBonus", ".25"); physical.ApplyElementType = "Physical";
            StatusInstance count = null;
            if (id == 453504)
            {
                physical.ScriptHook = "PaleFlameFull()";
                var countMain = new StatusMainData { StatusID = 453503, StatusID2 = "ST_Artifact_453503",
                    StatusType = "Count", MaxCount = 1, MaxStack = 1 };
                env.DataManager.StatusMainDict[countMain.StatusID2] = countMain;
                count = Add(owner, owner, countMain); count.RuntimeValue = 2f;
            }
            Add(owner, owner, physical);
            var ticking = Buff(env, 459970, null, "0");
            env.DataManager.StatusEffectDict[EffectID(ticking)] = new StatusEffectData
            { StatusEffectID = 45997001, StatusEffectID2 = EffectID(ticking), EffectType = "Damage",
                Element = "Physical", Param1 = "1*TotalATK,0", DamageType = "Normal", TargetType = "Enemy", TargetSelect = "1" };
            var actions = new System.Collections.Generic.List<StatusActionData> { new StatusActionData
            { StatusID = ticking.StatusID, StatusID2 = ticking.StatusID2, ActionType = "OnTrigger", Param1 = EffectID(ticking) } };
            env.DataManager.StatusActionDict[ticking.StatusID2] = actions;
            var tickingStatus = Add(owner, owner, ticking);
            env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            foreach (string element in new[] { "Physical", "physical", "PHYSICAL" })
                Assert.That(owner.GetStatusDMGBonus("query", "", -1, element, env.Enemy.Entity), Is.EqualTo(.25f).Within(.00001f));
            foreach (string element in new[] { "", "None", "Pyro", "Geo", "True" })
                Assert.That(owner.GetStatusDMGBonus("query", "", -1, element, env.Enemy.Entity), Is.Zero);
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            string effectID = statusDamage ? EffectID(ticking) : "SE_PhysicalProbe";
            var hits = new System.Collections.Generic.List<float>();
            using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, fact) =>
            { if (fact.Source.SourceEffectID == effectID) hits.Add(fact.DamageBeforeShield); }, 0);
            void Attack()
            {
                if (statusDamage) env.Ally.ExecuteStatusActionsTask(tickingStatus, actions, owner, "OnTrigger").GetAwaiter().GetResult();
                else Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True);
            }
            Attack();
            Assert.That(hits, Has.Count.EqualTo(1));
            Assert.That(hits[0], Is.EqualTo(62.5f).Within(.001f), "100白值*1.25物伤*.5防御，不能漏掉25%");
            if (count != null)
            {
                count.RuntimeValue = 1f; Attack();
                Assert.That(hits, Has.Count.EqualTo(2)); Assert.That(hits[1], Is.EqualTo(50f).Within(.001f));
            }
            Assert.That(random.FloatCallCount, Is.EqualTo(hits.Count)); Assert.That(random.IntCallCount, Is.Zero);
        }

        private static string EffectID(StatusMainData main) => "STE_Artifact_" + main.StatusID + "01";

        private static StatusMainData Buff(BattleFlowTestEnv env, int id, string part, string value)
        {
            var main = new StatusMainData { StatusID = id, StatusID2 = "ST_Artifact_" + id,
                StatusType = "Buff", MultiplierPart = part, MaxStack = 1, MaxCount = 1, WhenMax = "0" };
            env.DataManager.StatusMainDict[main.StatusID2] = main;
            env.DataManager.StatusEffectDict[EffectID(main)] = new StatusEffectData
            { StatusEffectID = id * 100 + 1, StatusEffectID2 = EffectID(main), EffectType = "Buff", Param1 = value };
            return main;
        }

        private static StatusMainData Count(BattleFlowTestEnv env)
        {
            var main = new StatusMainData { StatusID = 453805, StatusID2 = "ST_Artifact_453805",
                StatusType = "Count", MaxStack = 1, MaxCount = 1, WhenMax = "0" };
            env.DataManager.StatusMainDict[main.StatusID2] = main;
            return main;
        }

        private static StatusInstance Add(BattleEntity host, BattleEntity caster, StatusMainData main)
            => host.AddStatus(main.StatusID2, caster, 0, 1, 0, main);

        private static ReactionContext Context(BattleFlowTestEnv env, string element)
            => new ReactionContext { SourceEntity = env.Ally.Entity, Target = env.Enemy.Entity,
                SourceKind = ReactionSourceKind.CharacterSkill, SourceEffectID = "SE_Test_Mastery",
                SourceSkillID = "SK_Test_Mastery", AttackElement = element, AttackAmount = 1f,
                DamageType = "Skill", ApplicationPhase = 2 };
    }
}
