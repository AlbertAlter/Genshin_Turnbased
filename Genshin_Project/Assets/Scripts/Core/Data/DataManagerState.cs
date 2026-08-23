using System.Collections.Generic;

/// <summary>
/// DataManager 的一整套集合引用。新建实例即得到全空候选数据；
/// Capture 保存当前正式数据，以便加载失败时原样恢复。
/// </summary>
internal sealed class DataManagerState
{
    internal List<CharacterOverviewData> CharacterOverviewList = new List<CharacterOverviewData>();
    internal Dictionary<int, CharacterAttributesData> CharacterAttributesDict = new Dictionary<int, CharacterAttributesData>();
    internal Dictionary<int, List<CharacterAscensionData>> CharacterAscensionDict = new Dictionary<int, List<CharacterAscensionData>>();
    internal Dictionary<int, List<SkillMainData>> CharacterSkillDict = new Dictionary<int, List<SkillMainData>>();
    internal Dictionary<string, SkillEffectData> SkillEffectDict = new Dictionary<string, SkillEffectData>();
    internal Dictionary<string, Dictionary<int, SkillLevelData>> SkillLevelDict = new Dictionary<string, Dictionary<int, SkillLevelData>>();
    internal Dictionary<int, List<ConstellationData>> ConstellationDict = new Dictionary<int, List<ConstellationData>>();
    internal Dictionary<int, List<TalentData>> TalentDict = new Dictionary<int, List<TalentData>>();
    internal Dictionary<int, Dictionary<int, float>> GrowthCurveDict = new Dictionary<int, Dictionary<int, float>>();

    internal Dictionary<string, StatusMainData> StatusMainDict = new Dictionary<string, StatusMainData>();
    internal Dictionary<string, StatusEffectData> StatusEffectDict = new Dictionary<string, StatusEffectData>();
    internal Dictionary<string, List<StatusActionData>> StatusActionDict = new Dictionary<string, List<StatusActionData>>();
    internal Dictionary<string, StatusAttributesData> StatusAttributesDict = new Dictionary<string, StatusAttributesData>();

    internal Dictionary<int, EnemyMainData> EnemyMainDict = new Dictionary<int, EnemyMainData>();
    internal Dictionary<int, EnemySkillMainData> EnemySkillMainDict = new Dictionary<int, EnemySkillMainData>();
    internal Dictionary<string, EnemySkillEffectData> EnemySkillEffectDict = new Dictionary<string, EnemySkillEffectData>();
    internal Dictionary<string, EnemySkillParamData> EnemySkillParamDict = new Dictionary<string, EnemySkillParamData>();
    internal Dictionary<int, List<EnemyAIData>> EnemyAIDict = new Dictionary<int, List<EnemyAIData>>();
    internal Dictionary<int, List<EnemyAIRuleData>> EnemyAIRuleDict = new Dictionary<int, List<EnemyAIRuleData>>();

    internal Dictionary<int, float> BaseHPDict = new Dictionary<int, float>();
    internal Dictionary<int, float> BaseATK1Dict = new Dictionary<int, float>();
    internal Dictionary<int, float> BaseATK2Dict = new Dictionary<int, float>();
    internal Dictionary<int, float> ReactionLevelCoefficientDict = new Dictionary<int, float>();
    internal Dictionary<int, StageConfigData> StageConfigDict = new Dictionary<int, StageConfigData>();
    internal Dictionary<string, int> SkillStringToInt = new Dictionary<string, int>();

    internal static DataManagerState Capture(DataManager manager)
    {
        return new DataManagerState
        {
            CharacterOverviewList = manager.CharacterOverviewList,
            CharacterAttributesDict = manager.CharacterAttributesDict,
            CharacterAscensionDict = manager.CharacterAscensionDict,
            CharacterSkillDict = manager.CharacterSkillDict,
            SkillEffectDict = manager.SkillEffectDict,
            SkillLevelDict = manager.SkillLevelDict,
            ConstellationDict = manager.ConstellationDict,
            TalentDict = manager.TalentDict,
            GrowthCurveDict = manager.GrowthCurveDict,
            StatusMainDict = manager.StatusMainDict,
            StatusEffectDict = manager.StatusEffectDict,
            StatusActionDict = manager.StatusActionDict,
            StatusAttributesDict = manager.StatusAttributesDict,
            EnemyMainDict = manager.EnemyMainDict,
            EnemySkillMainDict = manager.EnemySkillMainDict,
            EnemySkillEffectDict = manager.EnemySkillEffectDict,
            EnemySkillParamDict = manager.EnemySkillParamDict,
            EnemyAIDict = manager.EnemyAIDict,
            EnemyAIRuleDict = manager.EnemyAIRuleDict,
            BaseHPDict = manager.BaseHPDict,
            BaseATK1Dict = manager.BaseATK1Dict,
            BaseATK2Dict = manager.BaseATK2Dict,
            ReactionLevelCoefficientDict = manager.ReactionLevelCoefficientDict,
            StageConfigDict = manager.StageConfigDict,
            SkillStringToInt = manager.SkillStringToInt
        };
    }

    internal void ApplyTo(DataManager manager)
    {
        manager.CharacterOverviewList = CharacterOverviewList;
        manager.CharacterAttributesDict = CharacterAttributesDict;
        manager.CharacterAscensionDict = CharacterAscensionDict;
        manager.CharacterSkillDict = CharacterSkillDict;
        manager.SkillEffectDict = SkillEffectDict;
        manager.SkillLevelDict = SkillLevelDict;
        manager.ConstellationDict = ConstellationDict;
        manager.TalentDict = TalentDict;
        manager.GrowthCurveDict = GrowthCurveDict;
        manager.StatusMainDict = StatusMainDict;
        manager.StatusEffectDict = StatusEffectDict;
        manager.StatusActionDict = StatusActionDict;
        manager.StatusAttributesDict = StatusAttributesDict;
        manager.EnemyMainDict = EnemyMainDict;
        manager.EnemySkillMainDict = EnemySkillMainDict;
        manager.EnemySkillEffectDict = EnemySkillEffectDict;
        manager.EnemySkillParamDict = EnemySkillParamDict;
        manager.EnemyAIDict = EnemyAIDict;
        manager.EnemyAIRuleDict = EnemyAIRuleDict;
        manager.BaseHPDict = BaseHPDict;
        manager.BaseATK1Dict = BaseATK1Dict;
        manager.BaseATK2Dict = BaseATK2Dict;
        manager.ReactionLevelCoefficientDict = ReactionLevelCoefficientDict;
        manager.StageConfigDict = StageConfigDict;
        manager.SkillStringToInt = SkillStringToInt;
    }
}
