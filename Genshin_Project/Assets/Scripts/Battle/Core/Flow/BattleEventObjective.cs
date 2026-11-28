using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

/// <summary>
/// 事件战斗胜负钩子。只读已提交的击杀/退场计数，不解析ConditionKey文案、不发奖。
/// REB101限时清场；REB102清场后检查可莉击杀；REB104普通清场；REB105逃跑超过5名立即失败。
/// 限时计分战斗须显式设置REB103()；空胜利条件始终表示清空敌人。
/// </summary>
public sealed class BattleEventObjective
{
    private readonly Func<bool, int, int, bool, bool> win, lose;
    private readonly Dictionary<int, int> kills = new Dictionary<int, int>();
    private readonly HashSet<object> defeated = new HashSet<object>();
    private readonly HashSet<object> escaped = new HashSet<object>();
    private readonly int maxRound;
    public int Escaped { get; private set; }
    public int TotalKills { get; private set; }
    public BattleEventObjective(string winCondition, string loseCondition, int maxRound = 0)
    {
        if (maxRound < 0) throw new ArgumentOutOfRangeException(nameof(maxRound));
        this.maxRound = maxRound;
        win = Parse(winCondition, false); lose = Parse(loseCondition, true);
    }
    public void RecordKill(int characterID, object enemyInstance = null)
    {
        // 同模板的不同实例分别计数，同实例重复声明不重复领取击杀。
        if (enemyInstance != null && !defeated.Add(enemyInstance)) return;
        TotalKills++;
        if (characterID > 0) kills[characterID] = Kills(characterID) + 1;
    }
    public int Kills(int characterID) => kills.TryGetValue(characterID, out int count) ? count : 0;
    public static (int CharacterID, int Amount) RequiredCharacterKills(string condition)
    {
        if ((condition ?? "").Trim() == "REB102()") return (1016, 5);
        var match = Regex.Match((condition ?? "").Trim(), @"^KillsByCharacter\((\d+),(\d+)\)$");
        return match.Success && int.TryParse(match.Groups[1].Value, out int id) &&
            int.TryParse(match.Groups[2].Value, out int amount) && id > 0 && amount > 0 ? (id, amount) : (0, 0);
    }
    public void RecordEscape(object enemyInstance = null)
    {
        if (enemyInstance == null || escaped.Add(enemyInstance)) Escaped++;
    }
    public bool Win(bool enemiesCleared, int livingAllies, int round = 0, bool roundLimitReached = false)
        => win(enemiesCleared, livingAllies, round, roundLimitReached);
    public bool Lose(bool enemiesCleared, int livingAllies, int round = 0, bool roundLimitReached = false)
        => lose(enemiesCleared, livingAllies, round, roundLimitReached);
    private Func<bool, int, int, bool, bool> Parse(string source, bool failure)
    {
        string rule = (source ?? "").Trim();
        if (rule.Length == 0) return failure ? (_, allies, __, ___) => allies == 0
            : (cleared, _, __, ___) => cleared;
        // 同一编号在WinCondition/LoseCondition列分别解析，不把同名钩子当成同一布尔表达式。
        switch (rule)
        {
            case "REB101()":
                int deadline = maxRound > 0 ? maxRound : 5;
                return failure ? (cleared, allies, round, limit) => allies == 0 || round > deadline || limit && !cleared
                    : (cleared, _, round, __) => cleared && round <= deadline;
            case "REB102()":
                // 1016来自Characters角色总表。所有归功可莉的直伤、持续伤害和反应沿用正式来源快照。
                return failure ? (cleared, allies, _, __) => allies == 0 || cleared && Kills(1016) < 5
                    : (cleared, _, __, ___) => cleared && Kills(1016) >= 5;
            case "REB103()":
                return failure ? (_, allies, __, ___) => allies == 0
                    : (cleared, allies, _, limit) => cleared || limit && allies > 0;
            case "REB104()":
                return failure ? (_, allies, __, ___) => allies == 0 : (cleared, _, __, ___) => cleared;
            case "REB105()":
                return failure ? (_, allies, __, ___) => allies == 0 || Escaped > 5
                    : (cleared, _, __, ___) => cleared && Escaped <= 5;
        }
        if (rule == "AllEnemiesDefeated") return (cleared, _, __, ___) => cleared;
        if (rule == "AllAlliesDefeated") return (_, allies, __, ___) => allies == 0;
        if (rule == "AnySurvivor") return (_, allies, __, ___) => allies > 0;
        var m = Regex.Match(rule, @"^(KillsByCharacter|KillCount|EscapedEnemiesAtMost|EscapedEnemiesExceeds)\((\d+)(?:,(\d+))?\)$");
        if (!m.Success || !int.TryParse(m.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int first)
            || m.Groups[3].Success && !int.TryParse(m.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            throw new FormatException("未知事件战斗条件：" + rule);
        string kind = m.Groups[1].Value;
        if (kind == "KillsByCharacter")
        {
            if (first < 1 || !m.Groups[3].Success) throw new FormatException("KillsByCharacter须填写角色ID和数量。");
            int amount = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture); return (_, __, ___, ____) => Kills(first) >= amount;
        }
        if (m.Groups[3].Success) throw new FormatException(kind + "只接收一个数量。");
        return kind switch {
            "KillCount" => (_, __, ___, ____) => TotalKills >= first,
            "EscapedEnemiesAtMost" => (_, __, ___, ____) => Escaped <= first,
            _ => (_, __, ___, ____) => Escaped > first };
    }
}
