using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    /// <summary>
    /// 真实整表加载与最小战斗链路回归，不是全部角色/天赋的完整验收。
    /// 加载失败保留真实错误（可能来自未出战角色的表），不能跳表或忽略失败。
    /// 凯亚/安柏只负责覆盖普攻、战技、切人、爆发、融化及敌方阶段。
    /// </summary>
    public class BattleFlowRealConfigSmokeTests
    {
        [Test]
        [Category("RealConfig")]
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
                Assert.That(data.EnemyMainDict.ContainsKey(2001), Is.True, "真实丘丘人EnemyID为2001，不以旧编号构造无配表单位");
                BattleEntity enemy = HookTestEnv.CreateEnemy(2001, 1, 1, manager, 5000f);
                kaeya.IsActive = true;
                amber.IsActive = false;

                Assert.That(kaeya.NormalAttackID, Is.EqualTo("SK_Normal_Kaeya"));
                Assert.That(kaeya.ElementalSkillID, Is.EqualTo("SK_Skill_Kaeya"));
                Assert.That(data.SkillEffectDict.ContainsKey("SE_Skill_Kaeya1"), Is.True);
                Assert.That(data.StatusMainDict.ContainsKey("ST_Burst_Kaeya"), Is.True);

                manager.StartBattle();
                flow.AdvanceToPhase(manager, TurnPhase.AllyAction);
                // 在战斗重置完成后注入，使暴击与后续敌方选择的随机序列可复现。
                var random = new SystemBattleRandomSource(10091010);
                BattleRandom.SetSource(random);
                float startHP = enemy.CurrentHP;
                long beforeNormal = random.Step;
                Assert.That(manager.UseNormalAttackBySlot(0), Is.True);
                Assert.That(random.Step, Is.GreaterThan(beforeNormal),
                    "真实普攻应进入最终暴击流程；具体hit数量仍由真实配表决定");
                Assert.That(manager.UseSkillBySlot(0), Is.True);
                Assert.That(enemy.CurrentHP, Is.LessThan(startHP));
                Assert.That(enemy.GetAura("Cryo"), Is.Not.Null);

                Assert.That(manager.TrySwitchActiveAlly(1), Is.True,
                    "真实配表流程应通过 BattleManager 公开入口切换到安柏");
                amber.Entity.CurrentEnergy = amber.Entity.MaxEnergy;
                Assert.That(manager.UseBurstBySlot(1), Is.True);
                Assert.That(enemy.GetAura("Cryo"), Is.Null,
                    "安柏真实爆发应通过真实伤害入口与凯亚冰附着发生融化");

                var enemyActions = new List<ActionEventData>();
                using (BattleEventHub.Subscribe<ActionEventData>(BattleEventKind.ActionCompleted,
                    (header, action) =>
                    {
                        if (action.Subject == enemy && action.ActionType == "EnemySkill")
                            enemyActions.Add(action);
                    }, 0))
                {
                    manager.EndAllyTurn();
                    flow.AdvanceUntil(manager,
                        () => manager.IsBattleOver || (manager.CurrentPhase == TurnPhase.EnemyPostTurn
                            && manager.IsCurrentPhaseReady),
                        "real enemy action completes or ends the battle");
                }
                Assert.That(enemyActions, Is.Not.Empty,
                    "到达敌方阶段不等于敌人出手；必须收到该敌人真实完成技能的声明");
                foreach (ActionEventData action in enemyActions)
                {
                    Assert.That(action.Phase, Is.EqualTo((int)TurnPhase.EnemyAction));
                    Assert.That(data.EnemySkillMainDict.Values.Any(skill => skill.EnemySkillID2 == action.SkillId),
                        Is.True, $"敌方实际使用的技能必须来自真实配表：{action.SkillId}");
                }
            }
            finally
            {
                try
                {
                    if (manager != null) manager.ResetBattle();
                }
                finally
                {
                    // 测试工厂创建的单位已挂到管理器下；初始化或断言失败时也一起销毁。
                    if (manager != null) UnityEngine.Object.DestroyImmediate(manager.gameObject);
                    HookTestEnv.ClearBattleManagerSingleton();
                    HookTestEnv.DestroyDataManager(data);
                    HookTestEnv.ResetStaticState();
                }
            }
        }
    }
}
