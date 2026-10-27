using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GenshinTurnBased.Tests.EditMode
{
    // 仅以内存表走正式选择、技能、状态和声明入口，不读取策划源目录。
    public sealed class BennettScriptHooksTests
    {
        private static StatusMainData Main(BattleFlowTestEnv env, int n, string id, string type = null, string part = null)
        {
            var value = new StatusMainData { StatusID = 4102000 + n, StatusID2 = id, StatusName = id,
                StatusType = type, MultiplierPart2 = part, MaxCount = 1, MaxStack = 1, WhenMax = "0" };
            env.DataManager.StatusMainDict[id] = value;
            if (!env.DataManager.CharacterStatusIDs.TryGetValue(1020, out var ids))
                env.DataManager.CharacterStatusIDs[1020] = ids = new HashSet<string>();
            ids.Add(id); return value;
        }
        private static StatusEffectData Effect(BattleFlowTestEnv env, int n, int index, string id, string type,
            string param = null, string target = "Self")
        {
            var effect = new StatusEffectData { StatusEffectID = (4102000 + n) * 100 + index,
                StatusEffectID2 = id, EffectIndex = index, EffectType = type, Param1 = param, TargetType = target };
            env.DataManager.StatusEffectDict[id] = effect; return effect;
        }
        private static void Level(BattleFlowTestEnv env, string id, string first, string second)
        {
            env.DataManager.SkillLevelDict[$"{BattleFlowTestEnv.AllyID}_3_{id}"] = new Dictionary<int, SkillLevelData>
            {
                [1] = new SkillLevelData { CharacterID = BattleFlowTestEnv.AllyID, SkillType = 3, SkillLevel = 1, ParamID = id, Hits1 = first },
                [2] = new SkillLevelData { CharacterID = BattleFlowTestEnv.AllyID, SkillType = 3, SkillLevel = 2, ParamID = id, Hits1 = second }
            };
        }
        private static BattleInputController Setup(BattleFlowTestEnv env)
        {
            env.ConfigureAllyAction(0, "SK_Normal_Bennett", "SE_Normal_Bennett", "Physical", ".01*TotalATK,0", 0);
            env.DataManager.SkillLevelDict[$"{BattleFlowTestEnv.AllyID}_0"][1].Hits2 = ".01*TotalATK,0";
            env.ConfigureAllyAction(3, "SK_Burst_Bennett", "SE_Burst_BennettDamage", "Pyro", ".01*TotalATK,2", 0);
            var damage = env.DataManager.SkillEffectDict["SE_Burst_BennettDamage"];
            damage.TargetNumber = 5; damage.TargetConsecutive = 0; damage.TargetConsecutiveSet = false;
            env.DataManager.CharacterSkillDict[BattleFlowTestEnv.AllyID].Last().EnergyUsed = 60;
            env.DataManager.SkillEffectDict["SE_Burst_BennettRecipients"] = new SkillEffectData
            { SkillEffectID2 = "SE_Burst_BennettRecipients", EffectIndex = 1, EffectType = "ApplyStatus",
                Param1 = BennettScriptHooks.Encouraged, TargetType = "Allies", TargetNumber = 2,
                TargetConsecutive = 1, TargetConsecutiveSet = true };
            env.DataManager.SkillEffectDict["SE_Burst_BennettField"] = new SkillEffectData
            { SkillEffectID2 = "SE_Burst_BennettField", EffectIndex = 2, EffectType = "ApplyStatus",
                Param1 = BennettScriptHooks.Field, TargetType = "Self", Duration = 3, AddInPhase = 2, TriggerPhase = 3 };
            Main(env, 1, BennettScriptHooks.Field); Main(env, 2, BennettScriptHooks.EndPhase);
            Main(env, 3, BennettScriptHooks.Encouraged, "Buff", "ATKBonusFlat");
            var pyro = Main(env, 4, "ST_Bennett_C6Pyro", "Buff", "DMGBonus"); pyro.ApplyElementType = "Pyro";
            Main(env, 5, "ST_Bennett_C6Enchant", "Enchant"); Main(env, 8, BennettScriptHooks.Follow);
            Main(env, 6, "ST_Bennett_T2Cost");
            Effect(env, 1, 1, "STE_Bennett_BindEnemyEnd", "BindStatus", BennettScriptHooks.EndPhase).TriggerPhase = 6;
            Effect(env, 1, 2, "STE_Bennett_BindT2Cost", "BindStatus", "ST_Bennett_T2Cost").ScriptHook = "Check(1020_T2)";
            Effect(env, 6, 1, "STE_Bennett_T2Cost", "APCost", "*0.5").Param2 = "Skill";
            var heal = Effect(env, 1, 3, "STE_Bennett_Heal", "Heal", null, "Allies");
            heal.TargetOverride = "Bennett_InjuredAllies()"; heal.ScriptHook = "Bennett_FieldAlive()";
            var contact = Effect(env, 1, 4, "STE_Bennett_ApplyPyro", "ApplyElement", "1", "Allies");
            contact.Element = "Pyro"; contact.TargetSelect = "-1"; contact.ScriptHook = "Bennett_FieldAlive()";
            foreach (string id in new[] { BennettScriptHooks.Field, BennettScriptHooks.EndPhase })
                env.DataManager.StatusActionDict[id] = new List<StatusActionData>
                { new StatusActionData { StatusID = env.DataManager.StatusMainDict[id].StatusID, StatusID2 = id,
                    ActionType = "OnItsTurn", Param1 = "STE_Bennett_Heal;STE_Bennett_ApplyPyro" } };
            var attack = Effect(env, 3, 1, "STE_Bennett_Attack", "Buff");
            attack.ScriptHook = "Bennett_Encouraged()"; attack.DamageHook = "Bennett_BaseAttack()";
            Effect(env, 3, 2, "STE_Bennett_BindC6Pyro", "BindStatus", "ST_Bennett_C6Pyro").ScriptHook = "Check(1020_C6)";
            var enchant = Effect(env, 3, 3, "STE_Bennett_BindC6Enchant", "BindStatus", "ST_Bennett_C6Enchant");
            enchant.Element = "Pyro"; enchant.Param2 = "1"; enchant.Param3 = "0";
            enchant.ScriptHook = "Check(1020_C6);Bennett_EnchantWeapon()";
            Effect(env, 4, 1, "STE_Bennett_C6Pyro", "Buff", ".15");
            var follow = Effect(env, 8, 1, "STE_Bennett_C4Damage", "Damage", "1.35*TotalATK,1", "Enemy");
            follow.Element = "Pyro"; follow.TargetOverride = "Bennett_C4Target()";
            Effect(env, 8, 2, "STE_Bennett_C4End", "RemoveStatus", BennettScriptHooks.Follow);
            env.DataManager.StatusActionDict[BennettScriptHooks.Follow] = new List<StatusActionData>
            { new StatusActionData { StatusID = 4102008, StatusID2 = BennettScriptHooks.Follow, ActionType = "OnTrigger",
                ScriptHook = "Bennett_C4Action()", Param1 = "STE_Bennett_C4Damage;STE_Bennett_C4End" } };
            Level(env, "STE_Bennett_Attack", ".56", ".602"); Level(env, "STE_Bennett_Heal", "50", "60");
            env.CreateMinimalBattle(); env.Ally.Entity.EntityID = 1020; env.Ally.Entity.CritRate = 0;
            env.CreateAlly(2); env.CreateAlly(3); env.CreateAlly(4);
            env.CreateEnemy(3); env.CreateEnemy(5);
            env.Manager.StartBattle(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.AllyAction);
            env.Ally.Entity.MaxEnergy = 60; env.Ally.Entity.CurrentEnergy = 60;
            BattleRandom.SetSource(new SequenceBattleRandomSource());
            return env.Manager.gameObject.AddComponent<BattleInputController>();
        }
        private static void Burst(BattleInputController input, params int[] positions)
        {
            input.UIEnterSelectSkill(3);
            input.Selector.CurrentSelection = new List<int>(positions); input.UIConfirmSelection();
            Assert.That(input.IsSelecting, Is.False, "只选鼓舞对象，不再询问敌方或自身");
        }
        private static float Attack(BattleEntity entity) => entity.GetStatusATKBonusFlat("", "", -1, "");

        [Test]
        public void Burst_CancelOnlyAlliedChoice_IsUncommitted_ThenAutomaticDamageDoesNotBorrowAlliedSlots()
        {
            using var env = new BattleFlowTestEnv(); var input = Setup(env); var owner = env.Ally.Entity;
            var first = env.Ally.GetFirstSelectEffect(3);
            Assert.That(first.SkillEffectID2, Is.EqualTo("SE_Burst_BennettRecipients"));
            Assert.That(first.TargetType, Is.EqualTo("Allies")); Assert.That(first.TargetNumber, Is.EqualTo(2));
            input.UIEnterSelectSkill(3); Assert.That(input.IsSelecting, Is.True);
            input.UICancelSelection(); Assert.That(owner.CurrentEnergy, Is.EqualTo(60));
            Assert.That(owner.HasStatus(BennettScriptHooks.Field), Is.False);
            Assert.That(env.Manager.Enemies.All(e => e.Entity.CurrentHP == 1000), Is.True);
            var hitPositions = new List<int>();
            using var listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, hit) =>
            { if (hit.Source.SourceEffectID == "SE_Burst_BennettDamage") hitPositions.Add(hit.Subject.SlotPosition); }, 0);
            Burst(input, 2, 3);
            Assert.That(owner.CurrentEnergy, Is.Zero); Assert.That(hitPositions, Is.EquivalentTo(new[] { 1, 3, 5 }));
            var field = owner.GetStatus(BennettScriptHooks.Field);
            Assert.That(field.BoundStatuses.Count(s => s.StatusID2 == BennettScriptHooks.Encouraged), Is.EqualTo(2));
            Assert.That(owner.HasStatus(BennettScriptHooks.Encouraged), Is.False);
            Assert.That(env.Manager.GetEntityByPosition(BattleSide.Ally, 4).HasStatus(BennettScriptHooks.Encouraged), Is.False);
        }

        [Test]
        public void Encouragement_ReadsLiveBurstLevelAndWhiteAttack_StrictThresholdAndC1()
        {
            using var env = new BattleFlowTestEnv(); var input = Setup(env); var owner = env.Ally.Entity;
            owner.TotalATK = 100; owner.WeaponATK = 50; owner.CharacterATKBonus = 2;
            Burst(input, 2, 3); var recipient = env.Manager.GetEntityByPosition(BattleSide.Ally, 2);
            Assert.That(Attack(recipient), Is.EqualTo(84).Within(.001));
            recipient.CurrentHP = .7f * recipient.GetFinalHP(); Assert.That(Attack(recipient), Is.Zero);
            owner.ConstellationLevel = 1; Assert.That(Attack(recipient), Is.EqualTo(114).Within(.001));
            env.Ally.SkillLevels[3] = 2; owner.WeaponATK = 100;
            Assert.That(Attack(recipient), Is.EqualTo(160.4f).Within(.001));
            owner.RemoveStatus(BennettScriptHooks.Field);
            Assert.That(Attack(recipient), Is.Zero); Assert.That(recipient.HasStatus(BennettScriptHooks.Encouraged), Is.False);
        }

        [Test]
        public void C6_BuffsBothRecipients_EnchantOnlyMelee_RefreshAndRemovalOwnConcreteBindings()
        {
            using var env = new BattleFlowTestEnv(); var input = Setup(env); var owner = env.Ally.Entity;
            owner.ConstellationLevel = 6;
            var sword = env.Manager.GetEntityByPosition(BattleSide.Ally, 2); sword.WeaponType = 0;
            var catalyst = env.Manager.GetEntityByPosition(BattleSide.Ally, 3); catalyst.WeaponType = 4;
            Burst(input, 2, 3); var old = owner.GetStatus(BennettScriptHooks.Field);
            Assert.That(sword.HasStatus("ST_Bennett_C6Enchant"), Is.True);
            Assert.That(catalyst.HasStatus("ST_Bennett_C6Enchant"), Is.False);
            Assert.That(sword.HasStatus("ST_Bennett_C6Pyro") && catalyst.HasStatus("ST_Bennett_C6Pyro"), Is.True);
            owner.CurrentEnergy = 60; Burst(input, 1, 2);
            Assert.That(old.IsActive, Is.False); Assert.That(catalyst.HasStatus(BennettScriptHooks.Encouraged), Is.False);
            Assert.That(catalyst.HasStatus("ST_Bennett_C6Pyro"), Is.False);
            Assert.That(sword.HasStatus("ST_Bennett_C6Enchant"), Is.True, "清旧父不能删掉新父的同ID实例");
            owner.RemoveStatus(BennettScriptHooks.Field);
            Assert.That(sword.HasStatus("ST_Bennett_C6Enchant"), Is.False);
            Assert.That(owner.HasStatus(BennettScriptHooks.EndPhase), Is.False);
        }

        [UnityTest]
        public IEnumerator Field_HealsWholePartyBelowThreshold_AtBothEndPhases_NotOnlySelectedPair()
            => HookTestEnv.AwaitTask(FieldAsync);
        private async Task FieldAsync()
        {
            using var env = new BattleFlowTestEnv(); var input = Setup(env); var owner = env.Ally.Entity;
            Burst(input, 2, 3); owner.CurrentHP = 100;
            var other = env.Manager.GetEntityByPosition(BattleSide.Ally, 4); other.CurrentHP = 100;
            var exact = env.Manager.GetEntityByPosition(BattleSide.Ally, 2); exact.CurrentHP = .7f * exact.GetFinalHP();
            float before = exact.CurrentHP;
            env.Manager.EndAllyTurn(); env.Flow.AdvanceToPhase(env.Manager, TurnPhase.EnemyPreTurn);
            await BattleExecution.Run(BattleExecution.Flush);
            Assert.That(owner.CurrentHP, Is.EqualTo(150)); Assert.That(other.CurrentHP, Is.EqualTo(150));
            Assert.That(exact.CurrentHP, Is.EqualTo(before));
            env.Flow.AdvanceToPhase(env.Manager, TurnPhase.EnemyPostTurn); await BattleExecution.Run(BattleExecution.Flush);
            Assert.That(owner.CurrentHP, Is.EqualTo(200)); Assert.That(other.CurrentHP, Is.EqualTo(200));
            Assert.That(exact.CurrentHP, Is.EqualTo(before));
        }

        [Test]
        public void Field_T2RequiresUnlockedTalent_AndDiscountUsesRealSkillSlot()
        {
            using var env = new BattleFlowTestEnv(); var input = Setup(env); var owner = env.Ally.Entity;
            var skill = new SkillMainData { APCost = 30 };
            Burst(input, 2, 3);
            Assert.That(APCostResolver.ForSkill(owner, skill, 2), Is.EqualTo(30));
            Assert.That(ScriptHookEvaluator.Evaluate("Bennett_SelfAirborne()", owner), Is.True);
            owner.RemoveStatus(BennettScriptHooks.Field); owner.CurrentEnergy = 60;
            // 单独隔离天赋效果；实际突破解锁门槛由CharacterAscensionRules公共测试覆盖。
            env.DataManager.TalentDict[1020] = new List<TalentData>
            { new TalentData { CharacterID = 1020, TalentIndex = 2, UnlockAfter = 0 } };
            Burst(input, 2, 3);
            Assert.That(APCostResolver.ForSkill(owner, skill, 2), Is.EqualTo(15));
            Assert.That(APCostResolver.ForSkill(owner, skill, 0), Is.EqualTo(30));
            Assert.That(ScriptHookEvaluator.Evaluate("Bennett_SelfAirborne()", owner), Is.False);
            owner.RemoveStatus(BennettScriptHooks.Field);
            Assert.That(APCostResolver.ForSkill(owner, skill, 2), Is.EqualTo(30));
        }

        [UnityTest]
        public IEnumerator Death_RemovesFieldAndFollowAndEveryConcreteChild_WithoutExpirationEffects()
            => HookTestEnv.AwaitTask(DeathAsync);
        private async Task DeathAsync()
        {
            using var env = new BattleFlowTestEnv(); var input = Setup(env); var owner = env.Ally.Entity;
            owner.ConstellationLevel = 6; Burst(input, 2, 3);
            var main = env.DataManager.StatusMainDict[BennettScriptHooks.Follow];
            owner.AddStatus(main.StatusID2, owner, 0, 2, 0, main);
            await BattleExecution.Run(async () =>
            { owner.TakeDamage(100000, DamageSourceInfo.CreateUnknown()); await BattleExecution.Flush(); });
            Assert.That(owner.HasStatus(BennettScriptHooks.Field), Is.False);
            Assert.That(owner.HasStatus(BennettScriptHooks.EndPhase), Is.False);
            Assert.That(owner.HasStatus(BennettScriptHooks.Follow), Is.False);
            foreach (int slot in new[] { 2, 3 })
            {
                var recipient = env.Manager.GetEntityByPosition(BattleSide.Ally, slot);
                Assert.That(recipient.HasStatus(BennettScriptHooks.Encouraged), Is.False);
                Assert.That(recipient.HasStatus("ST_Bennett_C6Pyro"), Is.False);
                Assert.That(recipient.HasStatus("ST_Bennett_C6Enchant"), Is.False);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void C4_OnlyAfterWholeNormalAction_Once_AndMissConsumesWithoutRetarget(bool miss)
        {
            using var env = new BattleFlowTestEnv(); Setup(env); var owner = env.Ally.Entity;
            if (miss) env.Ally.SetSkillEffects("SK_Normal_Bennett", new List<SkillEffectData>
            { new SkillEffectData { SkillEffectID2 = "SE_Normal_Bennett", EffectType = "Damage", Element = "Physical",
                DamageType = "Normal", ScriptHook = "OnField(999999)", TargetType = "Enemy", TargetNumber = 1 } });
            var main = env.DataManager.StatusMainDict[BennettScriptHooks.Follow];
            owner.AddStatus(main.StatusID2, owner, 0, 2, 0, main);
            var order = new List<string>();
            using var hits = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, hit) =>
            { if (hit.Source.SourceEntity == owner) { order.Add(hit.Source.SourceEffectID);
                if (hit.Source.SourceEffectID == "STE_Bennett_C4Damage") Assert.That(hit.DamageType, Is.Null.Or.Empty); } }, 0);
            Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True);
            Assert.That(order, Is.EqualTo(miss ? new string[0] : new[] { "SE_Normal_Bennett", "SE_Normal_Bennett", "STE_Bennett_C4Damage" }));
            Assert.That(owner.HasStatus(BennettScriptHooks.Follow), Is.False);
        }

        [Test]
        public void C4_DeadPrimaryTargetIsNotReplacedByAnotherLivingEnemy()
        {
            using var env = new BattleFlowTestEnv(); Setup(env); var owner = env.Ally.Entity;
            env.Enemy.Entity.CurrentHP = .001f;
            var main = env.DataManager.StatusMainDict[BennettScriptHooks.Follow];
            owner.AddStatus(main.StatusID2, owner, 0, 2, 0, main);
            int extra = 0;
            using var hits = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, hit) =>
            { if (hit.Source.SourceEffectID == "STE_Bennett_C4Damage") extra++; }, 0);
            env.Ally.ForcedTargetPositions = new List<int> { 1 };
            Assert.That(env.Manager.UseNormalAttackBySlot(0), Is.True);
            Assert.That(env.Enemy.Entity.IsAlive, Is.False); Assert.That(extra, Is.Zero);
            Assert.That(owner.HasStatus(BennettScriptHooks.Follow), Is.False);
        }
    }
}
