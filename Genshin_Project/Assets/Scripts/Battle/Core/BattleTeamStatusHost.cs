using System;
using System.Collections.Generic;

/// <summary>
/// 一方队伍的纯逻辑状态宿主，不是BattleEntity，不占位置，也没有HP、能量或角色状态栏。
/// Buff/Debuff由同阵营单位只读汇总；行动与计时始终针对这一份真实实例。
/// </summary>
public sealed class BattleTeamStatusHost : IDisposable
{
    private readonly StatusInstanceCollection statuses = new StatusInstanceCollection();
    public BattleSide Side { get; }
    public bool IsDisposed { get; private set; }
    public BattleTeamStatusHost(BattleSide side) { Side = side; }
    public bool Contains(StatusInstance status) => !IsDisposed && statuses.Contains(status);
    public bool HasStatus(string id) => GetStatusInstances(id).Exists(s => s.IsActive);
    public StatusInstance GetStatus(string id) => statuses.TryGetValue(id, out var status) ? status : null;
    public List<StatusInstance> GetStatusInstances(string id) => statuses.GetAll(id);
    /// <summary>不按Display筛选。队伍UI以后从这里自行选择显示项。</summary>
    public List<StatusInstance> GetStatusList()
    {
        var result = new List<StatusInstance>(statuses.Values);
        result.Sort((a, b) => a.ApplyOrder.CompareTo(b.ApplyOrder));
        return result;
    }

    public StatusInstance AddStatus(string id, BattleEntity caster, int duration, int addPhase,
        int triggerPhase, StatusMainData main, WeaponRuntimeContext weapon = null, string param2 = null,
        EquipmentEffectSource equipmentSource = null, HashSet<string> bindingAncestry = null,
        string enchantElement = null, string enchantOverride = null)
    {
        if (id == VerdantDewPool.ResourceID || id == TeamPublicStatuses.PolarDomain
            || id == TeamPublicStatuses.Thundercloud || id == TeamPublicStatuses.MoonCage)
            throw new InvalidOperationException("公共队伍机制状态须由对应公共处理器创建：" + id);
        return Add(id, caster, duration, addPhase, triggerPhase, main, weapon, param2,
            equipmentSource, bindingAncestry, false, enchantElement: enchantElement, enchantOverride: enchantOverride);
    }

    // 固定机制仍使用原处理器的时机；不能同时进入通用阶段桶再扣一次持续时间。
    internal StatusInstance AddManagedStatus(string id, BattleEntity caster, int duration, int phase,
        StatusMainData main, IEnumerable<long> causes = null)
        => Add(id, caster, duration, phase, 0, main, null, null, null, null, true, causes);

