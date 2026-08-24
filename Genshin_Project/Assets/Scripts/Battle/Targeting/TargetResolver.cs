using System;
using System.Collections.Generic;

/// <summary>目标是单位还是场地位置。场地位置即使为空也仍然是合法目标。</summary>
public enum BattleTargetDomain
{
    Unit = 0,
    Field = 1
}

/// <summary>
/// 与具体战斗对象无关的目标解析输入。解析器只处理位置编号，实体映射由 BattleTargetResolver 负责。
/// </summary>
public sealed class TargetResolutionRequest
{
    public BattleSide Side;
    public BattleTargetDomain Domain;
    public int MaxPosition;
    public int OriginPosition;
    public int TargetNumber;
    public int TargetConsecutive;
    public string TargetOverride;
    public string TargetSelect;
    public List<int> FixedPositions;
    public List<int> ForcedPositions;
    public List<int> PreviousPositions;
    public List<int> OverridePositions;
    public Func<int, bool> IsEligible;
    public Func<int, float> Score;
    public Func<int, int> RandomIndex;
}

/// <summary>统一目标解析结果。Positions 保留空位以及模式3产生的重复位置。</summary>
public sealed class TargetResolutionResult
{
    public BattleSide Side;
    public BattleTargetDomain Domain;
    public List<int> Positions = new List<int>();
    public string Error;

    public bool IsValid => string.IsNullOrEmpty(Error);
}

/// <summary>
/// 统一目标位置算法：
/// 0=非连续手选；1=连续手选；2=覆盖率优先的连续随机；3=随机可重复；4=随机不重复。
/// TargetSelect 以单个状态宿主为原点；TargetOverride 基于上一效果或外部筛选范围。
/// </summary>
public static class TargetResolver
{
    private const float ScoreEpsilon = 0.0001f;

    public static TargetResolutionResult Resolve(TargetResolutionRequest request)
    {
        var result = NewResult(request);
        if (request == null)
        {
            result.Error = "目标解析请求为空";
            return result;
        }

        if (request.MaxPosition <= 0)
        {
            result.Error = "目标阵营没有合法位置";
            return result;
        }

        if (!string.IsNullOrWhiteSpace(request.TargetSelect))
        {
            if (!TryParseOffsets(request.TargetSelect, 2, out var selectOffsets))
            {
                result.Error = $"TargetSelect 格式无效: {request.TargetSelect}";
                return result;
            }

            result.Positions = ExpandOrigin(
                request.OriginPosition,
                selectOffsets[0],
                selectOffsets[1],
                request.MaxPosition);
            return ValidateNonEmpty(request, result, "TargetSelect 没有产生有效目标");
        }

        if (!string.IsNullOrWhiteSpace(request.TargetOverride))
        {
            string targetOverride = request.TargetOverride.Trim();
            if (targetOverride.Contains(","))
            {
                if (!TryParseOffsets(targetOverride, -1, out var overrideOffsets)
                    || (overrideOffsets.Length != 2 && overrideOffsets.Length != 3))
                {
                    result.Error = $"TargetOverride 格式无效: {request.TargetOverride}";
                    return result;
                }

                bool includePrevious = overrideOffsets.Length == 2;
                int left = overrideOffsets[0];
                int right = overrideOffsets[overrideOffsets.Length - 1];
                result.Positions = ApplyPreviousRange(
                    request.PreviousPositions,
                    left,
                    right,
                    includePrevious,
                    request.MaxPosition);
                return ValidateNonEmpty(request, result, "TargetOverride 最终目标数不能为0");
            }

            var overridePool = Sanitize(request.OverridePositions, request.MaxPosition, false);
            if (targetOverride.StartsWith("PreAlliesDamage(", StringComparison.Ordinal))
            {
                result.Positions = SelectByMode(request, overridePool);
                return ValidateNonEmpty(request, result, "PreAlliesDamage 没有返回有效目标");
            }

            result.Positions = overridePool;
            return ValidateNonEmpty(request, result, $"没有找到带状态 {targetOverride} 的目标");
        }

        if (request.ForcedPositions != null && request.ForcedPositions.Count > 0)
        {
            result.Positions = Sanitize(request.ForcedPositions, request.MaxPosition, true);
            return ValidateNonEmpty(request, result, "手动选择范围内没有有效单位");
        }

        if (request.FixedPositions != null && request.FixedPositions.Count > 0)
        {
            result.Positions = Sanitize(request.FixedPositions, request.MaxPosition, true);
            return ValidateNonEmpty(request, result, "固定目标无效");
        }

        var fullPool = new List<int>();
        for (int position = 1; position <= request.MaxPosition; position++)
            fullPool.Add(position);

        if (request.TargetNumber < 0)
        {
            result.Positions = FilterEligible(request, fullPool);
            return ValidateNonEmpty(request, result, "目标范围内没有有效目标");
        }

        if (request.TargetNumber == 0)
        {
            result.Error = "TargetNumber 为0且没有 TargetOverride";
            return result;
        }

        result.Positions = SelectByMode(request, fullPool);
        return ValidateNonEmpty(request, result, "目标范围内没有有效目标");
    }

