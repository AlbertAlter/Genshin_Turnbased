using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GenshinTurnBased.Tests.EditMode
{
    // 处理器回归：构造内存配表与明确声明，不读取Charts，不自动运行Unity。
    public class VentiScriptHooksTests
    {
        private static StatusInstance Burst(BattleFlowTestEnv env)
        {
            var owner = env.Ally.Entity;
            owner.EntityID = 1012;
            var main = new StatusMainData { StatusID = 4101203, StatusID2 = VentiScriptHooks.BurstID };
            env.DataManager.StatusMainDict[main.StatusID2] = main;
            return owner.AddStatus(main.StatusID2, owner, 2, 2, 1, main);
        }

        private static ReactionResult Swirls(BattleEntity owner, BattleEntity target, string effect, params string[] elements)
        {
            var result = new ReactionResult();
            foreach (string element in elements)
            {
                var fact = new ReactionOccurrence
                {
                    Type = ReactionType.Swirl, SourceEntity = owner, Target = target,
                    SourceEffectID = effect, InvolvedElements = new List<string> { "Anemo", element }
                };
                fact.Declaration = BattleEventHub.Record(fact, new ReactionEventData(fact));
                result.TriggeredReactions.Add(fact);
            }
            return result;
        }

        [Test]
        public void Absorption_UsesWholeHitPriorityLocksAndDoesNotLeakAcrossBurstInstances()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                var owner = env.Ally.Entity;
                owner.EntityID = 1012;
                using (BattleSpecialRules.BeginSkill(owner))
                {
                    var first = Burst(env);
                    BattleSpecialRules.OnAnemoResult(owner, VentiScriptHooks.BurstInitialDamageID,
                        VentiScriptHooks.BurstRecordHook, null,
                        Swirls(owner, env.Enemy.Entity, VentiScriptHooks.BurstInitialDamageID, "Cryo", "Hydro"));
                    Assert.That(VentiScriptHooks.GetAbsorbedElement(first), Is.EqualTo("Hydro"));
                    BattleSpecialRules.OnAnemoResult(owner, VentiScriptHooks.BurstWindDamageID,
                        VentiScriptHooks.BurstRecordHook, first,
                        Swirls(owner, env.Enemy.Entity, VentiScriptHooks.BurstWindDamageID, "Pyro"));
                    Assert.That(VentiScriptHooks.GetAbsorbedElement(first), Is.EqualTo("Hydro"));
                    using (BattleSpecialRules.BeginSkill(owner))
                    {
                        var second = Burst(env);
                        Assert.That(VentiScriptHooks.GetAbsorbedElement(second), Is.Null);
                        BattleSpecialRules.OnAnemoResult(owner, VentiScriptHooks.BurstInitialDamageID,
                            VentiScriptHooks.BurstRecordHook, null,
                            Swirls(owner, env.Enemy.Entity, VentiScriptHooks.BurstInitialDamageID, "Electro", "Pyro"));
                        Assert.That(VentiScriptHooks.GetAbsorbedElement(second), Is.EqualTo("Pyro"));
                    }
                    Assert.That(VentiScriptHooks.GetAbsorbedElement(first), Is.EqualTo("Hydro"));
                    owner.RemoveStatusInstance(first);
                    Assert.That(VentiScriptHooks.GetAbsorbedElement(first), Is.Null);
                }
            }
        }

        [UnityTest]
        public IEnumerator BonusDamage_UsesLockedElementWithoutMutatingTheSharedRowOrDrawingWhenEmpty()
            => HookTestEnv.AwaitTask(BonusDamageAsync);

        private async Task BonusDamageAsync()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                var owner = env.Ally.Entity;
                owner.EntityID = 1012;
                using (BattleSpecialRules.BeginSkill(owner))
                {
                    var burst = Burst(env);
                    var row = new StatusEffectData
                    {
                        StatusEffectID2 = VentiScriptHooks.BurstBonusDamageID, EffectType = "Damage",
                        DamageHook = VentiScriptHooks.BurstBonusHook, Element = "Physical", Param1 = "0.188*TotalATK,1"
                    };
                    int calls = 0;
                    var context = new StatusDamageExecution
                    {
                        Effect = row, Status = burst,
                        ExecuteDefault = prepared =>
                        {
                            calls++;
                            Assert.That(prepared, Is.Not.SameAs(row));
                            Assert.That(prepared.Element, Is.EqualTo("Hydro"));
                            Assert.That(prepared.Param1, Is.EqualTo(row.Param1));
                            return Task.CompletedTask;
                        }
                    };
                    var random = new SequenceBattleRandomSource();
                    BattleRandom.SetSource(random);
                    await BattleSpecialRules.ExecuteStatusDamage(context);
                    Assert.That(calls, Is.Zero);
                    Assert.That(random.FloatCallCount, Is.Zero);
                    BattleSpecialRules.OnAnemoResult(owner, VentiScriptHooks.BurstWindDamageID,
                        VentiScriptHooks.BurstRecordHook, burst,
                        Swirls(owner, env.Enemy.Entity, VentiScriptHooks.BurstWindDamageID, "Hydro"));
                    await BattleSpecialRules.ExecuteStatusDamage(context);
                    Assert.That(calls, Is.EqualTo(1));
                    Assert.That(row.Element, Is.EqualTo("Physical"));
                }
            }
        }

        [Test]
        public void SkillTargets_UseActualHitsExplicitMinionMetadataAndIsolateNestedSkills()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                var owner = env.Ally.Entity;
                owner.EntityID = 1012;
                env.DataManager.EnemyMainDict[BattleFlowTestEnv.EnemyID].EnemyType = "Minion";
                using (BattleSpecialRules.BeginSkill(owner))
                {
                    using (var batch = new HitBatch())
                    {
                        batch.Resolve(new HitRequest
                        {
                            Target = env.Enemy.Entity, DamageBeforeShield = 1f, Element = "Anemo",
                            Source = DamageSourceInfo.Create(owner, ReactionSourceKind.CharacterSkill,
                                VentiScriptHooks.SkillID, VentiScriptHooks.SkillDamageID)
                        });
                        batch.Flush();
                    }
                    Assert.That(BattleSpecialRules.TryResolveTargets(VentiScriptHooks.AirborneTargetsHook,
                        env.Manager, owner, out var positions), Is.True);
                    CollectionAssert.AreEqual(new[] { 1 }, positions);
                    using (BattleSpecialRules.BeginSkill(env.Enemy.Entity))
                    {
                        BattleSpecialRules.TryResolveTargets(VentiScriptHooks.SkillTargetsHook,
                            env.Manager, owner, out positions);
                        Assert.That(positions, Is.Empty);
                    }
                    env.DataManager.EnemyMainDict[BattleFlowTestEnv.EnemyID].EnemyType = null;
                    BattleSpecialRules.TryResolveTargets(VentiScriptHooks.AirborneTargetsHook,
                        env.Manager, owner, out positions);
                    Assert.That(positions, Is.Empty); // 未分类只跳过击飞，仍保留真实命中记录。
                    env.DataManager.EnemyMainDict[BattleFlowTestEnv.EnemyID].EnemyType = "Elite";
                    BattleSpecialRules.TryResolveTargets(VentiScriptHooks.AirborneTargetsHook,
                        env.Manager, owner, out positions);
                    Assert.That(positions, Is.Empty);
                    BattleSpecialRules.TryResolveTargets(VentiScriptHooks.SkillTargetsHook,
                        env.Manager, owner, out positions);
                    CollectionAssert.AreEqual(new[] { 1 }, positions);
                }
            }
        }

        [Test]
        public void BurstEnergy_UsesConcreteBindingAndInnatePartyElementIncludingOffFieldMembers()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                var owner = env.Ally.Entity; owner.EntityID = 1012;
                var hydro = env.CreateAlly(2).Entity; hydro.EntityID = 50001;
                var pyro = env.CreateAlly(3).Entity; pyro.EntityID = 50002;
                env.DataManager.CharacterAttributesDict[50001] = new CharacterAttributesData { Element = "Hydro" };
                env.DataManager.CharacterAttributesDict[50002] = new CharacterAttributesData { Element = "Pyro" };
                using (BattleSpecialRules.BeginSkill(owner))
                {
                    var burst = Burst(env);
                    var childMain = new StatusMainData { StatusID = 4101212, StatusID2 = VentiScriptHooks.BurstEndPulseID };
                    var child = owner.AddStatus(childMain.StatusID2, owner, 2, 2, 3, childMain);
                    burst.BoundStatuses.Add(child);
                    BattleSpecialRules.OnAnemoResult(owner, VentiScriptHooks.BurstWindDamageID,
                        VentiScriptHooks.BurstRecordHook, child,
                        Swirls(owner, env.Enemy.Entity, VentiScriptHooks.BurstWindDamageID, "Hydro"));
                    Assert.That(VentiScriptHooks.GetAbsorbedElement(child), Is.EqualTo("Hydro"));
                    using (ScriptHookContext.EnterExecution(new ScriptHookContext { Caster = owner, Status = burst }))
                    {
                        BattleSpecialRules.TryResolveTargets("Venti_BurstEnergy()", env.Manager, owner, out var positions);
                        CollectionAssert.AreEqual(new[] { 1, 2 }, positions);
                    }
                    owner.RemoveStatusInstance(burst);
                    Assert.That(owner.HasStatusInstance(child), Is.False);
                    Assert.That(VentiScriptHooks.GetAbsorbedElement(child), Is.Null);
                }
            }
        }

        [Test]
        public void C2Resistance_ChecksEachListedElementAndCurrentAirborneState()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                var owner = env.Ally.Entity;
                owner.EntityID = 1012; owner.ConstellationLevel = 2;
                var target = env.Enemy.Entity;
                foreach (var pair in new[]
                {
                    new KeyValuePair<int, string>(4101205, VentiScriptHooks.BaseDebuffID),
                    new KeyValuePair<int, string>(4101206, VentiScriptHooks.AirborneDebuffID)
                })
                {
                    var main = new StatusMainData
                    {
                        StatusID = pair.Key, StatusID2 = pair.Value, StatusType = "Debuff",
                        MultiplierPart1 = "ResBonus", ApplyElementType = "Anemo;Physical",
                        ScriptHook = pair.Value == VentiScriptHooks.AirborneDebuffID ? "1012_C2()" : null
                    };
                    env.DataManager.StatusMainDict[main.StatusID2] = main;
                    env.DataManager.StatusEffectDict["Effect" + pair.Key] = new StatusEffectData
                    {
                        StatusEffectID = pair.Key * 100 + 1, StatusEffectID2 = "Effect" + pair.Key,
                        EffectType = "Debuff", Param1 = "-0.12"
                    };
                    target.AddStatus(main.StatusID2, owner, 2, 2, 0, main);
                }
                Assert.That(target.GetStatusResBonus("Anemo"), Is.EqualTo(-0.12f).Within(0.00001f));
                target.StatusDict[AirborneStatusHandler.EnemyStatusID] = new StatusInstance
                    { StatusID2 = AirborneStatusHandler.EnemyStatusID, IsActive = true };
                Assert.That(target.GetStatusResBonus("Anemo"), Is.EqualTo(-0.24f).Within(0.00001f));
                Assert.That(target.GetStatusResBonus("Physical"), Is.EqualTo(-0.24f).Within(0.00001f));
                Assert.That(target.GetStatusResBonus("Pyro"), Is.Zero);
                target.RemoveStatus(AirborneStatusHandler.EnemyStatusID);
                Assert.That(target.GetStatusResBonus("Anemo"), Is.EqualTo(-0.12f).Within(0.00001f));
            }
        }

        [Test]
        public void C6Resistance_KeepsTheApplicationElementAfterItsBurstEnds()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                var owner = env.Ally.Entity;
                owner.EntityID = 1012; owner.ConstellationLevel = 6;
                using (BattleSpecialRules.BeginSkill(owner))
                {
                    var burst = Burst(env);
                    BattleSpecialRules.OnAnemoResult(owner, VentiScriptHooks.BurstWindDamageID,
                        VentiScriptHooks.BurstRecordHook, burst,
                        Swirls(owner, env.Enemy.Entity, VentiScriptHooks.BurstWindDamageID, "Cryo"));
                    var main = new StatusMainData
                    {
                        StatusID = 4101211, StatusID2 = "ST_Venti_C6Debuff2", StatusType = "Debuff",
                        MultiplierPart1 = "ResBonus", ApplyElementType = "Pyro;Hydro;Electro;Cryo",
                        ScriptHook = "1012_C6Element()"
                    };
                    env.DataManager.StatusMainDict[main.StatusID2] = main;
                    env.DataManager.StatusEffectDict["STE_Venti_C6Debuff2"] = new StatusEffectData
                    {
                        StatusEffectID = 410121101, StatusEffectID2 = "STE_Venti_C6Debuff2",
                        EffectType = "Debuff", Param1 = "-0.2"
                    };
                    var hit = new DamageResolvedEvent
                    {
                        HitLanded = true, Target = env.Enemy.Entity,
                        Source = DamageSourceInfo.FromStatus(burst, VentiScriptHooks.BurstWindDamageID)
                    };
                    using (ScriptHookContext.EnterExecution(new ScriptHookContext { Caster = owner, DamageEvent = hit }))
                        env.Enemy.Entity.AddStatus(main.StatusID2, owner, 2, 2, 0, main);
                    owner.RemoveStatusInstance(burst);
                    Assert.That(env.Enemy.Entity.GetStatusResBonus("Cryo"), Is.EqualTo(-0.2f).Within(0.00001f));
                    Assert.That(env.Enemy.Entity.GetStatusResBonus("Pyro"), Is.Zero);
                }
            }
        }

        [UnityTest]
        public IEnumerator ConvertedDamage_FiltersByCommittedElementRatherThanPlaceholderMetadata()
            => HookTestEnv.AwaitTask(ConvertedDamageFiltersAsync);

        private async Task ConvertedDamageFiltersAsync()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                var owner = env.Ally.Entity; owner.EntityID = 1012;
                owner.CurrentEnergy = 0; owner.MaxEnergy = 80;
                var observer = new StatusMainData { StatusID = 9900042, StatusID2 = "ST_HydroObserver" };
                env.DataManager.StatusMainDict[observer.StatusID2] = observer;
                env.DataManager.StatusActionDict[observer.StatusID2] = new List<StatusActionData>
                {
                    new StatusActionData { StatusID2 = observer.StatusID2, ActionType = "OnTrigger",
                        Param1 = "STE_HydroObserver", OutgoingHit = "Hydro" }
                };
                env.DataManager.StatusEffectDict["STE_HydroObserver"] = new StatusEffectData
                {
                    StatusEffectID = 990004201, StatusEffectID2 = "STE_HydroObserver",
                    EffectType = "GainEnergy", Param1 = "1", Param2 = "Flat", TargetType = "Self"
                };
                env.DataManager.StatusEffectDict[VentiScriptHooks.BurstBonusDamageID] = new StatusEffectData
                {
                    StatusEffectID = 410120302, StatusEffectID2 = VentiScriptHooks.BurstBonusDamageID,
                    EffectType = "Damage", DamageType = "Burst", Element = "Physical"
                };
                owner.AddStatus(observer.StatusID2, owner, 0, 2, 0, observer);
                var burst = Burst(env);
                async Task Hit(string element)
                {
                    await BattleExecution.Run(() =>
                    {
                        using (var batch = new HitBatch())
                        {
                            batch.Resolve(new HitRequest
                            {
                                Target = env.Enemy.Entity, DamageBeforeShield = 1f,
                                Element = element, DamageType = "Burst",
                                Source = DamageSourceInfo.FromStatus(burst, VentiScriptHooks.BurstBonusDamageID)
                            });
                            batch.Flush();
                        }
                        return Task.CompletedTask;
                    });
                }
                await Hit("Hydro");
                Assert.That(owner.CurrentEnergy, Is.EqualTo(1f));
                await Hit("Physical");
                Assert.That(owner.CurrentEnergy, Is.EqualTo(1f));
            }
        }

        [UnityTest]
        public IEnumerator C4_RespondsOnlyToOwnActualPositiveEnergyAndRefreshesOneBuff()
            => HookTestEnv.AwaitTask(C4EnergyAsync);

        private async Task C4EnergyAsync()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                var owner = env.Ally.Entity;
                owner.EntityID = 1012; owner.ConstellationLevel = 4; owner.CurrentEnergy = 0; owner.MaxEnergy = 80;
                var listener = new StatusMainData { StatusID = 4101207, StatusID2 = "ST_Venti_C4" };
                var buff = new StatusMainData { StatusID = 4101208, StatusID2 = "ST_Venti_C4Buff", MaxStack = 1, WhenMax = "0" };
                env.DataManager.StatusMainDict[listener.StatusID2] = listener;
                env.DataManager.StatusMainDict[buff.StatusID2] = buff;
                env.DataManager.StatusActionDict[listener.StatusID2] = new List<StatusActionData>
                {
                    new StatusActionData { StatusID2 = listener.StatusID2, ActionType = "OnTrigger", Param1 = "STE_Venti_C4", ScriptHook = "1012_C4()" }
                };
                env.DataManager.StatusEffectDict["STE_Venti_C4"] = new StatusEffectData
                {
                    StatusEffectID = 410120701, StatusEffectID2 = "STE_Venti_C4", EffectType = "ApplyStatus",
                    Param1 = buff.StatusID2, TargetType = "Self", Duration = 2, AddInPhase = 2
                };
                owner.AddStatus(listener.StatusID2, owner, 0, 2, 0, listener);
                async Task Gain(float amount)
                {
                    await BattleExecution.Run(() =>
                    {
                        CharacterBattleController.ApplyEnergyGain(owner, amount, "Flat", "Physical");
                        return Task.CompletedTask;
                    });
                }
                await Gain(1.25f);
                var first = owner.GetStatus(buff.StatusID2);
                Assert.That(first, Is.Not.Null);
                await Gain(0.25f);
                Assert.That(owner.GetStatusInstances(buff.StatusID2).Count, Is.EqualTo(1));
                Assert.That(owner.GetStatus(buff.StatusID2), Is.Not.SameAs(first));
                owner.CurrentEnergy = 80;
                var full = owner.GetStatus(buff.StatusID2);
                await Gain(1f);
                Assert.That(owner.GetStatus(buff.StatusID2), Is.SameAs(full));
                await Gain(-1f);
                Assert.That(owner.GetStatus(buff.StatusID2), Is.SameAs(full));
            }
        }
    }
}
