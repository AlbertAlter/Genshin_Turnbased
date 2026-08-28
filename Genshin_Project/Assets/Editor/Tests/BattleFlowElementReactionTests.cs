using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    public class BattleFlowElementReactionTests
    {
        [Test]
        [Timeout(5000)]
        public void VaporizeAndMelt_UseDirectionalMultiplierAndExactAuraConsumption()
        {
            object[][] cases =
            {
                new object[] { "Hydro", 1f, "Pyro", 1f, 875f, "Hydro", 0.5f },
                new object[] { "Pyro", 1f, "Hydro", 1f, 850f, "Hydro", 0.5f },
                new object[] { "Pyro", 1f, "Cryo", 1f, 875f, "Pyro", 0.5f },
                new object[] { "Cryo", 1f, "Pyro", 1f, 850f, "Pyro", 0.5f }
            };
            foreach (object[] data in cases)
            {
                using (var env = new BattleFlowTestEnv())
                {
                    env.ConfigureAllyAction(0, "SK_Normal_Flow", "SE_Normal_Flow", (string)data[0],
                        $"1*TotalATK,0,{data[1]}", 10);
                    env.ConfigureAllyAction(2, "SK_Skill_Flow", "SE_Skill_Flow", (string)data[2],
                        $"1*TotalATK,0,{data[3]}", 10);
                    env.CreateMinimalBattle();
                    env.Manager.StartBattle();
                    env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                    Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True);
                    Assert.That(env.Manager.UseSkillBySlot(0), Is.True);
                    Assert.That(env.Enemy.Entity.CurrentHP, Is.EqualTo((float)data[4]).Within(0.01f));
                    Assert.That(env.Enemy.Entity.GetAura((string)data[5]).AuraAmount,
                        Is.EqualTo((float)data[6]).Within(0.01f));
                }
            }
        }

        [Test]
        [Timeout(5000)]
        public void Overloaded_DealsDerivedDamageToMainAndAdjacentAndConsumesBothAuras()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.ConfigureAllyAction(0, "SK_Normal_Flow", "SE_Normal_Flow", "Electro", "1*TotalATK,0,1", 10);
                env.ConfigureAllyAction(2, "SK_Skill_Flow", "SE_Skill_Flow", "Pyro", "1*TotalATK,0,1", 10);
                env.CreateMinimalBattle();
                env.Enemy.Entity.TotalHP = env.Enemy.Entity.CurrentHP = 1100f;
                EnemyBattleController adjacent = env.CreateEnemy(2);
                env.Manager.StartBattle();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                env.Manager.UseNormalAttackBySlot(0);
                env.Manager.UseSkillBySlot(0);

                // 主目标：两次直伤 50+50，加超载 17.2*4=68.8；相邻只受 68.8。
                Assert.That(env.Enemy.Entity.CurrentHP, Is.EqualTo(931.2f).Within(0.01f));
                Assert.That(adjacent.Entity.CurrentHP, Is.EqualTo(931.2f).Within(0.01f));
                Assert.That(env.Enemy.Entity.GetAura("Electro"), Is.Null);
                Assert.That(env.Enemy.Entity.GetAura("Pyro"), Is.Null);
            }
        }

        [Test]
        [Timeout(5000)]
        public void FreezeThenShatter_UsesRealElementSkillsAndPoiseCondition()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.ConfigureAllyAction(0, "SK_Normal_Flow", "SE_Normal_Flow", "Cryo", "1*TotalATK,0,1", 10);
                env.ConfigureAllyAction(2, "SK_Skill_Flow", "SE_Skill_Flow", "Hydro", "1*TotalATK,0,1", 10);
                env.ConfigureAllyAction(1, "SK_Heavy_Flow", "SE_Heavy_Flow", "None", "1*TotalATK,100,0", 10);
                env.CreateMinimalBattle();
                env.Manager.StartBattle();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                env.Manager.UseNormalAttackBySlot(0);
                env.Manager.UseSkillBySlot(0);

                Assert.That(FrozenReactionHandler.IsFrozen(env.Enemy.Entity), Is.True);
                Assert.That(FrozenReactionHandler.GetFrozenAuraAsCryo(env.Enemy.Entity), Is.EqualTo(1f).Within(0.01f));

                env.Manager.UseHeavyAttackBySlot(0);
                Assert.That(FrozenReactionHandler.IsFrozen(env.Enemy.Entity), Is.False);
                Assert.That(env.Enemy.Entity.Poise, Is.Zero.Within(0.01f));
                // 碎冰：17.2*3=51.6；三次直伤各50。
                Assert.That(env.Enemy.Entity.CurrentHP, Is.EqualTo(798.4f).Within(0.01f));
            }
        }

        [Test]
        [Timeout(5000)]
        public void BloomThenHyperbloom_CreatesConsumesCoreAndFiresFiveMissiles()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.ConfigureAllyAction(0, "SK_Normal_Flow", "SE_Normal_Flow", "Dendro", "1*TotalATK,0,1", 10);
                env.ConfigureAllyAction(2, "SK_Skill_Flow", "SE_Skill_Flow", "Hydro", "1*TotalATK,0,2", 10);
                env.ConfigureAllyAction(1, "SK_Heavy_Flow", "SE_Heavy_Flow", "Electro", "1*TotalATK,0,1", 10);
                env.CreateMinimalBattle();
                env.Manager.StartBattle();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                env.Manager.UseNormalAttackBySlot(0);
                env.Manager.UseSkillBySlot(0);

                Assert.That(BloomCoreSystem.ActiveCores.Count, Is.EqualTo(1));
                Assert.That(env.Enemy.Entity.GetAura("Dendro"), Is.Null);
                Assert.That(env.Enemy.Entity.GetAura("Hydro"), Is.Null);
                float beforeHyperbloom = env.Enemy.Entity.CurrentHP;

                env.Manager.UseHeavyAttackBySlot(0);

                Assert.That(BloomCoreSystem.ActiveCores, Is.Empty);
                // 5枚飞弹，每枚 17.2*3=51.6；另有一次50直伤。
                Assert.That(beforeHyperbloom - env.Enemy.Entity.CurrentHP,
                    Is.EqualTo(308f).Within(0.01f));
            }
        }

        [Test]
        [Timeout(5000)]
        public void ElectroCharged_CreatesCoexistingAurasAndRuntimeStateFromSkills()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.ConfigureAllyAction(0, "SK_Normal_Flow", "SE_Normal_Flow", "Hydro", "1*TotalATK,0,2", 10);
                env.ConfigureAllyAction(2, "SK_Skill_Flow", "SE_Skill_Flow", "Electro", "1*TotalATK,0,2", 10);
                env.CreateMinimalBattle();
                env.Manager.StartBattle();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                env.Manager.UseNormalAttackBySlot(0);
                env.Manager.UseSkillBySlot(0);
                Assert.That(ReactionStateSystem.TryGetEntityState(
                    env.Enemy.Entity, ReactionType.ElectroCharged, out ReactionStateInstance state), Is.True);
                Assert.That(state.ReactionElementAmount, Is.EqualTo(1f).Within(0.01f));
                Assert.That(env.Enemy.Entity.GetAura("Hydro").AuraAmount, Is.EqualTo(1f).Within(0.01f));
                Assert.That(env.Enemy.Entity.GetAura("Electro").AuraAmount, Is.EqualTo(1f).Within(0.01f));
            }
        }

        [Test]
        [Timeout(5000)]
        public void Superconduct_DealsAreaDamageAndCreatesThreeRoundPhysicalResistanceDebuff()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.ConfigureAllyAction(0, "SK_Normal_Flow", "SE_Normal_Flow", "Cryo", "1*TotalATK,0,1", 10);
                env.ConfigureAllyAction(2, "SK_Skill_Flow", "SE_Skill_Flow", "Electro", "1*TotalATK,0,1", 10);
                env.CreateMinimalBattle();
                env.Manager.StartBattle();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                env.Manager.UseNormalAttackBySlot(0);
                env.Manager.UseSkillBySlot(0);

                Assert.That(env.Enemy.Entity.CurrentHP, Is.EqualTo(882.8f).Within(0.01f));
                Assert.That(ReactionStateSystem.TryGetEntityState(
                    env.Enemy.Entity, ReactionType.Superconduct, out ReactionStateInstance state), Is.True);
                Assert.That(state.RemainingRounds, Is.EqualTo(3));
                Assert.That(env.Enemy.Entity.GetStatus("ST_SuperConduct"), Is.Not.Null);
            }
        }

        [Test]
        [Timeout(6000)]
        public void Burning_TicksAtTurnEndFromSourceSnapshotAndAppliesPyroAura()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.ConfigureAllyAction(0, "SK_Normal_Flow", "SE_Normal_Flow", "Dendro", "1*TotalATK,0,1", 10);
                env.ConfigureAllyAction(2, "SK_Skill_Flow", "SE_Skill_Flow", "Pyro", "1*TotalATK,0,1", 10);
                env.CreateMinimalBattle();
                env.Manager.StartBattle();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                env.Manager.UseNormalAttackBySlot(0);
                env.Manager.UseSkillBySlot(0);
                Assert.That(BurningReactionHandler.IsBurning(env.Enemy.Entity), Is.True);

                env.Manager.EndAllyTurn();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.EnemyPreTurn);

                // 两次直伤100，加燃烧 17.2*2=34.4。
                Assert.That(env.Enemy.Entity.CurrentHP, Is.EqualTo(865.6f).Within(0.01f));
                Assert.That(env.Enemy.Entity.GetAura("Pyro").AuraAmount, Is.EqualTo(1f).Within(0.01f));
            }
        }

        [Test]
        [Timeout(5000)]
        public void QuickenThenAggravate_UsesDurationAndAdditiveDamageFormula()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.ConfigureAllyAction(0, "SK_Normal_Flow", "SE_Normal_Flow", "Dendro", "1*TotalATK,0,1", 10);
                env.ConfigureAllyAction(2, "SK_Skill_Flow", "SE_Skill_Flow", "Electro", "1*TotalATK,0,1", 10);
                env.ConfigureAllyAction(1, "SK_Heavy_Flow", "SE_Heavy_Flow", "Electro", "1*TotalATK,0,1", 10);
                env.CreateMinimalBattle();
                env.Manager.StartBattle();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                env.Manager.UseNormalAttackBySlot(0);
                env.Manager.UseSkillBySlot(0);
                Assert.That(ReactionStateSystem.TryGetEntityState(
                    env.Enemy.Entity, ReactionType.Quicken, out ReactionStateInstance state), Is.True);
                Assert.That(state.RemainingRounds, Is.EqualTo(1));

                env.Manager.UseHeavyAttackBySlot(0);
                // 超激化：(100 + 1.15*17.2) * 0.5 = 59.89；此前两次直伤共100。
                Assert.That(env.Enemy.Entity.CurrentHP, Is.EqualTo(840.11f).Within(0.01f));
            }
        }

        [Test]
        [Timeout(5000)]
        public void BloomThenBurgeon_ConsumesCoreAndDamagesCenterAndAdjacentTargets()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.ConfigureAllyAction(0, "SK_Normal_Flow", "SE_Normal_Flow", "Dendro", "1*TotalATK,0,1", 10);
                env.ConfigureAllyAction(2, "SK_Skill_Flow", "SE_Skill_Flow", "Hydro", "1*TotalATK,0,2", 10);
                env.ConfigureAllyAction(1, "SK_Heavy_Flow", "SE_Heavy_Flow", "Pyro", "1*TotalATK,0,1", 10);
                env.CreateMinimalBattle();
                EnemyBattleController adjacent = env.CreateEnemy(2);
                env.Enemy.Entity.TotalHP = env.Enemy.Entity.CurrentHP = 1300f;
                env.Manager.StartBattle();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                env.Manager.UseNormalAttackBySlot(0);
                env.Manager.UseSkillBySlot(0);
                env.Manager.UseHeavyAttackBySlot(0);

                Assert.That(BloomCoreSystem.ActiveCores, Is.Empty);
                Assert.That(env.Enemy.Entity.CurrentHP, Is.EqualTo(1081.2f).Within(0.01f));
                Assert.That(adjacent.Entity.CurrentHP, Is.EqualTo(931.2f).Within(0.01f));
            }
        }

        [Test]
        [Timeout(8000)]
        public void Crystallize_UsesRealSkillsAndGrantsLivingPartyTwoRoundShield()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.ConfigureAllyAction(
                    0,
                    "SK_Normal_Flow",
                    "SE_Normal_Flow",
                    "Hydro",
                    "1*TotalATK,0,1",
                    10);
                env.ConfigureAllyAction(
                    2,
                    "SK_Skill_Flow",
                    "SE_Skill_Flow",
                    "Geo",
                    "1*TotalATK,0,2",
                    10);
                env.CreateMinimalBattle();
                CharacterBattleController background = env.CreateAlly(2);
                env.Manager.StartBattle();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);

                Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True);
                Assert.That(env.Manager.UseSkillBySlot(0), Is.True);

                Assert.That(env.Enemy.Entity.CurrentHP, Is.EqualTo(900f).Within(0.01f));
                Assert.That(env.Enemy.Entity.GetAura("Hydro"), Is.Null);
                Assert.That(env.Enemy.Entity.GetAura("Geo"), Is.Null);
                Shield active = GetCrystallizeShield(env.Ally.Entity);
                Shield reserve = GetCrystallizeShield(background.Entity);
                Assert.That(active, Is.Not.Null);
                Assert.That(reserve, Is.Not.Null);
                Assert.That(active.Element, Is.EqualTo("Hydro"));
                Assert.That(active.Value, Is.EqualTo(77.4f).Within(0.01f));
                Assert.That(reserve.Value, Is.EqualTo(active.Value).Within(0.01f));
                Assert.That(active.Duration, Is.EqualTo(2f));

                env.Manager.EndAllyTurn();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.EnemyPreTurn);
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                Assert.That(active.Duration, Is.EqualTo(1f));

                env.Manager.EndAllyTurn();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.EnemyPreTurn);
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                Assert.That(GetCrystallizeShield(env.Ally.Entity), Is.Null);
                Assert.That(GetCrystallizeShield(background.Entity), Is.Null);
            }
        }

        [Test]
        [Timeout(5000)]
        public void Crystallize_GeoMultiTargetSortsByEnemySlotAndLastSlotWinsShield()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.ConfigureAllyAction(
                    2,
                    "SK_Skill_Flow",
                    "SE_Skill_Flow",
                    "Geo",
                    "1*TotalATK,0,2",
                    10);
                env.CreateMinimalBattle();
                EnemyBattleController second = env.CreateEnemy(2);
                env.Enemy.Entity.ApplyAura("Hydro", 1f, env.Ally.Entity.EntityID);
                second.Entity.ApplyAura("Pyro", 1f, env.Ally.Entity.EntityID);
                env.Manager.StartBattle();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                env.Ally.ForcedTargetPositions.Clear();
                env.Ally.ForcedTargetPositions.Add(2);
                env.Ally.ForcedTargetPositions.Add(1);

                Assert.That(env.Manager.UseSkillBySlot(0), Is.True);

                Assert.That(env.Enemy.Entity.GetAura("Hydro"), Is.Null);
                Assert.That(second.Entity.GetAura("Pyro"), Is.Null);
                Assert.That(GetCrystallizeShield(env.Ally.Entity).Element, Is.EqualTo("Pyro"));
            }
        }

        [Test]
        [Timeout(6000)]
        public void EnemyGeoSkill_NeutralizesAuraAndDealsNormalDamageWithoutShield()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.ConfigureEnemyDamageSkill(1f, "Geo", 2f);
                env.CreateMinimalBattle();
                env.Manager.StartBattle();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                env.Ally.Entity.ApplyAura("Hydro", 1f, env.Enemy.Entity.EntityID);
                env.Manager.EndAllyTurn();

                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.EnemyPostTurn);

                Assert.That(env.Ally.Entity.CurrentHP, Is.EqualTo(916.5289f).Within(0.01f));
                Assert.That(env.Ally.Entity.GetAura("Hydro"), Is.Null);
                Assert.That(env.Ally.Entity.GetAura("Geo"), Is.Null);
                Assert.That(env.Ally.Entity.Shields, Is.Empty);
            }
        }

        [Test]
        [Timeout(5000)]
        public void Swirl_UsesRealSkillEntryDealsDamageAndSpreadsAuraToAdjacentSlot()
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.ConfigureAllyAction(
                    0,
                    "SK_Normal_Flow",
                    "SE_Normal_Flow",
                    "Pyro",
                    "1*TotalATK,0,1",
                    10);
                env.ConfigureAllyAction(
                    2,
                    "SK_Skill_Flow",
                    "SE_Skill_Flow",
                    "Anemo",
                    "1*TotalATK,0,1",
                    10);
                env.CreateMinimalBattle();
                EnemyBattleController adjacent = env.CreateEnemy(2);
                env.Manager.StartBattle();
                env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
                env.Ally.ForcedTargetPositions.Clear();
                env.Ally.ForcedTargetPositions.Add(1);

                Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True);
                env.Ally.ForcedTargetPositions.Clear();
                env.Ally.ForcedTargetPositions.Add(1);
                Assert.That(env.Manager.UseSkillBySlot(0), Is.True);

                Assert.That(env.Enemy.Entity.CurrentHP, Is.EqualTo(879.36f).Within(0.01f));
                Assert.That(env.Enemy.Entity.GetAura("Pyro"), Is.Null);
                Assert.That(env.Enemy.Entity.GetAura("Anemo"), Is.Null);
                ElementalAura propagated = adjacent.Entity.GetAura("Pyro");
                Assert.That(propagated, Is.Not.Null);
                Assert.That(propagated.AuraAmount, Is.EqualTo(1f).Within(0.0001f));
                Assert.That(propagated.SourceEffectID, Is.EqualTo("SE_Skill_Flow"));
                Assert.That(adjacent.Entity.CurrentHP, Is.EqualTo(1000f));
            }
        }

        private static Shield GetCrystallizeShield(BattleEntity entity)
        {
            return entity.Shields.Find(shield =>
                shield != null && shield.Kind == ShieldKind.Crystallize);
        }
    }
}
