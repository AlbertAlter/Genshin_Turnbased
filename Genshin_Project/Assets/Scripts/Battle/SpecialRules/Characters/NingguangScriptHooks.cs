using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

/// <summary>
/// 凝光专用索引。数值、倍率、目标与效果顺序在配表；不新建伤害或响应队列。
/// 普攻整次只获一枚星璇；屏共享六次完整敌方行动；蓄力只打起手选定位置的原目标。
/// </summary>
public static class NingguangScriptHooks
{
    public const string ChargeID = "ST_Ningguang_Charge";
    public const string ChargeTargetID = "ST_Ningguang_ChargeTarget";
    public const string ScreenFieldID = "ST_Ningguang_ScreenField";
    public const string C2ID = "ST_Ningguang_C2";
    private static readonly AsyncLocal<SkillFrame> Skill = new AsyncLocal<SkillFrame>();
    private static readonly AsyncLocal<ChargeFrame> Charge = new AsyncLocal<ChargeFrame>();
    private static readonly ConditionalWeakTable<StatusInstance, IDisposable> C2Listeners
        = new ConditionalWeakTable<StatusInstance, IDisposable>();

    internal static void Register()
    {
        BattleSpecialRules.RegisterSkillExecutionScope((owner, skill) => new SkillFrame(owner, skill));
        BattleSpecialRules.RegisterStatusActionScope(status => new ChargeFrame(status));
        BattleSpecialRules.RegisterStatusLifecycle(ChargeTargetID, CaptureField, null);
        BattleSpecialRules.RegisterStatusLifecycle(ScreenFieldID, CaptureField, null);
        BattleSpecialRules.RegisterStatusLifecycle(ChargeID, (status, host) => AdoptFields(status, ChargeTargetID), null);
        BattleSpecialRules.RegisterStatusLifecycle(C2ID, StartC2, StopC2);
        BattleSpecialRules.RegisterCondition("Ningguang_NormalHit", (arg, c) => Empty(arg)
            && c?.Caster?.EntityID == 1017 && Skill.Value?.Owner == c.Caster
            && Skill.Value.SkillID == "SK_Normal_Ningguang" && Skill.Value.NormalHit);
        BattleSpecialRules.RegisterCondition("Ningguang_StarJadeAP", (arg, c) => Empty(arg)
            && c?.Caster?.EntityID == 1017 && c.Caster.GetStatus(NingguangStarJadeCount.ID)?.RuntimeIntValue > 0);
        BattleSpecialRules.RegisterCondition("Ningguang_SkillEnergy", (arg, c) => Empty(arg)
            && c?.Caster?.EntityID == 1017 && Skill.Value?.Owner == c.Caster
            && Skill.Value.SkillID == "SK_Skill_Ningguang" && Skill.Value.CanGainEnergy);
        BattleSpecialRules.RegisterCondition("Ningguang_ScreenFieldActive", (arg, c) => Empty(arg)
            && IsBoundScreenField(c?.Status));
        BattleSpecialRules.RegisterCondition("Ningguang_ScreenActive", (arg, c) => Empty(arg)
            && c?.Caster?.IsAlive == true && c.Caster.GetStatus(NingguangScreenCount.ID)?.RuntimeIntValue > 0);
        BattleSpecialRules.RegisterCondition("Ningguang_ScreenBroken", (arg, c) => Empty(arg)
            && c?.Status?.StatusID2 == C2ID && IsBroken(c.Caster, c.Declaration?.Get<ResourceEventData>()));
        BattleSpecialRules.RegisterCount("Ningguang_BurstCount", owner => owner?.EntityID == 1017
            && Skill.Value?.Owner == owner && Skill.Value.SkillID == "SK_Burst_Ningguang" ? Skill.Value.BurstCount : 0);
        BattleSpecialRules.RegisterCount("Ningguang_FiredJades", owner => owner?.EntityID == 1017
            && Charge.Value?.Status?.Caster == owner ? Charge.Value.Jades : 0);
        BattleSpecialRules.RegisterTargets("Ningguang_ChargeTarget()", (battle, owner) =>
        {
            var frame = Charge.Value;
            return frame?.Status?.Caster == owner && frame.Target?.SlotPosition == frame.TargetSlot && Valid(battle, owner, frame.Target)
                ? new List<int> { frame.Target.SlotPosition } : new List<int>();
        });
    }

