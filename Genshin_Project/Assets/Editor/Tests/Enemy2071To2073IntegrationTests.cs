using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    public class Enemy2071To2073IntegrationTests
    {
        [Test]
        public void Withdraw_ZeroesHP_ReleasesSlot_SkipsDeathAndCachedHit()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            env.CreateEnemy(2); // 撤离后战斗继续，避免只验证“战斗结束”拦截。
            var entity = env.Enemy.Entity; var field = entity.Position; float hp = entity.CurrentHP;
            var personalStatus = Definition(env.DataManager, 60090, "ST_Test_WithdrawCleanup");
            entity.AddStatus(personalStatus.StatusID2, entity, 0, 6, 0, personalStatus);
            int deaths = 0, withdrawn = 0, hpChanges = 0;
            using var death = BattleEventHub.Subscribe<DeathEventData>(BattleEventKind.Death, (_, __) => deaths++, 0);
            using var leave = BattleEventHub.Subscribe<WithdrawnEventData>(BattleEventKind.Withdrawn, (_, __) => withdrawn++, 0);
            using var hpChanged = BattleEventHub.Subscribe<HPChangedEventData>(BattleEventKind.HPChanged, (_, change) => {
                if (change.Subject != entity) return;
                hpChanges++; Assert.That(change.Reason, Is.EqualTo("Withdrawn"));
                Assert.That(change.Before, Is.EqualTo(hp)); Assert.That(change.After, Is.Zero);
                Assert.That(entity.CurrentHP, Is.Zero); Assert.That(entity.IsDead, Is.True);
                Assert.That(field.Occupant, Is.Null);
                Assert.That(env.Manager.Enemies.Any(e => e == env.Enemy), Is.False);
            }, 0);
            Assert.That(env.Manager.WithdrawEnemy(env.Enemy), Is.True);
            Assert.That(entity.CurrentHP, Is.Zero); Assert.That(entity.IsDead, Is.True);
            Assert.That(entity.IsOnField, Is.False); Assert.That(field.Occupant, Is.Null);
            Assert.That(entity.GetStatusList(), Is.Empty);
            Assert.That(env.Manager.Enemies.Any(e => e == env.Enemy), Is.False); Assert.That(env.Enemy.CanAct, Is.False);
            using (var batch = new HitBatch()) Assert.That(batch.Resolve(new HitRequest {
                Target = entity, DamageBeforeShield = hp * 10, Element = "True",
                Source = DamageSourceInfo.Create(env.Ally.Entity, ReactionSourceKind.CharacterSkill, "SK_Leave", "SE_Leave") }), Is.Null);
            Assert.That(entity.CurrentHP, Is.Zero); Assert.That(deaths, Is.Zero); Assert.That(withdrawn, Is.EqualTo(1));
            ReviveEffect.Execute("100", env.Ally.Entity, new[] { entity });
            Assert.That(entity.CurrentHP, Is.Zero, "已退场对象不得通过旧目标引用复活回战斗。");
            Assert.That(env.Manager.IsBattleOver, Is.False); Assert.That(env.Manager.WithdrawEnemy(env.Enemy), Is.False);
            Assert.That(hpChanges, Is.EqualTo(1));
        }

        [Test]
        public void LastEnemyWithdraws_EndsBattleOnce_WithoutDeath()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle(); int deaths = 0, endings = 0;
            using var death = BattleEventHub.Subscribe<DeathEventData>(BattleEventKind.Death, (_, __) => deaths++, 0);
            using var end = BattleEventHub.Subscribe<BattleEndedEventData>(BattleEventKind.BattleEnded, (_, __) => endings++, 0);
            env.Manager.WithdrawEnemy(env.Enemy); env.Manager.CheckBattleEnd();
            Assert.That(env.Manager.IsBattleOver, Is.True); Assert.That(env.Manager.Victory, Is.True);
            Assert.That(deaths, Is.Zero); Assert.That(endings, Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ImmuneBoundShield_BreaksWithoutOverflow_ThenNextHitDamagesHP(bool elemental)
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            var data = env.DataManager; var target = env.Enemy.Entity;
            var shield = Definition(data, 60001, "ST_Test_BoundShield", "Shield");
            Definition(data, 60002, "ST_Test_BoundImmune", "Immune");
            data.StatusEffectDict["STE_Test_Shield"] = new StatusEffectData {
                StatusEffectID = 6000101, StatusEffectID2 = "STE_Test_Shield", EffectType = "Shield",
                Element = elemental ? "Hydro" : "None", Param1 = elemental ? "1U" : "100", TargetType = "Self" };
            data.StatusEffectDict["STE_Test_Bind"] = new StatusEffectData {
                StatusEffectID = 6000102, StatusEffectID2 = "STE_Test_Bind", EffectType = "BindStatus",
                Param1 = "ST_Test_BoundImmune", TargetType = "Self" };
            data.StatusEffectDict["STE_Test_Immune"] = new StatusEffectData {
                StatusEffectID = 6000201, StatusEffectID2 = "STE_Test_Immune", EffectType = "Immune", Element = "Physical;Cryo" };
            if (elemental) data.EnemyShieldRules.Add(EnemyShieldRuleParser.Parse("1U", "Hydro", "Cryo"));
            target.AddStatus(shield.StatusID2, target, 0, 6, 0, shield);
            float hp = target.CurrentHP;
            HitResolution Hit(float amount) {
                var contact = elemental ? ReactionResolver.Resolve(new ReactionContext {
                    SourceEntity = env.Ally.Entity, Target = target, SourceKind = ReactionSourceKind.CharacterSkill,
                    SourceSkillID = "SK_Shield", SourceEffectID = "SE_Shield", AttackElement = "Cryo", AttackAmount = 2,
                    PreReactionDamage = amount, DamageComponents = new ReactionDamageComponents { SkillBaseDamage = amount } }) : null;
                using var batch = new HitBatch();
                return batch.Resolve(new HitRequest { Target = target, Element = elemental ? "Cryo" : "Physical",
                    ElementAmount = 2, DamageBeforeShield = amount, EnemyShieldHit = contact?.EnemyShieldHit,
                    Source = DamageSourceInfo.Create(env.Ally.Entity, ReactionSourceKind.CharacterSkill, "SK_Shield", "SE_Shield") });
            }
            var first = Hit(200); Assert.That(first.DamageEvent.IsImmune, Is.True);
            Assert.That(target.CurrentHP, Is.EqualTo(hp)); Assert.That(target.HasStatus(shield.StatusID2), Is.False);
            Assert.That(target.HasStatus("ST_Test_BoundImmune"), Is.False);
            if (!elemental) {
                Assert.That(first.DamageEvent.ShieldAbsorbed, Is.EqualTo(100));
                Assert.That(first.DamageEvent.ImmuneDamage, Is.EqualTo(100));
            }
            Hit(20); Assert.That(target.CurrentHP, Is.EqualTo(hp - 20));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void QueuedWhale_PrecedesLeapCheck_EvenWhenLeapMarkExpired(bool expired)
        {
            using var env = Childe(); var owner = env.Enemy.Entity;
            Assert.That(env.Enemy.EnterHealthPhase(3), Is.True);
            var random = new SequenceBattleRandomSource(new[] { .5f }); BattleRandom.SetSource(random);
            Add(env, env.Ally.Entity, Enemy2071To2073Hooks.Hydro, owner, 2);
            Add(env, owner, Enemy2071To2073Hooks.Leap, owner);
            Add(env, owner, Enemy2071To2073Hooks.CCharge, owner, 1);
            if (expired) env.Ally.Entity.RemoveStatus(Enemy2071To2073Hooks.Hydro);
            typeof(BattleManager).GetProperty("TurnCount").SetValue(env.Manager, Math.Max(1, env.Manager.TurnCount) + 1);
            Assert.That(BattleSpecialRules.SelectReservedEnemySkill(env.Enemy), Is.EqualTo(207334), "首次行动必须先放上回合已蓄力的鲸鱼。");
            Assert.That(owner.HasStatus(Enemy2071To2073Hooks.Leap), Is.True, "鲸鱼不能提前取消或检查雷跃预约。");
            Assert.That(BattleSpecialRules.SelectReservedEnemySkill(env.Enemy), expired ? Is.Null : Is.EqualTo(207335));
            Assert.That(owner.HasStatus(Enemy2071To2073Hooks.Leap), Is.False);
            Assert.That(BattleSpecialRules.SelectReservedEnemySkill(env.Enemy), Is.Null, "旧预约不得重复检查。");
            Assert.That(random.FloatCallCount, Is.EqualTo(1)); Assert.That(random.IntCallCount, Is.Zero);
        }

        [UnityEngine.TestTools.UnityTest]
        public IEnumerator PhaseExit_SuppressesDestroyForBoundStates_AndCancelsChargedAttacks()
            => HookTestEnv.AwaitTask(PhaseCleanupAsync);

        private static async Task PhaseCleanupAsync()
        {
            using var env = Childe();
            var owner = env.Enemy.Entity;
            const string shieldID = "ST_Childe_HydroShield";
            const string childID = "ST_Test_PhaseShieldChild";
            const string markerID = "ST_Test_PhaseShieldResult";
            const string effectID = "STE_Test_PhaseShieldResult";
            Definition(env.DataManager, 60024, shieldID).ClearOnPhaseExit = true;
            Definition(env.DataManager, 60025, childID);
            Definition(env.DataManager, 60026, markerID);
            env.DataManager.StatusEffectDict[effectID] = new StatusEffectData {
                StatusEffectID2 = effectID, EffectType = "ApplyStatus", Param1 = markerID, TargetType = "Self" };
            env.DataManager.StatusActionDict[shieldID] = new List<StatusActionData> {
                new StatusActionData { StatusID2 = shieldID, ActionType = "OnDestroy", Param1 = effectID, ScriptHook = "ChildeHydroBroken()" }
            };
            env.DataManager.StatusActionDict[childID] = new List<StatusActionData> {
                new StatusActionData { StatusID2 = childID, ActionType = "OnDestroy", Param1 = effectID }
            };
            await BattleExecution.Run(async () => {
                Add(env, owner, shieldID, owner);
                owner.RemoveStatusInstance(owner.GetStatus(shieldID), "ShieldBroken");
                await BattleExecution.Flush();
                Assert.That(owner.HasStatus(markerID), Is.True);
                owner.RemoveStatus(markerID);

                Add(env, owner, shieldID, owner);
                Add(env, owner, childID, owner);
                var child = owner.GetStatus(childID);
                owner.GetStatus(shieldID).BoundStatuses.Add(child);
                BattleRandom.SetSource(new SequenceBattleRandomSource(new[] { .5f }));
                Add(env, env.Ally.Entity, Enemy2071To2073Hooks.Hydro, owner, 2);
                foreach (string id in new[] { Enemy2071To2073Hooks.Leap, Enemy2071To2073Hooks.CCharge })
                {
                    env.DataManager.StatusMainDict[id].ClearOnPhaseExit = true;
                    Add(env, owner, id, owner);
                }
                Assert.That(env.Enemy.EnterHealthPhase(3), Is.True);
                await BattleExecution.Flush();
                Assert.That(child.IsActive, Is.False);
                Assert.That(owner.HasStatus(markerID), Is.False);
                typeof(BattleManager).GetProperty("TurnCount").SetValue(env.Manager, Math.Max(1, env.Manager.TurnCount) + 1);
                Assert.That(BattleSpecialRules.SelectReservedEnemySkill(env.Enemy), Is.Null);
            });
        }

        [UnityEngine.TestTools.UnityTest]
        public IEnumerator LethalHit_ChangesPhaseAfterExecution_WithoutDeathOrEarlyVictory()
            => HookTestEnv.AwaitTask(PhaseAsync);

        private async Task PhaseAsync()
        {
            using var env = Childe(); var owner = env.Enemy.Entity; int deaths = 0;
            var phaseTwo = env.DataManager.EnemyPhaseDict[2073][2];
            var phaseInit = Definition(env.DataManager, 60020, "ST_Test_Phase2");
            phaseTwo.PhaseInitiateStatuses.Add(phaseInit.StatusID2);
            var persistent = Definition(env.DataManager, 60021, "ST_Test_Persistent");
            var marked = Definition(env.DataManager, 60022, "ST_Test_PhaseBound");
            marked.ClearOnPhaseExit = true;
            Add(env, owner, persistent.StatusID2, env.Ally.Entity);
            Add(env, env.Ally.Entity, persistent.StatusID2, owner);
            Add(env, owner, marked.StatusID2, owner);
            Add(env, env.Ally.Entity, marked.StatusID2, owner);
            var field = env.Ally.Entity.Position;
            var team = env.Manager.AllyTeam;
            var fieldStatus = field.AddStatus(marked.StatusID2, owner, 0, 3, 0, marked);
            var teamStatus = team.AddStatus(marked.StatusID2, owner, 0, 3, 0, marked);
            Assert.That(fieldStatus, Is.Not.Null);
            Assert.That(teamStatus, Is.Not.Null);
            env.DataManager.StatusMainDict[Enemy2071To2073Hooks.Hydro].ClearOnPhaseExit = true;
            env.DataManager.StatusMainDict[Enemy2071To2073Hooks.Electro].ClearOnPhaseExit = true;
            Add(env, env.Ally.Entity, Enemy2071To2073Hooks.Hydro, owner);
            Add(env, env.Ally.Entity, Enemy2071To2073Hooks.Electro, owner);
            owner.ElementalAuras.Add(new ElementalAura { Element = "Hydro", AuraAmount = 1f });
            using var death = BattleEventHub.Subscribe<DeathEventData>(BattleEventKind.Death, (_, __) => deaths++, 0);
            await BattleExecution.Run(async () => {
                HitResolution Hit()
                {
                    using var batch = new HitBatch();
                    return batch.Resolve(new HitRequest { Target = owner, Element = "True", DamageBeforeShield = 100000,
                        Source = DamageSourceInfo.Create(env.Ally.Entity, ReactionSourceKind.CharacterSkill, "SK_PhaseTest", "SE_PhaseTest") });
                }
                using (BattleResolutionContext.EnterAction(new BattleResolutionContext.CounterScope()))
                {
                    Assert.That(Hit().DamageEvent.CausedDeath, Is.False);
                    Assert.That(owner.CurrentHP, Is.Zero);
                    Assert.That(owner.IsDead, Is.False);
                    Assert.That(owner.Position.Occupant, Is.SameAs(owner));
                    Assert.That(Hit(), Is.Null);
                    owner.Heal(1000, env.Ally.Entity);
                    Assert.That(owner.CurrentHP, Is.Zero);
                    await BattleExecution.Flush();
                    Assert.That(env.Enemy.HealthPhases.Current, Is.EqualTo(1));
                    env.Manager.CheckBattleEnd();
                    Assert.That(env.Manager.IsBattleOver, Is.False);
                }
                await BattleExecution.Flush();
                Assert.That(env.Enemy.HealthPhases.Current, Is.EqualTo(2)); Assert.That(owner.CurrentHP, Is.EqualTo(2000));
                Assert.That(env.Enemy.CurrentPatternID, Is.EqualTo(12));
                Assert.That(owner.GetFinalATK(null, null, -1, null), Is.EqualTo(200)); Assert.That(owner.ElectroRes, Is.EqualTo(.5f));
                Assert.That(env.Manager.IsBattleOver, Is.False); Assert.That(deaths, Is.Zero);
                Assert.That(owner.HasStatus(phaseInit.StatusID2), Is.True);
                Assert.That(owner.HasStatus(persistent.StatusID2), Is.True);
                Assert.That(owner.ElementalAuras.Single().AuraAmount, Is.EqualTo(1f));
                Assert.That(env.Ally.Entity.HasStatus(persistent.StatusID2), Is.True);
                Assert.That(owner.HasStatus(marked.StatusID2), Is.False);
                Assert.That(env.Ally.Entity.HasStatus(marked.StatusID2), Is.False);
                Assert.That(StatusHostAccess.Contains(field, fieldStatus), Is.False);
                Assert.That(StatusHostAccess.Contains(team, teamStatus), Is.False);
                Assert.That(env.Ally.Entity.HasStatus(Enemy2071To2073Hooks.Hydro), Is.False);
                Assert.That(env.Ally.Entity.HasStatus(Enemy2071To2073Hooks.Electro), Is.False);

                var status = owner.GetStatus(persistent.StatusID2);
                using (var call = BattleResolutionContext.BeginStatusExecution(status))
                {
                    Assert.That(Hit().DamageEvent.CausedDeath, Is.False);
                    Assert.That(Hit(), Is.Null);
                    using (var nested = BattleResolutionContext.BeginStatusExecution(owner.GetStatus(phaseInit.StatusID2)))
                        nested.Complete();
                    BattleResolutionContext.ContinueStatusExecution(status);
                    Assert.That(env.Enemy.HealthPhases.Current, Is.EqualTo(3));
                    call.Complete();
                }
                Assert.That(owner.HasStatus(phaseInit.StatusID2), Is.False);
                Assert.That(env.Enemy.ActionContribution, Is.EqualTo(4));
                Assert.That(env.Enemy.CurrentPatternID, Is.EqualTo(13));
                Assert.That(Hit().DamageEvent.CausedDeath, Is.True);
                await BattleExecution.Flush();
                Assert.That(deaths, Is.EqualTo(1));
                Assert.That(env.Manager.Victory, Is.True);
            });
        }

        private static StatusMainData Definition(DataManager data, int id, string key, string type = null)
        {
            var main = new StatusMainData { StatusID = id, StatusID2 = key, StatusName = key,
                StatusType = type, MaxStack = 1, WhenMax = "0" }; data.StatusMainDict[key] = main; return main;
        }
        private static void Add(BattleFlowTestEnv env, BattleEntity host, string id, BattleEntity caster, int duration = 0)
            => host.AddStatus(id, caster, duration, 3, 0, env.DataManager.StatusMainDict[id]);
        private static BattleFlowTestEnv Childe()
        {
            var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            var phases = new Dictionary<int, EnemyMainData>();
            for (int phase = 1; phase <= 3; phase++) phases[phase] = new EnemyMainData {
                EnemyID = 2073, Phase = phase, EnemyNameID = "Childe", EnemyName = "公子", ActsGiven = phase == 3 ? 4 : 3,
                Threat = 6, HP_coeff = phase, ATK_coeff = phase, ATK_Curve = 1,
                HydroRes = phase == 1 ? .5f : phase == 3 ? .7f : 0, ElectroRes = phase == 2 ? .5f : phase == 3 ? .7f : 0 };
            env.DataManager.EnemyPhaseDict[2073] = phases; env.DataManager.EnemyMainDict[2073] = phases[1];
            env.DataManager.EnemyAIDict[2073] = new List<EnemyAIData> {
                new EnemyAIData { EnemyID = 2073, Phase = 1, PatternID = 11 },
                new EnemyAIData { EnemyID = 2073, Phase = 2, PatternID = 12 },
                new EnemyAIData { EnemyID = 2073, Phase = 3, PatternID = 13 } };
            env.DataManager.EnemyAIRuleDict[2073] = new List<EnemyAIRuleData>();
            env.Enemy.InitEnemy(2073, 1); int id = 60010;
            foreach (string key in new[] { Enemy2071To2073Hooks.CEntry, Enemy2071To2073Hooks.Hydro,
                Enemy2071To2073Hooks.Electro, Enemy2071To2073Hooks.Leap, Enemy2071To2073Hooks.CCharge }) Definition(env.DataManager, id++, key);
            Add(env, env.Enemy.Entity, Enemy2071To2073Hooks.CEntry, env.Enemy.Entity);
            return env;
        }
    }
}
