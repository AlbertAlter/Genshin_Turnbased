using NUnit.Framework;
using UnityEngine;

namespace GenshinTurnBased.Tests.EditMode
{
    /// <summary>
    /// KillHookSystem 测试（2026-08-19，规格第 2 项）：
    /// 使用真实配表数据（ST_Kaeya_C2 的 Kill(1010) + STE_Kaeya_C2 AddTurn 延长凛冽轮舞）。
    /// </summary>
    public class KillHookSystemTests
    {
        private GameObject _dmObject;
        private HookTestDataManager _dm;
        private GameObject _bmObject;
        private BattleManager _bm;
        private GameObject _kaeyaObject;
        private CharacterBattleController _kaeya;
        private GameObject _enemyObject;
        private BattleEntity _enemy;

        private StatusInstance _burst;    // ST_Burst_Kaeya（延长目标）
        private StatusInstance _kaeyaC2;  // ST_Kaeya_C2（Kill(1010) 状态实例）

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _dmObject = new GameObject("KillHook_DM");
            _dm = _dmObject.AddComponent<HookTestDataManager>();
            _dm.ReloadAllData();
            HookTestEnv.SetDataManagerSingleton(_dm);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            HookTestEnv.DestroyDataManager(_dm);
            _dmObject = null;
        }

        [SetUp]
        public void SetUp()
        {
            HookTestEnv.ResetStaticState();

            _bm = HookTestEnv.CreateBattleManager();
            _bmObject = _bm.gameObject;

            _kaeyaObject = new GameObject("KillHook_Kaeya");
            _kaeya = _kaeyaObject.AddComponent<CharacterBattleController>();
            _kaeya.Init(1010, 80);
            _kaeya.Entity.SlotPosition = 1;
            _kaeya.Entity.Side = BattleSide.Ally;
            _bm.RegisterAlly(_kaeya);
            if (_bm.APManager != null) _bm.APManager.ResetAP();

            // 凛冽轮舞（延长目标）与 C2 状态实例（Kill(1010) 行由真实配表提供）
            _burst = HookTestEnv.AttachStatus(_kaeya.Entity, "ST_Burst_Kaeya", _kaeya.Entity, 1);
            _kaeyaC2 = HookTestEnv.AttachStatus(_kaeya.Entity, "ST_Kaeya_C2", _kaeya.Entity, 1);

            _enemyObject = new GameObject("KillHook_Enemy");
            var enemyCtrl = _enemyObject.AddComponent<EnemyBattleController>();
            enemyCtrl.InitEnemy(20000, 1); // 测试木桩
            _enemy = enemyCtrl.Entity;
            _enemy.TotalHP = 100f;
            _enemy.CurrentHP = 100f;
            _enemy.SlotPosition = 1;
            _enemy.Side = BattleSide.Enemy;
            _bm.RegisterEnemy(enemyCtrl);
        }

        [TearDown]
        public void TearDown()
        {
            HookTestEnv.ResetStaticState();
            HookTestEnv.ClearBattleManagerSingleton();
            Object.DestroyImmediate(_enemyObject);
            Object.DestroyImmediate(_kaeyaObject);
            Object.DestroyImmediate(_bmObject);
        }

        // ---------- 参数匹配（纯单元） ----------

        [Test]
        public void MatchesKillArgument_Number_MatchesEntityId()
        {
            var source = DamageSourceInfo.Create(_kaeya.Entity, ReactionSourceKind.CharacterSkill, "SK_Test", "SE_Test");
            Assert.That(KillHookSystem.MatchesKillArgument("1010", source), Is.True);
            Assert.That(KillHookSystem.MatchesKillArgument("1009", source), Is.False);
        }

        [Test]
        public void MatchesKillArgument_SkillId_Match()
        {
            var source = DamageSourceInfo.Create(_kaeya.Entity, ReactionSourceKind.CharacterSkill, "SK_Normal_Kaeya", "SE_Test");
            Assert.That(KillHookSystem.MatchesKillArgument("SK_Normal_Kaeya", source), Is.True);
            Assert.That(KillHookSystem.MatchesKillArgument("SK_Other", source), Is.False);
        }

