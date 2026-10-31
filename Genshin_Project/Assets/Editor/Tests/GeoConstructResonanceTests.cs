using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class GeoConstructResonanceTests
    {
        private static StatusInstance Add(BattleFlowTestEnv env, int slot, string id, bool moon = false)
        {
            var position = env.Manager.Field.GetSlot(BattleSide.Enemy, slot);
            var main = new StatusMainData { StatusID2 = id, StatusType = moon ? "" : "GeoConstruct", MaxStack = 1 };
            // 月笼由专用反应入口创建；这里仅构造其已提交状态，不调用被禁止的直接ApplyStatus。
            if (moon)
            {
                var status = new StatusInstance { StatusID2 = TeamPublicStatuses.MoonCage, MainData = main,
                    Caster = env.Ally.Entity, Host = position, IsActive = true, ApplyOrder = 100 + slot };
                position.StatusList.Add(status);
                return status;
            }
            env.DataManager.StatusMainDict[id] = main;
            return position.AddStatus(id, env.Ally.Entity, 5, 3, 0, main);
        }

        [Test] public void TagAndNodeValidation_DoNotRequireZhongliTablesOrRoster()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            var node = Add(env, 1, "ST_TestConstruct");
            var tag = DamageProperties.Parse("Geo", "共鸣;NightSoul");
            Assert.That(tag.IsResonance && tag.IsNightSoul, Is.True);
            Assert.That(tag.UsesSpecialFormula, Is.False);
            Assert.Throws<FormatException>(() => DamageProperties.Parse("Physical", "Resonance"));
            bool executed = false;
            bool handled = GeoConstructStatusSystem.TryExecuteAsync(node, env.Ally.Entity,
                targets => { executed = true; return Task.CompletedTask; }).GetAwaiter().GetResult();
            Assert.That(handled, Is.False); Assert.That(executed, Is.False);
            Assert.That(GeoConstructStatusSystem.IsConstruct(node), Is.True);
        }

        [Test] public void EmptyPositionsAndMoonCage_ConductWithoutFabricatingHits_OncePerInstance()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            env.Ally.Entity.EntityID = 1032;
            var first = env.Manager.Field.GetSlot(BattleSide.Enemy, 1);
            first.Occupant = null;
            var last = env.Manager.Field.GetSlot(BattleSide.Enemy, 4);
            last.Occupant = env.Enemy.Entity;
            var a = Add(env, 1, "ST_TestPillar");
            var b = Add(env, 2, "ST_TestBridge");
            var c = Add(env, 3, TeamPublicStatuses.MoonCage, true);
            var emissions = new List<GeoResonanceEmission>();
            GeoConstructStatusSystem.TryExecuteAsync(a, env.Ally.Entity, targets =>
            {
                Assert.That(targets, Is.EqualTo(new[] { env.Enemy.Entity }));
                emissions.Add(GeoConstructStatusSystem.Current);
                return Task.CompletedTask;
            }).GetAwaiter().GetResult();
            Assert.That(emissions.Count, Is.EqualTo(1));
            Assert.That(emissions[0].Emitter, Is.SameAs(c));
            Assert.That(emissions[0].Origin, Is.SameAs(a));
            Assert.That(emissions[0].IsStoneStele, Is.False);
            Assert.That(GeoConstructStatusSystem.TryReceiveResonance(b, env.Manager.Field, env.Manager.TurnCount), Is.False);
            Assert.That(GeoConstructStatusSystem.TryReceiveResonance(c, env.Manager.Field, env.Manager.TurnCount), Is.False);
            Assert.That(GeoConstructStatusSystem.TryReceiveResonance(c, env.Manager.Field, env.Manager.TurnCount + 1), Is.True);
            Assert.That(GeoConstructStatusSystem.Current, Is.Null);
        }

        [Test] public void TwoPillars_HaveSeparatePeriodicAndReceivedAllowance_WithoutOnTriggerRows()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle(); env.Ally.Entity.EntityID = 1032;
            var a = Add(env, 1, "ST_TestPillar"); var b = Add(env, 2, "ST_TestPillar");
            var emissions = new List<GeoResonanceEmission>();
            Task Damage(List<BattleEntity> targets)
            { emissions.Add(GeoConstructStatusSystem.Current); return Task.CompletedTask; }
            GeoConstructStatusSystem.TryExecuteAsync(a, env.Ally.Entity, Damage).GetAwaiter().GetResult();
            GeoConstructStatusSystem.TryExecuteAsync(b, env.Ally.Entity, Damage).GetAwaiter().GetResult();
            foreach (var pillar in new[] { a, b })
            {
                Assert.That(emissions.Count(e => e.Emitter == pillar && !e.IsTriggered), Is.EqualTo(1));
                Assert.That(emissions.Count(e => e.Emitter == pillar && e.IsTriggered), Is.EqualTo(1));
            }
            Assert.That(emissions.All(e => e.IsStoneStele), Is.True);
            Assert.That(env.DataManager.StatusActionDict.ContainsKey("ST_TestPillar"), Is.False);
        }

        [Test] public void RemovingReceiverBeforeItsEmission_CancelsIt_AndFailureRestoresContext()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle(); env.Ally.Entity.EntityID = 1032;
            var a = Add(env, 1, "ST_TestPillar"); var b = Add(env, 2, "ST_TestBridge");
            int calls = 0;
            GeoConstructStatusSystem.TryExecuteAsync(a, env.Ally.Entity, targets =>
            {
                calls++;
                ((FieldPosition)b.Host).RemoveStatusInstance(b);
                return Task.CompletedTask;
            }).GetAwaiter().GetResult();
            Assert.That(calls, Is.EqualTo(1));
            Assert.Throws<InvalidOperationException>(() => GeoConstructStatusSystem.TryExecuteAsync(a,
                env.Ally.Entity, targets => throw new InvalidOperationException("test"))
                .GetAwaiter().GetResult());
            Assert.That(GeoConstructStatusSystem.Current, Is.Null);
        }

        [TestCase(false)] [TestCase(true)]
        public void RealDamageEntry_PreservesZhongliSourceSkillClassificationAndEmitter(bool skillEffect)
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            env.Ally.Entity.EntityID = 1032;
            env.Ally.Entity.TotalATK = 100; env.Ally.Entity.CritRate = 0;
            BattleRandom.SetSource(new SequenceBattleRandomSource());
            var pillar = Add(env, 1, "ST_TestPillar");
            var moon = Add(env, 2, TeamPublicStatuses.MoonCage, true);
            const string id = "SE_Test_Resonance";
            if (skillEffect) env.DataManager.SkillEffectDict[id] = new SkillEffectData
            { SkillEffectID = 480032001, SkillEffectID2 = id, EffectType = "Damage", Element = "Geo",
                DamageType = "Skill", Param1 = "0.1*TotalATK,0", Param3 = "Resonance", TargetType = "Enemy",
                TargetNumber = 1, TargetConsecutive = 1, ExecuteCount = 1 };
            else env.DataManager.StatusEffectDict[id] = new StatusEffectData
            { StatusEffectID = 480032001, StatusEffectID2 = id, EffectType = "Damage", Element = "Geo",
                DamageType = "Skill", Param1 = "0.1*TotalATK,0", Param3 = "Resonance", TargetType = "Enemy",
                TargetSelect = "1", ExecuteCount = 1 };
            var actions = new List<StatusActionData> { new StatusActionData
                { StatusID2 = pillar.StatusID2, ActionType = "OnItsTurn", Param1 = id, ExecuteCount = 1 } };
            var facts = new List<DamageEventData>();
            using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved,
                (_, fact) => { if (fact.Source.SourceEffectID == id) facts.Add(fact); }, 0);
            BattleExecution.Run(() => env.Ally.ExecuteStatusActionsTask(pillar, actions, pillar.Host, "OnItsTurn"))
                .GetAwaiter().GetResult();
            Assert.That(facts.Count, Is.EqualTo(3), "岩脊固定共鸣、月笼传导、岩脊被带动各一次");
            Assert.That(facts.All(f => f.IsResonanceDamage && f.DamageType == "Skill"
                && f.Source.SourceEntity == env.Ally.Entity && f.Source.SourceStatus == pillar), Is.True);
            Assert.That(facts.Count(f => f.Resonance.Emitter == moon), Is.EqualTo(1));
            Assert.That(facts.Count(f => f.Resonance.IsStoneStele), Is.EqualTo(2));
            Assert.That(env.Ally.Entity.CurrentEnergy, Is.Zero, "共鸣底层不自行捏造产能");
        }
    }
}
