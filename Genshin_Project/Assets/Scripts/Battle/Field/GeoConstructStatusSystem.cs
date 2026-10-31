using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

/// <summary>共鸣出伤位置与钟离伤害来源分开记录；不是新的战技施放或月笼协奏。</summary>
public sealed class GeoResonanceEmission
{
    public StatusInstance Origin { get; }
    public StatusInstance Emitter { get; }
    public FieldPosition Position { get; }
    public bool IsTriggered { get; }
    public bool IsStoneStele => Emitter != null && Origin != null
        && Emitter.StatusID2 == Origin.StatusID2 && Emitter.Caster == Origin.Caster;
    internal GeoResonanceEmission(StatusInstance origin, StatusInstance emitter, FieldPosition position, bool triggered)
    { Origin = origin; Emitter = emitter; Position = position; IsTriggered = triggered; }
}

/// <summary>
/// GeoConstruct沿用普通状态生命周期。月笼保留原身份，额外参加此通用传导。
/// 接收共鸣的次数由每个具体实例的代码账本控制，不要求各角色/敌人表填写OnTrigger或钟离效果ID。
/// 调用的始终是正在发起共鸣的钟离原Damage效果，仅替换出伤位置；不施放一遍战技、不扣AP。
/// </summary>
public static class GeoConstructStatusSystem
{
    public const string StatusType = "GeoConstruct";
    private sealed class TriggerMemory { public BattleField Field; public int Turn = -1; }
    private static readonly ConditionalWeakTable<StatusInstance, TriggerMemory> Triggers
        = new ConditionalWeakTable<StatusInstance, TriggerMemory>();
    private static readonly AsyncLocal<GeoResonanceEmission> Emission = new AsyncLocal<GeoResonanceEmission>();
    public static GeoResonanceEmission Current => Emission.Value;

    public static bool IsConstruct(StatusInstance status) => status != null
        && (string.Equals(status.MainData?.StatusType?.Trim(), StatusType, StringComparison.OrdinalIgnoreCase)
            || status.StatusID2 == TeamPublicStatuses.MoonCage);

    public static FieldPosition GetPosition(StatusInstance status)
    {
        if (!IsConstruct(status) || !status.IsActive) return null;
        if (status.Host is FieldPosition field && field.StatusList.Contains(status)) return field;
        if (status.Host is BattleEntity entity && entity.IsAlive && entity.HasStatusInstance(status))
            return entity.Position;
        return null;
    }

    /// <summary>同一实例每回合只接受一次传导。岩脊OnItsTurn不调用此入口，刷新/移除仍走普通生命周期。</summary>
    public static bool TryReceiveResonance(StatusInstance status, BattleField field, int turn)
    {
        var position = GetPosition(status);
        if (field == null || position?.OwnerField != field) return false;
        var memory = Triggers.GetValue(status, _ => new TriggerMemory());
        if (ReferenceEquals(memory.Field, field) && memory.Turn == turn) return false;
        memory.Field = field; memory.Turn = turn;
        return true; // 在执行伤害之前占用额度，防止嵌套回应重复触发。
    }

    private static List<FieldPosition> Coverage(FieldPosition center)
    {
        var positions = new List<FieldPosition>();
        if (center?.OwnerField == null) return positions;
        for (int slot = center.SlotIndex - 1; slot <= center.SlotIndex + 1; slot++)
        {
            var position = center.OwnerField.GetSlot(center.Side, slot);
            if (position != null) positions.Add(position);
        }
        return positions;
    }

    private static IEnumerable<StatusInstance> Nodes(IEnumerable<FieldPosition> positions)
    {
        var nodes = new List<StatusInstance>();
        foreach (var position in positions)
        {
            nodes.AddRange(position.StatusList.Where(IsConstruct));
            if (position.Occupant?.IsAlive == true)
                nodes.AddRange(position.Occupant.StatusDict.Values.Where(IsConstruct));
        }
        return nodes.Distinct().OrderBy(status => GetPosition(status)?.SlotIndex ?? int.MaxValue)
            .ThenBy(status => status.ApplyOrder).ToList();
    }

    /// <summary>
    /// 只从实际执行的钟离共鸣效果进入；没有钟离不查钟离的表，不构造虚假效果、不报引用缺失。
    /// executeDamage复用原控制器、原效果和原施加者，参数只提供本节点实际命中的存活目标。
    /// 空位置照常作为传导节点，但不会伪造命中、回能或暴击。
    /// </summary>
    public static async Task<bool> TryExecuteAsync(StatusInstance origin, BattleEntity source,
        Func<List<BattleEntity>, Task> executeDamage)
    {
        var battle = BattleManager.Instance;
        var start = GetPosition(origin);
        if (source?.EntityID != 1032 || !source.IsAlive || origin?.Caster != source || start == null
            || battle?.Field != start.OwnerField || !battle.Allies.Any(ally => ally != null && ally.Entity == source))
            return false;
        if (executeDamage == null) throw new ArgumentNullException(nameof(executeDamage));
        var pending = new Queue<GeoResonanceEmission>();
        pending.Enqueue(new GeoResonanceEmission(origin, origin, start, false));
        int turn = battle.TurnCount;
        while (pending.Count > 0)
        {
            if (!source.IsAlive || GetPosition(origin) == null || battle.IsBattleOver) break;
            var emission = pending.Dequeue();
            if (GetPosition(emission.Emitter) != emission.Position) continue;
            var coverage = Coverage(emission.Position);
            // 在该节点执行前确定覆盖到的实例；新生成的造物不会追溯收到此前共鸣。
            var receivers = Nodes(coverage).Where(status => status != emission.Emitter).ToList();
            var previous = Emission.Value;
            try
            {
                Emission.Value = emission;
                var targets = coverage.Where(position => position.Occupant?.IsOnField == true)
                    .Select(position => position.Occupant).ToList();
                if (targets.Count > 0) await executeDamage(targets);
            }
            finally { Emission.Value = previous; }
            if (!source.IsAlive || GetPosition(origin) == null || battle.IsBattleOver) break;
            foreach (var receiver in receivers)
            {
                if (!coverage.Contains(GetPosition(receiver))) continue;
                bool received = receiver.CountRuntimeState is MoonCageInstance cage
                    ? cage.TryReceiveResonance(start.OwnerField, turn)
                    : TryReceiveResonance(receiver, start.OwnerField, turn);
                if (received) pending.Enqueue(new GeoResonanceEmission(origin, receiver, GetPosition(receiver), true));
            }
        }
        return true;
    }
}
