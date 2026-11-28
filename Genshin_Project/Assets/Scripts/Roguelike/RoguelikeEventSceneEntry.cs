using System;
using UnityEngine;

/// <summary>主流程绑定真实事件；独立打开同场景只读预览配置，不创建HTML测试档或修改进行中的存档。</summary>
[DisallowMultipleComponent]
public sealed class RoguelikeEventSceneEntry : MonoBehaviour
{
    [SerializeField] private ProjectUiCanvas canvasPrefab;
    [SerializeField] private RoguelikeEventView view;
    [SerializeField] private string previewEventId = "RE101";
    [SerializeField, Min(1)] private int previewLevel = 12;
    [SerializeField] private Vector2 closeButtonInset = new Vector2(45, 62);

    private void Start()
    {
        var campaign = RoguelikeCampaignHost.Current;
        try
        {
            if (canvasPrefab == null || view == null) throw new InvalidOperationException("事件场景缺少Canvas或视图引用。");
            var canvas = Instantiate(canvasPrefab);
            view.transform.SetParent(canvas.ContentFrame, false);
            if (campaign != null)
            {
                campaign.AttachNavigation(canvas.ContentFrame, () => view.Ready,
                    buttonInset: closeButtonInset, onLeave: campaign.BackFromEvent);
                campaign.OpenEvent(view);
            }
            else
            {
                Action leave = RoguelikeCampaignHost.AttachStandaloneNavigation(canvas.ContentFrame, buttonInset: closeButtonInset);
                var result = RoguelikeNodeContentCatalog.LoadFromStreamingAssets().PreviewEvent(previewEventId);
                if (!result.Succeeded) throw new InvalidOperationException(result.Message);
                view.Bind(DataManager.GetOrCreateLoaded(), result.Data, 1, previewLevel,
                    ProjectUserSettings.English(false), null, leave, preview: true);
            }
        }
        catch (Exception error)
        {
            Debug.LogException(error, this);
            if (campaign != null) campaign.ReportBindingFailure(error);
            else if (view != null) view.ShowBindingError(error.Message);
        }
    }
}