    private static bool Empty(string argument) => string.IsNullOrEmpty(argument);
    private static bool Valid(BattleManager battle, BattleEntity owner, BattleEntity target)
        => battle != null && owner?.IsAlive == true && target?.IsAlive == true && target.Side != owner.Side
            && battle.GetEntityByPosition(target.Side, target.SlotPosition) == target;

    private sealed class SkillFrame : IDisposable
    {
        internal readonly BattleEntity Owner;
        internal readonly string SkillID;
        internal readonly bool CanGainEnergy;
        internal readonly int BurstCount;
        internal bool NormalHit;
        internal readonly List<StatusInstance> Fields = new List<StatusInstance>();
        private readonly SkillFrame previous = Skill.Value;
        private readonly IDisposable listener;
        internal SkillFrame(BattleEntity owner, SkillMainData skill)
        {
            Owner = owner; SkillID = skill?.SkillID2; Skill.Value = this;
            CanGainEnergy = owner?.EntityID == 1017 && !owner.HasStatus("ST_Ningguang_EnergyLock");
            BurstCount = owner?.GetStatus(NingguangScreenCount.ID)?.RuntimeIntValue > 0 ? 12 : 6;
            if (owner?.EntityID == 1017)
                listener = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (_, hit) =>
                {
                    var source = hit.Source;
                    if (SkillID == "SK_Normal_Ningguang" && hit.HitLanded && source.CountsAsHit
                        && hit.Subject != null && hit.Subject.Side != owner.Side && source.SourceEntity == owner
                        && source.SourceKind == ReactionSourceKind.CharacterSkill && source.SourceSkillID == SkillID
                        && source.SourceEffectID == "SE_Normal_Ningguang") NormalHit = true;
                }, 0, () => ReferenceEquals(Skill.Value, this));
        }
        public void Dispose()
        {
            try
            {
                listener?.Dispose();
                // 异常、中途死亡或取消时，尚未交给父状态的具体场地实例不能残留。
                foreach (var field in Fields) if (field.IsActive) StatusHostAccess.Remove(field.Host, field, "UnboundSkillField");
            }
            finally { Skill.Value = previous; }
        }
    }

    private static void CaptureField(StatusInstance status, object host)
    {
        var frame = Skill.Value;
        if (frame == null || frame.Owner != status.Caster || frame.Owner?.EntityID != 1017 || !(host is FieldPosition)) return;
        if ((status.StatusID2 == ChargeTargetID && frame.SkillID == "SK_Heavy_Ningguang")
            || (status.StatusID2 == ScreenFieldID && frame.SkillID == "SK_Skill_Ningguang")) frame.Fields.Add(status);
    }

    internal static void AdoptFields(StatusInstance parent, string fieldID)
    {
        var frame = Skill.Value;
        if (frame == null || frame.Owner != parent.Caster) return;
        for (int i = frame.Fields.Count - 1; i >= 0; i--)
        {
            var field = frame.Fields[i];
            if (field.StatusID2 != fieldID || !field.IsActive) continue;
            parent.BoundStatuses.Add(field); frame.Fields.RemoveAt(i);
        }
    }

    private sealed class ChargeFrame : IDisposable
    {
        internal readonly StatusInstance Status;
        internal readonly BattleEntity Target;
        internal readonly int TargetSlot;
        internal readonly int Jades;
        private readonly ChargeFrame previous = Charge.Value;
        internal ChargeFrame(StatusInstance status)
        {
            Status = status; Charge.Value = this;
            if (status?.StatusID2 != ChargeID || status.RemainingPhaseCount > 0 || !status.Caster.IsAlive) return;
            var battle = BattleManager.Instance;
            foreach (var child in status.BoundStatuses)
                if (child.IsActive && child.StatusID2 == ChargeTargetID && child.Host is FieldPosition field)
                {
                    TargetSlot = field.SlotIndex;
                    Target = battle?.GetEntityByPosition(field.Side, TargetSlot);
                }
            if (!Valid(battle, status.Caster, Target)) return;
            var stars = status.Caster.GetStatus(NingguangStarJadeCount.ID);
            Jades = stars?.RuntimeIntValue ?? 0;
            if (Jades > 0) NingguangStarJadeCount.Set(stars, status.Caster, 0, ResourceChangeReason.Spend);
            // 此时保存本次发射数。主体致死也已发射，不吞掉后续新获得的星璇；空位置不消费。
        }
        public void Dispose() => Charge.Value = previous;
    }

    internal static bool Covers(StatusInstance screen, BattleEntity attacker)
    {
        if (screen?.IsActive != true || screen.Caster?.IsAlive != true || screen.RuntimeIntValue <= 0
            || attacker == null || attacker.Side == screen.Caster.Side) return false;
        foreach (var child in screen.BoundStatuses)
            if (child.IsActive && child.StatusID2 == ScreenFieldID && child.Host is FieldPosition field
                && field.Side == attacker.Side && field.SlotIndex == attacker.SlotPosition
                && StatusHostAccess.Contains(field, child)) return true;
        return false;
    }

    private static bool IsBoundScreenField(StatusInstance fieldStatus)
    {
        if (fieldStatus?.StatusID2 != ScreenFieldID || !fieldStatus.IsActive
            || fieldStatus.Caster?.IsAlive != true || !(fieldStatus.Host is FieldPosition field)
            || !StatusHostAccess.Contains(field, fieldStatus)) return false;
        var parent = fieldStatus.Caster.GetStatus(NingguangScreenCount.ID);
        return parent?.IsActive == true && parent.RuntimeIntValue > 0 && parent.BoundStatuses.Contains(fieldStatus);
    }

    private static bool IsBroken(BattleEntity owner, ResourceEventData fact)
        => owner?.EntityID == 1017 && owner.IsAlive && owner.ConstellationLevel >= 2
            && fact?.Subject == owner && fact.Resource == "Count" && fact.PoolId == NingguangScreenCount.ID
            && fact.Reason == ResourceChangeReason.Spend && fact.Before > 0 && fact.After == 0;

    private static void StartC2(StatusInstance status, object host)
    {
        var owner = NingguangStarJadeCount.RequireOwner(status, host);
        C2Listeners.Add(status, BattleEventHub.Subscribe<ResourceEventData>(BattleEventKind.ResourceChanged, (_, fact) =>
        {
            if (!IsBroken(owner, fact) || owner.CharacterCtrl == null) return;
            var data = DataManager.Instance;
            if (data == null || !data.StatusActionDict.TryGetValue(C2ID, out var actions)) return;
            foreach (var action in actions)
                if (action.ActionType == "OnTrigger" && action.ScriptHook == "Ningguang_ScreenBroken()")
                    owner.CharacterCtrl.TryExecuteEventStatusAction(status, action, host, new ScriptHookContext
                    { Caster = owner, Status = status, StatusHost = host, Target = owner, Declaration = BattleEventHub.Current },
                    () => status.IsActive && owner.IsAlive && owner.HasStatusInstance(status));
        }, status.ApplyOrder, () => status.IsActive && owner.IsAlive && owner.HasStatusInstance(status)));
    }
    private static void StopC2(StatusInstance status, object host)
    {
        if (C2Listeners.TryGetValue(status, out var subscription)) subscription.Dispose();
        C2Listeners.Remove(status);
    }
}

