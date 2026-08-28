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

    // ========== Weapon ==========
    public Dictionary<int, WeaponAttributesData> WeaponAttributesDict = new Dictionary<int, WeaponAttributesData>();
    public Dictionary<int, WeaponLevelBonusData> WeaponLevelBonusDict = new Dictionary<int, WeaponLevelBonusData>();
    public Dictionary<int, Dictionary<int, WeaponParamData>> WeaponParamDict = new Dictionary<int, Dictionary<int, WeaponParamData>>();
    public Dictionary<int, List<string>> WeaponInitiateStatusDict = new Dictionary<int, List<string>>();
    public Dictionary<int, float> WeaponAscensionCurve3 = new Dictionary<int, float>();
    public Dictionary<int, float> WeaponAscensionCurve4 = new Dictionary<int, float>();
    public Dictionary<int, float> WeaponAscensionCurve5 = new Dictionary<int, float>();

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
