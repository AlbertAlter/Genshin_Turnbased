using System;
using TMPro;
using UnityEngine;

/// <summary>独立角色/背包场景。流程宿主也可将同一个视图绑定到自己的会话。</summary>
public sealed class RoguelikeRosterSceneEntry : MonoBehaviour
{
    [SerializeField] private ProjectUiCanvas canvasPrefab;
    [SerializeField] private RoguelikeRosterView view;
    [SerializeField] private UICardFactory cards;
    [SerializeField] private UIAdaptiveFrame framePrefab;
    [SerializeField] private TMP_FontAsset font;
    [SerializeField] private bool openInventory;
    [SerializeField] private bool openFormation;
    [SerializeField] private bool useTestSave;
    [SerializeField] private Vector2 closeButtonInset = new Vector2(30, 25);

    private void Start()
    {
        ProjectUiCanvas canvas = null;
        var campaign = RoguelikeCampaignHost.Current;
        try
        {
            if (canvasPrefab == null || canvasPrefab.ContentFrame == null || view == null || cards == null)
                throw new InvalidOperationException("角色场景缺少项目 Canvas、内容框、角色视图或卡牌工厂。");
            canvas = Instantiate(canvasPrefab);
            Action leave = campaign != null
                ? campaign.AttachNavigation(canvas.ContentFrame, () => view.Ready, view.TryNavigateBack, () => view.ShowPageExit, closeButtonInset)
                : RoguelikeCampaignHost.AttachStandaloneNavigation(canvas.ContentFrame, view.TryNavigateBack, () => view.ShowPageExit, closeButtonInset);
            var data = DataManager.GetOrCreateLoaded();
            if (campaign != null)
            {
                view.SetEnglish(campaign.English);
                cards.BindAppearance(campaign.Game.Appearance);
                view.Bind(canvas.ContentFrame, new RoguelikeRosterSession(data, campaign.Game.Flow, new RoguelikeSavedUpgradeGate()), cards, font, framePrefab);
                campaign.OpenRoster(view);
                return;
            }
            var regularPaths = new RoguelikeSavePaths();
            var runPaths = useTestSave ? new RoguelikeSavePaths(regularPaths.RootDirectory + "_RosterTest") : regularPaths;
            var saves = new RoguelikeSaveService(new RoguelikeSaveRepository(runPaths));
            var flow = new RoguelikeFlowService(saves, RosterOnlyRoutes.Instance);
            var appearance = new PlayerAppearanceService(useTestSave ? new RoguelikeSaveRepository(regularPaths) : saves.Repository,
                CardBackCatalog.FromStreamingAssets());
            if (appearance.Refresh().Succeeded) cards.BindAppearance(appearance);
            view.Bind(canvas.ContentFrame, new RoguelikeRosterSession(data, flow, new RoguelikeSavedUpgradeGate()),
                cards, font, framePrefab);
            if (openFormation) view.ShowFormation(cancelled: leave);
            else if (openInventory) view.ShowInventory();
        }
        catch (Exception error)
        {
            Debug.LogException(error, this);
            if (campaign != null) campaign.ReportBindingFailure(error);
            else if (view != null && canvas != null) view.ShowBindingError(canvas.ContentFrame, font, "角色与背包加载失败。\n" + error.Message);
        }
    }

    private sealed class RosterOnlyRoutes : IRoguelikeRouteRules
    {
        public static readonly RosterOnlyRoutes Instance = new();
        public RoguelikeSaveResult ValidateNodeEntry(RoguelikeProgressSnapshot snapshot, RoguelikeNodeSelection node)
            => Unavailable();
        public RoguelikeSaveResult ValidateChapterCompletion(RoguelikeProgressSnapshot snapshot) => Unavailable();
        private static RoguelikeSaveResult Unavailable()
            => RoguelikeSaveResult.Failure(RoguelikeSaveErrorCode.InvalidOperation, "请回到路线界面操作。");
    }
}
