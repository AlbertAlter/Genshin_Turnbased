using System.Globalization;
using System.Linq;
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    /// <summary>真实盾状态、公共反应入口和HitBatch之间的回归；按项目规则不自动运行Unity测试。</summary>
    public sealed class EnemyShieldIntegrationTests
    {
        private BattleFlowTestEnv env;
        private BattleEntity target;
        private int nextStatus;

        [SetUp]
        public void SetUp()
        {
            env = new BattleFlowTestEnv();
            env.ConfigureAllyAction(0, "SK_Shield_Integration", "SE_Shield_Integration", "Hydro", ".1*TotalATK,1", 0);
            env.CreateMinimalBattle();
            target = env.Enemy.Entity;
            target.TotalHP = target.CurrentHP = 1000f;
            nextStatus = 98980;
            env.DataManager.EnemyShieldRules = new EnemyShieldRuleTable();
            Rule("Pyro", "Hydro", "2"); Rule("Pyro", "Pyro", "0");
            Rule("Pyro", "Dendro", "1"); Rule("Pyro", "Electro", "1"); Rule("Pyro", "Anemo", "0.5");
            Rule("Hydro", "Cryo", "1"); Rule("Hydro", "Pyro", "0.5"); Rule("Hydro", "Electro", "1U");
            Rule("Electro", "Hydro", "1U"); Rule("Dendro", "Pyro", "1");
            Rule("Freeze", "Pyro", "2"); Rule("Freeze", "Physical", "0.25U");
            Rule("Geo", "Pyro", "2"); Rule("Geo", "Overloaded", "1U");
            BattleRandom.SetSource(new SequenceBattleRandomSource());
        }

        [TearDown]
        public void TearDown() { BattleRandom.ResetSource(); env?.Dispose(); }

        [Test]
        public void EnemyOwnedShieldStatus_CreatesShieldWithoutCharacterController_AndReplacementCleansOldState()
        {
            var first = Shield("Pyro", 12f);
            Assert.That(target.GetEnemyElementalShieldHP(), Is.EqualTo(12f));
            Assert.That(target.GetEnemyElementalShield().SourceStatus, Is.SameAs(first));
            var second = Shield("Hydro", 5f);
            Assert.That(target.HasStatusInstance(first), Is.False);
            Assert.That(target.Shields.Count(s => s.Kind == ShieldKind.EnemyElemental), Is.EqualTo(1));
            target.RemoveStatusInstance(second);
            Assert.That(target.GetEnemyElementalShield(), Is.Null);
        }

        [Test]
        public void RawHitBatch_DamagesHPAndGaugeIndependently_WithoutGaugeOverflowDamage()
        {
            Shield("Pyro", 3f);
            var first = Hit("Hydro", 1f).DamageEvent;
            Assert.That(first.IsImmune, Is.False);
            Assert.That(first.ActualHPDamage, Is.EqualTo(50f));
            Assert.That(target.GetEnemyElementalShieldHP(), Is.EqualTo(1f));
            Assert.That(Hit("Hydro", 1f).DamageEvent.ActualHPDamage, Is.EqualTo(50f));
            Assert.That(target.GetEnemyElementalShield(), Is.Null);
            Assert.That(target.CurrentHP, Is.EqualTo(900f));
            Assert.That(Hit("Hydro", 1f).DamageEvent.ActualHPDamage, Is.EqualTo(50f));
            Assert.That(target.CurrentHP, Is.EqualTo(850f));
        }

        [Test]
        public void ReactionAndMainHit_ShareOneShieldLoss_AndDoNotCreateOrdinaryAura()
        {
            Shield("Pyro", 5f);
            var reaction = Contact("Hydro", 1f);
            Assert.That(reaction.TriggeredReactions.Count(r => r.Type == ReactionType.Vaporize), Is.EqualTo(1));
            Assert.That(target.GetEnemyElementalShieldHP(), Is.EqualTo(3f));
            var hit = Hit("Hydro", 1f, reaction).DamageEvent;
            Assert.That(target.GetEnemyElementalShieldHP(), Is.EqualTo(3f));
            Assert.That(hit.ActualHPDamage, Is.EqualTo(reaction.FinalDamage).Within(.001f));
            Assert.That(target.CurrentHP, Is.EqualTo(1000f - reaction.FinalDamage).Within(.001f));
            Assert.That(ElementQuery.HasAura(target, "Pyro"), Is.False);
            Assert.That(ElementQuery.HasAura(target, "Hydro"), Is.False);
        }

        [Test]
        public void BreakingReaction_DiscardsExcessElement_ButStillAppliesItsOwnHPDamage()
        {
            Shield("Pyro", 1f);
            var reaction = Contact("Hydro", 3f);
            Assert.That(reaction.EnemyShieldHit.BrokeShield, Is.True);
            Assert.That(target.GetAura("Hydro"), Is.Null);
            var hit = Hit("Hydro", 3f, reaction).DamageEvent;
            Assert.That(hit.IsImmune, Is.False);
            Assert.That(hit.ActualHPDamage, Is.EqualTo(reaction.FinalDamage).Within(.001f));
            Assert.That(target.CurrentHP, Is.EqualTo(1000f - reaction.FinalDamage).Within(.001f));
        }

        [Test]
        public void SameElementContact_DoesNotReplaceOrConsumeShield()
        {
            Shield("Pyro", 12f);
            Hit("Pyro", 2f, Contact("Pyro", 2f));
            Assert.That(target.GetEnemyElementalShieldHP(), Is.EqualTo(12f));
            Assert.That(target.GetAura("Pyro"), Is.Null);
            Assert.That(target.CurrentHP, Is.EqualTo(950f));
        }

        [Test]
        public void HydroShield_Freezes_UsesFreezeRules_AndReturnsToHydroAfterMeltingFrozenPool()
        {
            Shield("Hydro", 5f);
            Contact("Cryo", 1f);
            Assert.That(target.GetEnemyElementalShieldHP(), Is.EqualTo(4f));
            Assert.That(FrozenReactionHandler.IsFrozen(target), Is.True);
            Contact("Cryo", 1f);
            Assert.That(target.GetEnemyElementalShieldHP(), Is.EqualTo(4f));
            var melt = Contact("Pyro", 1f);
            Assert.That(melt.TriggeredReactions.Count(r => r.Type == ReactionType.Melt), Is.EqualTo(1));
            Assert.That(target.GetEnemyElementalShieldHP(), Is.EqualTo(2f));
            Assert.That(FrozenReactionHandler.IsFrozen(target), Is.False);
            Contact("Pyro", 1f);
            Assert.That(target.GetEnemyElementalShieldHP(), Is.EqualTo(1.5f));
        }

        [Test]
        public void CryoShield_DoesNotGrantFrozenControl()
        {
            Shield("Cryo", 5f);
            var reaction = Contact("Hydro", 2f);
            Assert.That(reaction.HasReaction, Is.False);
            Assert.That(FrozenReactionHandler.IsFrozen(target), Is.False);
            Assert.That(target.GetEnemyElementalShieldHP(), Is.EqualTo(5f));
        }

        [Test]
        public void FrozenHydroShield_ShatterRestoresHydro_WithoutSecondPhysicalShieldLoss()
        {
            Shield("Hydro", 5f);
            Contact("Cryo", 1f);
            env.Ally.Entity.WeaponType = 1;
            var reaction = Contact("Physical", 0f);
            var resolution = Hit("Physical", 0f, reaction);
            ReactionEffectExecutor.FinalizePrimaryHit(reaction, resolution.DamageEvent.ActualHPDamage);
            ReactionEffectExecutor.ExecuteDerivedHits(reaction);
            Assert.That(FrozenReactionHandler.IsFrozen(target), Is.False);
            Assert.That(target.GetEnemyElementalShield().Element, Is.EqualTo("Hydro"));
            Assert.That(target.GetEnemyElementalShieldHP(), Is.EqualTo(3.75f));
            Assert.That(resolution.DamageEvent.ActualHPDamage, Is.EqualTo(50f));
            Assert.That(target.CurrentHP, Is.LessThan(1000f));
        }

        [Test]
        public void PyroShield_BurningTickDamagesHP_WithoutConsumingGUOrApplyingPyro()
        {
            Shield("Pyro", 12f);
            Contact("Dendro", 2f);
            Assert.That(BurningReactionHandler.IsBurning(target), Is.True);
            Assert.That(target.GetEnemyElementalShieldHP(), Is.EqualTo(10f));
            BurningReactionHandler.TickTurnEnd(TurnPhase.AllyPostTurn);
            Assert.That(target.GetEnemyElementalShieldHP(), Is.EqualTo(10f));
            Assert.That(target.GetAura("Pyro"), Is.Null);
            Assert.That(target.CurrentHP, Is.LessThan(1000f));
        }

        [Test]
        public void BurningPool_ConsumesIncomingFirst_AndOnlyRemainingHydroDepletesPyroShield()
        {
            Shield("Pyro", 12f);
            Contact("Dendro", 1f);
            var reaction = Contact("Hydro", 2f);
            Assert.That(target.GetEnemyElementalShieldHP(), Is.EqualTo(8f)); // 11 - (2 - 0.5) * 2
            Assert.That(reaction.TriggeredReactions.Count(r => r.Type == ReactionType.Vaporize), Is.EqualTo(1));
        }

        [TestCase("Hydro", "Electro")]
        [TestCase("Electro", "Hydro")]
        public void ConductingShield_UsesExistingElectroChargedTicks_AndDamagesHPWhenBreaking(string shield, string attack)
        {
            Shield(shield, 2f);
            var reaction = Contact(attack, 2f);
            Hit(attack, 2f, reaction);
            ReactionEffectExecutor.ExecuteDerivedHits(reaction);
            Assert.That(ElectroChargedReactionHandler.IsElectroCharged(target), Is.True);
            Assert.That(target.GetEnemyElementalShieldHP(), Is.EqualTo(1f));
            float beforeBreakingTick = target.CurrentHP;
            ElectroChargedReactionHandler.TickTurnEnd(TurnPhase.AllyPostTurn);
            Assert.That(target.GetEnemyElementalShield(), Is.Null);
            Assert.That(target.CurrentHP, Is.LessThan(beforeBreakingTick));
            Assert.That(ElectroChargedReactionHandler.IsElectroCharged(target), Is.False);
        }

        [Test]
        public void GeoShield_TriggersCrystallize_AndUsesGeoShieldMatrix()
        {
            Shield("Geo", 5f);
            var reaction = Contact("Pyro", 1f);
            Assert.That(reaction.TriggeredReactions.Any(r => r.Type == ReactionType.Crystallize), Is.True);
            Assert.That(target.GetEnemyElementalShieldHP(), Is.EqualTo(3f));
        }

        [TestCase("Hydro")]
        [TestCase("Anemo")]
        public void PureElementApplication_BreaksShieldWithoutOrdinaryHitOrOverflowAura(string element)
        {
            Shield("Pyro", .25f);
            int ordinaryHits = 0;
            using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved,
                (_, fact) => { if (fact.HitLanded) ordinaryHits++; }, 0);
            ApplyElementEffect.Execute(element, "1", env.Ally.Entity, new[] { target },
                ReactionSourceKind.CharacterSkill, "SK_Shield_Integration", "SE_Shield_Integration",
                ReactionResolver.BeginEffectExecution());
            Assert.That(target.GetEnemyElementalShield(), Is.Null);
            Assert.That(target.CurrentHP, Is.EqualTo(1000f));
            Assert.That(target.GetAura(element), Is.Null);
            Assert.That(ordinaryHits, Is.Zero);
        }

        [Test]
        public void OverloadedDerivedHit_UsesItsOwnMatrixColumnInsteadOfPyroElementColumn()
        {
            Shield("Geo", 5f);
            using var batch = new HitBatch();
            batch.Resolve(new HitRequest
            {
                Target = target, Element = "Pyro", DamageBeforeShield = 50f,
                Source = DamageSourceInfo.Create(env.Ally.Entity, ReactionSourceKind.DerivedReaction,
                    "SK_Shield_Integration", "SE_Shield_Integration", ReactionType.Overloaded, countsAsHit: false)
            });
            Assert.That(target.GetEnemyElementalShieldHP(), Is.EqualTo(4f));
            Assert.That(target.CurrentHP, Is.EqualTo(950f));
        }

        [TestCase("Hydro")]
        [TestCase("Anemo")]
        public void RealCharacterAttack_UsesShieldSnapshotAcrossReactionAndDamage(string element)
        {
            env.DataManager.SkillEffectDict["SE_Shield_Integration"].Element = element;
            env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            target.CurrentHP = 1000f;
            Shield("Pyro", .25f);
            Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True);
            Assert.That(target.GetEnemyElementalShield(), Is.Null);
            Assert.That(target.CurrentHP, Is.LessThan(1000f));
            Assert.That(target.GetAura(element), Is.Null);
        }

        [TestCase(-.5f, 25f)]
        [TestCase(-1f, 0f)]
        public void ShieldStatusDamageReduction_AppliesToBreakingHit_AndEndsBeforeNextHit(float taken, float hpDamage)
        {
            var shieldStatus = Shield("Pyro", 1f);
            shieldStatus.MainData.MultiplierPart = "DMGTaken";
            string effectID = "STE_Shield_Reduction";
            env.DataManager.StatusEffectDict[effectID] = new StatusEffectData
            {
                StatusEffectID = shieldStatus.MainData.StatusID * 100 + 2,
                StatusEffectID2 = effectID, EffectType = "Buff",
                Param1 = taken.ToString(CultureInfo.InvariantCulture)
            };
            var snapshot = target.PrepareDMGTaken("SE_Shield_Integration", "SK_Shield_Integration", "Normal", "Hydro");
            var contact = Contact("Hydro", 1f);
            Assert.That(target.GetEnemyElementalShield(), Is.Null);
            using (var batch = new HitBatch())
            {
                var hit = batch.Resolve(new HitRequest
                {
                    Target = target, Element = "Hydro", ElementAmount = 1f,
                    DamageBeforeShield = 50f, DamageType = "Normal", DMGTaken = snapshot,
                    EnemyShieldHit = contact.EnemyShieldHit,
                    Source = DamageSourceInfo.Create(env.Ally.Entity, ReactionSourceKind.CharacterSkill,
                        "SK_Shield_Integration", "SE_Shield_Integration")
                }).DamageEvent;
                Assert.That(hit.IsImmune, Is.False, "状态减伤归零不伪装成Immune");
                Assert.That(hit.ActualHPDamage, Is.EqualTo(hpDamage));
                Assert.That(hit.HitLanded, Is.True);
            }
            Assert.That(Hit("Hydro", 1f).DamageEvent.ActualHPDamage, Is.EqualTo(50f));
            Assert.That(target.CurrentHP, Is.EqualTo(950f - hpDamage));
        }

        [TestCase(100f, 0f)]
        [TestCase(150f, 50f)]
        public void HPShield_ExactBreakOrOverflow_OnlyExcessReachesHP(float incoming, float hpDamage)
        {
            target.AddShield(100f, "None", 3f);
            using var batch = new HitBatch();
            var hit = batch.Resolve(new HitRequest
            {
                Target = target, Element = "Physical", DamageBeforeShield = incoming,
                Source = DamageSourceInfo.Create(env.Ally.Entity, ReactionSourceKind.CharacterSkill,
                    "SK_Shield_Integration", "SE_Shield_Integration")
            }).DamageEvent;
            Assert.That(hit.ShieldAbsorbed, Is.EqualTo(100f));
            Assert.That(hit.ActualHPDamage, Is.EqualTo(hpDamage));
            Assert.That(target.CurrentHP, Is.EqualTo(1000f - hpDamage));
        }

        [Test]
        public void TrueDamage_RetainsExistingDirectHPBypass_WithoutConsumingEnemyShield()
        {
            Shield("Pyro", 12f);
            Assert.That(Hit("True", 0f).DamageEvent.ActualHPDamage, Is.EqualTo(50f));
            Assert.That(target.GetEnemyElementalShieldHP(), Is.EqualTo(12f));
        }

        private void Rule(string shield, string attack, string raw)
            => env.DataManager.EnemyShieldRules.Add(EnemyShieldRuleParser.Parse(raw, shield, attack));

        private StatusInstance Shield(string element, float gauge)
        {
            int id = nextStatus++;
            string statusID = "ST_Shield_Integration_" + id;
            var main = new StatusMainData { StatusID = id, StatusID2 = statusID, StatusType = "Shield" };
            env.DataManager.StatusMainDict[statusID] = main;
            env.DataManager.StatusEffectDict["STE_Shield_Integration_" + id] = new StatusEffectData
            {
                StatusEffectID = id * 100 + 1, StatusEffectID2 = "STE_Shield_Integration_" + id,
                EffectType = "Shield", Element = element, TargetType = "Self",
                Param1 = gauge.ToString(CultureInfo.InvariantCulture) + "U"
            };
            return target.AddStatus(statusID, target, 3, 1, 0, main);
        }

        private ReactionResult Contact(string element, float amount)
            => ReactionResolver.Resolve(new ReactionContext
            {
                SourceEntity = env.Ally.Entity, Target = target, SourceKind = ReactionSourceKind.CharacterSkill,
                SourceSkillID = "SK_Shield_Integration", SourceEffectID = "SE_Shield_Integration",
                EffectExecutionID = ReactionResolver.BeginEffectExecution(), LogicalHitID = BattleEventHub.NextIdentity(),
                TargetIndex = 1, ApplicationPhase = (int)TurnPhase.AllyAction,
                AttackElement = element, AttackAmount = amount, PreReactionDamage = 50f, DamageType = "Normal"
            });

        private HitResolution Hit(string element, float amount, ReactionResult reaction = null)
        {
            using var batch = new HitBatch();
            return batch.Resolve(new HitRequest
            {
                Target = target, Element = element, ElementAmount = amount, DamageBeforeShield = reaction?.FinalDamage ?? 50f,
                DamageType = "Normal", EnemyShieldHit = reaction?.EnemyShieldHit,
                Source = DamageSourceInfo.Create(env.Ally.Entity, ReactionSourceKind.CharacterSkill,
                    "SK_Shield_Integration", "SE_Shield_Integration", reaction?.PrimaryDamageReactionType ?? ReactionType.None)
            });
        }
    }
}
