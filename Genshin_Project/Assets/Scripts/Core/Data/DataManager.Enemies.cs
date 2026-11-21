using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using OfficeOpenXml;
using UnityEngine;

public partial class DataManager
{
    // ================================================================
    //  EnemyAttributes.xlsx/Coeff：生命、攻击系数及攻击曲线选择的唯一来源
    // ================================================================
    void LoadEnemyAttributes()
    {
        string path = Path.Combine(ChartsPath, "EnemyAttributes.xlsx");
        if (!File.Exists(path)) { Debug.LogWarning("EnemyAttributes.xlsx not found"); return; }
        using var pkg = OpenDataWorkbook(path);
        var sheet = pkg.Workbook.Worksheets["Coeff"];
        if (sheet == null) throw new TableValidationException("EnemyAttributes.xlsx缺少Coeff系数表。");
        LoadEnemyCoefficientSheet(sheet);
        LoadEnemyCurves(pkg);
    }

    // Phase也按表头读取；留空/0保留无阶段，不替换为第一阶段。
    private static int ReadEnemyPhase(ExcelWorksheet sheet, int row)
    {
        int column = ExcelTableHeaders.For(sheet).Optional("Phase");
        string raw = column > 0 ? Convert.ToString(sheet.Cells[row, column].Value, CultureInfo.InvariantCulture)?.Trim() : null;
        if (string.IsNullOrEmpty(raw)) return 0;
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double phase)
            && phase >= 0 && phase <= int.MaxValue && phase == Math.Truncate(phase)) return (int)phase;
        throw new TableValidationException($"[{sheet.Name}] 第{row}行Phase必须为空或非负整数，当前=[{raw}]。");
    }

    private void LoadEnemyCoefficientSheet(ExcelWorksheet sheet)
    {
        for (int row = GetDataStartRow(sheet); row <= GetSheetMaxRow(sheet); row++)
        {
            int id = SafeGetInt(sheet.Cells[row, FindColumnByHeader(sheet, "EnemyID")].Value);
            if (id <= 0) continue;
            int phase = ReadEnemyPhase(sheet, row);
            if (!EnemyPhaseDict.TryGetValue(id, out var phases))
                EnemyPhaseDict.Add(id, phases = new Dictionary<int, EnemyMainData>());
            if (phases.ContainsKey(phase))
                throw new TableValidationException($"[Coeff] 第{row}行重复EnemyID+Phase：{id}/{phase}。");
            phases.Add(phase, new EnemyMainData {
                EnemyID = id,
                Phase = phase,
                EnemyName = sheet.Cells[row, FindColumnByHeader(sheet, "中文名")].Text,
                EnemyNameID = sheet.Cells[row, FindColumnByHeader(sheet, "English Name")].Text,
                HP_coeff = SafeGetFloat(sheet.Cells[row, FindColumnByHeader(sheet, "HP_coeff")].Value),
                ATK_coeff = SafeGetFloat(sheet.Cells[row, FindColumnByHeader(sheet, "ATK_coeff")].Value),
                ATK_Curve = SafeGetInt(sheet.Cells[row, FindColumnByHeader(sheet, "ATK_Curve")].Value)
            });
        }
        foreach (var enemy in EnemyPhaseDict)
        {
            var phases = enemy.Value;
            if (phases.ContainsKey(0))
            {
                if (phases.Count != 1)
                    throw new TableValidationException($"敌人{enemy.Key}不能混用无阶段与显式Phase。");
            }
            else for (int phase = 1; phase <= phases.Count; phase++)
                if (!phases.ContainsKey(phase))
                    throw new TableValidationException($"敌人{enemy.Key}的Phase必须从1连续配置，缺少{phase}。");
            EnemyMainDict[enemy.Key] = phases[phases.ContainsKey(0) ? 0 : 1];
        }
    }

    /// <summary>按敌人编号和生命阶段取属性；未配置的阶段不回退到其他阶段。</summary>
    public bool TryGetEnemyPhaseData(int enemyID, int phase, out EnemyMainData data)
    {
        if (EnemyPhaseDict.TryGetValue(enemyID, out var phases)) return phases.TryGetValue(phase, out data);
        // 保留程序/测试直接注入无阶段EnemyMainData的既有入口。
        if (EnemyMainDict.TryGetValue(enemyID, out data) && data.Phase == phase) return true;
        data = null;
        return false;
    }

    /// <summary>无阶段返回0；显式阶段数量供生命阶段机制及血条适配器读取。</summary>
    public int GetEnemyPhaseCount(int enemyID)
    {
        if (EnemyPhaseDict.TryGetValue(enemyID, out var phases)) return phases.ContainsKey(0) ? 0 : phases.Count;
        return EnemyMainDict.TryGetValue(enemyID, out var main) && main.Phase > 0 ? 1 : 0;
    }

    // 技能分表仅补充标识、行动贡献、抗性与增伤，保留Coeff已加载的生命/攻击参数。
    void LoadEnemyAttributeSheet(ExcelWorksheet sheet)
    {
        var seen = new HashSet<(int enemyID, int phase)>();
        int startRow = GetDataStartRow(sheet);
        int maxRow = GetSheetMaxRow(sheet);
        for (int row = startRow; row <= maxRow; row++)
        {
            string cell = sheet.Cells[row, FindColumnByHeader(sheet, "EnemyID")].Text;
            if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(sheet.Cells[row, FindColumnByHeader(sheet, "EnemyNameID")].Text)) continue;
            int enemyID = SafeGetInt(sheet.Cells[row, FindColumnByHeader(sheet, "EnemyID")].Value);
            if (enemyID == 0) continue;
            int phase = ReadEnemyPhase(sheet, row);
            if (!seen.Add((enemyID, phase)))
                throw new TableValidationException($"[Enemy_Main] 第{row}行重复EnemyID+Phase：{enemyID}/{phase}。");
            if (!TryGetEnemyPhaseData(enemyID, phase, out var coefficient))
                throw new TableValidationException($"敌人{enemyID}的Phase={phase}未在EnemyAttributes.xlsx/Coeff配置属性系数。");
            var data = new EnemyMainData
            {
                EnemyID = SafeGetInt(sheet.Cells[row, FindColumnByHeader(sheet, "EnemyID")].Value),
                Phase = phase,
                EnemyNameID = sheet.Cells[row, FindColumnByHeader(sheet, "EnemyNameID")].Text,
                EnemyName = sheet.Cells[row, FindColumnByHeader(sheet, "EnemyName")].Text,
                Threat = SafeGetInt(sheet.Cells[row, FindColumnByHeader(sheet, "Threat")].Value),
                ActsGiven = SafeGetInt(sheet.Cells[row, FindColumnByHeader(sheet, "ActsGiven")].Value),
                ATK_Curve = coefficient.ATK_Curve,
                HP_coeff = coefficient.HP_coeff,
                ATK_coeff = coefficient.ATK_coeff,
                EnemyType = coefficient.EnemyType,
                EnemyFamily = coefficient.EnemyFamily,
                StatusImmunities = new HashSet<string>(coefficient.StatusImmunities, StringComparer.OrdinalIgnoreCase),
                PhysicalRes = SafeGetFloat(sheet.Cells[row, FindColumnByHeader(sheet, "PhysicalRes")].Value),
                PyroRes = SafeGetFloat(sheet.Cells[row, FindColumnByHeader(sheet, "PyroRes")].Value),
                HydroRes = SafeGetFloat(sheet.Cells[row, FindColumnByHeader(sheet, "HydroRes")].Value),
                ElectroRes = SafeGetFloat(sheet.Cells[row, FindColumnByHeader(sheet, "ElectroRes")].Value),
                CryoRes = SafeGetFloat(sheet.Cells[row, FindColumnByHeader(sheet, "CryoRes")].Value),
                AnemoRes = SafeGetFloat(sheet.Cells[row, FindColumnByHeader(sheet, "AnemoRes")].Value),
                DendroRes = SafeGetFloat(sheet.Cells[row, FindColumnByHeader(sheet, "DendroRes")].Value),
                GeoRes = SafeGetFloat(sheet.Cells[row, FindColumnByHeader(sheet, "GeoRes")].Value),
                PhysicalDmgBonus = SafeGetFloat(sheet.Cells[row, FindColumnByHeader(sheet, "PhysicalDmgBonus")].Value),
                PyroDmgBonus = SafeGetFloat(sheet.Cells[row, FindColumnByHeader(sheet, "PyroDmgBonus")].Value),
                HydroDmgBonus = SafeGetFloat(sheet.Cells[row, FindColumnByHeader(sheet, "HydroDmgBonus")].Value),
                ElectroDmgBonus = SafeGetFloat(sheet.Cells[row, FindColumnByHeader(sheet, "ElectroDmgBonus")].Value),
                CryoDmgBonus = SafeGetFloat(sheet.Cells[row, FindColumnByHeader(sheet, "CryoDmgBonus")].Value),
                AnemoDmgBonus = SafeGetFloat(sheet.Cells[row, FindColumnByHeader(sheet, "AnemoDmgBonus")].Value),
                DendroDmgBonus = SafeGetFloat(sheet.Cells[row, FindColumnByHeader(sheet, "DendroDmgBonus")].Value),
                GeoDmgBonus = SafeGetFloat(sheet.Cells[row, FindColumnByHeader(sheet, "GeoDmgBonus")].Value)
            };
            if (data.EnemyID == 0) continue;
            if (!EnemyPhaseDict.TryGetValue(enemyID, out var phases))
                EnemyPhaseDict.Add(enemyID, phases = new Dictionary<int, EnemyMainData>());
            phases[phase] = data;
            if (phase == 0 || phase == 1) EnemyMainDict[data.EnemyID] = data;
        }
        // 每个敌人的各阶段放在同一批工作簿；避免某阶段有系数却静默缺失抗性/行动属性。
        foreach (var key in seen)
            foreach (int phase in EnemyPhaseDict[key.enemyID].Keys)
                if (!seen.Contains((key.enemyID, phase)))
                    throw new TableValidationException($"[Enemy_Main] 敌人{key.enemyID}缺少Phase={phase}的主表属性。");
    }

    // 敌人身份来自正式Enemy.xlsx，不凭显示名、韧性、Threat或旧属性表顺序推断等级类别。
    void LoadEnemyIdentityTypes()
    {
        string path = Path.Combine(ChartsPath, "Enemy.xlsx");
        if (!File.Exists(path)) return; // 保留未知；需要此信息的技能须明确拒绝缺失分类。
        using var package = OpenDataWorkbook(path);
        if (package.Workbook.Worksheets.Count < 2)
            throw new TableValidationException("Enemy.xlsx缺少第二个子表（敌人类别、生物种类与状态免疫）。");
        LoadEnemyIdentitySheet(package.Workbook.Worksheets[1]);
    }

    void LoadEnemyIdentitySheet(ExcelWorksheet sheet)
    {
        var headers = ExcelTableHeaders.For(sheet);
        int idColumn = headers.Optional("EnemyID");
        if (idColumn < 1) idColumn = headers.Optional("顺序ID");
        int typeColumn = headers.Optional("EnemyType");
        int familyColumn = headers.Optional("生物种类");
        int immuneColumn = headers.Optional("Immune");
        if (idColumn < 1 || typeColumn < 1 || familyColumn < 1 || immuneColumn < 1)
            throw new TableValidationException("Enemy.xlsx第二子表须有EnemyID/顺序ID、EnemyType、生物种类、Immune表头。");
        var seen = new HashSet<int>();
        for (int row = GetDataStartRow(sheet); row <= GetSheetMaxRow(sheet); row++)
        {
            int id = SafeGetInt(sheet.Cells[row, idColumn].Value);
            if (id <= 0) continue;
            if (!seen.Add(id)) throw new TableValidationException($"Enemy.xlsx第{row}行重复敌人ID：{id}");
            if (EnemyMainDict.TryGetValue(id, out var enemy))
            {
                IEnumerable<EnemyMainData> stages = EnemyPhaseDict.TryGetValue(id, out var phases)
                    ? phases.Values : new[] { enemy };
                foreach (var stage in stages)
                {
                    stage.EnemyType = sheet.Cells[row, typeColumn].Text.Trim();
                    stage.EnemyFamily = sheet.Cells[row, familyColumn].Text.Trim();
                    stage.StatusImmunities = EnemyTraits.ParseStatusImmunities(sheet.Cells[row, immuneColumn].Text);
                }
            }
        }
    }

    // ================================================================
    //  EnemyAttributes.xlsx 的 Base_HP / Base_ATK1 / Base_ATK2 曲线
    // ================================================================
    void LoadEnemyCurves(ExcelPackage package)
    {
        LoadEnemyCurve(package.Workbook.Worksheets["Base_HP"], "hp", BaseHPDict);
        LoadEnemyCurve(package.Workbook.Worksheets["Base_ATK1"], "atk1", BaseATK1Dict);
        LoadEnemyCurve(package.Workbook.Worksheets["Base_ATK2"], "atk2", BaseATK2Dict);
    }

    private void LoadEnemyCurve(ExcelWorksheet sheet, string valueHeader, Dictionary<int, float> curve)
    {
        if (sheet == null) return;
        int startRow = GetDataStartRow(sheet);
        int maxRow = GetSheetMaxRow(sheet);
        if (startRow > maxRow) return;

        int levelColumn = FindColumnByHeader(sheet, "lv");
        int valueColumn = FindColumnByHeader(sheet, valueHeader);
        for (int row = startRow; row <= maxRow; row++)
        {
            var level = sheet.Cells[row, levelColumn];
            var value = sheet.Cells[row, valueColumn];
            if (string.IsNullOrEmpty(level.Text) && string.IsNullOrEmpty(value.Text)) continue;
            curve[SafeGetInt(level.Value)] = SafeGetFloat(value.Value);
        }
    }

    // 查询敌人基础生命（等级无数据返回0）
    public float GetEnemyBaseHP(int level)
    {
        return BaseHPDict.TryGetValue(level, out float v) ? v : 0f;
    }

    // 查询敌人基础攻击（按 Enemy_Main.ATK_Curve 选择曲线1或2；等级无数据返回0）
    public float GetEnemyBaseATK(int curveID, int level)
    {
        if (curveID == 2)
            return BaseATK2Dict.TryGetValue(level, out float v2) ? v2 : 0f;
        return BaseATK1Dict.TryGetValue(level, out float v1) ? v1 : 0f;
    }

    // ================================================================
    //  Enemies分组工作簿的技能、效果、参数与AI
    // ================================================================
    private void ValidateOwnedEnemyPhase(ExcelWorksheet sheet, int row, int enemyID, int phase)
    {
        if (phase == 0 ? GetEnemyPhaseCount(enemyID) == 0 : TryGetEnemyPhaseData(enemyID, phase, out _))
            return;
        throw new TableValidationException($"[{sheet.Name}] 第{row}行敌人{enemyID}的Phase={phase}无效。分阶段敌人必须填写已配置的生命阶段。");
    }

    private void ValidateEnemyEffectPhase(ExcelWorksheet sheet, int row, int effectID, int phase)
    {
        int skillID = effectID / 100;
        ValidateOwnedEnemyPhase(sheet, row, skillID / 100, phase);
        if (phase > 0 && (!EnemySkillMainDict.TryGetValue(skillID, out var skill) || skill.Phase != phase))
            throw new TableValidationException($"[{sheet.Name}] 第{row}行效果{effectID}的Phase={phase}必须与技能{skillID}一致。");
    }

    void LoadEnemySkillWorkbook(ExcelPackage pkg)
    {
        // EnemySkill_Main
        var mainSheet = pkg.Workbook.Worksheets["EnemySkill_Main"];
        if (mainSheet != null)
        {
            int startRow = GetDataStartRow(mainSheet);
            int maxRow = GetSheetMaxRow(mainSheet);
            for (int row = startRow; row <= maxRow; row++)
            {
                string cell = mainSheet.Cells[row, FindColumnByHeader(mainSheet, "EnemySkillID")].Text;
                if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(mainSheet.Cells[row, FindColumnByHeader(mainSheet, "EnemySkillID2")].Text)) continue;
                var data = new EnemySkillMainData
                {
                    EnemySkillID = SafeGetInt(mainSheet.Cells[row, FindColumnByHeader(mainSheet, "EnemySkillID")].Value),
                    Phase = ReadEnemyPhase(mainSheet, row),
                    EnemySkillID2 = mainSheet.Cells[row, FindColumnByHeader(mainSheet, "EnemySkillID2")].Text,
                    EnemySkillName = mainSheet.Cells[row, FindColumnByHeader(mainSheet, "EnemySkillName")].Text,
                    SkillTags = EnemySkillTags.Parse(ReadOptionalEffectText(mainSheet, row, "SkillTags")),
                    Cooldown = SafeGetFloat(mainSheet.Cells[row, FindColumnByHeader(mainSheet, "Cooldown")].Value),
                    UsePerTurn = SafeGetInt(mainSheet.Cells[row, FindColumnByHeader(mainSheet, "UsePerTurn")].Value),
                    HighThreat = SafeGetInt(mainSheet.Cells[row, FindColumnByHeader(mainSheet, "HighThreat")].Value),
                    Description = mainSheet.Cells[row, FindColumnByHeader(mainSheet, "Description")].Text
                };
                if (data.EnemySkillID == 0) continue;
                ValidateOwnedEnemyPhase(mainSheet, row, data.EnemySkillID / 100, data.Phase);
                foreach (var existing in EnemySkillMainDict.Values)
                    if ((data.Phase > 0 || existing.Phase > 0)
                        && (existing.EnemySkillID == data.EnemySkillID || existing.EnemySkillID2 == data.EnemySkillID2))
                        throw new TableValidationException($"[EnemySkill_Main] 第{row}行重复技能编号或字符串ID：{data.EnemySkillID}/{data.EnemySkillID2}。分阶段技能必须独立配置。");
                EnemySkillMainDict[data.EnemySkillID] = data;
            }
        }

        // EnemySkill_Effect
        var effSheet = pkg.Workbook.Worksheets["EnemySkill_Effect"];
        if (effSheet != null)
        {
            int startRow = GetDataStartRow(effSheet);
            int maxRow = GetSheetMaxRow(effSheet);
            for (int row = startRow; row <= maxRow; row++)
            {
                string cell = effSheet.Cells[row, FindColumnByHeader(effSheet, "SkillEffectID2")].Text;
                if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(effSheet.Cells[row, FindColumnByHeader(effSheet, "SkillEffectID")].Text)) continue;
                var data = new EnemySkillEffectData
                {
                    Phase = ReadEnemyPhase(effSheet, row),
                    SkillEffectID = SafeGetInt(effSheet.Cells[row, FindColumnByHeader(effSheet, "SkillEffectID")].Value),
                    SkillEffectID2 = cell,
                    EffectIndex = SafeGetInt(effSheet.Cells[row, FindColumnByHeader(effSheet, "EffectIndex")].Value),
                    EffectType = effSheet.Cells[row, FindColumnByHeader(effSheet, "EffectType")].Text,
                    Element = effSheet.Cells[row, FindColumnByHeader(effSheet, "Element")].Text,
                    DamageType = effSheet.Cells[row, FindColumnByHeader(effSheet, "DamageType")].Text,
                    Duration = SafeGetInt(effSheet.Cells[row, FindColumnByHeader(effSheet, "Duration")].Value),
                    AddInPhase = SafeGetInt(effSheet.Cells[row, FindColumnByHeader(effSheet, "AddInPhase")].Value),
                    TriggerPhase = SafeGetInt(effSheet.Cells[row, FindColumnByHeader(effSheet, "TriggerPhase")].Value),
                    Param1 = effSheet.Cells[row, FindColumnByHeader(effSheet, "Param1")].Text,
                    Param2 = ReadEffectParam2(effSheet, row, effSheet.Cells[row, FindColumnByHeader(effSheet, "EffectType")].Text),
                    Param3 = effSheet.Cells[row, FindColumnByHeader(effSheet, "Param3")].Text,
                    TargetType = effSheet.Cells[row, FindColumnByHeader(effSheet, "TargetType")].Text,
                    TargetNumber = SafeGetInt(effSheet.Cells[row, FindColumnByHeader(effSheet, "TargetNumber")].Value),
                    TargetConsecutive = SafeGetInt(effSheet.Cells[row, FindColumnByHeader(effSheet, "TargetConsecutive")].Value),
                    TargetOverride = effSheet.Cells[row, FindColumnByHeader(effSheet, "TargetOverride")].Text,
                    ScriptHook = effSheet.Cells[row, FindColumnByHeader(effSheet, "ScriptHook")].Text,
                    DamageHook = ReadOptionalEffectText(effSheet, row, "DamageHook"),
                    ExecuteCount = ReadExecuteCount(effSheet, row),
                    ExecuteCountHandler = ReadExecuteCountHandler(effSheet, row)
                };
                TryValidate("EnemySkill_Effect", () => TableValidator.ValidateEffectRow("EnemySkill_Effect", row,
                    data.EffectType, data.Element, data.Duration, data.Param2,
                    data.TargetType, data.TargetNumber, data.TargetConsecutive,
                    data.TargetOverride, isEnemy: true));
                TryValidate("EnemySkill_Effect", () =>
                {
                    TableValidator.ValidateDamageProperties("EnemySkill_Effect", row,
                        data.EffectType, data.Element, data.Param1, data.Param2, data.Param3, data.DamageHook);
                    if (data.EffectType == "Damage") data.Element = DamageProperties.NormalizeElement(data.Element);
                });
                if (data.EffectType == "ApplyElement")
                    TryValidate("EnemySkill_Effect", () => TableValidator.ValidateApplyElement("EnemySkill_Effect", row,
                        data.Element, data.Param1, data.Param2, data.Param3, data.DamageType, data.DamageHook));
                if (data.EffectType == "BondOfLife")
                    TryValidate("EnemySkill_Effect", () => TableValidator.ValidateBondOfLife("EnemySkill_Effect", row, data.Param1));
                if (data.EffectType == "Revive")
                    TryValidate("EnemySkill_Effect", () => TableValidator.ValidateRevive("EnemySkill_Effect", row,
                        data.Param1, data.Param2, data.Param3, data.Element, data.DamageType, data.DamageHook, data.Duration, data.TargetType));
                TryValidate("EnemySkill_Effect", () => TableValidator.ValidatePublicCountEffect(
                    "EnemySkill_Effect", row, data.EffectType, data.Param1, data.Param2, data.TargetType));
                // 敌人专用模式切换；ChangeControl仅允许在状态表中配置All/Freeze。
                if (data.EffectType == "Cooldown")
                    TryValidate("EnemySkill_Effect", () => TableValidator.ValidateCooldown("EnemySkill_Effect", row,
                        data.Param1, data.Param2, isEnemy: true));
                if (data.EffectType == "GrantAction")
                    TryValidate("EnemySkill_Effect", () => TableValidator.ValidateGrantAction("EnemySkill_Effect", row,
                        data.Param1, data.Param2, data.Param3, data.Element, data.DamageType, data.DamageHook,
                        data.Duration, data.AddInPhase, data.TriggerPhase));
                if (data.EffectType == "SwitchPattern")
                    TryValidate("EnemySkill_Effect", () => TableValidator.ValidateSwitchPattern("EnemySkill_Effect", row,
                        data.Param1, data.Param2, data.Param3, data.Element, data.DamageType, data.DamageHook,
                        data.Duration, data.AddInPhase, data.TriggerPhase));
                if (data.EffectType == "ChangeControl")
                    TryValidate("EnemySkill_Effect", () => TableValidator.ValidateChangeControl("EnemySkill_Effect", row,
                        data.Param1, data.Param2, data.Param3, data.Duration,
                        isStatusEffectTable: false, isEnemy: true));
                if (string.IsNullOrEmpty(data.SkillEffectID2)) continue;
                ValidateEnemyEffectPhase(effSheet, row, data.SkillEffectID, data.Phase);
                foreach (var existing in EnemySkillEffectDict.Values)
                    if ((data.Phase > 0 || existing.Phase > 0)
                        && (existing.SkillEffectID == data.SkillEffectID || existing.SkillEffectID2 == data.SkillEffectID2))
                        throw new TableValidationException($"[EnemySkill_Effect] 第{row}行重复效果编号或字符串ID：{data.SkillEffectID}/{data.SkillEffectID2}。");
                EnemySkillEffectDict[data.SkillEffectID2] = data;
            }
        }

        // EnemySkill_Param
        var paramSheet = pkg.Workbook.Worksheets["EnemySkill_Param"];
        if (paramSheet != null)
        {
            int startRow = GetDataStartRow(paramSheet);
            int maxRow = GetSheetMaxRow(paramSheet);
            for (int row = startRow; row <= maxRow; row++)
            {
                string cell = paramSheet.Cells[row, FindColumnByHeader(paramSheet, "SkillEffectID2")].Text;
                if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(paramSheet.Cells[row, FindColumnByHeader(paramSheet, "SkillEffectID")].Text)) continue;
                var data = new EnemySkillParamData
                {
                    SkillEffectID = SafeGetInt(paramSheet.Cells[row, FindColumnByHeader(paramSheet, "SkillEffectID")].Value),
                    SkillEffectID2 = cell,
                    EffectType = paramSheet.Cells[row, FindColumnByHeader(paramSheet, "EffectType")].Text,
                    Hits1 = paramSheet.Cells[row, FindColumnByHeader(paramSheet, "Hits1")].Text,
                    Hits2 = paramSheet.Cells[row, FindColumnByHeader(paramSheet, "Hits2")].Text,
                    Hits3 = paramSheet.Cells[row, FindColumnByHeader(paramSheet, "Hits3")].Text,
                    Hits4 = paramSheet.Cells[row, FindColumnByHeader(paramSheet, "Hits4")].Text,
                    Hits5 = paramSheet.Cells[row, FindColumnByHeader(paramSheet, "Hits5")].Text,
                    Hits6 = paramSheet.Cells[row, FindColumnByHeader(paramSheet, "Hits6")].Text,
                    Hits7 = paramSheet.Cells[row, FindColumnByHeader(paramSheet, "Hits7")].Text,
                    Hits8 = paramSheet.Cells[row, FindColumnByHeader(paramSheet, "Hits8")].Text
                };
                if (string.IsNullOrEmpty(data.SkillEffectID2)) continue;
                int phase = ReadEnemyPhase(paramSheet, row);
                ValidateEnemyEffectPhase(paramSheet, row, data.SkillEffectID, phase);
                bool hasEffect = EnemySkillEffectDict.TryGetValue(data.SkillEffectID2, out var effect);
                if ((phase > 0 || hasEffect && effect.Phase > 0)
                    && (!hasEffect || effect.Phase != phase || effect.SkillEffectID != data.SkillEffectID
                        || EnemySkillParamDict.ContainsKey(data.SkillEffectID2)))
                    throw new TableValidationException($"[EnemySkill_Param] 第{row}行效果{data.SkillEffectID2}必须与唯一一条同阶段的技能效果对应。");
                EnemySkillParamDict[data.SkillEffectID2] = data;
            }
        }

        // EnemyAI
        var aiSheet = pkg.Workbook.Worksheets["EnemyAI"];
        if (aiSheet != null)
        {
            int startRow = GetDataStartRow(aiSheet);
            int maxRow = GetSheetMaxRow(aiSheet);
            for (int row = startRow; row <= maxRow; row++)
            {
                string cell = aiSheet.Cells[row, FindColumnByHeader(aiSheet, "EnemyID")].Text;
                if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(aiSheet.Cells[row, FindColumnByHeader(aiSheet, "EnemyNameID")].Text)) continue;
                var data = new EnemyAIData
                {
                    EnemyID = SafeGetInt(aiSheet.Cells[row, FindColumnByHeader(aiSheet, "EnemyID")].Value),
                    Phase = ReadEnemyPhase(aiSheet, row),
                    EnemyNameID = aiSheet.Cells[row, FindColumnByHeader(aiSheet, "EnemyNameID")].Text,
                    EnemyName = aiSheet.Cells[row, FindColumnByHeader(aiSheet, "EnemyName")].Text,
                    PatternID = SafeGetInt(aiSheet.Cells[row, FindColumnByHeader(aiSheet, "PatternID")].Value),
                    PatternType = aiSheet.Cells[row, FindColumnByHeader(aiSheet, "PatternType")].Text,
                    NextPatternID = SafeGetInt(aiSheet.Cells[row, FindColumnByHeader(aiSheet, "NextPatternID")].Value),
                    ConditionType = aiSheet.Cells[row, FindColumnByHeader(aiSheet, "ConditionType")].Text,
                    ConditionParam = aiSheet.Cells[row, FindColumnByHeader(aiSheet, "ConditionParam")].Text
                };
                if (data.EnemyID == 0) continue;
                ValidateOwnedEnemyPhase(aiSheet, row, data.EnemyID, data.Phase);
                if (!EnemyAIDict.ContainsKey(data.EnemyID)) EnemyAIDict[data.EnemyID] = new List<EnemyAIData>();
                var modes = EnemyAIDict[data.EnemyID];
                if (data.Phase > 0 && modes.Exists(mode => mode.Phase == data.Phase && mode.PatternID == data.PatternID))
                    throw new TableValidationException($"[EnemyAI] 第{row}行敌人{data.EnemyID}的Phase={data.Phase}重复定义模式{data.PatternID}。");
                modes.Add(data);
            }
        }

        // EnemyAI_Rule（PatternID/PatternType/EnemyID 空值向上沿用）
        var ruleSheet = pkg.Workbook.Worksheets["EnemyAI_Rule"];
        if (ruleSheet != null)
        {
            int startRow = GetDataStartRow(ruleSheet);
            int maxRow = GetSheetMaxRow(ruleSheet);
            int lastPatternID = 0;
            string lastPatternType = "";
            int lastEnemyID = 0;
            for (int row = startRow; row <= maxRow; row++)
            {
                string cell = ruleSheet.Cells[row, FindColumnByHeader(ruleSheet, "EnemyID")].Text;
                if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(ruleSheet.Cells[row, FindColumnByHeader(ruleSheet, "PatternID")].Text) && string.IsNullOrEmpty(ruleSheet.Cells[row, FindColumnByHeader(ruleSheet, "SkillID")].Text)) continue;
                int enemyID = 0;
                if (!string.IsNullOrEmpty(cell)) enemyID = SafeGetInt(ruleSheet.Cells[row, FindColumnByHeader(ruleSheet, "EnemyID")].Value);
                string pidCell = ruleSheet.Cells[row, FindColumnByHeader(ruleSheet, "PatternID")].Text;
                string ptypeCell = ruleSheet.Cells[row, FindColumnByHeader(ruleSheet, "PatternType")].Text;
                int patternID = string.IsNullOrEmpty(pidCell) ? lastPatternID : SafeGetInt(ruleSheet.Cells[row, FindColumnByHeader(ruleSheet, "PatternID")].Value);
                string patternType = string.IsNullOrEmpty(ptypeCell) ? lastPatternType : ptypeCell;
                lastPatternID = patternID;
                lastPatternType = patternType;
                if (enemyID != 0) lastEnemyID = enemyID;
                var data = new EnemyAIRuleData
                {
                    EnemyID = enemyID == 0 ? lastEnemyID : enemyID,
                    Phase = ReadEnemyPhase(ruleSheet, row),
                    PatternID = patternID,
                    PatternType = patternType,
                    SkillID = SafeGetInt(ruleSheet.Cells[row, FindColumnByHeader(ruleSheet, "SkillID")].Value),
                    SkillIndex = SafeGetInt(ruleSheet.Cells[row, FindColumnByHeader(ruleSheet, "SkillIndex")].Value),
                    Weight = SafeGetInt(ruleSheet.Cells[row, FindColumnByHeader(ruleSheet, "Weight")].Value),
                    ScriptHook = ReadOptionalEffectText(ruleSheet, row, "ScriptHook")
                };
                if (data.EnemyID == 0) continue;
                ValidateOwnedEnemyPhase(ruleSheet, row, data.EnemyID, data.Phase);
                if (!EnemyAIRuleDict.ContainsKey(data.EnemyID)) EnemyAIRuleDict[data.EnemyID] = new List<EnemyAIRuleData>();
                TryValidate("EnemyAI_Rule", () =>
                {
                    if (!EnemySkillMainDict.TryGetValue(data.SkillID, out var skill))
                        throw new TableValidationException($"EnemyAI_Rule 第{row}行 EnemyID={data.EnemyID}, PatternID={data.PatternID}: SkillID={data.SkillID} 未在EnemySkill_Main定义");
                    if (skill.Phase != data.Phase || (data.Phase > 0 && skill.EnemySkillID / 100 != data.EnemyID))
                        throw new TableValidationException($"[EnemyAI_Rule] 第{row}行敌人{data.EnemyID}的Phase={data.Phase}只能引用本阶段技能，当前为{data.SkillID}。");
                    if (data.Phase > 0 && (!EnemyAIDict.TryGetValue(data.EnemyID, out var modes)
                        || !modes.Exists(mode => mode.Phase == data.Phase && mode.PatternID == data.PatternID)))
                        throw new TableValidationException($"[EnemyAI_Rule] 第{row}行敌人{data.EnemyID}的Phase={data.Phase}未定义模式{data.PatternID}。");
                });
                EnemyAIRuleDict[data.EnemyID].Add(data);
            }
        }
    }
}
