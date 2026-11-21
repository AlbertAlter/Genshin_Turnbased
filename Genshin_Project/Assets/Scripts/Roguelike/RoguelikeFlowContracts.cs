using System;
using System.Globalization;
using System.IO;

internal static class RoguelikeStageId
{
    internal static int Parse(string value, string error)
    {
        if (value.Length < 2 || (value[0] != 'R' && value[0] != 'r') ||
            !int.TryParse(value.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out int id) ||
            id < 101 || id % 100 == 0)
            throw new InvalidDataException(error + $"节点编号无效 [{value}]。");
        return id;
    }

    internal static bool IsReached(int stageId, RoguelikeProgressSnapshot snapshot)
    {
        int chapter = stageId / 100, node = stageId % 100;
        if (snapshot.CurrentChapterId < chapter) return false;
        if (snapshot.CurrentChapterId == chapter) return snapshot.CurrentStageIndex >= node - 1;
        return snapshot.CompletedChapterNodeCounts == null ||
            !snapshot.CompletedChapterNodeCounts.TryGetValue(chapter, out int count) || count >= node;
    }
}

public enum RoguelikeFlowStep
{
    Route,
    Node,
    Battle,
    Settlement,
    ChapterCompleted,
}

public enum RoguelikeNodeKind
{
    Battle,
    Event,
    Elite,
    Boss,
}

/// <summary>由路线/内容模块解析，不由流程层随机抽取或根据 ID 猜测内容。</summary>
[Serializable]
public sealed class RoguelikeNodeSelection
{
    public string NodeId;
    public int StageIndex;
    public RoguelikeNodeKind Kind;
    public string ContentId;
    // Explicit route/content identity. Not every Boss must be a chapter-ending Boss.
    public bool IsChapterFinalBoss;
}

/// <summary>可选的流程恢复数据。旧存档没有该字段时不改变原存档结构的含义。</summary>
[Serializable]
public sealed class RoguelikeFlowData
{
    public RoguelikeFlowStep Step;
    public string StepId;
    public RoguelikeNodeSelection Node;
    public System.Collections.Generic.List<AppearanceBattleParticipant> AppearanceParticipants;
}

/// <summary>
/// 每次读取都是独立副本。StepId 随步骤切换，异步界面/战斗回调提交时必须带回，防止旧结果重复入账。
/// BattleId 只在战斗恢复点有意义。队伍由节点前的配置模块决定；这里不管理队伍预设。
/// </summary>
public sealed class RoguelikeFlowContext
{
    public string RunId;
    public string BattleId;
    public RoguelikeResumePoint ResumePoint;
    public RoguelikeProgressSnapshot Snapshot;

    public RoguelikeFlowData Flow => Snapshot.Flow;
}

/// <summary>正式路线模块接入点：负责连通性、可选节点、内容合法性和章节完成条件。</summary>
public interface IRoguelikeRouteRules
{
    RoguelikeSaveResult ValidateNodeEntry(RoguelikeProgressSnapshot snapshot, RoguelikeNodeSelection node);
    RoguelikeSaveResult ValidateChapterCompletion(RoguelikeProgressSnapshot snapshot);
}

/// <summary>
/// 节点、事件、奖励界面等独立模块的接入约定。流程层不会自动执行处理器或伪造处理成功。
/// 处理器结束后显式调用流程服务的对应提交接口；需随机生成的待选内容由模块自行持久化。
/// </summary>
public interface IRoguelikeFlowStepHandler
{
    bool CanHandle(RoguelikeFlowStep step);
    void Enter(RoguelikeFlowContext context);
}

/// <summary>
/// 战斗场景接入点：从本次确认的配置及快照构建战斗，不读取队伍预设。
/// StartBattle 前必须成功调用流程服务 BeginBattle，初始化完成后应用 RoguelikeBattleEntryRules。
/// 胜利回调携带原 StepId 调用 RecordBattleVictory；失败/重试读取原战前恢复点，不提交战斗中间态。
/// </summary>
public interface IRoguelikeBattlePort
{
    void StartBattle(RoguelikeFlowContext context);
}

/// <summary>节点解锁与养成模块的接入点；具体等级上限和突破/天赋启用规则留给对应模块。</summary>
public interface IRoguelikeProgressionRules
{
    RoguelikeSaveResult ValidateChange(RoguelikeProgressSnapshot current, RoguelikeProgressSnapshot proposed);
}
