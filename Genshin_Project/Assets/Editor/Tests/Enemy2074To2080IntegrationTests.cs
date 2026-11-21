using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GenshinTurnBased.Tests.EditMode
{
    public class Enemy2074To2080IntegrationTests
    {
        private static StatusMainData Main(BattleFlowTestEnv e, string id, int number)
        {
            var m = new StatusMainData { StatusID = number, StatusID2 = id, StatusName = "冻结地面", MaxStack = 1, WhenMax = "0" };
            e.DataManager.StatusMainDict[id] = m; return m;
        }
        private static void Enemy(BattleFlowTestEnv e, int id, string type = "Minion", string family = "丘丘部族")
        {
            e.DataManager.EnemyMainDict[id] = new EnemyMainData { EnemyID = id, EnemyType = type, EnemyFamily = family,
                EnemyName = id.ToString(), HP_coeff = 1, ATK_coeff = 1, ATK_Curve = 1, ActsGiven = 1, Threat = 1 };
            e.DataManager.EnemyAIDict[id] = new List<EnemyAIData>();
        }

        [UnityTest] public IEnumerator IceGrounds_SameNameDifferentIDsCoexist_AndEachDamagesOccupant()
            => HookTestEnv.AwaitTask(GroundsAsync);
        private async Task GroundsAsync()
        {
            using var e = new BattleFlowTestEnv(); e.CreateMinimalBattle(); var field = e.Ally.Entity.Position;
            var definitions = new[] { Main(e, "ST_CryoRegisvine_Ground", 4206706), Main(e, "ST_Andrius_Ground", 4206906),
                Main(e, "ST_CryoShaman_Ground", 4207701), Main(e, "ST_Frostarm_Ground", 4208003) };
            var instances = new List<StatusInstance>(); var damages = new List<string>();
            foreach (var main in definitions) {
                string effect = "STE_Test_" + main.StatusID2;
                e.DataManager.StatusEffectDict[effect] = new StatusEffectData { StatusEffectID = main.StatusID * 100 + 1,
                    StatusEffectID2 = effect, EffectType = "Damage", Element = "Cryo", DamageType = "Skill",
                    Param1 = ".15*ATK,1", TargetType = "Enemy", TargetSelect = "1", TargetOverride = "PotionFieldOccupant()" };
                e.DataManager.StatusActionDict[main.StatusID2] = new List<StatusActionData> { new StatusActionData {
                    StatusID = main.StatusID, StatusID2 = main.StatusID2, ActionType = "OnItsTurn", Param1 = effect } };
                field.AddStatus(main.StatusID2, e.Enemy.Entity, 2, 6, 6, main);
                instances.Add(field.AddStatus(main.StatusID2, e.Enemy.Entity, 2, 6, 6, main));
                Assert.That(field.StatusList.Count(s => s.IsActive && s.StatusID2 == main.StatusID2), Is.EqualTo(1));
            }
            Assert.That(field.StatusList.Count, Is.EqualTo(4));
            using var hit = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, d) => {
                if (d.Subject == e.Ally.Entity && d.Source.SourceStatus != null) damages.Add(d.Source.SourceStatus.StatusID2);
            }, 0);
            await BattleExecution.Run(async () => {
                foreach (var s in instances) await e.Enemy.ExecuteStatusActionsTask(s, e.DataManager.StatusActionDict[s.StatusID2], field, "OnItsTurn");
                await BattleExecution.Flush();
            });
            Assert.That(damages, Is.EquivalentTo(definitions.Select(m => m.StatusID2)));
        }

        [UnityTest] public IEnumerator FrostPunch_EachPrimaryHitTriggersOneCryoArea() => HookTestEnv.AwaitTask(() => PunchAsync(false));
        [UnityTest] public IEnumerator FrostPunch_LethalCenterStillDamagesItsNeighbors() => HookTestEnv.AwaitTask(() => PunchAsync(true));
        private async Task PunchAsync(bool lethal)
        {
            using var e = new BattleFlowTestEnv(); e.ConfigureEnemyDamageSkill(.5f); e.CreateMinimalBattle();
            var center = e.CreateAlly(2, hp: lethal ? 1 : 1000); e.CreateAlly(3);
            e.DataManager.EnemySkillMainDict[990201].EnemySkillID2 = "SK_Frostarm_Punch";
            e.DataManager.EnemySkillEffectDict["SE_Enemy_BattleFlow"].TargetOverride = "Hook(20040102)";
            e.DataManager.EnemySkillParamDict["SE_Enemy_BattleFlow"].Hits2 = ".5,0";
            var armor = Main(e, Enemy2074To2080Hooks.FrostArmor, 4208002);
            const string effect = "STE_Test_FrostPunch";
            e.DataManager.StatusEffectDict[effect] = new StatusEffectData { StatusEffectID = 420800201, StatusEffectID2 = effect,
                EffectType = "Damage", Element = "Cryo", DamageType = "Skill", Param1 = ".5*ATK,1",
                TargetType = "Enemy", TargetSelect = "-1", TargetOverride = "FrostarmPunchNeighbors()" };
            e.DataManager.StatusActionDict[armor.StatusID2] = new List<StatusActionData> { new StatusActionData {
                StatusID = armor.StatusID, StatusID2 = armor.StatusID2, ActionType = "OnTrigger", Param1 = effect,
                ScriptHook = "FrostarmPunchHit()", OutgoingHit = "All" } };
            e.Enemy.Entity.AddStatus(armor.StatusID2, e.Enemy.Entity, 0, 5, 0, armor);
            var areas = new List<int>();
            using var damage = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, d) => {
                if (d.Source.SourceEffectID == effect) areas.Add(d.Subject.SlotPosition);
            }, 0);
            await BattleExecution.Run(async () => { await e.Enemy.TakeEnemyTurnAsync(); await BattleExecution.Flush(); });
            Assert.That(areas, Is.EquivalentTo(lethal ? new[] { 1, 3 } : new[] { 1, 2, 3, 1, 2, 3 }));
            Assert.That(center.Entity.IsDead, Is.EqualTo(lethal));
        }

        [Test]
        public void CryoVolley_UsesOnlyItsOwnLivingOnFieldCicins()
        {
            using var e = new BattleFlowTestEnv(); e.CreateMinimalBattle(); Enemy(e, 2074); Enemy(e, 2078, "Elite", "愚人众");
            var first = e.CreateEnemy(2, enemyId: 2078); var other = e.CreateEnemy(3, enemyId: 2078);
            var owned = e.Manager.SummonEnemy(2074, 1, 4, first.Entity); e.Manager.SummonEnemy(2074, 1, 5, other.Entity);
            Assert.That(BattleSpecialRules.TryGetEnemyDamageParticipants(Enemy2074To2080Hooks.MageVolley,
                e.Manager, first.Entity, out var participants), Is.True);
            Assert.That(participants, Is.EqualTo(new[] { owned }));
            var off = Main(e, PublicControlStates.OffField, 40040); owned.Entity.AddStatus(off.StatusID2, owned.Entity, 0, 3, 0, off);
            BattleSpecialRules.TryGetEnemyDamageParticipants(Enemy2074To2080Hooks.MageVolley, e.Manager, first.Entity, out participants);
            Assert.That(participants, Is.Empty);
        }

        [Test]
        public void CryoPillars_FilterTypeFamilyPresenceAndGU_AndBlockWhileAnotherOwnTypeShieldExists()
        {
            using var e = new BattleFlowTestEnv(); e.CreateMinimalBattle();
            e.DataManager.EnemyMainDict[BattleFlowTestEnv.EnemyID].EnemyType = "Minion";
            e.DataManager.EnemyMainDict[BattleFlowTestEnv.EnemyID].EnemyFamily = "丘丘部族";
            Enemy(e, 991210, "Elite"); Enemy(e, 991211, family: "愚人众");
            var elite = e.CreateEnemy(2, enemyId: 991210); e.CreateEnemy(3, enemyId: 991211);
            var off = e.CreateEnemy(4); var shielded = e.CreateEnemy(5);
            var offMain = Main(e, PublicControlStates.OffField, 40040); off.Entity.AddStatus(offMain.StatusID2, off.Entity, 0, 3, 0, offMain);
            var shield = Main(e, "ST_Test_CryoPillarSource", 991212);
            var instance = shielded.Entity.AddStatus(shield.StatusID2, elite.Entity, 0, 5, 0, shield);
            shielded.Entity.AddOrReplaceEnemyElementalShield(1, "Pyro", instance);
            Assert.That(Enemy2074To2080Hooks.PillarTargets(e.Manager, e.Enemy.Entity), Is.EqualTo(new[] { 1 }));
            Assert.That(Enemy2074To2080Hooks.CanPillar(e.Manager, e.Enemy.Entity), Is.True);
            var pillar = Main(e, Enemy2074To2080Hooks.Pillar, 4207702);
            var existing = off.Entity.AddStatus(pillar.StatusID2, elite.Entity, 0, 5, 0, pillar);
            off.Entity.AddOrReplaceEnemyElementalShield(12, "Cryo", existing);
            Assert.That(Enemy2074To2080Hooks.CanPillar(e.Manager, e.Enemy.Entity), Is.False);
        }

        [Test]
        public void MageClock_IsRelativeToActualEntry_AndRestartsThreeTurnsAfterShieldLoss()
        {
            using var e = new BattleFlowTestEnv(); e.CreateMinimalBattle(); var owner = e.Enemy.Entity;
            void Turn(int n) => typeof(BattleManager).GetProperty("TurnCount").SetValue(e.Manager, n);
            Turn(10); typeof(EnemyBattleController).GetProperty("IsLastGuaranteedAction").SetValue(e.Enemy, true);
            var entry = Main(e, Enemy2074To2080Hooks.MageEntry, 4207801); owner.AddStatus(entry.StatusID2, owner, 0, 5, 0, entry);
            Turn(11); Assert.That(ScriptHookEvaluator.Evaluate("CryoCicinShieldDue()", owner), Is.False);
            Turn(12); Assert.That(ScriptHookEvaluator.Evaluate("CryoCicinShieldDue()", owner), Is.True);
            var charge = Main(e, Enemy2074To2080Hooks.MageCharge, 4207802); owner.AddStatus(charge.StatusID2, owner, 0, 4, 0, charge);
            Assert.That(ScriptHookEvaluator.Evaluate("CryoCicinShieldDue()", owner), Is.False);
            owner.RemoveStatus(charge.StatusID2); Turn(14);
            Assert.That(ScriptHookEvaluator.Evaluate("CryoCicinShieldDue()", owner), Is.False);
            Turn(15); Assert.That(ScriptHookEvaluator.Evaluate("CryoCicinShieldDue()", owner), Is.True);
        }
    }
}
