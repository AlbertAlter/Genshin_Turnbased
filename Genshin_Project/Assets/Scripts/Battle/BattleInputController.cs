using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 战斗输入控制（正式组件，最终保留，挂场景任意物体上）。
/// 职责：
///  1. 键盘输入：Q/W/E/1 = 普攻/重击/战技/爆发，A/D = 目标左右切换，
///               空格 = 确认目标（选择中）/结束回合（非选择），X/Esc = 取消
///  2. 目标选择状态机（含 SkillPhase 多段技能推进）
///  3. UI 操作接口（UIBattleController 按钮调用，与键盘同一套逻辑）
/// 原实现位于 BattleTester（临时测试脚本），正式版迁移至此。
/// </summary>
public class BattleInputController : MonoBehaviour
{
    private TargetSelector _selector = new TargetSelector();
    private bool _selecting = false;
    private int _pendingSkill = -1;      // 0=普攻 1=重击 2=战技 3=爆发
    private string _pendingOriginalSkillID2; // 目标选择开始时的按钮原绑定（多段推进恢复用）
    private CharacterBattleController _pendingAlly; // 当前施放者（1234爆发可为任意角色，无需出战，2026-08-14）

    // 逐个模式（2026-08-15，文档：Consecutive=0 时 TargetNumber 有几个选几次，不可重复）
    private bool _oneByOneMode = false;
    private int _oneByOneRemaining = 0;
    private List<int> _oneByOnePicked = new List<int>();
    private List<int> _oneByOneExcluded = new List<int>();
    private bool _selectionAllowsEmpty;
    private bool _selectionPrefersLowerScore;
    private Func<int, float> _selectionScore;
    private Func<int, bool> _selectionAlive;

    /// <summary>当前施放者（UI高亮/溅射命中计算用；1234爆发可为非出战角色，2026-08-14）。</summary>
    public CharacterBattleController PendingAlly => _pendingAlly;

    private static readonly string[] SkillNames = { "普攻", "重击", "战技", "爆发" };

