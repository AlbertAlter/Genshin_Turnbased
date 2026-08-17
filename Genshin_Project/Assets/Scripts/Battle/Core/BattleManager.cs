using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 回合状态机单例。
/// 职责：
///  - 六阶段循环推进（1->2->...->6->1）
///  - 每进入一个阶段：先执行该阶段的触发集合（OnTrigger），再执行计时器集合（状态剩余回合-1，归零触发 OnEnd 并移除）
///  - 我方行动阶段（AllyAction）不会自动推进，必须等待"结束回合"接口 EndAllyTurn()（对应 UI 的结束回合按键）
///  - 其余阶段按 PhaseInterval 秒自动推进
///
/// 阶段推进顺序严格按设计文档：
///  "进入每个阶段前，先计算其触发集合，再计算其计时器集合"
/// </summary>
public class BattleManager : MonoBehaviour
{
    public static BattleManager Instance { get; private set; }

    [Header("阶段推进速度")]
    [Tooltip("非我方行动阶段自动推进的间隔秒数（配表默认0.5s）")]
    public float PhaseInterval = 0.5f;

    // ========== 战斗单位注册表 ==========
    private List<CharacterBattleController> _allies = new List<CharacterBattleController>();
    private List<EnemyBattleController> _enemies = new List<EnemyBattleController>();

    // ========== 场地位置系统（2026-08-07） ==========
    public BattleField Field { get; private set; } = new BattleField();

    // ========== 运行时状态 ==========
    public TurnPhase CurrentPhase { get; private set; }
    //全局AP（我方全体共用的行动点池，2026-08-14）
    public ActionPointManager APManager { get; private set; }
    public bool IsBattleRunning { get; private set; }

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

