using System;
using UnityEngine;

/// <summary>Standalone preview entry. Binds only the settings module and the shared project canvas.</summary>
public sealed class ProjectSettingsSceneEntry : MonoBehaviour
{
    public ProjectUiCanvas canvasPrefab;
    public ProjectSettingsView view;
    public ProjectSettingsView viewPrefab;
    public Camera backgroundCamera;
    private void Start()
    {
        ProjectUiCanvas canvas = null;
        try
        {
            canvas = Instantiate(canvasPrefab);
            ProjectUiCanvas.EnsureBackgroundViewport(backgroundCamera);
            RoguelikeCampaignHost.AttachStandaloneNavigation(canvas.ContentFrame);
            DataManager.GetOrCreateLoaded();
            if (view == null && viewPrefab != null) view = Instantiate(viewPrefab);
            view.Bind(canvas.ContentFrame);
        }
        catch (Exception error)
        {
            Debug.LogException(error, this);
            if (canvas != null && view != null)
            {
                var page = new RoguelikeCampaignScreen(canvas.ContentFrame, view.font, action => action(), view.framePrefab);
                page.Page(ProjectUserSettings.English() ? "Settings could not be loaded." : "设置加载失败。");
                page.Notice(error.Message);
            }
        }
    }
}
