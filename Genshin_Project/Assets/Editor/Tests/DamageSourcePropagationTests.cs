using NUnit.Framework;
using UnityEngine;

namespace GenshinTurnBased.Tests.EditMode
{
    /// <summary>
    /// 伤害来源传递测试（2026-08-19，规格第 14 项 DamageSourcePropagationTests）：
    /// 覆盖 角色直伤 / 溅射 / 敌人伤害 / 状态伤害 / 反应派生伤害 / 燃烧持续伤害 六条路径。
    /// 每条路径用 Kill(...) 钩子验证来源（角色ID/技能ID/效果ID）真实流过统一扣血入口。
    /// </summary>
    public class DamageSourcePropagationTests
    {
        private GameObject _dmObject;
        private HookTestDataManager _dm;
        private GameObject _bmObject;
        private BattleManager _bm;
        private GameObject _kaeyaObject;
        private CharacterBattleController _kaeya;
        private GameObject _enemyObject;
        private EnemyBattleController _enemyCtrl;
        private BattleEntity _enemy;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _dmObject = new GameObject("DmgSrc_DM");
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

            _kaeyaObject = new GameObject("DmgSrc_Kaeya");
            _kaeya = _kaeyaObject.AddComponent<CharacterBattleController>();
            _kaeya.Init(1010, 80);
            _kaeya.Entity.SlotPosition = 1;
            _kaeya.Entity.Side = BattleSide.Ally;
            _kaeya.Entity.TotalHP = 100f;
            _kaeya.Entity.CurrentHP = 100f;
            _bm.RegisterAlly(_kaeya);
            if (_bm.APManager != null) _bm.APManager.ResetAP();

            _enemyObject = new GameObject("DmgSrc_Enemy");
            _enemyCtrl = _enemyObject.AddComponent<EnemyBattleController>();
            _enemyCtrl.InitEnemy(20000, 1);
            _enemy = _enemyCtrl.Entity;
            _enemy.SlotPosition = 1;
            _enemy.Side = BattleSide.Enemy;
            _bm.RegisterEnemy(_enemyCtrl);
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

        /// <summary>注册一个 Kill(参数) 自定义状态并返回实例。</summary>
        private StatusInstance RegisterKillStatus(string statusID2, string killArg, string effectID2)
        {
            var inst = HookTestEnv.AttachStatus(_kaeya.Entity, statusID2, _kaeya.Entity, 3);
            HookTestEnv.InjectStatusEffect(_dm, effectID2, "GainEnergy", "5", "Flat", "None", "Self");
            HookTestEnv.InjectStatusAction(_dm, statusID2, "OnTrigger", effectID2, killArg);
            KillHookSystem.Register(inst, _kaeya.Entity);
            return inst;
        }

        private void ResetEnemy(float hp)
        {
            _enemy.TotalHP = hp;
            _enemy.CurrentHP = hp;
            _enemy.ElementalAuras.Clear();
            _enemy.Shields.Clear();
        }

        // ---------- 1. 角色直伤 ----------

        [Test]
        public void CharacterDirectDamage_CarriesSkillSource()
        {
            ResetEnemy(1f);
            var killStatus = RegisterKillStatus("ST_SRC_DIRECT", "Kill(SK_Normal_Kaeya)", "STE_SRC_DIRECT");

            _kaeya.ForcedTargetPositions.Add(1);
            _kaeya.ExecuteNormalAttack();

            Assert.That(_enemy.IsDead, Is.True, "普攻应击杀1血敌人");
            Assert.That(killStatus.LifeTriggerCount, Is.EqualTo(1), "角色直伤必须携带技能ID来源");
        }

        // ---------- 2. 溅射 ----------

        [Test]
        public void SplashDamage_CarriesSkillSource_AndHitsAdjacent()
        {
            // 1号位高血量主目标，2号位低血量溅射目标
            _enemy.TotalHP = 10000f;
            _enemy.CurrentHP = 10000f;
            var splashEnemy = HookTestEnv.CreateEnemy(20001, 1, 2, _bm, hp: 1f);
            var killStatus = RegisterKillStatus("ST_SRC_SPLASH", "Kill(SK_Heavy_Kaeya)", "STE_SRC_SPLASH");

            _kaeya.ForcedTargetPositions.Add(1);
            _kaeya.ExecuteHeavyAttack();

            Assert.That(_enemy.CurrentHP, Is.LessThan(10000f), "主目标应受到重击伤害");
            Assert.That(splashEnemy.IsDead, Is.True, "溅射应命中相邻目标并携带同一技能来源");
            Assert.That(killStatus.LifeTriggerCount, Is.EqualTo(1), "溅射伤害必须携带技能ID来源");
        }

