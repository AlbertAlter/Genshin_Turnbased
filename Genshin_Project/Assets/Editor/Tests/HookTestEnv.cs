using System.Reflection;
using UnityEngine;

namespace GenshinTurnBased.Tests.EditMode
{
    /// <summary>
    /// 状态钩子测试共享环境（2026-08-19）：
    ///  - HookTestDataManager：不自动加载配表、不注册单例的 DataManager 子类（沿用 AtomicTestDataManager 模式）
    ///  - 通过反射注入/清除 DataManager.Instance（测试内单例，TearDown 必清，避免污染其他测试）
    ///  - 战场/实体构造辅助
    /// </summary>
    internal static class HookTestEnv
    {
        public static void SetDataManagerSingleton(DataManager instance)
        {
            typeof(Singleton<DataManager>)
                .GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)
                .SetValue(null, instance);
        }

        public static void SetBattleManagerSingleton(BattleManager instance)
        {
            typeof(BattleManager)
                .GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)
                .SetValue(null, instance);
        }

        public static HookTestDataManager CreateDataManager(bool loadRealData = true)
        {
            var go = new GameObject("HookTest_DataManager");
            var dm = go.AddComponent<HookTestDataManager>();
            if (loadRealData) dm.ReloadAllData();
            SetDataManagerSingleton(dm);
            return dm;
        }

        public static void DestroyDataManager(HookTestDataManager dm)
        {
            SetDataManagerSingleton(null);
            if (dm != null) Object.DestroyImmediate(dm.gameObject);
        }

        public static BattleManager CreateBattleManager()
        {
            // 先清掉可能由其他 EditMode 测试遗留的单例引用。否则 AddComponent
            // 会在 Awake 的单例守卫中销毁新对象，随后即使反射回填 Instance，
            // Unity 的已销毁对象仍会让 TakeDamage 跳过 CheckBattleEnd。
            SetBattleManagerSingleton(null);
            var go = new GameObject("HookTest_BattleManager");
            var bm = go.AddComponent<BattleManager>();
            // 场景中若已有存活 BattleManager，Awake 单例守卫会 Destroy 新实例：测试必须强制接管单例，
            // 否则生产代码走 BattleManager.Instance 的路径（AP池/目标解析/阶段桶）会拿到空引用。
            SetBattleManagerSingleton(bm);
            bm.ResetBattle(); // 保证 APManager 就绪且 AP 回满、阶段桶/钩子清空
            return bm;
        }

        /// <summary>测试收尾时清除 BattleManager 单例，避免跨测试残留指向已销毁对象。</summary>
        public static void ClearBattleManagerSingleton()
        {
            SetBattleManagerSingleton(null);
        }

        /// <summary>创建并初始化角色控制器（Init 读 DataManager 真实配表），注册到战场，AP 重置。</summary>
        public static CharacterBattleController CreateCharacter(int characterID, int level, int slotPosition, BattleManager bm)
        {
            var go = new GameObject($"HookTest_Char_{characterID}");
            var ctrl = go.AddComponent<CharacterBattleController>();
            ctrl.Init(characterID, level);
            ctrl.Entity.SlotPosition = slotPosition;
            ctrl.Entity.Side = BattleSide.Ally;
            bm.RegisterAlly(ctrl);
            if (bm.APManager != null) bm.APManager.ResetAP();
            return ctrl;
        }

        /// <summary>创建敌人（InitEnemy 读 DataManager 真实配表），注册到战场；hp&lt;0 时用配表默认值。</summary>
        public static BattleEntity CreateEnemy(int enemyID, int level, int slotPosition, BattleManager bm, float hp = -1f)
        {
            var go = new GameObject($"HookTest_Enemy_{enemyID}");
            var ctrl = go.AddComponent<EnemyBattleController>();
            ctrl.InitEnemy(enemyID, level);
            ctrl.Entity.SlotPosition = slotPosition;
            ctrl.Entity.Side = BattleSide.Enemy;
            if (hp >= 0f)
            {
                ctrl.Entity.TotalHP = hp;
                ctrl.Entity.CurrentHP = hp;
            }
            bm.RegisterEnemy(ctrl);
            return ctrl.Entity;
        }

        /// <summary>把状态实例直接挂到实体（不走 AddStatus，避免额外副作用），返回实例。</summary>
        public static StatusInstance AttachStatus(BattleEntity entity, string statusID2, BattleEntity caster, int duration)
        {
            var dm = DataManager.Instance;
            StatusMainData mainData = null;
            if (dm != null && dm.StatusMainDict != null)
                dm.StatusMainDict.TryGetValue(statusID2, out mainData);

            var inst = new StatusInstance
            {
                StatusID2 = statusID2,
                StackCount = 1,
                RemainingPhaseCount = duration,
                Caster = caster,
                MainData = mainData,
                IsActive = true,
                ApplyOrder = ++BattleEntity._applyOrderCounter
            };
            entity.StatusDict[statusID2] = inst;
            return inst;
        }

        /// <summary>向 DataManager 注入测试用状态行动行（返回注入的实例）。
        /// 默认 MaxTimePerLife=5：同时覆盖全场次数上限；LifeTriggerCount 本身会记录所有实际触发。</summary>
        public static StatusActionData InjectStatusAction(
            HookTestDataManager dm,
            string statusID2,
            string actionType,
            string param1,
            string scriptHook,
            string hitSource = "",
            int maxTimePerTurn = 0,
            int maxTimePerLife = 5,
            int cooldown = 0)
        {
            var act = new StatusActionData
            {
                StatusID = 9900000,
                StatusID2 = statusID2,
                // 测试辅助保留旧的 OnHit 调用写法，但注入当前 Status_Action 结构：
                // 命中事件统一是 OnTrigger，过滤条件写入 OnHit，不再使用废弃的 HitSource。
                ActionType = actionType == "OnHit" ? "OnTrigger" : actionType,
                Param1 = param1,
                Param2 = string.Empty,
                Param3 = string.Empty,
                MaxTimePerTurn = maxTimePerTurn,
                MaxTimePerLife = maxTimePerLife,
                Cooldown = cooldown,
                OnHit = actionType == "OnHit" ? hitSource : string.Empty,
                ScriptHook = scriptHook
            };
            if (!dm.StatusActionDict.TryGetValue(statusID2, out var list))
            {
                list = new System.Collections.Generic.List<StatusActionData>();
                dm.StatusActionDict[statusID2] = list;
            }
            list.Add(act);
            return act;
        }

        /// <summary>向 DataManager 注入测试用状态效果（返回注入的实例）。</summary>
        public static StatusEffectData InjectStatusEffect(
            HookTestDataManager dm,
            string statusEffectID2,
            string effectType,
            string param1,
            string param2 = "",
            string element = "None",
            string targetType = "Self",
            int duration = 1)
        {
            var eff = new StatusEffectData
            {
                StatusEffectID = 990000101,
                StatusEffectID2 = statusEffectID2,
                EffectIndex = 0,
                EffectType = effectType,
                Element = element,
                DamageType = string.Empty,
                Duration = duration,
                AddInPhase = 0,
                TriggerPhase = 0,
                Param1 = param1,
                Param2 = param2,
                Param3 = string.Empty,
                TargetType = targetType,
                TargetSelect = string.Empty,
                TargetConsecutive = 0,
                TargetOverride = string.Empty,
                ScriptHook = string.Empty
            };
            dm.StatusEffectDict[statusEffectID2] = eff;
            return eff;
        }

        public static void ResetStaticState()
        {
            KillHookSystem.ClearAll();
            PreDamageHookSystem.ClearAll();
            ReactionStateSystem.ClearAll();
            BattleEntity._applyOrderCounter = 0;
            ReactionResolver.ResetSession();
        }
    }

    /// <summary>EditMode 测试用 DataManager：不自动加载配表、不注册全局单例。</summary>
    internal sealed class HookTestDataManager : DataManager
    {
        protected override void Awake()
        {
            // EditMode 测试：不注册全局单例，也不自动读取真实配表（由测试显式调用 ReloadAllData）。
        }
    }
}
