using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

public sealed partial class RoguelikeCampaignHost : MonoBehaviour
{
    public static RoguelikeCampaignHost Current { get; private set; }
    internal RoguelikeGameService Game { get; private set; }
    internal bool English => english;
    [SerializeField] private ProjectUiCanvas canvasPrefab;
    [SerializeField] private TMP_FontAsset font;
    [SerializeField] private UIAdaptiveFrame framePrefab;
    [SerializeField] private UIRewardPopup rewardPopupPrefab;
    [SerializeField] private bool english;
    [SerializeField] private string battleScene, rosterScene, wishScene, shopScene;
    private enum RosterPage { Characters, Bag, Formation, BeforeBattle }
    private RosterPage rosterPage;
    private RoguelikePlayableRoutes routes;
    private string home, loaded;
    private bool menu = true, loading;
    private bool routeOverview, handlingSceneReady;
    private string queuedScene;
    private RoguelikeBattleAdapter battle;
    private RoguelikeBattleOutcome outcome;
    private string failure;
    private RoguelikeCampaignScreen screen;
    private UIPageTransition transition;
    private Func<bool> destinationReady;

    private void Awake()
    {
        if (Current != null) { Destroy(gameObject); return; }
        Current = this; home = gameObject.scene.name;
        DontDestroyOnLoad(gameObject); SceneManager.sceneLoaded += SceneReady;
        transition = UIPageTransition.Create(canvasPrefab, transform, true);
    }
    private void Start()
    {
        // The editor's initial play scene may not emit sceneLoaded after this host subscribes.
        if (loaded == null) SceneReady(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        loading = true; StartCoroutine(RevealScene());
    }
    private void OnDestroy()
    {
        if (Current != this) return;
        SceneManager.sceneLoaded -= SceneReady; routes?.ReleaseArt(); CampaignUiTables.Clear(); ReleaseBattle(); ReleaseRewardPresentation(); Current = null;
    }
    private string L(string key, params object[] args) => DataManager.Instance.GetLangText(key, english, args);
    private void SceneReady(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single) return;
        ReleaseRewardPresentation();
        loaded = scene.name; failure = null; screen = null; destinationReady = null;
        handlingSceneReady = true;
        try
        {
            if (loaded == home)
            {
                var canvas = Instantiate(canvasPrefab);
                screen = new RoguelikeCampaignScreen(canvas.ContentFrame, font, Act, framePrefab);
                if (rewardPopupPrefab != null)
                {
                    rewardPopup = Instantiate(rewardPopupPrefab, canvas.ContentFrame, false);
                    rewardPopup.Failed += RewardPresentationFailed;
                }
                Game ??= RoguelikeGameService.CreateCampaign(DataManager.GetOrCreateLoaded(), out routes);
                if (menu) Menu(); else Progress();
                destinationReady = () => screen != null && screen.Ready;
                UIPageNavigation.Create(canvas.ContentFrame, () => Act(() => { if (menu) ExitGame(); else ExitOptions(); }),
                    () => rewardPopup?.TryNavigateBack() == true || screen.TryNavigateBack(), () => loading || rewardsWaiting || failure != null, () => false);
            }
            else if (loaded == battleScene)
            {
                var binding = RoguelikeBattleSceneEntry.Current;
                if (binding == null || binding.runtime == null || binding.navigationMount == null)
                    throw new InvalidOperationException("战斗场景未显式绑定流程入口和内容框。");
                screen = new RoguelikeCampaignScreen(binding.navigationMount, font, Act, framePrefab);
                var runtime = binding.runtime; runtime.enabled = false;
                runtime.characterDetails?.SetEnglish(english);
                battle = Game.CreateBattleAdapter(runtime); battle.Ended += Ended;
                Check(battle.TryStart(Read()));
                destinationReady = () => outcome != null || (BattleManager.Instance != null && BattleManager.Instance.IsAwaitingAllyInput);
                BattleControls();
            }
        }
        catch (Exception error)
        {
            failure = error.Message;
            Debug.LogException(error, this);
        }
        finally { handlingSceneReady = false; }
    }
    internal void ReportBindingFailure(Exception error) => failure = error.Message;
    internal Action AttachNavigation(RectTransform contentFrame, Func<bool> ready, Func<bool> tryBack = null,
        Func<bool> showExit = null, Vector2? buttonInset = null)
    {
        screen = new RoguelikeCampaignScreen(contentFrame, font, Act, framePrefab);
        Action leave = () => Act(Home);
        UIPageNavigation.Create(contentFrame, leave, tryBack, () => loading || failure != null, showExit, buttonInset);
        destinationReady = ready; return leave;
    }
    internal static Action AttachStandaloneNavigation(RectTransform contentFrame, Func<bool> tryBack = null,
        Func<bool> showExit = null, Vector2? buttonInset = null)
    {
        bool leaving = false;
        void Leave()
        {
            if (leaving) return;
            leaving = true;
            SceneManager.LoadSceneAsync(0);
        }
        UIPageNavigation.Create(contentFrame, Leave, tryBack, () => leaving, showExit, buttonInset);
        return Leave;
    }
    private void Act(Action command)
    {
        if (loading || rewardsWaiting || rewardPopup?.IsOpen == true) return;
        try { command(); }
        catch (Exception error) { Debug.LogException(error, this); screen?.Notice(error.Message); }
    }
    private static void Check(RoguelikeSaveResult result)
    {
        if (!result.Succeeded) throw new InvalidOperationException(result.Message, result.Exception);
    }
    private RoguelikeFlowContext Read() { var result = Game.Continue(); Check(result); return result.Data; }
    private void Go(string scene)
    {
        if (loading) return;
        if (!Application.CanStreamedLevelBeLoaded(scene)) throw new InvalidOperationException("构建未包含场景：" + scene);
        loading = true; StartCoroutine(ChangeScene(scene));
    }
    private IEnumerator ChangeScene(string scene)
    {
        yield return transition.Fade(true);
        yield return LoadCoveredScene(scene);
        yield return RevealScene();
    }
    private IEnumerator LoadCoveredScene(string scene)
    {
        ReleaseBattle(); destinationReady = null;
        yield return SceneManager.LoadSceneAsync(scene);
    }
    private IEnumerator RevealScene()
    {
        // SceneReady may immediately redirect the still-covered home scene to battle preparation.
        while (true)
        {
            float until = Time.realtimeSinceStartup + 25;
            while (failure == null && queuedScene == null)
            {
                try { if (destinationReady?.Invoke() == true) break; }
                catch (Exception error) { ReportBindingFailure(error); Debug.LogException(error, this); break; }
                if (Time.realtimeSinceStartup >= until)
                {
                    failure = english ? "Scene loading timed out: " + loaded : "场景加载超时：" + loaded;
                    Debug.LogWarning(failure, this);
                    break;
                }
                yield return null;
            }
            if (failure != null || queuedScene == null)
            {
                // Give the visible destination its first complete render under the cover.
                Canvas.ForceUpdateCanvases(); yield return null; yield return null;
            }
            string next = queuedScene; queuedScene = null;
            if (failure != null || next == null) break;
            if (!Application.CanStreamedLevelBeLoaded(next)) { failure = "构建未包含场景：" + next; break; }
            yield return LoadCoveredScene(next);
        }
        if (failure != null) ShowSceneFailure();
        yield return transition.Fade(false); loading = false;
    }

