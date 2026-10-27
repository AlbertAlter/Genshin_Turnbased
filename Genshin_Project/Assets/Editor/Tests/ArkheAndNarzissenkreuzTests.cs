using System.Collections.Generic;
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    /// <summary>始基力是普通公共隐藏标记；这里只写定向用例，不运行Unity。</summary>
    public sealed class ArkheAndNarzissenkreuzTests
    {
        private static void MarkDefinition(BattleFlowTestEnv env, string id, int number)
            => env.DataManager.StatusMainDict[id] = new StatusMainData
            { StatusID = number, StatusID2 = id, StatusName = id, StatusType = "", Display = 0,
                MaxCount = 1, MaxStack = 1, WhenMax = "1" };
        private static StatusInstance Mark(BattleFlowTestEnv env, BattleEntity owner, string id)
            => owner.AddStatus(id, owner, 0, 2, 0, env.DataManager.StatusMainDict[id]);

        [Test]
        public void ArkheMarkers_AreIndependentIdempotentOrdinaryStatuses_WithOnlyOwnPresenceQueries()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle(); var owner = env.Ally.Entity;
            MarkDefinition(env, ArkheStatuses.Pneuma, 40036); MarkDefinition(env, ArkheStatuses.Ousia, 40037);
            Assert.That(ArkheStatuses.HasAny(owner), Is.False); Assert.That(ArkheStatuses.HasAny(null), Is.False);
            var pneuma = Mark(env, owner, ArkheStatuses.Pneuma);
            Assert.That(Mark(env, owner, ArkheStatuses.Pneuma), Is.SameAs(pneuma));
            var ousia = Mark(env, owner, ArkheStatuses.Ousia);
            Assert.That(ArkheStatuses.HasPneuma(owner), Is.True); Assert.That(ArkheStatuses.HasOusia(owner), Is.True);
            Assert.That(owner.GetStatusInstances(ArkheStatuses.Pneuma), Has.Count.EqualTo(1));
            Assert.That(owner.GetStatusInstances(ArkheStatuses.Ousia), Has.Count.EqualTo(1));
            Assert.That(pneuma.IsPermanent && ousia.IsPermanent, Is.True);
            Assert.That(owner.ElementalAuras, Is.Empty, "标记不是元素附着，也不是Count或属性Buff");
            Assert.That(owner.GetStatusATKBonus("", "", -1, ""), Is.Zero);
            owner.RemoveStatusInstance(pneuma);
            Assert.That(ArkheStatuses.HasPneuma(owner), Is.False); Assert.That(ArkheStatuses.HasAny(owner), Is.True);
            owner.RemoveStatusInstance(ousia); Assert.That(ArkheStatuses.HasAny(owner), Is.False);
        }

        [Test]
        public void CharacterModifiers_InitializeSelectedArkheMarkersPermanently_OnlyWhenUnlocked()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            MarkDefinition(env, ArkheStatuses.Pneuma, 40036); MarkDefinition(env, ArkheStatuses.Ousia, 40037);
            env.DataManager.TalentDict[BattleFlowTestEnv.AllyID] = new List<TalentData>
            { new TalentData { CharacterID = BattleFlowTestEnv.AllyID, TalentIndex = 3, UnlockAfter = 20,
                Modifiers = "InitiateStatus(ST_Arkhe_Pneuma);InitiateStatus(ST_Arkhe_Ousia)" } };
            var profile = new CharacterProfile { CharacterID = BattleFlowTestEnv.AllyID, Level = 1 };
            CharacterBuildApplier.ApplyBuild(env.Ally, profile);
            Assert.That(ArkheStatuses.HasAny(env.Ally.Entity), Is.False);
            profile.Level = 20; profile.Ascension = 0;
            CharacterBuildApplier.ApplyBuild(env.Ally, profile);
            Assert.That(ArkheStatuses.HasAny(env.Ally.Entity), Is.False, "达到等级但未突破不解锁");
            profile.Ascension = 1; CharacterBuildApplier.ApplyBuild(env.Ally, profile);
            Assert.That(env.Ally.Entity.GetStatus(ArkheStatuses.Pneuma).IsPermanent, Is.True);
            Assert.That(env.Ally.Entity.GetStatus(ArkheStatuses.Ousia).IsPermanent, Is.True);
        }

        [TestCase(0), TestCase(1), TestCase(2), TestCase(3), Timeout(7000)]
        public void Sword_OnlyChecksWearerMarkers_UsesIntrinsicElementAndZeroAura_AndOnlySuccessTakesCooldown(int flags)
        {
            using var env = new BattleFlowTestEnv();
            env.ConfigureAllyAction(0, "SK_NarzTrigger", "SE_NarzTrigger", "Cryo", ".1*TotalATK,0", 0);
            MarkDefinition(env, ArkheStatuses.Pneuma, 40036); MarkDefinition(env, ArkheStatuses.Ousia, 40037);
            const string id = "ST_Weapon_4304701", effectID = "STE_Weapon_430470101";
            var main = new StatusMainData { StatusID = 4304701, StatusID2 = id, StatusType = "Buff", Display = 0,
                MaxStack = 1, WhenMax = "0" };
            env.DataManager.StatusMainDict[id] = main;
            var definition = new StatusEffectData { StatusEffectID = 430470101, StatusEffectID2 = effectID,
                EffectType = "Damage", Element = "Physical", Param1 = "Index(1)*TotalATK,0", Param3 = "Pneuma;Ousia",
                TargetType = "Enemy", TargetSelect = "-1", DamageHook = "Hook(430470101)" };
            env.DataManager.StatusEffectDict[effectID] = definition;
            env.DataManager.StatusActionDict[id] = new List<StatusActionData>
            { new StatusActionData { StatusID = 4304701, StatusID2 = id, ActionType = "OnTrigger", Param1 = effectID,
                ScriptHook = "PostSelfDamage(4304701);Hook(4304701)", OutgoingHit = "Normal;Heavy",
                MaxTimePerEffect = 1, Cooldown = 3 } };
            env.CreateMinimalBattle(); var owner = env.Ally.Entity; var second = env.CreateEnemy(2).Entity;
            env.DataManager.CharacterOverviewList.Add(new CharacterOverviewData
                { CharacterID = owner.EntityID, Element = "Pyro" });
            var other = env.CreateAlly(2).Entity;
            Mark(env, other, ArkheStatuses.Pneuma); Mark(env, other, ArkheStatuses.Ousia);
            if ((flags & 1) != 0) Mark(env, owner, ArkheStatuses.Pneuma);
            if ((flags & 2) != 0) Mark(env, owner, ArkheStatuses.Ousia);
            var weapon = new WeaponRuntimeContext { WeaponID = 3047, Equipper = owner, Refinement = 1,
                SourceInstanceID = "NarzTest" }; weapon.ResolvedParams[1] = 1.6f;
            owner.AddStatus(id, owner, 0, 2, 0, main, weapon);
            env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            var random = new SequenceBattleRandomSource(); BattleRandom.SetSource(random);
            foreach (var enemy in new[] { env.Enemy.Entity, second })
                enemy.ElementalAuras.Add(new ElementalAura { Element = "Hydro", AuraAmount = 1f });
            var hits = new List<DamageEventData>(); int reactions = 0;
            using var reactionListener = BattleEventHub.Subscribe<ReactionEventData>(BattleEventKind.ReactionOccurred, (_, fact) => reactions++, 0);
            using var damageListener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, fact) =>
            { if (fact.Source.SourceEffectID == effectID) hits.Add(fact); }, 0);
            Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True);
            Assert.That(hits, Has.Count.EqualTo(flags == 0 ? 2 : 0), "有任一标记不触发；队友的标记不算装备者的");
            owner.RemoveStatus(ArkheStatuses.Pneuma); owner.RemoveStatus(ArkheStatuses.Ousia);
            Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True);
            Assert.That(hits, Has.Count.EqualTo(2), "无标记时先前成功已占冷却；有标记时先前失败不得占冷却");
            foreach (var hit in hits)
            {
                Assert.That(hit.Element, Is.EqualTo("Pyro"), "固有火而不是触发攻击的冰或占位物理");
                Assert.That(hit.ElementAmount, Is.Zero); Assert.That(hit.DamageType, Is.Null.Or.Empty);
                Assert.That(hit.DamageBeforeShield, Is.GreaterThan(0f)); Assert.That(hit.Critical.HasValue, Is.True);
                Assert.That(hit.Source.SourceEntity, Is.SameAs(owner));
                Assert.That(hit.IsPneumaDamage && hit.IsOusiaDamage, Is.True, "同一段冲击同时携带芒荒，不拆伤害");
            }
            Assert.That(reactions, Is.Zero);
            foreach (var enemy in new[] { env.Enemy.Entity, second })
            {
                Assert.That(enemy.GetAura("Hydro")?.AuraAmount, Is.EqualTo(1f));
                Assert.That(enemy.GetAura("Pyro"), Is.Null);
            }
            Assert.That(definition.Element, Is.EqualTo("Physical"), "不改DataManager共享行");
            Assert.That(owner.HasStatus(ArkheStatuses.Pneuma), Is.False, "武器不会给角色反向初始化始基力");
            Assert.That(owner.HasStatus(ArkheStatuses.Ousia), Is.False);
            Assert.That(random.FloatCallCount, Is.EqualTo(4)); Assert.That(random.IntCallCount, Is.Zero);
        }
    }
}
