using System.Collections;
using System.Collections.Generic;
using System;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 回合状态机单例。
/// 职责：
///  - 六阶段循环推进（1->2->...->6->1）
///  - 每进入一个阶段：先执行 TriggerPhase 指定的 OnItsTurn，再执行计时器集合（状态剩余回合-1，归零触发 OnEnd 并移除）
///  - 我方行动阶段（AllyAction）不会自动推进，必须等待"结束回合"接口 EndAllyTurn()（对应 UI 的结束回合按键）
///  - 其余阶段按 PhaseInterval 秒自动推进
///
/// 阶段推进顺序严格按设计文档：
///  "进入每个阶段前，先计算其触发集合，再计算其计时器集合"
/// </summary>
public partial class BattleManager : MonoBehaviour
{
    public static BattleManager Instance { get; private set; }

    [Header("阶段推进速度")]
    [Tooltip("非我方行动阶段自动推进的间隔秒数（配表默认0.5s）")]
    public float PhaseInterval = 0.5f;

    // ========== 战斗单位注册表 ==========
    private readonly List<CharacterBattleController> _allies = new List<CharacterBattleController>();
    private List<EnemyBattleController> _enemies = new List<EnemyBattleController>();

    // ========== 场地位置系统（2026-08-07） ==========
    public BattleField Field { get; private set; } = new BattleField();
    public TeamBonusSystem TeamBonuses { get; private set; }
    private BattleTeamStatusHost _allyTeam;
    private BattleTeamStatusHost _enemyTeam;
    public BattleTeamStatusHost AllyTeam => GetTeam(BattleSide.Ally);
    public BattleTeamStatusHost EnemyTeam => GetTeam(BattleSide.Enemy);
    public BattleTeamStatusHost GetTeam(BattleSide side)
    {
        if (side == BattleSide.Ally) return _allyTeam ?? (_allyTeam = new BattleTeamStatusHost(side));
        return _enemyTeam ?? (_enemyTeam = new BattleTeamStatusHost(side));
    }
    /// <summary>只把本场真实成员映射到队伍，不能让上一场实体读取新队伍加成。</summary>
    public BattleTeamStatusHost GetTeamFor(BattleEntity entity)
    {
        if (entity == null) return null;
        if (entity.Side == BattleSide.Ally)
        {
            foreach (var ally in _allies) if (ally?.Entity == entity) return AllyTeam;
        }
        else foreach (var enemy in _enemies) if (enemy?.Entity == entity) return EnemyTeam;
        return null;
    }
    private void DisposeTeamHosts()
    {
        _allyTeam?.Dispose(); _enemyTeam?.Dispose();
        _allyTeam = _enemyTeam = null;
    }

    // ========== 运行时状态 ==========
    public TurnPhase CurrentPhase { get; private set; }
    //全局AP（我方全体共用的行动点池，2026-08-14）
    public ActionPointManager APManager { get; private set; }
    public bool IsBattleRunning { get; private set; }
    public bool IsCurrentPhaseReady { get; private set; }
    public bool IsAwaitingAllyInput => IsBattleRunning && !IsBattleOver && CurrentPhase == TurnPhase.AllyAction
        && IsCurrentPhaseReady && !_allyTurnEnded;
    public bool IsResolving => BattleExecution.IsBusy;

    private IBattleFlowScheduler _flowScheduler;

    // 战斗结束判定（2026-08-13）：敌方全灭=胜利，我方全灭=失败
    public bool IsBattleOver { get; private set; }
    public bool Victory { get; private set; }
    /// <summary>当前回合数（阶段6→1时+1，从1开始）。状态行动 MaxTimePerTurn/Cooldown 计数用。</summary>
    public int TurnCount { get; private set; } = 1;
    // 战斗结束 UI 接口（2026-08-13）：胜利/失败时触发（参数=true胜利/false失败），
    // UI 界面预留为空，后续接入专门的结果界面显示。
    public event System.Action<bool> OnBattleEnded;

    // 我方行动阶段等待结束回合标志
    private bool _allyTurnEnded;
    private AllyFrontline _frontline;
    private AllyFrontline Frontline => _frontline ??= new AllyFrontline(_allies);
    // 本地化提示出口；表现层订阅key，当前尚未创建提示跳字UI。
    public event System.Action<string> TipRequested;
    public void RequestTip(string key)
    {
        LogManager.Log(LogCategory.Action, DataManager.Instance?.GetLangText(key) ?? key);
        TipRequested?.Invoke(key);
    }
    public bool IsFreeDeathSwitchPending => Frontline.IsFreeDeathSwitchPending;
    // 第一次进入阶段1是战斗开场，不属于“敌方回合结束→下一回合”的跨回合点。
    private bool _isInitialAllyPreTurn = true;

