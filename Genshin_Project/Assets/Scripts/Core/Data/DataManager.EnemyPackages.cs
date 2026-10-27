using System;
using System.Collections.Generic;
using System.IO;
using OfficeOpenXml;

public partial class DataManager
{
    private IEnumerable<string> EnemyPackageFiles(string name)
    {
        string root = Path.Combine(ChartsPath, name);
        if (!Directory.Exists(root)) yield break;
        var files = Directory.GetFiles(root, "*.xlsx");
        Array.Sort(files, StringComparer.Ordinal);
        foreach (string file in files)
        {
            string title = Path.GetFileName(file);
            if (title.StartsWith("~$", StringComparison.Ordinal) || title.StartsWith("#", StringComparison.Ordinal)
                || title.StartsWith("0_", StringComparison.Ordinal)) continue;
            yield return file;
        }
    }

    private void LoadEnemyPackages()
    {
        var seen = new HashSet<int>();
        foreach (string file in EnemyPackageFiles("Enemies"))
        {
            using var package = new ExcelPackage(new FileInfo(file));
            var main = package.Workbook.Worksheets["Enemy_Main"];
            if (main == null) throw new TableValidationException(file + "缺少Enemy_Main");
            int idColumn = FindColumnByHeader(main, "EnemyID");
            for (int row = GetDataStartRow(main); row <= GetSheetMaxRow(main); row++)
            {
                int id = SafeGetInt(main.Cells[row, idColumn].Value);
                if (id <= 0) continue;
                if (!seen.Add(id)) throw new TableValidationException("敌人分表重复EnemyID：" + id);
                EnemyAIDict.Remove(id); EnemyAIRuleDict.Remove(id);
                foreach (int skill in new List<int>(EnemySkillMainDict.Keys))
                    if (skill / 100 == id) EnemySkillMainDict.Remove(skill);
                foreach (string effect in new List<string>(EnemySkillEffectDict.Keys))
                    if (EnemySkillEffectDict[effect].SkillEffectID / 10000 == id) EnemySkillEffectDict.Remove(effect);
                foreach (string effect in new List<string>(EnemySkillParamDict.Keys))
                    if (EnemySkillParamDict[effect].SkillEffectID / 10000 == id) EnemySkillParamDict.Remove(effect);
            }
            LoadEnemyAttributeSheet(main);
            LoadEnemySkillWorkbook(package);
            var init = package.Workbook.Worksheets["BattleInitiate"];
            if (init == null) continue;
            int enemyColumn = FindColumnByHeader(init, "EnemyID");
            for (int row = GetDataStartRow(init); row <= GetSheetMaxRow(init); row++)
            {
                int id = SafeGetInt(init.Cells[row, enemyColumn].Value);
                if (id == 0) continue;
                if (!EnemyMainDict.TryGetValue(id, out var enemy)) throw new TableValidationException("初始化引用未知敌人：" + id);
                for (int col = 1; col <= init.Dimension.End.Column; col++)
                {
                    if (!init.Cells[1, col].Text.StartsWith("InitiateStatus", StringComparison.Ordinal)) continue;
                    string status = init.Cells[row, col].Text.Trim();
                    if (status.Length == 0) continue;
                    if (!StatusMainDict.ContainsKey(status)) throw new TableValidationException("敌人初始化状态不存在：" + status);
                    if (!enemy.InitiateStatuses.Contains(status)) enemy.InitiateStatuses.Add(status);
                }
            }
        }
        LoadEnemyIdentityTypes();
        foreach (var effect in EnemySkillEffectDict.Values)
        {
            if (effect.EffectType != "SwitchPattern") continue;
            int owner = effect.SkillEffectID / 10000;
            TryValidate("SwitchPattern模式引用", () => {
                if (!int.TryParse(effect.Param1, out int pattern)
                    || !EnemyAIDict.TryGetValue(owner, out var modes) || !modes.Exists(p => p.PatternID == pattern)
                    || !EnemyAIRuleDict.TryGetValue(owner, out var rules) || !rules.Exists(r => r.PatternID == pattern && r.Weight > 0))
                    throw new TableValidationException($"{effect.SkillEffectID2}引用敌人{owner}未定义或空的模式{effect.Param1}。");
            });
        }
    }

    private void LoadEnemyStatusPackages()
    {
        foreach (string file in EnemyPackageFiles("EnemyStatus"))
        {
            using var package = new ExcelPackage(new FileInfo(file));
            string source = "EnemyStatus/" + Path.GetFileName(file);
            LoadStatusMainSheet(package.Workbook.Worksheets["StatusData_Main"]);
            LoadStatusEffectSheet(package.Workbook.Worksheets["Status_Effect"], source + "/Status_Effect", true);
            LoadStatusActionSheet(package.Workbook.Worksheets["Status_Action"], source + "/Status_Action");
            LoadStatusAttributesSheet(package.Workbook.Worksheets["Status_Attributes"]);
        }
    }
}
