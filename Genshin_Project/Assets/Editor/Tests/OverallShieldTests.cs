using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class OverallShieldTests
    {
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