    /// <summary>连续模式的全部物理位置窗口。窗口包含空位，是否可用由调用方判定。</summary>
    public static List<List<int>> BuildContinuousWindows(int maxPosition, int targetCount)
    {
        var windows = new List<List<int>>();
        if (maxPosition <= 0 || targetCount <= 0 || targetCount > maxPosition) return windows;

        for (int start = 1; start <= maxPosition - targetCount + 1; start++)
        {
            var window = new List<int>();
            for (int offset = 0; offset < targetCount; offset++)
                window.Add(start + offset);
            windows.Add(window);
        }
        return windows;
    }

    /// <summary>以单个状态宿主为原点，分别向左、向右扩展。</summary>
    public static List<int> ExpandOrigin(int origin, int left, int right, int maxPosition)
    {
        var positions = new List<int>();
        int start = Math.Max(1, origin - Math.Max(0, left));
        int end = Math.Min(maxPosition, origin + Math.Max(0, right));
        for (int position = start; position <= end; position++)
            positions.Add(position);
        return positions;
    }

    /// <summary>
    /// X,X 基于上次目标范围两端扩展/缩减；X,0,X 在同一范围上运算后排除上次目标。
    /// </summary>
    public static List<int> ApplyPreviousRange(
        IList<int> previousPositions,
        int left,
        int right,
        bool includePrevious,
        int maxPosition)
    {
        var previous = Sanitize(previousPositions, maxPosition, false);
        var result = new List<int>();
        if (previous.Count == 0) return result;
        previous.Sort();

        int firstKept = Math.Min(previous.Count, Math.Max(0, -left));
        int lastKept = previous.Count - 1 - Math.Max(0, -right);
        var selected = new HashSet<int>();

        if (left > 0)
        {
            int min = previous[0];
            for (int position = Math.Max(1, min - left); position < min; position++)
                selected.Add(position);
        }

        if (includePrevious && firstKept <= lastKept)
            for (int index = firstKept; index <= lastKept; index++)
                selected.Add(previous[index]);

        if (right > 0)
        {
            int max = previous[previous.Count - 1];
            for (int position = max + 1; position <= Math.Min(maxPosition, max + right); position++)
                selected.Add(position);
        }

        result.AddRange(selected);
        result.Sort();
        return result;
    }

    private static TargetResolutionResult NewResult(TargetResolutionRequest request)
    {
        return new TargetResolutionResult
        {
            Side = request != null ? request.Side : BattleSide.Enemy,
            Domain = request != null ? request.Domain : BattleTargetDomain.Unit
        };
    }

    private static TargetResolutionResult ValidateNonEmpty(
        TargetResolutionRequest request,
        TargetResolutionResult result,
        string error)
    {
        if (result.Positions.Count == 0)
        {
            result.Error = error;
            return result;
        }

        if (request.Domain == BattleTargetDomain.Unit)
        {
            bool hasUnit = false;
            foreach (int position in result.Positions)
            {
                if (IsEligible(request, position))
                {
                    hasUnit = true;
                    break;
                }
            }
            if (!hasUnit) result.Error = error;
        }
        return result;
    }

    private static List<int> SelectByMode(TargetResolutionRequest request, List<int> pool)
    {
        int count = request.TargetNumber;
        if (count <= 0) return FilterEligible(request, pool);

        switch (request.TargetConsecutive)
        {
            case 1:
                return SelectContinuous(request, pool, count, false);
            case 2:
                return SelectContinuous(request, pool, count, true);
            case 3:
                return SelectRandom(request, pool, count, true);
            case 4:
                return SelectRandom(request, pool, count, false);
            default:
                return SelectBestNonConsecutive(request, pool, count);
        }
    }

