using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using OfficeOpenXml;

// ============================================================
// 关卡编辑器：Tools/关卡编辑器
// 配置我方队伍（角色/等级/命座/突破）+ 敌方阵容（1-5号位）+ 初始能量
// 保存为 Assets/Resources/StageConfig.json
// ============================================================

public class StageBuilder : EditorWindow
{
    private List<StageAllySetup> _allies = new List<StageAllySetup>();
    private List<StageEnemySetup> _enemies = new List<StageEnemySetup>();
    private string _energyMode = "Percent";   // Percent / Fixed
    private float _energyValue = 0f;

    // 下拉数据源（编辑器内直读 StreamingAssets/Data 的 xlsx）
    private List<CharOption> _charOptions = new List<CharOption>();
    private List<EnemyOption> _enemyOptions = new List<EnemyOption>();
    private Dictionary<int, List<int>> _ascensionLevels = new Dictionary<int, List<int>>(); // 角色ID -> 突破等级列表

    private bool _loaded = false;
    private string _stageName = "测试关卡";   // 当前编辑的关卡名

    [MenuItem("Tools/关卡编辑器")]
    public static void Open()
    {
        GetWindow<StageBuilder>("关卡编辑器");
    }

    private void OnEnable()
    {
        LoadDataSources();
    }

    // ================= 数据源加载（EPPlus 读 StreamingAssets/Data） =================

    private string DataPath => Path.Combine(Application.streamingAssetsPath, "Data");

    private void LoadDataSources()
    {
        try
        {
            LoadCharacters();
            LoadEnemies();
            _loaded = true;
            LogManager.Log(LogCategory.Build, $"数据源加载完成：{_charOptions.Count}角色, {_enemyOptions.Count}敌人");
        }
        catch (Exception e)
        {
            _loaded = false;
            LogManager.LogError(LogCategory.Build, $"数据源加载失败：{e.Message}");
        }
    }

    private void LoadCharacters()
    {
        _charOptions.Clear();
        _ascensionLevels.Clear();
        string path = Path.Combine(DataPath, "Characters.xlsx");
        if (!File.Exists(path)) { LogManager.LogWarning(LogCategory.Build, $"找不到 {path}，请先同步 Charts 到 StreamingAssets"); return; }

        using (var pkg = new ExcelPackage(new FileInfo(path)))
        {
            // Sheet1: CharacterID, Name
            var sheet = pkg.Workbook.Worksheets[0];
            if (sheet == null || sheet.Dimension == null) return;
            int startRow = 2; // 第1行表头，第2行起数据（本表无类型行）
            int endRow = sheet.Dimension.End.Row;
            for (int row = startRow; row <= endRow; row++)
            {
                string idStr = sheet.Cells[row, 1].Text;
                string name = sheet.Cells[row, 2].Text;
                if (!int.TryParse(idStr, out int id) || id <= 0) continue;
                _charOptions.Add(new CharOption { ID = id, Name = name });
            }

            // 尝试加载各角色 Ascension 等级（角色文件在 Characters/ 子目录）
            string charDir = Path.Combine(DataPath, "Characters");
            if (Directory.Exists(charDir))
            {
                foreach (var f in Directory.GetFiles(charDir, "*.xlsx"))
                {
                    // 从文件名解析角色ID：如 1009_Amber.xlsx -> 1009
                    string fileName = Path.GetFileNameWithoutExtension(f);
                    string idPart = fileName.Split('_')[0];
                    if (!int.TryParse(idPart, out int cid) || cid <= 0) continue;

                    using (var cp = new ExcelPackage(new FileInfo(f)))
                    {
                        var asc = cp.Workbook.Worksheets["Ascension"];
                        if (asc == null || asc.Dimension == null) continue;
                        var levels = new List<int>();
                        int r = 2; // 第1行表头，第2行起数据
                        while (r <= asc.Dimension.End.Row)
                        {
                            // 第1列 = AscensionLevel（20/40/50/60/70/80）
                            string lvlStr = asc.Cells[r, 1].Text;
                            if (int.TryParse(lvlStr, out int lvl) && lvl > 0) levels.Add(lvl);
                            r++;
                        }
                        _ascensionLevels[cid] = levels;
                    }
                }
            }
        }
    }

