using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace Roguelike.Tests.RouteGeneration
{
    public enum RouteNodeType
    {
        Battle,
        Event,
        Elite,
        Boss
    }

    [Serializable]
    public sealed class RouteStageConfig
    {
        public string StageId;
        public int MinEncounter;
        public int MaxEncounter;
        public int BattleWeight;
        public int EventWeight;
        public int EliteWeight;
        public int BossWeight;

        public int TotalWeight => BattleWeight + EventWeight + EliteWeight + BossWeight;
    }

    [Serializable]
    public sealed class RouteUiRuleConfig
    {
        public float CoordinateDistance;
        public float RandomizeRadius;
        public float NoCloserThan;
        public float StageWidth;
        public float MidlineHeight;
        public float AdditionalRouteTwoChance;
        public float AdditionalRouteOneChance;
        public float AdditionalRouteOneChanceWhenOnlyTwo;
    }

    [Serializable]
    public sealed class RouteNodeData
    {
        public int Id;
        public int StageIndex;
        public int SlotIndex;
        public RouteNodeType Type;
        public Vector2 StandardPosition;
        public Vector2 Position;
    }

    [Serializable]
    public sealed class RouteEdgeData
    {
        public int FromNodeId;
        public int ToNodeId;

        public RouteEdgeData(int fromNodeId, int toNodeId)
        {
            FromNodeId = fromNodeId;
            ToNodeId = toNodeId;
        }
    }

    [Serializable]
    public sealed class RouteStageData
    {
        public string StageId;
        public int StageIndex;
        public readonly List<RouteNodeData> Nodes = new List<RouteNodeData>();
    }

    public sealed class RouteMapData
    {
        public readonly List<RouteStageData> Stages = new List<RouteStageData>();
        public readonly List<RouteEdgeData> Edges = new List<RouteEdgeData>();
        public float ContentWidth;
        public int AttemptNumber;
    }

    [Serializable]
    public sealed class RouteGenerationSettings
    {
        [Min(1f)] public float StageStartX = 180f;
        [FormerlySerializedAs("StageSpacingX"), Min(1f)] public float StageWidth = 225f;
        [Min(1f)] public float RouteHeight = 1080f;
        [FormerlySerializedAs("SlotSpacingY"), Min(1f)] public float CoordinateDistance = 140f;
        [Min(0f)] public float RandomizeRadius = 60f;
        [FormerlySerializedAs("MinimumNodeDistance"), Min(0f)] public float NoCloserThan = 25f;
        [Min(0f)] public float MidlineHeight = 510f;
        [Min(1f)] public float NodeWidth = 30f;
        [Min(1f)] public float NodeHeight = 30f;
        [Min(1)] public int PositionSampleAttempts = 12;
        [Min(1)] public int MaximumGenerationAttempts = 64;
        [Range(0f, 1f)] public float AdditionalRouteTwoChance = 0.10f;
        [Range(0f, 1f)] public float AdditionalRouteOneChance = 0.30f;
        [Range(0f, 1f)] public float AdditionalRouteOneChanceWhenOnlyTwo = 0.25f;
        [Min(0f)] public float LongEdgeThresholdIntervals = 2f;
        [Min(0f)] public float LongEdgeTargetIntervals = 1.5f;
        [Min(0.001f)] public float LongEdgeMoveStepIntervals = 0.05f;
        [Range(0f, 180f)] public float MinimumSameLayerEdgeAngle = 15f;

        public float StandardCoordinateInterval => CoordinateDistance;

        public void Apply(RouteUiRuleConfig rules)
        {
            if (rules == null)
                throw new ArgumentNullException(nameof(rules));
            CoordinateDistance = rules.CoordinateDistance;
            RandomizeRadius = rules.RandomizeRadius;
            NoCloserThan = rules.NoCloserThan;
            StageWidth = rules.StageWidth;
            MidlineHeight = rules.MidlineHeight;
            AdditionalRouteTwoChance = rules.AdditionalRouteTwoChance;
            AdditionalRouteOneChance = rules.AdditionalRouteOneChance;
            AdditionalRouteOneChanceWhenOnlyTwo = rules.AdditionalRouteOneChanceWhenOnlyTwo;
        }

        public void Validate()
        {
            if (PositionSampleAttempts < 1 || MaximumGenerationAttempts < 1)
                throw new InvalidOperationException("Route generation attempt counts must be positive.");
            if (StageStartX <= 0f || StageWidth <= 0f || RouteHeight <= 0f || CoordinateDistance <= 0f
                || NodeWidth <= 0f || NodeHeight <= 0f || RandomizeRadius < 0f || NoCloserThan < 0f
                || MidlineHeight < 0f)
                throw new InvalidOperationException("Route geometry values are invalid.");
            float lowest = MidlineHeight - 2.5f * CoordinateDistance - RandomizeRadius;
            float highest = MidlineHeight + 2.5f * CoordinateDistance + RandomizeRadius;
            if (lowest < 0f || highest > RouteHeight)
                throw new InvalidOperationException("The six standard-coordinate random regions extend beyond the route area.");
            if (AdditionalRouteTwoChance + AdditionalRouteOneChance > 1f)
                throw new InvalidOperationException("Additional route probabilities exceed 100%.");
            if (AdditionalRouteTwoChance < 0f || AdditionalRouteOneChance < 0f
                || AdditionalRouteOneChanceWhenOnlyTwo < 0f || AdditionalRouteTwoChance > 1f
                || AdditionalRouteOneChance > 1f || AdditionalRouteOneChanceWhenOnlyTwo > 1f)
                throw new InvalidOperationException("Extra-edge probabilities must be between 0 and 1.");
            if (LongEdgeThresholdIntervals <= 0f || LongEdgeTargetIntervals <= 0f
                || LongEdgeTargetIntervals >= LongEdgeThresholdIntervals || LongEdgeMoveStepIntervals <= 0f)
                throw new InvalidOperationException("Long-edge adjustment intervals are invalid.");
            if (MinimumSameLayerEdgeAngle < 0f || MinimumSameLayerEdgeAngle > 180f)
                throw new InvalidOperationException("The minimum connection angle must be between 0 and 180 degrees.");
        }
    }

    public sealed class RouteGenerationResult
    {
        public RouteMapData Map { get; }
        public string Error { get; }
        public bool Success => Map != null;

        private RouteGenerationResult(RouteMapData map, string error)
        {
            Map = map;
            Error = error;
        }

        public static RouteGenerationResult Succeeded(RouteMapData map) => new RouteGenerationResult(map, null);
        public static RouteGenerationResult Failed(string error) => new RouteGenerationResult(null, error);
    }
}