        [Test]
        public void MatchesKillArgument_EffectId_Match()
        {
            var source = DamageSourceInfo.Create(_kaeya.Entity, ReactionSourceKind.CharacterSkill, "SK_Test", "SE_Normal_Kaeya");
            Assert.That(KillHookSystem.MatchesKillArgument("SE_Normal_Kaeya", source), Is.True);
            Assert.That(KillHookSystem.MatchesKillArgument("STE_Kaeya_C2", source), Is.False);
        }

        [Test]
        public void MatchesKillArgument_EmptyOrUnknown_ReturnsFalse()
        {
            var source = DamageSourceInfo.Create(_kaeya.Entity, ReactionSourceKind.CharacterSkill, "SK_Test", "SE_Test");
            Assert.That(KillHookSystem.MatchesKillArgument("", source), Is.False);
            Assert.That(KillHookSystem.MatchesKillArgument("abc", source), Is.False);
            Assert.That(KillHookSystem.MatchesKillArgument("1010", null), Is.False);
        }

        // ---------- 死亡触发（真实 ST_Kaeya_C2 配置） ----------

        [Test]
        public void NonLethalDamage_DoesNotTriggerKill()
        {
            KillHookSystem.Register(_kaeyaC2, _kaeya.Entity);

            _enemy.TakeDamage(50f, DamageSourceInfo.Create(_kaeya.Entity, ReactionSourceKind.CharacterSkill, "SK_Test", "SE_Test"));

            Assert.That(_enemy.IsAlive, Is.True);
            Assert.That(_kaeyaC2.LifeTriggerCount, Is.Zero);
            Assert.That(_burst.RemainingPhaseCount, Is.EqualTo(1), "非致死伤害不应延长凛冽轮舞");
        }

        [Test]
        public void LethalDamage_TriggersKillOnce_ExtendsBurst()
        {
            KillHookSystem.Register(_kaeyaC2, _kaeya.Entity);

            _enemy.TakeDamage(100f, DamageSourceInfo.Create(_kaeya.Entity, ReactionSourceKind.CharacterSkill, "SK_Normal_Kaeya", "SE_Normal_Kaeya"));

            Assert.That(_enemy.IsDead, Is.True);
            Assert.That(_kaeyaC2.LifeTriggerCount, Is.EqualTo(1), "Kill(1010) 应正确匹配角色 1010 的击杀");
            Assert.That(_burst.RemainingPhaseCount, Is.EqualTo(2), "STE_Kaeya_C2 应延长凛冽轮舞 1t");
        }

        [Test]
        public void DamageOnDeadTarget_DoesNotReTrigger()
        {
            KillHookSystem.Register(_kaeyaC2, _kaeya.Entity);

            _enemy.TakeDamage(100f, DamageSourceInfo.Create(_kaeya.Entity, ReactionSourceKind.CharacterSkill, "SK_Test", "SE_Test"));
            _enemy.TakeDamage(100f, DamageSourceInfo.Create(_kaeya.Entity, ReactionSourceKind.CharacterSkill, "SK_Test", "SE_Test"));

            Assert.That(_kaeyaC2.LifeTriggerCount, Is.EqualTo(1), "已死亡目标再次受击不得再次产生死亡声明");
            Assert.That(_burst.RemainingPhaseCount, Is.EqualTo(2), "同一死亡只延长一次");
        }

        [Test]
        public void Kill_WrongCharacterId_DoesNotTrigger()
        {
            var custom = HookTestEnv.AttachStatus(_kaeya.Entity, "ST_TEST_KILL_WRONG", _kaeya.Entity, 3);
            HookTestEnv.InjectStatusEffect(_dm, "STE_TEST_ADD", "AddTurn", "ST_Burst_Kaeya", "1", "None", "Self");
            HookTestEnv.InjectStatusAction(_dm, "ST_TEST_KILL_WRONG", "OnTrigger", "STE_TEST_ADD", "Kill(1009)");
            KillHookSystem.Register(custom, _kaeya.Entity);

            _enemy.TakeDamage(100f, DamageSourceInfo.Create(_kaeya.Entity, ReactionSourceKind.CharacterSkill, "SK_Test", "SE_Test"));

            Assert.That(custom.LifeTriggerCount, Is.Zero, "Kill(1009) 不应匹配角色 1010 的击杀");
            Assert.That(_burst.RemainingPhaseCount, Is.EqualTo(1));
        }

