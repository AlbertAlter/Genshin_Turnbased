using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    /// <summary>内存配表走真实战斗/伤害/切人入口；按项目规则只编写，未运行Unity。</summary>
    public sealed class WeaponCompletionRegressionTests
    {
        private const string Line = "ST_Weapon_4309402";
        private const string Forest = "ST_Weapon_4309502";

        private static StatusMainData Main(BattleFlowTestEnv env, int id, bool count = false, string part = null)
        {
            var data = new StatusMainData { StatusID = id, StatusID2 = DataManager.WeaponStatusKey(id),
                StatusName = "WeaponCompletionTest", StatusType = count ? "Count" : "Buff",
                MultiplierPart1 = part, MaxStack = 1, MaxCount = 1, WhenMax = "0" };
            env.DataManager.StatusMainDict[data.StatusID2] = data;
            return data;
        }
        private static void Effect(BattleFlowTestEnv env, int id, string type, string parameter,
            int duration = 0, string target = "Self", string splash = null)
        {
            env.DataManager.StatusEffectDict["STE_Weapon_" + id] = new StatusEffectData
            {
                StatusEffectID = id, StatusEffectID2 = "STE_Weapon_" + id, EffectType = type, Param1 = parameter,
                Duration = duration, AddInPhase = duration > 0 ? 2 : 0, TargetType = target, Element = type == "Damage" ? "Physical" : "None", Param2 = splash,
                TargetSelect = target == "Enemy" ? "1" : null, TargetConsecutive = target == "Enemy" ? 4 : 0
            };
        }
        private static WeaponRuntimeContext Context(BattleEntity owner, int weapon, params float[] parameters)
        {
            var result = new WeaponRuntimeContext { Equipper = owner, WeaponID = weapon, Refinement = 1,
                SourceInstanceID = "WeaponCompletion:" + weapon };
            for (int i = 0; i < parameters.Length; i++) result.ResolvedParams[i + 1] = parameters[i];
            return result;
        }
        private static void LineTables(BattleFlowTestEnv env)
        {
            Main(env, 4309401); Main(env, 4309402, true);
            Effect(env, 430940101, "ApplyStatus", Line, 4);
            Effect(env, 430940201, "Damage", "Index(1)*TotalATK,0", target: "Enemy", splash: "Splash(1;1,1)");
            env.DataManager.StatusEffectDict["STE_Weapon_430940201"].TargetOverride = "PostSelfDamage(4309402)";
            env.DataManager.StatusActionDict["ST_Weapon_4309401"] = new List<StatusActionData>
            { new StatusActionData { StatusID2 = "ST_Weapon_4309401", ActionType = "OnTrigger",
                OnAction = "Skill", Param1 = "STE_Weapon_430940101", Cooldown = 3 } };
            env.DataManager.StatusActionDict[Line] = new List<StatusActionData>
            { new StatusActionData { StatusID2 = Line, ActionType = "OnTrigger", OutgoingHit = "All",
                ScriptHook = "PostSelfDamage(4309402);Hook(4309402)", Param1 = "STE_Weapon_430940201", MaxTimePerTurn = 2 } };
        }
        private static void ForestTables(BattleFlowTestEnv env)
        {
            Main(env, 4309501); Main(env, 4309502, true, "EMBonus");
            Effect(env, 430950101, "ApplyStatus", Forest, 3);
            Effect(env, 430950201, "Buff", "Count(" + Forest + ")");
            Effect(env, 430950202, "Damage", "Index(2)*TotalATK,0", target: "Enemy");
            Effect(env, 430950203, "RemoveStatus", Forest);
            env.DataManager.StatusActionDict["ST_Weapon_4309501"] = new List<StatusActionData>
            { new StatusActionData { StatusID2 = "ST_Weapon_4309501", ActionType = "OnTrigger", OnAction = "Skill;Burst",
                Param1 = "STE_Weapon_430950101", Cooldown = 5 } };
            env.DataManager.StatusActionDict[Forest] = new List<StatusActionData>
            {
                new StatusActionData { StatusID2 = Forest, ActionType = "OnEnd", Param1 = "STE_Weapon_430950202" },
                new StatusActionData { StatusID2 = Forest, ActionType = "OnTrigger", ScriptHook = "SwitchOut(Equipper)",
                    Param1 = "STE_Weapon_430950203" }
            };
        }
        private static void Start(BattleFlowTestEnv env)
        {
            env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
        }

        [Test]
        [Timeout(10000)]
        public void EndOfLine_FirstSkillCanProc_WholeSplashCostsOnce_AndNewInstanceKeepsTurnQuota()
        {
            using (var env = new BattleFlowTestEnv())
            {
                LineTables(env);
                env.ConfigureAllyAction(2, "SK_Line", "SE_Line", "Physical", "1*TotalATK,0", 10);
                env.DataManager.SkillLevelDict[$"{BattleFlowTestEnv.AllyID}_2"][1].Hits2 = "1*TotalATK,0";
                env.CreateMinimalBattle(); env.CreateEnemy(2); env.CreateEnemy(3);
                Start(env);
                var owner = env.Ally.Entity; var weapon = Context(owner, 3094, .8f);
                owner.AddStatus("ST_Weapon_4309401", owner, 0, 2, 0,
                    env.DataManager.StatusMainDict["ST_Weapon_4309401"], weapon);
                env.Ally.ForcedTargetPositions.AddRange(new[] { 1, 2 });
                var random = new SequenceBattleRandomSource(intValues: new[] { 1 });
                BattleRandom.SetSource(random);
                var hits = new List<DamageEventData>(); var countsDuringHit = new List<float>();
                using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, damage) =>
                {
                    if (damage.Source.SourceEffectID != "STE_Weapon_430940201") return;
                    hits.Add(damage); countsDuringHit.Add(owner.GetStatus(Line)?.RuntimeValue ?? -1f);
                }, 0);
                Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
                var count = owner.GetStatus(Line);
                Assert.That(count.RuntimeValue, Is.EqualTo(2f));
                Assert.That(hits.Select(h => h.Subject.SlotPosition), Is.EquivalentTo(new[] { 1, 2, 3 }));
                Assert.That(countsDuringHit, Is.All.EqualTo(3f), "最后一个范围目标结清之前不能扣次数或移除");
                Assert.That(random.IntCallCount, Is.EqualTo(1), "只从本次命中池抽一次中心");
                Assert.That(random.FloatCallCount, Is.EqualTo(7), "4个源目标hit及3个追加范围目标分别判暴击");
                var ledger = count.ActionRuntime.Values.Single();
                var reapplied = owner.AddStatus(Line, owner, 4, 2, 0, env.DataManager.StatusMainDict[Line], weapon);
                Assert.That(reapplied, Is.SameAs(count)); Assert.That(count.RuntimeValue, Is.EqualTo(3f));
                Assert.That(count.ActionRuntime.Values.Single(), Is.SameAs(ledger));
                count.RuntimeValue = 1f;
                Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
                Assert.That(owner.HasStatus(Line), Is.False); Assert.That(hits, Has.Count.EqualTo(5));
                Assert.That(countsDuringHit.Skip(3), Is.All.EqualTo(1f));
                var latest = owner.AddStatus(Line, owner, 4, 2, 0, env.DataManager.StatusMainDict[Line], weapon);
                Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
                Assert.That(latest.RuntimeValue, Is.EqualTo(3f)); Assert.That(hits, Has.Count.EqualTo(5));
                env.Manager.EndAllyTurn(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
                Assert.That(latest.RuntimeValue, Is.EqualTo(2f)); Assert.That(hits, Has.Count.EqualTo(7));
            }
        }

        [Test]
        [Timeout(6000)]
        public void EndOfLine_ShieldHitsConsumeAndOwnBonusCannotRecursivelyTrigger()
        {
            using (var env = new BattleFlowTestEnv())
            {
                LineTables(env);
                env.ConfigureAllyAction(2, "SK_LineShield", "SE_LineShield", "Physical", "1*TotalATK,0", 10);
                env.CreateMinimalBattle(); Start(env);
                var owner = env.Ally.Entity; var weapon = Context(owner, 3094, .8f);
                var count = owner.AddStatus(Line, owner, 4, 2, 0, env.DataManager.StatusMainDict[Line], weapon);
                env.Enemy.Entity.AddShield(10000f, "", 4f);
                var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
                Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
                Assert.That(count.RuntimeValue, Is.EqualTo(2f)); Assert.That(env.Enemy.Entity.CurrentHP, Is.EqualTo(1000f));
                var action = env.DataManager.StatusActionDict[Line][0];
                var hook = new ScriptHookContext { Status = count, Caster = owner, EventHookName = "PostSelfDamage",
                    ActionTargetPositions = new List<int> { 1 },
                    HitEffectID = "STE_Weapon_430940201", DamageEvent = new DamageResolvedEvent { HitLanded = true,
                        Source = DamageSourceInfo.Create(owner, ReactionSourceKind.StatusEffect, "", "STE_Weapon_430940201") } };
                Assert.That(ScriptHookEvaluator.Evaluate(action.ScriptHook, hook), Is.False, "自身追加不能回触发");
                Assert.That(random.FloatCallCount, Is.EqualTo(2));
            }
        }

        [Test]
        [Timeout(5000)]
        public void EndOfLine_EmptyLivePoolAfterKillingSourceTargetDoesNotSpendAUse()
        {
            using (var env = new BattleFlowTestEnv())
            {
                LineTables(env);
                env.ConfigureAllyAction(2, "SK_LineEmpty", "SE_LineEmpty", "Physical", "100*TotalATK,0", 10);
                env.CreateMinimalBattle(); env.CreateEnemy(2); Start(env);
                var owner = env.Ally.Entity; var weapon = Context(owner, 3094, .8f);
                var count = owner.AddStatus(Line, owner, 4, 2, 0, env.DataManager.StatusMainDict[Line], weapon);
                env.Ally.ForcedTargetPositions.Add(1);
                var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
                int extraHits = 0;
                using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, hit) =>
                { if (hit.Source.SourceEffectID == "STE_Weapon_430940201") extraHits++; }, 0);
                Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
                Assert.That(env.Enemy.Entity.IsAlive, Is.False); Assert.That(extraHits, Is.Zero);
                Assert.That(count.RuntimeValue, Is.EqualTo(3f));
                Assert.That(count.ActionRuntime.Values.All(s => s.TurnTriggerCount == 0 && s.PendingTriggerCount == 0), Is.True);
                Assert.That(random.FloatCallCount, Is.EqualTo(1)); Assert.That(random.IntCallCount, Is.Zero);
            }
        }

        [Test]
        [Timeout(7000)]
        public void KingsSquire_SwitchOutRemovesEMAndDealsExactlyOneRandomEnemyHitWithRemovalCause()
        {
            using (var env = new BattleFlowTestEnv())
            {
                ForestTables(env);
                env.ConfigureAllyAction(2, "SK_Forest", "SE_Forest", "Physical", "1*TotalATK,0", 10);
                env.CreateMinimalBattle(); var enemy2 = env.CreateEnemy(2); env.CreateAlly(2);
                Start(env);
                var owner = env.Ally.Entity; float em = owner.GetFinalEM();
                owner.AddStatus("ST_Weapon_4309501", owner, 0, 2, 0,
                    env.DataManager.StatusMainDict["ST_Weapon_4309501"], Context(owner, 3095, 60f, 1f));
                Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
                var forest = owner.GetStatus(Forest);
                Assert.That(owner.GetFinalEM(), Is.EqualTo(em + 60f));
                float firstHP = env.Enemy.Entity.CurrentHP, secondHP = enemy2.Entity.CurrentHP, ownerHP = owner.CurrentHP;
                var random = new SequenceBattleRandomSource(intValues: new[] { 1 }); BattleRandom.SetSource(random);
                var damage = new List<DamageEventData>(); BattleDeclaration removed = null; BattleDeclaration terminal = null;
                using var a = BattleEventHub.Subscribe<StatusEventData>(BattleEventKind.StatusRemoved, (_, fact) =>
                { if (fact.InstanceId == forest.ApplyOrder) removed = BattleEventHub.Current; }, 0);
                using var b = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, fact) =>
                {
                    if (fact.Source.SourceEffectID != "STE_Weapon_430950202") return;
                    damage.Add(fact); terminal = BattleEventHub.Current;
                    Assert.That(forest.IsActive, Is.False, "正常移除的终结不用重新激活状态");
                }, 0);
                Assert.That(env.Manager.TrySwitchActiveAlly(1), Is.True);
                Assert.That(owner.GetFinalEM(), Is.EqualTo(em)); Assert.That(owner.HasStatus(Forest), Is.False);
                Assert.That(damage, Has.Count.EqualTo(1)); Assert.That(damage[0].Subject, Is.SameAs(enemy2.Entity));
                Assert.That(damage[0].Source.SourceEntity, Is.SameAs(owner));
                Assert.That(env.Enemy.Entity.CurrentHP, Is.EqualTo(firstHP)); Assert.That(enemy2.Entity.CurrentHP, Is.LessThan(secondHP));
                Assert.That(owner.CurrentHP, Is.EqualTo(ownerHP));
                Assert.That(terminal.Header.CauseEventIds, Does.Contain(removed.Header.EventId));
                Assert.That(random.IntCallCount, Is.EqualTo(1)); Assert.That(random.FloatCallCount, Is.EqualTo(1));
                Assert.That(owner.RemoveStatusInstance(forest), Is.False); Assert.That(damage, Has.Count.EqualTo(1));
            }
        }

        [Test]
        [Timeout(9000)]
        public void KingsSquire_NaturalExpiryAndSubsequentRemovalDoNotDoubleFire()
        {
            using (var env = new BattleFlowTestEnv())
            {
                ForestTables(env); env.CreateMinimalBattle(); Start(env);
                var owner = env.Ally.Entity;
                var forest = owner.AddStatus(Forest, owner, 3, 2, 0, env.DataManager.StatusMainDict[Forest], Context(owner, 3095, 60f, 1f));
                var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
                int terminalHits = 0;
                using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, hit) =>
                { if (hit.Source.SourceEffectID == "STE_Weapon_430950202") terminalHits++; }, 0);
                forest.RemainingPhaseCount = 1;
                env.Manager.EndAllyTurn(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                Assert.That(terminalHits, Is.EqualTo(1)); Assert.That(owner.HasStatus(Forest), Is.False);
                Assert.That(forest.CountRuntimeState, Is.Null);
                Assert.That(owner.RemoveStatusInstance(forest, "Expired"), Is.False); Assert.That(terminalHits, Is.EqualTo(1));
            }
        }

        [TestCase("EquipmentOwnerDied")]
        [TestCase("BoundRemoved")]
        [TestCase("BattleCleanup")]
        [Timeout(5000)]
        public void KingsSquire_NonGameplayCleanupNeverCreatesTerminalDamage(string reason)
        {
            using (var env = new BattleFlowTestEnv())
            {
                ForestTables(env); env.CreateMinimalBattle(); Start(env);
                var owner = env.Ally.Entity;
                var forest = owner.AddStatus(Forest, owner, 3, 2, 0, env.DataManager.StatusMainDict[Forest], Context(owner, 3095, 60f, 1f));
                var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
                float hp = env.Enemy.Entity.CurrentHP;
                owner.RemoveStatusInstance(forest, reason);
                Assert.That(env.Enemy.Entity.CurrentHP, Is.EqualTo(hp));
                Assert.That(random.IntCallCount, Is.Zero); Assert.That(random.FloatCallCount, Is.Zero);
            }
        }

        [Test]
        [Timeout(5000)]
        public void KingsSquire_QueuedRemovalDamageIsCancelledWhenWearerDiesBeforeResponses()
        {
            using (var env = new BattleFlowTestEnv())
            {
                ForestTables(env); env.CreateMinimalBattle(); env.CreateAlly(2); Start(env);
                var owner = env.Ally.Entity;
                var forest = owner.AddStatus(Forest, owner, 3, 2, 0, env.DataManager.StatusMainDict[Forest], Context(owner, 3095, 60f, 1f));
                var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
                float enemyHP = env.Enemy.Entity.CurrentHP;
                using (BattleResolutionContext.BeginCollection())
                {
                    owner.RemoveStatusInstance(forest);
                    using var hit = new HitBatch();
                    hit.Resolve(new HitRequest { Target = owner, DamageBeforeShield = 10000f, Element = "Physical",
                        Source = DamageSourceInfo.Create(env.Enemy.Entity, ReactionSourceKind.EnemySkill, "SK_Fatal", "SE_Fatal") });
                    hit.Flush();
                }
                Assert.That(owner.IsAlive, Is.False);
                Assert.That(env.Enemy.Entity.CurrentHP, Is.EqualTo(enemyHP));
                Assert.That(random.IntCallCount, Is.Zero); Assert.That(random.FloatCallCount, Is.Zero);
            }
        }

        [Test]
        [Timeout(5000)]
        public void KingsSquire_StoppedBattleDoesNotTurnCleanupIntoGameplayDamage()
        {
            using (var env = new BattleFlowTestEnv())
            {
                ForestTables(env); env.CreateMinimalBattle(); Start(env);
                var owner = env.Ally.Entity;
                var forest = owner.AddStatus(Forest, owner, 3, 2, 0, env.DataManager.StatusMainDict[Forest], Context(owner, 3095, 60f, 1f));
                env.Manager.StopBattle();
                var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
                float hp = env.Enemy.Entity.CurrentHP;
                owner.RemoveStatusInstance(forest);
                Assert.That(env.Enemy.Entity.CurrentHP, Is.EqualTo(hp));
                Assert.That(random.IntCallCount, Is.Zero); Assert.That(random.FloatCallCount, Is.Zero);
            }
        }
    }
}