    // ========== 状态结算登记表（2026-08-12） ==========
    // 以状态为中心：状态施加时按 AddInPhase/TriggerPhase 登记到对应阶段桶，
    // 阶段结算直接取本阶段列表（追加顺序 = 施加顺序 = 大顺序），到期/移除时注销。
    // 只负责单阶段内的状态结算（OnTrigger / OnItsTurn / OnEnd），不涉及其它状态机逻辑。
    private readonly Dictionary<int, List<(StatusInstance inst, BattleEntity entity, FieldPosition slot)>> _statusByPhase
        = new Dictionary<int, List<(StatusInstance inst, BattleEntity entity, FieldPosition slot)>>();
    private readonly Dictionary<int, List<(StatusInstance inst, BattleEntity entity, FieldPosition slot)>> _triggerByPhase
        = new Dictionary<int, List<(StatusInstance inst, BattleEntity entity, FieldPosition slot)>>();

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
        _allies.Clear();
        _enemies.Clear();
    }

    // ================================================================
    //  状态登记（2026-08-12：以状态为中心的大顺序结算）
    // ================================================================

    /// <summary>
    /// 状态施加登记：按 AddInPhase 放进计时桶；若该状态有 OnTrigger 行动行且 TriggerPhase>0，
    /// 再按 TriggerPhase 放进触发桶。桶内追加顺序 = 施加顺序 = 大顺序。
    /// </summary>
    public void RegisterStatusTick(StatusInstance inst, BattleEntity entity, FieldPosition slot)
    {
        if (inst == null) return;
        var item = (inst, entity, slot);

        if (!_statusByPhase.TryGetValue(inst.AddInPhase, out var tickList))
        {
            tickList = new List<(StatusInstance, BattleEntity, FieldPosition)>();
            _statusByPhase[inst.AddInPhase] = tickList;
        }
        tickList.Add(item);

        if (inst.TriggerPhase > 0)
        {
            var actions = GetStatusActions(inst.StatusID2);
            bool hasOnTrigger = false;
            if (actions != null)
            {
                foreach (var act in actions)
                    if (act.ActionType == "OnTrigger") { hasOnTrigger = true; break; }
            }
            if (hasOnTrigger)
            {
                if (!_triggerByPhase.TryGetValue(inst.TriggerPhase, out var trigList))
                {
                    trigList = new List<(StatusInstance, BattleEntity, FieldPosition)>();
                    _triggerByPhase[inst.TriggerPhase] = trigList;
                }
                trigList.Add(item);
            }
        }
    }

    /// <summary>状态注销（到期/移除/战斗结束）：从计时桶和触发桶移除（按实例引用，幂等）。</summary>
    public void UnregisterStatusTick(StatusInstance inst)
    {
        if (inst == null) return;
        if (_statusByPhase.TryGetValue(inst.AddInPhase, out var tickList))
        {
            int idx = tickList.FindIndex(t => t.inst == inst);
            if (idx >= 0) tickList.RemoveAt(idx);
        }
        if (inst.TriggerPhase > 0 && _triggerByPhase.TryGetValue(inst.TriggerPhase, out var trigList))
        {
            int idx2 = trigList.FindIndex(t => t.inst == inst);
            if (idx2 >= 0) trigList.RemoveAt(idx2);
        }
    }

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
        if (e == null) return 0f;
        return e.CurrentHP;
    }

    /// <summary>查询敌方某位置是否有存活单位。全空组合判定用</summary>
    public bool IsEnemyAliveAt(int position)
    {
        var e = GetEntityByPosition(BattleSide.Enemy, position);
        return e != null && e.IsAlive;
    }

    /// <summary>
    /// 以指定敌方位置为起点，按数量+连续规则展开目标（爆发选3个相邻等）。
    /// consecutive: 1=连续（从起点往后），其他=从起点向后随机（简化）
    /// </summary>
    public List<BattleEntity> GetEnemiesFromPosition(BattleSide side, int position, int count, int consecutive)
    {
        var result = new List<BattleEntity>();
        if (side != BattleSide.Enemy || count <= 0) return result;

        // 按位置升序收集存活敌人
        var alive = new List<KeyValuePair<int, BattleEntity>>();
        foreach (var enemy in _enemies)
        {
            if (enemy != null && enemy.Entity != null && enemy.Entity.IsAlive)
                alive.Add(new KeyValuePair<int, BattleEntity>(enemy.Entity.SlotPosition, enemy.Entity));
        }
        alive.Sort((a, b) => a.Key.CompareTo(b.Key));

        if (consecutive == 1)
        {
            // 从起点位置开始往后取 count 个（连续）
            int startIdx = -1;
            for (int i = 0; i < alive.Count; i++)
                if (alive[i].Key == position) { startIdx = i; break; }
            if (startIdx < 0) return result;
            for (int i = startIdx; i < alive.Count && result.Count < count; i++)
                result.Add(alive[i].Value);
        }
        else
        {
            // 简化：从起点起随机向后取
            var pool = new List<BattleEntity>();
            int s = -1;
            for (int i = 0; i < alive.Count; i++)
                if (alive[i].Key == position) { s = i; break; }
            if (s < 0) return result;
            for (int i = s; i < alive.Count; i++) pool.Add(alive[i].Value);
            while (pool.Count > 0 && result.Count < count)
            {
                int idx = UnityEngine.Random.Range(0, pool.Count);
                result.Add(pool[idx]);
                pool.RemoveAt(idx);
            }
        }
        return result;
    }

    /// <summary>
    /// 获取某实体在己方阵营的存活相邻单位（不含自身，供 BindStatus 的 TargetSelect 展开）。
    /// </summary>
    public List<BattleEntity> GetAdjacentTargets(BattleSide side, int position, int range)
    {
        var result = new List<BattleEntity>();
        var positions = BattlePositionSystem.GetAdjacentPositions(side, position, range);
        foreach (int pos in positions)
        {
            if (pos == position) continue; // 相邻不含原点自身
            var e = GetEntityByPosition(side, pos);
            if (e != null && e.IsAlive) result.Add(e);
        }
        return result;
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
        IsBattleRunning = true;
        _allyTurnEnded = false;
        if (Field == null) Field = new BattleField();
        CurrentPhase = TurnPhase.AllyPreTurn;
        LogManager.Log(LogCategory.Turn, "Battle started. Phase = AllyPreTurn(1)");
        StartCoroutine(PhaseLoop());
    }

    /// <summary>
    /// 重置战斗（热加载/重开时调用）：停止阶段循环、清理状态，允许重新 StartBattle。
    /// </summary>
    public void ResetBattle()
    {
        IsBattleRunning = false;
        _allyTurnEnded = false;
        StopAllCoroutines();
        CurrentPhase = TurnPhase.AllyPreTurn;
    }

    /// <summary>
    /// 结束回合接口：供"结束回合"UI 按键调用。
    /// 仅在我方行动阶段（AllyAction）有意义，调用后立即推进到下一阶段。
    /// </summary>
    public void EndAllyTurn()
    {
        if (CurrentPhase == TurnPhase.AllyAction)
        {
            _allyTurnEnded = true;
        }
    }

    // ================================================================
    //  按槽位行动接口（UI 按键 / 键盘数字键绑定）
    //  slotIndex: 队伍位置，从左到右 0 起（0=最左，1=左二……）
    //  仅在我方行动阶段（AllyAction）且角色可行动时生效
    // ================================================================
    /// <summary>当前出战角色（IsActive），无则回退第一个。换人后操作/UI 都作用于此（2026-08-14）。</summary>
    public CharacterBattleController GetActiveAlly()
    {
        foreach (var a in _allies)
            if (a != null && a.IsActive) return a;
        return _allies.Count > 0 ? _allies[0] : null;
    }

    /// <summary>我方位置（1~4）血量查询（目标选择器用，2026-08-14）。</summary>
    public float GetAllyHpAt(int pos)
    {
        if (pos < 1 || pos > _allies.Count) return 0f;
        var a = _allies[pos - 1];
        return (a != null && a.Entity != null) ? a.Entity.CurrentHP : 0f;
    }

    /// <summary>我方位置（1~4）存活查询（目标选择器用，2026-08-14）。</summary>
    public bool IsAllyAliveAt(int pos)
    {
        if (pos < 1 || pos > _allies.Count) return false;
        var a = _allies[pos - 1];
        return a != null && a.Entity != null && a.Entity.IsAlive;
    }

    public CharacterBattleController GetAllyBySlot(int slotIndex)
    {
        if (_allies == null || slotIndex < 0 || slotIndex >= _allies.Count) return null;
        return _allies[slotIndex];
    }

    public int AllyCount => _allies != null ? _allies.Count : 0;

    public bool UseNormalAttackBySlot(int slotIndex)
    {
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
        IsBattleRunning = false;
        _allyTurnEnded = true;
        StopAllCoroutines();
    }

    // ================================================================
    //  战斗结束判定（2026-08-13）：敌方全灭=胜利，我方全灭=失败
    // ================================================================
    public void CheckBattleEnd()
    {
        if (IsBattleOver) return;

        bool allEnemiesDead = true;
        foreach (var e in _enemies)
            if (e != null && e.Entity != null && !e.Entity.IsDead) { allEnemiesDead = false; break; }
        if (allEnemiesDead && _enemies.Count > 0)
        {
            IsBattleOver = true;
            Victory = true;
            LogManager.Log(LogCategory.Battle, "胜利！敌方全灭");
            OnBattleEnded?.Invoke(true);
            return;
        }

        bool allAlliesDead = true;
        foreach (var a in _allies)
            if (a != null && a.Entity != null && !a.Entity.IsDead) { allAlliesDead = false; break; }
        if (allAlliesDead && _allies.Count > 0)
        {
            IsBattleOver = true;
            Victory = false;
            LogManager.Log(LogCategory.Battle, "失败！我方全灭");
            OnBattleEnded?.Invoke(false);
        }
    }

    // ================================================================
    //  阶段循环
    // ================================================================
    private IEnumerator PhaseLoop()
    {
        while (IsBattleRunning)
        {
            // 战斗结束（胜利/失败）：停止阶段推进（2026-08-13）
            if (IsBattleOver) yield break;

            // 1. 进入当前阶段：先触发集合，后计时集合（协程链：Display==1 状态行动会让流程暂停 0.5s 演出）
            yield return StartCoroutine(EnterPhase(CurrentPhase));

            // 2. 我方行动阶段：先重置所有我方角色的 AP/冷却（每回合开始时 AP 全回复）
            if (CurrentPhase == TurnPhase.AllyAction)
            {
                APManager.ResetAP(); //全局AP：我方每回合共用100（2026-08-14）
                foreach (var ally in _allies)
                    ally.OnTurnStart();

                _allyTurnEnded = false;
                while (!_allyTurnEnded && IsBattleRunning)
                    yield return null;
            }
            else
            {
                yield return new WaitForSeconds(PhaseInterval);
            }

            if (!IsBattleRunning) break;

            // 2.5 敌方行动阶段：驱动所有存活敌人行动
            if (CurrentPhase == TurnPhase.EnemyAction)
            {
                ExecuteEnemyTurn();
            }

            // 3. 推进到下一阶段
            CurrentPhase = TurnPhaseUtil.Next(CurrentPhase);
            LogManager.Log(LogCategory.Turn, $"Phase -> {CurrentPhase}");
        }
    }

    // ================================================================
    //  阶段结算（设计文档时间线）
    //  每个阶段按固定顺序：
    //    ① 触发集合：按施加顺序触发 OnTrigger
    //    ② 全部状态 -1 回合（计时器集合统一减）
    //    ③ 所有该 End 的状态触发对应 End 效果
    //    ④ 元素量回合递减
    //    ⑤ 若是全局跨回合（敌方回合结束→我方回合开始，即进入阶段1时）：
    //       元素量递减之后，全局技能冷却 -1（含我方角色与敌人）
    // ================================================================
    private IEnumerator EnterPhase(TurnPhase phase)
    {
        LogManager.Log(LogCategory.Turn, $"Enter Phase {(int)phase} ({phase})");

        // ① 触发集合：遍历所有单位身上 TriggerPhase == 当前阶段的 OnTrigger 状态
        yield return StartCoroutine(ExecuteTriggerSet(phase));

        // ②③ 计时器集合：先全部 -1 回合，再统一触发到期状态的 OnEnd
        yield return StartCoroutine(ExecuteTimerSet(phase));

        // ④ 元素量回合递减
        foreach (var ally in _allies)
            if (ally.Entity != null) ally.Entity.TickAuras();
        foreach (var enemy in _enemies)
            if (enemy.Entity != null) enemy.Entity.TickAuras();

        // ⑤ 全局跨回合（阶段6 → 阶段1）：进入阶段1时，全局技能冷却-1（角色+敌人）+ 回合计数+1
        if (phase == TurnPhase.AllyPreTurn)
        {
            TurnCount++;
            foreach (var ally in _allies)
                ally.TickSkillCooldowns();
            foreach (var enemy in _enemies)
                enemy.TickSkillCooldowns();
        }
    }

    /// <summary>
    /// 触发集合（2026-08-12 以状态为中心）：
    /// 状态施加时已按 TriggerPhase 登记到触发桶，这里直接取本阶段桶，
    /// 按施加顺序（大顺序）逐个触发 OnTrigger。
    /// 结算顺序：触发集合整体在计时器集合（OnItsTurn）之前。
    /// </summary>
    private IEnumerator ExecuteTriggerSet(TurnPhase phase)
    {
        if (!_triggerByPhase.TryGetValue((int)phase, out var list) || list.Count == 0) yield break;
        foreach (var item in list)
        {
            var actions = GetStatusActions(item.inst.StatusID2);
            if (actions == null) continue;
            if (item.inst.Caster != null && item.inst.Caster.CharacterCtrl != null)
            {
                object host = item.entity != null ? (object)item.entity : item.slot;
                LogManager.Log(LogCategory.Status, $"OnTrigger {item.inst.StatusID2} on {(item.entity != null ? item.entity.name : "slot")} at phase {(int)phase}");
                yield return StartCoroutine(item.inst.Caster.CharacterCtrl.ExecuteStatusActionsAsync(item.inst, actions, host, "OnTrigger"));
            }
        }
    }

    /// <summary>
    /// 通用条件触发（2026-08-12，特殊处理器/事件类 OnTrigger 条件预留）：
    /// 先查该条件本次命中了哪些状态（身上有 OnTrigger 行动行且满足条件），
    /// 再按这些状态在本阶段的大顺序（ApplyOrder 全局施加顺序）排序后逐个触发其 Action。
    /// </summary>
    private void TriggerByCondition(System.Func<StatusInstance, bool> condition)
    {
        var hits = new List<(StatusInstance inst, BattleEntity entity, FieldPosition slot)>();
        foreach (var bucket in _statusByPhase.Values)
        {
            foreach (var item in bucket)
            {
                if (item.inst == null || !condition(item.inst)) continue;
                var actions = GetStatusActions(item.inst.StatusID2);
                if (actions == null) continue;
                bool hasOnTrigger = false;
                foreach (var act in actions)
                    if (act.ActionType == "OnTrigger") { hasOnTrigger = true; break; }
                if (hasOnTrigger) hits.Add(item);
            }
        }
        hits.Sort((a, b) => a.inst.ApplyOrder.CompareTo(b.inst.ApplyOrder));
        foreach (var item in hits)
        {
            var actions = GetStatusActions(item.inst.StatusID2);
            if (actions == null || item.inst.Caster == null || item.inst.Caster.CharacterCtrl == null) continue;
            object host = item.entity != null ? (object)item.entity : item.slot;
            item.inst.Caster.CharacterCtrl.ExecuteStatusActions(item.inst, actions, host, "OnTrigger");
        }
    }

    /// <summary>
    /// 计时器集合：状态 AddInPhase 等于当前阶段时，剩余回合-1；
    /// 归零后触发 OnEnd 并移除该状态。
    /// 2026-08-12 改造：以状态为中心——不管施加给敌方/我方/位置，只要是本阶段要结算的状态，
    /// 全部收进一个大顺序（ApplyOrder 全局施加顺序），严格按大顺序逐个结算（OnItsTurn），
    /// 到期状态按同样顺序统一执行 OnEnd 并移除。
    /// </summary>
    private IEnumerator ExecuteTimerSet(TurnPhase phase)
    {
        // 直接取本阶段登记的状态列表（施加时已按 AddInPhase 登记，列表顺序 = 施加顺序 = 大顺序）
        if (!_statusByPhase.TryGetValue((int)phase, out var list) || list.Count == 0) yield break;

        // 本阶段统一 -1 回合 + OnItsTurn（严格按大顺序），收集到期
        var expired = new List<(StatusInstance inst, BattleEntity entity, FieldPosition slot)>();
        foreach (var item in list)
        {
            item.inst.RemainingPhaseCount--;
            // OnItsTurn：轮到该状态回合（AddInPhase==当前阶段）时生效
            if (item.inst.Caster != null && item.inst.Caster.CharacterCtrl != null)
            {
                var acts = GetStatusActions(item.inst.StatusID2);
                if (acts != null)
                {
                    object host = item.entity != null ? (object)item.entity : item.slot;
                    yield return StartCoroutine(item.inst.Caster.CharacterCtrl.ExecuteStatusActionsAsync(item.inst, acts, host, "OnItsTurn"));
                }
            }
            if (item.inst.RemainingPhaseCount <= 0)
                expired.Add(item);
        }

        // 到期统一处理（同样按大顺序）：OnEnd → 子状态清理 → 移除 + 注销
        var removedNames = new List<string>();
        foreach (var item in expired)
        {
            if (item.entity != null)
            {
                LogManager.Log(LogCategory.Status, $"{item.inst.StatusID2} on {item.entity.name} expired at phase {(int)phase}");
                var actions = GetStatusActions(item.inst.StatusID2);
                if (actions != null && item.inst.Caster != null && item.inst.Caster.CharacterCtrl != null)
                    yield return StartCoroutine(item.inst.Caster.CharacterCtrl.ExecuteStatusActionsAsync(item.inst, actions, item.entity, "OnEnd"));
                item.entity.RemoveStatus(item.inst.StatusID2); // RemoveStatus 内部会注销
                removedNames.Add(item.inst.StatusID2);
            }
            else if (item.slot != null)
            {
                LogManager.Log(LogCategory.Status, $"{item.inst.StatusID2} on slot {item.slot} expired at phase {(int)phase}");
                var actions = GetStatusActions(item.inst.StatusID2);
                if (actions != null && item.inst.Caster != null && item.inst.Caster.CharacterCtrl != null)
                    yield return StartCoroutine(item.inst.Caster.CharacterCtrl.ExecuteStatusActionsAsync(item.inst, actions, item.slot, "OnEnd"));

                // 父状态移除 → 连带移除绑定的位置子状态（BindStatus 生命周期跟随父状态）
                if (item.inst.BoundStatuses != null && item.inst.BoundStatuses.Count > 0)
                {
                    foreach (var childInst in item.inst.BoundStatuses)
                    {
                        if (Field == null) break;
                        foreach (var eslot in Field.EnemySlots)
                        {
                            if (eslot != null && eslot.StatusList.Remove(childInst))
                            {
                                UnregisterStatusTick(childInst);
                                removedNames.Add(childInst.StatusID2);
                                break;
                            }
                        }
                    }
                    item.inst.BoundStatuses.Clear();
                }

                // 位置状态到期：解除其 ChangeControl 记录（2026-08-12 补）
                if (item.inst.Caster != null && item.inst.Caster.CharacterCtrl != null)
                    item.inst.Caster.CharacterCtrl.OnStatusChangeControlRemoved(item.inst);

                item.slot.StatusList.Remove(item.inst);
                removedNames.Add(item.inst.StatusID2);
            }
            UnregisterStatusTick(item.inst); // 幂等：实体走 RemoveStatus 已注销，位置手动注销
        }
        if (removedNames.Count > 0)
            LogManager.Log(LogCategory.Status, $"消失状态清单 (phase {(int)phase}): {string.Join(", ", removedNames)}");
    }

    /// <summary>
    /// 敌方行动阶段：驱动所有存活敌人依次行动。
    /// </summary>
    private void ExecuteEnemyTurn()
    {
        foreach (var enemy in _enemies)
        {
            if (enemy != null && enemy.Entity != null && enemy.Entity.IsAlive)
            {
                enemy.TakeEnemyTurn();
            }
        }
    }

    // ================================================================
    //  数据查询
    // ================================================================
    /// <summary>
    /// 查配表 Status_Action，返回某状态的全部行动定义（OnApply/OnItsTurn/OnTrigger/OnEnd/OnHit）。
    /// </summary>
    private List<StatusActionData> GetStatusActions(string statusID2)
    {
        var dm = DataManager.Instance;
        if (dm == null) return null;
        dm.StatusActionDict.TryGetValue(statusID2, out var actions);
        return actions;
    }
}
