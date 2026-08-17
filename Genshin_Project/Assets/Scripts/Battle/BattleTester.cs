using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 测试入口（临时场景脚本，正式版移除）。
/// 职责：按 StageConfig.json 创建我方/敌方实体并启动战斗。
/// 输入控制（键盘 Q/W/E/1/A/D/空格/X/Esc + 目标选择状态机 + UI操作接口）
/// 已迁移至 BattleInputController（正式组件，最终保留）。
/// 用法：场景中新建一个空物体，挂上本脚本（自动附带 BattleInputController），点 Play。
///  - 自动创建我方角色（安柏 1009，1级）
///  - 敌方：木桩(20000)在位置3，丘丘人(20001)在位置2和4，全1级
///  - 自动调用 BattleManager.StartBattle() 启动六阶段循环
/// </summary>
public class BattleTester : MonoBehaviour
{
    /// <summary>编辑器热加载请求：StageBuilder 置 true 后本组件下一帧重建战斗</summary>
    public static bool ReloadRequest = false;

    [Header("测试配置（由 Tools/关卡编辑器 生成的 StageConfig.json 驱动）")]
    public TextAsset StageConfigJson;   // 可留空：留空时自动从 Resources/StageConfig.json 读取

    void Start()
    {
        // 输入控制：测试时自动挂载（正式场景手动挂 BattleInputController）
        if (GetComponent<BattleInputController>() == null)
            gameObject.AddComponent<BattleInputController>();

        // 确保 DataManager 已初始化
        if (DataManager.Instance == null)
        {
            var dmGO = new GameObject("DataManager");
            dmGO.AddComponent<DataManager>();
        }

        var dm = DataManager.Instance;
        if (dm == null)
        {
            Debug.LogError("DataManager.Instance is null after AddComponent. Check DataManager.Awake/Init.");
            return;
        }

        // 确保 BattleManager 存在
        if (BattleManager.Instance == null)
            gameObject.AddComponent<BattleManager>();

        var bm = BattleManager.Instance;
        bm.UnregisterAll();

        // ---- 读取关卡配置（编辑器工具生成的 JSON，无则用默认） ----
        StageSetupData cfg = LoadStageConfig();
        ReloadStage(cfg);
    }

    void Update()
    {
        // 编辑器热加载：检测到请求标志就重读 JSON 并重建战斗
        if (ReloadRequest)
        {
            ReloadRequest = false;
            var cfg = LoadStageConfig();
            LogManager.Log(LogCategory.Data, "检测到编辑器热加载请求，重建战斗");
            ReloadStage(cfg);
        }
    }

