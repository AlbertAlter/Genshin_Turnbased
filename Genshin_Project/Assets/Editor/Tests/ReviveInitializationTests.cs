using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class ReviveInitializationTests
    {
        private const string Passive = "ST_ReviveInitialHP", Temporary = "ST_ReviveTemporary";
        private const string Count = "ST_Weapon_4305602", Artifact = "ST_Artifact_ReviveProbe";

        [UnityTest]
        public IEnumerator Revival_RecreatesOnlyInitialStates_InRegistrationOrder_WithFreshCountAndEquipmentIdentity()
            => HookTestEnv.AwaitTask(RestoreAsync);

        private async Task RestoreAsync()
        {
            using var env = new BattleFlowTestEnv();
            env.ConfigureStatus(Passive); env.ConfigureStatus(Temporary); env.ConfigureStatus(Count); env.ConfigureStatus(Artifact);
            var main = env.DataManager.StatusMainDict[Passive];
            main.StatusID = 9917101; main.StatusType = "Buff"; main.MultiplierPart1 = "HPBonusFlat";
            env.DataManager.StatusEffectDict["STE_ReviveInitialHP"] = new StatusEffectData
            { StatusEffectID = 991710101, StatusEffectID2 = "STE_ReviveInitialHP", EffectType = "Buff", Param1 = "200" };
            env.DataManager.StatusMainDict[Count].StatusType = "Count";
            env.DataManager.CharacterStatusIDs[BattleFlowTestEnv.AllyID] = new HashSet<string> { Passive, Temporary };
            env.DataManager.TalentDict[BattleFlowTestEnv.AllyID] = new List<TalentData>
            { new TalentData { CharacterID = BattleFlowTestEnv.AllyID, Modifiers = "InitiateStatus(" + Passive + ")" } };
            env.DataManager.ConstellationDict[BattleFlowTestEnv.AllyID] = new List<ConstellationData>
            { new ConstellationData { ConstellationIndex = 3, Modifiers = "SkillLevelUp(2,3)" },
              new ConstellationData { ConstellationIndex = 5, Modifiers = "SkillLevelUp(3,3)" } };
            env.DataManager.WeaponAttributesDict[3056] = new WeaponAttributesData
            { WeaponID = 3056, WeaponType = 0, BaseATK = 40f };
            env.DataManager.WeaponInitiateStatusDict[3056] = new List<string> { Count };
            env.DataManager.ArtifactInitiateStatusDict[999] = new Dictionary<int, List<string>>
            { [2] = new List<string> { Artifact } };
            env.ConfigureAllyAction(2, "SK_ReviveRuntime", "SE_ReviveRuntime", "Physical", "0.1", cooldown: 3);
            env.DataManager.CharacterSkillDict[BattleFlowTestEnv.AllyID].Find(skill => skill.SkillID2 == "SK_ReviveRuntime").MaxCharge = 2;
            env.DataManager.SkillLevelDict[$"{BattleFlowTestEnv.AllyID}_2"][7] = new SkillLevelData
            { CharacterID = BattleFlowTestEnv.AllyID, SkillType = 2, SkillLevel = 7, ParamID = "SE_ReviveRuntime", Hits1 = "0.1" };
            env.CreateMinimalBattle(); env.CreateAlly(2);
            CharacterBuildApplier.ApplyBuild(env.Ally, new CharacterProfile
            { CharacterID = BattleFlowTestEnv.AllyID, Level = 1, ConstellationLevel = 5, SkillLevels = new[] { 2, 3, 4, 5 },
                Weapon = new WeaponLoadout { WeaponID = 3056 } });
            Assert.That(env.Ally.InitializeArtifactSetStatuses(new Dictionary<int, int> { [999] = 2 }), Is.True);
            var owner = env.Ally.Entity;
            var original = new[] { owner.GetStatus(Passive), owner.GetStatus(Count), owner.GetStatus(Artifact) };
            original[1].RuntimeValue = 4f;
            var staleDamage = DamageSourceInfo.FromStatus(original[1], "STE_ReviveOldDamage");
            owner.AddStatus(Temporary, owner, 3, 2, 0, env.DataManager.StatusMainDict[Temporary]);
            env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            await BattleExecution.Run(() => env.Ally.ExecuteSkillByIDAsync("SK_ReviveRuntime", 2, true));
            int cooldown = env.Ally.GetCooldownRemaining(2), charges = env.Ally.GetBoundSkillCurrentCharge(2);
            float weaponATK = owner.WeaponATK; owner.CurrentEnergy = 6.5f;
            await BattleExecution.Run(async () =>
            {
                owner.TakeDamage(100000, DamageSourceInfo.Create(env.Enemy.Entity, ReactionSourceKind.EnemySkill, "test", "test"));
                await BattleExecution.Flush();
            });
            Assert.That(original.All(status => !status.IsActive), Is.True);
            var surviving = env.Manager.EnemyTeam.AddStatus("ST_KeptOrdering", env.Enemy.Entity, 0, 2, 0,
                new StatusMainData { StatusID2 = "ST_KeptOrdering" });
            int healthEvents = 0;
            using var listener = BattleEventHub.Subscribe<HPChangedEventData>(BattleEventKind.HPChanged, (_, fact) =>
            {
                if (fact.Subject != owner || fact.Reason != "Revive") return;
                healthEvents++;
                Assert.That(owner.HasStatus(Passive) && owner.HasStatus(Count) && owner.HasStatus(Artifact), Is.True);
                Assert.That(owner.CurrentHP, Is.EqualTo(600f));
            }, 0);
            await BattleExecution.Run(() =>
            {
                ReviveEffect.Execute("0.5*Target.TotalHP", env.Manager.GetEntityByPosition(BattleSide.Ally, 2), new[] { owner });
                return Task.CompletedTask;
            });
            var restored = new[] { owner.GetStatus(Passive), owner.GetStatus(Count), owner.GetStatus(Artifact) };
            Assert.That(restored.All(status => status.IsActive && status.Caster == owner), Is.True);
            Assert.That(restored.Select(status => status.ApplyOrder), Is.Ordered.Ascending);
            Assert.That(restored[0].ApplyOrder, Is.GreaterThan(surviving.ApplyOrder));
            Assert.That(restored[1].RuntimeValue, Is.EqualTo(1f), "新Count采用处理器初值");
            Assert.That(restored[1].EquipmentSource, Is.Not.SameAs(original[1].EquipmentSource));
            Assert.That(restored[1].EquipmentSource.IsAvailable, Is.True);
            Assert.That(EquipmentStatusLifetime.CanExecute(staleDamage), Is.False);
            Assert.That(owner.HasStatus(Temporary), Is.False);
            Assert.That(owner.CurrentHP, Is.EqualTo(600f)); Assert.That(healthEvents, Is.EqualTo(1));
            Assert.That(owner.CurrentEnergy, Is.EqualTo(6.5f)); Assert.That(owner.WeaponATK, Is.EqualTo(weaponATK));
            Assert.That(env.Ally.GetEffectiveSkillLevel(2), Is.EqualTo(7));
            Assert.That(env.Ally.GetEffectiveSkillLevel(3), Is.EqualTo(8));
            Assert.That(env.Ally.GetCooldownRemaining(2), Is.EqualTo(cooldown));
            Assert.That(env.Ally.GetBoundSkillCurrentCharge(2), Is.EqualTo(charges));
            Assert.That(env.Ally.InitializeArtifactSetStatuses(new Dictionary<int, int> { [999] = 2 }), Is.True);
            Assert.That(owner.GetStatus(Artifact), Is.SameAs(restored[2]), "更新复活缓存，不能重复施加");
            listener.Dispose();
            await BattleExecution.Run(async () =>
            {
                owner.TakeDamage(100000, DamageSourceInfo.Create(env.Enemy.Entity, ReactionSourceKind.EnemySkill, "test", "test"));
                await BattleExecution.Flush();
                ReviveEffect.Execute("250", null, new[] { owner });
                await BattleExecution.Flush();
            });
            Assert.That(owner.CurrentHP, Is.EqualTo(250f), "固定值复活不能填满生命");
            Assert.That(env.Ally.GetEffectiveSkillLevel(2), Is.EqualTo(7));
            Assert.That(env.Ally.GetEffectiveSkillLevel(3), Is.EqualTo(8), "再次复活不能再加一次命座等级");
            Assert.That(owner.GetStatus(Count).RuntimeValue, Is.EqualTo(1f));
            Assert.That(restored[1].EquipmentSource.IsAvailable, Is.False);
        }

        [Test]
        public void InitialPublicState_ThatSurvivedDeath_IsNotReapplied_AndLivingTargetsAreIgnored()
        {
            using var env = new BattleFlowTestEnv(); env.ConfigureStatus(Passive);
            env.DataManager.TalentDict[BattleFlowTestEnv.AllyID] = new List<TalentData>
            { new TalentData { CharacterID = BattleFlowTestEnv.AllyID, Modifiers = "InitiateStatus(" + Passive + ")" } };
            env.CreateMinimalBattle(); env.CreateAlly(2);
            CharacterBuildApplier.ApplyBuild(env.Ally, new CharacterProfile { CharacterID = BattleFlowTestEnv.AllyID, Level = 1 });
            var owner = env.Ally.Entity; var kept = owner.GetStatus(Passive);
            kept.LifeTriggerCount = 7;
            owner.CurrentHP = 0;
            ReviveEffect.Execute("100", null, new[] { owner });
            Assert.That(owner.GetStatus(Passive), Is.SameAs(kept)); Assert.That(kept.LifeTriggerCount, Is.EqualTo(7));
            Assert.That(owner.GetStatusList().Count(status => status.StatusID2 == Passive), Is.EqualTo(1));
            Assert.That(owner.CurrentHP, Is.EqualTo(100f));
            ReviveEffect.Execute("900", null, new[] { owner });
            Assert.That(owner.CurrentHP, Is.EqualTo(100f));
        }
    }
}