    private readonly BattleStatusPhaseScheduler _statusPhases = new BattleStatusPhaseScheduler();
    private readonly EnemyTurnRunner _enemyTurn = new EnemyTurnRunner();

    // ========== 战斗流程预解析缓存（钩子触发用，逻辑见 PreDamageHookSystem.cs，2026-08-15 独立） ==========
    /// <summary>预解析缓存：当前主动行为所有 Damage 效果的目标位置并集（位置编号，去重）</summary>
    public readonly List<int> PendingActionTargetPositions = new List<int>();

    private void Awake()
    {
        APManager = new ActionPointManager();
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        EnsureFlowScheduler();
        ReactionStateSystem.SetDisplayAdapter(new StatusDataReactionStateDisplayAdapter());
    }

    private void OnDestroy()
    {
        using var teardown = StatusRemovalActions.SuppressForCleanup();
        if (Instance != this) return;
        TeamBonuses?.Dispose();
        TeamBonuses = null;
        DisposeTeamHosts();
        BattleExecution.Cancel();
        StopFlow();
        IsCurrentPhaseReady = false;
        if (Instance == this) Instance = null;
    }

    private void StopFlow()
    {
        _flowScheduler?.Stop();
        _enemyTurn.Clear();
    }

    private void EnsureFlowScheduler()
    {
        if (_flowScheduler == null)
            _flowScheduler = new UnityBattleFlowScheduler(this);
    }

    public void SetFlowScheduler(IBattleFlowScheduler scheduler)
    {
        if (scheduler == null) throw new ArgumentNullException(nameof(scheduler));
        if (IsBattleRunning)
            throw new InvalidOperationException("Cannot replace the battle flow scheduler while battle is running.");

        StopFlow();
        _flowScheduler = scheduler;
    }

    // ================================================================
    //  单位注册
    // ================================================================
    public void RegisterAlly(CharacterBattleController ally)
    {
        if (ally != null && !_allies.Contains(ally))
        {
            _allies.Add(ally);
            AssignToField(ally.Entity, BattleSide.Ally);
        }
    }

    public void RegisterEnemy(EnemyBattleController enemy)
    {
        if (enemy != null && !_enemies.Contains(enemy))
        {
            _enemies.Add(enemy);
            AssignToField(enemy.Entity, BattleSide.Enemy);
            if (IsBattleRunning) enemy.ApplyInitiateStatuses();
        }
    }

    // 把单位分配到场地位置（位置编号由外部显式设置：BattleTester/编队系统设 SlotPosition）
    private void AssignToField(BattleEntity entity, BattleSide side)
    {
        if (entity == null || Field == null) return;
        var slot = Field.GetSlot(side, entity.SlotPosition);
        if (slot != null)
        {
            slot.Occupant = entity;
            entity.Position = slot;
        }
    }

    public void UnregisterAll()
    {
        TeamBonuses?.Dispose();
        TeamBonuses = null;
        DisposeTeamHosts();
        _allies.Clear();
        _enemies.Clear();
        ClearSummonedEnemies();
    }

    public void RegisterStatusTick(StatusInstance inst, BattleEntity entity, FieldPosition slot)
        => RegisterStatusTick(inst, entity != null ? (object)entity : slot);

    public void RegisterStatusTick(StatusInstance inst, object host) => _statusPhases.Register(inst, host);

    public void UnregisterStatusTick(StatusInstance inst) => _statusPhases.Unregister(inst);

    public IReadOnlyList<CharacterBattleController> Allies => _allies;
    public IReadOnlyList<EnemyBattleController> Enemies => _enemies;

    // ================================================================
    //  位置系统查询（2026-08-06）
    //  规则：我方在右侧从左往右1-4；敌方从右往左1-5。
    //  敌方"从右往左1-5"：位置1=最右（列表索引0），位置5=最左（列表索引4）。
    // ================================================================