    private void LoadEnemies()
    {
        _enemyOptions.Clear();
        string path = Path.Combine(DataPath, "EnemyAttributes.xlsx");
        if (!File.Exists(path)) { LogManager.LogWarning(LogCategory.Build, $"找不到 {path}"); return; }

        using (var pkg = new ExcelPackage(new FileInfo(path)))
        {
            var sheet = pkg.Workbook.Worksheets["Enemy_Main"];
            if (sheet == null || sheet.Dimension == null) return;
            int startRow = 2; // 第1行表头，第2行起数据（本表无类型行）
            int endRow = sheet.Dimension.End.Row;
            for (int row = startRow; row <= endRow; row++)
            {
                string idStr = sheet.Cells[row, 1].Text;
                string name = sheet.Cells[row, 3].Text; // EnemyName 第3列
                if (!int.TryParse(idStr, out int id) || id <= 0) continue;
                _enemyOptions.Add(new EnemyOption { ID = id, Name = name });
            }
        }
    }

    // ================= GUI =================

    private void OnGUI()
    {
        if (!_loaded)
        {
            EditorGUILayout.HelpBox("数据源加载失败，请先运行 Tools/Sync Charts to StreamingAssets 同步表数据", MessageType.Error);
            if (GUILayout.Button("重新加载")) { LoadDataSources(); }
            return;
        }

        EditorGUILayout.LabelField("我方队伍（最多4人）", EditorStyles.boldLabel);
        while (_allies.Count < 4) _allies.Add(new StageAllySetup());
        while (_allies.Count > 4) _allies.RemoveAt(_allies.Count - 1);

        DrawAllySlots();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("敌方阵容（最多5个，位置从左到右5→1）", EditorStyles.boldLabel);
        while (_enemies.Count < 5) _enemies.Add(new StageEnemySetup { Slot = _enemies.Count + 1 });
        while (_enemies.Count > 5) _enemies.RemoveAt(_enemies.Count - 1);

        DrawEnemySlots();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("初始能量", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        string[] modes = { "Percent", "Fixed" };
        int modeIdx = Array.IndexOf(modes, _energyMode);
        if (modeIdx < 0) modeIdx = 0;
        _energyMode = modes[EditorGUILayout.Popup(modeIdx, modes, GUILayout.Width(90))];
        _energyValue = EditorGUILayout.FloatField(_energyValue, GUILayout.Width(80));
        EditorGUILayout.LabelField(_energyMode == "Percent" ? "（% 最大能量）" : "（固定数值）", GUILayout.Width(100));
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("关卡名称", EditorStyles.boldLabel);
        _stageName = EditorGUILayout.TextField(_stageName);

        EditorGUILayout.Space();
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("保存为当前关卡")) { SaveStage(); }
        if (GUILayout.Button("应用并加载")) { SaveStage(); ApplyToRuntime(); }
        if (GUILayout.Button("查看当前配置")) { ShowCurrentStage(); }
        if (GUILayout.Button("重置为默认")) { _allies = StageSetupData.CreateDefault().Allies; _enemies = StageSetupData.CreateDefault().Enemies; }
        if (GUILayout.Button("关闭")) { Close(); }
        EditorGUILayout.EndHorizontal();

        DrawStageList();
    }

    // ---------- 搜索过滤 ----------
    private string _charSearch = "";
    private string _enemySearch = "";

    private void DrawAllySlots()
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("搜索:", GUILayout.Width(50));
        _charSearch = EditorGUILayout.TextField(_charSearch);
        EditorGUILayout.LabelField("（ID或名字）", GUILayout.Width(80));
        EditorGUILayout.EndHorizontal();

        // 过滤后的角色选项
        var filtered = _charOptions
            .Where(o => string.IsNullOrEmpty(_charSearch)
                || o.Name.Contains(_charSearch)
                || o.ID.ToString().Contains(_charSearch))
            .ToList();
        if (filtered.Count == 0) filtered = _charOptions;
        // 下拉数组：前面插入“（空位）”项（ID=0）
        string[] charNames = new string[filtered.Count + 1];
        charNames[0] = "（空位）";
        for (int k = 0; k < filtered.Count; k++) charNames[k + 1] = $"{filtered[k].ID} {filtered[k].Name}";

        for (int i = 0; i < _allies.Count; i++)
        {
            var ally = _allies[i];
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"槽位{i + 1}", GUILayout.Width(50));

            // 角色下拉：-1=空位（ID=0）
            int charIdx = filtered.FindIndex(o => o.ID == ally.CharacterID);
            int displayIdx = charIdx < 0 ? 0 : charIdx + 1;   // 空位显示下标0
            int newCharIdx = EditorGUILayout.Popup(displayIdx, charNames, GUILayout.Width(180));
            if (newCharIdx >= 0 && newCharIdx < charNames.Length)
                ally.CharacterID = newCharIdx == 0 ? 0 : filtered[newCharIdx - 1].ID;