    private StatusInstance Add(string id, BattleEntity caster, int duration, int addPhase,
        int triggerPhase, StatusMainData main, WeaponRuntimeContext weapon, string param2,
        EquipmentEffectSource equipmentSource, HashSet<string> bindingAncestry, bool managed, IEnumerable<long> causes = null,
        string enchantElement = null, string enchantOverride = null)
    {
        if (id == PublicControlStates.OffField || id == PublicControlStates.EnemyStun)
            throw new InvalidOperationException(id + "只能施加于合法单位实体。");
        if (IsDisposed) throw new ObjectDisposedException(nameof(BattleTeamStatusHost));
        if (string.IsNullOrWhiteSpace(id) || main == null || main.StatusID2 != id)
            throw new ArgumentException("队伍状态必须提供匹配的主表定义。", nameof(main));
        using var resolution = BattleEventHub.BeginResolution();
        using var collection = BattleResolutionContext.BeginCollection();
        equipmentSource = EquipmentStatusLifetime.Resolve(id, caster, weapon, equipmentSource);
        if (equipmentSource != null && !equipmentSource.IsAvailable) return null;
        OverallShieldSystem.ValidateHost(main, this, caster);
        CountStatusSystem.Validate(main);
        var enchant = EnchantStatusSystem.Parse(main, enchantElement, param2, enchantOverride);
        if (string.Equals(main?.StatusType, "Shield", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("队伍不是护盾受盾实体；请由队伍状态行动向单位施加护盾：" + id);
        if (main.GetMultiplierPart() == "HPBonus" || main.GetMultiplierPart() == "HPBonusFlat")
            throw new NotSupportedException("队伍生命上限Buff尚无通用增删重算规则，不能仅投影列表后假定已生效：" + id);
        // 个人Count有专用实体规则，不因为支持队伍宿主就擅自改成共享资源。
        if (CountStatusSystem.IsCount(main))
            throw new NotSupportedException("该Count尚未定义队伍宿主规则：" + id);
        if (!managed && DataManager.Instance != null
            && DataManager.Instance.StatusActionDict.TryGetValue(id, out var actions) && actions.Count > 0
            && caster?.CharacterCtrl == null)
            throw new NotSupportedException("队伍状态行动目前需要真实角色来源执行器：" + id);
        if (addPhase == 0)
        {
            int actual = BattleManager.Instance != null ? (int)BattleManager.Instance.CurrentPhase : 0;
            addPhase = actual >= 1 && actual <= 6 ? actual : (int)TurnPhase.AllyPreTurn;
        }
        var old = statuses.GetAll(id);
        if (main != null && main.MaxStack > 0 && old.Count >= main.MaxStack)
        {
            if (main.WhenMax == "1") return old[0];
            int remove = old.Count - main.MaxStack + 1;
            for (int i = 0; i < remove; i++) RemoveStatusInstance(old[i], "Replaced");
        }
        var status = new StatusInstance
        {
            StatusID2 = id, StackCount = 1, RemainingPhaseCount = duration,
            IsPermanent = duration <= 0, AddInPhase = addPhase, TriggerPhase = triggerPhase,
            MainData = main, WeaponContext = weapon, EquipmentSource = equipmentSource,
            Enchant = enchant,
            IsActive = true, ApplyOrder = ++BattleEntity._applyOrderCounter, Host = this
        };
        status.RecordSource(caster);
        BattleSpecialRules.OnStatusCreated(status, this);
        statuses.Add(status);
        using var declaration = BattleEventHub.Enter(BattleEventHub.Publish(new StatusEventData(status, this, true, "Applied"), causes: causes));
        if (!managed)
        {
            StatusBindingSystem.ApplyBindings(status, this, bindingAncestry ?? new HashSet<string>());
            caster?.CharacterCtrl?.OnStatusApplied(status, this);
            if (!Contains(status)) return status;
            BattleManager.Instance?.RegisterStatusTick(status, this);
            PreDamageHookSystem.RegisterPreDamageHook(status, this);
            KillHookSystem.Register(status, this);
        }
        StatusHostAccess.OnExtraBaseHPChanged(status, this);
        return status;
    }

    public bool RemoveStatusInstance(StatusInstance status, string reason = "Removed")
    {
        if (status?.StatusID2 == VerdantDewPool.ResourceID)
            throw new InvalidOperationException("草露须通过VerdantDew.TrySpend整笔消费，不能绕过资源声明直接移除。");
        return Remove(status, reason);
    }

    internal bool SpendResourceStatus(StatusInstance status) => Remove(status, "ResourceSpent");

    private bool Remove(StatusInstance status, string reason)
    {
        using var resolution = BattleEventHub.BeginResolution();
        using var collection = BattleResolutionContext.BeginCollection();
        if (status == null || !statuses.Remove(status)) return false;
        status.IsActive = false;
        BattleSpecialRules.OnStatusRemoved(status, this);
        using var declaration = BattleEventHub.Enter(BattleEventHub.Publish(new StatusEventData(status, this, false, reason)));
        StatusRemovalActions.OnRemoved(status, this, reason);
        StatusBindingSystem.RemoveBindings(status);
        BattleManager.Instance?.UnregisterStatusTick(status);
        PreDamageHookSystem.UnregisterPreDamageHook(status);
        KillHookSystem.Unregister(status);
        StatusHostAccess.OnExtraBaseHPChanged(status, this);
        return true;
    }

    public bool RemoveStatus(string id)
    {
        bool removed = false;
        foreach (var status in statuses.GetAll(id)) removed |= RemoveStatusInstance(status);
        return removed;
    }

    internal void ClearManagedStatuses(string id)
    {
        foreach (var status in statuses.GetAll(id))
        {
            status.IsActive = false;
            statuses.Remove(status);
        }
    }

    public void Dispose()
    {
        using var teardown = StatusRemovalActions.SuppressForTeardown();
        if (IsDisposed) return;
        StatusInstance extraHP = GetStatusList().Find(status => status.MainData?.GetMultiplierPart() == "ExtraBaseHP");
        // 会话销毁不冒充战斗内消耗、到期或移除，不执行OnEnd。
        IsDisposed = true;
        foreach (var status in GetStatusList())
        {
            status.IsActive = false;
            StatusBindingSystem.RemoveBindings(status);
            BattleManager.Instance?.UnregisterStatusTick(status);
            PreDamageHookSystem.UnregisterPreDamageHook(status);
            KillHookSystem.Unregister(status);
        }
        statuses.Clear();
        if (extraHP != null) StatusHostAccess.OnExtraBaseHPChanged(extraHP, this);
    }
    public override string ToString() => Side + "Team";
}

/// <summary>统一查询真实宿主；属性投影不能代替这些实例级生命周期操作。</summary>
public static class StatusHostAccess
{
    /// <summary>ExtraBaseHP增删只改变真实上限；本批替换/绑定/响应结清后裁切，不在查询时补血或制造临时掉血。</summary>
    internal static void OnExtraBaseHPChanged(StatusInstance status, object host)
    {
        if (status?.MainData?.GetMultiplierPart() != "ExtraBaseHP") return;
        BattleResolutionContext.AfterResponses(() =>
        {
            if (host is BattleEntity entity) entity.ClampCurrentHPToMaximum();
            else if (host is FieldPosition field) field.Occupant?.ClampCurrentHPToMaximum();
            else if (host is BattleTeamStatusHost team)
            {
                var battle = BattleManager.Instance;
                if (battle == null) return;
                foreach (var ally in battle.Allies)
                    if (ally?.Entity != null && ally.Entity.Side == team.Side) ally.Entity.ClampCurrentHPToMaximum();
                foreach (var enemy in battle.Enemies)
                    if (enemy?.Entity != null && enemy.Entity.Side == team.Side) enemy.Entity.ClampCurrentHPToMaximum();
            }
        });
    }

    public static void VisitEntity(BattleEntity entity, Action<IEnumerable<StatusInstance>, object> visit)
    {
        if (entity == null) return;
        visit(entity.GetStatusList(), entity);
        if (entity.Position != null) visit(new List<StatusInstance>(entity.Position.StatusList), entity.Position);
        var team = BattleManager.Instance?.GetTeamFor(entity);
        if (team != null) visit(team.GetStatusList(), team);
    }

    public static void VisitBattle(BattleManager battle, Action<IEnumerable<StatusInstance>, object> visit)
    {
        if (battle == null) return;
        foreach (var ally in battle.Allies) if (ally?.Entity != null) visit(ally.Entity.GetStatusList(), ally.Entity);
        foreach (var enemy in battle.Enemies) if (enemy?.Entity != null) visit(enemy.Entity.GetStatusList(), enemy.Entity);
        if (battle.Field != null)
        {
            foreach (var field in battle.Field.AllySlots) visit(new List<StatusInstance>(field.StatusList), field);
            foreach (var field in battle.Field.EnemySlots) visit(new List<StatusInstance>(field.StatusList), field);
        }
        visit(battle.AllyTeam.GetStatusList(), battle.AllyTeam);
        visit(battle.EnemyTeam.GetStatusList(), battle.EnemyTeam);
    }

    public static bool Contains(object host, StatusInstance status)
        => host is BattleEntity entity ? entity.HasStatusInstance(status)
        : host is FieldPosition field ? field.StatusList.Contains(status)
        : host is BattleTeamStatusHost team && team.Contains(status);

    public static bool Remove(object host, StatusInstance status, string reason = "Removed")
        => host is BattleEntity entity ? entity.RemoveStatusInstance(status, reason)
        : host is FieldPosition field ? field.RemoveStatusInstance(status, reason)
        : host is BattleTeamStatusHost team && team.RemoveStatusInstance(status, reason);

    public static object FindHost(StatusInstance status, BattleEntity recipient = null)
    {
        if (status?.Host != null && Contains(status.Host, status)) return status.Host;
        if (recipient != null && recipient.HasStatusInstance(status)) return recipient;
        if (recipient?.Position != null && Contains(recipient.Position, status)) return recipient.Position;
        return null;
    }

    /// <summary>被动Buff/Debuff、Immune及Enchant可影响同阵营成员；不复制状态，Count、标记和Shield不投影。</summary>
    public static bool IsTeamBuff(StatusInstance status)
    {
        if (status == null || !status.IsActive || status.MainData == null) return false;
        if (DamageImmunity.IsImmuneType(status.MainData.StatusType) || EnchantStatusSystem.IsEnchant(status.MainData)) return true;
        if (string.IsNullOrEmpty(status.MainData.GetMultiplierPart())) return false;
        var data = DataManager.Instance;
        if (data == null) return false;
        foreach (var effect in data.StatusEffectDict.Values)
            if (effect != null && effect.StatusEffectID != 0 && effect.StatusEffectID / 100 == status.MainData.StatusID
                && (effect.EffectType == "Buff" || effect.EffectType == "Debuff")) return true;
        return false;
    }
}
