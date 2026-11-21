using System;
using UnityEngine;

/// <summary>Mounts the reusable shop on the project canvas and binds the active roguelike run.</summary>
[DisallowMultipleComponent]
public sealed class RoguelikeShopSceneEntry : MonoBehaviour
{
    [SerializeField] private ProjectUiCanvas canvasPrefab;
    [SerializeField] private RoguelikeShopView view;
    [SerializeField] private Vector2 closeButtonInset = new Vector2(34, 84);

    private void Start()
    {
        var campaign = RoguelikeCampaignHost.Current;
        try
        {
            if (canvasPrefab == null || canvasPrefab.ContentFrame == null || view == null)
                throw new InvalidOperationException("商店场景缺少项目 Canvas、内容框或商店视图。");
            var canvas = Instantiate(canvasPrefab);
            view.transform.SetParent(canvas.ContentFrame, false);
            if (campaign != null)
                campaign.AttachNavigation(canvas.ContentFrame, () => view.Ready, view.TryNavigateBack, buttonInset: closeButtonInset);
            else
                RoguelikeCampaignHost.AttachStandaloneNavigation(canvas.ContentFrame, view.TryNavigateBack, buttonInset: closeButtonInset);
            var data = DataManager.GetOrCreateLoaded();
            if (campaign != null)
            {
                view.SetEnglish(campaign.English);
                var active = campaign.Game;
                view.Bind(active, data, active.Items);
                return;
            }
            var items = data.GetItemCatalog();
            var saves = new RoguelikeSaveService(new RoguelikeSaveRepository(new RoguelikeSavePaths()));
            var game = new RoguelikeGameService(saves, ShopOnlyRoutes.Instance,
                new DataManagerRoguelikeEquipmentCatalog(data), items, new DataManagerRoguelikeCharacterAcquisitionCatalog(data));
            view.Bind(game, data, items);
        }
        catch (Exception error)
        {
            Debug.LogException(error, this);
            if (campaign != null) campaign.ReportBindingFailure(error);
            else if (view != null) view.ShowBindingError(error.Message);
        }
    }

    private sealed class ShopOnlyRoutes : IRoguelikeRouteRules
    {
        internal static readonly ShopOnlyRoutes Instance = new();

        public RoguelikeSaveResult ValidateNodeEntry(RoguelikeProgressSnapshot snapshot, RoguelikeNodeSelection node)
            => Unavailable();

        public RoguelikeSaveResult ValidateChapterCompletion(RoguelikeProgressSnapshot snapshot) => Unavailable();

        private static RoguelikeSaveResult Unavailable()
            => RoguelikeSaveResult.Failure(RoguelikeSaveErrorCode.InvalidOperation, "请回到路线界面操作。");
    }
}