            EditorGUILayout.LabelField("Lv", GUILayout.Width(20));
            ally.Level = EditorGUILayout.IntField(ally.Level, GUILayout.Width(40));
            EditorGUILayout.LabelField("命座", GUILayout.Width(30));
            int[] consOptions = { 0, 1, 2, 3, 4, 5, 6 };
            string[] consLabels = { "C0", "C1", "C2", "C3", "C4", "C5", "C6" };
            ally.Constellation = EditorGUILayout.IntPopup(ally.Constellation, consLabels, consOptions, GUILayout.Width(60));

            // 突破后：仅当等级达到某个突破等级时可选
            bool hasAsc = _ascensionLevels.ContainsKey(ally.CharacterID) && _ascensionLevels[ally.CharacterID].Count > 0;
            GUI.enabled = hasAsc;
            ally.IsAscended = EditorGUILayout.Toggle("突破后", ally.IsAscended, GUILayout.Width(70));
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();

            // 技能等级（独立升级 1-15）：普攻/重击/战技/爆发
            if (ally.SkillLevels == null || ally.SkillLevels.Length < 4)
                ally.SkillLevels = new int[4] { 1, 1, 1, 1 };
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("技能", GUILayout.Width(50));
            string[] skillNames = { "普攻", "重击", "战技", "爆发" };
            for (int s = 0; s < 4; s++)
            {
                EditorGUILayout.LabelField(skillNames[s], GUILayout.Width(30));
                ally.SkillLevels[s] = Mathf.Clamp(EditorGUILayout.IntField(ally.SkillLevels[s], GUILayout.Width(36)), 1, 15);
            }
            EditorGUILayout.EndHorizontal();
        }
    }

    private void DrawEnemySlots()
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("搜索:", GUILayout.Width(50));
        _enemySearch = EditorGUILayout.TextField(_enemySearch);
        EditorGUILayout.LabelField("（ID或名字）", GUILayout.Width(80));
        EditorGUILayout.EndHorizontal();

        var filtered = _enemyOptions
            .Where(o => string.IsNullOrEmpty(_enemySearch)
                || o.Name.Contains(_enemySearch)
                || o.ID.ToString().Contains(_enemySearch))
            .ToList();
        if (filtered.Count == 0) filtered = _enemyOptions;
        // 下拉数组：前面插入“（空位）”项（ID=0）
        string[] enemyNames = new string[filtered.Count + 1];
        enemyNames[0] = "（空位）";
        for (int k = 0; k < filtered.Count; k++) enemyNames[k + 1] = $"{filtered[k].ID} {filtered[k].Name}";

        for (int i = 0; i < _enemies.Count; i++)
        {
            var enemy = _enemies[i];
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"位置{i + 1}", GUILayout.Width(50));

            int eIdx = filtered.FindIndex(o => o.ID == enemy.EnemyID);
            int displayIdx = eIdx < 0 ? 0 : eIdx + 1;
            int newEIdx = EditorGUILayout.Popup(displayIdx, enemyNames, GUILayout.Width(180));
            if (newEIdx >= 0 && newEIdx < enemyNames.Length)
                enemy.EnemyID = newEIdx == 0 ? 0 : filtered[newEIdx - 1].ID;

            enemy.Slot = i + 1;   // 界面位置1-5 = 实际Slot
            EditorGUILayout.LabelField("Lv", GUILayout.Width(20));
            enemy.Level = EditorGUILayout.IntField(enemy.Level, GUILayout.Width(40));
            EditorGUILayout.EndHorizontal();
        }
    }


    // ================= 保存 =================

    private string StageDir => Path.Combine(Application.dataPath, "Resources", "Stages");

    private void SaveStage()
    {
        // 清理空槽
        var allies = _allies.Where(a => a.CharacterID > 0).ToList();
        var enemies = _enemies.Where(e => e.EnemyID > 0)
                              .OrderBy(e => e.Slot)
                              .ToList();

        var cfg = new StageSetupData
        {
            StageName = _stageName,
            Allies = allies,
            Enemies = enemies,
            InitialEnergyMode = _energyMode,
            InitialEnergyValue = _energyValue
        };

        string json = JsonUtility.ToJson(cfg, true);

        // 按关卡名保存到 Resources/Stages/
        if (!Directory.Exists(StageDir)) Directory.CreateDirectory(StageDir);
        string safeName = string.IsNullOrEmpty(_stageName) ? "未命名" : _stageName;
        string stagePath = Path.Combine(StageDir, safeName + ".json");
        File.WriteAllText(stagePath, json);

        // 同步为当前生效关卡
        string dir = Path.Combine(Application.dataPath, "Resources");
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "StageConfig.json");
        File.WriteAllText(path, json);

        AssetDatabase.Refresh();
        LogManager.Log(LogCategory.Build, $"已保存关卡「{safeName}」：{stagePath}（我方{allies.Count}人，敌方{enemies.Count}人）");
    }

    // ================= 关卡列表 =================

    private void DrawStageList()
    {
        if (!Directory.Exists(StageDir)) return;
        var files = Directory.GetFiles(StageDir, "*.json").OrderBy(f => f).ToList();
        if (files.Count == 0) return;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("已保存关卡", EditorStyles.boldLabel);
        foreach (var f in files)
        {
            string name = Path.GetFileNameWithoutExtension(f);
            // 读取摘要
            string summary = "";
            try
            {
                var cfg = JsonUtility.FromJson<StageSetupData>(File.ReadAllText(f));
                if (cfg != null)
                {
                    int ac = cfg.Allies == null ? 0 : cfg.Allies.Count;
                    int ec = cfg.Enemies == null ? 0 : cfg.Enemies.Count;
                    summary = $"我方{ac}人 敌方{ec}人 能量{cfg.InitialEnergyMode}{cfg.InitialEnergyValue}";
                }
            }
            catch (Exception e) { summary = "解析失败:" + e.Message; }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(name, GUILayout.Width(120));
            EditorGUILayout.LabelField(summary, GUILayout.Width(200));
            if (GUILayout.Button("加载", GUILayout.Width(50)))
            {
                LoadStageFile(f);
            }
            if (GUILayout.Button("删除", GUILayout.Width(50)))
            {
                File.Delete(f);
                AssetDatabase.Refresh();
            }
            EditorGUILayout.EndHorizontal();
        }
    }

    private void LoadStageFile(string path)
    {
        try
        {
            var cfg = JsonUtility.FromJson<StageSetupData>(File.ReadAllText(path));
            if (cfg == null) { LogManager.LogError(LogCategory.Build, "关卡解析失败"); return; }
            _stageName = cfg.StageName ?? Path.GetFileNameWithoutExtension(path);
            _allies = cfg.Allies ?? new List<StageAllySetup>();
            _enemies = cfg.Enemies ?? new List<StageEnemySetup>();
            _energyMode = cfg.InitialEnergyMode;
            _energyValue = cfg.InitialEnergyValue;
            // 同步为当前生效关卡并热加载
            string dir = Path.Combine(Application.dataPath, "Resources");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "StageConfig.json"), File.ReadAllText(path));
            AssetDatabase.Refresh();
            BattleTester.ReloadRequest = true;
            LogManager.Log(LogCategory.Build, $"已加载关卡「{_stageName}」并应用到运行时");
        }
        catch (Exception e)
        {
            LogManager.LogError(LogCategory.Build, $"加载关卡失败：{e.Message}");
        }
    }

    private class CharOption { public int ID; public string Name; }
    private class EnemyOption { public int ID; public string Name; }

    /// <summary>保存后通知运行时 BattleTester 重读 JSON 并重建战斗</summary>
    private void ApplyToRuntime()
    {
        // 运行时组件通过静态字段接收请求（编辑器程序集可访问运行时类型）
        BattleTester.ReloadRequest = true;
        LogManager.Log(LogCategory.Build, "已置热加载请求，运行中的 BattleTester 将在下一帧重建战斗");
    }

    /// <summary>显示当前已保存的关卡配置（从 JSON 读取）</summary>
    private void ShowCurrentStage()
    {
        var asset = Resources.Load<TextAsset>("StageConfig");
        if (asset == null)
        {
            EditorUtility.DisplayDialog("当前关卡", "尚未保存任何关卡（Resources/StageConfig.json 不存在）", "确定");
            return;
        }

        var cfg = JsonUtility.FromJson<StageSetupData>(asset.text);
        if (cfg == null)
        {
            EditorUtility.DisplayDialog("当前关卡", "StageConfig.json 解析失败", "确定");
            return;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("当前已保存关卡配置：");
        sb.AppendLine("【我方】");
        if (cfg.Allies == null || cfg.Allies.Count == 0)
            sb.AppendLine("  (空)");
        else foreach (var a in cfg.Allies)
            sb.AppendLine($"  角色{a.CharacterID} Lv{a.Level} 命座{a.Constellation} 突破{(a.IsAscended ? "是" : "否")}");
        sb.AppendLine("【敌方】");
        if (cfg.Enemies == null || cfg.Enemies.Count == 0)
            sb.AppendLine("  (空)");
        else foreach (var e in cfg.Enemies)
            sb.AppendLine($"  {e.Slot}号位 敌人{e.EnemyID} Lv{e.Level}");
        sb.AppendLine($"【初始能量】{cfg.InitialEnergyMode} {cfg.InitialEnergyValue}");

        EditorUtility.DisplayDialog("当前关卡", sb.ToString(), "确定");
    }
}