    /// <summary>
    /// 销毁现有战斗实体并按配置重建（编辑器热加载/首次启动共用）
    /// </summary>
    public void ReloadStage(StageSetupData cfg)
    {
        var bm = BattleManager.Instance;
        if (bm == null) return;

        // 销毁旧实体
        foreach (var ally in new List<CharacterBattleController>(bm.Allies))
        {
            if (ally != null && ally.gameObject != null)
                Destroy(ally.gameObject);
        }
        foreach (var enemy in new List<EnemyBattleController>(bm.Enemies))
        {
            if (enemy != null && enemy.gameObject != null)
                Destroy(enemy.gameObject);
        }
        bm.UnregisterAll();

        // ---- 创建我方角色 ----
        for (int i = 0; i < cfg.Allies.Count && i < 4; i++)
        {
            var a = cfg.Allies[i];
            var allyGO = new GameObject($"Ally_{a.CharacterID}");
            var allyCtrl = allyGO.AddComponent<CharacterBattleController>();
            allyCtrl.Init(a.CharacterID, a.Level);
            // 技能等级（独立升级，0=普攻 1=重击 2=战技 3=爆发）
            if (a.SkillLevels != null && a.SkillLevels.Length >= 4)
                Array.Copy(a.SkillLevels, allyCtrl.SkillLevels, 4);

            // 天赋/命座/突破初始化（独立玩法层：按档案应用 Modifiers，开场状态/技能等级/效果注入）
            CharacterBuildApplier.ApplyBuild(allyCtrl, CharacterProfile.FromStageAlly(a));

            allyCtrl.IsActive = (i == 0);
            if (allyCtrl.Entity != null)
            {
                allyCtrl.Entity.Side = BattleSide.Ally;
                allyCtrl.Entity.SlotPosition = i + 1;  // 先设位置再注册，避免 AssignToField 用默认0
                allyCtrl.Entity.ConstellationLevel = a.Constellation;  // 命座（效果实装前仅存储）
                allyCtrl.Entity.IsAscended = a.IsAscended;             // 突破标记
                LogManager.Log(LogCategory.Ally, $"{a.CharacterID} 分配位置 pos={i + 1}");
            }
            bm.RegisterAlly(allyCtrl);
        }

        // ---- 创建敌方（按配置的槽位） ----
        foreach (var e in cfg.Enemies)
        {
            SpawnEnemy(bm, e.EnemyID, $"Enemy_{e.EnemyID}", e.Slot, e.Level);
        }

        // ---- 初始能量 ----
        ApplyInitialEnergy(cfg);

        // 重置战斗并启动（热加载时旧协程可能还在跑）
        bm.ResetBattle();
        bm.StartBattle();
    }

    // 读取关卡配置：优先用 Inspector 拖的 TextAsset，否则读 Resources/StageConfig
    private StageSetupData LoadStageConfig()
    {
        TextAsset asset = StageConfigJson;
        if (asset == null)
            asset = Resources.Load<TextAsset>("StageConfig");
        if (asset == null)
        {
            // 没有编辑器保存的关卡时，用默认配置继续（不会阻断战斗）
            LogManager.Log(LogCategory.Data, "未找到关卡配置(StageConfig.json)，使用默认配置：木桩+2丘丘人");
            return StageSetupData.CreateDefault();
        }

        var cfg = JsonUtility.FromJson<StageSetupData>(asset.text);
        if (cfg == null) return StageSetupData.CreateDefault();
        if (cfg.Allies == null) cfg.Allies = new List<StageAllySetup>();
        if (cfg.Enemies == null) cfg.Enemies = new List<StageEnemySetup>();
        return cfg;
    }

    // 初始能量：Percent=MaxEnergy百分比 / Fixed=固定值
    private void ApplyInitialEnergy(StageSetupData cfg)
    {
        var allies = BattleManager.Instance != null ? BattleManager.Instance.Allies : null;
        if (allies == null) return;

        foreach (var ally in allies)
        {
            if (ally == null || ally.Entity == null) continue;
            float max = ally.Entity.MaxEnergy;
            if (max <= 0) continue;

            if (cfg.InitialEnergyMode == "Fixed")
                ally.Entity.CurrentEnergy = Mathf.RoundToInt(cfg.InitialEnergyValue);
            else
                ally.Entity.CurrentEnergy = Mathf.RoundToInt(max * Mathf.Clamp01(cfg.InitialEnergyValue / 100f));
        }
    }

    private void SpawnEnemy(BattleManager bm, int enemyID, string name, int position, int level = 1)
    {
        var enemyGO = new GameObject(name);
        var enemyCtrl = enemyGO.AddComponent<EnemyBattleController>();
        enemyCtrl.InitEnemy(enemyID, level);
        if (enemyCtrl.Entity != null)
        {
            enemyCtrl.Entity.Side = BattleSide.Enemy;
            enemyCtrl.Entity.SlotPosition = position;   // 先设位置再注册，避免 AssignToField 用默认0
            LogManager.Log(LogCategory.Enemy, $"{enemyID} 分配位置 pos={position}");
        }
        bm.RegisterEnemy(enemyCtrl);
    }
}
