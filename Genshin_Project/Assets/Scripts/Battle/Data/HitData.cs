using System;

/// <summary>
/// 一段 Hit 的运行时数据（倍率、削韧、附着量）
/// </summary>
[Serializable]
public struct HitData
{
    public float Multiplier;    // M: 倍率部分
    public float Poise;         // P: 削韧值
    public float ElementAura;   // E: 元素附着量

    public HitData(float multiplier, float poise, float aura)
    {
        Multiplier = multiplier;
        Poise = poise;
        ElementAura = aura;
    }

    /// <summary>
    /// 从 "1.24*ATK;100;2" 格式解析
    /// 如果格式不对则返回默认值（解析工作在 HitDataParser 中完成）
    /// </summary>
    public override string ToString()
    {
        return $"{Multiplier}*ATK;{Poise};{ElementAura}";
    }
}