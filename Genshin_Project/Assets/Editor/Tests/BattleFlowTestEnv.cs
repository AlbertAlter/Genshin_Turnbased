using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace GenshinTurnBased.Tests.EditMode
{
    /// <summary>
    /// 战斗流程验收测试的独立环境。只负责构造确定性的配表数据、单位、战场和 BattleManager；
    /// 测试行为仍必须从 BattleManager 的公开战斗入口进入。
    /// </summary>
    internal sealed class BattleFlowTestEnv : IDisposable
    {
        internal const int AllyID = 990001;
        internal const int EnemyID = 990002;

        private readonly List<GameObject> _ownedObjects = new List<GameObject>();

        public BattleFlowTestDataManager DataManager { get; }
        public BattleManager Manager { get; }
        public ManualBattleFlowScheduler Scheduler { get; }
        public BattleFlowTestDriver Flow { get; }
        public CharacterBattleController Ally { get; private set; }
        public EnemyBattleController Enemy { get; private set; }

        public BattleFlowTestEnv(float phaseInterval = 0f)
        {
            SetBattleManagerSingleton(null);
            SetDataManagerSingleton(null);
            ResetStaticSystems();

            DataManager = CreateObject("BattleFlow_DataManager")
                .AddComponent<BattleFlowTestDataManager>();
            SetDataManagerSingleton(DataManager);
            AddMinimalDeterministicData();

            Manager = CreateObject("BattleFlow_BattleManager")
                .AddComponent<BattleManager>();
            SetBattleManagerSingleton(Manager);
            // Pure EditMode does not guarantee MonoBehaviour.Awake. Install the same
            // runtime display adapter explicitly so reaction display state is deterministic.
            ReactionStateSystem.SetDisplayAdapter(new StatusDataReactionStateDisplayAdapter());
            Scheduler = new ManualBattleFlowScheduler();
            Flow = new BattleFlowTestDriver(Scheduler);
            Manager.SetFlowScheduler(Scheduler);
            Manager.ResetBattle();
            Manager.PhaseInterval = phaseInterval;
        }

        public void CreateMinimalBattle()
        {
            Ally = CreateObject("BattleFlow_Ally")
                .AddComponent<CharacterBattleController>();
            Ally.Init(AllyID, 1);
            Ally.IsActive = true;
            Ally.Entity.Side = BattleSide.Ally;
            Ally.Entity.SlotPosition = 1;
            Manager.RegisterAlly(Ally);

            Enemy = CreateObject("BattleFlow_Enemy")
                .AddComponent<EnemyBattleController>();
            Enemy.InitEnemy(EnemyID, 1);
            Enemy.Entity.Side = BattleSide.Enemy;
            Enemy.Entity.SlotPosition = 1;
            Manager.RegisterEnemy(Enemy);
        }

        public void ConfigureAllyAction(
            int skillType,
            string skillID2,
            string effectID2,
            string element,
            string hitData,
            int apCost = 20,
            int cooldown = 0,
            string effectType = "Damage",
            string param1 = "",
            int duration = 0,
            int addInPhase = 0,
            int triggerPhase = 0,
            string targetType = "Enemy")
        {
            var attributes = DataManager.CharacterAttributesDict[AllyID];
            if (skillType == 0) attributes.NormalAttackID = skillID2;
            else if (skillType == 1) attributes.HeavyAttackID = skillID2;
            else if (skillType == 2) attributes.ElementalSkillID = skillID2;
            else attributes.ElementalBurstID = skillID2;

            DataManager.CharacterSkillDict[AllyID].Add(new SkillMainData
            {
                SkillID = Math.Abs(skillID2.GetHashCode()),
                SkillID2 = skillID2,
                SkillName = skillID2,
                APCost = apCost,
                Cooldown = cooldown,
                MaxCharge = -1,
                ActionType = skillType == 0 ? "Normal" : skillType == 1 ? "Heavy" : skillType == 2 ? "Skill" : "Burst"
            });
            DataManager.SkillEffectDict[effectID2] = new SkillEffectData
            {
                SkillEffectID = Math.Abs(effectID2.GetHashCode()),
                SkillEffectID2 = effectID2,
                EffectIndex = 0,
                EffectType = effectType,
                Element = element,
                DamageType = skillType == 0 ? "Normal" : skillType == 1 ? "Heavy" : skillType == 2 ? "Skill" : "Burst",
                Param1 = param1,
                Duration = duration,
                AddInPhase = addInPhase,
                TriggerPhase = triggerPhase,
                TargetType = targetType,
                TargetNumber = 1,
                TargetConsecutive = 1,
                TargetConsecutiveSet = true
            };
            if (!string.IsNullOrEmpty(hitData))
            {
                DataManager.SkillLevelDict[$"{AllyID}_{skillType}"] = new Dictionary<int, SkillLevelData>
                {
                    [1] = new SkillLevelData
                    {
                        CharacterID = AllyID,
                        SkillType = skillType,
                        SkillLevel = 1,
                        ParamID = effectID2,
                        Hits1 = hitData
                    }
                };
            }
        }

        public void ConfigureStatus(
            string statusID2,
            int maxStack = 1,
            string whenMax = "0",
            string actionType = null,
            string actionEffectID2 = null)
        {
            DataManager.StatusMainDict[statusID2] = new StatusMainData
            {
                StatusID = Math.Abs(statusID2.GetHashCode()),
                StatusID2 = statusID2,
                StatusName = statusID2,
                MaxStack = maxStack,
                WhenMax = whenMax
            };
            if (!string.IsNullOrEmpty(actionType))
            {
                DataManager.StatusActionDict[statusID2] = new List<StatusActionData>
                {
                    new StatusActionData
                    {
                        StatusID = Math.Abs(statusID2.GetHashCode()),
                        StatusID2 = statusID2,
                        ActionType = actionType,
                        Param1 = actionEffectID2
                    }
                };
            }
        }

        public EnemyBattleController CreateEnemy(int position, float hp = 1000f)
        {
            var enemy = CreateObject($"BattleFlow_Enemy_{position}")
                .AddComponent<EnemyBattleController>();
            enemy.InitEnemy(EnemyID, 1);
            enemy.Entity.Side = BattleSide.Enemy;
            enemy.Entity.SlotPosition = position;
            enemy.Entity.TotalHP = hp;
            enemy.Entity.CurrentHP = hp;
            Manager.RegisterEnemy(enemy);
            return enemy;
        }

        public CharacterBattleController CreateAlly(
            int position,
            bool isActive = false,
            float hp = 1000f)
        {
            var ally = CreateObject($"BattleFlow_Ally_{position}")
                .AddComponent<CharacterBattleController>();
            ally.Init(AllyID, 1);
            ally.IsActive = isActive;
            ally.Entity.Side = BattleSide.Ally;
            ally.Entity.SlotPosition = position;
            ally.Entity.TotalHP = hp;
            ally.Entity.CurrentHP = hp;
            Manager.RegisterAlly(ally);
            return ally;
        }

        public void ConfigureEnemyDamageSkill(
            float multiplier = 1f,
            string element = "None",
            float elementAmount = 0f)
        {
            const int skillID = 990201;
            const string skillID2 = "SK_Enemy_BattleFlow";
            const string effectID2 = "SE_Enemy_BattleFlow";
            DataManager.EnemySkillMainDict[skillID] = new EnemySkillMainData
            {
                EnemySkillID = skillID,
                EnemySkillID2 = skillID2,
                EnemySkillName = skillID2,
                Cooldown = 0,
                UsePerTurn = 1
            };
            DataManager.EnemySkillEffectDict[effectID2] = new EnemySkillEffectData
            {
                SkillEffectID = skillID * 100 + 1,
                SkillEffectID2 = effectID2,
                EffectIndex = 0,
                EffectType = "Damage",
                Element = element,
                DamageType = "Normal",
                // TargetType 是相对施法者定义；敌人的 Enemy 即我方。
                TargetType = "Enemy",
                TargetNumber = 1,
                TargetConsecutive = 1
            };
            DataManager.EnemySkillParamDict[effectID2] = new EnemySkillParamData
            {
                SkillEffectID = skillID * 100 + 1,
                SkillEffectID2 = effectID2,
                EffectType = "Damage",
                Hits1 = $"{multiplier},0,{elementAmount}"
            };
            DataManager.EnemyAIDict[EnemyID] = new List<EnemyAIData>
            {
                new EnemyAIData { EnemyID = EnemyID, PatternID = 1, PatternType = "Weighted" }
            };
            DataManager.EnemyAIRuleDict[EnemyID] = new List<EnemyAIRuleData>
            {
                new EnemyAIRuleData
                {
                    EnemyID = EnemyID,
                    PatternID = 1,
                    PatternType = "Weighted",
                    SkillID = skillID,
                    SkillIndex = 1,
                    Weight = 1
                }
            };
        }

        public void Dispose()
        {
            if (Manager != null)
                Manager.ResetBattle();
            if (Scheduler.IsRunning)
                throw new InvalidOperationException("Battle flow scheduler was not stopped by ResetBattle().");

            SetBattleManagerSingleton(null);
            SetDataManagerSingleton(null);
            ResetStaticSystems();

            for (int i = _ownedObjects.Count - 1; i >= 0; i--)
            {
                if (_ownedObjects[i] != null)
                    UnityEngine.Object.DestroyImmediate(_ownedObjects[i]);
            }
            _ownedObjects.Clear();
        }

        private GameObject CreateObject(string name)
        {
            var gameObject = new GameObject(name);
            _ownedObjects.Add(gameObject);
            return gameObject;
        }

        private void AddMinimalDeterministicData()
        {
            DataManager.CharacterAttributesDict[AllyID] = new CharacterAttributesData
            {
                CharacterID = AllyID,
                NameID = "BattleFlowAlly",
                Name = "流程测试角色",
                GrowthCurveID = 1,
                Element = "Pyro",
                BaseHP = 1000f,
                BaseATK = 100f,
                BaseDEF = 100f,
                MaxEnergy = 60,
                Poise = 100f,
                HitWeight = 100f,
                BaseRechargeRate = 1f,
                CritRate = 0f,
                CritDMG = 0f
            };
            DataManager.CharacterSkillDict[AllyID] = new List<SkillMainData>();

            DataManager.BaseHPDict[1] = 1000f;
            DataManager.BaseATK1Dict[1] = 100f;
            DataManager.ReactionLevelCoefficientDict[1] = 17.2f;
            DataManager.EnemyMainDict[EnemyID] = new EnemyMainData
            {
                EnemyID = EnemyID,
                EnemyNameID = "BattleFlowEnemy",
                EnemyName = "流程测试敌人",
                Threat = 1,
                ActsGiven = 1,
                Poise = 100f,
                ATK_Curve = 1,
                HP_coeff = 1f,
                ATK_coeff = 1f
            };
            DataManager.EnemyAIDict[EnemyID] = new List<EnemyAIData>();

            AddReactionStatus("ST_Freeze");
            AddReactionStatus("ST_SuperConduct");
            AddReactionStatus("ST_ElectroCharged");
            AddReactionStatus("ST_Burning");
            AddReactionStatus("ST_Bloom");
            AddReactionStatus("ST_Quicken");
        }

        private void AddReactionStatus(string statusID2)
        {
            DataManager.StatusMainDict[statusID2] = new StatusMainData
            {
                StatusID = Math.Abs(statusID2.GetHashCode()),
                StatusID2 = statusID2,
                StatusName = statusID2,
                MaxStack = 1,
                WhenMax = "0"
            };
        }

        private static void SetDataManagerSingleton(DataManager instance)
        {
            typeof(Singleton<DataManager>)
                .GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)
                .SetValue(null, instance);
        }

        private static void SetBattleManagerSingleton(BattleManager instance)
        {
            typeof(BattleManager)
                .GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)
                .SetValue(null, instance);
        }

        private static void ResetStaticSystems()
        {
            KillHookSystem.ClearAll();
            PreDamageHookSystem.ClearAll();
            ReactionStateSystem.ClearAll();
            ReactionStateSystem.SetDisplayAdapter(null);
            BloomCoreSystem.ClearAll();
            ReactionResolver.ResetSession();
            BloomSecondaryReactionHandler.ResetRandomIndexProvider();
            BattleEntity._applyOrderCounter = 0;
        }
    }

    internal sealed class BattleFlowTestDataManager : DataManager
    {
        protected override void Awake()
        {
            // 测试显式注入数据和单例，禁止 Awake 自动读取真实配表。
        }
    }
}