        [Test]
        public void Kill_SkillId_Match_Triggers()
        {
            var custom = HookTestEnv.AttachStatus(_kaeya.Entity, "ST_TEST_KILL_SK", _kaeya.Entity, 3);
            HookTestEnv.InjectStatusEffect(_dm, "STE_TEST_ADD_SK", "AddTurn", "ST_Burst_Kaeya", "1", "None", "Self");
            HookTestEnv.InjectStatusAction(_dm, "ST_TEST_KILL_SK", "OnTrigger", "STE_TEST_ADD_SK", "Kill(SK_Normal_Kaeya)");
            KillHookSystem.Register(custom, _kaeya.Entity);

            _enemy.TakeDamage(100f, DamageSourceInfo.Create(_kaeya.Entity, ReactionSourceKind.CharacterSkill, "SK_Normal_Kaeya", "SE_Normal_Kaeya"));

            Assert.That(custom.LifeTriggerCount, Is.EqualTo(1), "Kill(SK_xxx) 应匹配来源技能ID2");
            Assert.That(_burst.RemainingPhaseCount, Is.EqualTo(2));
        }

        [Test]
        public void Kill_EffectId_Match_Triggers()
        {
            var custom = HookTestEnv.AttachStatus(_kaeya.Entity, "ST_TEST_KILL_SE", _kaeya.Entity, 3);
            HookTestEnv.InjectStatusEffect(_dm, "STE_TEST_ADD_SE", "AddTurn", "ST_Burst_Kaeya", "1", "None", "Self");
            HookTestEnv.InjectStatusAction(_dm, "ST_TEST_KILL_SE", "OnTrigger", "STE_TEST_ADD_SE", "Kill(SE_Normal_Kaeya)");
            KillHookSystem.Register(custom, _kaeya.Entity);

            _enemy.TakeDamage(100f, DamageSourceInfo.Create(_kaeya.Entity, ReactionSourceKind.CharacterSkill, "SK_Normal_Kaeya", "SE_Normal_Kaeya"));

            Assert.That(custom.LifeTriggerCount, Is.EqualTo(1), "Kill(SE_xxx) 应匹配来源效果ID2");
            Assert.That(_burst.RemainingPhaseCount, Is.EqualTo(2));
        }

        // ---------- 生命周期 ----------

        [Test]
        public void StatusRemoved_DoesNotTrigger()
        {
            KillHookSystem.Register(_kaeyaC2, _kaeya.Entity);
            _kaeya.Entity.RemoveStatus("ST_Kaeya_C2");

            _enemy.TakeDamage(100f, DamageSourceInfo.Create(_kaeya.Entity, ReactionSourceKind.CharacterSkill, "SK_Test", "SE_Test"));

            Assert.That(_kaeyaC2.LifeTriggerCount, Is.Zero, "状态移除后不得再触发");
        }

        [Test]
        public void StatusExpired_DoesNotTrigger()
        {
            // 到期路径与移除路径在 BattleManager 到期结算中统一走 RemoveStatus → KillHookSystem.Unregister
            KillHookSystem.Register(_kaeyaC2, _kaeya.Entity);
            _kaeyaC2.RemainingPhaseCount = 0;
            _kaeya.Entity.RemoveStatus("ST_Kaeya_C2");

            _enemy.TakeDamage(100f, DamageSourceInfo.Create(_kaeya.Entity, ReactionSourceKind.CharacterSkill, "SK_Test", "SE_Test"));

            Assert.That(_kaeyaC2.LifeTriggerCount, Is.Zero, "状态到期后不得再触发");
        }

        [Test]
        public void ResetBattle_ClearsKillHooks()
        {
            KillHookSystem.Register(_kaeyaC2, _kaeya.Entity);
            Assert.That(KillHookSystem.EntryCount, Is.GreaterThan(0));

            _bm.ResetBattle();

            Assert.That(KillHookSystem.EntryCount, Is.Zero, "ResetBattle 必须清空上一场登记");
            _enemy.TakeDamage(100f, DamageSourceInfo.Create(_kaeya.Entity, ReactionSourceKind.CharacterSkill, "SK_Test", "SE_Test"));
            Assert.That(_kaeyaC2.LifeTriggerCount, Is.Zero);
        }

