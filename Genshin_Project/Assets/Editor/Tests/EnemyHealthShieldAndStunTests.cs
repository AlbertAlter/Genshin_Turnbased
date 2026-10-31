using System;
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class EnemyHealthShieldAndStunTests
    {
        private const string ShieldID = "ST_TestEnemyHealthShield";
        private const string EffectID = "STE_TestEnemyHealthShield";

        private static StatusMainData ShieldDefinition(BattleFlowTestEnv env, string element = "None", string formula = "0.2*TotalHP")
        {
            env.ConfigureStatus(ShieldID);
            var main = env.DataManager.StatusMainDict[ShieldID];
            main.StatusID = 49991; main.StatusType = "Shield";
            env.DataManager.StatusEffectDict[EffectID] = new StatusEffectData
            { StatusEffectID = 4999101, StatusEffectID2 = EffectID, EffectType = "Shield", Element = element, Param1 = formula, TargetType = "Self" };
            return main;
        }

        private static StatusInstance ApplyShield(BattleFlowTestEnv env, StatusMainData main)
            => env.Enemy.Entity.AddStatus(ShieldID, env.Enemy.Entity, 1, 6, 0, main);

        [Test]
        public void HealthShield_UsesMaxHPOnce_AbsorbsOneToOneAndAllowsOverflow()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            var main = ShieldDefinition(env);
            float expected = env.Enemy.Entity.GetFinalHP() * 0.2f;
            var status = ApplyShield(env, main);
            Assert.That(env.Enemy.Entity.GetEnemyElementalShield(), Is.Null);
            Assert.That(env.Enemy.Entity.GetTotalShieldHP(), Is.EqualTo(expected).Within(0.001f));
            Assert.That(env.Enemy.Entity.AbsorbDamageWithShield(expected / 2, "Pyro"), Is.Zero);
            EnemyShieldEffectSystem.Apply(env.Enemy.Entity, env.DataManager.StatusEffectDict[EffectID], status);
            Assert.That(env.Enemy.Entity.GetTotalShieldHP(), Is.EqualTo(expected / 2).Within(0.001f), "重复调用不能补满盾");
            Assert.That(env.Enemy.Entity.AbsorbDamageWithShield(expected / 2 + 7, "Cryo"), Is.EqualTo(7).Within(0.001f));
            Assert.That(env.Enemy.Entity.HasStatus(ShieldID), Is.False);
        }

        [Test]
        public void BrokenShield_RemovesBoundTauntAndReportsShieldBroken()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            var main = ShieldDefinition(env, formula: "100");
            env.ConfigureStatus("Taunt");
            env.DataManager.StatusEffectDict["STE_TestEnemyBindTaunt"] = new StatusEffectData
            { StatusEffectID = 4999102, StatusEffectID2 = "STE_TestEnemyBindTaunt", EffectType = "BindStatus", Param1 = "Taunt", TargetType = "Self" };
            ApplyShield(env, main);
            Assert.That(env.Enemy.Entity.HasStatus("Taunt"), Is.True);
            string reason = null;
            using var listener = BattleEventHub.Subscribe<StatusEventData>(BattleEventKind.StatusRemoved,
                (_, fact) => { if (fact.StatusId == ShieldID) reason = fact.Reason; }, 0);
            env.Enemy.Entity.AbsorbDamageWithShield(101, "Physical");
            Assert.That(env.Enemy.Entity.HasStatus("Taunt"), Is.False);
            Assert.That(reason, Is.EqualTo("ShieldBroken"));
        }

        [TestCase("Pyro", "0.2*TotalHP")]
        [TestCase("", "100")]
        [TestCase("None", "12U")]
        public void InvalidShieldDefinition_DoesNotRemoveExistingShield(string element, string formula)
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            var main = ShieldDefinition(env, formula: "100");
            var old = ApplyShield(env, main);
            env.DataManager.StatusEffectDict[EffectID].Element = element;
            env.DataManager.StatusEffectDict[EffectID].Param1 = formula;
            Assert.Throws<FormatException>(() => ApplyShield(env, main));
            Assert.That(old.IsActive, Is.True);
            Assert.That(env.Enemy.Entity.GetTotalShieldHP(), Is.EqualTo(100));
        }

        [Test]
        public void ElementalGaugeShield_StillUsesGaugeAndDoesNotAbsorbHPDamage()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            ApplyShield(env, ShieldDefinition(env, "Hydro", "12U"));
            Assert.That(env.Enemy.Entity.GetEnemyElementalShieldHP(), Is.EqualTo(12));
            Assert.That(env.Enemy.Entity.AbsorbDamageWithShield(50, "Physical"), Is.EqualTo(50));
        }

        [Test]
        public void BreakingHit_KeepsItsReduction_ThenLaterHitLosesIt()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            var main = ShieldDefinition(env, formula: "100");
            env.ConfigureStatus("ST_TestEnemyShieldReduction");
            var reduction = env.DataManager.StatusMainDict["ST_TestEnemyShieldReduction"];
            reduction.StatusID = 49992; reduction.StatusType = "Buff"; reduction.MultiplierPart = "DMGTaken";
            env.DataManager.StatusEffectDict["STE_TestEnemyReduction"] = new StatusEffectData
            { StatusEffectID = 4999201, StatusEffectID2 = "STE_TestEnemyReduction", EffectType = "Buff", Param1 = "-0.1", TargetType = "Self" };
            env.DataManager.StatusEffectDict["STE_TestBindReduction"] = new StatusEffectData
            { StatusEffectID = 4999102, StatusEffectID2 = "STE_TestBindReduction", EffectType = "BindStatus", Param1 = reduction.StatusID2, TargetType = "Self" };
            ApplyShield(env, main);
            var source = DamageSourceInfo.Create(env.Ally.Entity, ReactionSourceKind.CharacterSkill, "test", "test");
            using (var batch = new HitBatch())
            {
                var result = batch.Resolve(new HitRequest { Target = env.Enemy.Entity, DamageBeforeShield = 200,
                    Element = "Pyro", DamageType = "Normal", Source = source });
                Assert.That(result.DMGTakenMultiplier, Is.EqualTo(0.9f).Within(0.0001f));
                Assert.That(result.DamageAfterShield, Is.EqualTo(80).Within(0.001f));
            }
            Assert.That(env.Enemy.Entity.HasStatus(reduction.StatusID2), Is.False);
            using (var batch = new HitBatch())
            {
                var result = batch.Resolve(new HitRequest { Target = env.Enemy.Entity, DamageBeforeShield = 100,
                    Element = "Pyro", DamageType = "Normal", Source = source });
                Assert.That(result.DamageAfterShield, Is.EqualTo(100).Within(0.001f));
            }
        }

        [Test]
        public void SourceStatusRemoval_CleansHealthShield()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            var status = ApplyShield(env, ShieldDefinition(env));
            env.Enemy.Entity.RemoveStatusInstance(status, "Expired");
            Assert.That(env.Enemy.Entity.GetTotalShieldHP(), Is.Zero);
        }

        [Test]
        public void EnemyStun_BlocksActionsAndContribution_UntilRemoved()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            env.ConfigureStatus(PublicControlStates.EnemyStun);
            var main = env.DataManager.StatusMainDict[PublicControlStates.EnemyStun];
            var status = env.Enemy.Entity.AddStatus(main.StatusID2, env.Enemy.Entity, 1, 6, 0, main);
            Assert.That(status, Is.Not.Null);
            Assert.That(env.Enemy.CanAct, Is.False);
            Assert.That(env.Enemy.CanContributeActions, Is.False);
            var pool = new EnemyTurnActionPool(new[] { env.Enemy });
            Assert.That(pool.RemainingActions, Is.Zero);
            env.Enemy.Entity.RemoveStatusInstance(status);
            Assert.That(env.Enemy.CanAct, Is.True);
            Assert.That(env.Enemy.CanContributeActions, Is.True);
            Assert.That(pool.TryTakeNext(out _), Is.False, "解除眩晕不返还已扣行动贡献");
        }

        [Test]
        public void EnemyStun_RejectsAllySource_CharacterTarget_AndNonUnitHosts()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            env.ConfigureStatus(PublicControlStates.EnemyStun);
            var main = env.DataManager.StatusMainDict[PublicControlStates.EnemyStun];
            Assert.That(env.Enemy.Entity.AddStatus(main.StatusID2, env.Ally.Entity, 1, 6, 0, main), Is.Null);
            Assert.That(env.Ally.Entity.AddStatus(main.StatusID2, env.Enemy.Entity, 1, 6, 0, main), Is.Null);
            Assert.That(env.Enemy.Entity.AddStatus(main.StatusID2, null, 1, 6, 0, main), Is.Null);
            Assert.Throws<InvalidOperationException>(() => env.Manager.EnemyTeam.AddStatus(main.StatusID2, env.Enemy.Entity, 1, 6, 0, main));
            var field = new FieldPosition(env.Manager.Field, BattleSide.Enemy, 1);
            Assert.Throws<InvalidOperationException>(() => field.AddStatus(main.StatusID2, env.Enemy.Entity, 1, 6, 0, main));
        }

        [TestCase("0.2*TotalHP+10", 210)]
        [TestCase("100", 100)]
        public void HealthFormula_AcceptsExpectedValues(string formula, float expected)
            => Assert.That(EnemyHealthShieldFormula.Evaluate(formula, _ => 1000), Is.EqualTo(expected).Within(0.001f));

        [TestCase("12U")][TestCase("NaN")][TestCase("-1")][TestCase("0")][TestCase("1+")][TestCase("TotalHP/5")]
        public void HealthFormula_RejectsInvalidDefinitions(string formula)
            => Assert.Throws<FormatException>(() => EnemyHealthShieldFormula.Evaluate(formula, _ => 1000));
    }
}
