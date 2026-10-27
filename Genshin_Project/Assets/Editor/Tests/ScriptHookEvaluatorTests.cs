using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    /// <summary>
    /// ScriptHookEvaluator 单元测试（2026-08-19）：
    /// 原有 Check / HitLanded / 未知钩子 / 非法格式 不退化；新事件驱动识别与 Kill 普通路径拦截。
    /// </summary>
    [Category("Unit")]
    public class ScriptHookEvaluatorTests
    {
        private BattleFlowTestEnv _env;
        private BattleEntity _caster;

        [SetUp]
        public void SetUp()
        {
            // 只测试钩子条件；使用内存数据，不让无关角色表阻断这些单元测试。
            _env = new BattleFlowTestEnv();
            _env.CreateMinimalBattle();
            _caster = _env.Ally.Entity;
            _caster.EntityID = 1010;
            _caster.ConstellationLevel = 2;
            _caster.Level = 60;
        }

        [TearDown]
        public void TearDown()
        {
            try { _env?.Dispose(); }
            finally { _env = null; _caster = null; }
        }

        // ---------- 基础规则 ----------

        [Test]
        public void EmptyOrNullHook_ReturnsTrue()
        {
            Assert.That(ScriptHookEvaluator.Evaluate("", _caster), Is.True);
            Assert.That(ScriptHookEvaluator.Evaluate(null, _caster), Is.True);
        }

        [Test]
        public void UnknownHook_ReturnsFalse()
        {
            Assert.That(ScriptHookEvaluator.Evaluate("FooBar(1)", _caster), Is.False);
            Assert.That(ScriptHookEvaluator.Evaluate("FooBar", _caster), Is.False);
        }

        [Test]
        public void InvalidFormat_MissingRightParen_ReturnsFalse()
        {
            Assert.That(ScriptHookEvaluator.Evaluate("Check(1009_C2", _caster), Is.False);
        }

        [Test]
        public void RandomWeaponHook_EachEvaluationUsesOneIndependentBattleRoll()
        {
            var random = new SequenceBattleRandomSource(new[] { 0.49f, 0.51f });
            BattleRandom.SetSource(random);

            Assert.That(ScriptHookEvaluator.Evaluate("Hook(4300501)", _caster), Is.True);
            Assert.That(ScriptHookEvaluator.Evaluate("Hook(4300501)", _caster), Is.False);
            Assert.That(random.FloatCallCount, Is.EqualTo(2));
        }

        // ---------- Check ----------

        [Test]
        public void Check_ConstellationActive_ReturnsTrue()
        {
            Assert.That(ScriptHookEvaluator.Evaluate("Check(1010_C2)", _caster), Is.True);
        }

        [Test]
        public void Check_ConstellationNotActive_ReturnsFalse()
        {
            _caster.ConstellationLevel = 1;
            Assert.That(ScriptHookEvaluator.Evaluate("Check(1010_C2)", _caster), Is.False);
        }

        [Test]
        public void Check_WrongCaster_ReturnsFalse()
        {
            Assert.That(ScriptHookEvaluator.Evaluate("Check(1009_C2)", _caster), Is.False);
        }

        [Test]
        public void Check_BadFormat_ReturnsFalse()
        {
            Assert.That(ScriptHookEvaluator.Evaluate("Check(abc)", _caster), Is.False);
        }

        // ---------- HitLanded ----------

        [Test]
        public void HitLanded_MatchingHitContext_ReturnsTrue()
        {
            Assert.That(ScriptHookEvaluator.Evaluate("HitLanded(SE_Test)", _caster, "SE_Test"), Is.True);
        }

        [Test]
        public void HitLanded_NonMatchingHitContext_ReturnsFalse()
        {
            Assert.That(ScriptHookEvaluator.Evaluate("HitLanded(SE_Test)", _caster, "SE_Other"), Is.False);
        }

        [Test]
        public void HitLanded_EmptyHitContext_ReturnsFalse()
        {
            Assert.That(ScriptHookEvaluator.Evaluate("HitLanded(SE_Test)", _caster, ""), Is.False);
        }

        // ---------- Kaeya_T2（原有钩子不退化） ----------

        [Test]
        public void KaeyaT2_NoActionTargets_ReturnsFalse()
        {
            Assert.That(ScriptHookEvaluator.Evaluate("Kaeya_T2", _caster), Is.False);
        }

        [Test]
        public void KaeyaT2_FrozenTargetInActionPositions_ReturnsTrue()
        {
            var enemy = _env.Enemy.Entity;
            ReactionStateSystem.SetEntityState(enemy, ReactionType.Frozen, 1, null);
            _env.Manager.PendingActionTargetPositions.Add(enemy.SlotPosition);
            Assert.That(ScriptHookEvaluator.Evaluate("Kaeya_T2", _caster), Is.True, "本次行动目标处于冻结状态应触发");
        }

        [Test]
        public void KaeyaT2_NonFrozenTarget_ReturnsFalse()
        {
            _env.Manager.PendingActionTargetPositions.Add(_env.Enemy.Entity.SlotPosition);
            Assert.That(ScriptHookEvaluator.Evaluate("Kaeya_T2", _caster), Is.False);
        }

        // ---------- 事件驱动钩子 ----------

        [Test]
        public void IsEventDriven_PreAlliesDamageAndKill_True()
        {
            Assert.That(ScriptHookEvaluator.IsEventDriven("PreAlliesDamage"), Is.True);
            Assert.That(ScriptHookEvaluator.IsEventDriven("Kill(1010)"), Is.True);
            Assert.That(ScriptHookEvaluator.IsEventDriven("Kill(SK_Test)"), Is.True);
        }

        [Test]
        public void IsEventDriven_OrdinaryHooks_False()
        {
            Assert.That(ScriptHookEvaluator.IsEventDriven("Kaeya_C4"), Is.False);
            Assert.That(ScriptHookEvaluator.IsEventDriven("Check(1010_C2)"), Is.False);
            Assert.That(ScriptHookEvaluator.IsEventDriven(""), Is.False);
            Assert.That(ScriptHookEvaluator.IsEventDriven(null), Is.False);
        }

        [Test]
        public void Kill_PlainEvaluationPath_ReturnsFalse()
        {
            // Kill 行不得在普通阶段触发集合中执行：无死亡事件上下文时一律不触发
            Assert.That(ScriptHookEvaluator.Evaluate("Kill(1010)", _caster), Is.False);
        }

        [Test]
        public void Kill_ContextWithCausedDeathEvent_MatchingSource_ReturnsTrue()
        {
            var evt = new DamageResolvedEvent
            {
                Target = _caster,
                Source = DamageSourceInfo.Create(_caster, ReactionSourceKind.CharacterSkill, "SK_Test", "SE_Test"),
                HPBefore = 10f,
                HPAfter = 0f,
                ActualHPDamage = 10f,
                HitLanded = true,
                CausedDeath = true
            };
            var context = new ScriptHookContext { Caster = _caster, DamageEvent = evt, EventHookName = "Kill" };
            Assert.That(ScriptHookEvaluator.Evaluate("Kill(1010)", context), Is.True);
        }

        [Test]
        public void Kill_ContextWithoutDeathEvent_ReturnsFalse()
        {
            var context = new ScriptHookContext { Caster = _caster };
            Assert.That(ScriptHookEvaluator.Evaluate("Kill(1010)", context), Is.False);
        }
    }
}