        // ---------- 3. 敌人伤害（统一 OnHit + HitSource 匹配） ----------

        [Test]
        public void EnemyDamage_FlowsThroughUnifiedOnHit()
        {
            // 自定义 OnHit 状态：HitSource=SE_Test（测试木桩唯一伤害效果）
            var onHit = HookTestEnv.AttachStatus(_kaeya.Entity, "ST_SRC_ONHIT", _kaeya.Entity, 3);
            HookTestEnv.InjectStatusEffect(_dm, "STE_SRC_ONHIT", "GainEnergy", "5", "Flat", "None", "Self");
            HookTestEnv.InjectStatusAction(_dm, "ST_SRC_ONHIT", "OnHit", "STE_SRC_ONHIT", "", hitSource: "SE_Test");

            _enemyCtrl.TakeEnemyTurn();

            Assert.That(_kaeya.Entity.CurrentHP, Is.LessThan(100f), "敌人应造成伤害");
            Assert.That(_kaeya.Entity.CurrentEnergy, Is.EqualTo(5), "敌人伤害必须走统一 OnHit 入口并携带效果ID来源");
        }

        // ---------- 4. 状态伤害 ----------

        [Test]
        public void StatusDamage_CarriesStatusEffectSource()
        {
            ResetEnemy(5f);
            var killStatus = RegisterKillStatus("ST_SRC_STATUS", "Kill(STE_SRC_STAT_DMG)", "STE_SRC_STAT_DMG_ENERGY");

            // 状态伤害效果（Damage，等级数据沿用凯亚战技）
            HookTestEnv.InjectStatusEffect(_dm, "STE_SRC_STAT_DMG", "Damage", "", "", "Pyro", "Enemy", 1);
            var triggerStatus = HookTestEnv.AttachStatus(_kaeya.Entity, "ST_SRC_TRIGGER", _kaeya.Entity, 3);
            var actionLine = HookTestEnv.InjectStatusAction(_dm, "ST_SRC_TRIGGER", "OnTrigger", "STE_SRC_STAT_DMG", "");

            bool executed = _kaeya.TryExecuteEventStatusAction(
                triggerStatus, actionLine, _kaeya.Entity,
                new ScriptHookContext { Caster = _kaeya.Entity });

            Assert.That(executed, Is.True);
            Assert.That(_enemy.IsDead, Is.True, "状态伤害应击杀低血量敌人");
            Assert.That(killStatus.LifeTriggerCount, Is.EqualTo(1), "状态伤害必须携带状态效果ID来源");
        }

        // ---------- 5. 反应派生伤害 ----------

        [Test]
        public void ReactionDerivedDamage_CarriesOriginalEffectSource()
        {
            ResetEnemy(10f);
            _enemy.ApplyAura("Electro", 2f, 1010);
            var killStatus = RegisterKillStatus("ST_SRC_REACTION", "Kill(SE_Test)", "STE_SRC_REACTION");

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
            Assert.That(killStatus.LifeTriggerCount, Is.EqualTo(1), "反应派生伤害必须保留原始效果ID来源");
        }

        // ---------- 6. 燃烧持续伤害 ----------

        [Test]
        public void BurningDoT_CarriesCreationSnapshotSource()
        {
            ResetEnemy(5f);
            var killStatus = RegisterKillStatus("ST_SRC_BURNING", "Kill(SE_FireSeed)", "STE_SRC_BURNING");

            // 创建燃烧状态：来源=凯亚 + SE_FireSeed
            var context = new ReactionContext
            {
                SourceEntity = _kaeya.Entity,
                Target = _enemy,
                SourceKind = ReactionSourceKind.CharacterSkill,
                SourceSkillID = "SK_Fire",
                SourceEffectID = "SE_FireSeed",
                EffectExecutionID = ReactionResolver.BeginEffectExecution(),
                ApplicationPhase = (int)TurnPhase.EnemyPostTurn,
                AttackElement = "Pyro",
                AttackAmount = 2f,
                PreReactionDamage = 1f,
                DamageType = "Normal",
                PoiseDamage = 0f
            };
            ReactionSourceSnapshot snapshot = ReactionSourceSnapshot.Capture(context, ReactionType.Burning, "Pyro");
            ReactionStateSystem.SetEntityState(
                _enemy, ReactionType.Burning, 3, snapshot,
                (int)TurnPhase.EnemyPostTurn, true, 2f);

            BurningReactionHandler.TickTurnEnd(TurnPhase.EnemyPostTurn);

            Assert.That(_enemy.IsDead, Is.True, "燃烧持续伤害应击杀低血量敌人");
            Assert.That(killStatus.LifeTriggerCount, Is.EqualTo(1), "燃烧持续伤害必须使用创建时保存的来源快照");
        }
    }
}
