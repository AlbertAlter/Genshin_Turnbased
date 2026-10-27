using System.Collections.Generic;
using NUnit.Framework;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class EquipmentStatusLifetimeTests
    {
        [TestCase("ST_Weapon_LifetimeTest")]
        [TestCase("ST_Artifact_LifetimeTest")]
        public void OwnerDeath_RemovesOnlyItsEquipmentAcrossHosts_CancelsQueuedAndStaleDamage(string id)
        {
            using (var env = new BattleFlowTestEnv())
            {
                env.CreateMinimalBattle();
                var owner = env.Ally.Entity;
                var reserve = env.CreateAlly(2, false).Entity;
                var enemy = env.Enemy.Entity;
                var rootMain = Main(env, id, 990301);
                var childMain = Main(env, "ST_Lifetime_SharedChild", 990302);
                childMain.MaxStack = 0;
                var ordinaryMain = Main(env, "ST_Lifetime_CharacterSkill", 990303);
                var fieldMain = Main(env, "ST_Lifetime_Field", 990304);
                env.DataManager.StatusEffectDict["STE_Lifetime_Bind"] = new StatusEffectData
                {
                    StatusEffectID = 99030101, StatusEffectID2 = "STE_Lifetime_Bind", EffectType = "BindStatus",
                    Param1 = childMain.StatusID2, TargetType = "Allies", TargetSelect = "-1"
                };
                var root = owner.AddStatus(id, owner, 0, 1, 0, rootMain);
                var ownChild = reserve.GetStatus(childMain.StatusID2);
                Assert.That(ownChild, Is.Not.Null);
                Assert.That(ownChild.EquipmentSource, Is.SameAs(root.EquipmentSource));
                var otherSource = reserve.AddStatus(childMain.StatusID2, reserve, 3, 1, 0, childMain);
                var ordinary = owner.AddStatus(ordinaryMain.StatusID2, owner, 3, 1, 0, ordinaryMain);
                var enemyDebuff = enemy.AddStatus(childMain.StatusID2, owner, 3, 1, 0, childMain,
                    null, null, root.EquipmentSource);
                var field = env.Manager.Field.GetSlot(BattleSide.Enemy, 1);
                var fieldStatus = field.AddStatus(fieldMain.StatusID2, owner, 3, 1, 0, fieldMain,
                    equipmentSource: root.EquipmentSource);
                var staleDamage = DamageSourceInfo.FromStatus(root, "STE_Lifetime_Damage");
                var staleReaction = DamageSourceInfo.FromReactionSnapshot(ReactionSourceSnapshot.Capture(new ReactionContext
                { SourceEntity = owner, EquipmentSource = root.EquipmentSource }), ReactionType.Overloaded);

                env.DataManager.StatusEffectDict["STE_Lifetime_Energy"] = new StatusEffectData
                {
                    StatusEffectID2 = "STE_Lifetime_Energy", EffectType = "GainEnergy", Param1 = "1", Param2 = "Flat",
                    TargetType = "Allies", TargetSelect = "-1"
                };
                var queued = new StatusActionData { StatusID2 = id, ActionType = "OnTrigger", Param1 = "STE_Lifetime_Energy" };
                owner.CurrentEnergy = reserve.CurrentEnergy = 0f;
                using (BattleResolutionContext.BeginCollection())
                {
                    Assert.That(env.Ally.TryExecuteEventStatusAction(root, queued, owner,
                        new ScriptHookContext { Caster = owner, Status = root, StatusHost = owner }), Is.True);
                    owner.TakeDamage(owner.TotalHP + 1f,
                        DamageSourceInfo.Create(enemy, ReactionSourceKind.EnemySkill, "test", "test"));
                    Assert.That(owner.HasStatusInstance(root), Is.False);
                    Assert.That(reserve.HasStatusInstance(ownChild), Is.False);
                    Assert.That(enemy.HasStatusInstance(enemyDebuff), Is.False);
                    Assert.That(field.StatusList.Contains(fieldStatus), Is.False);
                    Assert.That(reserve.HasStatusInstance(otherSource), Is.True);
                    Assert.That(owner.HasStatusInstance(ordinary), Is.True, "未登记在该角色状态表中的普通状态不由装备清理删除");
                }
                Assert.That(reserve.CurrentEnergy, Is.Zero, "阵亡前排队的装备行动不能在之后执行");
                Assert.That(EquipmentStatusLifetime.CanExecute(staleDamage), Is.False);
                Assert.That(EquipmentStatusLifetime.CanExecute(staleReaction), Is.False);
                owner.CurrentHP = owner.TotalHP;
                Assert.That(EquipmentStatusLifetime.CanExecute(staleDamage), Is.False, "复活不能使旧请求复效");
                float hp = enemy.CurrentHP;
                using (var hits = new HitBatch())
                    Assert.That(hits.Resolve(new HitRequest
                    { Target = enemy, Source = staleDamage, DamageBeforeShield = 100f }), Is.Null);
                Assert.That(enemy.CurrentHP, Is.EqualTo(hp));
                Assert.That(owner.AddStatus(id, owner, 0, 1, 0, rootMain).EquipmentSource.IsAvailable, Is.True);
            }
        }

        private static StatusMainData Main(BattleFlowTestEnv env, string id, int numeric)
        {
            var main = new StatusMainData { StatusID = numeric, StatusID2 = id, MaxStack = 1, WhenMax = "0" };
            env.DataManager.StatusMainDict[id] = main;
            return main;
        }
    }
}