    /// <summary>
    /// 按位置查找同阵营存活实体（位置编号：我方1-4从左往右，敌方1-5从右往左）。
    /// 通过遍历 SlotPosition 匹配（不假设列表索引=位置-1，因为可能缺员）。
    /// </summary>
    public BattleEntity GetEntityByPosition(BattleSide side, int position)
    {
        if (side == BattleSide.Ally)
        {
            foreach (var ally in _allies)
            {
                if (ally != null && ally.Entity != null && ally.Entity.SlotPosition == position)
                    return ally.Entity;
            }
        }
        else
        {
            foreach (var enemy in _enemies)
            {
                if (enemy != null && enemy.Entity != null && enemy.Entity.SlotPosition == position)
                    return enemy.Entity;
            }
        }
        return null;
    }

    /// <summary>查询敌方某位置的威胁度（空位/无实体=0）。备用（当前自动选择用绝对血量）</summary>
    public int GetEnemyThreatAt(int position)
    {
        var e = GetEntityByPosition(BattleSide.Enemy, position);
        if (e == null) return 0;
        return e.Threat;
    }

    /// <summary>查询敌方某位置的当前绝对血量（空位/无实体=0）。自动选择敌方目标时用（文档：绝对血量最高）</summary>
    public float GetEnemyHpAt(int position)
    {
        var e = GetEntityByPosition(BattleSide.Enemy, position);
        if (e?.IsOnField != true) return 0f;
        return e.CurrentHP;
    }

    /// <summary>查询敌方某位置是否有存活单位。全空组合判定用</summary>
    public bool IsEnemyAliveAt(int position)
    {
        var e = GetEntityByPosition(BattleSide.Enemy, position);
        return e != null && e.IsOnField;
    }

    // ================================================================
    //  战斗控制
    // ================================================================
    /// <summary>
    /// 开始战斗：重置到阶段1（我方回合开始前）并启动阶段循环协程。
    /// </summary>
    public void StartBattle()
    {
        if (IsBattleRunning) return;
        EnsureFlowScheduler();
        IsBattleRunning = true;
        IsCurrentPhaseReady = false;
        _allyTurnEnded = false;
        Frontline.ClearPendingSwitches();
        if (Field == null) Field = new BattleField();
        CurrentPhase = TurnPhase.AllyPreTurn;
        LogManager.Log(LogCategory.Turn, "Battle started. Phase = AllyPreTurn(1)");
        try
        {
            if (TeamBonuses != null) { TeamBonuses.Dispose(); DisposeTeamHosts(); }
            var roster = new List<BattleEntity>();
            foreach (CharacterBattleController ally in _allies)
                if (ally != null && ally.Entity != null) roster.Add(ally.Entity);
            var openingHealth = new List<BattleAttributeInitialization.OpeningHealth>();
            foreach (var actor in roster) openingHealth.Add(BattleAttributeInitialization.Capture(actor));
            foreach (var enemy in _enemies)
                if (enemy?.Entity != null) openingHealth.Add(BattleAttributeInitialization.Capture(enemy.Entity));
            DataManager data = DataManager.Instance;
            Task OpenBattle() => BattleExecution.Run(async () =>
            {
                TeamBonuses = new TeamBonusSystem(roster, data != null ? data.TeamBonusRules : new TeamBonusRules(),
                    actor => data != null && data.CharacterAttributesDict.TryGetValue(actor.EntityID, out var attr)
                        ? attr.Element : string.Empty, AllyTeam, EnemyTeam);
                BattleSpecialRules.OnBattleReady(this);
                await BattleExecution.Flush();
                BattleExecution.ThrowIfCancelled();
                BattleAttributeInitialization.Complete(openingHealth);
            });

            // Keep immediate initialization for tools. A real player's opening shield animation
            // and its queued declarations must finish before the phase loop opens another resolution.
            Task opening = BattleExecution.IsBusy ? null : OpenBattle();
            if (opening != null && opening.IsCompleted) opening.GetAwaiter().GetResult();
            _flowScheduler.Start(PhaseLoop(opening, OpenBattle));
        }
        catch
        {
            TeamBonuses?.Dispose();
            TeamBonuses = null;
            DisposeTeamHosts();
            IsBattleRunning = false;
            IsCurrentPhaseReady = false;
            throw;
        }
    }