    private static List<int> SelectContinuous(
        TargetResolutionRequest request,
        List<int> pool,
        int count,
        bool coverageFirst)
    {
        var allowed = new HashSet<int>(pool);
        var windows = BuildContinuousWindows(request.MaxPosition, count);
        var candidates = new List<List<int>>();
        int bestCoverage = int.MinValue;
        float bestScore = float.MinValue;

        foreach (var window in windows)
        {
            bool insidePool = true;
            int coverage = 0;
            float score = 0f;
            foreach (int position in window)
            {
                if (!allowed.Contains(position)) insidePool = false;
                if (allowed.Contains(position) && IsEligible(request, position)) coverage++;
                if (request.Score != null) score += request.Score(position);
            }
            if (!insidePool) continue;
            if (request.Domain == BattleTargetDomain.Unit && coverage == 0) continue;

            if (coverageFirst)
            {
                if (coverage > bestCoverage)
                {
                    bestCoverage = coverage;
                    candidates.Clear();
                    candidates.Add(window);
                }
                else if (coverage == bestCoverage)
                {
                    candidates.Add(window);
                }
            }
            else
            {
                if (score > bestScore + ScoreEpsilon)
                {
                    bestScore = score;
                    candidates.Clear();
                    candidates.Add(window);
                }
                else if (Math.Abs(score - bestScore) <= ScoreEpsilon)
                {
                    candidates.Add(window);
                }
            }
        }

        if (candidates.Count == 0) return new List<int>();
        return new List<int>(candidates[ChooseIndex(request, candidates.Count)]);
    }

    private static List<int> SelectBestNonConsecutive(
        TargetResolutionRequest request,
        List<int> pool,
        int count)
    {
        var remaining = FilterEligible(request, pool);
        var selected = new List<int>();
        while (remaining.Count > 0 && selected.Count < count)
        {
            float bestScore = float.MinValue;
            var bestIndices = new List<int>();
            for (int i = 0; i < remaining.Count; i++)
            {
                float score = request.Score != null ? request.Score(remaining[i]) : 0f;
                if (score > bestScore + ScoreEpsilon)
                {
                    bestScore = score;
                    bestIndices.Clear();
                    bestIndices.Add(i);
                }
                else if (Math.Abs(score - bestScore) <= ScoreEpsilon)
                {
                    bestIndices.Add(i);
                }
            }

            int chosenRemainingIndex = bestIndices[ChooseIndex(request, bestIndices.Count)];
            selected.Add(remaining[chosenRemainingIndex]);
            remaining.RemoveAt(chosenRemainingIndex);
        }
        return selected;
    }

    private static List<int> SelectRandom(
        TargetResolutionRequest request,
        List<int> pool,
        int count,
        bool allowRepeat)
    {
        var candidates = FilterEligible(request, pool);
        var selected = new List<int>();
        if (candidates.Count == 0) return selected;

        while (selected.Count < count && candidates.Count > 0)
        {
            int index = ChooseIndex(request, candidates.Count);
            selected.Add(candidates[index]);
            if (!allowRepeat) candidates.RemoveAt(index);
        }
        return selected;
    }

    private static List<int> FilterEligible(TargetResolutionRequest request, IList<int> positions)
    {
        var result = new List<int>();
        if (positions == null) return result;
        foreach (int position in positions)
            if (IsEligible(request, position)) result.Add(position);
        return result;
    }

    private static bool IsEligible(TargetResolutionRequest request, int position)
    {
        if (position < 1 || position > request.MaxPosition) return false;
        if (request.Domain == BattleTargetDomain.Field) return true;
        return request.IsEligible != null && request.IsEligible(position);
    }

    private static int ChooseIndex(TargetResolutionRequest request, int count)
    {
        if (count <= 1) return 0;
        if (request.RandomIndex == null) return UnityEngine.Random.Range(0, count);
        int index = request.RandomIndex(count);
        if (index < 0) index = 0;
        if (index >= count) index = count - 1;
        return index;
    }

    private static List<int> Sanitize(IList<int> source, int maxPosition, bool keepDuplicates)
    {
        var result = new List<int>();
        if (source == null) return result;
        var seen = keepDuplicates ? null : new HashSet<int>();
        foreach (int position in source)
        {
            if (position < 1 || position > maxPosition) continue;
            if (seen != null && !seen.Add(position)) continue;
            result.Add(position);
        }
        return result;
    }

    private static bool TryParseOffsets(string value, int expectedCount, out int[] offsets)
    {
        offsets = null;
        if (string.IsNullOrWhiteSpace(value)) return false;
        string[] parts = value.Split(',');
        if (expectedCount > 0 && parts.Length != expectedCount) return false;
        var parsed = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            if (!int.TryParse(parts[i].Trim(), out parsed[i])) return false;
        offsets = parsed;
        return true;
    }
}
