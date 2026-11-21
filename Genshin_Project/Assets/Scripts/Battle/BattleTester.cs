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
///  - 敌方：由关卡编辑器 Stage JSON 的 EnemyID、Level、Slot 决定
///  - 自动调用 BattleManager.StartBattle() 启动六阶段循环
/// </summary>
public class BattleTester : BattleSceneRuntime
{
    /// <summary>编辑器热加载请求：StageBuilder 置 true 后本组件下一帧重建战斗</summary>
    public static bool ReloadRequest = false;

    [Header("测试配置（由 Tools/关卡编辑器 生成的 StageConfig.json 驱动）")]
    public TextAsset StageConfigJson;   // 可留空：留空时自动从 Resources/StageConfig.json 读取

    /// <summary>可由章节/存档入口注入；有来源时只读其真实角色养成，不回退到测试关卡等级。</summary>
    public RoguelikeChapterRuntime CharacterProgressSource { get; set; }

    void Start()
    {
        PrepareScene();
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
    public void ReloadStage(StageSetupData cfg, RoguelikeChapterRuntime characterProgress)
    {
        CharacterProgressSource = characterProgress;
        ReloadStage(cfg);
    }

    public void ReloadStage(StageSetupData cfg)
    {
        var bm = BattleManager.Instance;
        if (bm == null) return;

        // 每次创建前重新查询，避免升级后沿用旧profile；缺少正式记录直接失败，不伪造测试配置数据。
        var profiles = new List<CharacterProfile>();
        for (int i = 0; i < cfg.Allies.Count && i < 4; i++)
            profiles.Add(CharacterProgressSource == null ? CharacterProfile.FromStageAlly(cfg.Allies[i])
                : CharacterProfile.FromProgress(CharacterProgressSource.GetCharacterProgress(cfg.Allies[i].CharacterID)));

        Assemble(cfg, profiles);
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


}