/// <summary>本人整数Count，0～7；普通施加+1至3且不削去已有7枚；Param2=C6直接设7；出手时清零。</summary>
public sealed class NingguangStarJadeCount : CountStatusHandler
{
    public const string ID = "ST_Ningguang_StarJade";
    internal static BattleEntity RequireOwner(StatusInstance status, object host)
    {
        if (!(host is BattleEntity owner) || owner != status.Caster || owner.EntityID != 1017)
            throw new InvalidOperationException("凝光的星璇、屏次数及二命监听必须挂在本人。");
        return owner;
    }
    private static bool IsC6(string argument)
    {
        if (string.IsNullOrEmpty(argument)) return false;
        if (argument == "C6") return true;
        throw new ArgumentException("星璇ApplyStatus.Param2只接受留空（普通获得）或C6（直接设为7）。");
    }
    public override void OnCreated(StatusInstance status, object host) => OnCreated(status, host, null);
    public override void OnCreated(StatusInstance status, object host, CountStatusApplication application)
    {
        RequireOwner(status, host); status.RuntimeIntValue = IsC6(application?.Param2) ? 7 : 1;
        status.IsPermanent = true; status.StackCount = 1;
    }
    public override StatusInstance OnReapplied(StatusInstance status, BattleEntity host, CountStatusApplication application)
    {
        RequireOwner(status, host);
        int next = IsC6(application.Param2) ? 7 : status.RuntimeIntValue < 3 ? status.RuntimeIntValue + 1 : status.RuntimeIntValue;
        Set(status, host, next, ResourceChangeReason.Gain); return status;
    }
    internal static void Set(StatusInstance status, BattleEntity owner, int value, ResourceChangeReason reason)
    {
        int before = status.RuntimeIntValue; status.RuntimeIntValue = Math.Max(0, Math.Min(7, value));
        if (before != status.RuntimeIntValue)
            BattleEventHub.Publish(new ResourceEventData("Count", ID, owner, before, status.RuntimeIntValue,
                status.RuntimeIntValue - before, reason, true, actor: owner));
    }
}