    /// <summary>
    /// 重置战斗（热加载/重开时调用）：停止阶段循环、清理状态，允许重新 StartBattle。
    /// </summary>
    public void ResetBattle()
    {
        using var teardown = StatusRemovalActions.SuppressForCleanup();
        TeamBonuses?.Dispose();
        TeamBonuses = null;
        DisposeTeamHosts();
        BattleExecution.Cancel();
        IsBattleRunning = false;
        IsCurrentPhaseReady = false;
        _allyTurnEnded = false;
        Frontline.ClearPendingSwitches();
        EnsureFlowScheduler();
        StopFlow();

        IsBattleOver = false;
        Victory = false;
        TurnCount = 1;
        _isInitialAllyPreTurn = true;
        CurrentPhase = TurnPhase.AllyPreTurn;

        // EditMode 测试或特殊工具链可能在 Awake 尚未执行时调用重置；重置入口自行保证 AP 池存在。
        if (APManager == null) APManager = new ActionPointManager();
        APManager.ResetAP();
        _statusPhases.Clear();
        PendingActionTargetPositions.Clear();
        PreDamageHookSystem.ClearAll();
        PostDamageHookSystem.ClearAll();
        // Kill 钩子清空（2026-08-19）：不能让上一场战斗的登记残留到下一场
        KillHookSystem.ClearAll();
        BattleEntity._applyOrderCounter = 0;
        ReactionResolver.ResetSession();
        HitBatch.ResetSession();
        BattleEventHub.ResetSession();
        BattleEntity.ResetDamageResolutionSession();
        BattleRandom.ResetSource();

        ClearRegisteredEntityRuntimeState();
        UnregisterAll();
        Field = new BattleField();
    }

    private void ClearRegisteredEntityRuntimeState()
    {
        foreach (CharacterBattleController ally in _allies)
            ClearEntityRuntimeState(ally != null ? ally.Entity : null);
        foreach (EnemyBattleController enemy in _enemies)
            ClearEntityRuntimeState(enemy != null ? enemy.Entity : null);
    }

    private static void ClearEntityRuntimeState(BattleEntity entity)
    {
        if (entity == null) return;
        entity.HasWithdrawn = false;
        BattleSpecialRules.OnEntityReset(entity);
        entity.ElementalAuras.Clear();
        entity.Shields.Clear();
        entity.StatusDict.Clear();
        entity.Position = null;
        BattleAttributeInitialization.Prepare(entity);
    }

    /// <summary>
    /// 结束回合接口：供"结束回合"UI 按键调用。
    /// 仅在我方行动阶段（AllyAction）有意义，调用后立即推进到下一阶段。
    /// </summary>
    public void EndAllyTurn()
    {
        if (BattleExecution.IsBusy) return ;
        if (IsBattleRunning && !IsBattleOver && CurrentPhase == TurnPhase.AllyAction)
        {
            if (IsFreeDeathSwitchPending) return;
            _allyTurnEnded = true;
        }
    }

    // ================================================================
    //  按槽位行动接口（UI 按键 / 键盘数字键绑定）
    //  slotIndex: 队伍位置，从左到右 0 起（0=最左，1=左二……）
    //  仅在我方行动阶段（AllyAction）且角色可行动时生效
    // ================================================================
    private bool IsAllyActionPhase => IsBattleRunning && !IsBattleOver && CurrentPhase == TurnPhase.AllyAction;
    private bool CanPlayerSwitch => !BattleExecution.IsBusy && IsAllyActionPhase;

    /// <summary>当前出战角色；保留阵亡对象供免费换人流程识别。</summary>
    public CharacterBattleController GetActiveAlly() => Frontline.GetActiveAlly();

    public bool CanSwitchActiveAllyTo(int slotIndex)
        => CanPlayerSwitch && Frontline.CanSwitchTo(GetAllyBySlot(slotIndex));

    public bool TrySwitchActiveAlly(int slotIndex)
        => CanPlayerSwitch && Frontline.TrySwitchManually(GetAllyBySlot(slotIndex), APManager, chargeAP: false);

    /// <summary>效果自动上场：允许在技能或状态执行中调用，沿用统一的换人提交。</summary>
    public bool TrySwitchInFromEffect(BattleEntity target)
    {
        if (!IsBattleRunning || IsBattleOver || target == null || target.Side != BattleSide.Ally || !target.IsOnField)
            return false;
        return GetEntityByPosition(BattleSide.Ally, target.SlotPosition) == target && Frontline.TrySwitchFromEffect(target);
    }

    // 回场在完整响应之后处理。若还没进入可操作的我方阶段，则留到阶段入口处理。
    internal void OnAllyReturnedToField(BattleEntity entity)
    {
        if (!Frontline.RecordReturn(entity)) return;
        BattleResolutionContext.AfterResponses(() =>
        {
            if (IsCurrentPhaseReady) EnsureActiveAllyAfterReturn();
        });
    }

