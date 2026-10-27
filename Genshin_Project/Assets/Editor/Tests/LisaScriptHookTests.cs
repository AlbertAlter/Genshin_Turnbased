using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace GenshinTurnBased.Tests.EditMode
{
    public class LisaScriptHookTests
    {
        private HookTestDataManager data;
        private BattleManager battle;
        private BattleEntity lisa;
        private readonly List<GameObject> objects = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            data = HookTestEnv.CreateDataManager(false);
            data.SkillLevelDict["1011_2_ST_Lisa_Conductive"] = new Dictionary<int, SkillLevelData>
            {
                { 1, new SkillLevelData { Hits1 = "0.48" } },
                { 2, new SkillLevelData { Hits1 = "0.516" } }
            };
            data.TalentDict[1011] = new List<TalentData> { new TalentData { TalentIndex = 1, UnlockAfter = 20 } };
            data.StatusMainDict[LisaScriptHooks.ConductiveID] = new StatusMainData
            {
                StatusID = 69011, StatusID2 = LisaScriptHooks.ConductiveID, MaxStack = 3
            };
            battle = HookTestEnv.CreateBattleManager();
            lisa = Entity(1011, BattleSide.Ally, 1);
            data.CharacterAttributesDict[1011] = new CharacterAttributesData
            { CharacterID = 1011, BaseHP = 100f, BaseATK = 100f, BaseDEF = 100f,
                BaseRechargeRate = 1f, MaxEnergy = 80 };
            var controller = lisa.gameObject.AddComponent<CharacterBattleController>();
            controller.Init(1011, 20, 1); // 天赋资格来自真实突破阶段，不以裸Entity的等级冒充已突破。
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject obj in objects) UnityEngine.Object.DestroyImmediate(obj);
            objects.Clear();
            if (battle != null) UnityEngine.Object.DestroyImmediate(battle.gameObject);
            HookTestEnv.ClearBattleManagerSingleton();
            HookTestEnv.DestroyDataManager(data);
        }

        private BattleEntity Entity(int id, BattleSide side, int slot)
        {
            var obj = new GameObject("LisaHookTest_" + id);
            objects.Add(obj);
            var entity = obj.AddComponent<BattleEntity>();
            entity.EntityID = id;
            entity.Side = side;
            entity.SlotPosition = slot;
            entity.CurrentHP = entity.TotalHP = 100;
            if (side == BattleSide.Enemy)
            {
                var controller = obj.AddComponent<EnemyBattleController>();
                typeof(EnemyBattleController).GetProperty("Entity", BindingFlags.Public | BindingFlags.Instance)
                    .SetValue(controller, entity);
                battle.RegisterEnemy(controller);
            }
            return entity;
        }

        private void Conductive(BattleEntity target, int count)
        {
            if (count < 0 || count > 3) throw new ArgumentOutOfRangeException(nameof(count));
            target.RemoveStatus(LisaScriptHooks.ConductiveID, -1);
            // 零层就是没有该状态；正层数用正式入口施加多份独立实例。
            for (int index = 0; index < count; index++)
                target.AddStatus(LisaScriptHooks.ConductiveID, lisa, 0, 2, 0,
                    data.StatusMainDict[LisaScriptHooks.ConductiveID]);
        }

        [TestCase(0, 0f)]
        [TestCase(1, 0.48f)]
        [TestCase(2, 1.02f)]
        [TestCase(3, 1.56f)]
        public void DamageBonusUsesDocumentFormula(int stacks, float expected)
        {
            var target = Entity(2001, BattleSide.Enemy, 1);
            Conductive(target, stacks);
            Assert.That(target.GetStatusInstances(LisaScriptHooks.ConductiveID), Has.Count.EqualTo(stacks));
            Assert.That(target.HasStatus(LisaScriptHooks.ConductiveID), Is.EqualTo(stacks > 0));
            var original = new HitData(3.2f, 2f);
            var result = LisaScriptHooks.ModifyHit(LisaScriptHooks.DamageHook, lisa, target, original, 1);
            Assert.That(result.Multiplier, Is.EqualTo(3.2f + expected).Within(0.00001f));
            Assert.That(result.ElementAura, Is.EqualTo(2f));
            Assert.That(original.Multiplier, Is.EqualTo(3.2f));
        }

        [Test]
        public void LevelParameterIsReadAndMissingParameterFailsExplicitly()
        {
            var target = Entity(2001, BattleSide.Enemy, 1);
            Conductive(target, 2);
            var hit = LisaScriptHooks.ModifyHit(LisaScriptHooks.DamageHook, lisa, target, new HitData(), 2);
            Assert.That(hit.Multiplier, Is.EqualTo(0.516f * 2.125f).Within(0.00001f));
            data.SkillLevelDict.Clear();
            Assert.Throws<InvalidOperationException>(() => LisaScriptHooks.ModifyHit(LisaScriptHooks.DamageHook, lisa, target, new HitData(), 1));
        }

        [Test]
        public void TalentRemembersThreeStacksAfterRemovalButNotDeadOrReplacedTargets()
        {
            var target = Entity(2001, BattleSide.Enemy, 2);
            Conductive(target, 3);
            var context = new ScriptHookContext { Caster = lisa };
            using (LisaScriptHooks.BeginSkill(lisa))
            {
                LisaScriptHooks.ModifyHit(LisaScriptHooks.DamageHook, lisa, target, new HitData(), 1);
                target.StatusDict.Clear();
                Assert.IsTrue(ScriptHookEvaluator.Evaluate("1011_T1()", context));
                var resolved = BattleTargetResolver.Resolve(battle, lisa, lisa, "Enemy", 0, 0,
                    LisaScriptHooks.TargetHook, null, null, null);
                CollectionAssert.AreEqual(new[] { 2 }, resolved.Positions);
                lisa.Level = 19;
                Assert.IsFalse(ScriptHookEvaluator.Evaluate("1011_T1()", context));
                lisa.Level = 20;
                target.CurrentHP = 0;
                Assert.IsFalse(ScriptHookEvaluator.Evaluate("1011_T1()", context));
                target.CurrentHP = 100;
                var replacement = Entity(2002, BattleSide.Enemy, 3);
                var oldController = target.GetComponent<EnemyBattleController>();
                typeof(EnemyBattleController).GetProperty("Entity").SetValue(oldController, replacement);
                replacement.SlotPosition = 2;
                Assert.IsFalse(ScriptHookEvaluator.Evaluate("1011_T1()", context));
            }
            Assert.IsFalse(ScriptHookEvaluator.Evaluate("1011_T1()", context));
        }

        [Test]
        public void NestedSkillsAndExceptionDoNotLeakSnapshot()
        {
            var target = Entity(2001, BattleSide.Enemy, 1);
            Conductive(target, 3);
            using (LisaScriptHooks.BeginSkill(lisa))
            {
                LisaScriptHooks.ModifyHit(LisaScriptHooks.DamageHook, lisa, target, new HitData(), 1);
                Assert.Throws<InvalidOperationException>(() =>
                {
                    using (LisaScriptHooks.BeginSkill(lisa))
                    {
                        Assert.That(LisaScriptHooks.GetTripleTargetPositions(battle, lisa).Count, Is.EqualTo(0));
                        throw new InvalidOperationException("test unwind");
                    }
                });
                CollectionAssert.AreEqual(new[] { 1 }, LisaScriptHooks.GetTripleTargetPositions(battle, lisa));
            }
            Assert.That(LisaScriptHooks.GetTripleTargetPositions(battle, lisa).Count, Is.EqualTo(0));
        }

        [Test]
        public void UnsupportedHookOrOwnerIsRejected()
        {
            Assert.Throws<NotSupportedException>(() => LisaScriptHooks.ValidateDamageHook("Unknown()", lisa, "Damage"));
            Assert.Throws<NotSupportedException>(() => LisaScriptHooks.ValidateDamageHook(LisaScriptHooks.DamageHook, lisa, "Heal"));
            lisa.EntityID = 1009;
            Assert.Throws<NotSupportedException>(() => LisaScriptHooks.ValidateDamageHook(LisaScriptHooks.DamageHook, lisa, "Damage"));
        }

        [Test]
        public void TriggerTarget_IsScopedAndCannotMoveToAReplacementUnit()
        {
            var first = Entity(2001, BattleSide.Enemy, 1);
            var second = Entity(2002, BattleSide.Enemy, 2);
            var status = new StatusInstance { StatusID2 = "ST_Lisa_C2" };
            var parent = new ScriptHookContext
            {
                Caster = lisa, Status = status,
                DamageEvent = new DamageResolvedEvent { Target = first, HitLanded = true,
                    Source = DamageSourceInfo.Create(lisa, ReactionSourceKind.CharacterSkill, "SK_Test", "SE_Heavy_Lisa1") }
            };
            var child = parent.Snapshot();
            child.DamageEvent.Target = second;
            using (ScriptHookContext.EnterExecution(parent))
            {
                Assert.Throws<InvalidOperationException>(() =>
                {
                    using (ScriptHookContext.EnterExecution(child))
                    {
                        CollectionAssert.AreEqual(new[] { 2 }, LisaScriptHooks.GetTriggerTargetPositions(battle, lisa, "ST_Lisa_C2()"));
                        throw new InvalidOperationException("unwind");
                    }
                });
                CollectionAssert.AreEqual(new[] { 1 }, LisaScriptHooks.GetTriggerTargetPositions(battle, lisa, "ST_Lisa_C2()"));
                first.CurrentHP = 0;
                Assert.That(LisaScriptHooks.GetTriggerTargetPositions(battle, lisa, "ST_Lisa_C2()"), Is.Empty);
                first.CurrentHP = 100;
                typeof(EnemyBattleController).GetProperty("Entity").SetValue(first.GetComponent<EnemyBattleController>(), second);
                second.SlotPosition = 1;
                Assert.That(LisaScriptHooks.GetTriggerTargetPositions(battle, lisa, "ST_Lisa_C2()"), Is.Empty);
            }
            Assert.That(ScriptHookContext.Current, Is.Null);
        }
    }
}
