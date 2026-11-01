using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 目标选择器（2026-08-07 组合选择版）
/// 按《战斗系统流程.txt》规则：
///  1. 进入选择时自动选中"最优组合"（敌方=绝对血量最高，空位视为0；并列随机）
///  2. 组合窗口从位置1开始滑动（TargetNumber=3, Consecutive=1 → [1,2,3][2,3,4][3,4,5]）
///  3. A/D 在"非全空组合"之间切换，全空组合直接跳过不停
///  4. 敌方类型目标不允许出现全空组合（全空=无目标，禁止释放）
/// UI 接口：OnSelectionChanged 事件（本轮只打日志，视觉高亮留待 UI 实现）
/// </summary>
public class TargetSelector
{
    public int MinPosition = 1;
    public int MaxPosition = 5;
    /// <summary>选择方向（2026-08-14）：Enemy=敌方位置1~5，Ally=我方位置1~4。CurrentSelection 的位置编号在该方向内解释。</summary>
    public BattleSide Side = BattleSide.Enemy;

    /// <summary>当前选中的位置组合（如爆发 [2,3,4]）</summary>
    public List<int> CurrentSelection = new List<int>();

    /// <summary>UI 高亮接口：选中组合变化时触发（本轮由 BattleTester 订阅打日志）</summary>
    public event Action<List<int>> OnSelectionChanged;

    /// <summary>敌方位置 → 当前绝对血量 的查询委托（由 BattleManager 提供，空位返回0）</summary>
    private Func<int, float> _hpProvider;

    /// <summary>敌方位置是否存活（全空组合跳过用）</summary>
    private Func<int, bool> _aliveProvider;

    // 内部：所有非全空的候选组合（按窗口滑动顺序）
    private List<List<int>> _validWindows = new List<List<int>>();
    private readonly List<List<int>> _candidateWindows = new List<List<int>>();
    private Func<int, bool> _tauntProvider;
    private bool _allowEmptyWindows;
    // Windows retain their geometry for A/D and confirmation. Unit previews use live occupants only.
    public bool TargetsField => _allowEmptyWindows;
    public bool HasOccupant(int position) => _aliveProvider?.Invoke(position) == true;
    public bool ShowsPosition(int position) =>
        CurrentSelection.Contains(position) && (TargetsField || HasOccupant(position));
    public List<int> SelectedTargets() => CurrentSelection.FindAll(p => TargetsField || HasOccupant(p));
    private bool _tauntRestricted;
    public bool LastMoveBlockedByTaunt { get; private set; }
    private int _windowIndex = -1;

