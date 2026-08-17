using System;
using System.Collections.Generic;

/// <summary>
/// 一个 EffectID 在所有等级下的 Hit 数据集合
/// Key: Level (1~15), Value: 该等级的所有 Hit 列表
/// </summary>
[Serializable]
public class HitLevelData
{
    public string EffectID;
    public Dictionary<int, List<HitData>> LevelHits;  // Level → List<HitData>
}