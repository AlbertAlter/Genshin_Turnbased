using System.Globalization;
using System.Collections.Generic;
using System.Text;

namespace Roguelike.Tests.RouteGeneration
{
    /// <summary>
    /// Records the values already drawn by route generation. Logging never performs an extra draw.
    /// A complete generation is emitted as one Console message so related values stay together.
    /// </summary>
    public sealed class RouteRandomLog
    {
        private readonly StringBuilder _builder = new StringBuilder(4096);

        public void Begin(string randomSource)
        {
            _builder.AppendLine("[RouteRandomLog] source=" + randomSource);
        }

        public void BeginAttempt(int attempt)
        {
            _builder.AppendLine("-- attempt " + attempt + " --");
        }

        public void RecordNodeCount(string stageId, int minInclusive, int maxInclusive, int result)
        {
            _builder.Append("COUNT ").Append(stageId)
                .Append(" range=[").Append(minInclusive).Append(',').Append(maxInclusive).Append("] => ")
                .Append(result).AppendLine();
        }

        public void RecordSlots(string stageId, IEnumerable<int> slots)
        {
            _builder.Append("SLOTS ").Append(stageId).Append(" => [")
                .Append(string.Join(",", slots)).AppendLine("]");
        }

        public void RecordNodeType(string stageId, int slotIndex, int roll, RouteStageConfig config,
            RouteNodeType result)
        {
            _builder.Append("TYPE  ").Append(stageId).Append(" S").Append(slotIndex)
                .Append(" roll=").Append(roll).Append('/').Append(config.TotalWeight)
                .Append(" weights(B=").Append(config.BattleWeight)
                .Append(",Event=").Append(config.EventWeight)
                .Append(",Elite=").Append(config.EliteWeight)
                .Append(",Boss=").Append(config.BossWeight)
                .Append(") => ").Append(result).AppendLine();
        }

        public void RecordExtraEdges(string stageId, int sourceNodeCount, float roll, int desiredCount)
        {
            _builder.Append("EXTRA ").Append(stageId)
                .Append(" nodes=").Append(sourceNodeCount)
                .Append(" roll=").Append(roll.ToString("0.000000", CultureInfo.InvariantCulture))
                .Append(" => request ").Append(desiredCount).AppendLine();
        }

        public void RecordTypeReroll(string reason, string stageId, int slotIndex, RouteNodeType originalType,
            RouteNodeType excludedType, int roll, int totalWeight, RouteNodeType result)
        {
            _builder.Append("REROLL ").Append(stageId).Append(" S").Append(slotIndex)
                .Append(" reason=").Append(reason)
                .Append(" old=").Append(originalType)
                .Append(" excluded=").Append(excludedType)
                .Append(" roll=").Append(roll).Append('/').Append(totalWeight)
                .Append(" => ").Append(result).AppendLine();
        }

        public void RecordRejectedAttempt(string reason)
        {
            _builder.AppendLine("REJECT " + reason);
        }

        public string Complete(RouteGenerationResult result)
        {
            if (result.Success)
            {
                _builder.Append("SUCCESS attempt=").Append(result.Map.AttemptNumber)
                    .Append(" nodes=").Append(GetNodeCount(result.Map))
                    .Append(" edges=").Append(result.Map.Edges.Count).AppendLine();
            }
            else
            {
                _builder.AppendLine("FAILED " + result.Error);
            }
            return _builder.ToString();
        }

        private static int GetNodeCount(RouteMapData map)
        {
            int count = 0;
            foreach (RouteStageData stage in map.Stages)
                count += stage.Nodes.Count;
            return count;
        }
    }
}
