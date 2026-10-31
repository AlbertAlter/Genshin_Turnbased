using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    public class StatusEffectDispatchTests
    {
        [Test]
        public void RuntimeElement_IsEmptyByDefaultAndIndependentPerInstance()
        {
            var first = new StatusInstance { StatusID2 = "ST_Element" };
            var second = new StatusInstance { StatusID2 = "ST_Element" };
            Assert.That(first.RuntimeElement, Is.Empty);
            first.RuntimeElement = "Pyro";
            first.BoundStatuses.Add(second);
            Assert.That(second.RuntimeElement, Is.Empty, "同名实例和绑定子状态不自动共享元素记录");
            second.RuntimeElement = "Physical";
            Assert.That(first.RuntimeElement, Is.EqualTo("Pyro"));
            Assert.That(second.RuntimeElement, Is.EqualTo("Physical"), "物理伤害类型不等于尚未记录");
        }

        [Test]
        public void RuntimeElement_RemainsIndependentAcrossReapplicationAndReplacement()
        {
            using var env = new BattleFlowTestEnv();
            const string id = "ST_RuntimeElement";
            env.ConfigureStatus(id, maxStack: 2, whenMax: "0");
            env.CreateMinimalBattle();
            var entity = env.Ally.Entity;
            var main = env.DataManager.StatusMainDict[id];
            var first = entity.AddStatus(id, entity, 2, 2, 0, main);
            first.RuntimeElement = "Hydro";
            var second = entity.AddStatus(id, entity, 3, 2, 0, main);
            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(second.RuntimeElement, Is.Empty);
            second.RuntimeElement = "Electro";
            Assert.That(first.RuntimeElement, Is.EqualTo("Hydro"));
            Assert.That(first.RemainingPhaseCount, Is.EqualTo(2));
            CollectionAssert.AreEqual(new[] { first, second }, entity.GetStatusInstances(id));
            var replacement = entity.AddStatus(id, entity, 2, 2, 0, main);
            Assert.That(replacement, Is.Not.SameAs(first));
            Assert.That(replacement.RuntimeElement, Is.Empty);
            Assert.That(first.IsActive, Is.False);
            CollectionAssert.AreEqual(new[] { second, replacement }, entity.GetStatusInstances(id));
            Assert.That(second.RuntimeElement, Is.EqualTo("Electro"));
            Assert.That(second.RemainingPhaseCount, Is.EqualTo(3));
            replacement.RuntimeElement = "Cryo";
            entity.RemoveStatus(id, -1);
            Assert.That(second.IsActive, Is.False);
            Assert.That(replacement.IsActive, Is.False);
            var reapplied = entity.AddStatus(id, entity, 2, 2, 0, main);
            Assert.That(reapplied.RuntimeElement, Is.Empty);
        }

        [TestCase("Chance(0)", 0f, false, 1)]
        [TestCase("Chance(1)", 0.999f, true, 1)]
        [TestCase("Chance(0.5)", 0.5f, false, 1)]
        [TestCase("Chance(0.5)", 0.49f, true, 1)]
        [TestCase("Chance(-0.1)", 0f, false, 0)]
        [TestCase("Chance(1.1)", 0f, false, 0)]
        [TestCase("Chance(NaN)", 0f, false, 0)]
        public void Chance_UsesOneRollForValidProbabilitiesOnly(string hook, float roll, bool result, int draws)
        {
            var random = new SequenceBattleRandomSource(new[] { roll });
            BattleRandom.SetSource(random);
            try
            {
                Assert.That(ScriptHookEvaluator.Evaluate(hook, (BattleEntity)null), Is.EqualTo(result));
                Assert.That(random.FloatCallCount, Is.EqualTo(draws));
            }
            finally { BattleRandom.ResetSource(); }
        }

        [TestCase("ST_Lisa_C2", "SE_Heavy_Lisa1", "Chance(0.5)", 0)]
        [TestCase("ST_Lisa_T2", "SE_BurstAttack_Lisa", "", 2)]
        public void LisaHitResponse_PreservesEachTargetAndRollsOncePerEffect(
            string listenerID, string damageID, string chance, int duration)
        {
            using (var env = new BattleFlowTestEnv())
            {
                const string appliedID = "ST_Lisa_Conductive";
                env.ConfigureStatus(appliedID, maxStack: 3);
                env.ConfigureStatus(listenerID, actionType: "OnTrigger", actionEffectID2: "STE_Lisa_Test");
                env.DataManager.StatusActionDict[listenerID][0].OutgoingHit = damageID;
                env.DataManager.StatusEffectDict["STE_Lisa_Test"] = new StatusEffectData
                {
                    StatusEffectID2 = "STE_Lisa_Test", EffectType = "ApplyStatus", Param1 = appliedID,
                    Duration = duration, TargetType = "Enemy", TargetOverride = listenerID + "()", ScriptHook = chance
                };
                env.CreateMinimalBattle();
                env.Ally.Entity.EntityID = 1011;
                var targets = new[] { env.Enemy.Entity, env.CreateEnemy(2).Entity, env.CreateEnemy(3).Entity };
                env.Ally.Entity.AddStatus(listenerID, env.Ally.Entity, 0, 2, 0,
                    env.DataManager.StatusMainDict[listenerID]);
                var events = new List<DamageResolvedEvent>();
                foreach (var target in targets)
                    events.Add(new DamageResolvedEvent
                    {
                        HitBatchID = 1, HitIndex = 1, Target = target, HitLanded = true,
                        // 护盾全吸收也属于命中，不要求 ActualHPDamage > 0。
                        Source = DamageSourceInfo.Create(env.Ally.Entity, ReactionSourceKind.CharacterSkill, "SK_Test", damageID)
                    });
                var random = new SequenceBattleRandomSource(new[] { 0.2f, 0.8f, 0.3f });
                BattleRandom.SetSource(random);
                BattleExecution.Run(() =>
                {
                    StatusOnHitHookSystem.NotifyDamageBatch(events);
                    // 队列必须使用已经捕获的事件，不能读取被后续事件覆盖的目标。
                    events[0].Target = targets[1];
                    return Task.CompletedTask;
                }).GetAwaiter().GetResult();
                Assert.That(targets[0].HasStatus(appliedID), Is.True);
                Assert.That(targets[1].HasStatus(appliedID), Is.EqualTo(string.IsNullOrEmpty(chance)));
                Assert.That(targets[2].HasStatus(appliedID), Is.True);
                Assert.That(targets[0].GetStatus(appliedID).IsPermanent, Is.EqualTo(duration == 0));
                Assert.That(targets[0].GetStatus(appliedID).RemainingPhaseCount, Is.EqualTo(duration));
                Assert.That(random.FloatCallCount, Is.EqualTo(string.IsNullOrEmpty(chance) ? 0 : 3));
                Assert.That(ScriptHookContext.Current, Is.Null);
            }
        }

        private static StatusInstance Source(BattleFlowTestEnv env, string id)
        {
            var status = new StatusInstance { StatusID2 = id, IsActive = true,
                MainData = env.DataManager.StatusMainDict[id] };
            status.RecordSource(env.Ally.Entity);
            return status;
        }

        private static void Execute(BattleFlowTestEnv env, StatusInstance status, object host)
        {
            BattleExecution.Run(() => env.Ally.ExecuteStatusActionsTask(status,
                env.DataManager.StatusActionDict[status.StatusID2], host, "OnTrigger"))
                .GetAwaiter().GetResult();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void QueuedExecuteEffect_PreservesCalledTargetAndRestoresCallerOnFailure(bool fail)
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.ConfigureAllyAction(2, "SK_Dispatch", "SE_Dispatch", "Physical", "1*TotalATK,0");
                if (fail) env.DataManager.SkillEffectDict["SE_Dispatch"].DamageHook = "Unknown()";
                env.ConfigureStatus("ST_Dispatch", actionType: "OnTrigger", actionEffectID2: "STE_Dispatch");
                env.DataManager.StatusEffectDict["STE_Dispatch"] = new StatusEffectData
                {
                    StatusEffectID2 = "STE_Dispatch", EffectType = "ExecuteEffect", Param1 = "SE_Dispatch",
                    TargetType = "Enemy", TargetSelect = "0,0"
                };
                env.CreateMinimalBattle();
                var other = env.CreateEnemy(2);
                env.Ally.ForcedTargetPositions.Add(2);
                var source = Source(env, "ST_Dispatch");
                if (fail)
                    Assert.Throws<NotSupportedException>(() => Execute(env, source, env.Enemy.Entity));
                else
                    Execute(env, source, env.Enemy.Entity);
                Assert.That(env.Enemy.Entity.CurrentHP, Is.EqualTo(fail ? 1000f : 950f).Within(0.01f));
                Assert.That(other.Entity.CurrentHP, Is.EqualTo(1000f));
                CollectionAssert.AreEqual(new[] { 2 }, env.Ally.ForcedTargetPositions);
            }
        }

        [Test]
        public void StatusEffectHook_IsEvaluatedOncePerExecutionIncludingRejectedExecution()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.ConfigureStatus("ST_Conditional", actionType: "OnTrigger", actionEffectID2: "STE_Conditional");
                env.DataManager.StatusEffectDict["STE_Conditional"] = new StatusEffectData
                {
                    StatusEffectID2 = "STE_Conditional", EffectType = "GainEnergy", Param1 = "1",
                    Param2 = "Flat", TargetType = "Self", ExecuteCount = 2, ScriptHook = "Hook(4300501)"
                };
                env.CreateMinimalBattle();
                env.Ally.Entity.CurrentEnergy = 0;
                var random = new SequenceBattleRandomSource(new[] { 0.2f, 0.8f });
                BattleRandom.SetSource(random);
                Execute(env, Source(env, "ST_Conditional"), env.Ally.Entity);
                Assert.That(env.Ally.Entity.CurrentEnergy, Is.EqualTo(1));
                Assert.That(random.FloatCallCount, Is.EqualTo(2));
            }
        }

        [TestCase(0.2f, true)]
        [TestCase(0.8f, false)]
        public void EnemyEffectHook_GatesExecutionWithOneRoll(float roll, bool expected)
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.ConfigureStatus("ST_EnemyConditional");
                env.CreateMinimalBattle();
                var random = new SequenceBattleRandomSource(new[] { roll });
                BattleRandom.SetSource(random);
                var effect = new EnemySkillEffectData
                {
                    EffectType = "ApplyStatus", Param1 = "ST_EnemyConditional", TargetType = "Self",
                    Duration = 1, ScriptHook = "Hook(4300501)"
                };
                var method = typeof(EnemyBattleController).GetMethod("ExecuteEnemyEffectAsync",
                    BindingFlags.NonPublic | BindingFlags.Instance, null,
                    new[] { typeof(EnemySkillEffectData), typeof(StatusInstance) }, null);
                Assert.That(method, Is.Not.Null);
                BattleExecution.Run(() => (Task)method.Invoke(env.Enemy, new object[] { effect, null }))
                    .GetAwaiter().GetResult();
                Assert.That(env.Enemy.Entity.HasStatus("ST_EnemyConditional"), Is.EqualTo(expected));
                Assert.That(random.FloatCallCount, Is.EqualTo(1));
            }
        }

        [TestCase(0.2f, true)]
        [TestCase(0.8f, false)]
        public void BindEffectHook_GatesChildCreationWithOneRoll(float roll, bool expected)
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.ConfigureStatus("ST_BindParent");
                env.ConfigureStatus("ST_BindChild");
                env.DataManager.StatusMainDict["ST_BindParent"].StatusID = 990010;
                env.DataManager.StatusEffectDict["STE_BindConditional"] = new StatusEffectData
                {
                    StatusEffectID = 99001001, StatusEffectID2 = "STE_BindConditional",
                    EffectType = "BindStatus", Param1 = "ST_BindChild", TargetType = "Self",
                    ScriptHook = "Hook(4300501)"
                };
                env.CreateMinimalBattle();
                var random = new SequenceBattleRandomSource(new[] { roll });
                BattleRandom.SetSource(random);
                var parent = Source(env, "ST_BindParent");
                StatusBindingSystem.ApplyBindings(parent, env.Ally.Entity);
                Assert.That(env.Ally.Entity.HasStatus("ST_BindChild"), Is.EqualTo(expected));
                Assert.That(parent.BoundStatuses.Count, Is.EqualTo(expected ? 1 : 0));
                Assert.That(random.FloatCallCount, Is.EqualTo(1));
            }
        }
    }
}
