using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// UI 按键桥接组件：把场景里创建的 UI 按钮绑定到战斗行动上。
///
/// 用法（在 Unity 场景里）：
///  1. 创建一个 UI Button（Canvas 下）
///  2. 在按钮对象上挂本组件（Add Component -> BattleButtonBridge）
///  3. Inspector 里配置：
///       ActionType : 这个按钮代表什么行动（普攻/重击/战技/爆发/结束回合）
///       SlotIndex  : 作用在第几个角色（0=队伍最左，1=左二……；爆发/普攻等都按角色位置）
///  4. 在 Button 的 OnClick 事件里，把本组件拖进去，选择 "OnClick()" 方法
///
/// 说明：只有在我方行动阶段（AllyAction）且角色可行动时，按钮才会真正生效。
/// </summary>
public class BattleButtonBridge : MonoBehaviour
{
    public enum ActionType
    {
        NormalAttack,   // 普攻
        HeavyAttack,    // 重击（蓄力）
        Skill,          // 元素战技
        Burst,          // 元素爆发
        EndTurn         // 结束回合
    }

    [Header("按钮配置")]
    public ActionType ButtonAction = ActionType.NormalAttack;
    [Tooltip("目标角色槽位：0=队伍最左，1=左二……（EndTurn 时忽略）")]
    public int SlotIndex = 0;

    [Header("事件（可选，按钮点击后的额外回调）")]
    public UnityEvent OnActionExecuted;   // 行动成功执行后触发
    public UnityEvent OnActionFailed;     // 行动不可用/失败时触发

    /// <summary>
    /// 按钮 OnClick 绑定的入口方法。
    /// </summary>
    public void OnClick()
    {
        var bm = BattleManager.Instance;
        if (bm == null)
        {
            LogManager.LogWarning(LogCategory.UI, "BattleManager 不存在");
            OnActionFailed?.Invoke();
            return;
        }

        bool ok = false;
        switch (ButtonAction)
        {
            case ActionType.NormalAttack:
                ok = bm.UseNormalAttackBySlot(SlotIndex);
                break;
            case ActionType.HeavyAttack:
                ok = bm.UseHeavyAttackBySlot(SlotIndex);
                break;
            case ActionType.Skill:
                ok = bm.UseSkillBySlot(SlotIndex);
                break;
            case ActionType.Burst:
                ok = bm.UseBurstBySlot(SlotIndex);
                break;
            case ActionType.EndTurn:
                bm.EndAllyTurn();
                ok = true;
                break;
        }

        if (ok)
            OnActionExecuted?.Invoke();
        else
            OnActionFailed?.Invoke();
    }

    /// <summary>
    /// 手动调用（供脚本/键盘输入直接触发，等同 OnClick）。
    /// </summary>
    public void Trigger()
    {
        OnClick();
    }
}
