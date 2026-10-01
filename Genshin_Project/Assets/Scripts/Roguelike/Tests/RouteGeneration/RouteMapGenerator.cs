using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Roguelike.Tests.RouteGeneration
{
    public sealed class RouteMapGenerator
    {
        private readonly RouteGenerationSettings _settings;
        private readonly IBattleRandomSource _random;
        private readonly RouteRandomLog _randomLog;
        private int _nextNodeId;

        private sealed class NarrowAngleViolation
        {
            public RouteNodeData SharedNode;
            public RouteEdgeData FirstEdge;
            public RouteEdgeData SecondEdge;
            public RouteNodeData FirstOther;
            public RouteNodeData SecondOther;
        }

        public RouteMapGenerator(RouteGenerationSettings settings, IBattleRandomSource randomSource = null,
            RouteRandomLog randomLog = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _random = randomSource ?? BattleRandom.Source;
            _randomLog = randomLog;
        }

        public RouteGenerationResult Generate(IReadOnlyList<RouteStageConfig> configs)
        {
            if (configs == null || configs.Count < 2)
                return RouteGenerationResult.Failed("At least two stage configurations are required.");

            try
            {
                _settings.Validate();
            }
            catch (Exception exception)
            {
                return RouteGenerationResult.Failed(exception.Message);
            }

            string lastError = "Unknown validation error.";
            for (int attempt = 1; attempt <= _settings.MaximumGenerationAttempts; attempt++)
            {
                _randomLog?.BeginAttempt(attempt);
                _nextNodeId = 1;
                RouteMapData map = BuildAttempt(configs, attempt);
                if (Validate(map, out lastError) && ValidateGeometry(map, out lastError))
                    return RouteGenerationResult.Succeeded(map);
                _randomLog?.RecordRejectedAttempt(lastError);
            }

            return RouteGenerationResult.Failed(
                "Route generation failed after " + _settings.MaximumGenerationAttempts + " attempts. Last error: " + lastError);
        }

        private RouteMapData BuildAttempt(IReadOnlyList<RouteStageConfig> configs, int attempt)
        {
            RouteMapData map = new RouteMapData
            {
                AttemptNumber = attempt,
                ContentWidth = _settings.StageStartX * 2f + (configs.Count - 1) * _settings.StageWidth
            };

            for (int stageIndex = 0; stageIndex < configs.Count; stageIndex++)
            {
                RouteStageConfig config = configs[stageIndex];
                bool isEndpointStage = stageIndex == 0 || stageIndex == configs.Count - 1;
                int nodeCount = _random.NextInt(config.MinEncounter, config.MaxEncounter + 1);
                if (isEndpointStage && nodeCount != 1)
                    throw new InvalidOperationException(config.StageId + " must contain exactly one endpoint node.");
                _randomLog?.RecordNodeCount(config.StageId, config.MinEncounter, config.MaxEncounter, nodeCount);
                List<int> slots = isEndpointStage
                    ? new List<int> { 3 }
                    : ChooseUniqueSlots(nodeCount);
                _randomLog?.RecordSlots(config.StageId, slots);
                RouteStageData stage = new RouteStageData { StageId = config.StageId, StageIndex = stageIndex };
                RouteStageData previous = stageIndex > 0 ? map.Stages[stageIndex - 1] : null;

                foreach (int slot in slots)
                {
                    Vector2 standard = new Vector2(
                        _settings.StageStartX + stageIndex * _settings.StageWidth,
                        isEndpointStage
                            ? _settings.MidlineHeight
                            : _settings.MidlineHeight + (2.5f - slot) * _settings.CoordinateDistance);
                    RouteNodeData node = new RouteNodeData
                    {
                        Id = _nextNodeId++,
                        StageIndex = stageIndex,
                        SlotIndex = slot,
                        Type = DrawNodeType(config, slot),
                        StandardPosition = standard,
                        Position = isEndpointStage
                            ? standard
                            : SamplePosition(standard, stage.Nodes, previous?.Nodes)
                    };
                    stage.Nodes.Add(node);
                }
                stage.Nodes.Sort((left, right) => left.SlotIndex.CompareTo(right.SlotIndex));
                map.Stages.Add(stage);
            }

            AddBaseEdges(map);
            AddExtraEdges(map);
            RepairNarrowAngles(map);
            RepairNodeTypes(map, configs);
            AdjustLongEdges(map);
            return map;
        }

        private List<int> ChooseUniqueSlots(int count)
        {
            if (count <= 1)
                return new List<int> { _random.NextInt(0, 6) };

            // 普通层至少在中线上下两半各有一个节点；其余位置再从未选坐标中等概率抽取。
            List<int> selected = new List<int>
            {
                _random.NextInt(0, 3),
                _random.NextInt(3, 6)
            };
            List<int> remaining = Enumerable.Range(0, 6).Where(slot => !selected.Contains(slot)).ToList();
            while (selected.Count < count)
            {
                int index = _random.NextInt(0, remaining.Count);
                selected.Add(remaining[index]);
                remaining.RemoveAt(index);
            }
            return selected.OrderBy(slot => slot).ToList();
        }

        private RouteNodeType DrawNodeType(RouteStageConfig config, int slotIndex)
        {
            int roll = _random.NextInt(0, config.TotalWeight);
            int remaining = roll;
            RouteNodeType result;
            if (remaining < config.BattleWeight)
                result = RouteNodeType.Battle;
            else if ((remaining -= config.BattleWeight) < config.EventWeight)
                result = RouteNodeType.Event;
            else if ((remaining -= config.EventWeight) < config.EliteWeight)
                result = RouteNodeType.Elite;
            else
                result = RouteNodeType.Boss;
            _randomLog?.RecordNodeType(config.StageId, slotIndex, roll, config, result);
            return result;
        }

        private Vector2 SamplePosition(Vector2 standard, IReadOnlyCollection<RouteNodeData> sameStage,
            IReadOnlyCollection<RouteNodeData> previousStage)
        {
            List<RouteNodeData> relevant = new List<RouteNodeData>(sameStage);
            if (previousStage != null)
                relevant.AddRange(previousStage);

            Vector2 best = standard;
            float bestMinimumDistance = float.NegativeInfinity;
            for (int sample = 0; sample < _settings.PositionSampleAttempts; sample++)
            {
                float angle = _random.NextFloat01() * Mathf.PI * 2f;
                float radius = Mathf.Sqrt(_random.NextFloat01()) * _settings.RandomizeRadius;
                Vector2 candidate = standard + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                float minimumDistance = relevant.Count == 0
                    ? float.PositiveInfinity
                    : relevant.Min(node => Vector2.Distance(candidate, node.Position));
                if (minimumDistance > bestMinimumDistance)
                {
                    best = candidate;
                    bestMinimumDistance = minimumDistance;
                }
                if (minimumDistance >= _settings.NoCloserThan)
                    return candidate;
            }
            return best;
        }

        private static void AddBaseEdges(RouteMapData map)
        {
            for (int stageIndex = 1; stageIndex < map.Stages.Count; stageIndex++)
            {
                RouteStageData previous = map.Stages[stageIndex - 1];
                RouteStageData current = map.Stages[stageIndex];
                foreach (RouteNodeData node in current.Nodes)
                    AddEdgeIfMissing(map, FindNearest(node, previous.Nodes).Id, node.Id);

                foreach (RouteNodeData node in previous.Nodes)
                {
                    if (!map.Edges.Any(edge => edge.FromNodeId == node.Id))
                        AddEdgeIfMissing(map, node.Id, FindNearest(node, current.Nodes).Id);
                }
            }
        }

        private void AddExtraEdges(RouteMapData map)
        {
            Dictionary<int, RouteNodeData> nodes = AllNodes(map);
            for (int stageIndex = 1; stageIndex < map.Stages.Count - 1; stageIndex++)
            {
                RouteStageData sourceStage = map.Stages[stageIndex];
                RouteStageData targetStage = map.Stages[stageIndex + 1];
                if (!AllNodesHaveSingleOutgoingEdge(sourceStage, map.Edges))
                    continue;

                int desired = DrawExtraEdgeCount(sourceStage.StageId, sourceStage.Nodes.Count);
                if (desired == 0 || targetStage.Nodes.Count < 2)
                    continue;

                List<RouteNodeData> sources = new List<RouteNodeData>(sourceStage.Nodes);
                Shuffle(sources);
                int added = 0;
                foreach (RouteNodeData source in sources)
                {
                    RouteNodeData target = targetStage.Nodes
                        .OrderBy(node => (node.Position - source.Position).sqrMagnitude)
                        .Skip(1)
                        .FirstOrDefault();
                    if (target == null || map.Edges.Any(edge => edge.FromNodeId == source.Id && edge.ToNodeId == target.Id))
                        continue;
                    RouteEdgeData candidate = new RouteEdgeData(source.Id, target.Id);
                    if (ExceedsMaximumSlotDifference(source, target) || WouldCrossAny(candidate, map.Edges, nodes))
                        continue;
                    map.Edges.Add(candidate);
                    added++;
                    if (added >= desired)
                        break;
                }
            }
        }

        private int DrawExtraEdgeCount(string stageId, int sourceNodeCount)
        {
            float roll = _random.NextFloat01();
            int result;
            if (sourceNodeCount == 2)
                result = roll < _settings.AdditionalRouteOneChanceWhenOnlyTwo ? 1 : 0;
            else if (roll < _settings.AdditionalRouteTwoChance)
                result = 2;
            else if (roll < _settings.AdditionalRouteTwoChance + _settings.AdditionalRouteOneChance)
                result = 1;
            else
                result = 0;
            _randomLog?.RecordExtraEdges(stageId, sourceNodeCount, roll, result);
            return result;
        }

        private void RepairNarrowAngles(RouteMapData map)
        {
            int nodeCount = map.Stages.Sum(stage => stage.Nodes.Count);
            int maximumRepairs = Mathf.Max(1, nodeCount * 16);
            for (int repair = 0; repair < maximumRepairs; repair++)
            {
                Dictionary<int, RouteNodeData> nodes = AllNodes(map);
                if (!TryFindFirstNarrowAngle(map, nodes, out NarrowAngleViolation violation))
                    return;
                if (!TryRegenerateLongerEdgeEndpoint(map, nodes, violation))
                    return;
            }
        }

        private bool TryFindFirstNarrowAngle(RouteMapData map,
            IReadOnlyDictionary<int, RouteNodeData> nodes, out NarrowAngleViolation violation)
        {
            // 依设计从第二层开始，层内按标准坐标编号扫描；每次修复后会重新从这里开始。
            for (int stageIndex = 1; stageIndex < map.Stages.Count; stageIndex++)
            {
                foreach (RouteNodeData node in map.Stages[stageIndex].Nodes.OrderBy(item => item.SlotIndex))
                {
                    List<RouteEdgeData> incoming = map.Edges
                        .Where(edge => edge.ToNodeId == node.Id).ToList();
                    if (TryFindNarrowAngleInGroup(node, incoming, true, nodes, out violation))
                        return true;

                    List<RouteEdgeData> outgoing = map.Edges
                        .Where(edge => edge.FromNodeId == node.Id).ToList();
                    if (TryFindNarrowAngleInGroup(node, outgoing, false, nodes, out violation))
                        return true;
                }
            }

            // 首层不在主要扫描顺序内，但最终仍必须满足全局合格。
            foreach (RouteNodeData node in map.Stages[0].Nodes.OrderBy(item => item.SlotIndex))
            {
                List<RouteEdgeData> outgoing = map.Edges
                    .Where(edge => edge.FromNodeId == node.Id).ToList();
                if (TryFindNarrowAngleInGroup(node, outgoing, false, nodes, out violation))
                    return true;
            }

            violation = null;
            return false;
        }

        private bool TryFindNarrowAngleInGroup(RouteNodeData sharedNode,
            IReadOnlyList<RouteEdgeData> edges, bool incoming,
            IReadOnlyDictionary<int, RouteNodeData> nodes, out NarrowAngleViolation violation)
        {
            for (int first = 0; first < edges.Count; first++)
            for (int second = first + 1; second < edges.Count; second++)
            {
                RouteNodeData firstOther = nodes[incoming ? edges[first].FromNodeId : edges[first].ToNodeId];
                RouteNodeData secondOther = nodes[incoming ? edges[second].FromNodeId : edges[second].ToNodeId];
                if (Vector2.Angle(firstOther.Position - sharedNode.Position,
                        secondOther.Position - sharedNode.Position) >= _settings.MinimumSameLayerEdgeAngle)
                    continue;

                violation = new NarrowAngleViolation
                {
                    SharedNode = sharedNode,
                    FirstEdge = edges[first],
                    SecondEdge = edges[second],
                    FirstOther = firstOther,
                    SecondOther = secondOther
                };
                return true;
            }

            violation = null;
            return false;
        }

        private bool TryRegenerateLongerEdgeEndpoint(RouteMapData map,
            IReadOnlyDictionary<int, RouteNodeData> nodes, NarrowAngleViolation violation)
        {
            float firstLength = Vector2.Distance(violation.SharedNode.Position, violation.FirstOther.Position);
            float secondLength = Vector2.Distance(violation.SharedNode.Position, violation.SecondOther.Position);
            RouteNodeData movable = firstLength >= secondLength ? violation.FirstOther : violation.SecondOther;
            if (movable.StageIndex == 0 || movable.StageIndex == map.Stages.Count - 1)
                return false;

            HashSet<string> angleBefore = GetAngleViolationKeys(map, nodes);
            HashSet<string> crossingBefore = GetCrossingViolationKeys(map, nodes);
            HashSet<string> overlongBefore = GetOverlongEdgeKeys(
                map, nodes, _settings.StandardCoordinateInterval * 1.7f);
            HashSet<string> tooCloseBefore = GetTooClosePairKeys(nodes, _settings.NoCloserThan);
            Vector2 originalPosition = movable.Position;
            int attempts = Mathf.Max(1, _settings.PositionSampleAttempts * 8);

            for (int attempt = 0; attempt < attempts; attempt++)
            {
                float angle = _random.NextFloat01() * Mathf.PI * 2f;
                float radius = Mathf.Sqrt(_random.NextFloat01()) * _settings.RandomizeRadius;
                movable.Position = movable.StandardPosition
                                   + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;

                Vector2 firstDirection = violation.FirstOther.Position - violation.SharedNode.Position;
                Vector2 secondDirection = violation.SecondOther.Position - violation.SharedNode.Position;
                if (Vector2.Angle(firstDirection, secondDirection) + 0.0001f
                    < _settings.MinimumSameLayerEdgeAngle)
                    continue;

                HashSet<string> angleAfter = GetAngleViolationKeys(map, nodes);
                if (angleAfter.Count >= angleBefore.Count || angleAfter.Except(angleBefore).Any())
                    continue;
                HashSet<string> crossingAfter = GetCrossingViolationKeys(map, nodes);
                if (crossingAfter.Except(crossingBefore).Any())
                    continue;
                HashSet<string> overlongAfter = GetOverlongEdgeKeys(
                    map, nodes, _settings.StandardCoordinateInterval * 1.7f);
                if (overlongAfter.Except(overlongBefore).Any())
                    continue;
                HashSet<string> tooCloseAfter = GetTooClosePairKeys(nodes, _settings.NoCloserThan);
                if (tooCloseAfter.Except(tooCloseBefore).Any())
                    continue;
                return true;
            }

            movable.Position = originalPosition;
            return false;
        }

        private void RepairNodeTypes(RouteMapData map, IReadOnlyList<RouteStageConfig> configs)
        {
            int nodeCount = map.Stages.Sum(stage => stage.Nodes.Count);
            int maximumRepairs = Mathf.Max(1, nodeCount * 8);

            for (int repair = 0; repair < maximumRepairs; repair++)
            {
                if (!TryFindLongTypeRun(map, out List<RouteNodeData> run))
                    break;
                List<RouteNodeData> candidates = run
                    .Where(node => GetInDegree(node.Id, map.Edges) > 1 || GetOutDegree(node.Id, map.Edges) > 1)
                    .ToList();
                if (candidates.Count == 0)
                    candidates.AddRange(run);
                Shuffle(candidates);

                bool changed = false;
                foreach (RouteNodeData node in candidates)
                {
                    if (!TryRerollNodeType(map, node, configs[node.StageIndex], node.Type, false,
                            "four-or-more consecutive nodes"))
                        continue;
                    changed = true;
                    break;
                }
                if (!changed)
                    return;
            }

            for (int repair = 0; repair < maximumRepairs; repair++)
            {
                Dictionary<int, RouteNodeData> nodes = AllNodes(map);
                List<RouteEdgeData> adjacentEliteEdges = map.Edges
                    .Where(edge => nodes[edge.FromNodeId].Type == RouteNodeType.Elite
                                   && nodes[edge.ToNodeId].Type == RouteNodeType.Elite)
                    .ToList();
                if (adjacentEliteEdges.Count == 0)
                    return;

                RouteEdgeData selectedEdge = adjacentEliteEdges[_random.NextInt(0, adjacentEliteEdges.Count)];
                List<RouteNodeData> edgeNodes = new List<RouteNodeData>
                {
                    nodes[selectedEdge.FromNodeId],
                    nodes[selectedEdge.ToNodeId]
                };
                List<RouteNodeData> candidates = edgeNodes
                    .Where(node => GetInDegree(node.Id, map.Edges) <= 1 && GetOutDegree(node.Id, map.Edges) <= 1)
                    .ToList();
                if (candidates.Count == 0)
                    candidates.AddRange(edgeNodes);
                Shuffle(candidates);

                bool changed = false;
                foreach (RouteNodeData node in candidates)
                {
                    if (!TryRerollNodeType(map, node, configs[node.StageIndex], RouteNodeType.Elite, true,
                            "adjacent elite nodes"))
                        continue;
                    changed = true;
                    break;
                }
                if (!changed)
                    return;
            }
        }

        private bool TryRerollNodeType(RouteMapData map, RouteNodeData node, RouteStageConfig config,
            RouteNodeType excludedType, bool requireNoLongRun, string reason)
        {
            List<RouteNodeType> candidates = new List<RouteNodeType>();
            List<int> weights = new List<int>();
            int totalWeight = 0;
            RouteNodeType originalType = node.Type;

            foreach (RouteNodeType candidate in Enum.GetValues(typeof(RouteNodeType)))
            {
                if (candidate == excludedType)
                    continue;
                int weight = GetTypeWeight(config, candidate);
                if (weight <= 0)
                    continue;
                if (requireNoLongRun)
                {
                    node.Type = candidate;
                    bool createsLongRun = TryFindLongTypeRun(map, out _);
                    node.Type = originalType;
                    if (createsLongRun)
                        continue;
                }
                candidates.Add(candidate);
                weights.Add(weight);
                totalWeight += weight;
            }

            if (totalWeight <= 0)
                return false;
            int roll = _random.NextInt(0, totalWeight);
            int remaining = roll;
            RouteNodeType result = candidates[candidates.Count - 1];
            for (int index = 0; index < candidates.Count; index++)
            {
                if (remaining < weights[index])
                {
                    result = candidates[index];
                    break;
                }
                remaining -= weights[index];
            }
            node.Type = result;
            _randomLog?.RecordTypeReroll(reason, config.StageId, node.SlotIndex, originalType,
                excludedType, roll, totalWeight, result);
            return true;
        }

        private static int GetTypeWeight(RouteStageConfig config, RouteNodeType type)
        {
            switch (type)
            {
                case RouteNodeType.Battle: return config.BattleWeight;
                case RouteNodeType.Event: return config.EventWeight;
                case RouteNodeType.Elite: return config.EliteWeight;
                case RouteNodeType.Boss: return config.BossWeight;
                default: return 0;
            }
        }

        public static bool TryFindLongTypeRun(RouteMapData map, out List<RouteNodeData> run)
        {
            Dictionary<int, RouteNodeData> nodes = AllNodes(map);
            Dictionary<int, int> lengths = new Dictionary<int, int>();
            Dictionary<int, int> previous = new Dictionary<int, int>();

            foreach (RouteStageData stage in map.Stages)
            foreach (RouteNodeData node in stage.Nodes)
            {
                int bestLength = 1;
                int bestPrevious = -1;
                foreach (RouteEdgeData edge in map.Edges.Where(edge => edge.ToNodeId == node.Id))
                {
                    RouteNodeData predecessor = nodes[edge.FromNodeId];
                    if (predecessor.Type != node.Type)
                        continue;
                    int candidateLength = lengths[predecessor.Id] + 1;
                    if (candidateLength <= bestLength)
                        continue;
                    bestLength = candidateLength;
                    bestPrevious = predecessor.Id;
                }
                lengths[node.Id] = bestLength;
                previous[node.Id] = bestPrevious;
                if (bestLength < 4)
                    continue;

                run = new List<RouteNodeData>();
                int current = node.Id;
                while (current >= 0 && nodes[current].Type == node.Type)
                {
                    run.Add(nodes[current]);
                    current = previous[current];
                }
                run.Reverse();
                return true;
            }

            run = null;
            return false;
        }

        private static int GetInDegree(int nodeId, IEnumerable<RouteEdgeData> edges)
        {
            return edges.Count(edge => edge.ToNodeId == nodeId);
        }

        private static int GetOutDegree(int nodeId, IEnumerable<RouteEdgeData> edges)
        {
            return edges.Count(edge => edge.FromNodeId == nodeId);
        }

        private void AdjustLongEdges(RouteMapData map)
        {
            Dictionary<int, RouteNodeData> nodes = AllNodes(map);
            float interval = _settings.StandardCoordinateInterval;
            float thresholdDistance = interval * _settings.LongEdgeThresholdIntervals;
            float targetDistance = interval * _settings.LongEdgeTargetIntervals;
            float stepDistance = interval * _settings.LongEdgeMoveStepIntervals;

            // Edges enter a stage from the previous stage. This orders the scan from stage two,
            // then by the target node's standard-coordinate number as required by the design.
            List<RouteEdgeData> orderedEdges = map.Edges
                .OrderBy(edge => nodes[edge.ToNodeId].StageIndex)
                .ThenBy(edge => nodes[edge.ToNodeId].SlotIndex)
                .ThenBy(edge => nodes[edge.FromNodeId].SlotIndex)
                .ToList();

            foreach (RouteEdgeData edge in orderedEdges)
            {
                RouteNodeData from = nodes[edge.FromNodeId];
                RouteNodeData to = nodes[edge.ToNodeId];
                float initialDistance = Vector2.Distance(from.Position, to.Position);
                if (initialDistance <= thresholdDistance)
                    continue;

                bool freezeFrom = from.StageIndex == 0;
                bool freezeTo = to.StageIndex == map.Stages.Count - 1;
                int movingEndpointCount = (freezeFrom ? 0 : 1) + (freezeTo ? 0 : 1);
                if (movingEndpointCount == 0)
                    continue;
                int maximumSteps = Mathf.CeilToInt(
                    (initialDistance - targetDistance) / (movingEndpointCount * stepDistance)) + 2;
                for (int step = 0; step < maximumSteps; step++)
                {
                    Vector2 delta = to.Position - from.Position;
                    float distance = delta.magnitude;
                    if (distance < targetDistance || distance <= Mathf.Epsilon)
                        break;

                    float moveDistance = Mathf.Min(stepDistance,
                        (distance - targetDistance) / movingEndpointCount + interval * 0.0001f);
                    Vector2 direction = delta / distance;
                    Vector2 oldFrom = from.Position;
                    Vector2 oldTo = to.Position;
                    HashSet<string> crossingBefore = GetCrossingViolationKeys(map, nodes);
                    HashSet<string> angleBefore = GetAngleViolationKeys(map, nodes);
                    HashSet<string> overlongBefore = GetOverlongEdgeKeys(map, nodes, interval * 1.7f);
                    HashSet<string> tooCloseBefore = GetTooClosePairKeys(nodes, _settings.NoCloserThan);
                    if (!freezeFrom)
                        from.Position += direction * moveDistance;
                    if (!freezeTo)
                        to.Position -= direction * moveDistance;

                    HashSet<string> crossingAfter = GetCrossingViolationKeys(map, nodes);
                    HashSet<string> angleAfter = GetAngleViolationKeys(map, nodes);
                    HashSet<string> overlongAfter = GetOverlongEdgeKeys(map, nodes, interval * 1.7f);
                    HashSet<string> tooCloseAfter = GetTooClosePairKeys(nodes, _settings.NoCloserThan);
                    if (crossingAfter.Except(crossingBefore).Any() || angleAfter.Except(angleBefore).Any()
                        || overlongAfter.Except(overlongBefore).Any()
                        || tooCloseAfter.Except(tooCloseBefore).Any())
                    {
                        from.Position = oldFrom;
                        to.Position = oldTo;
                        break;
                    }
                }
            }
        }

        private HashSet<string> GetAngleViolationKeys(RouteMapData map,
            IReadOnlyDictionary<int, RouteNodeData> nodes)
        {
            HashSet<string> result = new HashSet<string>();
            foreach (RouteNodeData node in nodes.Values)
            {
                List<int> incoming = Enumerable.Range(0, map.Edges.Count)
                    .Where(index => map.Edges[index].ToNodeId == node.Id).ToList();
                AddAngleViolations(result, "I", node, incoming, map.Edges, nodes);
                List<int> outgoing = Enumerable.Range(0, map.Edges.Count)
                    .Where(index => map.Edges[index].FromNodeId == node.Id).ToList();
                AddAngleViolations(result, "O", node, outgoing, map.Edges, nodes);
            }
            return result;
        }

        private static HashSet<string> GetOverlongEdgeKeys(RouteMapData map,
            IReadOnlyDictionary<int, RouteNodeData> nodes, float maximumDistance)
        {
            HashSet<string> result = new HashSet<string>();
            for (int index = 0; index < map.Edges.Count; index++)
            {
                RouteEdgeData edge = map.Edges[index];
                if (Vector2.Distance(nodes[edge.FromNodeId].Position, nodes[edge.ToNodeId].Position) > maximumDistance)
                    result.Add(index.ToString());
            }
            return result;
        }

        private static HashSet<string> GetTooClosePairKeys(
            IReadOnlyDictionary<int, RouteNodeData> nodes, float minimumDistance)
        {
            HashSet<string> result = new HashSet<string>();
            List<RouteNodeData> ordered = nodes.Values.OrderBy(node => node.Id).ToList();
            for (int first = 0; first < ordered.Count; first++)
            for (int second = first + 1; second < ordered.Count; second++)
            {
                if (Vector2.Distance(ordered[first].Position, ordered[second].Position) >= minimumDistance)
                    continue;
                result.Add(ordered[first].Id + ":" + ordered[second].Id);
            }
            return result;
        }

        private void AddAngleViolations(ICollection<string> result, string directionGroup, RouteNodeData node,
            IReadOnlyList<int> edgeIndices, IReadOnlyList<RouteEdgeData> edges,
            IReadOnlyDictionary<int, RouteNodeData> nodes)
        {
            for (int first = 0; first < edgeIndices.Count; first++)
            for (int second = first + 1; second < edgeIndices.Count; second++)
            {
                int firstIndex = edgeIndices[first];
                int secondIndex = edgeIndices[second];
                RouteEdgeData firstEdge = edges[firstIndex];
                RouteEdgeData secondEdge = edges[secondIndex];
                int firstOtherId = directionGroup == "I" ? firstEdge.FromNodeId : firstEdge.ToNodeId;
                int secondOtherId = directionGroup == "I" ? secondEdge.FromNodeId : secondEdge.ToNodeId;
                Vector2 firstDirection = nodes[firstOtherId].Position - node.Position;
                Vector2 secondDirection = nodes[secondOtherId].Position - node.Position;
                if (Vector2.Angle(firstDirection, secondDirection) < _settings.MinimumSameLayerEdgeAngle)
                    result.Add(node.Id + directionGroup + firstIndex + ":" + secondIndex);
            }
        }

        public static bool AllNodesHaveSingleOutgoingEdge(RouteStageData stage, IEnumerable<RouteEdgeData> edges)
        {
            return stage.Nodes.All(node => edges.Count(edge => edge.FromNodeId == node.Id) == 1);
        }

        public static bool ExceedsMaximumSlotDifference(RouteNodeData source, RouteNodeData target)
        {
            return Mathf.Abs(source.SlotIndex - target.SlotIndex) > 2;
        }

        public static bool EdgesCross(RouteEdgeData first, RouteEdgeData second,
            IReadOnlyDictionary<int, RouteNodeData> nodes)
        {
            RouteNodeData firstFrom = nodes[first.FromNodeId];
            RouteNodeData firstTo = nodes[first.ToNodeId];
            RouteNodeData secondFrom = nodes[second.FromNodeId];
            RouteNodeData secondTo = nodes[second.ToNodeId];
            if (first.FromNodeId == second.FromNodeId || first.ToNodeId == second.ToNodeId)
                return false;
            if (first.FromNodeId == second.ToNodeId || first.ToNodeId == second.FromNodeId)
                return false;
            return SegmentsIntersect(firstFrom.Position, firstTo.Position, secondFrom.Position, secondTo.Position);
        }

        private static bool WouldCrossAny(RouteEdgeData candidate, IEnumerable<RouteEdgeData> edges,
            IReadOnlyDictionary<int, RouteNodeData> nodes)
        {
            return edges.Any(existing => EdgesCross(candidate, existing, nodes));
        }

        private static HashSet<string> GetCrossingViolationKeys(RouteMapData map,
            IReadOnlyDictionary<int, RouteNodeData> nodes)
        {
            HashSet<string> result = new HashSet<string>();
            for (int first = 0; first < map.Edges.Count; first++)
            for (int second = first + 1; second < map.Edges.Count; second++)
                if (EdgesCross(map.Edges[first], map.Edges[second], nodes))
                    result.Add(first + ":" + second);
            return result;
        }

        private static bool SegmentsIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float abC = Cross(b - a, c - a);
            float abD = Cross(b - a, d - a);
            float cdA = Cross(d - c, a - c);
            float cdB = Cross(d - c, b - c);
            const float epsilon = 0.0001f;

            if (((abC > epsilon && abD < -epsilon) || (abC < -epsilon && abD > epsilon))
                && ((cdA > epsilon && cdB < -epsilon) || (cdA < -epsilon && cdB > epsilon)))
                return true;
            if (Mathf.Abs(abC) <= epsilon && IsOnSegment(a, b, c, epsilon)) return true;
            if (Mathf.Abs(abD) <= epsilon && IsOnSegment(a, b, d, epsilon)) return true;
            if (Mathf.Abs(cdA) <= epsilon && IsOnSegment(c, d, a, epsilon)) return true;
            return Mathf.Abs(cdB) <= epsilon && IsOnSegment(c, d, b, epsilon);
        }

        private static float Cross(Vector2 left, Vector2 right)
        {
            return left.x * right.y - left.y * right.x;
        }

        private static bool IsOnSegment(Vector2 start, Vector2 end, Vector2 point, float epsilon)
        {
            return point.x >= Mathf.Min(start.x, end.x) - epsilon
                   && point.x <= Mathf.Max(start.x, end.x) + epsilon
                   && point.y >= Mathf.Min(start.y, end.y) - epsilon
                   && point.y <= Mathf.Max(start.y, end.y) + epsilon;
        }

        private static RouteNodeData FindNearest(RouteNodeData source, IEnumerable<RouteNodeData> candidates)
        {
            return candidates.OrderBy(node => (node.Position - source.Position).sqrMagnitude).First();
        }

        private static void AddEdgeIfMissing(RouteMapData map, int from, int to)
        {
            if (!map.Edges.Any(edge => edge.FromNodeId == from && edge.ToNodeId == to))
                map.Edges.Add(new RouteEdgeData(from, to));
        }

        private void Shuffle<T>(IList<T> items)
        {
            for (int index = items.Count - 1; index > 0; index--)
            {
                int other = _random.NextInt(0, index + 1);
                T value = items[index];
                items[index] = items[other];
                items[other] = value;
            }
        }

        public static bool Validate(RouteMapData map, out string error)
        {
            Dictionary<int, RouteNodeData> nodes = AllNodes(map);
            HashSet<string> uniqueEdges = new HashSet<string>();
            foreach (RouteEdgeData edge in map.Edges)
            {
                string key = edge.FromNodeId + ">" + edge.ToNodeId;
                if (!uniqueEdges.Add(key))
                {
                    error = "Duplicate edge " + key + ".";
                    return false;
                }
                if (nodes[edge.ToNodeId].StageIndex != nodes[edge.FromNodeId].StageIndex + 1)
                {
                    error = "An edge does not connect adjacent stages: " + key + ".";
                    return false;
                }
            }

            for (int first = 0; first < map.Edges.Count; first++)
            for (int second = first + 1; second < map.Edges.Count; second++)
            {
                if (!EdgesCross(map.Edges[first], map.Edges[second], nodes))
                    continue;
                error = "Crossing edges were generated.";
                return false;
            }

            foreach (RouteNodeData node in nodes.Values)
            {
                if (node.StageIndex > 0 && !map.Edges.Any(edge => edge.ToNodeId == node.Id))
                {
                    error = "Node " + node.Id + " has no incoming edge.";
                    return false;
                }
                if (node.StageIndex < map.Stages.Count - 1 && !map.Edges.Any(edge => edge.FromNodeId == node.Id))
                {
                    error = "Node " + node.Id + " has no outgoing edge.";
                    return false;
                }
            }

            HashSet<int> reachableFromStart = Traverse(map.Stages[0].Nodes.Select(node => node.Id), map.Edges, true);
            if (reachableFromStart.Count != nodes.Count)
            {
                error = "Not every node is reachable from the first stage.";
                return false;
            }
            HashSet<int> canReachEnd = Traverse(map.Stages[map.Stages.Count - 1].Nodes.Select(node => node.Id), map.Edges, false);
            if (canReachEnd.Count != nodes.Count)
            {
                error = "Not every node can reach the final stage.";
                return false;
            }

            if (TryFindLongTypeRun(map, out _))
            {
                error = "A route contains four or more consecutive nodes of the same type.";
                return false;
            }
            if (map.Edges.Any(edge => nodes[edge.FromNodeId].Type == RouteNodeType.Elite
                                      && nodes[edge.ToNodeId].Type == RouteNodeType.Elite))
            {
                error = "A route contains adjacent elite nodes.";
                return false;
            }

            error = null;
            return true;
        }

        private bool ValidateGeometry(RouteMapData map, out string error)
        {
            RouteNodeData firstEndpoint = map.Stages[0].Nodes.Single();
            RouteNodeData lastEndpoint = map.Stages[map.Stages.Count - 1].Nodes.Single();
            if (!Mathf.Approximately(firstEndpoint.Position.y, _settings.MidlineHeight)
                || !Mathf.Approximately(lastEndpoint.Position.y, _settings.MidlineHeight))
            {
                error = "The first and final stage nodes must remain fixed on the midline.";
                return false;
            }

            List<RouteNodeData> nodes = map.Stages.SelectMany(stage => stage.Nodes).ToList();
            for (int first = 0; first < nodes.Count; first++)
            for (int second = first + 1; second < nodes.Count; second++)
            {
                if (Vector2.Distance(nodes[first].Position, nodes[second].Position) + 0.0001f >= _settings.NoCloserThan)
                    continue;
                error = "Two randomized node coordinates are closer than NoCloserThan.";
                return false;
            }

            Dictionary<int, RouteNodeData> nodeLookup = nodes.ToDictionary(node => node.Id);
            if (GetAngleViolationKeys(map, nodeLookup).Count > 0)
            {
                error = "Two same-side edges form an angle smaller than MinimumSameLayerEdgeAngle.";
                return false;
            }
            error = null;
            return true;
        }

        private static HashSet<int> Traverse(IEnumerable<int> starts, IReadOnlyCollection<RouteEdgeData> edges, bool forwards)
        {
            HashSet<int> visited = new HashSet<int>(starts);
            Queue<int> queue = new Queue<int>(visited);
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                IEnumerable<int> next = forwards
                    ? edges.Where(edge => edge.FromNodeId == current).Select(edge => edge.ToNodeId)
                    : edges.Where(edge => edge.ToNodeId == current).Select(edge => edge.FromNodeId);
                foreach (int nodeId in next)
                    if (visited.Add(nodeId))
                        queue.Enqueue(nodeId);
            }
            return visited;
        }

        private static Dictionary<int, RouteNodeData> AllNodes(RouteMapData map)
        {
            return map.Stages.SelectMany(stage => stage.Nodes).ToDictionary(node => node.Id);
        }
    }
}
