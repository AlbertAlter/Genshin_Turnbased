using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

/// <summary>BondOfLife 效果：单个有符号数，或有符号系数*白名单变量；不是任意脚本表达式。</summary>
public static class BondOfLifeEffect
{
    private static readonly Regex IndexCoefficient = new Regex(@"^[+-]?Index\(\s*[1-9]\d*\s*\)$");
    private static readonly HashSet<string> Variables = new HashSet<string>(StringComparer.Ordinal)
    {
        "TotalHP", "CurrentHP", "TotalATK", "TotalDEF", "TotalEM",
        "CurrentEnergy", "MaxEnergy", "BondOfLife"
    };

    public static bool IsValidExpression(string expression)
    {
        string coefficient, variable;
        return TryParse(expression, true, out coefficient, out variable);
    }

    private static bool TryParse(string expression, bool allowIndex, out string coefficient, out string variable)
    {
        coefficient = variable = string.Empty;
        if (string.IsNullOrWhiteSpace(expression)) return false;
        string[] parts = expression.Trim().Split('*');
        if (parts.Length < 1 || parts.Length > 2) return false;
        coefficient = parts[0].Trim();
        float scalar;
        bool number = float.TryParse(coefficient, NumberStyles.Float, CultureInfo.InvariantCulture, out scalar)
            && !float.IsNaN(scalar) && !float.IsInfinity(scalar);
        if (!number && !(allowIndex && IndexCoefficient.IsMatch(coefficient))) return false;
        if (parts.Length == 1) return true;
        variable = parts[1].Trim();
        string name = variable.StartsWith("Target.", StringComparison.Ordinal) ? variable.Substring(7) : variable;
        return Variables.Contains(name);
    }

    public static float Evaluate(string expression, BattleEntity source, BattleEntity target,
        StatusInstance sourceStatus = null, string effectID = null, int skillType = 0, string element = null)
    {
        string coefficient, variable;
        string expanded = WeaponExpressionEvaluator.Expand(expression, sourceStatus);
        if (!TryParse(expanded, false, out coefficient, out variable))
            throw new FormatException("BondOfLife Param1 无法解析：[" + expression + "]");
        float value = float.Parse(coefficient, NumberStyles.Float, CultureInfo.InvariantCulture);
        if (!string.IsNullOrEmpty(variable))
        {
            bool usesTarget = variable.StartsWith("Target.", StringComparison.Ordinal);
            value *= ReadVariable(usesTarget ? target : source, usesTarget ? variable.Substring(7) : variable,
                effectID, skillType, element);
        }
        if (float.IsNaN(value) || float.IsInfinity(value))
            throw new InvalidOperationException("BondOfLife Param1 计算结果不是有限数值：[" + expression + "]");
        return value;
    }

    private static float ReadVariable(BattleEntity entity, string variable, string effectID, int skillType, string element)
    {
        if (entity == null) throw new InvalidOperationException("BondOfLife 公式缺少变量所属实体：" + variable);
        switch (variable)
        {
            case "TotalHP": return entity.GetFinalHP(effectID, "", skillType, element);
            case "CurrentHP": return entity.CurrentHP;
            case "TotalATK": return entity.CharacterCtrl != null
                ? entity.CharacterCtrl.GetFinalATK(effectID, skillType, element) : entity.TotalATK;
            case "TotalDEF": return entity.GetFinalDEF();
            case "TotalEM": return entity.GetFinalEM(effectID, "", skillType, element);
            case "CurrentEnergy": return entity.CurrentEnergy;
            case "MaxEnergy": return entity.MaxEnergy;
            case "BondOfLife": return BondOfLifeStatusHandler.GetValue(entity);
            default: throw new FormatException("BondOfLife 不支持变量：" + variable);
        }
    }

    public static void Execute(string expression, BattleEntity source, IEnumerable<BattleEntity> targets,
        StatusInstance sourceStatus = null, string effectID = null, int skillType = 0, string element = null)
    {
        if (targets == null) return;
        string coefficient, variable;
        if (!TryParse(expression, true, out coefficient, out variable))
            throw new FormatException("BondOfLife Param1 无法解析：[" + expression + "]");
        bool usesTarget = variable.StartsWith("Target.", StringComparison.Ordinal);
        // 无前缀变量在本次效果开始时只读取一次；群体效果不能因先改了施放者而改变后续目标的增减量。
        float commonDelta = usesTarget ? 0f : Evaluate(expression, source, null, sourceStatus, effectID, skillType, element);
        var seen = new HashSet<BattleEntity>();
        var preparedTargets = new List<BattleEntity>();
        var deltas = new List<float>();
        foreach (BattleEntity target in targets)
        {
            if (target == null || !target.IsAlive || !seen.Add(target)) continue;
            float delta = usesTarget
                ? Evaluate(expression, source, target, sourceStatus, effectID, skillType, element) : commonDelta;
            float after = BondOfLifeStatusHandler.GetValue(target) + delta;
            if (float.IsNaN(after) || float.IsInfinity(after))
                throw new InvalidOperationException("BondOfLife 数值相加溢出");
            preparedTargets.Add(target);
            deltas.Add(delta);
        }
        using var resolution = BattleEventHub.BeginResolution();
        using var collection = BattleResolutionContext.BeginCollection();
        for (int index = 0; index < preparedTargets.Count; index++)
        {
            BattleEntity target = preparedTargets[index];
            BondOfLifeValueChange change = BondOfLifeStatusHandler.ChangeValue(target, deltas[index]);
            LogManager.Log(LogCategory.Effect, "BondOfLife " + effectID + " -> " + target.EntityID
                + " " + change.Before + " -> " + change.After);
        }
    }
}
