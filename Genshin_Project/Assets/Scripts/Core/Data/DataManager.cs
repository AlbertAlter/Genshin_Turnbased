using System;
using System.Collections.Generic;
using System.IO;
using OfficeOpenXml;
using UnityEngine;

public partial class DataManager : Singleton<DataManager>
{
    // ========== Characters ==========
    public List<CharacterOverviewData> CharacterOverviewList = new List<CharacterOverviewData>();
    public Dictionary<int, CharacterAttributesData> CharacterAttributesDict = new Dictionary<int, CharacterAttributesData>();
    public Dictionary<int, List<CharacterAscensionData>> CharacterAscensionDict = new Dictionary<int, List<CharacterAscensionData>>();
    public Dictionary<int, List<SkillMainData>> CharacterSkillDict = new Dictionary<int, List<SkillMainData>>();
    public Dictionary<string, SkillEffectData> SkillEffectDict = new Dictionary<string, SkillEffectData>();
    public Dictionary<string, Dictionary<int, SkillLevelData>> SkillLevelDict = new Dictionary<string, Dictionary<int, SkillLevelData>>();
    public Dictionary<int, List<ConstellationData>> ConstellationDict = new Dictionary<int, List<ConstellationData>>();
    public Dictionary<int, List<TalentData>> TalentDict = new Dictionary<int, List<TalentData>>();

    // ========== Growth Curve ==========
    // 曲线ID -> (等级 -> 倍率)，曲线ID对应角色Attributes.GrowthCurveID
    public Dictionary<int, Dictionary<int, float>> GrowthCurveDict = new Dictionary<int, Dictionary<int, float>>();

    // ========== Status ==========
    public Dictionary<string, StatusMainData> StatusMainDict = new Dictionary<string, StatusMainData>();
    public Dictionary<string, StatusEffectData> StatusEffectDict = new Dictionary<string, StatusEffectData>();
    public Dictionary<string, List<StatusActionData>> StatusActionDict = new Dictionary<string, List<StatusActionData>>();
    public Dictionary<string, StatusAttributesData> StatusAttributesDict = new Dictionary<string, StatusAttributesData>();

    // ========== Enemy ==========
    public Dictionary<int, EnemyMainData> EnemyMainDict = new Dictionary<int, EnemyMainData>();
    public Dictionary<int, EnemySkillMainData> EnemySkillMainDict = new Dictionary<int, EnemySkillMainData>();
    public Dictionary<string, EnemySkillEffectData> EnemySkillEffectDict = new Dictionary<string, EnemySkillEffectData>();
    public Dictionary<string, EnemySkillParamData> EnemySkillParamDict = new Dictionary<string, EnemySkillParamData>();
    public Dictionary<int, List<EnemyAIData>> EnemyAIDict = new Dictionary<int, List<EnemyAIData>>();
    public Dictionary<int, List<EnemyAIRuleData>> EnemyAIRuleDict = new Dictionary<int, List<EnemyAIRuleData>>();

    // ========== Enemy Curves（敌人等级基础值曲线，EnemyAttributes.xlsx 其余sheet） ==========
    public Dictionary<int, float> BaseHPDict = new Dictionary<int, float>();          // 等级 -> 基础生命
    public Dictionary<int, float> BaseATK1Dict = new Dictionary<int, float>();        // 等级 -> 基础攻击（曲线1）
    public Dictionary<int, float> BaseATK2Dict = new Dictionary<int, float>();        // 等级 -> 基础攻击（曲线2）

    // ========== Reaction ==========
    public Dictionary<int, float> ReactionLevelCoefficientDict = new Dictionary<int, float>();

    // ========== Stage ==========
    public Dictionary<int, StageConfigData> StageConfigDict = new Dictionary<int, StageConfigData>();

    // ========== Lookup ==========
    public Dictionary<string, int> SkillStringToInt = new Dictionary<string, int>();

    // 程序运行时读取的目录：StreamingAssets/Data（由编辑器工具 Tools/Sync Charts to StreamingAssets 从 Charts 同步而来）
    private string ChartsPath => Path.Combine(Application.streamingAssetsPath, "Data");

    protected override void Init()
    {
        ReloadAllData();
    }

    // ================================================================
    //  通用读取辅助：探测数据起始行 + 安全行范围
    //  约定：第1行永远是表头。若第2行第一个单元格能解析为数字，
    //  说明没有"类型行"（Type,string,int...），数据从第2行开始；
    //  否则第2行是类型行，数据从第3行开始。
    // ================================================================
    static int GetDataStartRow(ExcelWorksheet sheet)
    {
        if (sheet.Dimension == null) return 2;
        string c1 = sheet.Cells[2, 1].Text;
        if (int.TryParse(c1, out _)) return 2;   // 无类型行，数据从第2行开始
        return 3;                                 // 有类型行，数据从第3行开始
    }

    static int GetSheetMaxRow(ExcelWorksheet sheet)
    {
        if (sheet.Dimension == null) return 1;
        return sheet.Dimension.End.Row;
    }