        [Test]
        public void MultipleKillHooks_AllFireOnOneDeath()
        {
            // 两个独立状态实例的 Kill 钩子（ApplyOrder 不同），同一次死亡都应按顺序触发
            var orderA = HookTestEnv.AttachStatus(_kaeya.Entity, "ST_TEST_ORDER_A", _kaeya.Entity, 3);
            orderA.ApplyOrder = 2;
            var orderB = HookTestEnv.AttachStatus(_kaeya.Entity, "ST_TEST_ORDER_B", _kaeya.Entity, 3);
            orderB.ApplyOrder = 1;
            HookTestEnv.InjectStatusEffect(_dm, "STE_TEST_ORDER_A", "GainEnergy", "5", "Flat", "None", "Self");
            HookTestEnv.InjectStatusEffect(_dm, "STE_TEST_ORDER_B", "GainEnergy", "5", "Flat", "None", "Self");
            HookTestEnv.InjectStatusAction(_dm, "ST_TEST_ORDER_A", "OnTrigger", "STE_TEST_ORDER_A", "Kill(1010)");
            HookTestEnv.InjectStatusAction(_dm, "ST_TEST_ORDER_B", "OnTrigger", "STE_TEST_ORDER_B", "Kill(1010)");
            KillHookSystem.Register(orderA, _kaeya.Entity);
            KillHookSystem.Register(orderB, _kaeya.Entity);

            _enemy.TakeDamage(100f, DamageSourceInfo.Create(_kaeya.Entity, ReactionSourceKind.CharacterSkill, "SK_Test", "SE_Test"));

            Assert.That(orderA.LifeTriggerCount, Is.EqualTo(1));
            Assert.That(orderB.LifeTriggerCount, Is.EqualTo(1));
            Assert.That(_kaeya.Entity.CurrentEnergy, Is.EqualTo(10), "两个 Kill 钩子的行动都应在一次死亡中执行");
        }

        [Test]
        public void LastEnemyKilled_KillHookFiresAndBattleEnds()
        {
            KillHookSystem.Register(_kaeyaC2, _kaeya.Entity);

            _enemy.TakeDamage(100f, DamageSourceInfo.Create(_kaeya.Entity, ReactionSourceKind.CharacterSkill, "SK_Normal_Kaeya", "SE_Normal_Kaeya"));

            // 死亡钩子先于最终战斗结束通知执行：击杀最后敌人时钩子仍能收到死亡声明
            Assert.That(_burst.RemainingPhaseCount, Is.EqualTo(2), "击杀最后敌人时 Kill 钩子必须已执行");
            Assert.That(_bm.IsBattleOver, Is.True);
            Assert.That(_bm.Victory, Is.True);
        }

        [Test]
        public void ReactionDerivedDamage_KeepsCharacterSource()
        {
            // 超载派生伤害（来源快照保留角色1010归属）击杀敌人 → Kill(1010) 触发
            KillHookSystem.Register(_kaeyaC2, _kaeya.Entity);
            _enemy.TotalHP = 100f;
            _enemy.CurrentHP = 10f;
            _enemy.ApplyAura("Electro", 2f, 1010);

            var context = new ReactionContext
            {
                SourceEntity = _kaeya.Entity,
                Target = _enemy,
                SourceKind = ReactionSourceKind.CharacterSkill,
                SourceSkillID = "SK_Test",
                SourceEffectID = "SE_Test",
                EffectExecutionID = ReactionResolver.BeginEffectExecution(),
                ApplicationPhase = (int)TurnPhase.AllyAction,
                AttackElement = "Pyro",
                AttackAmount = 1f,
                PreReactionDamage = 1f,
                DamageType = "Normal",
                PoiseDamage = 0f
            };
            ReactionResult result = ReactionResolver.Resolve(context);
            Assert.That(result.HasReaction, Is.True, "火攻雷附着应触发超载");

            float finalDamage = _enemy.AbsorbDamageWithShield(result.FinalDamage, "Pyro");
            _enemy.TakeDamage(finalDamage, DamageSourceInfo.FromReactionContext(context, result.TriggeredReactions[0].Type));
            ReactionEffectExecutor.ExecuteDerivedHits(result);

            Assert.That(_enemy.IsDead, Is.True, "超载派生伤害应击杀低血量敌人");
            Assert.That(_kaeyaC2.LifeTriggerCount, Is.EqualTo(1), "反应派生伤害必须保留原始角色1010来源");
            Assert.That(_burst.RemainingPhaseCount, Is.EqualTo(2));
        }
    }
}
