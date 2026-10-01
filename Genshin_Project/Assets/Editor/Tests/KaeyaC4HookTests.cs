using NUnit.Framework;
using UnityEngine;

namespace GenshinTurnBased.Tests.EditMode
{
    /// <summary>
    /// Kaeya_C4 钩子测试（2026-08-19，规格第 12 项）：
    /// 钩子级严格条件 + 统一 OnHit 集成（敌人伤害也能触发）+ 15 回合冷却。
    /// 真实配表：ST_Kaeya_C4 OnHit → STE_Kaeya_C4 Shield 0.3*TotalHP, Cryo, 5t, Cooldown=15。
    /// </summary>
    public class KaeyaC4HookTests
    {
        private GameObject _dmObject;
        private HookTestDataManager _dm;
        private GameObject _bmObject;
        private BattleManager _bm;
        private GameObject _kaeyaObject;
        private CharacterBattleController _kaeya;
        private GameObject _enemyObject;
        private BattleEntity _enemy;
        private StatusInstance _kaeyaC4;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _dmObject = new GameObject("KaeyaC4_DM");
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

            _kaeyaObject = new GameObject("KaeyaC4_Kaeya");
            _kaeya = _kaeyaObject.AddComponent<CharacterBattleController>();
            _kaeya.Init(1010, 80);
            _kaeya.Entity.SlotPosition = 1;
            _kaeya.Entity.Side = BattleSide.Ally;
            _kaeya.Entity.TotalHP = 100f;
            _kaeya.Entity.CurrentHP = 100f;
            _bm.RegisterAlly(_kaeya);
            if (_bm.APManager != null) _bm.APManager.ResetAP();

            _kaeyaC4 = HookTestEnv.AttachStatus(_kaeya.Entity, "ST_Kaeya_C4", _kaeya.Entity, 5);

            _enemyObject = new GameObject("KaeyaC4_Enemy");
            var enemyCtrl = _enemyObject.AddComponent<EnemyBattleController>();
            enemyCtrl.InitEnemy(20000, 1);
            _enemy = enemyCtrl.Entity;
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

        // ---------- 钩子级严格条件（纯求值） ----------

        private static ScriptHookContext C4Context(BattleEntity target, float hpBefore, float hpAfter, float actualDamage, bool hitLanded = true)
        {
            // 钩子判断读取实体当前血量（IsAlive），必须与事件中的受击后血量一致
            target.CurrentHP = hpAfter;
            return new ScriptHookContext
            {
                Caster = target,
                Target = target,
                DamageEvent = new DamageResolvedEvent
                {
                    Target = target,
                    Source = DamageSourceInfo.CreateUnknown(),
                    HPBefore = hpBefore,
                    HPAfter = hpAfter,
                    ActualHPDamage = actualDamage,
                    HitLanded = hitLanded,
                    CausedDeath = hpBefore > 0f && hpAfter <= 0f
                }
            };
        }

        [Test]
        public void Hook_Below20Percent_ReturnsTrue()
        {
            var target = new GameObject("C4_T").AddComponent<BattleEntity>();
            try
            {
                target.TotalHP = 100f;
                var ctx = C4Context(target, 69f, 19f, 50f);
                Assert.That(ScriptHookEvaluator.Evaluate("Kaeya_C4", ctx), Is.True, "剩余19%应触发");
            }
            finally { Object.DestroyImmediate(target.gameObject); }
        }

        [Test]
        public void Hook_Exactly20Percent_ReturnsFalse()
        {
            var target = new GameObject("C4_T").AddComponent<BattleEntity>();
            try
            {
                target.TotalHP = 100f;
                var ctx = C4Context(target, 70f, 20f, 50f);
                Assert.That(ScriptHookEvaluator.Evaluate("Kaeya_C4", ctx), Is.False, "正好20%不触发");
            }
            finally { Object.DestroyImmediate(target.gameObject); }
        }

        [Test]
        public void Hook_Above20Percent_ReturnsFalse()
        {
            var target = new GameObject("C4_T").AddComponent<BattleEntity>();
            try
            {
                target.TotalHP = 100f;
                var ctx = C4Context(target, 71f, 21f, 50f);
                Assert.That(ScriptHookEvaluator.Evaluate("Kaeya_C4", ctx), Is.False, "剩余21%不触发");
            }
            finally { Object.DestroyImmediate(target.gameObject); }
        }

        [Test]
        public void Hook_TargetDead_ReturnsFalse()
        {
            var target = new GameObject("C4_T").AddComponent<BattleEntity>();
            try
            {
                target.TotalHP = 100f;
                var ctx = C4Context(target, 10f, 0f, 10f);
                Assert.That(ScriptHookEvaluator.Evaluate("Kaeya_C4", ctx), Is.False, "目标死亡不生成护盾");
            }
            finally { Object.DestroyImmediate(target.gameObject); }
        }