    /// <summary>
    /// 进入选择模式时初始化。
    /// </summary>
    /// <param name="targetCount">需要选几个位置（TargetNumber）</param>
    /// <param name="consecutive">1=连续滑动窗口（从位置1开始）；其他=简化按连续处理</param>
    /// <param name="hpProvider">敌方位置→当前绝对血量（空位返回0）</param>
    /// <param name="aliveProvider">敌方位置是否有存活单位</param>
    /// <param name="excludePositions">排除的位置（Consecutive=0 逐个选时已选过的位置不可重复选，2026-08-15）</param>
    public void Init(
        BattleSide side,
        int targetCount,
        int consecutive,
        Func<int, float> hpProvider,
        Func<int, bool> aliveProvider,
        List<int> excludePositions = null,
        bool allowEmptyWindows = false,
        bool preferLowerScore = false,
        Func<int, bool> tauntProvider = null)
    {
        Side = side;
        MaxPosition = side == BattleSide.Enemy ? 5 : BattleField.ALLY_SLOTS;
        _hpProvider = hpProvider;
        _aliveProvider = aliveProvider;
        _tauntProvider = !allowEmptyWindows && consecutive >= 0 && consecutive <= 1 ? tauntProvider : null;
        _allowEmptyWindows = allowEmptyWindows;
        LastMoveBlockedByTaunt = false;

        int count = Mathf.Max(1, targetCount);
        _validWindows.Clear();
        _candidateWindows.Clear();

        // 生成滑动窗口：从位置1到位置 Max-count+1
        int maxStart = MaxPosition - count + 1;
        if (maxStart < MinPosition) maxStart = MinPosition;

        for (int start = MinPosition; start <= maxStart; start++)
        {
            var win = new List<int>();
            for (int i = 0; i < count; i++) win.Add(start + i);
            bool containsExcluded = false;
            if (excludePositions != null)
                foreach (int position in win)
                    if (excludePositions.Contains(position)) { containsExcluded = true; break; }
            if (containsExcluded) continue;

            // 单位目标：全空组合不参与切换；场地目标允许选择空位置。
            _candidateWindows.Add(win);
        }
        RefreshWindows(false);

        // 全部全空（无目标可选）→ 留空，由调用方判断 IsSelectionValid()==false
        if (_validWindows.Count == 0)
        {
            _windowIndex = -1;
            CurrentSelection = new List<int>();
            return;
        }

        // 自动选中最优组合：绝对血量最高；并列随机
        float bestHp = preferLowerScore ? float.MaxValue : float.MinValue;
        List<int> bestCandidates = new List<int>();
        string windowLog = "";
        for (int i = 0; i < _validWindows.Count; i++)
        {
            float hp = SumHp(_validWindows[i]);
            windowLog += $"[{string.Join("", _validWindows[i])}={hp:F0}] ";
            bool isBetter = preferLowerScore
                ? hp < bestHp - 0.0001f
                : hp > bestHp + 0.0001f;
            if (isBetter)
            {
                bestHp = hp;
                bestCandidates.Clear();
                bestCandidates.Add(i);
            }
            else if (Mathf.Abs(hp - bestHp) <= 0.0001f)
            {
                bestCandidates.Add(i);
            }
        }
        Log($"自动选择 各组合血量: {windowLog}");

        // 并列：随机选一个
        _windowIndex = bestCandidates[BattleRandom.NextInt(0, bestCandidates.Count)];
        ApplySelection("自动");
    }

    /// <summary>是否有可选的合法组合（敌方全空=false）</summary>
    public bool IsSelectionValid()
    {
        RefreshWindows(true);
        return _validWindows.Count > 0;
    }

    /// <summary>新战斗开始前清空上一场的选择窗口和查询委托。</summary>
    public void Reset()
    {
        CurrentSelection.Clear();
        _validWindows.Clear();
        _candidateWindows.Clear();
        _tauntProvider = null;
        _tauntRestricted = false;
        LastMoveBlockedByTaunt = false;
        _windowIndex = -1;
        _hpProvider = null;
        _aliveProvider = null;
        Side = BattleSide.Enemy;
        MinPosition = 1;
        MaxPosition = BattleField.ENEMY_SLOTS;
    }

    /// <summary>
    /// 全体模式（2026-08-15，Consecutive=2/3/4 随机）：目标方向所有存活位置全选，走个流程，A/D 无切换，空格直接施放。
    /// </summary>
    public void InitAll(
        BattleSide side,
        Func<int, float> hpProvider,
        Func<int, bool> aliveProvider,
        bool includeEmptyPositions = false)
    {
        Side = side;
        MaxPosition = side == BattleSide.Enemy ? 5 : BattleField.ALLY_SLOTS;
        _hpProvider = hpProvider;
        _aliveProvider = aliveProvider;
        _tauntProvider = null;
        _tauntRestricted = false;
        LastMoveBlockedByTaunt = false;
        _allowEmptyWindows = includeEmptyPositions;

        var all = new List<int>();
        for (int p = MinPosition; p <= MaxPosition; p++)
            if (includeEmptyPositions || (_aliveProvider != null && _aliveProvider(p))) all.Add(p);

        _validWindows.Clear();
        _candidateWindows.Clear();
        if (all.Count == 0)
        {
            _windowIndex = -1;
            CurrentSelection = new List<int>();
            return;
        }
        _validWindows.Add(all);
        _candidateWindows.Add(all);
        _windowIndex = 0;
        ApplySelection("全体");
    }

    /// <summary>向左切换（窗口索引-1，位置号变小，屏幕方向=向右）：跳到前一个非全空组合；到边界停</summary>
    public void MoveLeft()
    {
        RefreshWindows(true);
        LastMoveBlockedByTaunt = false;
        if (_windowIndex <= 0) { LastMoveBlockedByTaunt = _tauntRestricted; Log("已到最右"); return; }
        _windowIndex--;
        ApplySelection("手动");
    }