    private void ShowSceneFailure()
    {
        ReleaseRewardPresentation();
        // The destination may have failed before creating its own canvas or navigation.
        var canvas = Instantiate(canvasPrefab);
        canvas.UseFullScreenAtReferenceHeight();
        canvas.GetComponent<Canvas>().sortingOrder = transition.GetComponentInParent<Canvas>().sortingOrder - 1;
        var errorPage = new RoguelikeCampaignScreen(canvas.ContentFrame, font, Act, framePrefab);
        errorPage.Page(failure);
        errorPage.Root.GetComponent<UnityEngine.UI.Image>().raycastTarget = true;
        bool toMenu = loaded == home || Game == null;
        void Leave()
        {
            routeOverview = !toMenu;
            menu = toMenu;
            Go(home);
        }
        string key = toMenu ? "btn_main_back_to_menu" : "btn_main_back_to_route";
        string label = DataManager.Instance?.LangDict.ContainsKey(key) == true ? L(key) :
            toMenu ? (english ? "Back to Main Menu" : "返回主菜单") : (english ? "Back to Route" : "返回路线");
        errorPage.Choice(label, Leave);
        UIPageNavigation.Create(canvas.ContentFrame, () => Act(Leave), blocked: () => loading, showButton: () => false);
    }
    private void Home() { menu = false; Go(home); }
    private void ReleaseBattle()
    {
        if (battle != null) { battle.Ended -= Ended; battle.Dispose(); battle = null; }
        outcome = null;
    }
    private void Progress()
    {
        var context = Read(); var step = context.Flow;
        if (step.Step == RoguelikeFlowStep.Node && step.Node.Kind != RoguelikeNodeKind.Event && !routeOverview)
        { Roster(RosterPage.BeforeBattle); return; }
        screen.Page(L("btn_main_chapter", context.Snapshot.CurrentChapterId));
        screen.Back(L("btn_main_exit"), ExitOptions);
        switch (step.Step)
        {
            case RoguelikeFlowStep.Route:
            case RoguelikeFlowStep.Node when routeOverview:
                screen.RoutePage();
                screen.RouteNavigation(context.Snapshot.Inventory.Items.Find(item => item.ItemId == RoguelikeWishService.FateItemId)?.Quantity ?? 0,
                    new[] { L("btn_main_exit"), L("ui_wish_title"), L("btn_main_shop"), L("btn_characters"), L("btn_inventory"), L("btn_main_party_setup") },
                    ExitOptions, () => Go(wishScene), () => Go(shopScene), () => Roster(RosterPage.Characters),
                    () => Roster(RosterPage.Bag), () => Roster(RosterPage.Formation));
                screen.Map(routes, context.Snapshot, node =>
                {
                    if (step.Step == RoguelikeFlowStep.Route)
                        Check(Game.EnterNode(step.StepId, new RoguelikeNodeSelection { NodeId = RoguelikePlayableRoutes.Key(node),
                            Kind = (RoguelikeNodeKind)Enum.Parse(typeof(RoguelikeNodeKind), node.Type.ToString()), StageIndex = node.StageIndex }));
                    routeOverview = false;
                    Progress();
                });
                if (step.Step == RoguelikeFlowStep.Route && routes.ValidateChapterCompletion(context.Snapshot).Succeeded)
                    screen.Choice(L("btn_main_complete_chapter"), () => { Check(Game.CompleteChapter(step.StepId)); Progress(); });
                break;
            case RoguelikeFlowStep.Node:
                if (step.Node.Kind == RoguelikeNodeKind.Event) { screen.Notice(L("tip_main_event_unavailable")); break; }
                Roster(RosterPage.BeforeBattle); break;
            case RoguelikeFlowStep.Battle:
                screen.Choice(L("btn_main_resume_battle"), () => Go(battleScene)); break;
            case RoguelikeFlowStep.Settlement:
                if (context.Snapshot.PendingRewardData != null) { screen.Notice(L("tip_main_reward_unavailable")); break; }
                // 本场胜利已持久化。重启后同样走此入口，失败则保留待发清单并允许重试。
                var returned = Game.ReturnToRoute(step.StepId);
                if (!returned.Succeeded)
                {
                    screen.Notice(returned.Message);
                    screen.Choice(L("btn_main_retry_save"), Progress);
                    screen.Choice(L("btn_inventory"), () => Roster(RosterPage.Bag));
                    break;
                }
                Progress();
                PresentNodeRewards(context.Snapshot.PendingNodeReward?.Items);
                break;
            case RoguelikeFlowStep.ChapterCompleted: Slots(true, context.Snapshot.CurrentChapterId + 1); break;
        }
    }
    private void Roster(RosterPage page)
    {
        rosterPage = page;
        if (loading || handlingSceneReady) queuedScene = rosterScene;
        else Go(rosterScene);
    }
    private void BackFromPreBattle() { routeOverview = true; Home(); }
    internal void OpenRoster(RoguelikeRosterView view)
    {
        if (rosterPage == RosterPage.Bag) view.ShowInventory();
        else if (rosterPage == RosterPage.Formation) view.ShowFormation(null, Home, Home);
        else if (rosterPage == RosterPage.BeforeBattle)
        {
            var original = Read();
            view.ShowFormation(original.Flow.Node.ContentId, () => Act(() =>
            {
                var current = Read();
                if (current.RunId != original.RunId || current.Flow.Step != RoguelikeFlowStep.Node ||
                    current.Flow.Node.NodeId != original.Flow.Node.NodeId) throw new InvalidOperationException(L("tip_main_progress_changed"));
                Check(Game.BeginBattle(current.Flow.StepId, Guid.NewGuid().ToString("N"))); Go(battleScene);
            }), BackFromPreBattle);
        }
        else view.ShowCharacterList();
    }
    private void Ended(RoguelikeBattleOutcome result) { outcome = result; if (screen != null) BattleControls(); }
    private void BattleControls()
    {
        screen.Clear();
        if (failure != null) screen.Notice(failure);
        if (outcome?.Victory == true && !outcome.ProgressCommit.Succeeded)
        {
            screen.Notice(outcome.ProgressCommit.Message);
            screen.Choice(L("btn_main_retry_save"), () => { Check(battle.RetryVictoryCommit()); Home(); });
        }
        else if (outcome != null)
        {
            if (!outcome.Victory) screen.Choice(L("btn_main_retry_battle"), () => { Check(Game.RetryCurrentBattle()); Go(battleScene); });
            screen.Choice(L("btn_main_back_to_route"), Home);
        }
        else screen.SceneBack(Home);
    }
}
