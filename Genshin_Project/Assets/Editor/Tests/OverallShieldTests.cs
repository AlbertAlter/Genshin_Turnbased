using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class OverallShieldTests
    {
        [TestCase(-0.1, 0f)]
        [TestCase(0, 0f)]
        [TestCase(0.125, 0.5f)]
        [TestCase(0.25, 1f)]
        [TestCase(1, 1f)]
        public void ShieldRevealClampsAndSmooths(double elapsed, float expected)
        {
            Assert.That(UIShieldStyle.SampleReveal(elapsed, .25f), Is.EqualTo(expected).Within(.00001));
            Assert.That(UIShieldStyle.SampleReveal(elapsed, 0), Is.EqualTo(1));
        }

        [Test]
        public void ShieldFadePreservesCurrentAlphaWhenPausedAndStopsAtZero()
        {
            float alpha = UIShieldStyle.StepAlpha(1, false, .09f, .18f);
            Assert.That(alpha, Is.EqualTo(.5f).Within(.00001));
            Assert.That(UIShieldStyle.StepAlpha(alpha, false, 0, .18f), Is.EqualTo(alpha));
            Assert.That(UIShieldStyle.StepAlpha(alpha, false, -.1f, .18f), Is.EqualTo(alpha));
            Assert.That(UIShieldStyle.StepAlpha(alpha, false, 1, .18f), Is.Zero);
            Assert.That(UIShieldStyle.StepAlpha(alpha, true, .03f, .15f), Is.EqualTo(.7f).Within(.00001));
            Assert.That(UIShieldStyle.StepAlpha(alpha, false, 0, 0), Is.Zero);
        }

        [Test]
        public void FirstSweepWaitsForRevealWithoutRollingVisualRandomEarly()
        {
            var rolls = new SequenceBattleRandomSource(new[] { .5f });
            var clock = new ShieldSweepClock(rolls);
            clock.Restart(10.25);
            Assert.That(clock.Sample(10.1, .8f, 4, .4f, 0, true, out float before), Is.Zero);
            Assert.That(before, Is.Zero);
            Assert.That(rolls.FloatCallCount, Is.Zero);
            Assert.That(clock.Sample(10.65, .8f, 4, .4f, .15f, true, out float half), Is.EqualTo(1).Within(.00001));
            Assert.That(half, Is.EqualTo(.5f).Within(.00001));
            clock.Sample(11.1, .8f, 4, .4f, .15f, true, out _);
            Assert.That(rolls.FloatCallCount, Is.EqualTo(1));
        }

        [Test] public void CardSweepsDrawOncePerWaitAndDriftIndependently()
        {
            var rolls = new SequenceBattleRandomSource(new[] { 0f, .75f, .5f });
            var a = new ShieldSweepClock(rolls);
            var b = new ShieldSweepClock(rolls);
            float Progress(ShieldSweepClock card, double now)
            {
                card.Sample(now, 1, 4, .5f, .15f, true, out float progress);
                return progress;
            }
            Assert.That(Progress(a, .5), Is.EqualTo(Progress(b, .5)));
            Assert.That(rolls.FloatCallCount, Is.Zero);
            Progress(a, 1); Progress(b, 1); Progress(a, 2); Progress(b, 3);
            Assert.That(rolls.FloatCallCount, Is.EqualTo(2));
            Assert.That(Progress(a, 4.75), Is.EqualTo(.25f));
            Assert.That(Progress(b, 4.75), Is.EqualTo(1f));
            Assert.That(Progress(a, 5.5), Is.EqualTo(1f));
            Assert.That(Progress(b, 5.5), Is.EqualTo(.25f));
            Assert.That(rolls.FloatCallCount, Is.EqualTo(3));
            a.Restart(6);
            a.Sample(7, 1, 4, 0, .15f, true, out _);
            b.Sample(7, 1, 4, .5f, .15f, false, out _);
            Assert.That(rolls.FloatCallCount, Is.EqualTo(3));
        }

        private static StatusInstance Add(BattleFlowTestEnv env, BattleEntity owner, string id, float amount, string element = "None")
        {
            var main = new StatusMainData { StatusID2 = id, StatusType = "OverallShield", MaxStack = 1, WhenMax = "0" };
            env.DataManager.StatusMainDict[id] = main;
            var status = owner.AddStatus(id, owner, 3, 2, 0, main);
            owner.AddShield(amount, element, 3, sourceStatus: status);
            return status;
        }
        private static HitRequest Hit(BattleEntity target, float damage, string element = "Physical")
        {
            var source = DamageSourceInfo.CreateUnknown(); source.CountsAsHit = true;
            return new HitRequest { Target = target, DamageBeforeShield = damage, Element = element,
                Source = source, DamageType = "Normal" };
        }

        [TestCase(false)] [TestCase(true)]
        public void SharedBreak_DistributesByOriginalDamage_ThenUsesPersonalShield(bool reverse)
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            var a = env.Ally.Entity; var b = env.CreateAlly(2).Entity;
            Add(env, a, "ST_TestOverall", 50); b.AddShield(10, "None", 3);
            float ahp = a.CurrentHP, bhp = b.CurrentHP;
            HitResolution ar, br;
            using (var batch = new HitBatch())
            {
                if (reverse) { br = batch.Resolve(Hit(b, 75)); ar = batch.Resolve(Hit(a, 25)); }
                else { ar = batch.Resolve(Hit(a, 25)); br = batch.Resolve(Hit(b, 75)); }
                Assert.That(a.CurrentHP, Is.EqualTo(ahp), "先收齐本hit，不能按遍历先后抢盾。");
                batch.Flush();
            }
            Assert.That(ahp - a.CurrentHP, Is.EqualTo(12.5f).Within(.001));
            Assert.That(bhp - b.CurrentHP, Is.EqualTo(27.5f).Within(.001));
            Assert.That(ar.DamageEvent.HitLanded, Is.True); Assert.That(br.DamageEvent.HitLanded, Is.True);
            Assert.That(a.HasStatus("ST_TestOverall"), Is.False);
        }

        [Test] public void MultipleSources_ConsumeSimultaneously_UseOwnCreatorsStrength()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            var a = env.Ally.Entity; var b = env.CreateAlly(2).Entity; var target = env.CreateAlly(3).Entity;
            a.ShieldStrength = 1; b.ShieldStrength = 0; target.ShieldStrength = 99;
            Add(env, a, "ST_FirstOverall", 50); Add(env, b, "ST_SecondOverall", 80);
            float hp = target.CurrentHP;
            using (var batch = new HitBatch()) batch.Resolve(Hit(target, 120));
            Assert.That(hp - target.CurrentHP, Is.EqualTo(20).Within(.001), "容量100和80只挡100，不相加，不读取受击者盾强。");
            Assert.That(a.Shields, Is.Empty); Assert.That(b.Shields, Is.Empty);
        }

        [Test] public void RemainingSource_KeepsProtection_WithoutLeakingIntoPersonalShieldQuery()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            var a = env.Ally.Entity; var b = env.CreateAlly(2).Entity; var target = env.CreateAlly(3).Entity;
            Add(env, a, "ST_FirstOverall", 20); var surviving = Add(env, b, "ST_SecondOverall", 100);
            using (var batch = new HitBatch()) batch.Resolve(Hit(target, 40));
            Assert.That(a.Shields, Is.Empty); Assert.That(b.Shields[0].Value, Is.EqualTo(60));
            Assert.That(target.HasShieldProtection(), Is.True);
            Assert.That(target.GetTotalShieldHP(), Is.Zero); Assert.That(b.GetTotalShieldHP(), Is.Zero);
            b.RemoveStatusInstance(surviving); Assert.That(target.HasShieldProtection(), Is.False);
        }

        [Test] public void DirectUsesRawValue_TrueBypasses_AndDeadSourceCannotProtect()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            var a = env.Ally.Entity; var target = env.CreateAlly(2).Entity;
            a.ShieldStrength = 4; Add(env, a, "ST_TestOverall", 50, "Geo");
            float hp = target.CurrentHP;
            using (var batch = new HitBatch()) batch.Resolve(Hit(target, 10, "True"));
            Assert.That(a.Shields[0].Value, Is.EqualTo(50));
            using (var batch = new HitBatch()) batch.Resolve(Hit(target, 60, "None"));
            Assert.That(hp - target.CurrentHP, Is.EqualTo(20).Within(.001));
            Add(env, a, "ST_TestOverall", 50); a.CurrentHP = 0;
            Assert.That(target.HasShieldProtection(), Is.False);
        }

        [Test] public void IndependentDisplay_InterfaceReceivesWholeTeamAndRemovesOnlyOneSource()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            var a = env.Ally.Entity; var b = env.CreateAlly(2).Entity; env.Manager.StartBattle();
            var first = Add(env, a, "ST_FirstOverall", 50); Add(env, b, "ST_SecondOverall", 75);
            var go = new GameObject("OverallShieldDisplayTest", typeof(RectTransform));
            try
            {
                var layout = go.AddComponent<BattleUiLayout>();
                var rect = (RectTransform)go.transform;
                layout.cards = new[] {
                    new BattleUiCardBinding { side=BattleSide.Ally, slot=1, root=rect, art=rect },
                    new BattleUiCardBinding { side=BattleSide.Ally, slot=2, root=rect, art=rect }
                };
                var view = go.AddComponent<OverallShieldRecordingView>();
                var source = go.AddComponent<BattleUiOverallShieldSource>();
                source.battle=env.Manager; source.layout=layout; source.view=view; source.Refresh();
                Assert.That(view.Counts.Count, Is.EqualTo(2)); Assert.That(view.Counts.Values, Is.All.EqualTo(2));
                a.RemoveStatusInstance(first); source.Refresh(); Assert.That(view.Counts.Count, Is.EqualTo(1));
                a.CurrentHP=0; source.Refresh(); Assert.That(view.Counts.Values, Is.All.EqualTo(1));
                a.CurrentHP=1; source.Refresh(); Assert.That(view.Counts.Values, Is.All.EqualTo(2));
                source.enabled=false; Assert.That(view.Counts, Is.Empty);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test] public void ReapplyReplacesOnlyOwnInstance_AndWrongHostIsRejected()
        {
            using var env = new BattleFlowTestEnv(); env.CreateMinimalBattle();
            var a = env.Ally.Entity; var b = env.CreateAlly(2).Entity;
            var old = Add(env, a, "ST_TestOverall", 50); Add(env, b, "ST_TestOverall", 75);
            var next = Add(env, a, "ST_TestOverall", 100);
            Assert.That(old.IsActive, Is.False); Assert.That(next.IsActive, Is.True);
            Assert.That(a.Shields.Count, Is.EqualTo(1)); Assert.That(b.Shields[0].Value, Is.EqualTo(75));
            Assert.Throws<System.InvalidOperationException>(() => b.AddStatus("ST_TestOverall", a, 3, 2, 0, next.MainData));
        }
    }

    public sealed class OverallShieldRecordingView : MonoBehaviour, IBattleUiOverallShields
    {
        public readonly Dictionary<long,int> Counts = new Dictionary<long,int>();
        public bool SetOverallShield(long id, string element, IReadOnlyList<BattleUiCardBinding> targets)
        { Counts[id]=targets.Count; return true; }
        public void RemoveOverallShield(long id) => Counts.Remove(id);
        public void ClearOverallShields() => Counts.Clear();
    }
}
