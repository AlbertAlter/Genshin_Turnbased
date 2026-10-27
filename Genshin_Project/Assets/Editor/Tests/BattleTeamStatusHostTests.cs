using System;
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    /// <summary>队伍宿主接入的人工Unity验收用例；不读取策划源目录。</summary>
    public sealed class BattleTeamStatusHostTests
    {
        private static StatusMainData Buff(BattleFlowTestEnv env, string id = "ST_Team_Test", int number = 99110)
        {
            var main = new StatusMainData { StatusID = number, StatusID2 = id, StatusName = id,
                Display = 1, MultiplierPart1 = "ATKBonus", MaxStack = 1, MaxCount = 1, WhenMax = "0" };
            env.DataManager.StatusMainDict[id] = main;
            env.DataManager.StatusEffectDict["STE_" + id] = new StatusEffectData
                { StatusEffectID = number * 100 + 1, StatusEffectID2 = "STE_" + id, EffectType = "Buff", Param1 = ".25" };
            return main;
        }

        [Test]
        public void RealSkill_TeamBuffAffectsReserves_WithoutCopyingOrSelectingUnits()
        {
            using (var env = new BattleFlowTestEnv())
            {
                var main = Buff(env);
                env.ConfigureAllyAction(2, "SK_Skill_Team", "SE_Skill_Team", "Physical", null,
                    effectType: "ApplyStatus", param1: main.StatusID2, duration: 2, addInPhase: 3, targetType: "AllyTeam");
                env.CreateMinimalBattle();
                var reserve = env.CreateAlly(2, false).Entity;
                env.Manager.StartBattle();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                Assert.That(env.Ally.GetFirstSelectEffect(2), Is.Null);
                Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
                var status = env.Manager.AllyTeam.GetStatus(main.StatusID2);
                Assert.That(status, Is.Not.Null);
                Assert.That(status.Host, Is.SameAs(env.Manager.AllyTeam));
                foreach (var member in new[] { env.Ally.Entity, reserve })
                {
                    Assert.That(member.HasStatus(main.StatusID2), Is.False);
                    Assert.That(member.GetStatusList(), Has.No.Member(status), "个人状态栏仍只读实际挂载");
                    Assert.That(member.GetEffectiveStatusList(), Has.Member(status));
                    Assert.That(member.GetStatusATKBonus("", "", -1, ""), Is.EqualTo(.25f));
                }
                Assert.That(env.Enemy.Entity.GetEffectiveStatusList(), Has.No.Member(status));
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyPostTurn);
                Assert.That(status.RemainingPhaseCount, Is.EqualTo(1), "不是每个受益者各扣一次");
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyPostTurn);
                Assert.That(env.Manager.AllyTeam.Contains(status), Is.False);
                Assert.That(reserve.GetStatusATKBonus("", "", -1, ""), Is.Zero);
            }
        }

        [Test]
        public void EnemyTeamBuff_UsesHostSideRatherThanCasterSide_AndDisplayDoesNotFilterQueries()
        {
            using (var env = new BattleFlowTestEnv())
            {
                var main = Buff(env);
                main.Display = 0;
                env.CreateMinimalBattle();
                var enemy2 = env.CreateEnemy(2).Entity;
                env.Manager.StartBattle();
                var status = env.Manager.EnemyTeam.AddStatus(main.StatusID2, env.Ally.Entity, 0, 2, 0, main);
                Assert.That(env.Manager.EnemyTeam.GetStatusList(), Has.Member(status));
                foreach (var enemy in new[] { env.Enemy.Entity, enemy2 })
                    Assert.That(enemy.GetStatusATKBonus("", "", -1, ""), Is.EqualTo(.25f));
                Assert.That(env.Ally.Entity.GetStatusATKBonus("", "", -1, ""), Is.Zero);
                var result = BattleTargetResolver.Resolve(env.Manager, env.Enemy.Entity, env.Enemy.Entity,
                    "AllyTeam", 0, 0, null, null, null, null);
                Assert.That(result.Domain, Is.EqualTo(BattleTargetDomain.Team));
                Assert.That(result.Positions, Is.Empty);
                Assert.That(BattleTargetResolver.GetTeams(env.Manager, result)[0], Is.SameAs(env.Manager.EnemyTeam));
            }
        }

        [Test]
        public void TeamAction_IsTriggeredOnce_AndSelfStillMeansRealCaster()
        {
            using (var env = new BattleFlowTestEnv())
            {
                const string id = "ST_Team_Action";
                env.ConfigureStatus(id, actionType: "OnTrigger", actionEffectID2: "STE_Team_Energy");
                env.DataManager.StatusActionDict[id][0].OnAction = "Skill";
                env.DataManager.StatusEffectDict["STE_Team_Energy"] = new StatusEffectData
                { StatusEffectID2 = "STE_Team_Energy", EffectType = "GainEnergy", Param1 = "1", Param2 = "Flat", TargetType = "Self" };
                env.CreateMinimalBattle();
                env.CreateAlly(2, false); env.CreateAlly(3, false); env.CreateAlly(4, false);
                env.Manager.StartBattle();
                var status = env.Manager.AllyTeam.AddStatus(id, env.Ally.Entity, 0, 2, 0, env.DataManager.StatusMainDict[id]);
                float before = env.Ally.Entity.CurrentEnergy;
                StatusOnHitHookSystem.NotifyPlayerAction(env.Ally.Entity, new SkillMainData { SkillID2 = "SK_Test", ActionType = "Skill" });
                Assert.That(status.LifeTriggerCount, Is.EqualTo(1));
                Assert.That(env.Ally.Entity.CurrentEnergy, Is.EqualTo(before + 1));
                env.Manager.AllyTeam.RemoveStatusInstance(status);
                StatusOnHitHookSystem.NotifyPlayerAction(env.Ally.Entity, new SkillMainData { SkillID2 = "SK_Test", ActionType = "Skill" });
                Assert.That(status.LifeTriggerCount, Is.EqualTo(1));
            }
        }

        [Test]
        public void BindStatus_CanBindAcrossTeams_AndRemovalOnlyRemovesItsOwnChild()
        {
            using (var env = new BattleFlowTestEnv())
            {
                var child = Buff(env);
                var parent = new StatusMainData { StatusID = 99111, StatusID2 = "ST_Team_Parent", MaxStack = 1 };
                env.DataManager.StatusMainDict[parent.StatusID2] = parent;
                env.DataManager.StatusEffectDict["STE_Team_Bind"] = new StatusEffectData
                { StatusEffectID = 9911101, StatusEffectID2 = "STE_Team_Bind", EffectType = "BindStatus", Param1 = child.StatusID2, TargetType = "EnemyTeam" };
                env.CreateMinimalBattle(); env.Manager.StartBattle();
                var status = env.Manager.AllyTeam.AddStatus(parent.StatusID2, env.Ally.Entity, 0, 2, 0, parent);
                Assert.That(status.BoundStatuses, Has.Count.EqualTo(1));
                Assert.That(env.Manager.EnemyTeam.Contains(status.BoundStatuses[0]), Is.True);
                Assert.That(env.Enemy.Entity.GetStatusATKBonus("", "", -1, ""), Is.EqualTo(.25f));
                env.Manager.AllyTeam.RemoveStatusInstance(status);
                Assert.That(env.Manager.EnemyTeam.HasStatus(child.StatusID2), Is.False);
            }
        }

        [Test]
        public void EquipmentOwnerDeath_ClearsTeamEffects_ButDoesNotEraseSharedDew()
        {
            using (var env = new BattleFlowTestEnv())
            {
                var main = Buff(env);
                env.CreateMinimalBattle(); env.CreateAlly(2, false); env.Manager.StartBattle();
                env.Manager.TeamBonuses.VerdantDew.Gain(3, env.Ally.Entity);
                var status = env.Manager.AllyTeam.AddStatus(main.StatusID2, env.Ally.Entity, 0, 2, 0, main,
                    equipmentSource: new EquipmentEffectSource(env.Ally.Entity));
                env.Ally.Entity.TakeDamage(100000, DamageSourceInfo.Create(env.Enemy.Entity, ReactionSourceKind.EnemySkill, "SK_Kill", "SE_Kill"));
                Assert.That(status.IsActive, Is.False);
                Assert.That(env.Manager.AllyTeam.Contains(status), Is.False);
                Assert.That(env.Manager.TeamBonuses.VerdantDew.Value, Is.EqualTo(3));
                Assert.That(env.Manager.AllyTeam.GetStatusInstances(TeamPublicStatuses.VerdantDew), Has.Count.EqualTo(3));
            }
        }

        [Test]
        public void Dew_UsesPublicInstances_AtomicSpendAndResetHaveNoSecondBalance()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle(); env.Manager.StartBattle();
                var host = env.Manager.AllyTeam;
                var pool = env.Manager.TeamBonuses.VerdantDew;
                pool.Gain(int.MaxValue, env.Ally.Entity);
                var tokens = host.GetStatusInstances(TeamPublicStatuses.VerdantDew);
                Assert.That(tokens, Has.Count.EqualTo(3));
                foreach (var token in tokens)
                {
                    Assert.That(token.MainData, Is.SameAs(env.DataManager.StatusMainDict[TeamPublicStatuses.VerdantDew]));
                    Assert.That(token.MainData.StatusID, Is.EqualTo(40025));
                    Assert.That(token.StackCount, Is.EqualTo(1));
                    Assert.That(env.Ally.Entity.GetStatusList(), Has.No.Member(token));
                }
                Assert.Throws<InvalidOperationException>(() => host.RemoveStatusInstance(tokens[0]));
                Assert.That(pool.TrySpend(4, env.Ally.Entity), Is.False);
                Assert.That(pool.TrySpend(2, env.Ally.Entity), Is.True);
                Assert.That(host.GetStatusInstances(TeamPublicStatuses.VerdantDew), Has.Count.EqualTo(1));
                Assert.That(pool.Value, Is.EqualTo(1));
                env.Manager.ResetBattle();
                Assert.That(host.IsDisposed, Is.True);
                Assert.That(host.GetStatusList(), Is.Empty);
                Assert.That(pool.Value, Is.Zero);
                Assert.That(env.Manager.AllyTeam, Is.Not.SameAs(host));
            }
        }

        [Test]
        public void TeamTargets_AreNotUnitDamageTargets_AndPersonalCountsAreNotMadeShared()
        {
            Assert.Throws<TableValidationException>(() => TableValidator.ValidateEffectRow("Test", 2,
                "Damage", "Pyro", 0, null, "AllyTeam", 0, 0, null, false));
            Assert.DoesNotThrow(() => TableValidator.ValidateEffectRow("Test", 2,
                "ApplyStatus", null, 0, null, "AllyTeam", 0, 0, null, true));
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle(); env.Manager.StartBattle();
                var result = BattleTargetResolver.Resolve(env.Manager, env.Ally.Entity, env.Ally.Entity,
                    "EnemyTeam", 0, 0, null, null, null, null);
                Assert.Throws<InvalidOperationException>(() => BattleTargetResolver.GetEntities(env.Manager, result));
                Assert.Throws<NotSupportedException>(() => env.Manager.AllyTeam.AddStatus(BondOfLifeStatusHandler.StatusID,
                    env.Ally.Entity, 0, 2, 0, new StatusMainData { StatusID2 = BondOfLifeStatusHandler.StatusID, StatusType = "Count" }));
                var hp = Buff(env, "ST_Team_HP", 99112);
                hp.MultiplierPart1 = "HPBonus";
                Assert.Throws<NotSupportedException>(() => env.Manager.AllyTeam.AddStatus(hp.StatusID2,
                    env.Ally.Entity, 0, 2, 0, hp));
                var cloud = env.DataManager.StatusMainDict[TeamPublicStatuses.Thundercloud];
                Assert.That(cloud.StatusName, Is.EqualTo("雷暴云"));
                Assert.That(cloud.StatusID, Is.EqualTo(40026));
                Assert.That(env.Manager.EnemyTeam.HasStatus(cloud.StatusID2), Is.False, "只预留定义，不自动假造云生成");
            }
        }
    }
}
