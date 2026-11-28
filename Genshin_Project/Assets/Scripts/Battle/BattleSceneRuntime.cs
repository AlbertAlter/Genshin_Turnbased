using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class BattleSceneRuntime : MonoBehaviour, IRoguelikeBattleRuntime, IRoguelikeEventBattleStatistics
{
    public int EventEnemyKills => BattleManager.Instance?.EventEnemyKills ?? 0;
    // Legacy test scene creates its manager at startup; bind the explicitly selected voice module then.
    public CharacterBattleVoice characterVoice;
    public BattleUiStatusRiseView statusRise;
    public BattleUiSkillInfo skillInfo;
    public BattleActionPromptView actionPrompt;
    public BattlefieldStageViewLoader stageView;
    public BattleUiCharacterDetails characterDetails;
    public BattleUiFlowPresentation flowPresentation;
    private readonly Dictionary<int, IReadOnlyList<RoguelikeArtifactInstanceData>> detailArtifacts = new();
    private Action<bool> ended;

    protected void PrepareScene()
    {
        // 输入控制：测试时自动挂载（正式场景手动挂 BattleInputController）
        var input = GetComponent<BattleInputController>() ?? gameObject.AddComponent<BattleInputController>();
        if (actionPrompt != null) actionPrompt.selectionInput = input;

        // 确保 DataManager 已初始化
        if (DataManager.Instance == null)
        {
            var dmGO = new GameObject("DataManager");
            dmGO.AddComponent<DataManager>();
        }

        var dm = DataManager.Instance;
        if (dm == null || !dm.IsLoaded)
        {
            throw new InvalidOperationException("DataManager 初始化失败或配表未完整加载。");
        }

        // 确保 BattleManager 存在
        if (BattleManager.Instance == null)
            gameObject.AddComponent<BattleManager>();

        var bm = BattleManager.Instance;
        if (flowPresentation != null) flowPresentation.BindBattle(bm);
        if (characterVoice != null) characterVoice.battle = bm;
        if (statusRise != null) statusRise.battle = bm;
        if (skillInfo != null)
        {
            skillInfo.Bind(skillInfo.buttons, input, bm,
                skillInfo.canvas, skillInfo.viewPrefab);
        }
        if (characterDetails != null)
            characterDetails.BindBattle(bm, input, actor => detailArtifacts.TryGetValue(actor.CharacterID, out var artifacts)
                ? artifacts : Array.Empty<RoguelikeArtifactInstanceData>());
        bm.UnregisterAll();

    }

    public RoguelikeSaveResult StartBattle(RoguelikeBattleSetup setup, Action<bool> onEnded)
    {
        PrepareScene();
        var stage = new StageSetupData { BattleID = setup.BackgroundId, Enemies = setup.Enemies,
            Waves = setup.Waves,
            MaxRound = setup.MaxRound, WinCondition = setup.WinCondition, LoseCondition = setup.LoseCondition,
            EnemyHPMultiplier = setup.EnemyHPMultiplier, EnemyATKMultiplier = setup.EnemyATKMultiplier,
            Allies = setup.Allies.Select(ally => new StageAllySetup { CharacterID = ally.Character.CharacterID,
                Level = ally.Character.Level, Constellation = ally.Character.ConstellationLevel }).ToList() };
        if (stageView == null) throw new InvalidOperationException("正式战斗缺少关卡显示模块。");
        stageView.RuntimeStage = stage;
        ended = onEnded;
        BattleManager.Instance.OnBattleEnded += ended;
        Assemble(stage, setup.Allies.Select(ally => ally.CreateCharacterProfile()).ToList(), setup.Allies);
        return RoguelikeSaveResult.Success();
    }

    public void StopBattle()
    {
        if (characterDetails != null) characterDetails.Close();
        detailArtifacts.Clear();
        var manager = BattleManager.Instance;
        if (manager != null && ended != null) { manager.OnBattleEnded -= ended; manager.ResetBattle(); }
        ended = null;
    }

    protected void Assemble(StageSetupData cfg, List<CharacterProfile> profiles, List<RoguelikeBattleAllyBuild> builds = null)
    {
        if (characterDetails != null) characterDetails.Close();
        detailArtifacts.Clear();
        if (builds != null)
            foreach (var build in builds) detailArtifacts[build.Character.CharacterID] = build.Artifacts;
        var bm = BattleManager.Instance;
        // 先保留旧对象引用，再彻底结束上一场。ResetBattle 会清空注册表和场地。
        var oldAllies = new List<CharacterBattleController>(bm.Allies);
        var oldEnemies = new List<EnemyBattleController>(bm.Enemies);
        GetComponent<BattleInputController>()?.ResetForBattle();
        bm.ResetBattle();

        // 销毁旧实体
        foreach (var ally in oldAllies)
        {
            if (ally != null && ally.gameObject != null)
                Destroy(ally.gameObject);
        }
        foreach (var enemy in oldEnemies)
        {
            if (enemy != null && enemy.gameObject != null)
                Destroy(enemy.gameObject);
        }

        // ---- 创建我方角色 ----
        for (int i = 0; i < cfg.Allies.Count && i < 4; i++)
        {
            var a = cfg.Allies[i];
            var allyGO = new GameObject($"Ally_{a.CharacterID}");
            var allyCtrl = allyGO.AddComponent<CharacterBattleController>();
            CharacterBuildApplier.InitializePanel(allyCtrl, profiles[i]);

            allyCtrl.IsActive = (i == 0);
            if (allyCtrl.Entity != null)
            {
                allyCtrl.Entity.Side = BattleSide.Ally;
                allyCtrl.Entity.SlotPosition = i + 1;  // 先设位置再注册，避免 AssignToField 用默认0
                LogManager.Log(LogCategory.Ally, $"{a.CharacterID} 分配位置 pos={i + 1}");
            }
            bm.RegisterAlly(allyCtrl);
        }

        // ---- 创建敌方（按配置的槽位） ----
        var waves = cfg.Waves?.Count > 0 ? cfg.Waves : new List<StageWaveSetup> {
            new StageWaveSetup { Enemies = cfg.Enemies } };
        bm.ConfigureEnemyWaves(waves, e => SpawnEnemy(bm, e, cfg.EnemyHPMultiplier, cfg.EnemyATKMultiplier),
            enemy => { if (enemy != null) Destroy(enemy.gameObject); });
        bm.ConfigureEventObjectives(cfg.MaxRound, cfg.WinCondition, cfg.LoseCondition);

        // 先完成双方实体、真实站位和注册，再挂载构筑。OnApply/BindStatus 的 Self、全队、
        // 敌方目标都依赖这些注册信息；逐人创建时提前应用会漏掉本人或后续成员，不能靠重跑OnApply补救。
        for (int i = 0; i < bm.Allies.Count; i++)
            CharacterBuildApplier.ApplyBuild(bm.Allies[i], profiles[i]);


        if (builds == null) ApplyInitialEnergy(cfg);
        else for (int index = 0; index < builds.Count; index++)
        {
            var ally = bm.Allies[index];
            RoguelikeBattleEquipment.Apply(ally, builds[index].Artifacts);
            RoguelikeBattleEntryRules.Apply(ally.Entity);
        }
        bm.StartBattle();
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
                ally.Entity.CurrentEnergy = cfg.InitialEnergyValue;
            else
                ally.Entity.CurrentEnergy = max * Mathf.Clamp01(cfg.InitialEnergyValue / 100f);
        }
    }

    private EnemyBattleController SpawnEnemy(BattleManager bm, StageEnemySetup setup, float healthMultiplier, float attackMultiplier)
    {
        int enemyID = setup.EnemyID, position = setup.Slot;
        var enemyGO = new GameObject($"Enemy_{enemyID}");
        var enemyCtrl = enemyGO.AddComponent<EnemyBattleController>();
        enemyCtrl.InitEnemy(enemyID, setup.Level, false);
        if (enemyCtrl.Entity != null)
        {
            enemyCtrl.Entity.TotalHP *= healthMultiplier * setup.HPMultiplier;
            enemyCtrl.Entity.CurrentHP = enemyCtrl.Entity.TotalHP * (setup.InitialHPPercent / 100f);
            enemyCtrl.Entity.TotalATK *= attackMultiplier * setup.ATKMultiplier;
            BattleAttributeInitialization.Prepare(enemyCtrl.Entity);
            enemyCtrl.Entity.Side = BattleSide.Enemy;
            enemyCtrl.Entity.SlotPosition = position;   // 先设位置再注册，避免 AssignToField 用默认0
            LogManager.Log(LogCategory.Enemy, $"{enemyID} 分配位置 pos={position}");
        }
        bm.RegisterEnemy(enemyCtrl, false);
        return enemyCtrl;
    }
}
