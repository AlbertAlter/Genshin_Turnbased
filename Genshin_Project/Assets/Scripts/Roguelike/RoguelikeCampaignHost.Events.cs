using System;

public sealed partial class RoguelikeCampaignHost
{
    // 战前编队与入战条件窗共用；空条件和“无”不展示，不从钩子名称推导玩家文案。
    private string BattleConditionText(string key, StageSetupData setup = null)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        string text = BattleEventConditionText.Provider(DataManager.Instance, key, () => english, setup)();
        return string.IsNullOrWhiteSpace(text) || text.Trim() == L("ui_event_condition_none").Trim()
            ? null : text.Replace("\\n", "\n");
    }

    private void BindBattleEventConditions(RoguelikeBattleSceneEntry binding)
    {
        if (binding.conditionsPrefab == null) return;
        var current = Read();
        var encounter = Game.NodeContents.Resolve(current.Snapshot.CurrentChapterId, current.Flow.Node);
        Check(encounter);
        string description = BattleConditionText(encounter.Data.ConditionKey, encounter.Data);
        if (description == null) return;
        battleEventConditions = Instantiate(binding.conditionsPrefab, binding.navigationMount, false);
        var describe = BattleEventConditionText.Provider(DataManager.Instance, encounter.Data.ConditionKey,
            () => ProjectUserSettings.English(english), encounter.Data, binding.runtime.GetComponent<BattleManager>());
        battleEventConditions.Bind(L("ui_battle_event_conditions"), description,
            () => !loading && !rewardsWaiting && rewardPopup?.IsOpen != true && pauseMenu?.IsOpen != true &&
                pauseMenu?.input?.IsUiInputBlocked != true && pauseMenu?.input?.IsSelecting != true &&
                pauseMenu?.switcher?.IsSwitching != true, describe);
    }

    // 与编队同样在转场遮罩内重定向，不闪出主入口或角色列表。
    private void EventPage()
    {
        if (loading || handlingSceneReady) queuedScene = eventScene;
        else Go(eventScene);
    }

    internal void BackFromEvent() { routeOverview = true; Home(); }

    internal void OpenEvent(RoguelikeEventView view)
    {
        var original = Read();
        var result = Game.NodeContents.ResolveEvent(original.Snapshot.CurrentChapterId, original.Flow.Node);
        Check(result);
        if (original.Flow.Step != RoguelikeFlowStep.Node) throw new InvalidOperationException(L("tip_main_progress_changed"));
        bool canFight = !string.IsNullOrEmpty(result.Data.BattleContentId);
        void ValidateOriginal()
        {
            var current = Read();
            if (current.RunId != original.RunId || current.Flow.Step != RoguelikeFlowStep.Node ||
                current.Flow.StepId != original.Flow.StepId || current.Flow.Node.ContentId != result.Data.EventId)
                throw new InvalidOperationException(L("tip_main_progress_changed"));
        }
        view.Bind(DataManager.GetOrCreateLoaded(), result.Data, original.Flow.Node.StageIndex + 1,
            Game.NodeContents.EventEnemyLevel(original.Snapshot.CurrentChapterId, original.Flow.Node), english,
            canFight ? () => Act(() => { ValidateOriginal(); Roster(RosterPage.BeforeBattle); }) : (Action)null,
            () => Act(() => { ValidateOriginal(); Check(Game.SkipEvent(original.Flow.StepId)); BackFromEvent(); }));
    }
}