    private void EnsureActiveAllyAfterReturn()
    {
        if (IsAllyActionPhase) Frontline.ResolveReturn();
    }

    public const int SwitchAPCost = AllyFrontline.SwitchAPCost;

    /// <summary>查询指定换人目标的最终费用，不扣 AP，不产生上下场事件。</summary>
    public int GetSwitchAPCost(int slotIndex) => Frontline.GetSwitchAPCost(GetAllyBySlot(slotIndex));

    /// <summary>玩家换人入口。普通换人支付结算后的费用，回合开始时的阵亡强制换人免费。</summary>
    public bool TrySwitchActiveAllyWithAPCost(int slotIndex)
        => CanPlayerSwitch && Frontline.TrySwitchManually(GetAllyBySlot(slotIndex), APManager, chargeAP: true);

    public bool CanSwitchActiveAlly() => CanPlayerSwitch && Frontline.CanSwitch();

    /// <summary>我方位置（1~4）血量查询（目标选择器用，2026-08-14）。</summary>
    public float GetAllyHpAt(int pos)
    {
        CharacterBattleController ally = GetAllyBySlot(pos - 1);
        return ally != null && ally.Entity?.IsOnField == true ? ally.Entity.CurrentHP : 0f;
    }

    /// <summary>我方位置（1~4）存活查询（目标选择器用，2026-08-14）。</summary>
    public bool IsAllyAliveAt(int pos)
    {
        CharacterBattleController ally = GetAllyBySlot(pos - 1);
        return ally != null && ally.Entity != null && ally.Entity.IsOnField;
    }

    /// <summary>按实际编队位置取角色；注册列表允许缺员，不能把列表索引当成位置。</summary>
    public CharacterBattleController GetAllyBySlot(int slotIndex)
    {
        if (_allies == null || slotIndex < 0) return null;
        int position = slotIndex + 1;
        foreach (CharacterBattleController ally in _allies)
        {
            if (ally != null && ally.Entity != null && ally.Entity.SlotPosition == position)
                return ally;
        }
        return null;
    }

    public int AllySlotCount => Field != null && Field.AllySlots != null ? Field.AllySlots.Count : 4;
    public int AllyCount => _allies != null ? _allies.Count : 0;

    /// <summary>A/D 换人选择：按编队位置循环，并跳过空位和阵亡角色。</summary>
    public int FindNextLivingAllySlot(int currentSlotIndex, int direction)
    {
        int slotCount = AllySlotCount;
        if (slotCount <= 0 || direction == 0) return currentSlotIndex;
        int step = direction > 0 ? 1 : -1;
        int normalized = ((currentSlotIndex % slotCount) + slotCount) % slotCount;
        for (int offset = 1; offset <= slotCount; offset++)
        {
            int candidate = (normalized + step * offset + slotCount * 2) % slotCount;
            CharacterBattleController ally = GetAllyBySlot(candidate);
            if (ally != null && ally.Entity != null && ally.Entity.IsOnField)
                return candidate;
        }
        return normalized;
    }


    public bool CanClickControlAlly(BattleEntity target) => !BattleExecution.IsBusy && IsAwaitingAllyInput
        && !IsFreeDeathSwitchPending && target != null
        && GetEntityByPosition(BattleSide.Ally, target.SlotPosition) == target
        && AllyControlRelease.GetOldest(target).Exists;

    public bool TryClickControlAlly(BattleEntity target, out AllyControlClickResult result)
    {
        result = AllyControlClickResult.Ignored;
        if (!CanClickControlAlly(target)) return false;
        using var resolution = BattleEventHub.BeginResolution();
        using var collection = BattleResolutionContext.BeginCollection();
        result = AllyControlRelease.Click(target, APManager, out var handled);
        if (result == AllyControlClickResult.InsufficientAP)
            RequestTip(handled.Airborne != null ? "tip_airborne_insufficient_ap" : BattleTipKeys.ThawInsufficientAP);
        if (result == AllyControlClickResult.Released)
            StatusOnHitHookSystem.NotifyAPUsed(target, AllyControlRelease.APCost,
                handled.Airborne != null ? APSpendKind.RecoverFromAirborne : APSpendKind.Thaw);
        return result == AllyControlClickResult.Progressed || result == AllyControlClickResult.Released;
    }