        [Test]
        public void Hook_ShieldAbsorbedAll_NoActualDamage_ReturnsFalse()
        {
            var target = new GameObject("C4_T").AddComponent<BattleEntity>();
            try
            {
                target.TotalHP = 100f;
                var ctx = C4Context(target, 10f, 10f, 0f);
                Assert.That(ScriptHookEvaluator.Evaluate("Kaeya_C4", ctx), Is.False, "护盾全吸收、HP未减少不触发");
            }
            finally { Object.DestroyImmediate(target.gameObject); }
        }

        [Test]
        public void Hook_AlreadyBelow20_AdditionalHit_ReturnsTrue()
        {
            var target = new GameObject("C4_T").AddComponent<BattleEntity>();
            try
            {
                target.TotalHP = 100f;
                var ctx = C4Context(target, 15f, 10f, 5f);
                Assert.That(ScriptHookEvaluator.Evaluate("Kaeya_C4", ctx), Is.True, "原本已低于20%再受实际扣血也可触发");
            }
            finally { Object.DestroyImmediate(target.gameObject); }
        }

        [Test]
        public void Hook_NotHitLanded_ReturnsFalse()
        {
            var target = new GameObject("C4_T").AddComponent<BattleEntity>();
            try
            {
                target.TotalHP = 100f;
                var ctx = C4Context(target, 69f, 19f, 50f, hitLanded: false);
                Assert.That(ScriptHookEvaluator.Evaluate("Kaeya_C4", ctx), Is.False);
            }
            finally { Object.DestroyImmediate(target.gameObject); }
        }

        // ---------- 统一 OnHit 集成（敌人伤害触发） ----------

        [Test]
        public void EnemyDamage_Below20_ShieldCreated()
        {
            _kaeya.Entity.TakeDamage(90f, DamageSourceInfo.Create(_enemy, ReactionSourceKind.EnemySkill, "SK_Hilichurl_Hit", "SE_Hilichurl_Hit"));

            Assert.That(_kaeya.Entity.CurrentHP, Is.EqualTo(10f));
            Assert.That(_kaeya.Entity.Shields.Count, Is.EqualTo(1), "敌人伤害也应能触发凯亚C4护盾");
            Assert.That(_kaeya.Entity.Shields[0].Value, Is.EqualTo(30f).Within(0.001f), "护盾量=0.3*最大生命");
            Assert.That(_kaeya.Entity.Shields[0].Element, Is.EqualTo("Cryo"));
        }

        [Test]
        public void Cooldown15_BlocksSecondTrigger()
        {
            _kaeya.Entity.TakeDamage(90f, DamageSourceInfo.Create(_enemy, ReactionSourceKind.EnemySkill, "SK_Test", "SE_Test"));
            Assert.That(_kaeya.Entity.Shields.Count, Is.EqualTo(1));

            // 回血后再受击：钩子条件满足但冷却未结束（同一回合 turn=1）
            _kaeya.Entity.Heal(100f);
            _kaeya.Entity.TakeDamage(90f, DamageSourceInfo.Create(_enemy, ReactionSourceKind.EnemySkill, "SK_Test", "SE_Test"));

            Assert.That(_kaeya.Entity.Shields.Count, Is.EqualTo(1), "15回合冷却内不能再次触发");
            Assert.That(_kaeyaC4.LifeTriggerCount, Is.EqualTo(1));
        }

        [Test]
        public void CooldownExpired_CanTriggerAgain()
        {
            _kaeya.Entity.TakeDamage(90f, DamageSourceInfo.Create(_enemy, ReactionSourceKind.EnemySkill, "SK_Test", "SE_Test"));
            Assert.That(_kaeya.Entity.Shields.Count, Is.EqualTo(1));

            // 冷却按每条状态行动独立保存；把 C4 行动的运行态推到20回合前。
            Assert.That(_kaeyaC4.ActionRuntime, Has.Count.EqualTo(1));
            foreach (StatusActionRuntimeState runtime in _kaeyaC4.ActionRuntime.Values)
                runtime.LastTriggerTurn = -20;
            _kaeya.Entity.Heal(100f);
            _kaeya.Entity.TakeDamage(90f, DamageSourceInfo.Create(_enemy, ReactionSourceKind.EnemySkill, "SK_Test", "SE_Test"));

            Assert.That(_kaeya.Entity.Shields.Count, Is.EqualTo(2), "冷却结束后可以再次触发");
            Assert.That(_kaeyaC4.LifeTriggerCount, Is.EqualTo(2));
        }

        [Test]
        public void OneHit_TriggersOnlyOnce()
        {
            _kaeya.Entity.TakeDamage(90f, DamageSourceInfo.Create(_enemy, ReactionSourceKind.EnemySkill, "SK_Test", "SE_Test"));

            // C4 配表只有 Cooldown=15（无 MaxTimePerLife），触发次数体现在护盾数量上
            Assert.That(_kaeya.Entity.Shields.Count, Is.EqualTo(1), "一次 hit 只能触发一次");
        }
    }
}
