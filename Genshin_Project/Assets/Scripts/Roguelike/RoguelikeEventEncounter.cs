using System;

/// <summary>固定单次遭遇的只读展示数据。敌阵复用BattleID；没有嵌套选项或HTML测试档。</summary>
[Serializable]
public sealed class RoguelikeEventEncounter
{
    public string EventId;
    public string TitleKey;
    public string TextKey;
    public string PlaceKey;
    public string ActionKey;
    public string ConditionKey;
    public string BattleContentId;
    public int PreviewEnemyId;
    public int[] EnemyIds;
}
