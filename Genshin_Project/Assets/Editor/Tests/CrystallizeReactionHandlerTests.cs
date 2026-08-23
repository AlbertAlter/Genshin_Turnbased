using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GenshinTurnBased.Tests.EditMode
{
    public class CrystallizeReactionHandlerTests
    {
        [TestCase("Pyro")]
        [TestCase("Hydro")]
        [TestCase("Electro")]
        [TestCase("Cryo")]
        public void Resolve_FourElements_CreateExpectedPartyShieldWithoutChangingDamage(
            string auraElement)
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                env.Enemy.Entity.ApplyAura(auraElement, 1f, env.Ally.Entity.EntityID);

                ReactionResult result = ReactionResolver.Resolve(
                    NewContext(env.Ally.Entity, env.Enemy.Entity, 2f));

                Assert.That(result.HasReaction, Is.True);
                Assert.That(result.TriggeredReactions, Has.Count.EqualTo(1));
                Assert.That(result.TriggeredReactions[0].Type, Is.EqualTo(ReactionType.Crystallize));
                Assert.That(result.TriggeredReactions[0].DisplayName, Is.EqualTo("结晶"));
                Assert.That(result.FinalDamage, Is.EqualTo(123f));
                Assert.That(result.RemainingAttackAmount, Is.Zero);
                Assert.That(result.DerivedHits, Is.Empty);
                Assert.That(env.Enemy.Entity.GetAura(auraElement), Is.Null);
                Assert.That(env.Enemy.Entity.GetAura("Geo"), Is.Null);

                Shield shield = GetCrystallizeShield(env.Ally.Entity);
                Assert.That(shield, Is.Not.Null);
                Assert.That(shield.Element, Is.EqualTo(auraElement));
                Assert.That(shield.Value, Is.EqualTo(77.4f).Within(0.001f));
                Assert.That(shield.Duration, Is.EqualTo(2f));
                Assert.That(shield.OriginPhase, Is.EqualTo((int)TurnPhase.AllyAction));
            }
        }

        [TestCase(0.25f, 0f)]
        [TestCase(1f, 0f)]
        [TestCase(2f, 1f)]
        public void Resolve_GeoIsFullyConsumedAndHydroUsesOneToTwoRatio(
            float hydroAmount,
            float expectedHydro)
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                env.Enemy.Entity.ApplyAura("Hydro", hydroAmount, env.Ally.Entity.EntityID);

                ReactionResult result = ReactionResolver.Resolve(
                    NewContext(env.Ally.Entity, env.Enemy.Entity, 2f));

                Assert.That(result.RemainingAttackAmount, Is.Zero);
                ElementalAura hydro = env.Enemy.Entity.GetAura("Hydro");
                if (expectedHydro <= 0f) Assert.That(hydro, Is.Null);
                else Assert.That(hydro.AuraAmount, Is.EqualTo(expectedHydro).Within(0.001f));
            }
        }

        [Test]
        public void Resolve_MultipleAuras_UsesPyroHydroElectroCryoPriorityOnce()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                foreach (string element in new[] { "Cryo", "Electro", "Hydro", "Pyro" })
                    env.Enemy.Entity.ApplyAura(element, 1f, env.Ally.Entity.EntityID);

                ReactionResult result = ReactionResolver.Resolve(
                    NewContext(env.Ally.Entity, env.Enemy.Entity, 2f));

                Assert.That(result.TriggeredReactions, Has.Count.EqualTo(1));
                Assert.That(env.Enemy.Entity.GetAura("Pyro"), Is.Null);
                Assert.That(env.Enemy.Entity.GetAura("Hydro"), Is.Not.Null);
                Assert.That(env.Enemy.Entity.GetAura("Electro"), Is.Not.Null);
                Assert.That(env.Enemy.Entity.GetAura("Cryo"), Is.Not.Null);
                Assert.That(GetCrystallizeShield(env.Ally.Entity).Element, Is.EqualTo("Pyro"));
            }
        }

        [Test]
        public void Resolve_PyroConsumesNormalAuraBeforeBurningVirtualAura()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                env.Enemy.Entity.ApplyAura("Pyro", 0.25f, env.Ally.Entity.EntityID);
                ReactionStateSystem.SetEntityState(
                    env.Enemy.Entity,
                    ReactionType.Burning,
                    1,
                    ReactionSourceSnapshot.Capture(
                        NewContext(env.Ally.Entity, env.Enemy.Entity, 1f)),
                    (int)TurnPhase.AllyAction,
                    true,
                    0.75f);

                ReactionResolver.Resolve(NewContext(env.Ally.Entity, env.Enemy.Entity, 2f));

                Assert.That(env.Enemy.Entity.GetAura("Pyro"), Is.Null);
                Assert.That(BurningReactionHandler.IsBurning(env.Enemy.Entity), Is.False);
                Assert.That(GetCrystallizeShield(env.Ally.Entity).Element, Is.EqualTo("Pyro"));
            }
        }

        [Test]
        public void Resolve_FrozenVirtualCryo_RespectsIgnoreFrozenAura()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                ReactionStateSystem.SetEntityState(
                    env.Enemy.Entity,
                    ReactionType.Frozen,
                    1,
                    ReactionSourceSnapshot.Capture(
                        NewContext(env.Ally.Entity, env.Enemy.Entity, 1f)),
                    (int)TurnPhase.AllyAction,
                    true,
                    1f);

                ReactionContext ignored = NewContext(env.Ally.Entity, env.Enemy.Entity, 2f);
                ignored.IgnoreFrozenAura = true;
                ReactionResult ignoredResult = ReactionResolver.Resolve(ignored);

                Assert.That(ignoredResult.HasReaction, Is.False);
                Assert.That(FrozenReactionHandler.IsFrozen(env.Enemy.Entity), Is.True);
                Assert.That(env.Enemy.Entity.GetAura("Geo"), Is.Null);

                ReactionResult result = ReactionResolver.Resolve(
                    NewContext(env.Ally.Entity, env.Enemy.Entity, 2f));
                Assert.That(result.HasReaction, Is.True);
                Assert.That(FrozenReactionHandler.IsFrozen(env.Enemy.Entity), Is.False);
                Assert.That(GetCrystallizeShield(env.Ally.Entity).Element, Is.EqualTo("Cryo"));
            }
        }

        [Test]
        public void Resolve_SourceSnapshotOverride_DeterminesShieldValue()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                env.Enemy.Entity.ApplyAura("Hydro", 1f, env.Ally.Entity.EntityID);
                ReactionContext context = NewContext(null, env.Enemy.Entity, 2f);
                context.SourceSnapshotOverride = new ReactionSourceSnapshot
                {
                    SourceEntityID = 777,
                    SourceSide = BattleSide.Ally,
                    SourceKind = ReactionSourceKind.StatusEffect,
                    Level = 90,
                    LevelCoefficient = 100f,
                    TotalEM = 200f
                };

                ReactionResult result = ReactionResolver.Resolve(context);

                float expected = ReactionDamageCalculator.CalculateCharacterCrystalShield(100f, 200f);
                Assert.That(result.HasReaction, Is.True);
                Assert.That(GetCrystallizeShield(env.Ally.Entity).Value,
                    Is.EqualTo(expected).Within(0.001f));
            }
        }

        [Test]
        public void Resolve_GrantsSameShieldToLivingPartyAndSkipsDeadMember()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                CharacterBattleController background = env.CreateAlly(2);
                CharacterBattleController dead = env.CreateAlly(3);
                dead.Entity.CurrentHP = 0f;
                env.Enemy.Entity.ApplyAura("Electro", 1f, env.Ally.Entity.EntityID);

                ReactionResolver.Resolve(NewContext(env.Ally.Entity, env.Enemy.Entity, 2f));

                Assert.That(GetCrystallizeShield(env.Ally.Entity).Value,
                    Is.EqualTo(GetCrystallizeShield(background.Entity).Value).Within(0.001f));
                Assert.That(GetCrystallizeShield(dead.Entity), Is.Null);
            }
        }

        [Test]
        public void Resolve_EnemyGeoNeutralizesWithoutOccurrenceOrShield()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                env.Ally.Entity.ApplyAura("Hydro", 1f, env.Enemy.Entity.EntityID);

                ReactionResult result = ReactionResolver.Resolve(NewContext(
                    env.Enemy.Entity,
                    env.Ally.Entity,
                    2f,
                    ReactionSourceKind.EnemySkill));

                Assert.That(result.HasReaction, Is.False);
                Assert.That(result.FinalDamage, Is.EqualTo(123f));
                Assert.That(result.RemainingAttackAmount, Is.Zero);
                Assert.That(env.Ally.Entity.GetAura("Hydro"), Is.Null);
                Assert.That(env.Ally.Entity.Shields, Is.Empty);
            }
        }

        [Test]
        public void Resolve_DisabledReactionOrMissingAura_DoesNotConsumeAndNeverAppliesGeo()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                env.Enemy.Entity.ApplyAura("Hydro", 1f, env.Ally.Entity.EntityID);
                ReactionContext disabled = NewContext(env.Ally.Entity, env.Enemy.Entity, 2f);
                disabled.CanTriggerReaction = false;

                ReactionResult disabledResult = ReactionResolver.Resolve(disabled);
                Assert.That(disabledResult.HasReaction, Is.False);
                Assert.That(env.Enemy.Entity.GetAura("Hydro").AuraAmount, Is.EqualTo(1f));
                Assert.That(env.Enemy.Entity.GetAura("Geo"), Is.Null);

                env.Enemy.Entity.RemoveAura("Hydro");
                ReactionResult noAura = ReactionResolver.Resolve(
                    NewContext(env.Ally.Entity, env.Enemy.Entity, 2f));
                Assert.That(noAura.HasReaction, Is.False);
                Assert.That(env.Enemy.Entity.GetAura("Geo"), Is.Null);
            }
        }

        [Test]
        public void Resolve_CanApplyAuraFalse_DoesNotDisableReaction()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                env.Enemy.Entity.ApplyAura("Hydro", 1f, env.Ally.Entity.EntityID);
                ReactionContext context = NewContext(env.Ally.Entity, env.Enemy.Entity, 2f);
                context.CanApplyAura = false;

                ReactionResult result = ReactionResolver.Resolve(context);

                Assert.That(result.HasReaction, Is.True);
                Assert.That(env.Enemy.Entity.GetAura("Hydro"), Is.Null);
                Assert.That(GetCrystallizeShield(env.Ally.Entity), Is.Not.Null);
            }
        }

        [Test]
        public void Resolve_ZeroGeoOrDeadTarget_DoesNotConsumeAura()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                env.Enemy.Entity.ApplyAura("Hydro", 1f, env.Ally.Entity.EntityID);

                ReactionResult zero = ReactionResolver.Resolve(
                    NewContext(env.Ally.Entity, env.Enemy.Entity, 0f));
                Assert.That(zero.HasReaction, Is.False);
                Assert.That(env.Enemy.Entity.GetAura("Hydro").AuraAmount, Is.EqualTo(1f));

                env.Enemy.Entity.CurrentHP = 0f;
                ReactionResult dead = ReactionResolver.Resolve(
                    NewContext(env.Ally.Entity, env.Enemy.Entity, 2f));
                Assert.That(dead.HasReaction, Is.False);
                Assert.That(env.Enemy.Entity.GetAura("Hydro").AuraAmount, Is.EqualTo(1f));
                Assert.That(env.Enemy.Entity.GetAura("Geo"), Is.Null);
            }
        }

        [Test]
        public void Resolve_ZeroLevelCoefficient_StillCompletesWithoutAddingInvalidShield()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                env.DataManager.ReactionLevelCoefficientDict[1] = 0f;
                env.Enemy.Entity.ApplyAura("Hydro", 1f, env.Ally.Entity.EntityID);

                ReactionResult result = ReactionResolver.Resolve(
                    NewContext(env.Ally.Entity, env.Enemy.Entity, 2f));

                Assert.That(result.HasReaction, Is.True);
                Assert.That(result.RemainingAttackAmount, Is.Zero);
                Assert.That(env.Enemy.Entity.GetAura("Hydro"), Is.Null);
                Assert.That(GetCrystallizeShield(env.Ally.Entity), Is.Null);
            }
        }

        [Test]
        public void Resolve_WithoutBattleManager_StillConsumesAndReportsReaction()
        {
            HookTestEnv.SetBattleManagerSingleton(null);
            var sourceObject = new GameObject("Crystallize_NoManager_Source");
            var targetObject = new GameObject("Crystallize_NoManager_Target");
            try
            {
                BattleEntity source = sourceObject.AddComponent<BattleEntity>();
                source.Type = BattleEntity.EntityType.Character;
                source.Side = BattleSide.Ally;
                source.Level = 1;
                BattleEntity target = targetObject.AddComponent<BattleEntity>();
                target.Type = BattleEntity.EntityType.Enemy;
                target.Side = BattleSide.Enemy;
                target.TotalHP = target.CurrentHP = 1000f;
                target.ApplyAura("Hydro", 1f, source.EntityID);
                ReactionContext context = NewContext(source, target, 2f);
                context.SourceSnapshotOverride = new ReactionSourceSnapshot
                {
                    SourceEntity = source,
                    SourceEntityID = 1,
                    SourceSide = BattleSide.Ally,
                    SourceKind = ReactionSourceKind.CharacterSkill,
                    LevelCoefficient = 17.2f
                };
                LogAssert.Expect(
                    LogType.Warning,
                    "[反应] 结晶反应已完成，但当前没有 BattleManager，无法为队伍施加结晶盾。");

                ReactionResult result = ReactionResolver.Resolve(context);

                Assert.That(result.HasReaction, Is.True);
                Assert.That(target.GetAura("Hydro"), Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(targetObject);
                Object.DestroyImmediate(sourceObject);
                ReactionResolver.ResetSession();
            }
        }

        private static ReactionContext NewContext(
            BattleEntity source,
            BattleEntity target,
            float geoAmount,
            ReactionSourceKind sourceKind = ReactionSourceKind.CharacterSkill)
        {
            return new ReactionContext
            {
                SourceEntity = source,
                Target = target,
                SourceKind = sourceKind,
                SourceSkillID = "SK_Crystallize_Test",
                SourceEffectID = "SE_Crystallize_Test",
                EffectExecutionID = ReactionResolver.BeginEffectExecution(),
                ApplicationPhase = (int)TurnPhase.AllyAction,
                AttackElement = "Geo",
                AttackAmount = geoAmount,
                PreReactionDamage = 123f,
                DamageType = "Skill",
                PoiseDamage = 20f
            };
        }

        private static Shield GetCrystallizeShield(BattleEntity entity)
        {
            return entity.Shields.Find(shield =>
                shield != null && shield.Kind == ShieldKind.Crystallize);
        }
    }
}
