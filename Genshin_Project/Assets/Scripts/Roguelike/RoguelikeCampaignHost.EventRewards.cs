using System;
using System.Collections;
using UnityEngine;

public sealed partial class RoguelikeCampaignHost
{
    [SerializeField, Tooltip("可选的IRoguelikeEventRewardEntry展示扩展；留空使用公共奖励浮层，奖励已由ReturnToRoute入账。")]
    private MonoBehaviour eventRewardEntry;
    private IRoguelikeEventRewardEntry boundEventRewardEntry;
    private RoguelikeEventRewardContext eventRewardForRoute;
    private Coroutine eventRewardRouteNotification;

    /// <summary>后续事件奖励模块显式绑定。此入口不改变战斗结束、奖励浮层或返回路线操作。</summary>
    public void BindEventRewardEntry(IRoguelikeEventRewardEntry entry) => boundEventRewardEntry = entry;

    // 只在正常ReturnToRoute存档事务已成功后调用；失败、战败、离开和取消不产生通知。
    private void QueueEventRewardForRoute(RoguelikeFlowContext completed)
    {
        var request = RoguelikeEventRewardContext.Capture(completed, Game.NodeContents);
        if (request != null) eventRewardForRoute = request;
    }

    private void NotifyEventRewardWhenRouteReady()
    {
        if (eventRewardForRoute == null || eventRewardRouteNotification != null) return;
        eventRewardRouteNotification = StartCoroutine(NotifyEventRewardOnRoute(eventRewardForRoute, screen));
    }

    private IEnumerator NotifyEventRewardOnRoute(RoguelikeEventRewardContext request, RoguelikeCampaignScreen routeScreen)
    {
        try
        {
            // 先让本帧的普通结算恢复入口安排自身奖励浮层；事件奖励不能抢在它前面。
            yield return null;
            while (eventRewardForRoute == request && loaded == home && screen == routeScreen && !menu && failure == null &&
                (loading || rewardsWaiting || rewardPopup?.IsOpen == true || !routeScreen.Ready))
                yield return null;
            if (eventRewardForRoute != request || loaded != home || screen != routeScreen || menu || failure != null) yield break;
            try
            {
                // 返回路线后再次核对RunId及实际完成节点，拒绝重开/读档留下的旧回调。
                var current = Read();
                if (!request.MatchesRoute(current)) { eventRewardForRoute = null; yield break; }
                var entry = boundEventRewardEntry ?? eventRewardEntry as IRoguelikeEventRewardEntry;
                if (entry is UnityEngine.Object owner && owner == null) entry = null;
                // 已在ReturnToRoute事务入账，这里只展示；绑定扩展入口同样不得再次发放本表奖励。
                if (entry != null) entry.OnReturnedToRoute(request);
                else if (request.Rewards.Count > 0) PresentNodeRewards(request.Rewards);
                if (eventRewardForRoute == request) eventRewardForRoute = null;
            }
            catch (Exception error)
            {
                Debug.LogException(error, this);
                // 仅保留本次会话的通知供下次路线入口重试；实际奖励的耐久记录由未来奖励模块负责。
            }
        }
        finally { eventRewardRouteNotification = null; }
    }

    private void CancelEventRewardRouteNotification()
    {
        if (eventRewardRouteNotification != null) StopCoroutine(eventRewardRouteNotification);
        eventRewardRouteNotification = null;
        // 只取消旧场景的等待，尚未交接的身份保留到真正返回路线；不把转场取消当成领奖。
    }
}
