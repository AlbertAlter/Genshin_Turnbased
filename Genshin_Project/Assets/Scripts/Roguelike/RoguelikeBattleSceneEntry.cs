using UnityEngine;
using System.Collections;

/// <summary>Explicit campaign battle binding. Independently opened scenes retain their authored test entry.</summary>
public sealed class RoguelikeBattleSceneEntry : MonoBehaviour
{
    internal static RoguelikeBattleSceneEntry Current { get; private set; }
    [SerializeField] internal BattleSceneRuntime runtime;
    [SerializeField] internal RectTransform navigationMount;
    [SerializeField] internal BattleEventConditionView conditionsPrefab;
    [SerializeField, Tooltip("仅独立战斗预览使用；留空不显示，填写条件Lang key可检查布局。")]
    private string previewConditionKey;

    private IEnumerator Start()
    {
        if (RoguelikeCampaignHost.Current != null || conditionsPrefab == null || string.IsNullOrWhiteSpace(previewConditionKey)) yield break;
        while (DataManager.Instance?.IsLoaded != true) yield return null;
        var data = DataManager.Instance; bool english = ProjectUserSettings.English();
        var describe = BattleEventConditionText.Provider(data, previewConditionKey,
            () => ProjectUserSettings.English(english), battle: runtime != null ? runtime.GetComponent<BattleManager>() : null);
        string body = describe();
        if (body.Trim() == data.GetLangText("ui_event_condition_none", english).Trim()) yield break;
        var popup = Instantiate(conditionsPrefab, navigationMount, false);
        var input = runtime != null ? runtime.GetComponent<BattleInputController>() : null;
        var pause = runtime != null ? runtime.GetComponent<BattleUiPauseMenu>() : null;
        popup.Bind(data.GetLangText("ui_battle_event_conditions", english), body,
            () => input?.IsUiInputBlocked != true && input?.IsSelecting != true && pause?.IsOpen != true, describe);
    }
    private void Awake()
    {
        Current = this;
        if (RoguelikeCampaignHost.Current != null && runtime != null) runtime.enabled = false;
    }
    private void OnDestroy() { if (Current == this) Current = null; }
}