    // ================================================================
    //  Characters.xlsx 的 Sheet1
    // ================================================================
    void LoadCharacterOverview()
    {
        string path = Path.Combine(ChartsPath, "Characters.xlsx");
        if (!File.Exists(path)) { Debug.LogWarning("StreamingAssets/Data/Characters.xlsx not found"); return; }
        using var pkg = new ExcelPackage(new FileInfo(path));
        var sheet = pkg.Workbook.Worksheets[0];
        if (sheet == null) return;
        int startRow = GetDataStartRow(sheet);
        int maxRow = GetSheetMaxRow(sheet);
        for (int row = startRow; row <= maxRow; row++)
        {
            string cell = sheet.Cells[row, 1].Text;
            if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(sheet.Cells[row, 2].Text)) continue;
            var data = new CharacterOverviewData
            {
                CharacterID = SafeGetInt(sheet.Cells[row, 1].Value),
                Name = sheet.Cells[row, 2].Text
            };
            if (data.CharacterID == 0) continue;
            CharacterOverviewList.Add(data);
        }
    }

    // ================================================================
    //  Characters/{id}_{name}.xlsx 全部 sheets
    //  跳过 0_ 开头的模板文件；跳过 CharacterID 为 0 的空行
    // ================================================================
    void LoadCharacterSheets()
    {
        string charDir = Path.Combine(ChartsPath, "Characters");
        if (!Directory.Exists(charDir)) { Debug.LogWarning("StreamingAssets/Data/Characters/ not found"); return; }
        var files = Directory.GetFiles(charDir, "*.xlsx");
        foreach (string f in files)
        {
            string fileName = Path.GetFileNameWithoutExtension(f);
            if (fileName.StartsWith("0_")) continue; // 跳过模板
            try
            {
                using var pkg = new ExcelPackage(new FileInfo(f));
                var sheets = pkg.Workbook.Worksheets;
                if (sheets.Count == 0) continue;

                // 每个角色工作簿只描述一个角色。Ascension 表本身没有 CharacterID 列，
                // 因此角色 ID 必须从同一工作簿的 Attributes 表取得。
                int workbookCharacterId = 0;

                var attrSheet = sheets["Attributes"];
                if (attrSheet != null)
                {
                    int startRow = GetDataStartRow(attrSheet);
                    int maxRow = GetSheetMaxRow(attrSheet);
                    for (int row = startRow; row <= maxRow; row++)
                    {
                        string cell = attrSheet.Cells[row, 1].Text;
                        if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(attrSheet.Cells[row, 2].Text)) continue;
                        var data = new CharacterAttributesData
                        {
                            CharacterID = SafeGetInt(attrSheet.Cells[row, 1].Value),
                            NameID = attrSheet.Cells[row, 2].Text,
                            Name = attrSheet.Cells[row, 3].Text,
                            Star = SafeGetInt(attrSheet.Cells[row, 4].Value),
                            GrowthCurveID = SafeGetInt(attrSheet.Cells[row, 5].Value),
                            WeaponType = SafeGetInt(attrSheet.Cells[row, 6].Value),
                            Element = attrSheet.Cells[row, 7].Text,
                            BaseHP = SafeGetFloat(attrSheet.Cells[row, 8].Value),
                            BaseATK = SafeGetFloat(attrSheet.Cells[row, 9].Value),
                            BaseDEF = SafeGetFloat(attrSheet.Cells[row, 10].Value),
                            MaxEnergy = SafeGetInt(attrSheet.Cells[row, 11].Value),
                            Poise = SafeGetFloat(attrSheet.Cells[row, 12].Value),
                            HitWeight = SafeGetFloat(attrSheet.Cells[row, 13].Value),
                            NormalAttackID = attrSheet.Cells[row, 14].Text,
                            HeavyAttackID = attrSheet.Cells[row, 15].Text,
                            ElementalSkillID = attrSheet.Cells[row, 16].Text,
                            ElementalBurstID = attrSheet.Cells[row, 17].Text,
                            BaseEM = SafeGetInt(attrSheet.Cells[row, 18].Value),
                            BaseRechargeRate = SafeGetFloat(attrSheet.Cells[row, 19].Value),
                            CritRate = SafeGetFloat(attrSheet.Cells[row, 20].Value),
                            CritDMG = SafeGetFloat(attrSheet.Cells[row, 21].Value),
                            PyroRes = SafeGetFloat(attrSheet.Cells[row, 22].Value),
                            HydroRes = SafeGetFloat(attrSheet.Cells[row, 23].Value),
                            ElectroRes = SafeGetFloat(attrSheet.Cells[row, 24].Value),
                            CryoRes = SafeGetFloat(attrSheet.Cells[row, 25].Value),
                            AnemoRes = SafeGetFloat(attrSheet.Cells[row, 26].Value),
                            DendroRes = SafeGetFloat(attrSheet.Cells[row, 27].Value),
                            GeoRes = SafeGetFloat(attrSheet.Cells[row, 28].Value),
                            PhysicalRes = SafeGetFloat(attrSheet.Cells[row, 29].Value),
                            PyroDmgBonus = SafeGetFloat(attrSheet.Cells[row, 30].Value),
                            HydroDmgBonus = SafeGetFloat(attrSheet.Cells[row, 31].Value),
                            ElectroDmgBonus = SafeGetFloat(attrSheet.Cells[row, 32].Value),
                            CryoDmgBonus = SafeGetFloat(attrSheet.Cells[row, 33].Value),
                            AnemoDmgBonus = SafeGetFloat(attrSheet.Cells[row, 34].Value),
                            GeoDmgBonus = SafeGetFloat(attrSheet.Cells[row, 35].Value),
                            DendroDmgBonus = SafeGetFloat(attrSheet.Cells[row, 36].Value),
                            PhysicalDmgBonus = SafeGetFloat(attrSheet.Cells[row, 37].Value)
                        };
                        if (data.CharacterID == 0) continue;
                        if (workbookCharacterId != 0 && workbookCharacterId != data.CharacterID)
                            throw new InvalidDataException($"角色表 {fileName} 的 Attributes 中出现了多个 CharacterID");
                        workbookCharacterId = data.CharacterID;
                        CharacterAttributesDict[data.CharacterID] = data;
                    }
                }

                var ascSheet = sheets["Ascension"];
                if (ascSheet != null)
                {
                    if (workbookCharacterId == 0)
                        throw new InvalidDataException($"角色表 {fileName} 无法从 Attributes 取得 CharacterID");

                    int startRow = GetDataStartRow(ascSheet);
                    int maxRow = GetSheetMaxRow(ascSheet);
                    for (int row = startRow; row <= maxRow; row++)
                    {
                        string cell = ascSheet.Cells[row, 1].Text;
                        if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(ascSheet.Cells[row, 2].Text)) continue;
                        var data = new CharacterAscensionData
                        {
                            AscensionLevel = SafeGetInt(ascSheet.Cells[row, 1].Value),
                            BaseHPFlat = SafeGetFloat(ascSheet.Cells[row, 2].Value),
                            BaseATKFlat = SafeGetFloat(ascSheet.Cells[row, 3].Value),
                            BaseDEFFlat = SafeGetFloat(ascSheet.Cells[row, 4].Value),
                            HPBonus = SafeGetFloat(ascSheet.Cells[row, 5].Value),
                            ATKBonus = SafeGetFloat(ascSheet.Cells[row, 6].Value),
                            DEFBonus = SafeGetFloat(ascSheet.Cells[row, 7].Value),
                            CritRate = SafeGetFloat(ascSheet.Cells[row, 8].Value),
                            CritDMG = SafeGetFloat(ascSheet.Cells[row, 9].Value),
                            EM = SafeGetFloat(ascSheet.Cells[row, 10].Value),
                            EnergyRechargeRate = SafeGetFloat(ascSheet.Cells[row, 11].Value),
                            PyroDmgBonus = SafeGetFloat(ascSheet.Cells[row, 12].Value),
                            HydroDmgBonus = SafeGetFloat(ascSheet.Cells[row, 13].Value),
                            ElectroDmgBonus = SafeGetFloat(ascSheet.Cells[row, 14].Value),
                            CryoDmgBonus = SafeGetFloat(ascSheet.Cells[row, 15].Value),
                            AnemoDmgBonus = SafeGetFloat(ascSheet.Cells[row, 16].Value),
                            GeoDmgBonus = SafeGetFloat(ascSheet.Cells[row, 17].Value),
                            DendroDmgBonus = SafeGetFloat(ascSheet.Cells[row, 18].Value),
                            PhysicalDmgBonus = SafeGetFloat(ascSheet.Cells[row, 19].Value)
                        };
                        if (!CharacterAscensionDict.ContainsKey(workbookCharacterId))
                            CharacterAscensionDict[workbookCharacterId] = new List<CharacterAscensionData>();
                        CharacterAscensionDict[workbookCharacterId].Add(data);
                    }
                }

                var skillSheet = sheets["Skills"];
                if (skillSheet != null)
                {
                    int startRow = GetDataStartRow(skillSheet);
                    int maxRow = GetSheetMaxRow(skillSheet);
                    int charId = 0;
                    for (int row = startRow; row <= maxRow; row++)
                    {
                        string cell = skillSheet.Cells[row, 1].Text;
                        if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(skillSheet.Cells[row, 2].Text)) continue;
                        var data = new SkillMainData
                        {
                            SkillID = SafeGetInt(skillSheet.Cells[row, 1].Value),
                            SkillID2 = skillSheet.Cells[row, 2].Text,
                            SkillName = skillSheet.Cells[row, 3].Text,
                            APCost = SafeGetInt(skillSheet.Cells[row, 4].Value),
                            Cooldown = SafeGetFloat(skillSheet.Cells[row, 5].Value),
                            EnergyUsed = SafeGetInt(skillSheet.Cells[row, 6].Value),
                            MaxCharge = SafeGetInt(skillSheet.Cells[row, 7].Value),
                            InitialCharge = SafeGetInt(skillSheet.Cells[row, 8].Value),
                            SkillPhase = SafeGetInt(skillSheet.Cells[row, 9].Value),
                            ActionType = skillSheet.Cells[row, 10].Text,
                            Description = skillSheet.Cells[row, 11].Text
                        };
                        // 角色ID = SkillID / 100（SkillID规则：角色ID + 从01开始的两位序号，如 1009 -> 100901）
                        if (charId == 0 && data.SkillID != 0) charId = data.SkillID / 100;
                        if (charId == 0) continue;
                        if (!CharacterSkillDict.ContainsKey(charId)) CharacterSkillDict[charId] = new List<SkillMainData>();
                        CharacterSkillDict[charId].Add(data);
                        if (!string.IsNullOrEmpty(data.SkillID2)) SkillStringToInt[data.SkillID2] = data.SkillID;
                    }
                }

                var seSheet = sheets["SkillsEffect"];
                if (seSheet != null)
                {
                    int startRow = GetDataStartRow(seSheet);
                    int maxRow = GetSheetMaxRow(seSheet);
                    for (int row = startRow; row <= maxRow; row++)
                    {
                        string cell = seSheet.Cells[row, 2].Text;
                        if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(seSheet.Cells[row, 1].Text)) continue;
                        var data = new SkillEffectData
                        {
                            SkillEffectID = SafeGetInt(seSheet.Cells[row, 1].Value),
                            SkillEffectID2 = seSheet.Cells[row, 2].Text,
                            EffectIndex = SafeGetInt(seSheet.Cells[row, 3].Value),
                            EffectType = seSheet.Cells[row, 4].Text,
                            Element = seSheet.Cells[row, 5].Text,
                            DamageType = seSheet.Cells[row, 6].Text,
                            Duration = SafeGetInt(seSheet.Cells[row, 7].Value),
                            AddInPhase = SafeGetInt(seSheet.Cells[row, 8].Value),
                            TriggerPhase = SafeGetInt(seSheet.Cells[row, 9].Value),
                            Param1 = seSheet.Cells[row, 10].Text,
                            Param2 = seSheet.Cells[row, 11].Text,
                            Param3 = seSheet.Cells[row, 12].Text,
                            TargetType = seSheet.Cells[row, 13].Text,
                            TargetNumber = SafeGetInt(seSheet.Cells[row, 14].Value),
                            TargetConsecutive = SafeGetInt(seSheet.Cells[row, 15].Value),
                            TargetConsecutiveSet = !string.IsNullOrEmpty(seSheet.Cells[row, 15].Text),
                            TargetOverride = seSheet.Cells[row, 16].Text,
                            ScriptHook = seSheet.Cells[row, 17].Text
                        };
                        TryValidate("SkillsEffect", () => TableValidator.ValidateEffectRow("SkillsEffect", row,
                            data.EffectType, data.Element, data.Duration, data.Param2,
                            data.TargetType, data.TargetNumber, data.TargetConsecutive,
                            data.TargetOverride, isEnemy: false));
                        // ChangeControl 专属校验（2026-08-11）：技能效果表允许，填了Duration定时结束，没填=永久
                        if (data.EffectType == "ChangeControl")
                            TryValidate("SkillsEffect", () => TableValidator.ValidateChangeControl("SkillsEffect", row,
                                data.Param1, data.Param2, data.Param3, data.Duration,
                                isStatusEffectTable: false, isEnemy: false));
                        if (string.IsNullOrEmpty(data.SkillEffectID2)) continue;
                        SkillEffectDict[data.SkillEffectID2] = data;
                    }
                }

                var slSheet = sheets["SkillLevel"];
                if (slSheet != null)
                {
                    int startRow = GetDataStartRow(slSheet);
                    int maxRow = GetSheetMaxRow(slSheet);
                    int charId = 0;
                    for (int row = startRow; row <= maxRow; row++)
                    {
                        string cid = slSheet.Cells[row, 1].Text;
                        string stype = slSheet.Cells[row, 2].Text;
                        string slevel = slSheet.Cells[row, 3].Text;
                        if (string.IsNullOrEmpty(cid) && string.IsNullOrEmpty(stype) && string.IsNullOrEmpty(slevel)) continue;
                        if (!string.IsNullOrEmpty(cid)) charId = SafeGetInt(slSheet.Cells[row, 1].Value);
                        if (charId == 0) continue;
                        int skillType = 0;
                        if (!int.TryParse(stype, out skillType))
            {
                switch (stype.Trim())
                {
                    case "Normal": skillType = 0; break;
                    case "Heavy": skillType = 1; break;
                    case "Skill": skillType = 2; break;
                    case "Burst": skillType = 3; break;
                }
            }
                        int skillLevel = 0;
                        int.TryParse(slevel, out skillLevel);
                        if (skillLevel <= 0) continue;
                        string key = $"{charId}_{skillType}";
                        var data = new SkillLevelData
                        {
                            CharacterID = charId,
                            SkillType = skillType,
                            SkillLevel = skillLevel,
                            ParamID = slSheet.Cells[row, 4].Text,
                            Hits1 = slSheet.Cells[row, 5].Text,
                            Hits2 = slSheet.Cells[row, 6].Text,
                            Hits3 = slSheet.Cells[row, 7].Text,
                            Hits4 = slSheet.Cells[row, 8].Text,
                            Hits5 = slSheet.Cells[row, 9].Text,
                            Hits6 = slSheet.Cells[row, 10].Text,
                            Hits7 = slSheet.Cells[row, 11].Text
                        };
                        if (!SkillLevelDict.ContainsKey(key)) SkillLevelDict[key] = new Dictionary<int, SkillLevelData>();
                        SkillLevelDict[key][skillLevel] = data;
                    }
                }

                var conSheet = sheets["Constellation"];
                if (conSheet != null)
                {
                    int startRow = GetDataStartRow(conSheet);
                    int maxRow = GetSheetMaxRow(conSheet);
                    int charId = 0;
                    for (int row = startRow; row <= maxRow; row++)
                    {
                        string cell = conSheet.Cells[row, 1].Text;
                        if (!string.IsNullOrEmpty(cell)) charId = SafeGetInt(conSheet.Cells[row, 1].Value);
                        if (charId == 0) continue;
                        if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(conSheet.Cells[row, 2].Text)) continue;
                        var data = new ConstellationData
                        {
                            CharacterID = charId,
                            ConstellationIndex = SafeGetInt(conSheet.Cells[row, 2].Value),
                            ConstellationName = conSheet.Cells[row, 3].Text,
                            Modifiers = conSheet.Cells[row, 4].Text,
                            Description = conSheet.Cells[row, 5].Text
                        };
                        if (!ConstellationDict.ContainsKey(charId)) ConstellationDict[charId] = new List<ConstellationData>();
                        ConstellationDict[charId].Add(data);
                    }
                }

                var talSheet = sheets["Talent"];
                if (talSheet != null)
                {
                    int startRow = GetDataStartRow(talSheet);
                    int maxRow = GetSheetMaxRow(talSheet);
                    int charId = 0;
                    for (int row = startRow; row <= maxRow; row++)
                    {
                        string cell = talSheet.Cells[row, 1].Text;
                        if (!string.IsNullOrEmpty(cell)) charId = SafeGetInt(talSheet.Cells[row, 1].Value);
                        if (charId == 0) continue;
                        if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(talSheet.Cells[row, 2].Text)) continue;
                        var data = new TalentData
                        {
                            CharacterID = charId,
                            TalentIndex = SafeGetInt(talSheet.Cells[row, 2].Value),
                            TalentName = talSheet.Cells[row, 3].Text,
                            UnlockAfter = SafeGetInt(talSheet.Cells[row, 4].Value),
                            Modifiers = talSheet.Cells[row, 5].Text,
                            Description = talSheet.Cells[row, 6].Text
                        };
                        if (!TalentDict.ContainsKey(charId)) TalentDict[charId] = new List<TalentData>();
                        TalentDict[charId].Add(data);
                    }
                }
            }
            catch (TableValidationException)
            {
                throw; // 配表校验失败：直接中断加载，不吞异常
            }
            catch (Exception e)
            {
                _loadErrors.Add($"[Characters/{Path.GetFileName(f)}] {e.Message}");
            }
        }
    }

    // ================================================================
    //  Character_Growth_Curve.xlsx（多sheet：GrowthCurve_1 / GrowthCurve_2）
    //  按 sheet 序号作为曲线ID（第1张=1，第2张=2，依此类推）
    // ================================================================
    void LoadGrowthCurve()
    {
        string path = Path.Combine(ChartsPath, "Character_Growth_Curve.xlsx");
        if (!File.Exists(path)) return;
        using var pkg = new ExcelPackage(new FileInfo(path));
        for (int s = 0; s < pkg.Workbook.Worksheets.Count; s++)
        {
            var sheet = pkg.Workbook.Worksheets[s];
            if (sheet == null) continue;
            int curveID = s + 1; // 曲线ID从1开始
            if (!GrowthCurveDict.ContainsKey(curveID)) GrowthCurveDict[curveID] = new Dictionary<int, float>();
            int startRow = GetDataStartRow(sheet);
            int maxRow = GetSheetMaxRow(sheet);
            for (int row = startRow; row <= maxRow; row++)
            {
                string cell = sheet.Cells[row, 1].Text;
                if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(sheet.Cells[row, 2].Text)) continue;
                GrowthCurveDict[curveID][SafeGetInt(sheet.Cells[row, 1].Value)] = SafeGetFloat(sheet.Cells[row, 2].Value);
            }
        }
    }

    // 查询指定曲线在指定等级的成长倍率；曲线不存在或等级不存在返回1f
    public float GetGrowthCurve(int curveID, int level)
    {
        if (GrowthCurveDict.TryGetValue(curveID, out var levelMap))
        {
            if (levelMap.TryGetValue(level, out float mult)) return mult;
        }
        return 1f;
    }

    // ================================================================
    //  StatusData.xlsx 4 sheets
    // ================================================================
    void LoadStatusData()
    {
        string path = Path.Combine(ChartsPath, "StatusData.xlsx");
        if (!File.Exists(path)) { Debug.LogWarning("StatusData.xlsx not found"); return; }
        using var pkg = new ExcelPackage(new FileInfo(path));

        // StatusData_Main
        var mainSheet = pkg.Workbook.Worksheets["StatusData_Main"];
        if (mainSheet != null)
        {
            int startRow = GetDataStartRow(mainSheet);
            int maxRow = GetSheetMaxRow(mainSheet);
            for (int row = startRow; row <= maxRow; row++)
            {
                string cell = mainSheet.Cells[row, 2].Text;
                if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(mainSheet.Cells[row, 1].Text)) continue;
                var data = new StatusMainData
                {
                    StatusID = SafeGetInt(mainSheet.Cells[row, 1].Value),
                    StatusID2 = cell,
                    StatusName = mainSheet.Cells[row, 3].Text,
                    StatusType = mainSheet.Cells[row, 4].Text,
                    Display = SafeGetInt(mainSheet.Cells[row, 5].Value),
                    Description = mainSheet.Cells[row, 6].Text,
                    MultiplierPart1 = mainSheet.Cells[row, 7].Text,
                    MultiplierPart2 = mainSheet.Cells[row, 8].Text,
                    MultiplierPart3 = mainSheet.Cells[row, 9].Text,
                    ApplyDamageType = mainSheet.Cells[row, 10].Text,
                    ApplyReactionType = mainSheet.Cells[row, 11].Text,
                    ApplyElementType = mainSheet.Cells[row, 12].Text,
                    ApplyString = mainSheet.Cells[row, 13].Text,
                    ScriptHook = mainSheet.Cells[row, 14].Text,
                    MaxCount = SafeGetInt(mainSheet.Cells[row, 15].Value),
                    MaxStack = SafeGetInt(mainSheet.Cells[row, 16].Value),
                    WhenMax = mainSheet.Cells[row, 17].Text
                };
                if (string.IsNullOrEmpty(data.StatusID2)) continue;
                StatusMainDict[data.StatusID2] = data;
            }
        }

        // Status_Effect
        var effSheet = pkg.Workbook.Worksheets["Status_Effect"];
        if (effSheet != null)
        {
            int startRow = GetDataStartRow(effSheet);
            int maxRow = GetSheetMaxRow(effSheet);
            for (int row = startRow; row <= maxRow; row++)
            {
                string cell = effSheet.Cells[row, 2].Text;
                if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(effSheet.Cells[row, 1].Text)) continue;
                var data = new StatusEffectData
                {
                    StatusEffectID = SafeGetInt(effSheet.Cells[row, 1].Value),
                    StatusEffectID2 = cell,
                    EffectIndex = SafeGetInt(effSheet.Cells[row, 3].Value),
                    EffectType = effSheet.Cells[row, 4].Text,
                    Element = effSheet.Cells[row, 5].Text,
                    DamageType = effSheet.Cells[row, 6].Text,
                    Duration = SafeGetInt(effSheet.Cells[row, 7].Value),
                    AddInPhase = SafeGetInt(effSheet.Cells[row, 8].Value),
                    TriggerPhase = SafeGetInt(effSheet.Cells[row, 9].Value),
                    Param1 = effSheet.Cells[row, 10].Text,
                    Param2 = effSheet.Cells[row, 11].Text,
                    Param3 = effSheet.Cells[row, 12].Text,
                    TargetType = effSheet.Cells[row, 13].Text,
                    // 状态效果表第14列=TargetNumber：
                    // 含逗号（"1,1"相邻范围，如兔兔伯爵爆炸）读进 TargetSelect；数字（-1等）不读（状态效果目标数量暂不支持）
                    TargetSelect = effSheet.Cells[row, 14].Text.Contains(",") ? effSheet.Cells[row, 14].Text : "",
                    TargetConsecutive = SafeGetInt(effSheet.Cells[row, 15].Value),
                    TargetOverride = effSheet.Cells[row, 16].Text,
                    ScriptHook = effSheet.Cells[row, 17].Text
                };
                TryValidate("Status_Effect", () => TableValidator.ValidateEffectRow("Status_Effect", row,
                    data.EffectType, data.Element, data.Duration, data.Param2,
                    data.TargetType, 0, data.TargetConsecutive,
                    data.TargetOverride, isEnemy: false, checkTargetNumber: false));
                // ChangeControl 专属校验（2026-08-11）：状态效果表挂载，跟状态 duration 走（效果行不填 Duration）
                if (data.EffectType == "ChangeControl")
                    TryValidate("Status_Effect", () => TableValidator.ValidateChangeControl("Status_Effect", row,
                        data.Param1, data.Param2, data.Param3, data.Duration,
                        isStatusEffectTable: true, isEnemy: false));
                if (string.IsNullOrEmpty(data.StatusEffectID2)) continue;
                StatusEffectDict[data.StatusEffectID2] = data;
            }
        }

        // Status_Action
        var actSheet = pkg.Workbook.Worksheets["Status_Action"];
        if (actSheet != null)
        {
            int startRow = GetDataStartRow(actSheet);
            int maxRow = GetSheetMaxRow(actSheet);
            for (int row = startRow; row <= maxRow; row++)
            {
                string cell = actSheet.Cells[row, 2].Text;
                if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(actSheet.Cells[row, 1].Text)) continue;
                var data = new StatusActionData
                {
                    StatusID = SafeGetInt(actSheet.Cells[row, 1].Value),
                    StatusID2 = cell,
                    ActionType = actSheet.Cells[row, 3].Text,
                    Param1 = actSheet.Cells[row, 4].Text,
                    Param2 = actSheet.Cells[row, 5].Text,
                    Param3 = actSheet.Cells[row, 6].Text,
                    MaxTimePerTurn = SafeGetInt(actSheet.Cells[row, 7].Value),
                    MaxTimePerLife = SafeGetInt(actSheet.Cells[row, 8].Value),
                    Cooldown = SafeGetInt(actSheet.Cells[row, 9].Value),
                    HitSource = actSheet.Cells[row, 10].Text,
                    ScriptHook = actSheet.Cells[row, 11].Text
                };
                if (string.IsNullOrEmpty(data.StatusID2)) continue;
                if (!StatusActionDict.ContainsKey(data.StatusID2)) StatusActionDict[data.StatusID2] = new List<StatusActionData>();
                StatusActionDict[data.StatusID2].Add(data);
            }
        }

        // Status_Attributes
        var attrSheet = pkg.Workbook.Worksheets["Status_Attributes"];
        if (attrSheet != null)
        {
            int startRow = GetDataStartRow(attrSheet);
            int maxRow = GetSheetMaxRow(attrSheet);
            for (int row = startRow; row <= maxRow; row++)
            {
                string cell = attrSheet.Cells[row, 2].Text;
                if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(attrSheet.Cells[row, 1].Text)) continue;
                var data = new StatusAttributesData
                {
                    StatusID = SafeGetInt(attrSheet.Cells[row, 1].Value),
                    StatusID2 = cell,
                    TotalHP = SafeGetInt(attrSheet.Cells[row, 3].Value),
                    TotalATK = SafeGetInt(attrSheet.Cells[row, 4].Value),
                    TotalDEF = SafeGetInt(attrSheet.Cells[row, 5].Value),
                    TotalEM = SafeGetInt(attrSheet.Cells[row, 6].Value),
                    TotalRechargeRate = SafeGetInt(attrSheet.Cells[row, 7].Value),
                    CritRate = SafeGetInt(attrSheet.Cells[row, 8].Value),
                    CritDMG = SafeGetInt(attrSheet.Cells[row, 9].Value),
                    BaseDMGBonusFlat = SafeGetInt(attrSheet.Cells[row, 10].Value),
                    DMGBonus = SafeGetInt(attrSheet.Cells[row, 11].Value),
                    DEFReduction = SafeGetInt(attrSheet.Cells[row, 12].Value),
                    DEFIgnored = SafeGetInt(attrSheet.Cells[row, 13].Value),
                    ResBonus = SafeGetInt(attrSheet.Cells[row, 14].Value),
                    ShieldStrength = SafeGetInt(attrSheet.Cells[row, 15].Value),
                    HealBonus = SafeGetInt(attrSheet.Cells[row, 16].Value),
                    BeHealedBonus = SafeGetInt(attrSheet.Cells[row, 17].Value),
                    Elevation = SafeGetInt(attrSheet.Cells[row, 18].Value),
                    BaseDMGBonus = SafeGetInt(attrSheet.Cells[row, 19].Value),
                    LunarDMGBonus = SafeGetInt(attrSheet.Cells[row, 20].Value),
                    StellarDMGBonus = SafeGetInt(attrSheet.Cells[row, 21].Value)
                };
                if (string.IsNullOrEmpty(data.StatusID2)) continue;
                StatusAttributesDict[data.StatusID2] = data;
            }
        }
    }

    // ================================================================
    //  StatusData_Enemy.xlsx（敌人状态表，2026-08-14 单独拆分，结构与主状态表一致）
    //  sheet：StatusData_Main_Enemy / Status_Action_Enemy / Status_Effect / Status_Attributes
    //  加载进共用的 StatusMainDict / StatusActionDict / StatusEffectDict / StatusAttributesDict
    // ================================================================
    void LoadEnemyStatusData()
    {
        string path = Path.Combine(ChartsPath, "StatusData_Enemy.xlsx");
        if (!File.Exists(path)) { Debug.LogWarning("StatusData_Enemy.xlsx not found"); return; }
        using var pkg = new ExcelPackage(new FileInfo(path));

        // StatusData_Main_Enemy
        var mainSheet = pkg.Workbook.Worksheets["StatusData_Main_Enemy"];
        if (mainSheet != null)
        {
            int startRow = GetDataStartRow(mainSheet);
            int maxRow = GetSheetMaxRow(mainSheet);
            for (int row = startRow; row <= maxRow; row++)
            {
                string cell = mainSheet.Cells[row, 2].Text;
                if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(mainSheet.Cells[row, 1].Text)) continue;
                var data = new StatusMainData
                {
                    StatusID = SafeGetInt(mainSheet.Cells[row, 1].Value),
                    StatusID2 = cell,
                    StatusName = mainSheet.Cells[row, 3].Text,
                    StatusType = mainSheet.Cells[row, 4].Text,
                    Display = SafeGetInt(mainSheet.Cells[row, 5].Value),
                    Description = mainSheet.Cells[row, 6].Text,
                    MultiplierPart1 = mainSheet.Cells[row, 7].Text,
                    MultiplierPart2 = mainSheet.Cells[row, 8].Text,
                    MultiplierPart3 = mainSheet.Cells[row, 9].Text,
                    ApplyDamageType = mainSheet.Cells[row, 10].Text,
                    ApplyReactionType = mainSheet.Cells[row, 11].Text,
                    ApplyElementType = mainSheet.Cells[row, 12].Text,
                    ApplyString = mainSheet.Cells[row, 13].Text,
                    ScriptHook = mainSheet.Cells[row, 14].Text,
                    MaxCount = SafeGetInt(mainSheet.Cells[row, 15].Value),
                    MaxStack = SafeGetInt(mainSheet.Cells[row, 16].Value),
                    WhenMax = mainSheet.Cells[row, 17].Text
                };
                if (string.IsNullOrEmpty(data.StatusID2)) continue;
                StatusMainDict[data.StatusID2] = data;
            }
        }

        // Status_Effect
        var effSheet = pkg.Workbook.Worksheets["Status_Effect"];
        if (effSheet != null)
        {
            int startRow = GetDataStartRow(effSheet);
            int maxRow = GetSheetMaxRow(effSheet);
            for (int row = startRow; row <= maxRow; row++)
            {
                string cell = effSheet.Cells[row, 2].Text;
                if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(effSheet.Cells[row, 1].Text)) continue;
                var data = new StatusEffectData
                {
                    StatusEffectID = SafeGetInt(effSheet.Cells[row, 1].Value),
                    StatusEffectID2 = cell,
                    EffectIndex = SafeGetInt(effSheet.Cells[row, 3].Value),
                    EffectType = effSheet.Cells[row, 4].Text,
                    Element = effSheet.Cells[row, 5].Text,
                    DamageType = effSheet.Cells[row, 6].Text,
                    Duration = SafeGetInt(effSheet.Cells[row, 7].Value),
                    AddInPhase = SafeGetInt(effSheet.Cells[row, 8].Value),
                    TriggerPhase = SafeGetInt(effSheet.Cells[row, 9].Value),
                    Param1 = effSheet.Cells[row, 10].Text,
                    Param2 = effSheet.Cells[row, 11].Text,
                    Param3 = effSheet.Cells[row, 12].Text,
                    TargetType = effSheet.Cells[row, 13].Text,
                    TargetSelect = effSheet.Cells[row, 14].Text.Contains(",") ? effSheet.Cells[row, 14].Text : "",
                    TargetConsecutive = SafeGetInt(effSheet.Cells[row, 15].Value),
                    TargetOverride = effSheet.Cells[row, 16].Text,
                    ScriptHook = effSheet.Cells[row, 17].Text
                };
                TryValidate("Status_Effect(Enemy)", () => TableValidator.ValidateEffectRow("Status_Effect(Enemy)", row,
                    data.EffectType, data.Element, data.Duration, data.Param2,
                    data.TargetType, 0, data.TargetConsecutive,
                    data.TargetOverride, isEnemy: true, checkTargetNumber: false));
                if (data.EffectType == "ChangeControl")
                    TryValidate("Status_Effect(Enemy)", () => TableValidator.ValidateChangeControl("Status_Effect(Enemy)", row,
                        data.Param1, data.Param2, data.Param3, data.Duration,
                        isStatusEffectTable: true, isEnemy: true));
                if (string.IsNullOrEmpty(data.StatusEffectID2)) continue;
                StatusEffectDict[data.StatusEffectID2] = data;
            }
        }

        // Status_Action_Enemy
        var actSheet = pkg.Workbook.Worksheets["Status_Action_Enemy"];
        if (actSheet != null)
        {
            int startRow = GetDataStartRow(actSheet);
            int maxRow = GetSheetMaxRow(actSheet);
            for (int row = startRow; row <= maxRow; row++)
            {
                string cell = actSheet.Cells[row, 2].Text;
                if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(actSheet.Cells[row, 1].Text)) continue;
                var data = new StatusActionData
                {
                    StatusID = SafeGetInt(actSheet.Cells[row, 1].Value),
                    StatusID2 = cell,
                    ActionType = actSheet.Cells[row, 3].Text,
                    Param1 = actSheet.Cells[row, 4].Text,
                    Param2 = actSheet.Cells[row, 5].Text,
                    Param3 = actSheet.Cells[row, 6].Text,
                    MaxTimePerTurn = SafeGetInt(actSheet.Cells[row, 7].Value),
                    MaxTimePerLife = SafeGetInt(actSheet.Cells[row, 8].Value),
                    Cooldown = SafeGetInt(actSheet.Cells[row, 9].Value),
                    HitSource = actSheet.Cells[row, 10].Text,
                    ScriptHook = actSheet.Cells[row, 11].Text
                };
                if (string.IsNullOrEmpty(data.StatusID2)) continue;
                if (!StatusActionDict.ContainsKey(data.StatusID2)) StatusActionDict[data.StatusID2] = new List<StatusActionData>();
                StatusActionDict[data.StatusID2].Add(data);
            }
        }

        // Status_Attributes
        var attrSheet = pkg.Workbook.Worksheets["Status_Attributes"];
        if (attrSheet != null)
        {
            int startRow = GetDataStartRow(attrSheet);
            int maxRow = GetSheetMaxRow(attrSheet);
            for (int row = startRow; row <= maxRow; row++)
            {
                string cell = attrSheet.Cells[row, 2].Text;
                if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(attrSheet.Cells[row, 1].Text)) continue;
                var data = new StatusAttributesData
                {
                    StatusID = SafeGetInt(attrSheet.Cells[row, 1].Value),
                    StatusID2 = cell,
                    TotalHP = SafeGetInt(attrSheet.Cells[row, 3].Value),
                    TotalATK = SafeGetInt(attrSheet.Cells[row, 4].Value),
                    TotalDEF = SafeGetInt(attrSheet.Cells[row, 5].Value),
                    TotalEM = SafeGetInt(attrSheet.Cells[row, 6].Value),
                    TotalRechargeRate = SafeGetInt(attrSheet.Cells[row, 7].Value),
                    CritRate = SafeGetInt(attrSheet.Cells[row, 8].Value),
                    CritDMG = SafeGetInt(attrSheet.Cells[row, 9].Value),
                    BaseDMGBonusFlat = SafeGetInt(attrSheet.Cells[row, 10].Value),
                    DMGBonus = SafeGetInt(attrSheet.Cells[row, 11].Value),
                    DEFReduction = SafeGetInt(attrSheet.Cells[row, 12].Value),
                    DEFIgnored = SafeGetInt(attrSheet.Cells[row, 13].Value),
                    ResBonus = SafeGetInt(attrSheet.Cells[row, 14].Value),
                    ShieldStrength = SafeGetInt(attrSheet.Cells[row, 15].Value),
                    HealBonus = SafeGetInt(attrSheet.Cells[row, 16].Value),
                    BeHealedBonus = SafeGetInt(attrSheet.Cells[row, 17].Value),
                    Elevation = SafeGetInt(attrSheet.Cells[row, 18].Value),
                    BaseDMGBonus = SafeGetInt(attrSheet.Cells[row, 19].Value),
                    LunarDMGBonus = SafeGetInt(attrSheet.Cells[row, 20].Value),
                    StellarDMGBonus = SafeGetInt(attrSheet.Cells[row, 21].Value)
                };
                if (string.IsNullOrEmpty(data.StatusID2)) continue;
                StatusAttributesDict[data.StatusID2] = data;
            }
        }
        LogManager.Log(LogCategory.Data, $"EnemyStatusData loaded, {StatusMainDict.Count} statuses");
    }

    // ================================================================
    //  StatusData_Overall.xlsx：由通用战斗脚本施加的显示状态（倒地、冻结、激化等）。
    //  当前倒地使用 Main 显示信息及 Character 的 ChangeControl 效果；生命周期由脚本控制。
    // ================================================================
    void LoadOverallStatusData()
    {
        string path = Path.Combine(ChartsPath, "StatusData_Overall.xlsx");
        if (!File.Exists(path))
        {
            Debug.LogWarning("StatusData_Overall.xlsx not found; scripted statuses will use runtime fallback metadata");
            return;
        }

        using var pkg = new ExcelPackage(new FileInfo(path));
        var mainSheet = pkg.Workbook.Worksheets["StatusData_Overall_Main"];
        if (mainSheet != null)
        {
            int startRow = GetDataStartRow(mainSheet);
            int maxRow = GetSheetMaxRow(mainSheet);
            for (int row = startRow; row <= maxRow; row++)
            {
                string id2 = mainSheet.Cells[row, 2].Text;
                if (string.IsNullOrEmpty(id2)) continue;
                StatusMainDict[id2] = new StatusMainData
                {
                    StatusID = SafeGetInt(mainSheet.Cells[row, 1].Value),
                    StatusID2 = id2,
                    StatusName = mainSheet.Cells[row, 3].Text,
                    StatusType = mainSheet.Cells[row, 4].Text,
                    Display = SafeGetInt(mainSheet.Cells[row, 5].Value),
                    Description = mainSheet.Cells[row, 6].Text,
                    MultiplierPart1 = mainSheet.Cells[row, 7].Text,
                    MultiplierPart2 = mainSheet.Cells[row, 8].Text,
                    MultiplierPart3 = mainSheet.Cells[row, 9].Text,
                    ApplyDamageType = mainSheet.Cells[row, 10].Text,
                    ApplyReactionType = mainSheet.Cells[row, 11].Text,
                    ApplyElementType = mainSheet.Cells[row, 12].Text,
                    ApplyString = mainSheet.Cells[row, 13].Text,
                    ScriptHook = mainSheet.Cells[row, 14].Text,
                    MaxCount = SafeGetInt(mainSheet.Cells[row, 15].Value),
                    MaxStack = SafeGetInt(mainSheet.Cells[row, 16].Value),
                    WhenMax = mainSheet.Cells[row, 17].Text
                };
            }
        }

        var effectSheet = pkg.Workbook.Worksheets["StatusEffect_Overall"];
        if (effectSheet != null)
        {
            int startRow = GetDataStartRow(effectSheet);
            int maxRow = GetSheetMaxRow(effectSheet);
            for (int row = startRow; row <= maxRow; row++)
            {
                string id2 = effectSheet.Cells[row, 2].Text;
                if (string.IsNullOrEmpty(id2)) continue;
                var data = new StatusEffectData
                {
                    StatusEffectID = SafeGetInt(effectSheet.Cells[row, 1].Value),
                    StatusEffectID2 = id2,
                    EffectIndex = SafeGetInt(effectSheet.Cells[row, 3].Value),
                    EffectType = effectSheet.Cells[row, 4].Text,
                    Element = effectSheet.Cells[row, 5].Text,
                    DamageType = effectSheet.Cells[row, 6].Text,
                    Duration = SafeGetInt(effectSheet.Cells[row, 7].Value),
                    AddInPhase = SafeGetInt(effectSheet.Cells[row, 8].Value),
                    TriggerPhase = SafeGetInt(effectSheet.Cells[row, 9].Value),
                    Param1 = effectSheet.Cells[row, 10].Text,
                    Param2 = effectSheet.Cells[row, 11].Text,
                    Param3 = effectSheet.Cells[row, 12].Text,
                    TargetType = effectSheet.Cells[row, 13].Text,
                    TargetSelect = effectSheet.Cells[row, 14].Text.Contains(",") ? effectSheet.Cells[row, 14].Text : "",
                    TargetConsecutive = SafeGetInt(effectSheet.Cells[row, 15].Value),
                    TargetOverride = effectSheet.Cells[row, 16].Text,
                    ScriptHook = effectSheet.Cells[row, 17].Text
                };
                if (data.EffectType == "ChangeControl")
                    TryValidate("StatusEffect_Overall", () => TableValidator.ValidateChangeControl(
                        "StatusEffect_Overall", row, data.Param1, data.Param2, data.Param3,
                        data.Duration, isStatusEffectTable: true, isEnemy: false));
                StatusEffectDict[id2] = data;
            }
        }

        LogManager.Log(LogCategory.Data, $"OverallStatusData loaded, {StatusMainDict.Count} statuses");
    }

    // ================================================================
    //  EnemyAttributes.xlsx 的 Enemy_Main
    // ================================================================
    void LoadEnemyAttributes()
    {
        string path = Path.Combine(ChartsPath, "EnemyAttributes.xlsx");
        if (!File.Exists(path)) { Debug.LogWarning("EnemyAttributes.xlsx not found"); return; }
        using var pkg = new ExcelPackage(new FileInfo(path));
        var sheet = pkg.Workbook.Worksheets["Enemy_Main"];
        if (sheet == null) return;
        int startRow = GetDataStartRow(sheet);
        int maxRow = GetSheetMaxRow(sheet);
        for (int row = startRow; row <= maxRow; row++)
        {
            string cell = sheet.Cells[row, 1].Text;
            if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(sheet.Cells[row, 2].Text)) continue;
            var data = new EnemyMainData
            {
                EnemyID = SafeGetInt(sheet.Cells[row, 1].Value),
                EnemyNameID = sheet.Cells[row, 2].Text,
                EnemyName = sheet.Cells[row, 3].Text,
                Threat = SafeGetInt(sheet.Cells[row, 4].Value),
                ActsGiven = SafeGetInt(sheet.Cells[row, 5].Value),
                Poise = SafeGetFloat(sheet.Cells[row, 6].Value),
                ATK_Curve = SafeGetInt(sheet.Cells[row, 7].Value),
                HP_coeff = SafeGetFloat(sheet.Cells[row, 8].Value),
                ATK_coeff = SafeGetFloat(sheet.Cells[row, 9].Value),
                PhysicalRes = SafeGetFloat(sheet.Cells[row, 10].Value),
                PyroRes = SafeGetFloat(sheet.Cells[row, 11].Value),
                HydroRes = SafeGetFloat(sheet.Cells[row, 12].Value),
                ElectroRes = SafeGetFloat(sheet.Cells[row, 13].Value),
                CryoRes = SafeGetFloat(sheet.Cells[row, 14].Value),
                AnemoRes = SafeGetFloat(sheet.Cells[row, 15].Value),
                DendroRes = SafeGetFloat(sheet.Cells[row, 16].Value),
                GeoRes = SafeGetFloat(sheet.Cells[row, 17].Value),
                PhysicalDmgBonus = SafeGetFloat(sheet.Cells[row, 18].Value),
                PyroDmgBonus = SafeGetFloat(sheet.Cells[row, 19].Value),
                HydroDmgBonus = SafeGetFloat(sheet.Cells[row, 20].Value),
                ElectroDmgBonus = SafeGetFloat(sheet.Cells[row, 21].Value),
                CryoDmgBonus = SafeGetFloat(sheet.Cells[row, 22].Value),
                AnemoDmgBonus = SafeGetFloat(sheet.Cells[row, 23].Value),
                DendroDmgBonus = SafeGetFloat(sheet.Cells[row, 24].Value),
                GeoDmgBonus = SafeGetFloat(sheet.Cells[row, 25].Value)
            };
            TryValidate("Enemy_Main", () => TableValidator.ValidateEnemyMainRow(row, data.EnemyID, data.Poise));
            if (data.EnemyID == 0) continue;
            EnemyMainDict[data.EnemyID] = data;
        }
    }

    // ================================================================
    //  EnemyAttributes.xlsx 的 Base_HP / Base_ATK1 / Base_ATK2 曲线
    // ================================================================
    void LoadEnemyCurves()
    {
        string path = Path.Combine(ChartsPath, "EnemyAttributes.xlsx");
        if (!File.Exists(path)) { Debug.LogWarning("EnemyAttributes.xlsx not found"); return; }
        using var pkg = new ExcelPackage(new FileInfo(path));

        var hpSheet = pkg.Workbook.Worksheets["Base_HP"];
        if (hpSheet != null)
        {
            int startRow = GetDataStartRow(hpSheet);
            int maxRow = GetSheetMaxRow(hpSheet);
            for (int row = startRow; row <= maxRow; row++)
            {
                string cell = hpSheet.Cells[row, 1].Text;
                if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(hpSheet.Cells[row, 2].Text)) continue;
                BaseHPDict[SafeGetInt(hpSheet.Cells[row, 1].Value)] = SafeGetFloat(hpSheet.Cells[row, 2].Value);
            }
        }

        var atk1Sheet = pkg.Workbook.Worksheets["Base_ATK1"];
        if (atk1Sheet != null)
        {
            int startRow = GetDataStartRow(atk1Sheet);
            int maxRow = GetSheetMaxRow(atk1Sheet);
            for (int row = startRow; row <= maxRow; row++)
            {
                string cell = atk1Sheet.Cells[row, 1].Text;
                if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(atk1Sheet.Cells[row, 2].Text)) continue;
                BaseATK1Dict[SafeGetInt(atk1Sheet.Cells[row, 1].Value)] = SafeGetFloat(atk1Sheet.Cells[row, 2].Value);
            }
        }

        var atk2Sheet = pkg.Workbook.Worksheets["Base_ATK2"];
        if (atk2Sheet != null)
        {
            int startRow = GetDataStartRow(atk2Sheet);
            int maxRow = GetSheetMaxRow(atk2Sheet);
            for (int row = startRow; row <= maxRow; row++)
            {
                string cell = atk2Sheet.Cells[row, 1].Text;
                if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(atk2Sheet.Cells[row, 2].Text)) continue;
                BaseATK2Dict[SafeGetInt(atk2Sheet.Cells[row, 1].Value)] = SafeGetFloat(atk2Sheet.Cells[row, 2].Value);
            }
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
    //  EnemySkill.xlsx 5 sheets
    // ================================================================
    void LoadEnemySkill()
    {
        string path = Path.Combine(ChartsPath, "EnemySkill.xlsx");
        if (!File.Exists(path)) { Debug.LogWarning("EnemySkill.xlsx not found"); return; }
        using var pkg = new ExcelPackage(new FileInfo(path));

        // EnemySkill_Main
        var mainSheet = pkg.Workbook.Worksheets["EnemySkill_Main"];
        if (mainSheet != null)
        {
            int startRow = GetDataStartRow(mainSheet);
            int maxRow = GetSheetMaxRow(mainSheet);
            for (int row = startRow; row <= maxRow; row++)
            {
                string cell = mainSheet.Cells[row, 1].Text;
                if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(mainSheet.Cells[row, 2].Text)) continue;
                var data = new EnemySkillMainData
                {
                    EnemySkillID = SafeGetInt(mainSheet.Cells[row, 1].Value),
                    EnemySkillID2 = mainSheet.Cells[row, 2].Text,
                    EnemySkillName = mainSheet.Cells[row, 3].Text,
                    Cooldown = SafeGetFloat(mainSheet.Cells[row, 4].Value),
                    UsePerTurn = SafeGetInt(mainSheet.Cells[row, 5].Value),
                    HighThreat = SafeGetInt(mainSheet.Cells[row, 6].Value),
                    Description = mainSheet.Cells[row, 7].Text
                };
                if (data.EnemySkillID == 0) continue;
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
                string cell = effSheet.Cells[row, 2].Text;
                if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(effSheet.Cells[row, 1].Text)) continue;
                var data = new EnemySkillEffectData
                {
                    SkillEffectID = SafeGetInt(effSheet.Cells[row, 1].Value),
                    SkillEffectID2 = cell,
                    EffectIndex = SafeGetInt(effSheet.Cells[row, 3].Value),
                    EffectType = effSheet.Cells[row, 4].Text,
                    Element = effSheet.Cells[row, 5].Text,
                    DamageType = effSheet.Cells[row, 6].Text,
                    Duration = SafeGetInt(effSheet.Cells[row, 7].Value),
                    AddInPhase = SafeGetInt(effSheet.Cells[row, 8].Value),
                    TriggerPhase = SafeGetInt(effSheet.Cells[row, 9].Value),
                    Param1 = effSheet.Cells[row, 10].Text,
                    Param2 = effSheet.Cells[row, 11].Text,
                    Param3 = effSheet.Cells[row, 12].Text,
                    TargetType = effSheet.Cells[row, 13].Text,
                    TargetNumber = SafeGetInt(effSheet.Cells[row, 14].Value),
                    TargetConsecutive = SafeGetInt(effSheet.Cells[row, 15].Value),
                    TargetOverride = effSheet.Cells[row, 16].Text,
                    ScriptHook = effSheet.Cells[row, 17].Text
                };
                TryValidate("EnemySkill_Effect", () => TableValidator.ValidateEffectRow("EnemySkill_Effect", row,
                    data.EffectType, data.Element, data.Duration, data.Param2,
                    data.TargetType, data.TargetNumber, data.TargetConsecutive,
                    data.TargetOverride, isEnemy: true));
                // ChangeControl 专属校验（2026-08-11）：敌人没有按键绑定，禁止使用
                if (data.EffectType == "ChangeControl")
                    TryValidate("EnemySkill_Effect", () => TableValidator.ValidateChangeControl("EnemySkill_Effect", row,
                        data.Param1, data.Param2, data.Param3, data.Duration,
                        isStatusEffectTable: false, isEnemy: true));
                if (string.IsNullOrEmpty(data.SkillEffectID2)) continue;
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
                string cell = paramSheet.Cells[row, 2].Text;
                if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(paramSheet.Cells[row, 1].Text)) continue;
                var data = new EnemySkillParamData
                {
                    SkillEffectID = SafeGetInt(paramSheet.Cells[row, 1].Value),
                    SkillEffectID2 = cell,
                    EffectType = paramSheet.Cells[row, 3].Text,
                    Hits1 = paramSheet.Cells[row, 4].Text,
                    Hits2 = paramSheet.Cells[row, 5].Text,
                    Hits3 = paramSheet.Cells[row, 6].Text,
                    Hits4 = paramSheet.Cells[row, 7].Text,
                    Hits5 = paramSheet.Cells[row, 8].Text,
                    Hits6 = paramSheet.Cells[row, 9].Text,
                    Hits7 = paramSheet.Cells[row, 10].Text,
                    Hits8 = paramSheet.Cells[row, 11].Text
                };
                if (string.IsNullOrEmpty(data.SkillEffectID2)) continue;
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
                string cell = aiSheet.Cells[row, 1].Text;
                if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(aiSheet.Cells[row, 2].Text)) continue;
                var data = new EnemyAIData
                {
                    EnemyID = SafeGetInt(aiSheet.Cells[row, 1].Value),
                    EnemyNameID = aiSheet.Cells[row, 2].Text,
                    EnemyName = aiSheet.Cells[row, 3].Text,
                    PatternID = SafeGetInt(aiSheet.Cells[row, 4].Value),
                    PatternType = aiSheet.Cells[row, 5].Text,
                    NextPatternID = SafeGetInt(aiSheet.Cells[row, 6].Value),
                    ConditionType = aiSheet.Cells[row, 7].Text,
                    ConditionParam = aiSheet.Cells[row, 8].Text
                };
                if (data.EnemyID == 0) continue;
                if (!EnemyAIDict.ContainsKey(data.EnemyID)) EnemyAIDict[data.EnemyID] = new List<EnemyAIData>();
                EnemyAIDict[data.EnemyID].Add(data);
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
                string cell = ruleSheet.Cells[row, 1].Text;
                if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(ruleSheet.Cells[row, 2].Text) && string.IsNullOrEmpty(ruleSheet.Cells[row, 4].Text)) continue;
                int enemyID = 0;
                if (!string.IsNullOrEmpty(cell)) enemyID = SafeGetInt(ruleSheet.Cells[row, 1].Value);
                string pidCell = ruleSheet.Cells[row, 2].Text;
                string ptypeCell = ruleSheet.Cells[row, 3].Text;
                int patternID = string.IsNullOrEmpty(pidCell) ? lastPatternID : SafeGetInt(ruleSheet.Cells[row, 2].Value);
                string patternType = string.IsNullOrEmpty(ptypeCell) ? lastPatternType : ptypeCell;
                lastPatternID = patternID;
                lastPatternType = patternType;
                if (enemyID != 0) lastEnemyID = enemyID;
                var data = new EnemyAIRuleData
                {
                    EnemyID = enemyID == 0 ? lastEnemyID : enemyID,
                    PatternID = patternID,
                    PatternType = patternType,
                    SkillID = SafeGetInt(ruleSheet.Cells[row, 4].Value),
                    SkillIndex = SafeGetInt(ruleSheet.Cells[row, 5].Value),
                    Weight = SafeGetInt(ruleSheet.Cells[row, 6].Value)
                };
                if (data.EnemyID == 0) continue;
                if (!EnemyAIRuleDict.ContainsKey(data.EnemyID)) EnemyAIRuleDict[data.EnemyID] = new List<EnemyAIRuleData>();
                EnemyAIRuleDict[data.EnemyID].Add(data);
            }
        }
    }

    // ================================================================
    //  ReactionLevelCoefficient.xlsx
    // ================================================================
    void LoadReactionLevelCoefficient()
    {
        string path = Path.Combine(ChartsPath, "ReactionLevelCoefficient.xlsx");
        if (!File.Exists(path)) return;
        using var pkg = new ExcelPackage(new FileInfo(path));
        var sheet = pkg.Workbook.Worksheets[0];
        if (sheet == null) return;
        int startRow = GetDataStartRow(sheet);
        int maxRow = GetSheetMaxRow(sheet);
        for (int row = startRow; row <= maxRow; row++)
        {
            string cell = sheet.Cells[row, 1].Text;
            if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(sheet.Cells[row, 2].Text)) continue;
            ReactionLevelCoefficientDict[SafeGetInt(sheet.Cells[row, 1].Value)] = SafeGetFloat(sheet.Cells[row, 2].Value);
        }
    }

    // ================================================================
    //  StageConfig.xlsx
    // ================================================================
    void LoadStageConfig()
    {
        string path = Path.Combine(ChartsPath, "StageConfig.xlsx");
        if (!File.Exists(path)) return;
        using var pkg = new ExcelPackage(new FileInfo(path));
        var sheet = pkg.Workbook.Worksheets[0];
        if (sheet == null) return;
        int startRow = GetDataStartRow(sheet);
        int maxRow = GetSheetMaxRow(sheet);
        for (int row = startRow; row <= maxRow; row++)
        {
            string cell = sheet.Cells[row, 1].Text;
            if (string.IsNullOrEmpty(cell) && string.IsNullOrEmpty(sheet.Cells[row, 2].Text)) continue;
            var data = new StageConfigData
            {
                StageID = SafeGetInt(sheet.Cells[row, 1].Value),
                StageName = sheet.Cells[row, 2].Text,
                StageDescription = sheet.Cells[row, 3].Text,
                EnemyIDs = sheet.Cells[row, 4].Text,
                EnemyCounts = sheet.Cells[row, 5].Text,
                EnemyLevels = sheet.Cells[row, 6].Text,
                AllyIDs = sheet.Cells[row, 7].Text,
                AllyCounts = sheet.Cells[row, 8].Text,
                AllyLevels = sheet.Cells[row, 9].Text,
                WinCondition = sheet.Cells[row, 10].Text,
                LoseCondition = sheet.Cells[row, 11].Text
            };
            if (data.StageID == 0) continue;
            StageConfigDict[data.StageID] = data;
        }
    }

    // ================================================================
    //  Helpers
    // ================================================================
    int SafeGetInt(object val)
    {
        if (val == null) return 0;
        if (val is int i32) return i32;
        if (val is double d) return (int)d;
        if (val is float f) return (int)f;
        if (int.TryParse(val.ToString(), out int r)) return r;
        return 0;
    }

    float SafeGetFloat(object val)
    {
        if (val == null) return 0f;
        if (val is float f) return f;
        if (val is double d) return (float)d;
        if (val is int i32) return i32;
        if (float.TryParse(val.ToString(), out float r)) return r;
        return 0f;
    }
}
