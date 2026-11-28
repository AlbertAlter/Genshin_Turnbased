using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>绑定状态图标与其真实实例的详情；生命周期随图标池中的当前绑定。</summary>
public sealed class BattleStatusDescriptionView : MonoBehaviour, IPointerClickHandler
{
    private const string ViewResource = "UI/BattleSkillInfo";

    private StatusInstance status;
    private object host;
    private BattleManager sourceBattle;
    private bool cardObject;
    private BattleInputController input;
    private Canvas canvas;
    private RectTransform overlay;
    private BattleSkillInfoView view;
    private BattleInfoPopupStyle style;
    private bool open;
    private bool langError;
    private string shownBody, shownSummary;

    public void Bind(StatusInstance value, BattleEntity statusHost, BattleInputController battleInput)
    {
        Rebind(value, statusHost, battleInput, null, false);
    }

    /// <summary>卡底对象已由专用表选出，允许隐藏的场地状态；详情仍读取真实实例及其原宿主。</summary>
    public void BindCardObject(StatusInstance value, object statusHost, BattleInputController battleInput, BattleManager battle)
        => Rebind(value, statusHost, battleInput, battle, true);

    private void Rebind(StatusInstance value, object statusHost, BattleInputController battleInput, BattleManager battle, bool isCardObject)
    {
        if (!ReferenceEquals(status, value) || !ReferenceEquals(host, statusHost) || input != battleInput
            || sourceBattle != battle || cardObject != isCardObject) Close();
        status = value;
        host = statusHost;
        input = battleInput;
        sourceBattle = battle; cardObject = isCardObject;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left || eventData.dragging || open
            || input?.IsUiInputBlocked == true || !CanShow()) return;
        canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;
        var prefab = Resources.Load<GameObject>(ViewResource)?.GetComponent<BattleSkillInfoView>();
        if (prefab == null)
        {
            Debug.LogError("[UI] 状态详情缺少共用弹窗预制件：" + ViewResource, this);
            return;
        }

        style = BattleInfoPopupStyle.Load();
        overlay = BattleSkillInfoView.CreateDismissOverlay(canvas, "StatusInfoOverlay", Close);
        view = Instantiate(prefab, overlay, false);
        view.name = "StatusInstanceInfo";
        Refresh(true);
        input?.SetUiInputBlocked(this, true);
        open = true;
        eventData.Use();
    }

    private void LateUpdate()
    {
        if (!open) return;
        if (canvas == null || !canvas.isActiveAndEnabled || view == null || !CanShow()
            || Input.GetKeyDown(KeyCode.Escape) || ProjectKeyBindings.Down(BattleShortcut.Cancel))
        { Close(); return; }
        Refresh(false);
    }

    private void OnDisable() => Close();
    private void OnApplicationFocus(bool focused) { if (!focused) Close(); }

    private bool CanShow()
    {
        if (!isActiveAndEnabled || status?.IsActive != true || status.MainData == null
            || (!cardObject && status.MainData.Display != 1)) return false;
        var battle = sourceBattle != null ? sourceBattle : BattleManager.Instance;
        if (battle == null || !battle.IsBattleRunning || battle.IsBattleOver) return false;
        if (host is BattleEntity entity)
            return entity.IsAlive && entity.IsOnField && entity.HasStatusInstance(status);
        return cardObject && host is FieldPosition field
            && battle.Field?.GetSlot(field.Side, field.SlotIndex) == field && field.StatusList.Contains(status);
    }

    internal static bool ShowsCount(StatusInstance status)
    {
        if (!CountStatusSystem.IsCount(status.MainData)) return false;
        switch (status.StatusID2)
        {
            case XingqiuScriptHooks.Rainbow:
            case WidsithWeaponCount.ID:
            // 这些Count保存增益值而非层数，实际增益已显示在正文。
            case "ST_Weapon_4309102": case "ST_Weapon_4309502": case "ST_Weapon_4309602":
            case "ST_Weapon_4310002": case "ST_Weapon_4310902": case "ST_Weapon_4313402":
            case "ST_Weapon_4314604": case "ST_Weapon_4321203":
                return false;
            default: return true;
        }
    }

    private static string CountLabel(string id)
    {
        switch (id)
        {
            case YanfeiScriptHooks.Brilliance: return "累计AP";
            case SeaHuedClamArtifact.BubbleID: case SongOfDaysPastArtifact.RecordID: return "累计治疗";
            case SongOfDaysPastArtifact.WaveID: return "记录治疗";
            case "ST_Weapon_4304903": case "ST_Weapon_4309402": case "ST_Weapon_4316203":
            case "ST_Weapon_4317103": case "ST_Weapon_4318703": return "剩余次数";
            default: return "层数";
        }
    }

    private void Close()
    {
        if (open && input != null) input.SetUiInputBlocked(this, false);
        open = false;
        if (overlay != null)
        {
            overlay.gameObject.SetActive(false);
            Destroy(overlay.gameObject);
        }
        overlay = null;
        view = null;
        langError = false;
        shownBody = shownSummary = null;
    }

    private void Refresh(bool first)
    {
        string summary = status.IsPermanent ? "剩余回合数：不限" : $"剩余回合数：{status.RemainingPhaseCount}";
        if (ShowsCount(status))
        {
            string count = status.HasIntegerRuntimeValue ? status.RuntimeIntValue.ToString(CultureInfo.InvariantCulture)
                : status.RuntimeValue.ToString("0.##", CultureInfo.InvariantCulture);
            summary += "   " + CountLabel(status.StatusID2) + "：" + count;
        }
        string langBody = null;
        if (!langError)
        {
            try { langBody = DataManager.Instance?.GetStatusIconDescriptionText(status, host, ProjectUserSettings.English()); }
            catch (FormatException error)
            {
                Debug.LogError(error.Message, this);
                langError = true;
            }
        }
        string body = langBody ?? status.GetDescription(host);
        bool changed = first || shownSummary != summary || shownBody != body;
        if (changed)
        {
            view.ShowStatusInstance(status.MainData.StatusName, summary, body, langBody != null);
            shownSummary = summary;
            shownBody = body;
        }

        view.LayoutBelowAnchor((RectTransform)canvas.transform, (RectTransform)transform, style, changed, first);
    }
}