    /// <summary>向右切换（窗口索引+1，位置号变大，屏幕方向=向左）：跳到下一个非全空组合；到边界停</summary>
    public void MoveRight()
    {
        RefreshWindows(true);
        LastMoveBlockedByTaunt = false;
        if (_windowIndex < 0 || _windowIndex >= _validWindows.Count - 1) { LastMoveBlockedByTaunt = _tauntRestricted; Log("已到最左"); return; }
        _windowIndex++;
        ApplySelection("手动");
    }

    /// <summary>Screen-space movement, independent of numeric slot order. Does not reroll targets.</summary>
    public void MoveScreen(int direction, Func<int, float?> screenX = null)
    {
        if (direction == 0) return;
        RefreshWindows(true);
        LastMoveBlockedByTaunt = false;
        if (_windowIndex < 0) return;
        var centers = new float[_validWindows.Count];
        bool completeLayout = screenX != null;
        for (int i = 0; i < _validWindows.Count && completeLayout; i++)
        {
            foreach (int slot in _validWindows[i])
            {
                float? x = screenX(slot);
                if (!x.HasValue || float.IsNaN(x.Value) || float.IsInfinity(x.Value)) { completeLayout = false; break; }
                centers[i] += x.Value;
            }
            centers[i] /= _validWindows[i].Count;
        }
        // Legacy scenes without a layout keep their original enemy-right-to-left / ally-left-to-right convention.
        if (!completeLayout)
            for (int i = 0; i < _validWindows.Count; i++)
            {
                centers[i] = 0;
                foreach (int slot in _validWindows[i]) centers[i] += Side == BattleSide.Enemy ? -slot : slot;
                centers[i] /= _validWindows[i].Count;
            }
        int best = -1;
        float distance = float.PositiveInfinity;
        for (int i = 0; i < centers.Length; i++)
        {
            float delta = (centers[i] - centers[_windowIndex]) * (direction > 0 ? 1 : -1);
            if (delta > .0001f && delta < distance) { best = i; distance = delta; }
        }
        if (best < 0)
        {
            LastMoveBlockedByTaunt = _tauntRestricted;
            Log(direction < 0 ? "已到最左" : "已到最右");
            return;
        }
        _windowIndex = best;
        ApplySelection("手动");
    }

    private void RefreshWindows(bool preserveSelection)
    {
        var taunts = new HashSet<int>();
        foreach (var window in _candidateWindows)
            foreach (int p in window)
                if (_aliveProvider?.Invoke(p) == true && _tauntProvider?.Invoke(p) == true) taunts.Add(p);
        _tauntRestricted = taunts.Count > 0;
        _validWindows.Clear();
        foreach (var window in _candidateWindows)
            if ((_allowEmptyWindows || !IsAllEmpty(window))
                && (!_tauntRestricted || window.Exists(taunts.Contains))) _validWindows.Add(window);
        if (!preserveSelection) return;
        int previous = _validWindows.FindIndex(window => window.Count == CurrentSelection.Count
            && window.TrueForAll(CurrentSelection.Contains));
        _windowIndex = previous >= 0 ? previous : _validWindows.Count > 0 ? 0 : -1;
        if (previous < 0) ApplySelection("目标状态更新");
    }

    private void ApplySelection(string source)
    {
        if (_windowIndex >= 0 && _windowIndex < _validWindows.Count)
            CurrentSelection = new List<int>(_validWindows[_windowIndex]);
        else
            CurrentSelection = new List<int>();

        string s = CurrentSelection.Count > 0 ? string.Join("", CurrentSelection) : "无";
        Log($"当前选择目标 {s}（{source}）");

        OnSelectionChanged?.Invoke(new List<int>(CurrentSelection));
    }

    private bool IsAllEmpty(List<int> win)
    {
        foreach (int pos in win)
            if (_aliveProvider != null && _aliveProvider(pos)) return false;
        return true;
    }

    private float SumHp(List<int> win)
    {
        float sum = 0f;
        foreach (int pos in win)
            if (_hpProvider != null) sum += _hpProvider(pos);
        return sum;
    }

    private void Log(string msg)
    {
        LogManager.Log(LogCategory.Select, msg);
    }
}
