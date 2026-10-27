using System;
using System.Collections;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class IndependentStatusActionTests
    {
        [UnityTest]
        public IEnumerator IndependentStatus_InheritsParent_AndRestoresAfterAwaitAndException()
            => HookTestEnv.AwaitTask(ScopeLifetime);

        private async Task ScopeLifetime()
        {
            using var e = new BattleFlowTestEnv();
            e.ConfigureStatus("ST_ScopeRoot"); e.ConfigureStatus("ST_ScopeChild"); e.CreateMinimalBattle();
            var owner = e.Ally.Entity;
            var root = owner.AddStatus("ST_ScopeRoot", owner, 0, 2, 0, e.DataManager.StatusMainDict["ST_ScopeRoot"]);
            var child = owner.AddStatus("ST_ScopeChild", owner, 0, 2, 0, e.DataManager.StatusMainDict["ST_ScopeChild"]);
            var quota = new StatusActionData { ActionKey = "per-rune", MaxTimePerAction = 1 };
            Assert.That(BattleResolutionContext.CurrentAction, Is.Null);
            long first;
            using (BattleResolutionContext.BeginIndependentStatusAction(root))
            {
                var scope = BattleResolutionContext.CurrentAction; first = scope.ScopeId;
                Assert.That(scope.IndependentStatus, Is.SameAs(root));
                Assert.That(BattleResolutionContext.TryCollect(root, quota), Is.True);
                await Task.Yield();
                try
                {
                    using (BattleResolutionContext.BeginIndependentStatusAction(child))
                    {
                        Assert.That(BattleResolutionContext.CurrentAction, Is.SameAs(scope));
                        Assert.That(BattleResolutionContext.TryCollect(root, quota), Is.False);
                        Assert.That(BattleResolutionContext.TryCollect(child, quota), Is.True);
                        throw new InvalidOperationException("scope sentinel");
                    }
                }
                catch (InvalidOperationException ex) { Assert.That(ex.Message, Is.EqualTo("scope sentinel")); }
                Assert.That(BattleResolutionContext.CurrentAction, Is.SameAs(scope));
            }
            Assert.That(BattleResolutionContext.CurrentAction, Is.Null);
            using (BattleResolutionContext.BeginIndependentStatusAction(root))
            {
                Assert.That(BattleResolutionContext.CurrentAction.ScopeId, Is.Not.EqualTo(first));
                Assert.That(BattleResolutionContext.TryCollect(root, quota), Is.True);
            }
            var active = new BattleResolutionContext.CounterScope();
            using (BattleResolutionContext.EnterAction(active))
            using (BattleResolutionContext.BeginIndependentStatusAction(root))
            {
                await Task.Yield();
                Assert.That(BattleResolutionContext.CurrentAction, Is.SameAs(active));
                Assert.That(active.IndependentStatus, Is.Null);
            }
            Assert.That(BattleResolutionContext.CurrentAction, Is.Null);
        }
    }
}