/// <summary>整屏共享6次/5回合；实际命中的完整敌方技能行动结清后扣1，自然到期/刷新/爆发不算摧毁。</summary>
public sealed class NingguangScreenCount : CountStatusHandler
{
    public const string ID = "ST_Ningguang_Screen";
    private sealed class Memory
    {
        internal IDisposable Hits, Actions;
        internal readonly Dictionary<long, DamageSourceInfo> Pending = new Dictionary<long, DamageSourceInfo>();
    }
    public override void OnCreated(StatusInstance status, object host)
    {
        var owner = NingguangStarJadeCount.RequireOwner(status, host);
        status.RuntimeIntValue = 6; status.StackCount = 1;
        NingguangScriptHooks.AdoptFields(status, NingguangScriptHooks.ScreenFieldID);
        var memory = new Memory(); status.CountRuntimeState = memory;
        Func<bool> active = () => status.IsActive && owner.IsAlive && owner.HasStatusInstance(status);
        memory.Hits = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (header, hit) =>
        {
            var source = hit.Source;
            if (!hit.HitLanded || !source.CountsAsHit || header.ActionExecutionId == 0
                || hit.Subject?.Side != owner.Side || source.SourceKind != ReactionSourceKind.EnemySkill
                || !NingguangScriptHooks.Covers(status, source.SourceEntity)) return;
            memory.Pending[header.ActionExecutionId] = source;
        }, status.ApplyOrder, active);
        memory.Actions = BattleEventHub.Subscribe<ActionEventData>(BattleEventKind.ActionCompleted, (header, action) =>
        {
            if (!memory.Pending.TryGetValue(header.ActionExecutionId, out var source)) return;
            memory.Pending.Remove(header.ActionExecutionId);
            if (action.ActionType != "EnemySkill" || source.SourceEntity != action.Subject || source.SourceSkillID != action.SkillId) return;
            int before = status.RuntimeIntValue; status.RuntimeIntValue = Math.Max(0, before - 1);
            BattleEventHub.Publish(new ResourceEventData("Count", ID, owner, before, status.RuntimeIntValue,
                -1, ResourceChangeReason.Spend, true, actor: action.Subject));
            if (status.RuntimeIntValue == 0) owner.RemoveStatusInstance(status, "ChargesExhausted");
        }, status.ApplyOrder, active);
    }
    public override StatusInstance OnReapplied(StatusInstance status, BattleEntity host, CountStatusApplication application)
    {
        host.RemoveStatusInstance(status, "Replaced"); return application.ApplyTo(host);
    }
    public override void OnRemoved(StatusInstance status, object host)
    {
        if (status.CountRuntimeState is Memory memory) { memory.Hits?.Dispose(); memory.Actions?.Dispose(); memory.Pending.Clear(); }
        status.CountRuntimeState = null;
    }
}