    void Update()
    {
        var bm = BattleManager.Instance;
        if (bm == null) return;

        // 战斗结束（胜利/失败）：战斗输入全部屏蔽（结果界面预留，2026-08-14）
        // 换人界面为模态界面：进入时由 UIBattleController 停用本组件（input.enabled=false），此处无需再判断（2026-08-14）
        if (bm.IsBattleOver) return;

        if (_selecting)
        {
            // ===== 目标选择阶段 =====
            // 敌人编号：从右往左1~5（位置1最右）。A=左移=位置号变大（MoveRight），D=右移=位置号变小（MoveLeft）
            // 临时诊断：按 A/D 必定打日志（区分 输入没收到 vs 选择状态已退出）
            if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.D))
            {
                LogManager.Log(LogCategory.Select, $"收到A/D键 _selecting={_selecting}");
                if (Input.GetKeyDown(KeyCode.A))
                    _selector.MoveRight();
                else if (Input.GetKeyDown(KeyCode.D))
                    _selector.MoveLeft();
            }
            else if (Input.GetKeyDown(KeyCode.Space))
            {
                // 确认：把选中组合交给角色，执行技能（2026-08-15 效果级：一个技能可多次确认）
                bool done = ConfirmAndExecute(bm);
                if (done) ExitSelecting();
                // 还有待选效果：保持选择状态（InitSelectorForEffect 已在 ConfirmAndExecute 内完成）
            }
            else if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.X))
            {
                LogManager.Log(LogCategory.Select, "取消目标选择");
                ExitSelecting();
            }
            // 选择中可切换技能/多段推进：Q/W/E/数字1
            else if (Input.GetKeyDown(KeyCode.Q)) HandleSelectSkillKey(bm, 0);
            else if (Input.GetKeyDown(KeyCode.W)) HandleSelectSkillKey(bm, 1);
            else if (Input.GetKeyDown(KeyCode.E)) HandleSelectSkillKey(bm, 2);
            else if (Input.GetKeyDown(KeyCode.Alpha1)) HandleSelectSkillKey(bm, 3);
            return;
        }

        // ===== 非选择阶段 =====
        if (bm.CurrentPhase == TurnPhase.AllyAction)
        {
            if (Input.GetKeyDown(KeyCode.Q)) TryEnterSelecting(bm, 0, false);
            else if (Input.GetKeyDown(KeyCode.W)) TryEnterSelecting(bm, 1, false);
            else if (Input.GetKeyDown(KeyCode.E)) TryEnterSelecting(bm, 2, false);
            //1234=四槽角色爆发（接口文档：可点击任意角色能量槽或主键盘1234，能量符合要求时进入对应爆发目标选择，该角色无需出战，2026-08-14）
            else if (Input.GetKeyDown(KeyCode.Alpha1)) TryEnterBurstBySlot(bm, 0);
            else if (Input.GetKeyDown(KeyCode.Alpha2)) TryEnterBurstBySlot(bm, 1);
            else if (Input.GetKeyDown(KeyCode.Alpha3)) TryEnterBurstBySlot(bm, 2);
            else if (Input.GetKeyDown(KeyCode.Alpha4)) TryEnterBurstBySlot(bm, 3);
        }

        // 空格 = 结束回合（非选择阶段）
        // 空格 = 结束回合（2026-08-14）：换人界面等 UI 拦截状态已在 Update 开头屏蔽，此处无需再判断
        if (Input.GetKeyDown(KeyCode.Space))
            bm.EndAllyTurn();
    }

    // 检查技能是否可用：不可用直接打原因日志并返回false（不进目标选择）
    private bool TryEnterSelecting(BattleManager bm, int skill, bool switchFromSelecting, CharacterBattleController specifiedAlly = null)
    {
        //施放者：默认当前出战；1234爆发可指定任意角色（无需出战，2026-08-14）
        var ally = specifiedAlly != null ? specifiedAlly : bm.GetActiveAlly();
        if (ally == null) return false;

        // 选择中切换技能：先恢复上一技能多段推进的临时绑定
        if (_selecting && _pendingOriginalSkillID2 != null)
        {
            ally.RevertSkillPhase(_pendingSkill, _pendingOriginalSkillID2);
            _pendingOriginalSkillID2 = null;
        }

        string reason = ally.GetBlockReason(skill);
        if (reason != null)
        {
            LogManager.Log(LogCategory.Action, $"{SkillNames[skill]}不可用：{reason}");
            return false;
        }

        // 效果级（2026-08-15，按《战斗界面》文档）：找该技能第一个需选目标的效果
        var first = ally.GetFirstSelectEffect(skill);
        if (first == null)
        {
            // 无任何需选效果：直接执行（配表现状：按钮技能均有需选效果，此分支为通用兜底）
            LogManager.Log(LogCategory.Select, $"{SkillNames[skill]}无需选目标，直接执行");
            _pendingSkill = skill;
            _pendingAlly = ally;
            ally.ForcedTargetPositions.Clear();
            ExecutePendingSkill(bm, ally);
            if (_selecting) ExitSelecting();
            return true;
        }

        // 按该效果的参数初始化选择器（Self→仅高亮施放者；Enemy/EnemyField→选敌方；Allies→选我方）
        InitSelectorForEffect(bm, ally, skill, first);

        if (!_selector.IsSelectionValid())
        {
            LogManager.Log(LogCategory.Action, $"{SkillNames[skill]}失败：目标区域没有可选目标");
            return false;
        }

        _pendingSkill = skill;
        _pendingAlly = ally;
        _pendingOriginalSkillID2 = ally.GetBoundSkillID(skill);   // 记录原绑定（多段推进恢复用）
        _selecting = true;
        LogManager.Log(LogCategory.Select, $"进入目标选择（{SkillNames[skill]}：{first.SkillEffectID2}），A/D切换，空格确认，Esc/X取消");
        return true;
    }

    /// <summary>
    /// 按效果参数初始化选择器（2026-08-15，效果级）：
    ///  Self→仅高亮施放者；Consecutive=0且TargetNumber&gt;1→逐个选（不可重复）；2/3/4→全体特效走流程；其余→滑动窗口。
    /// </summary>
    void InitSelectorForEffect(BattleManager bm, CharacterBattleController ally, int skill, SkillEffectData eff)
    {
        _oneByOneMode = false;
        _oneByOneExcluded.Clear();
        ally.ForcedTargetPositions.Clear(); // 每次新选择阶段开始：清空残留（逐个模式用 AddRange 累积）
        ally.MarkPendingSelectEffect(eff); // 挂载当前待选效果（UI 高亮/溅射预览判定用，2026-08-15）
        if (eff.TargetType == "Self")
        {
            // 仅高亮自身（文档：Self 且填了 Consecutive → 无需解析连续本意，直接仅高亮自身）
            _selector.Init(BattleSide.Ally, 1, 0,
                pos => (ally.Entity != null && ally.Entity.SlotPosition == pos) ? ally.Entity.CurrentHP : 0f,
                pos => ally.Entity != null && ally.Entity.SlotPosition == pos && ally.Entity.IsAlive);
            return;
        }
        bool allySide = eff.TargetType == "Allies" || eff.TargetType == "AlliesOnly" || eff.TargetType == "AllyField";
        bool fieldTarget = !string.IsNullOrEmpty(eff.TargetType) && eff.TargetType.EndsWith("Field", StringComparison.Ordinal);
        var side = allySide ? BattleSide.Ally : BattleSide.Enemy;
        Func<int, float> hp = allySide ? (Func<int, float>)bm.GetAllyHpAt : bm.GetEnemyHpAt;
        Func<int, bool> alive = allySide ? (Func<int, bool>)bm.IsAllyAliveAt : bm.IsEnemyAliveAt;
        bool preferLower = allySide && (eff.EffectType == "Heal" || eff.EffectType == "Shield");
        // 友方增益的正式评分依赖“本场伤害统计”；该系统未建立前保持中性并列随机，不能拿血量冒充伤害。
        Func<int, float> score = allySide ? (Func<int, float>)(_ => 0f) : hp;
        if (preferLower)
        {
            score = position =>
            {
                var entity = bm.GetEntityByPosition(side, position);
                return entity != null && entity.TotalHP > 0f ? entity.CurrentHP / entity.TotalHP : 0f;
            };
        }

        if (eff.TargetType == "AlliesOnly" && ally.Entity != null)
            _oneByOneExcluded.Add(ally.Entity.SlotPosition);

        _selectionAllowsEmpty = fieldTarget;
        _selectionPrefersLowerScore = preferLower;
        _selectionScore = score;
        _selectionAlive = alive;
        if (eff.TargetConsecutive >= 2 && eff.TargetConsecutive <= 4)
        {
            // 2/3/4 随机：全体存活目标特效，走个流程，空格直接施放（2026-08-15）
            _selector.InitAll(side, score, alive, fieldTarget);
        }
        else if (eff.TargetConsecutive == 0 && eff.TargetNumber > 1)
        {
            // 0 非连续：逐个选，TargetNumber 选几次，不可重复（2026-08-15）
            _oneByOneMode = true;
            _oneByOneRemaining = eff.TargetNumber;
            _oneByOnePicked = new List<int>();
            _selector.Init(side, 1, 0, score, alive, _oneByOneExcluded, fieldTarget, preferLower);
        }
        else
        {
            _selector.Init(side, eff.TargetNumber, eff.TargetConsecutive, score, alive, _oneByOneExcluded, fieldTarget, preferLower);
        }
    }

    private void ExitSelecting()
    {
        // 2026-08-15：取消/结束时终止未完成的效果序列（已扣资源/已执行部分效果时清状态，防止污染下一次技能）
        if (_selecting && _pendingAlly != null)
            _pendingAlly.AbortEffectSequence();
        // 恢复多段推进的临时绑定（取消/施放后按钮回到原技能）
        // 2026-08-15：用 _pendingAlly（施放者）而非出战角色——1234爆发施放者可为非出战角色，
        //             若恢复作用到出战角色会把其按钮绑定错误覆盖成别人的技能
        if (_selecting && _pendingOriginalSkillID2 != null)
        {
            var ally = _pendingAlly;
            if (ally != null) ally.RevertSkillPhase(_pendingSkill, _pendingOriginalSkillID2);
        }
        _selecting = false;
        _pendingSkill = -1;
        _pendingAlly = null;
        _pendingOriginalSkillID2 = null;
        _oneByOneMode = false;
        _oneByOneRemaining = 0;
        _oneByOnePicked = new List<int>();
        _oneByOneExcluded = new List<int>();
        _selectionScore = null;
        _selectionAlive = null;
        _selectionAllowsEmpty = false;
        _selectionPrefersLowerScore = false;
    }

    /// <summary>测试战斗重建前退出目标选择，并丢弃上一场的选择缓存。</summary>
    public void ResetForBattle()
    {
        ExitSelecting();
        _selector.Reset();
    }

    // 选择中按技能键：同一按钮 = 多段推进（SkillPhase），不同按钮 = 切换技能
    private void HandleSelectSkillKey(BattleManager bm, int skill)
    {
        var ally = bm.GetActiveAlly();
        if (ally == null) return;

        if (skill == _pendingSkill)
        {
            // 多段推进：按钮临时绑定到下一段（同SkillPhase、SkillID更大的技能）
            string next = ally.AdvanceSkillPhase(skill);
            if (next == null)
            {
                LogManager.Log(LogCategory.Select, $"{SkillNames[skill]}已是最后一段或非多段技能");
                return;
            }
            // 2026-08-15 效果级：找下一段技能的第一个需选效果
            var first = ally.GetFirstSelectEffect(skill);
            if (first == null)
            {
                // 下一段无需选目标：直接施放
                LogManager.Log(LogCategory.Select, $"多段推进至 {ally.GetBoundSkillID(skill)}（{SkillNames[skill]}：无需选目标，直接施放）");
                ConfirmAndExecute(bm);
                ExitSelecting();
                return;
            }
            InitSelectorForEffect(bm, ally, skill, first);
            if (!_selector.IsSelectionValid())
            {
                LogManager.Log(LogCategory.Action, $"{SkillNames[skill]}多段推进失败：目标区域没有可选目标");
                return;
            }
            LogManager.Log(LogCategory.Select, $"多段推进至 {ally.GetBoundSkillID(skill)}（{SkillNames[skill]}：{first.SkillEffectID2}）");
            return;
        }

        // 不同按钮：切换技能（TryEnterSelecting 内部会恢复上一技能绑定）
        TryEnterSelecting(bm, skill, true);
    }

    /// <summary>确认当前选择并推进技能执行。返回 true=全部完成（可结束选择），false=还有待选效果（保持选择继续）。</summary>
    private bool ConfirmAndExecute(BattleManager bm)
    {
        //施放者：1234爆发等指定角色优先（2026-08-14）
        var ally = _pendingAlly != null ? _pendingAlly : bm.GetActiveAlly();
        if (ally == null) return true;

        var sel = _selector.CurrentSelection;
        if (sel == null || sel.Count == 0)
        {
            LogManager.Log(LogCategory.Select, "无有效目标，取消");
            return true;
        }

        // 逐个模式（Consecutive=0 且 TargetNumber>1，2026-08-15）：每次选1个，选完N次再执行
        if (_oneByOneMode)
        {
            ally.ForcedTargetPositions.AddRange(sel);
            _oneByOnePicked.AddRange(sel);
            _oneByOneRemaining--;
            if (_oneByOneRemaining > 0)
            {
                var side = _selector.Side;
                var excluded = new List<int>(_oneByOneExcluded);
                excluded.AddRange(_oneByOnePicked);
                _selector.Init(
                    side,
                    1,
                    0,
                    _selectionScore,
                    _selectionAlive,
                    excluded,
                    _selectionAllowsEmpty,
                    _selectionPrefersLowerScore); // 排除施放者和已选位置，不可重复
                if (!_selector.IsSelectionValid())
                {
                    LogManager.Log(LogCategory.Select, "无可选剩余目标，取消");
                    return true;
                }
                LogManager.Log(LogCategory.Select, $"逐个选择剩 {_oneByOneRemaining} 次（已选 {string.Join(",", _oneByOnePicked)}），继续选下一个");
                return false;
            }
            LogManager.Log(LogCategory.Select, $"逐个选择完成：{string.Join(",", _oneByOnePicked)}，执行技能");
            ExecutePendingSkill(bm, ally);
            return ally.PendingSelectEffect == null;
        }

        ally.ForcedTargetPositions = new List<int>(sel);
        LogManager.Log(LogCategory.Select, $"确认目标 {string.Join("", sel)}，执行技能");
        ExecutePendingSkill(bm, ally);
        return ally.PendingSelectEffect == null;
    }

    private void ExecutePendingSkill(BattleManager bm, CharacterBattleController ally)
    {
        //可用性检查（原走 UseXxxBySlot 自带检查，直接调用 controller 时需自行检查，2026-08-14）
        //2026-08-15：仅首次确认时检查——效果序列进行中资源已扣，二次检查 AP 会误拦
        if (!ally.HasPendingEffectSequence)
        {
            string reason = ally.GetBlockReason(_pendingSkill);
            if (reason != null)
            {
                LogManager.Log(LogCategory.Action, $"{SkillNames[_pendingSkill]}失败：{reason}");
                return;
            }
        }
        //2026-08-15：按钮主动释放 = 效果级分段执行（每个需选效果单独停一次选择）
        bool done = ally.HasPendingEffectSequence
            ? ally.AdvanceEffectSequence()
            : ally.BeginSkillSelectionSequence(_pendingSkill);
        if (!done && ally.PendingSelectEffect != null)
        {
            // 停在下一个需选效果：重新初始化选择器，继续选择
            InitSelectorForEffect(bm, ally, _pendingSkill, ally.PendingSelectEffect);
            if (!_selector.IsSelectionValid())
            {
                LogManager.Log(LogCategory.Action, $"{SkillNames[_pendingSkill]}失败：下一效果目标区域没有可选目标");
                ally.AbortEffectSequence();
            }
        }
        else if (done)
        {
            LogManager.Log(LogCategory.Select, $"{SkillNames[_pendingSkill]}效果全部施放完成");
        }
    }

    //1234=四槽角色爆发（接口文档：能量符合要求时进入对应爆发目标选择，该角色无需出战，2026-08-14）
    private bool TryEnterBurstBySlot(BattleManager bm, int slot)
    {
        if (bm == null || slot < 0 || slot >= bm.AllySlotCount) return false;
        var ally = bm.GetAllyBySlot(slot);
        if (ally == null || ally.Entity == null || !ally.Entity.IsAlive) return false;
        string reason = ally.GetBlockReason(3);
        if (reason != null)
        {
            LogManager.Log(LogCategory.Action, $"角色{slot +1}爆发不可用：{reason}");
            return false;
        }
        return TryEnterSelecting(bm, 3, false, ally);
    }

    // ================= UI操作接口（UIBattleController 调用，与键盘同一套逻辑） =================

    /// <summary>UI：进入目标选择（0普攻/1重击/2战技/3爆发）。</summary>
    public void UIEnterSelectSkill(int skill)
    {
        var bm = BattleManager.Instance;
        if (bm == null || _selecting) return;
        TryEnterSelecting(bm, skill, false);
    }

    /// <summary>UI：点击槽位能量球/爆发按钮 → 该槽位角色爆发（文档：能量符合要求时进入对应爆发目标选择，该角色无需出战，2026-08-15）。</summary>
    public void UIEnterBurstBySlot(int slot)
    {
        var bm = BattleManager.Instance;
        if (bm == null || _selecting) return;
        TryEnterBurstBySlot(bm, slot);
    }

    /// <summary>UI：选择中按技能键（同一按钮=多段推进，不同按钮=切换技能）。</summary>
    public void UISelectSkillKey(int skill)
    {
        var bm = BattleManager.Instance;
        if (bm == null || !_selecting) return;
        HandleSelectSkillKey(bm, skill);
    }

    /// <summary>UI：确认当前目标选择并施放（2026-08-15 效果级：一个技能可多次确认）。</summary>
    public void UIConfirmSelection()
    {
        var bm = BattleManager.Instance;
        if (bm == null || !_selecting) return;
        bool done = ConfirmAndExecute(bm);
        if (done) ExitSelecting();
    }

    /// <summary>UI：目标选择左右移动（dir=1右 / -1左）。</summary>
    public void UIMoveSelection(int dir)
    {
        if (!_selecting) return;
        if (dir > 0) _selector.MoveRight();
        else _selector.MoveLeft();
    }

    /// <summary>UI：取消目标选择。</summary>
    public void UICancelSelection()
    {
        if (_selecting) ExitSelecting();
    }

    /// <summary>UI：结束回合。</summary>
    public void UIEndTurn()
    {
        var bm = BattleManager.Instance;
        if (bm == null || _selecting) return;
        if (bm.CurrentPhase == TurnPhase.AllyAction) bm.EndAllyTurn();
    }

    /// <summary>UI：让当前出战的倒地角色花费10 AP起身。</summary>
    public bool UIStandUp()
    {
        var bm = BattleManager.Instance;
        return bm != null && !_selecting && bm.TryStandUpActiveAlly();
    }

    /// <summary>UI：让当前出战的冻结角色花费10 AP解冻。</summary>
    public bool UIThaw()
    {
        var bm = BattleManager.Instance;
        return bm != null && !_selecting && bm.TryThawActiveAlly();
    }

    /// <summary>UI：直接切换到指定槽位（slot=0~3），仍遵守普通换人 AP/阵亡免费规则。</summary>
    public void UISwitchCharacter(int slot)
    {
        var bm = BattleManager.Instance;
        if (bm == null || slot < 0 || slot >= bm.AllySlotCount) return;
        if (bm.TrySwitchActiveAllyWithAPCost(slot))
        {
            if (_selecting) ExitSelecting();
            LogManager.Log(LogCategory.UI, $"切换出战角色 -> {slot}");
        }
        else
            LogManager.LogWarning(LogCategory.UI, $"切换出战角色失败 -> {slot}");
    }

    /// <summary>是否处于目标选择阶段（UI遮罩/特效用）。</summary>
    public bool IsSelecting => _selecting;

    /// <summary>当前选择的技能按钮类型（-1=无）。</summary>
    public int PendingSkill => _pendingSkill;

    /// <summary>目标选择器（UI读当前选择组合）。</summary>
    public TargetSelector Selector => _selector;
}
