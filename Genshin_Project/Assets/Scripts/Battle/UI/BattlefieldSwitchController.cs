using System.Linq;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// BattleField1 的换人模态状态。保留技能选目标现场，换人成功后才取消该选择；
/// 普通换人消耗 AP，回合开始时的阵亡换人不可取消且不消耗 AP。
/// </summary>
[DefaultExecutionOrder(-100)]
public sealed class BattlefieldSwitchController : MonoBehaviour
{
    private BattleInputController _input;
    private Button _button;
    private Image _disabledOverlay;
    private bool _switching;
    private bool _forcedDeathSwitch;
    private bool _restoreInputInLateUpdate;
    private bool _inputWasEnabled;
    private int _selectedSlot = -1;

    public bool IsSwitching => _switching;
    public bool IsForcedDeathSwitch => _forcedDeathSwitch;
    public int SelectedSlot => _selectedSlot;

    private void Start()
    {
        CacheControls();
    }

    private void Update()
    {
        CacheInput();
        CacheControls();

        BattleManager battle = BattleManager.Instance;
        if (battle == null || !battle.IsBattleRunning || battle.IsBattleOver)
        {
            if (_switching) CloseSwitch();
            RefreshButton(null);
            return;
        }

        if (!_switching && battle.IsFreeDeathSwitchPending)
        {
            BeginSwitch(true);
            RefreshButton(battle);
            return;
        }

        RefreshButton(battle);
        if (_switching)
        {
            if (Input.GetKeyDown(KeyCode.A)) UIMoveSwitch(-1);
            else if (Input.GetKeyDown(KeyCode.D)) UIMoveSwitch(1);
            else if (Input.GetKeyDown(KeyCode.F) || Input.GetKeyDown(KeyCode.Space)) UIConfirmSwitch();
            else if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.X)) UICancelSwitch();
            return;
        }

        if (Input.GetKeyDown(KeyCode.F)) UIBeginSwitch();
    }

    private void LateUpdate()
    {
        // 确认键包含 Space。延迟恢复输入，避免同一帧又被 BattleInputController
        // 当成“结束回合”处理。
        if (!_restoreInputInLateUpdate) return;
        _restoreInputInLateUpdate = false;
        if (_input != null && _inputWasEnabled) _input.enabled = true;
    }

    private void OnDisable()
    {
        if (_switching) CloseSwitch();
        RestoreInputImmediately();
    }

    /// <summary>UI/F：进入换人；已经在换人时等同确认。</summary>
    public bool UIBeginSwitch()
    {
        return _switching ? UIConfirmSwitch() : BeginSwitch(false);
    }

    /// <summary>UI/A/D：按编队槽位循环，并跳过空位和阵亡角色。</summary>
    public void UIMoveSwitch(int direction)
    {
        BattleManager battle = BattleManager.Instance;
        if (!_switching || battle == null || direction == 0) return;
        _selectedSlot = battle.FindNextLivingAllySlot(_selectedSlot, direction);
    }

    /// <summary>UI/F/Space：确认换人。</summary>
    public bool UIConfirmSwitch()
    {
        BattleManager battle = BattleManager.Instance;
        if (!_switching || battle == null) return false;
        if (!battle.CanSwitchActiveAllyTo(_selectedSlot))
        {
            LogManager.LogWarning(LogCategory.UI, $"切换出战角色失败 -> {_selectedSlot}");
            return false;
        }

        bool freeDeathSwitch = battle.IsFreeDeathSwitchPending;
        if (!battle.TrySwitchActiveAllyWithAPCost(_selectedSlot))
        {
            if (!freeDeathSwitch &&
                (battle.APManager == null || !battle.APManager.CanAfford(BattleManager.SwitchAPCost)))
                LogManager.Log(LogCategory.Action,
                    $"切换角色失败：AP不足（需要{BattleManager.SwitchAPCost}AP）");
            else
                LogManager.LogWarning(LogCategory.UI, $"切换出战角色失败 -> {_selectedSlot}");
            return false;
        }

        if (_input != null && _input.IsSelecting) _input.UICancelSelection();
        LogManager.Log(LogCategory.UI,
            $"切换出战角色 -> {_selectedSlot}{(freeDeathSwitch ? "（阵亡免费换人）" : string.Empty)}");
        CloseSwitch();
        return true;
    }

    /// <summary>UI/X/Esc：取消普通换人；阵亡强制换人不可取消。</summary>
    public void UICancelSwitch()
    {
        if (!_switching || _forcedDeathSwitch ||
            (BattleManager.Instance != null && BattleManager.Instance.IsFreeDeathSwitchPending))
            return;
        CloseSwitch();
    }

    private bool BeginSwitch(bool forcedDeathSwitch)
    {
        BattleManager battle = BattleManager.Instance;
        if (battle == null || !battle.CanSwitchActiveAlly()) return false;

        if (_restoreInputInLateUpdate) RestoreInputImmediately();
        CacheInput();
        _inputWasEnabled = _input != null && _input.enabled;
        if (_input != null) _input.enabled = false;
        _restoreInputInLateUpdate = false;
        _switching = true;
        _forcedDeathSwitch = forcedDeathSwitch || battle.IsFreeDeathSwitchPending;

        CharacterBattleController active = battle.GetActiveAlly();
        _selectedSlot = active != null && active.Entity != null
            ? Mathf.Clamp(active.Entity.SlotPosition - 1, 0, Mathf.Max(0, battle.AllySlotCount - 1))
            : 0;

        if (_forcedDeathSwitch)
        {
            for (int slot = 0; slot < battle.AllySlotCount; slot++)
            {
                if (!battle.CanSwitchActiveAllyTo(slot)) continue;
                _selectedSlot = slot;
                break;
            }
        }

        LogManager.Log(LogCategory.UI,
            $"进入{(_forcedDeathSwitch ? "免费强制" : string.Empty)}换人界面，当前选中 {_selectedSlot}");
        return true;
    }

    private void CloseSwitch()
    {
        _switching = false;
        _forcedDeathSwitch = false;
        _selectedSlot = -1;
        _restoreInputInLateUpdate = true;
    }

    private void RestoreInputImmediately()
    {
        _restoreInputInLateUpdate = false;
        if (_input != null && _inputWasEnabled) _input.enabled = true;
    }

    private void CacheInput()
    {
        if (_input == null) _input = FindObjectOfType<BattleInputController>();
    }

    private void CacheControls()
    {
        if (_button != null) return;
        PsdUiBinding icon = FindObjectsOfType<PsdUiBinding>(true)
            .FirstOrDefault(binding => binding.bindingKey == "SwitchIcon");
        if (icon == null || icon.transform.parent == null) return;

        GameObject hitArea = icon.transform.parent.gameObject;
        Image hitGraphic = hitArea.GetComponent<Image>() ?? hitArea.AddComponent<Image>();
        hitGraphic.color = Color.clear;
        hitGraphic.raycastTarget = true;
        _button = hitArea.GetComponent<Button>() ?? hitArea.AddComponent<Button>();
        _button.transition = Selectable.Transition.None;
        _button.onClick.AddListener(() => UIBeginSwitch());

        PsdUiBinding overlay = hitArea.GetComponentsInChildren<PsdUiBinding>(true)
            .FirstOrDefault(binding => binding.bindingKey == "DisabledOverlay");
        _disabledOverlay = overlay != null
            ? overlay.GetComponent<Image>() ?? overlay.GetComponentInChildren<Image>(true)
            : null;
    }

    private void RefreshButton(BattleManager battle)
    {
        if (_button == null) return;
        bool usable = _switching || (battle != null && battle.CanSwitchActiveAlly());
        _button.interactable = usable;
        if (_disabledOverlay != null) _disabledOverlay.enabled = !usable;
    }
}
