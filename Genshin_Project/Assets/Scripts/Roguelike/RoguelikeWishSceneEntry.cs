using System;
using UnityEngine;

/// <summary>独立祈愿场景绑定当前肉鸽档。流程宿主也可以直接绑定同一视图。</summary>
public sealed class RoguelikeWishSceneEntry : MonoBehaviour
{
    [SerializeField] private RoguelikeWishView view;
    [SerializeField] private Camera displayCamera;
    [SerializeField] private Vector2 closeButtonInset = new Vector2(34, 59);

    private void Awake()
    {
        var canvas = view != null ? view.GetComponentInParent<Canvas>() : null;
        if (canvas == null) return;
        canvas = canvas.rootCanvas;
        var camera = displayCamera != null ? displayCamera : canvas.worldCamera;

        // Keep the backdrop camera separate from UGUI rendering and pointer coordinates.
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.worldCamera = null;
        if (camera == null || !camera.gameObject.activeInHierarchy)
        {
            // The editor can enter Play Mode from an older, already-open copy of the scene.
            camera = gameObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(113, 103, 88, 255);
            camera.orthographic = true;
            camera.depth = -1f;
            camera.cullingMask = LayerMask.GetMask("Default", "UI");
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.stereoTargetEye = StereoTargetEyeMask.None;
        }

        camera.targetTexture = null;
        camera.targetDisplay = 0;
        camera.enabled = true;
    }

    private void Start()
    {
        var campaign = RoguelikeCampaignHost.Current;
        try
        {
            if (view == null || view.GetComponentInParent<Canvas>() == null)
                throw new InvalidOperationException("祈愿场景缺少祈愿视图或 Canvas。");
            var root = (RectTransform)view.transform;
            if (campaign != null)
                campaign.AttachNavigation(root, () => view.Ready, view.TryNavigateBack, () => view.ShowPageExit, closeButtonInset);
            else
                RoguelikeCampaignHost.AttachStandaloneNavigation(root, view.TryNavigateBack, () => view.ShowPageExit, closeButtonInset);
            var data = DataManager.GetOrCreateLoaded();
            view.Bind(campaign?.Game ?? RoguelikeGameService.CreateFromStreamingAssets(data, WishOnlyRoutes.Instance), data);
            if (campaign != null) view.SetEnglish(campaign.English);
        }
        catch (Exception error)
        {
            Debug.LogException(error, this);
            if (campaign != null) campaign.ReportBindingFailure(error);
            else if (view != null) view.ShowBindingError();
        }
    }

    private sealed class WishOnlyRoutes : IRoguelikeRouteRules
    {
        public static readonly WishOnlyRoutes Instance = new WishOnlyRoutes();

        public RoguelikeSaveResult ValidateNodeEntry(RoguelikeProgressSnapshot snapshot, RoguelikeNodeSelection node)
            => Unavailable();

        public RoguelikeSaveResult ValidateChapterCompletion(RoguelikeProgressSnapshot snapshot)
            => Unavailable();

        private static RoguelikeSaveResult Unavailable()
            => RoguelikeSaveResult.Failure(RoguelikeSaveErrorCode.InvalidOperation, "请回到路线界面操作。");
    }
}
