using System;
using System.Globalization;

/// <summary>
/// 一段 Hit 的运行时数据；配表及日志统一使用倍率、附着量两项（M,E）。
/// </summary>
[Serializable]
public struct HitData
{
    public float Multiplier;    // M: 倍率部分
    public float ElementAura;   // E: 元素附着量

    public HitData(float multiplier, float aura)
    {
        Multiplier = multiplier;
        ElementAura = aura;
    }

    /// <summary>
    /// 输出 M,E 格式，不再输出已废弃的削韧项。
    /// </summary>
    public override string ToString()
    {
        return Multiplier.ToString(CultureInfo.InvariantCulture) + "*ATK,"
            + ElementAura.ToString(CultureInfo.InvariantCulture);
    }
}
