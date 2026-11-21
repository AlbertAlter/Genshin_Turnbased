using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Roguelike.Tests.RouteGeneration;
using UnityEngine;

public sealed class RoguelikePlayableRoutes : IRoguelikeRouteRules
{
    internal readonly RouteGenerationSettings Settings = new RouteGenerationSettings();
    internal RouteMapStyle Style { get; private set; }
    internal readonly Dictionary<string, Sprite> Art = new();
    internal void ReleaseArt() => RouteMapView.DestroyArt(Art);

    private RoguelikeNodeContentCatalog catalog;
    private RouteMapData map;
    private int seed, chapter;
    internal bool HasChapter(int number) => catalog.RouteStages(number).Count > 0;

    internal void Bind(RoguelikeNodeContentCatalog value)
    {
        Bind(value, Path.Combine(Application.streamingAssetsPath, "Data", "UI", "Rouge_Route", "StageRule.xlsx"));
        try { Style = RouteMapStyle.ReadFile(Path.Combine(Application.streamingAssetsPath, RouteMapStyle.RelativePath)); }
        catch (Exception error)
        {
            Debug.LogWarning("路线样式加载失败，使用默认值：" + error.Message);
            Style = new RouteMapStyle();
        }
    }

    internal void Bind(RoguelikeNodeContentCatalog value, string rulePath)
    {
        catalog = value;
        Settings.Apply(new RouteConfigLoader().LoadRouteUiRules(rulePath));
        map = null;
    }

    internal RouteMapData Map(RoguelikeProgressSnapshot snapshot)
    {
        if (map != null && seed == snapshot.RunSeed && chapter == snapshot.CurrentChapterId) return map;
        var stages = catalog.RouteStages(snapshot.CurrentChapterId);
        var result = new RouteMapGenerator(Settings, new SystemBattleRandomSource(unchecked(snapshot.RunSeed * 397 ^ snapshot.CurrentChapterId))).Generate(stages);
        if (!result.Success) throw new InvalidDataException($"第 {snapshot.CurrentChapterId} 章路线不可用：{result.Error}");
        seed = snapshot.RunSeed;
        chapter = snapshot.CurrentChapterId;
        return map = result.Map;
    }

    internal IEnumerable<RouteNodeData> Next(RoguelikeProgressSnapshot snapshot)
    {
        var route = Map(snapshot);
        // Returning from formation keeps the already selected encounter, content and save step.
        if (snapshot.Flow?.Step == RoguelikeFlowStep.Node)
            return route.Stages.SelectMany(stage => stage.Nodes).Where(node => Key(node) == snapshot.Flow.Node.NodeId);
        if (snapshot.ClearedNodeIds.Count == 0) return route.Stages[0].Nodes;
        var previous = Current(snapshot, route);
        var next = route.Edges.Where(edge => edge.FromNodeId == previous.Id).Select(edge => edge.ToNodeId).ToHashSet();
        return route.Stages.SelectMany(stage => stage.Nodes).Where(node => next.Contains(node.Id));
    }

    internal static string Key(RouteNodeData node) => node.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static RouteNodeData Current(RoguelikeProgressSnapshot snapshot, RouteMapData route)
        => route.Stages[snapshot.CurrentStageIndex].Nodes.Single(node => Key(node) == snapshot.CurrentNodeId
            && snapshot.ClearedNodeIds.Contains(snapshot.CurrentNodeId));

    public RoguelikeSaveResult ValidateNodeEntry(RoguelikeProgressSnapshot snapshot, RoguelikeNodeSelection selection)
        => Next(snapshot).Any(node => Key(node) == selection.NodeId && node.StageIndex == selection.StageIndex &&
            node.Type.ToString() == selection.Kind.ToString()) ? RoguelikeSaveResult.Success() : Invalid("节点不在当前路线的可达集合中。");

    public RoguelikeSaveResult ValidateChapterCompletion(RoguelikeProgressSnapshot snapshot)
    {
        var route = Map(snapshot);
        return snapshot.ClearedNodeIds.Count != 0 && snapshot.CurrentStageIndex == route.Stages.Count - 1 &&
            Current(snapshot, route).Type == RouteNodeType.Boss ? RoguelikeSaveResult.Success() : Invalid("章节末的首领尚未通关。");
    }

    private static RoguelikeSaveResult Invalid(string message) => RoguelikeSaveResult.Failure(RoguelikeSaveErrorCode.InvalidOperation, message);
}
