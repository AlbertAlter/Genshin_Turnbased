using System;
using System.Globalization;
using System.Text.RegularExpressions;

/// <summary>敌人数值盾公式：有限非负常量与属性的加法、乘法；不执行脚本。</summary>
public static class EnemyHealthShieldFormula
{
    public static float Evaluate(string formula, Func<string, float> attribute)
    {
        if (string.IsNullOrWhiteSpace(formula)) throw new FormatException("敌人数值盾缺少Param1公式。");
        float sum = 0f;
        foreach (string term in Regex.Replace(formula, @"\s+", "").Split('+'))
        {
            float product = 1f;
            foreach (string factor in term.Split('*'))
            {
                float value;
                if (!float.TryParse(factor, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value))
                {
                    switch (factor)
                    {
                        case "ATK": case "TotalATK": case "DEF": case "TotalDEF":
                        case "MaxHP": case "TotalHP": case "Target.TotalHP":
                            if (attribute == null) throw new FormatException("敌人数值盾缺少属性来源：" + factor);
                            value = attribute(factor);
                            break;
                        default: throw new FormatException("无法解析敌人数值盾公式：" + formula);
                    }
                }
                if (value < 0f || float.IsNaN(value) || float.IsInfinity(value))
                    throw new FormatException("敌人数值盾公式只接受有限非负数：" + formula);
                product *= value;
            }
            sum += product;
        }
        if (sum <= 0f || float.IsNaN(sum) || float.IsInfinity(sum))
            throw new FormatException("敌人数值盾基础盾量必须为有限正数：" + formula);
        return sum;
    }
}
