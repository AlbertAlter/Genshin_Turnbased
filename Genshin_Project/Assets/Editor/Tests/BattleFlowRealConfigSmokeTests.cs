using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    public class BattleFlowRealConfigSmokeTests
    {
        [Test]
        [Timeout(12000)]
        public void KaeyaAndAmberAgainstHilichurl_LoadAndTraverseRealConfigGraph()
        {
            HookTestDataManager data = null;
            BattleManager manager = null;
            try
            {
                data = HookTestEnv.CreateDataManager(loadRealData: true);
                manager = HookTestEnv.CreateBattleManager();
                var scheduler = new ManualBattleFlowScheduler();
                var flow = new BattleFlowTestDriver(scheduler);
                manager.SetFlowScheduler(scheduler);
                manager.PhaseInterval = 0f;
                CharacterBattleController kaeya = HookTestEnv.CreateCharacter(1010, 1, 1, manager);
                CharacterBattleController amber = HookTestEnv.CreateCharacter(1009, 1, 2, manager);
                BattleEntity enemy = HookTestEnv.CreateEnemy(20001, 1, 1, manager, 5000f);
                kaeya.IsActive = true;
                amber.IsActive = false;

                Assert.That(kaeya.NormalAttackID, Is.EqualTo("SK_Normal_Kaeya"));
                Assert.That(kaeya.ElementalSkillID, Is.EqualTo("SK_Skill_Kaeya"));
                Assert.That(data.SkillEffectDict.ContainsKey("SE_Skill_Kaeya1"), Is.True);
                Assert.That(data.StatusMainDict.ContainsKey("ST_Burst_Kaeya"), Is.True);

                manager.StartBattle();
                flow.AdvanceToPhase(manager, TurnPhase.AllyAction);
                float startHP = enemy.CurrentHP;
                Assert.That(manager.UseNormalAttackBySlot(0), Is.True);
                Assert.That(manager.UseSkillBySlot(0), Is.True);
                Assert.That(enemy.CurrentHP, Is.LessThan(startHP));
                Assert.That(enemy.GetAura("Cryo"), Is.Not.Null);

                Assert.That(manager.TrySwitchActiveAlly(1), Is.True,
                    "真实配表流程应通过 BattleManager 公开入口切换到安柏");
                amber.Entity.CurrentEnergy = amber.Entity.MaxEnergy;
                Assert.That(manager.UseBurstBySlot(1), Is.True);
                Assert.That(enemy.GetAura("Cryo"), Is.Null,
                    "安柏真实爆发应通过真实伤害入口与凯亚冰附着发生融化");

                manager.EndAllyTurn();
                flow.AdvanceToPhase(manager, TurnPhase.EnemyPostTurn);
                Assert.That(manager.IsBattleRunning || manager.IsBattleOver, Is.True);
            }
            finally
            {
                if (manager != null) UnityEngine.Object.DestroyImmediate(manager.gameObject);
                HookTestEnv.ClearBattleManagerSingleton();
                HookTestEnv.DestroyDataManager(data);
                HookTestEnv.ResetStaticState();
            }
        }
    }
}