    public bool CanClickThawActiveAlly => CanClickThawAlly(GetActiveAlly()?.Entity);
    public bool CanClickThawAlly(BattleEntity target) => CanClickControlAlly(target)
        && AllyControlRelease.GetOldest(target).Frozen != null;

    public bool TryThawActiveAlly() => TryThawActiveAlly(out _);
    public bool TryThawActiveAlly(out FrozenThawClickResult result) => TryThawAlly(GetActiveAlly()?.Entity, out result);

    // 保留解冻调用方的返回契约，不能绕过较早施加的击飞。
    public bool TryThawAlly(BattleEntity target, out FrozenThawClickResult result)
    {
        result = FrozenThawClickResult.Ignored;
        if (!CanClickThawAlly(target)) return false;
        bool accepted = TryClickControlAlly(target, out var clicked);
        switch (clicked)
        {
            case AllyControlClickResult.Progressed: result = FrozenThawClickResult.Cracked; break;
            case AllyControlClickResult.Released: result = FrozenThawClickResult.Thawed; break;
            case AllyControlClickResult.InsufficientAP: result = FrozenThawClickResult.InsufficientAP; break;
        }
        return accepted;
    }

    public bool UseNormalAttackBySlot(int slotIndex)
    {
        if (BattleExecution.IsBusy) return false;
        if (!IsBattleRunning || IsBattleOver) { LogManager.Log(LogCategory.Action, "普攻失败：战斗未运行或已结束"); return false; }
        if (CurrentPhase != TurnPhase.AllyAction) { LogManager.Log(LogCategory.Action, $"普攻失败：非我方行动阶段"); return false; }
        var c = GetActiveAlly();
        if (c == null) { LogManager.Log(LogCategory.Action, $"普攻失败：槽位{slotIndex}无角色"); return false; }
        var reason = c.GetBlockReason(0);
        if (reason != null) { LogManager.Log(LogCategory.Action, $"普攻失败：{reason}"); return false; }
        c.ExecuteNormalAttack();
        return true;
    }

    public bool UseHeavyAttackBySlot(int slotIndex)
    {
        if (BattleExecution.IsBusy) return false;
        if (!IsBattleRunning || IsBattleOver) { LogManager.Log(LogCategory.Action, "重击失败：战斗未运行或已结束"); return false; }
        if (CurrentPhase != TurnPhase.AllyAction) { LogManager.Log(LogCategory.Action, $"重击失败：非我方行动阶段"); return false; }
        var c = GetActiveAlly();
        if (c == null) { LogManager.Log(LogCategory.Action, $"重击失败：槽位{slotIndex}无角色"); return false; }
        var reason = c.GetBlockReason(1);
        if (reason != null) { LogManager.Log(LogCategory.Action, $"重击失败：{reason}"); return false; }
        c.ExecuteHeavyAttack();
        return true;
    }

    public bool UseSkillBySlot(int slotIndex)
    {
        if (BattleExecution.IsBusy) return false;
        if (!IsBattleRunning || IsBattleOver) { LogManager.Log(LogCategory.Action, "战技失败：战斗未运行或已结束"); return false; }
        if (CurrentPhase != TurnPhase.AllyAction) { LogManager.Log(LogCategory.Action, $"战技失败：非我方行动阶段"); return false; }
        var c = GetActiveAlly();
        if (c == null) { LogManager.Log(LogCategory.Action, $"战技失败：槽位{slotIndex}无角色"); return false; }
        var reason = c.GetBlockReason(2);
        if (reason != null) { LogManager.Log(LogCategory.Action, $"战技失败：{reason}"); return false; }
        c.ExecuteSkill();
        return true;
    }

    public bool UseBurstBySlot(int slotIndex)
    {
        if (BattleExecution.IsBusy) return false;
        if (!IsBattleRunning || IsBattleOver) { LogManager.Log(LogCategory.Action, "爆发失败：战斗未运行或已结束"); return false; }
        if (CurrentPhase != TurnPhase.AllyAction) { LogManager.Log(LogCategory.Action, $"爆发失败：非我方行动阶段"); return false; }
        var c = GetActiveAlly();
        if (c == null) { LogManager.Log(LogCategory.Action, $"爆发失败：槽位{slotIndex}无角色"); return false; }
        var reason = c.GetBlockReason(3);
        if (reason != null) { LogManager.Log(LogCategory.Action, $"爆发失败：{reason}"); return false; }
        c.ExecuteBurst();
        return true;
    }

