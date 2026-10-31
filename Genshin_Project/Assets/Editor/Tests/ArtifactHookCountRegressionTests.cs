using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class ArtifactHookCountRegressionTests
    {
        [Test]
        public void ArtifactCounts_AllThirteenAuthoredIDsAreRegistered_UnknownRemainsUnsupported()
        {
            foreach (int id in new[] { 452207, 452305, 453503, 453805, 453903, 454003,
                454505, 454803, 455003, 455004, 455203, 455302, 456002 })
                Assert.That(CountStatusSystem.IsRegistered("ST_Artifact_" + id), Is.True, id.ToString());
            Assert.That(CountStatusSystem.IsRegistered("ST_Artifact_Unknown"), Is.False);
        }

        [Test]
        public void ReactionConditions_RequireCurrentDamageDomain_NotAnEarlierReactionDeclaration()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                var context = new ScriptHookContext { Caster = env.Ally.Entity };
                Assert.That(ScriptHookEvaluator.Evaluate("Hook(456302)", context), Is.False);
                context.ReactionEvent = new ReactionOccurrence { Type = ReactionType.Superconduct };
                Assert.That(ScriptHookEvaluator.Evaluate("Hook(456302)", context), Is.False);
                context.DamageReaction = ReactionType.Superconduct;
                Assert.That(ScriptHookEvaluator.Evaluate("Hook(456302)", context), Is.True);
                context.DamageReaction = ReactionType.StellarSuperconduct;
                Assert.That(ScriptHookEvaluator.Evaluate("Hook(456302)", context), Is.False);
                var main = Main(env, 456302, "Buff");
                main.MultiplierPart = "DMGBonus";
                main.ApplyReactionType = "Superconduct";
                main.ScriptHook = "Hook(456302)";
                env.DataManager.StatusEffectDict["STE_Artifact_45630201"] = new StatusEffectData
                { StatusEffectID = 45630201, EffectType = "Buff", Param1 = "0.8" };
                Add(env, env.Ally.Entity, main.StatusID2);
                Assert.That(env.Ally.Entity.GetStatusDMGBonus("test", "test", 0, "Cryo", env.Enemy.Entity), Is.Zero);
                Assert.That(ReactionDamageCalculator.CollectReactionBonuses(env.Ally.Entity,
                    ReactionType.Superconduct, "Cryo").DMGBonus, Is.EqualTo(0.8f).Within(0.00001f));
                Assert.That(ReactionDamageCalculator.CollectExclusiveReactionDamageBonus(env.Ally.Entity,
                    ReactionType.Superconduct, "Cryo"), Is.EqualTo(0.8f).Within(0.00001f));
            }
        }

        [Test]
        public void SpecialReactionBuffs_ReadLiveCountIntoIndependentParts_AndSnapshotFreezesCapturedValues()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                var owner = env.Ally.Entity;
                owner.DMGBonus = .1f;
                owner.BaseDMGBonus = .25f;
                owner.LunarDMGBonus = .04f;
                owner.StellarDMGBonus = .07f;
                Main(env, 453805, "Count");
                var count = Add(env, owner, "ST_Artifact_453805");
                count.RuntimeValue = 2f;
                var lunar = Main(env, 459991, "Buff");
                lunar.MultiplierPart = "LunarDMGBonus";
                lunar.ApplyReactionType = "月曜";
                lunar.ApplyElementType = "Geo";
                env.DataManager.StatusEffectDict["STE_Artifact_45999101"] = new StatusEffectData
                { StatusEffectID = 45999101, EffectType = "Buff", Param1 = "0.06 * Count(ST_Artifact_453805)" };
                Add(env, owner, lunar.StatusID2);
                var stellar = Main(env, 459992, "Buff");
                stellar.MultiplierPart = "StellarDMGBonus";
                stellar.ApplyReactionType = "星烁";
                stellar.ApplyElementType = "Cryo";
                env.DataManager.StatusEffectDict["STE_Artifact_45999201"] = new StatusEffectData
                { StatusEffectID = 45999201, EffectType = "Buff", Param1 = "0.03" };
                Add(env, owner, stellar.StatusID2);

                var context = new ReactionContext { SourceEntity = owner, Target = env.Enemy.Entity,
                    SourceKind = ReactionSourceKind.CharacterSkill, SourceSkillID = "skill", SourceEffectID = "effect" };
                var snapshot = ReactionSourceSnapshot.Capture(context, ReactionType.LunarCrystallize, "Geo");
                Assert.That(snapshot.DMGBonus, Is.EqualTo(.1f).Within(.00001f), "月曜增伤不混入普通增伤");
                Assert.That(snapshot.BaseDMGBonus, Is.EqualTo(.25f).Within(.00001f));
                Assert.That(snapshot.LunarDMGBonus, Is.EqualTo(.16f).Within(.00001f));
                Assert.That(snapshot.StellarDMGBonus, Is.Zero);
                count.RuntimeValue = 1f;
                var current = ReactionDamageCalculator.CollectReactionBonuses(owner, ReactionType.LunarCrystallize, "Geo");
                Assert.That(current.LunarDMGBonus, Is.EqualTo(.1f).Within(.00001f));
                Assert.That(snapshot.LunarDMGBonus, Is.EqualTo(.16f).Within(.00001f), "后续Count下降不改旧快照");
                Assert.That(snapshot.Clone().LunarDMGBonus, Is.EqualTo(.16f).Within(.00001f));
                context.SourceSnapshotOverride = snapshot;
                Assert.That(ReactionSourceSnapshot.Capture(context, ReactionType.LunarCrystallize, "Geo").LunarDMGBonus,
                    Is.EqualTo(.16f).Within(.00001f), "持续反应覆盖源也不重新读实时Count");

                var polar = ReactionDamageCalculator.CollectReactionBonuses(owner, ReactionType.StellarSuperconduct, "Cryo");
                Assert.That(polar.StellarDMGBonus, Is.EqualTo(.1f).Within(.00001f));
                Assert.That(polar.LunarDMGBonus, Is.Zero);
                Assert.That(polar.DMGBonus, Is.EqualTo(.1f).Within(.00001f));
                var normal = ReactionDamageCalculator.CollectReactionBonuses(owner, ReactionType.Crystallize, "Geo");
                Assert.That(normal.BaseDMGBonus, Is.EqualTo(.25f).Within(.00001f), "独立汇总，不挤入普通增伤区");
                Assert.That(normal.LunarDMGBonus, Is.Zero);
                Assert.That(normal.StellarDMGBonus, Is.Zero);
                Assert.That(owner.GetStatusDMGBonus("effect", "skill", 2, "Geo", env.Enemy.Entity), Is.Zero,
                    "专属状态数值不得漏进普通直伤增伤");
                Assert.That(count.RuntimeValue, Is.EqualTo(1f), "重复查询与快照不能消耗或修改Count");
            }
        }

        [TestCase("Pyro", "Hydro", 1.5f)]
        [TestCase("Hydro", "Pyro", 2f)]
        [TestCase("Pyro", "Cryo", 2f)]
        [TestCase("Cryo", "Pyro", 1.5f)]
        public void CrimsonWitch_BaseBonusFlowsIntoAmplifyingHandler_InAllFourDirections(
            string attackElement, string auraElement, float multiplier)
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                var owner = env.Ally.Entity;
                owner.TotalEM = 200f;
                AddReactionBaseBonus(env, 452303, "Vaporize;Melt", "0.15");
                AddUnrelatedProbabilityBonus(env);
                var random = new SequenceBattleRandomSource(new[] { .4f });
                BattleRandom.SetSource(random);
                env.Enemy.Entity.ApplyAura(auraElement, 4f, owner.EntityID);
                var context = new ReactionContext { SourceEntity = owner, Target = env.Enemy.Entity,
                    SourceKind = ReactionSourceKind.CharacterSkill, AttackElement = attackElement, AttackAmount = 1f,
                    PreReactionDamage = 100f, DamageType = "Skill" };
                Assert.That(AmplifyingReactionHandler.TryResolve(context, out var result), Is.True);
                Assert.That(result.FinalDamage,
                    Is.EqualTo(100f * multiplier * (1f + 2.78f * 200f / 1600f + .15f)).Within(.001f));
                Assert.That(random.FloatCallCount, Is.Zero, "读取基础加成不求值无关DMGBonus的Chance条件");
                Assert.That(random.IntCallCount, Is.Zero);
            }
        }

        [TestCase("Electro", ReactionType.Aggravate, 1.15f, .2f)]
        [TestCase("Dendro", ReactionType.Spread, 1.25f, 0f)]
        public void ThunderingFury_BaseBonusOnlyRaisesAggravateIncrement_NotSpreadOrWholeSkill(
            string element, ReactionType reaction, float coefficient, float expectedBonus)
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                var owner = env.Ally.Entity;
                owner.TotalEM = 200f;
                AddReactionBaseBonus(env, 452203, "Aggravate", "0.2");
                AddUnrelatedProbabilityBonus(env);
                var random = new SequenceBattleRandomSource(new[] { .4f });
                BattleRandom.SetSource(random);
                ReactionStateSystem.SetEntityState(env.Enemy.Entity, ReactionType.Quicken, 1,
                    new ReactionSourceSnapshot { SourceEntity = owner }, 2);
                var context = new ReactionContext { SourceEntity = owner, Target = env.Enemy.Entity,
                    SourceKind = ReactionSourceKind.CharacterSkill, AttackElement = element, AttackAmount = 1f,
                    DamageType = "Skill", DamageComponents = new ReactionDamageComponents
                    { SkillBaseDamage = 200f, DamageBonusMultiplier = 1.2f, DefenseMultiplier = .5f, ResistanceMultiplier = .8f } };
                Assert.That(QuickenDamageHandler.TryResolve(context, 100f, out var result), Is.True);
                Assert.That(result.Type, Is.EqualTo(reaction));
                Assert.That(result.FinalDamage, Is.EqualTo((200f + coefficient * 100f
                    * (1f + 5f * 200f / 1400f + expectedBonus)) * 1.2f * .5f * .8f).Within(.001f));
                Assert.That(random.FloatCallCount, Is.Zero);
                Assert.That(random.IntCallCount, Is.Zero);
            }
        }

        [Test]
        public void AmplifyingAndQuickenHandlers_PreserveCapturedBaseBonusInsteadOfReadingNewLiveValue()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                var owner = env.Ally.Entity;
                owner.BaseDMGBonus = .9f;
                env.Enemy.Entity.ApplyAura("Hydro", 2f, owner.EntityID);
                var context = new ReactionContext { SourceEntity = owner, Target = env.Enemy.Entity,
                    SourceKind = ReactionSourceKind.StatusEffect, AttackElement = "Pyro", AttackAmount = 1f,
                    PreReactionDamage = 100f, SourceSnapshotOverride = new ReactionSourceSnapshot
                    { TotalEM = 0f, BaseDMGBonus = .15f } };
                Assert.That(AmplifyingReactionHandler.TryResolve(context, out var vaporize), Is.True);
                Assert.That(vaporize.FinalDamage, Is.EqualTo(100f * 1.5f * 1.15f).Within(.001f));
                ReactionStateSystem.SetEntityState(env.Enemy.Entity, ReactionType.Quicken, 1,
                    new ReactionSourceSnapshot { SourceEntity = owner }, 2);
                context.AttackElement = "Electro";
                context.DamageComponents = new ReactionDamageComponents
                { SkillBaseDamage = 200f, DamageBonusMultiplier = 1f, DefenseMultiplier = 1f, ResistanceMultiplier = 1f };
                owner.TotalEM = 0f;
                context.SourceSnapshotOverride.BaseDMGBonus = .2f;
                Assert.That(QuickenDamageHandler.TryResolve(context, 100f, out var aggravate), Is.True);
                Assert.That(aggravate.FinalDamage, Is.EqualTo(200f + 115f * 1.2f).Within(.001f));
            }
        }

        [Test]
        public void NymphConditions_CountSourceKinds_NotInstances_AndUseExclusiveTiers()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                var owner = env.Ally.Entity;
                var context = new ScriptHookContext { Caster = owner };
                Assert.That(ScriptHookEvaluator.Evaluate("Hook(45460201)", context), Is.False);
                for (int id = 454608; id <= 454611; id++)
                {
                    var main = Main(env, id);
                    main.MaxStack = 0;
                    Add(env, owner, main.StatusID2, 2);
                    Add(env, owner, main.StatusID2, 2);
                    int tier = System.Math.Min(3, id - 454607);
                    for (int candidate = 1; candidate <= 3; candidate++)
                        Assert.That(ScriptHookEvaluator.Evaluate("Hook(" + (454601 + candidate) + "01)", context),
                            Is.EqualTo(candidate == tier));
                }
                Assert.That(ScriptHookEvaluator.Evaluate("Hook(455702)", (ScriptHookContext)null), Is.False);
                Assert.That(ScriptHookEvaluator.Evaluate("Hook(455902)", (ScriptHookContext)null), Is.False);
            }
        }

        [Test]
        public void NighttimeWhispers_RequiresWindow_AndReadsCrystalShieldOrAnyEnemyFieldMoonCageLive()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                var owner = env.Ally.Entity;
                var teammate = env.CreateAlly(2, false).Entity;
                foreach (int id in new[] { 455103, 455104 })
                {
                    var main = Main(env, id, "Buff");
                    main.MultiplierPart = "DMGBonus";
                    main.ApplyElementType = "Geo";
                    if (id == 455104) main.ScriptHook = "Hook(455104)";
                    env.DataManager.StatusEffectDict["STE_Artifact_" + id + "01"] = new StatusEffectData
                    { StatusEffectID = id * 100 + 1, EffectType = "Buff", Param1 = id == 455103 ? "0.2" : "0.3" };
                }
                var extra = Add(env, owner, "ST_Artifact_455104");
                var context = new ScriptHookContext { Caster = owner, Status = extra };
                env.Manager.StartBattle();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                var resonance = env.Manager.TeamBonuses.Resonance;
                Assert.That(resonance.Has("Geo"), Is.False, "无双岩仍能读取月笼存在事实");
                resonance.SetHasMoonCage(teammate, true);
                Assert.That(ScriptHookEvaluator.Evaluate("Hook(455104)", context), Is.False, "月笼不能绕过战技窗口");

                var window = Add(env, owner, "ST_Artifact_455103", 3);
                Assert.That(owner.GetStatusDMGBonus("test", "test", 2, "Geo", env.Enemy.Entity),
                    Is.EqualTo(.5f).Within(.00001f), "任意创建者的有效月笼均可，含后台队友");
                resonance.SetHasMoonCage(owner, true);
                resonance.SetHasMoonCage(teammate, false);
                Assert.That(ScriptHookEvaluator.Evaluate("Hook(455104)", context), Is.True, "仍有其他创建者的月笼");
                resonance.SetHasMoonCage(owner, false);
                owner.AddShield(100f, "Geo", 2f);
                Assert.That(owner.GetStatusDMGBonus("test", "test", 2, "Geo", env.Enemy.Entity),
                    Is.EqualTo(.2f).Within(.00001f), "普通岩盾不能冒充结晶盾");
                var crystal = owner.AddOrReplaceCrystallizeShield(100f, "Pyro", 2, 2);
                Assert.That(owner.GetStatusDMGBonus("test", "test", 2, "Geo", env.Enemy.Entity),
                    Is.EqualTo(.5f).Within(.00001f));
                resonance.SetHasMoonCage(teammate, true);
                Assert.That(owner.GetStatusDMGBonus("test", "test", 2, "Geo", env.Enemy.Entity),
                    Is.EqualTo(.5f).Within(.00001f), "盾与月笼同时存在不重复加成");
                resonance.SetHasMoonCage(teammate, false);
                crystal.Value = 0f;
                Assert.That(ScriptHookEvaluator.Evaluate("Hook(455104)", context), Is.False);
                crystal.Value = 100f;
                crystal.Duration = 0f;
                Assert.That(ScriptHookEvaluator.Evaluate("Hook(455104)", context), Is.False);
                Assert.That(owner.HasStatusInstance(window), Is.True, "条件失效不移除基础20%的窗口");
                resonance.SetHasMoonCage(teammate, true);
                window.RemainingPhaseCount = 0;
                Assert.That(ScriptHookEvaluator.Evaluate("Hook(455104)", context), Is.False, "窗口到期不能仅凭月笼继续生效");
                owner.RemoveStatusInstance(window, "Expired");
                Assert.That(owner.GetStatusDMGBonus("test", "test", 2, "Geo", env.Enemy.Entity), Is.Zero);
                env.Manager.TeamBonuses.Dispose();
                resonance.SetHasMoonCage(teammate, true);
                Assert.That(resonance.HasEnemyFieldMoonCage, Is.False, "战斗结束后的旧登记不能复活");
            }
        }

        [Test]
        public void Echoes_RollsOncePerLogicalHit_MultiTargetSharesDecision_AndQueriesNeverRoll()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                var owner = env.Ally.Entity;
                var second = env.CreateEnemy(2).Entity;
                var main = Main(env, 454102, "Buff");
                main.MultiplierPart = "BaseDMGBonusFlat";
                main.ScriptHook = "Hook(454102)";
                env.DataManager.StatusEffectDict["STE_Artifact_45410201"] = new StatusEffectData
                { StatusEffectID = 45410201, EffectType = "Buff", Param1 = "0.70*TotalATK" };
                var status = Add(env, owner, main.StatusID2);
                var random = new SequenceBattleRandomSource(new[] { .4f, .5f, .4f });
                BattleRandom.SetSource(random);
                var query = new ScriptHookContext { Caster = owner, Status = status, Target = env.Enemy.Entity };
                Assert.That(ScriptHookEvaluator.Evaluate("Hook(454102)", query), Is.False);
                Assert.That(random.FloatCallCount, Is.Zero);
                for (int hit = 0; hit < 3; hit++)
                {
                    using (BattleSpecialRules.BeginHit(BattleEventHub.NextIdentity(), owner, new[] { env.Enemy.Entity, second }))
                    using (var batch = new CountDamageBatch())
                    {
                        var first = batch.Prepare(Context(owner, env.Enemy.Entity));
                        var next = batch.Prepare(Context(owner, second));
                        // .4>36%失败，.5<56%成功，成功后.4>36%再失败。
                        Assert.That(first.BaseDamageBonus > 0f, Is.EqualTo(hit == 1));
                        Assert.That(next.BaseDamageBonus, Is.EqualTo(first.BaseDamageBonus));
                        Assert.That(ScriptHookEvaluator.Evaluate("Hook(454102)", query), Is.EqualTo(hit == 1));
                        Assert.That(ScriptHookEvaluator.Evaluate("Hook(454102)", query), Is.EqualTo(hit == 1));
                        first.Resolve(new DamageResolvedEvent { HitLanded = true });
                        next.Resolve(new DamageResolvedEvent { HitLanded = true });
                    }
                }
                Assert.That(random.FloatCallCount, Is.EqualTo(3));
                Assert.That(random.IntCallCount, Is.Zero);
            }
        }

        [UnityTest]
        public IEnumerator Song_RecordsGroupHealingAcrossWearersWithoutRefresh_AndRejectsForcedTransfer()
            => HookTestEnv.AwaitTask(Song_RecordsGroupHealingAcrossWearersWithoutRefreshAsync);

        private async Task Song_RecordsGroupHealingAcrossWearersWithoutRefreshAsync()
        {
            using (var env = new BattleFlowTestEnv())
            {
                ConfigureSong(env);
                var owner = env.Ally.Entity;
                var second = env.CreateAlly(2, false).Entity;
                Add(env, second, SongOfDaysPastArtifact.ListenerID);
                HealResolvedEvent first;
                using (var heals = new HealBatch())
                {
                    heals.Add(owner, 1000f, owner);
                    heals.Add(second, 2000f, owner);
                    heals.Flush();
                    first = heals.Results[0];
                }
                await BattleExecution.Flush();
                var record = owner.GetStatus(SongOfDaysPastArtifact.RecordID);
                Assert.That(record, Is.Not.Null);
                Assert.That(record.RuntimeValue, Is.EqualTo(3000f));
                record.RemainingPhaseCount = 1;
                long order = record.ApplyOrder;
                using (ScriptHookContext.EnterExecution(new ScriptHookContext
                { Declaration = BattleEventHub.Find(first), HealEvent = first, Caster = owner }))
                    Add(env, owner, SongOfDaysPastArtifact.RecordID, 2);
                Assert.That(record.RuntimeValue, Is.EqualTo(3000f), "同一治疗声明不能重复记录");
                using (var heals = new HealBatch()) heals.Add(owner, 20000f, second);
                await BattleExecution.Flush();
                Assert.That(owner.GetStatus(SongOfDaysPastArtifact.RecordID), Is.SameAs(record));
                Assert.That(second.HasStatus(SongOfDaysPastArtifact.RecordID), Is.False);
                Assert.That(record.RuntimeValue, Is.EqualTo(15000f));
                Assert.That(record.RemainingPhaseCount, Is.EqualTo(1));
                Assert.That(record.ApplyOrder, Is.EqualTo(order));
                Assert.That(record.Caster, Is.SameAs(owner));
                Assert.That(ScriptHookEvaluator.Evaluate("Hook(455003)",
                    new ScriptHookContext { Status = record }), Is.False);
                owner.RemoveStatusInstance(record, "ForcedCleanup");
                Assert.That(owner.HasStatus(SongOfDaysPastArtifact.WaveID), Is.False);
            }
        }

        [UnityTest]
        public IEnumerator Song_FifthLogicalHitIncludesAllTargets_CancelDoesNotSpend_AndOffFieldCannotUse()
            => HookTestEnv.AwaitTask(Song_FifthLogicalHitIncludesAllTargetsAsync);

        private async Task Song_FifthLogicalHitIncludesAllTargetsAsync()
        {
            using (var env = new BattleFlowTestEnv())
            {
                ConfigureSong(env);
                var owner = env.Ally.Entity;
                var reserve = env.CreateAlly(2, false).Entity;
                var secondEnemy = env.CreateEnemy(2).Entity;
                using (var heals = new HealBatch()) heals.Add(owner, 20000f, owner);
                await BattleExecution.Flush();
                var record = owner.GetStatus(SongOfDaysPastArtifact.RecordID);
                record.RemainingPhaseCount = 0;
                await env.Ally.ExecuteStatusActionsTask(record,
                    env.DataManager.StatusActionDict[record.StatusID2], owner, "OnEnd");
                await BattleExecution.Flush();
                owner.RemoveStatusInstance(record, "Expired");
                var wave = owner.GetStatus(SongOfDaysPastArtifact.WaveID);
                Assert.That(wave, Is.Not.Null);
                Assert.That(wave.RuntimeValue, Is.EqualTo(15000f), "原记录清零不能影响已转交数值");
                Assert.That(wave.RemainingPhaseCount, Is.EqualTo(3));
                Assert.That(reserve.HasStatus(SongOfDaysPastArtifact.BonusID), Is.True);
                AssertPlan(reserve, env.Enemy.Entity, 0f, true);
                for (int hit = 0; hit < 4; hit++) AssertPlan(owner, env.Enemy.Entity, 1200f, true);
                AssertPlan(owner, env.Enemy.Entity, 1200f, false);
                using (BattleSpecialRules.BeginHit(BattleEventHub.NextIdentity(), owner, new[] { env.Enemy.Entity, secondEnemy }))
                using (var batch = new CountDamageBatch())
                {
                    var first = batch.Prepare(Context(owner, env.Enemy.Entity));
                    var second = batch.Prepare(Context(owner, secondEnemy));
                    Assert.That(first.BaseDamageBonus, Is.EqualTo(1200f));
                    Assert.That(second.BaseDamageBonus, Is.EqualTo(1200f));
                    first.Resolve(new DamageResolvedEvent { HitLanded = true, ShieldAbsorbed = 1200f });
                    second.Resolve(new DamageResolvedEvent { HitLanded = true });
                    Assert.That(owner.HasStatus(SongOfDaysPastArtifact.WaveID), Is.True);
                }
                Assert.That(owner.HasStatus(SongOfDaysPastArtifact.WaveID), Is.False);
                Assert.That(owner.HasStatus(SongOfDaysPastArtifact.BonusID), Is.False);
                Assert.That(reserve.HasStatus(SongOfDaysPastArtifact.BonusID), Is.False);
                AssertPlan(owner, env.Enemy.Entity, 0f, true);
            }
        }

        [UnityTest]
        public IEnumerator Song_OwnerDeathDropsRecordAndWave_AnotherWearerStartsANewRecord()
            => HookTestEnv.AwaitTask(Song_OwnerDeathDropsRecordAndWaveAsync);

        [TestCase("Physical", "", 58.5f, 18.5f, false)]
        [TestCase("Geo", "lunar", 205.75f, 105.75f, false)]
        [TestCase("Electro", "lunar", 403.75f, 303.75f, false)]
        [TestCase("Dendro", "lunar", 178.75f, 78.75f, false)]
        [TestCase("Cryo", "stellar", 178.75f, 78.75f, false)]
        [TestCase("Physical", "", 58.5f, 18.5f, true)]
        [TestCase("Geo", "lunar", 205.75f, 105.75f, true)]
        [TestCase("Electro", "lunar", 403.75f, 303.75f, true)]
        [TestCase("Dendro", "lunar", 178.75f, 78.75f, true)]
        [TestCase("Cryo", "stellar", 178.75f, 78.75f, true)]
        [Timeout(10000)]
        public void Song_RealSkillAndStatusDamage_AddPreparedFlatOnce_InCorrectPosition_ThroughFifthMultiTargetHit(
            string element, string properties, float withWave, float withoutWave, bool statusDamage)
        {
            using var env = new BattleFlowTestEnv();
            string effectID = statusDamage ? "STE_Artifact_45997001" : "SE_Song_Formula";
            ConfigureSong(env, setup =>
            {
                setup.ConfigureAllyAction(0, "SK_Song_Formula", "SE_Song_Formula", element, ".1*TotalATK,0", 0);
                setup.DataManager.SkillLevelDict[$"{BattleFlowTestEnv.AllyID}_0"][1].Hits2 = ".1*TotalATK,0";
                setup.DataManager.SkillEffectDict["SE_Song_Formula"].Param3 = properties;
            });
            var owner = env.Ally.Entity;
            env.Enemy.Entity.TotalHP = env.Enemy.Entity.CurrentHP = 10000f;
            env.CreateEnemy(2, 10000f);
            owner.TotalEM = 500f; owner.BaseDMGBonus = .5f; owner.BaseDMGBonusFlat = 7f;
            owner.LunarDMGBonus = owner.StellarDMGBonus = .2f; owner.Elevation = .25f;
            owner.CritRate = owner.WeaponCritRate = 0f;
            var passive = Main(env, 459980, "Buff"); passive.MultiplierPart = "BaseDMGBonusFlat";
            env.DataManager.StatusEffectDict["STE_Artifact_45998001"] = new StatusEffectData
            { StatusEffectID = 45998001, EffectType = "Buff", Param1 = "20" };
            Add(env, owner, passive.StatusID2);
            var tickingMain = Main(env, 459970, "Buff");
            env.DataManager.StatusEffectDict["STE_Artifact_45997001"] = new StatusEffectData
            { StatusEffectID = 45997001, StatusEffectID2 = "STE_Artifact_45997001", EffectType = "Damage",
                Element = element, Param1 = ".1*TotalATK,0;.1*TotalATK,0", Param3 = properties,
                DamageType = "Normal", TargetType = "Enemy", TargetSelect = "-1" };
            var tickingActions = new List<StatusActionData> { new StatusActionData
            { StatusID2 = tickingMain.StatusID2, ActionType = "OnTrigger", Param1 = "STE_Artifact_45997001" } };
            env.DataManager.StatusActionDict[tickingMain.StatusID2] = tickingActions;
            var tickingStatus = Add(env, owner, tickingMain.StatusID2);
            env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            using (var heals = new HealBatch()) heals.Add(owner, 1000f, owner);
            BattleExecution.Flush().GetAwaiter().GetResult();
            var record = owner.GetStatus(SongOfDaysPastArtifact.RecordID);
            Assert.That(record.RuntimeValue, Is.EqualTo(1000f));
            record.RemainingPhaseCount = 0;
            env.Ally.ExecuteStatusActionsTask(record, env.DataManager.StatusActionDict[record.StatusID2], owner, "OnEnd")
                .GetAwaiter().GetResult();
            BattleExecution.Flush().GetAwaiter().GetResult(); owner.RemoveStatusInstance(record, "Expired");
            var wave = owner.GetStatus(SongOfDaysPastArtifact.WaveID);
            var results = new List<float>();
            using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, fact) =>
            { if (fact.Source.SourceEffectID == effectID) results.Add(fact.DamageBeforeShield); }, 0);
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            for (int use = 0; use < 3; use++)
            {
                if (statusDamage) env.Ally.ExecuteStatusActions(tickingStatus, tickingActions, owner, "OnTrigger");
                else
                {
                    env.Ally.ForcedTargetPositions.AddRange(new[] { 1, 2 });
                    Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True);
                }
                if (use < 2) Assert.That(owner.GetStatus(SongOfDaysPastArtifact.WaveID), Is.SameAs(wave));
            }
            Assert.That(results, Has.Count.EqualTo(12));
            // 技能白值10；浪潮80、被动20和字段7只各加一次。普通先加再乘DEF=.5。
            // 特殊：(10*专属系数*1.5*(1+1.2+.2) + 80+20+7)*1.25，不让80再吃专属倍率。
            for (int i = 0; i < 10; i++) Assert.That(results[i], Is.EqualTo(withWave).Within(.002f), "受益hit/目标="+i);
            for (int i = 10; i < 12; i++) Assert.That(results[i], Is.EqualTo(withoutWave).Within(.002f), "第6hit无浪潮");
            Assert.That(owner.HasStatus(SongOfDaysPastArtifact.WaveID), Is.False);
            Assert.That(random.FloatCallCount, Is.EqualTo(12)); Assert.That(random.IntCallCount, Is.Zero);
        }

        private async Task Song_OwnerDeathDropsRecordAndWaveAsync()
        {
            using (var env = new BattleFlowTestEnv())
            {
                ConfigureSong(env);
                var first = env.Ally.Entity;
                var second = env.CreateAlly(2, false).Entity;
                Add(env, second, SongOfDaysPastArtifact.ListenerID);
                using (var heals = new HealBatch()) heals.Add(second, 1000f, first);
                await BattleExecution.Flush();
                var oldRecord = first.GetStatus(SongOfDaysPastArtifact.RecordID);
                first.TakeDamage(first.TotalHP + 1f,
                    DamageSourceInfo.Create(env.Enemy.Entity, ReactionSourceKind.EnemySkill, "test", "test"));
                Assert.That(first.HasStatusInstance(oldRecord), Is.False);
                Assert.That(first.HasStatus(SongOfDaysPastArtifact.WaveID), Is.False, "阵亡移除不能转化浪潮");
                using (var heals = new HealBatch()) heals.Add(second, 2000f, second);
                await BattleExecution.Flush();
                var record = second.GetStatus(SongOfDaysPastArtifact.RecordID);
                Assert.That(record, Is.Not.Null);
                Assert.That(record.RuntimeValue, Is.EqualTo(2000f));
                Assert.That(record.Caster, Is.SameAs(second));
                record.RemainingPhaseCount = 0;
                await second.CharacterCtrl.ExecuteStatusActionsTask(record,
                    env.DataManager.StatusActionDict[record.StatusID2], second, "OnEnd");
                await BattleExecution.Flush();
                second.RemoveStatusInstance(record, "Expired");
                Assert.That(second.HasStatus(SongOfDaysPastArtifact.WaveID), Is.True);
                second.TakeDamage(second.TotalHP + 1f,
                    DamageSourceInfo.Create(env.Enemy.Entity, ReactionSourceKind.EnemySkill, "test", "test"));
                Assert.That(second.HasStatus(SongOfDaysPastArtifact.WaveID), Is.False);
                Assert.That(second.HasStatus(SongOfDaysPastArtifact.BonusID), Is.False);
            }
        }

        [UnityTest]
        public IEnumerator ReactionHooks_UseRecordedBlessingElementsAndFieldState_NotResponseTimeState()
            => HookTestEnv.AwaitTask(ReactionHooks_UseRecordedFactsAsync);

        private async Task ReactionHooks_UseRecordedFactsAsync()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.DataManager.CharacterAttributesDict[BattleFlowTestEnv.AllyID].Element = "Pyro";
                env.CreateMinimalBattle();
                var owner = env.Ally.Entity;
                var teammate = env.CreateAlly(2, false).Entity;
                var cinder = Add(env, owner, Main(env, 455402).StatusID2);
                var blessing = new StatusMainData { StatusID = 990501, StatusID2 = "ST_NightSoulBlessing", MaxStack = 1 };
                env.DataManager.StatusMainDict[blessing.StatusID2] = blessing;
                owner.AddStatus(blessing.StatusID2, owner, 0, 1, 0, blessing);
                env.Manager.StartBattle();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                var occurrence = new ReactionOccurrence
                {
                    Type = ReactionType.Overloaded, SourceEntity = owner, Target = env.Enemy.Entity,
                    InvolvedElements = new List<string> { "Pyro", "Electro" }
                };
                var declaration = BattleEventHub.Publish(new ReactionEventData(occurrence), occurrence);
                owner.RemoveStatus(blessing.StatusID2);
                var context = new ScriptHookContext { Caster = owner, Status = cinder, Declaration = declaration };
                Assert.That(ScriptHookEvaluator.Evaluate("Hook(455402)", context), Is.True);
                Assert.That(ScriptHookEvaluator.Evaluate("Hook(45540201)", context), Is.True);
                Assert.That(ScriptHookEvaluator.Evaluate("Hook(45540208)", context), Is.True,
                    "28%档读取反应时仍有加持，不能因响应前移除而漏掉");
                Assert.That(ScriptHookEvaluator.Evaluate("Hook(45540209)", context), Is.False);

                var moon = Add(env, owner, Main(env, 455802).StatusID2);
                env.DataManager.StatusActionDict[moon.StatusID2] = new List<StatusActionData>
                { new StatusActionData { StatusID2 = moon.StatusID2, ActionType = "OnTrigger",
                    ScriptHook = "Hook(455802)", Param1 = "STE_Test_MoonEnergy" } };
                env.DataManager.StatusEffectDict["STE_Test_MoonEnergy"] = new StatusEffectData
                { StatusEffectID2 = "STE_Test_MoonEnergy", EffectType = "GainEnergy", Param1 = "1", Param2 = "Flat", TargetType = "Self" };
                owner.CurrentEnergy = 0f;
                var lunar = new ReactionOccurrence { Type = ReactionType.LunarElectroCharged, SourceEntity = teammate,
                    Target = env.Enemy.Entity, InvolvedElements = new List<string> { "Hydro", "Electro" } };
                using (BattleResolutionContext.BeginCollection())
                {
                    BattleEventHub.Publish(new ReactionEventData(lunar), lunar);
                    env.Ally.IsActive = false; // 发生时前台，响应前切出，仍应触发。
                }
                await BattleExecution.Flush();
                Assert.That(owner.CurrentEnergy, Is.EqualTo(1f));
                BattleEventHub.Publish(new ReactionEventData(lunar), lunar);
                await BattleExecution.Flush();
                Assert.That(owner.CurrentEnergy, Is.EqualTo(1f), "同一反应声明不得重复发奖");
                using (BattleResolutionContext.BeginCollection())
                {
                    BattleEventHub.Publish(new ReactionEventData(new ReactionOccurrence
                    { Type = ReactionType.LunarBloom, SourceEntity = teammate, Target = env.Enemy.Entity }));
                    env.Ally.IsActive = true; // 发生时后台，响应前切入，不能补领。
                }
                await BattleExecution.Flush();
                Assert.That(owner.CurrentEnergy, Is.EqualTo(1f));
            }
        }

        private static void AssertPlan(BattleEntity attacker, BattleEntity target, float bonus, bool landed)
        {
            using (BattleSpecialRules.BeginHit(BattleEventHub.NextIdentity(), attacker, new[] { target }))
            using (var batch = new CountDamageBatch())
            {
                var plan = batch.Prepare(Context(attacker, target));
                Assert.That(plan.BaseDamageBonus, Is.EqualTo(bonus));
                plan.Resolve(new DamageResolvedEvent { HitLanded = landed });
            }
        }
        private static CountDamageContext Context(BattleEntity attacker, BattleEntity target) => new CountDamageContext
        { Attacker = attacker, Target = target, SourceKind = ReactionSourceKind.CharacterSkill, DamageType = "Normal" };
        private static StatusMainData Main(BattleFlowTestEnv env, int id, string type = "")
        {
            var main = new StatusMainData { StatusID = id, StatusID2 = "ST_Artifact_" + id,
                StatusType = type, MaxCount = 1, MaxStack = 1, WhenMax = "0" };
            env.DataManager.StatusMainDict[main.StatusID2] = main;
            return main;
        }
        private static StatusInstance Add(BattleFlowTestEnv env, BattleEntity host, string id, int duration = 0)
            => host.AddStatus(id, host, duration, 1, 0, env.DataManager.StatusMainDict[id]);
        private static void AddReactionBaseBonus(BattleFlowTestEnv env, int id, string reaction, string value)
        {
            var main = Main(env, id, "Buff");
            main.MultiplierPart = "BaseDMGBonus";
            main.ApplyReactionType = reaction;
            env.DataManager.StatusEffectDict["STE_Artifact_" + id + "01"] = new StatusEffectData
            { StatusEffectID = id * 100 + 1, EffectType = "Buff", Param1 = value };
            Add(env, env.Ally.Entity, main.StatusID2);
        }
        private static void AddUnrelatedProbabilityBonus(BattleFlowTestEnv env)
        {
            var main = Main(env, 459993, "Buff");
            main.MultiplierPart = "DMGBonus";
            main.ScriptHook = "Chance(0.5)";
            env.DataManager.StatusEffectDict["STE_Artifact_45999301"] = new StatusEffectData
            { StatusEffectID = 45999301, EffectType = "Buff", Param1 = "0.9" };
            Add(env, env.Ally.Entity, main.StatusID2);
        }
        private static void ConfigureSong(BattleFlowTestEnv env, System.Action<BattleFlowTestEnv> configure = null)
        {
            configure?.Invoke(env);
            env.CreateMinimalBattle();
            Main(env, 455002); Main(env, 455003, "Count"); Main(env, 455004, "Count");
            var bonus = Main(env, 455005, "Buff");
            bonus.MultiplierPart = "BaseDMGBonusFlat";
            bonus.ApplyDamageType = "Normal;Heavy;Skill;Burst";
            bonus.ScriptHook = "Hook(455005)";
            env.DataManager.StatusActionDict[SongOfDaysPastArtifact.ListenerID] = new List<StatusActionData>
            { new StatusActionData { StatusID2 = SongOfDaysPastArtifact.ListenerID, ActionType = "OnTrigger",
                WhenHealing = "All", Param1 = "STE_Artifact_45500201" } };
            env.DataManager.StatusActionDict[SongOfDaysPastArtifact.RecordID] = new List<StatusActionData>
            { new StatusActionData { StatusID2 = SongOfDaysPastArtifact.RecordID, ActionType = "OnEnd",
                ScriptHook = "Hook(455003)", Param1 = "STE_Artifact_45500301" } };
            foreach (var effect in new[]
            {
                new StatusEffectData { StatusEffectID = 45500201, StatusEffectID2 = "STE_Artifact_45500201",
                    EffectType = "ApplyStatus", Param1 = SongOfDaysPastArtifact.RecordID, TargetType = "Allies",
                    TargetSelect = "-1", TargetOverride = "Hook(45500201)", Duration = 2, AddInPhase = 1 },
                new StatusEffectData { StatusEffectID = 45500301, StatusEffectID2 = "STE_Artifact_45500301",
                    EffectType = "ApplyStatus", Param1 = SongOfDaysPastArtifact.WaveID, TargetType = "Self",
                    Duration = 3, AddInPhase = 1 },
                new StatusEffectData { StatusEffectID = 45500401, StatusEffectID2 = "STE_Artifact_45500401",
                    EffectType = "BindStatus", Param1 = SongOfDaysPastArtifact.BonusID, TargetType = "Allies",
                    TargetSelect = "-1", AddInPhase = 1 },
                new StatusEffectData { StatusEffectID = 45500501, StatusEffectID2 = "STE_Artifact_45500501",
                    EffectType = "Buff", Param1 = "0.08 * Count(ST_Artifact_455004)", TargetType = "Self" }
            }) env.DataManager.StatusEffectDict[effect.StatusEffectID2] = effect;
            Add(env, env.Ally.Entity, SongOfDaysPastArtifact.ListenerID);
        }
    }
}