    /// <summary>
    /// 强制结束战斗（测试用）。
    /// </summary>
    public void StopBattle()
    {
        BattleExecution.Cancel();
        IsBattleRunning = false;
        IsCurrentPhaseReady = false;
        _allyTurnEnded = true;
        EnsureFlowScheduler();
        StopFlow();
    }

    // ================================================================
    //  战斗结束判定（2026-08-13）：敌方全灭=胜利，我方全灭=失败
    // ================================================================
    public void CheckBattleEnd()
    {
        if (IsBattleOver) return;
        if (TryCompleteVictory()) return;
        if (BattleDeathResolution.DeferBattleEndCheck()) return;

        bool allAlliesDead = true;
        foreach (var a in _allies)
            if (a != null && a.Entity != null && !a.Entity.IsDead) { allAlliesDead = false; break; }
        if (allAlliesDead && _allies.Count > 0)
        {
            LogManager.Log(LogCategory.Battle, "失败！我方全灭");
            CompleteBattle(false);
        }
    }

    internal bool AllEnemiesDefeated
    {
        get
        {
            if (_enemies.Count == 0) return withdrawnEnemyCount > 0;
            foreach (var enemy in _enemies)
                if (enemy?.Entity != null && !enemy.Entity.IsDead) return false;
            return true;
        }
    }

    private bool TryCompleteVictory()
    {
        if (!AllEnemiesDefeated) return false;
        LogManager.Log(LogCategory.Battle, "胜利！敌方全灭");
        CompleteBattle(true);
        return true;
    }

    private void CompleteBattle(bool victory)
    {
        if (IsBattleOver) return;
        IsBattleOver = true;
        Victory = victory;
        using (BattleResolutionContext.BeginCollection())
        {
            BattleEventHub.Publish(new BattleEndedEventData(victory));
            // 先结清结束时的正式状态响应，再通知原有结算/存档调用者。
            BattleResolutionContext.AfterResponses(() =>
                BattleExecution.AfterPresentations(() => OnBattleEnded?.Invoke(victory)));
        }
    }

    // ================================================================
    //  阶段循环
    // ================================================================
    private IEnumerator PhaseLoop(Task opening, Func<Task> openBattle)
    {
        if (opening == null)
        {
            while (BattleExecution.IsBusy) yield return null;
            opening = openBattle();
        }
        while (!opening.IsCompleted) yield return null;
        if (opening.IsFaulted || opening.IsCanceled)
        {
            IsBattleRunning = false;
            TeamBonuses?.Dispose();
            TeamBonuses = null;
            DisposeTeamHosts();
            if (opening.IsFaulted) opening.GetAwaiter().GetResult();
            yield break;
        }
        while (IsBattleRunning)
        {
            // 战斗结束（胜利/失败）：停止阶段推进（2026-08-13）
            if (IsBattleOver)
            {
                IsCurrentPhaseReady = false;
                yield break;
            }

            // 1. 阶段结算等待真实表现完成；默认播放器立即完成，不引入额外帧。
            IsCurrentPhaseReady = false;
            yield return EnterPhase(CurrentPhase);

            // 2. 我方行动阶段：先重置所有我方角色的 AP/冷却（每回合开始时 AP 全回复）
            if (CurrentPhase == TurnPhase.AllyAction)
            {
                APManager.ResetAP(); //全局AP：我方每回合共用100（2026-08-14）
                foreach (var ally in _allies)
                    if (ally != null && ally.Entity != null && ally.Entity.IsAlive)
                        ally.OnTurnStart();

                CharacterBattleController active = GetActiveAlly();
                if (active?.Entity?.IsOnField != true) RequestTip(BattleTipKeys.NoActiveCharacter);
                Frontline.PrepareAllyAction(active, CanPlayerSwitch);

                _allyTurnEnded = false;
                IsCurrentPhaseReady = true;
                while (!_allyTurnEnded && IsBattleRunning && !IsBattleOver)
                    yield return null;
            }
            else
            {
                IsCurrentPhaseReady = true;
                yield return new WaitForSeconds(PhaseInterval);
            }

            if (!IsBattleRunning || IsBattleOver)
            {
                IsCurrentPhaseReady = false;
                break;
            }

            // 2.5 敌方行动阶段：驱动所有存活敌人行动
            if (CurrentPhase == TurnPhase.EnemyAction)
            {
                yield return _enemyTurn.Execute(_enemies, () => IsBattleRunning && !IsBattleOver);
            }

            // 3. 推进到下一阶段
            IsCurrentPhaseReady = false;
            CurrentPhase = TurnPhaseUtil.Next(CurrentPhase);
            LogManager.Log(LogCategory.Turn, $"Phase -> {CurrentPhase}");
        }
    }

    // ================================================================
    //  阶段结算（设计文档时间线）
    //  每个阶段按固定顺序：
    //    ① 触发集合：按施加顺序执行指定 TriggerPhase 的 OnItsTurn
    //    ② 全部状态 -1 回合（计时器集合统一减）
    //    ③ 所有该 End 的状态触发对应 End 效果
    //    ④ 元素量回合递减
    //    ⑤ 若是全局跨回合（敌方回合结束→我方回合开始，即进入阶段1时）：
    //       元素量递减之后，全局技能冷却 -1（含我方角色与敌人）
    // ================================================================
    private IEnumerator EnterPhase(TurnPhase phase)
    {
        LogManager.Log(LogCategory.Turn, $"Enter Phase {(int)phase} ({phase})");

        // 全局跨回合入口必须先清理上个主动行为的临时目标缓存，避免阶段1状态误读旧目标。
        // PreDamageHookSystem 保存的是持续登记，不属于行为缓存，不能在这里清空。
        if (phase == TurnPhase.AllyPreTurn)
        {
            if (_isInitialAllyPreTurn)
            {
                _isInitialAllyPreTurn = false;
            }
            else
            {
                PendingActionTargetPositions.Clear();
                PostDamageHookSystem.ClearAll();
                TurnCount++;
                foreach (var ally in _allies)
                    if (ally != null) ally.TickSkillCooldowns();
                foreach (var enemy in _enemies)
                    if (enemy != null) enemy.TickSkillCooldowns();
            }
        }

        // 结晶盾以生成阶段为计时原点，并在本阶段任何伤害/状态触发前到期移除。
        TeamBonuses?.EnterPhase((int)phase, TurnCount);
        foreach (CharacterBattleController ally in _allies)
            if (ally != null && ally.Entity != null) ally.Entity.TickShields((int)phase);
        foreach (EnemyBattleController enemy in _enemies)
            if (enemy != null && enemy.Entity != null) enemy.Entity.TickShields((int)phase);

        // 草原核以创建阶段为一回合原点，到期时先完成位置爆炸。
        yield return BattleExecution.Wait(BattleExecution.Run(async () =>
        {
            BloomCoreSystem.TickPhase((int)phase);
            await BattleExecution.Flush();
        }));

        // 感电在双方回合结束阶段先消耗水雷并结算伤害，随后才进入通用状态与元素衰减流程。
        yield return BattleExecution.Wait(BattleExecution.Run(async () =>
        {
            TeamBonuses?.Thundercloud.TickTurnEnd(phase);
            await BattleExecution.Flush();
        }));
        yield return BattleExecution.Wait(BattleExecution.Run(async () =>
        {
            ElectroChargedReactionHandler.TickTurnEnd(phase);
            await BattleExecution.Flush();
        }));
        yield return BattleExecution.Wait(BattleExecution.Run(async () =>
        {
            BurningReactionHandler.TickTurnEnd(phase);
            await BattleExecution.Flush();
        }));

        // 反应状态先结算旧实例；本阶段后续触发中新建的状态从下一次同阶段开始计时。
        TeamBonuses?.Thundercloud.TickDuration((int)phase);
        TeamBonuses?.MoonCages.TickDuration((int)phase);
        ReactionStateSystem.TickPhase((int)phase);

        yield return _statusPhases.ExecutePhase(phase, TurnCount);

        // ④ 元素量回合递减
        foreach (var ally in _allies)
            if (ally != null && ally.Entity != null) ally.Entity.TickAuras();
        foreach (var enemy in _enemies)
            if (enemy != null && enemy.Entity != null) enemy.Entity.TickAuras();
        ElectroChargedReactionHandler.CleanupInvalidStates();
        if (phase == TurnPhase.AllyAction)
            yield return BattleExecution.Wait(BattleExecution.Run(async () =>
            {
                EnsureActiveAllyAfterReturn();
                await BattleExecution.Flush();
            }));
    }

    /// <summary>只在敌方行动池存续时登记次数变动，由行动池在下一轮分配前结算。</summary>
    public bool TryGrantEnemyActions(int delta) => !IsBattleOver && _enemyTurn.TryGrantActions(delta);
}
