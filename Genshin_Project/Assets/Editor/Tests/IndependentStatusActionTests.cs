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
        public IEnumerator Execution_PerInvocationQuota_AndRestoresAfterAwaitAndException()
            => HookTestEnv.AwaitTask(ScopeLifetime);

        private async Task ScopeLifetime()
        {
            using var e = new BattleFlowTestEnv();
            e.ConfigureStatus("ST_ScopeRoot"); e.ConfigureStatus("ST_ScopeChild"); e.CreateMinimalBattle();
            var owner = e.Ally.Entity;
            var root = owner.AddStatus("ST_ScopeRoot", owner, 0, 2, 0, e.DataManager.StatusMainDict["ST_ScopeRoot"]);
            var child = owner.AddStatus("ST_ScopeChild", owner, 0, 2, 0, e.DataManager.StatusMainDict["ST_ScopeChild"]);
            var quota = new StatusActionData { ActionKey = "per-rune", MaxTimePerExecution = 1 };
            Assert.That(BattleResolutionContext.CurrentAction, Is.Null);
            long first;
            using (BattleResolutionContext.BeginStatusExecution(root))
            {
                var scope = BattleResolutionContext.CurrentExecution; first = scope.ScopeId;
                Assert.That(BattleResolutionContext.CurrentAction, Is.Null);
                Assert.That(scope.IndependentStatus, Is.SameAs(root));
                Assert.That(BattleResolutionContext.TryCollect(root, quota), Is.True);
                await Task.Yield();
                try
                {
                    using (BattleResolutionContext.BeginStatusExecution(child))
                    {
                        Assert.That(BattleResolutionContext.CurrentExecution, Is.Not.SameAs(scope));
                        Assert.That(BattleResolutionContext.TryCollect(root, quota), Is.True);
                        Assert.That(BattleResolutionContext.TryCollect(child, quota), Is.True);
                        throw new InvalidOperationException("scope sentinel");
                    }
                }
                catch (InvalidOperationException ex) { Assert.That(ex.Message, Is.EqualTo("scope sentinel")); }
                Assert.That(BattleResolutionContext.CurrentExecution, Is.SameAs(scope));
                BattleResolutionContext.ContinueStatusExecution(root);
                Assert.That(BattleResolutionContext.CurrentExecution, Is.Not.SameAs(scope), "下一次实际调用A的效果才创建新段");
                Assert.That(BattleResolutionContext.TryCollect(root, quota), Is.True);
                Assert.That(BattleResolutionContext.TryCollect(root, quota), Is.False);
                using (BattleResolutionContext.BeginStatusExecution(child))
                    Assert.That(BattleResolutionContext.TryCollect(root, quota), Is.True, "同一实例再次触发有新额度");
                using (BattleResolutionContext.BeginStatusExecution(root, joinCurrentExecution: true))
                    Assert.That(BattleResolutionContext.TryCollect(root, quota), Is.True, "被其他状态打断后再调用A获得新额度");
                using (BattleResolutionContext.BeginStatusExecution(root, joinCurrentExecution: true))
                    Assert.That(BattleResolutionContext.TryCollect(root, quota), Is.False, "连续行动行共用当前执行");
            }
            Assert.That(BattleResolutionContext.CurrentAction, Is.Null);
            using (BattleResolutionContext.BeginStatusExecution(root))
            {
                Assert.That(BattleResolutionContext.CurrentExecution.ScopeId, Is.Not.EqualTo(first));
                Assert.That(BattleResolutionContext.TryCollect(root, quota), Is.True);
            }
            var active = new BattleResolutionContext.CounterScope();
            using (BattleResolutionContext.EnterAction(active))
            using (BattleResolutionContext.BeginStatusExecution(root))
            {
                await Task.Yield();
                Assert.That(BattleResolutionContext.CurrentAction, Is.SameAs(active));
                Assert.That(active.IndependentStatus, Is.Null);
            }
            Assert.That(BattleResolutionContext.CurrentAction, Is.Null);
            Assert.That(BattleResolutionContext.CurrentExecution, Is.Null);
        }

        [Test]
        public void Action_RequiresActiveContainer_AndSharesQuotaWithStatusExtensions()
        {
            var listener = new StatusInstance(); var source = new StatusInstance();
            var quota = new StatusActionData { ActionKey = "active-only", MaxTimePerAction = 1 };
            Assert.That(BattleResolutionContext.TryCollect(listener, quota), Is.False);
            using (BattleResolutionContext.BeginSkillAction(true))
            {
                var active = BattleResolutionContext.CurrentAction;
                Assert.That(BattleResolutionContext.TryCollect(listener, quota), Is.True);
                using (var status = BattleResolutionContext.BeginStatusExecution(source))
                {
                    Assert.That(BattleResolutionContext.CurrentAction, Is.SameAs(active));
                    Assert.That(BattleResolutionContext.TryCollect(listener, quota), Is.False);
                    status.Complete();
                }
            }
            using (var status = BattleResolutionContext.BeginStatusExecution(source))
            {
                Assert.That(BattleResolutionContext.CurrentAction, Is.Null);
                Assert.That(BattleResolutionContext.TryCollect(listener, quota), Is.False);
                status.Complete();
            }
        }

        [Test]
        public void SameStatus_ThreeTriggersWithinOneActiveAction_HaveThreeExecutionScopes()
        {
            var source = new StatusInstance(); var listener = new StatusInstance();
            var scopes = new System.Collections.Generic.HashSet<long>();
            var quota = new StatusActionData { ActionKey = "three-triggers", MaxTimePerExecution = 1 };
            using (BattleResolutionContext.BeginSkillAction(true))
                for (int i = 0; i < 3; i++)
                    using (var status = BattleResolutionContext.BeginStatusExecution(source))
                    {
                        scopes.Add(BattleResolutionContext.CurrentExecution.ScopeId);
                        Assert.That(BattleResolutionContext.TryCollect(listener, quota), Is.True);
                        Assert.That(BattleResolutionContext.TryCollect(listener, quota), Is.False);
                        Assert.That(BattleResolutionContext.HasApplicableLimit(quota), Is.False,
                            "每次触发重开Execution，不能单独拿它解除重入保护");
                        status.Complete();
                    }
            Assert.That(scopes.Count, Is.EqualTo(3));
        }

        [Test]
        public void LinearStatusCalls_AThenBThenA_UseThreeQuotas_WhileOldFactsKeepTheirSource()
        {
            var a = new StatusInstance(); var b = new StatusInstance(); var listener = new StatusInstance();
            var quota = new StatusActionData { ActionType = "OnTrigger", ActionKey = "linear", MaxTimePerExecution = 1 };
            using var action = BattleResolutionContext.BeginSkillAction(true);
            var active = BattleResolutionContext.CurrentAction;
            using var outer = BattleResolutionContext.BeginStatusExecution(a);
            var first = BattleResolutionContext.CurrentExecution;
            var oldFact = BattleResolutionContext.Capture();
            Assert.That(BattleResolutionContext.TryCollect(listener, quota), Is.True);
            using (var child = BattleResolutionContext.BeginStatusExecution(b))
            {
                Assert.That(BattleResolutionContext.TryCollect(listener, quota), Is.True);
                child.Complete();
            }
            BattleResolutionContext.ContinueStatusExecution(a);
            var resumed = BattleResolutionContext.CurrentExecution;
            Assert.That(resumed, Is.Not.SameAs(first));
            Assert.That(BattleResolutionContext.TryCollect(listener, quota), Is.True);
            using (oldFact.Enter())
            {
                Assert.That(BattleResolutionContext.CurrentExecution, Is.SameAs(first));
                Assert.That(BattleResolutionContext.TryCollect(listener, quota), Is.False);
            }
            BattleResolutionContext.ContinueStatusExecution(a);
            Assert.That(BattleResolutionContext.CurrentExecution, Is.SameAs(resumed), "回放旧事实不移动线性调用游标");
            Assert.That(BattleResolutionContext.TryCollect(listener, quota), Is.False);
            Assert.That(BattleResolutionContext.CurrentAction, Is.SameAs(active));
            outer.Complete();
        }

        [TestCase("ST_Weapon_Sequence")]
        [TestCase("ST_Artifact_Sequence")]
        public void EquipmentCall_DoesNotSplitTheSurroundingStatusSegment(string id)
        {
            var a = new StatusInstance(); var listener = new StatusInstance();
            var quota = new StatusActionData { ActionType = "OnTrigger", MaxTimePerExecution = 1 };
            using var outer = BattleResolutionContext.BeginStatusExecution(a);
            var first = BattleResolutionContext.CurrentExecution;
            Assert.That(BattleResolutionContext.TryCollect(listener, quota), Is.True);
            using (var gear = BattleResolutionContext.BeginStatusExecution(new StatusInstance { StatusID2 = id }))
            {
                Assert.That(BattleResolutionContext.TryCollect(listener, quota), Is.False);
                gear.Complete();
            }
            BattleResolutionContext.ContinueStatusExecution(a);
            Assert.That(BattleResolutionContext.CurrentExecution, Is.SameAs(first));
            Assert.That(BattleResolutionContext.TryCollect(listener, quota), Is.False);
            outer.Complete();
        }

        [UnityTest]
        public IEnumerator CharacterOnApply_ConsecutiveRowsShareExecution()
            => HookTestEnv.AwaitTask(() => OnApplySequence(false, false, true));

        [UnityTest]
        public IEnumerator EnemyOnApply_ConsecutiveRowsShareExecution()
            => HookTestEnv.AwaitTask(() => OnApplySequence(true, false, true));

        [UnityTest]
        public IEnumerator CharacterOnApply_InterruptedEffectsResumeWithNewExecution()
            => HookTestEnv.AwaitTask(() => OnApplySequence(false, true, false));

        [UnityTest]
        public IEnumerator EnemyOnApply_InterruptedEffectsResumeWithNewExecution()
            => HookTestEnv.AwaitTask(() => OnApplySequence(true, true, false));

        [UnityTest]
        public IEnumerator CharacterOnApply_InterruptedRowsResumeWithNewExecution()
            => HookTestEnv.AwaitTask(() => OnApplySequence(false, true, true));

        [UnityTest]
        public IEnumerator EnemyOnApply_InterruptedRowsResumeWithNewExecution()
            => HookTestEnv.AwaitTask(() => OnApplySequence(true, true, true));

        [UnityTest]
        public IEnumerator CharacterOnTrigger_ConsecutiveRowsShareExecution()
            => HookTestEnv.AwaitTask(() => OnApplySequence(false, false, true, "OnTrigger"));

        [UnityTest]
        public IEnumerator EnemyOnTrigger_ConsecutiveRowsShareExecution()
            => HookTestEnv.AwaitTask(() => OnApplySequence(true, false, true, "OnTrigger"));

        [UnityTest]
        public IEnumerator CharacterOnTrigger_ChildStateSplitsResumedRows()
            => HookTestEnv.AwaitTask(() => OnApplySequence(false, true, true, "OnTrigger"));

        [UnityTest]
        public IEnumerator EnemyOnTrigger_ChildStateSplitsResumedRows()
            => HookTestEnv.AwaitTask(() => OnApplySequence(true, true, true, "OnTrigger"));

        [UnityTest]
        public IEnumerator OnTrigger_DifferentOccurrencesDoNotMerge()
            => HookTestEnv.AwaitTask(() => OnApplySequence(false, false, true, "OnTrigger", 2));

        [UnityTest]
        public IEnumerator OnTrigger_SeparateDeclarationCallbacksShareExecution()
            => HookTestEnv.AwaitTask(() => OnApplySequence(false, false, true, "OnTrigger", 1, true));

        [UnityTest]
        public IEnumerator OnTrigger_SeparateCallbacksStillSplitAfterAChildState()
            => HookTestEnv.AwaitTask(() => OnApplySequence(false, true, true, "OnTrigger", 1, true));

        [UnityTest]
        public IEnumerator CharacterOnItsTurn_ConsecutiveRowsShareExecution()
            => HookTestEnv.AwaitTask(() => OnApplySequence(false, false, true, "OnItsTurn"));

        [UnityTest]
        public IEnumerator EnemyOnItsTurn_ConsecutiveRowsShareExecution()
            => HookTestEnv.AwaitTask(() => OnApplySequence(true, false, true, "OnItsTurn"));

        [UnityTest]
        public IEnumerator CharacterOnEnd_ConsecutiveRowsShareExecution()
            => HookTestEnv.AwaitTask(() => OnApplySequence(false, false, true, "OnEnd"));

        [UnityTest]
        public IEnumerator EnemyOnEnd_ConsecutiveRowsShareExecution()
            => HookTestEnv.AwaitTask(() => OnApplySequence(true, false, true, "OnEnd"));

        [UnityTest]
        public IEnumerator CharacterOnDestroy_ConsecutiveRowsShareExecution()
            => HookTestEnv.AwaitTask(() => OnApplySequence(false, false, true, "OnDestroy"));

        [UnityTest]
        public IEnumerator EnemyOnDestroy_ConsecutiveRowsShareExecution()
            => HookTestEnv.AwaitTask(() => OnApplySequence(true, false, true, "OnDestroy"));

        [UnityTest]
        public IEnumerator CharacterOnDeath_ConsecutiveRowsShareExecution()
            => HookTestEnv.AwaitTask(() => OnApplySequence(false, false, true, "OnDeath"));

        [UnityTest]
        public IEnumerator EnemyOnDeath_ConsecutiveRowsShareExecution()
            => HookTestEnv.AwaitTask(() => OnApplySequence(true, false, true, "OnDeath"));

        private async Task OnApplySequence(bool enemySource, bool interrupt, bool separateRows,
            string entryType = "OnApply", int occurrences = 1, bool viaDeclarationCallbacks = false)
        {
            using var e = new BattleFlowTestEnv();
            const string a = "ST_SequenceA", b = "ST_SequenceB";
            e.ConfigureStatus(a); e.ConfigureStatus(b);
            foreach (string id in new[] { "STE_SequenceA1", "STE_SequenceA2", "STE_SequenceB" })
                e.DataManager.StatusEffectDict[id] = new StatusEffectData {
                    StatusEffectID2 = id, EffectType = "Damage", Element = "Physical", Param1 = "0.1*ATK,0",
                    TargetType = "Enemy", TargetSelect = "1", TargetConsecutive = 1, ExecuteCount = 1 };
            e.DataManager.StatusEffectDict["STE_SequenceApplyB"] = new StatusEffectData {
                StatusEffectID2 = "STE_SequenceApplyB", EffectType = "ApplyStatus", Param1 = b,
                TargetType = "Self", ExecuteCount = 1 };
            var ids = new System.Collections.Generic.List<string> { "STE_SequenceA1" };
            if (interrupt) ids.Add("STE_SequenceApplyB");
            ids.Add("STE_SequenceA2");
            var actions = new System.Collections.Generic.List<StatusActionData>();
            if (separateRows)
                foreach (string id in ids) actions.Add(new StatusActionData {
                    StatusID2 = a, ActionType = entryType, ActionKey = id, Param1 = id });
            else actions.Add(new StatusActionData { StatusID2 = a, ActionType = entryType, Param1 = string.Join(";", ids) });
            if (entryType == "OnTrigger" && !viaDeclarationCallbacks)
                foreach (var action in actions) action.OutgoingHit = "Normal";
            e.DataManager.StatusActionDict[a] = actions;
            e.DataManager.StatusActionDict[b] = new System.Collections.Generic.List<StatusActionData> {
                new StatusActionData { StatusID2 = b, ActionType = "OnApply", Param1 = "STE_SequenceB" } };
            e.CreateMinimalBattle();
            if (entryType == "OnDeath")
            {
                if (enemySource) e.CreateEnemy(2); else e.CreateAlly(2);
            }
            var random = new SystemBattleRandomSource(1104); BattleRandom.SetSource(random);
            var labels = new System.Collections.Generic.List<string>();
            var scopes = new System.Collections.Generic.List<long>();
            var callbacks = new System.Collections.Generic.List<IDisposable>();
            using var observer = BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (header, hit) => {
                string id = hit.Source.SourceStatus?.StatusID2;
                if (id != a && id != b) return;
                labels.Add(id); scopes.Add(header.ExecutionScopeId);
                if (entryType == "OnTrigger") Assert.That(header.ActionScopeId, Is.GreaterThan(0));
                else Assert.That(header.ActionScopeId, Is.Zero, "独立状态入口不伪造主动行为");
            }, long.MinValue);
            await BattleExecution.Run(async () => {
                var source = enemySource ? e.Enemy.Entity : e.Ally.Entity;
                var status = source.AddStatus(a, source, 0, enemySource ? 5 : 2, 0, e.DataManager.StatusMainDict[a]);
                await BattleExecution.Flush();
                if (viaDeclarationCallbacks)
                    foreach (var action in actions)
                        callbacks.Add(BattleEventHub.Subscribe<DamageEventData>(BattleEventKind.DamageResolved, (header, hit) => {
                            if (hit.Source.SourceEntity != source || hit.Source.SourceStatus != null || hit.DamageType != "Normal") return;
                            var context = new ScriptHookContext { Caster = source, Status = status, StatusHost = source,
                                Target = hit.Subject, Declaration = BattleEventHub.Current };
                            if (enemySource) e.Enemy.TryExecuteEventStatusAction(status, action, source, context);
                            else e.Ally.TryExecuteEventStatusAction(status, action, source, context);
                        }, status.ApplyOrder, () => status.IsActive));
                if (entryType == "OnTrigger")
                {
                    using var active = BattleResolutionContext.BeginSkillAction(true);
                    for (int i = 0; i < occurrences; i++)
                    {
                        using (var hit = new HitBatch()) hit.Resolve(new HitRequest {
                            Target = enemySource ? e.Ally.Entity : e.Enemy.Entity, Element = "Physical",
                            DamageBeforeShield = 1f, DamageType = "Normal",
                            Source = DamageSourceInfo.Create(source, enemySource ? ReactionSourceKind.EnemySkill : ReactionSourceKind.CharacterSkill,
                                "SK_Sequence", "SE_Sequence") });
                        await BattleExecution.Flush();
                    }
                }
                else if (entryType == "OnDestroy")
                {
                    source.RemoveStatusInstance(status);
                    await BattleExecution.Flush();
                }
                else if (entryType == "OnDeath")
                {
                    await BattleDeathResolution.RunAsync(async () => {
                        var attacker = enemySource ? e.Ally.Entity : e.Enemy.Entity;
                        using (var hit = new HitBatch()) hit.Resolve(new HitRequest {
                            Target = source, Element = "True", DamageBeforeShield = source.CurrentHP + 1f,
                            Source = DamageSourceInfo.Create(attacker, enemySource ? ReactionSourceKind.CharacterSkill : ReactionSourceKind.EnemySkill,
                                "SK_DeathSequence", "SE_DeathSequence") });
                        await BattleExecution.Flush();
                    });
                }
                else if (entryType != "OnApply")
                {
                    StatusActionDispatch.Enqueue(status, actions, source, entryType);
                    await BattleExecution.Flush();
                }
            });
            foreach (var callback in callbacks) callback.Dispose();
            var expected = new System.Collections.Generic.List<string>();
            for (int i = 0; i < occurrences; i++) expected.AddRange(interrupt ? new[] { a, b, a } : new[] { a, a });
            Assert.That(labels, Is.EqualTo(expected));
            Assert.That(scopes[0], Is.GreaterThan(0));
            if (interrupt)
                Assert.That(new System.Collections.Generic.HashSet<long>(scopes).Count, Is.EqualTo(3));
            else
            {
                for (int i = 0; i < occurrences; i++)
                    Assert.That(scopes[i * 2 + 1], Is.EqualTo(scopes[i * 2]), "拆成多行不会凭空多算一次调用");
                if (occurrences > 1) Assert.That(scopes[2], Is.Not.EqualTo(scopes[0]), "真实的下一次触发不能并入上一次");
            }
        }

        [UnityTest]
        public IEnumerator WeaponResults_DoNotTriggerStatusActions_OrConsumeCharacterQuota()
            => HookTestEnv.AwaitTask(() => EquipmentResults("ST_Weapon_ExecutionSourceTest"));

        [UnityTest]
        public IEnumerator ArtifactResults_DoNotTriggerStatusActions_OrConsumeCharacterQuota()
            => HookTestEnv.AwaitTask(() => EquipmentResults("ST_Artifact_ExecutionSourceTest"));

        private async Task EquipmentResults(string equipmentID)
        {
            using var e = new BattleFlowTestEnv();
            const string observerID = "ST_EquipmentResultObserver";
            const string childID = "ST_PublicEquipmentChild";
            const string markerID = "ST_EquipmentHitMarker";
            const string enemyObserverID = "ST_EnemyEquipmentResultObserver";
            e.ConfigureStatus(equipmentID, actionType: "OnApply", actionEffectID2: "STE_EquipmentInit");
            foreach (string id in new[] { childID, observerID, enemyObserverID, markerID }) e.ConfigureStatus(id);
            foreach (string id in new[] { "STE_EquipmentInit", "STE_ObserveLimited", "STE_ObserveUnlimited" })
                e.DataManager.StatusEffectDict[id] = new StatusEffectData {
                    StatusEffectID2 = id, EffectType = "GainEnergy", TargetType = "Self", Param2 = "Flat",
                    Param1 = id == "STE_ObserveUnlimited" ? "10" : "1", ExecuteCount = 1 };
            e.DataManager.StatusEffectDict["STE_EquipmentAttack"] = new StatusEffectData {
                StatusEffectID2 = "STE_EquipmentAttack", EffectType = "Damage", Element = "Physical",
                Param1 = "0.1*ATK,0", TargetType = "Enemy", TargetSelect = "1", ExecuteCount = 1 };
            e.DataManager.StatusEffectDict["STE_ObserveEnemy"] = new StatusEffectData {
                StatusEffectID2 = "STE_ObserveEnemy", EffectType = "ApplyStatus", Param1 = markerID,
                TargetType = "Self", Duration = 1, AddInPhase = 5, ExecuteCount = 1 };
            var limited = new StatusActionData { StatusID2 = observerID, ActionKey = "limited",
                ActionType = "OnTrigger", OutgoingHit = "All", Param1 = "STE_ObserveLimited",
                MaxTimePerAction = 1, MaxTimePerExecution = 1, MaxTimePerEffect = 1,
                MaxTimePerTurn = 1, MaxTimePerLife = 1 };
            var unlimited = new StatusActionData { StatusID2 = observerID, ActionKey = "unlimited",
                ActionType = "OnTrigger", OutgoingHit = "All", Param1 = "STE_ObserveUnlimited" };
            e.DataManager.StatusActionDict[observerID] = new System.Collections.Generic.List<StatusActionData> { limited, unlimited };
            e.DataManager.StatusActionDict[enemyObserverID] = new System.Collections.Generic.List<StatusActionData> {
                new StatusActionData { StatusID2 = enemyObserverID, ActionType = "OnTrigger", OnHit = "All",
                    Param1 = "STE_ObserveEnemy", MaxTimePerExecution = 1, MaxTimePerEffect = 1 } };
            e.CreateMinimalBattle();
            await BattleExecution.Run(async () => {
                var owner = e.Ally.Entity; var enemy = e.Enemy.Entity;
                owner.CurrentEnergy = 0f;
                var equipment = owner.AddStatus(equipmentID, owner, 0, 2, 0, e.DataManager.StatusMainDict[equipmentID]);
                await BattleExecution.Flush();
                Assert.That(owner.CurrentEnergy, Is.EqualTo(1f), "装备自己的OnApply正常执行");
                owner.CurrentEnergy = 0f;
                var child = owner.AddStatus(childID, owner, 0, 2, 0, e.DataManager.StatusMainDict[childID],
                    weaponContext: equipment.WeaponContext,
                    equipmentSource: equipment.EquipmentSource);
                owner.AddStatus(observerID, owner, 0, 2, 0, e.DataManager.StatusMainDict[observerID]);
                enemy.AddStatus(enemyObserverID, enemy, 0, 5, 0, e.DataManager.StatusMainDict[enemyObserverID]);
                await BattleExecution.Flush();
                var attack = new StatusActionData { StatusID2 = childID, ActionType = "OnTrigger", Param1 = "STE_EquipmentAttack" };
                using (BattleResolutionContext.BeginSkillAction(true))
                using (BattleResolutionContext.BeginEffect())
                {
                    float hp = enemy.CurrentHP;
                    Assert.That(e.Ally.TryExecuteEventStatusAction(child, attack, owner,
                        new ScriptHookContext { Caster = owner, Status = child, StatusHost = owner }), Is.True,
                        "装备仍可正常响应角色的主动行为");
                    await BattleExecution.Flush();
                    Assert.That(enemy.CurrentHP, Is.LessThan(hp), "装备追加伤害仍真实结算");
                    Assert.That(owner.CurrentEnergy, Is.Zero, "装备伤害不引出有限次或无限次的状态行动");
                    Assert.That(enemy.HasStatus(markerID), Is.False, "敌方状态行动同样排除装备引子");
                    for (int i = 0; i < 2; i++)
                    {
                        using (var hit = new HitBatch()) hit.Resolve(new HitRequest {
                            Target = enemy, Element = "Physical", DamageBeforeShield = 1f,
                            Source = DamageSourceInfo.Create(owner, ReactionSourceKind.CharacterSkill, "SK_Test", "SE_Test") });
                        await BattleExecution.Flush();
                        Assert.That(owner.CurrentEnergy, Is.EqualTo(i == 0 ? 11f : 21f),
                            "装备不占角色Action/Effect/Execution/Turn/Life额度，普通角色事件照常限次");
                    }
                    Assert.That(enemy.HasStatus(markerID), Is.True);
                }
            });
        }

        [UnityTest]
        public IEnumerator EquipmentOrigin_SurvivesDelayedDeclaration_AndRestoresAfterException()
            => HookTestEnv.AwaitTask(EquipmentOriginLifetime);

        private async Task EquipmentOriginLifetime()
        {
            using var e = new BattleFlowTestEnv(); e.CreateMinimalBattle();
            var source = new StatusInstance { EquipmentSource = new EquipmentEffectSource(e.Ally.Entity) };
            var row = new StatusActionData { ActionType = "OnTrigger", MaxTimePerExecution = 1, MaxTimePerEffect = 1 };
            BattleDeclaration declaration = null;
            using (BattleResolutionContext.BeginSkillAction(true))
            using (BattleResolutionContext.BeginEffect())
            {
                var original = BattleEventHub.Publish(new HPChangedEventData(e.Ally.Entity, 3f, 2f, "TestCharacter"));
                try
                {
                    using var status = BattleResolutionContext.BeginStatusExecution(source);
                    await Task.Yield();
                    declaration = BattleEventHub.Publish(new HPChangedEventData(e.Ally.Entity, 2f, 1f, "TestEquipment"));
                    Assert.That(BattleResolutionContext.TryCollect(source, row), Is.False);
                    Assert.That(BattleResolutionContext.CanTriggerStatusAction(row,
                        new ScriptHookContext { Declaration = original }), Is.True,
                        "装备处理器仍可继续处理明确保存的原角色事实");
                    foreach (string lifecycle in new[] { "OnApply", "OnItsTurn", "OnEnd", "OnDestroy", "OnDeath" })
                        Assert.That(BattleResolutionContext.CanTriggerStatusAction(new StatusActionData { ActionType = lifecycle }), Is.True);
                    throw new InvalidOperationException("equipment source sentinel");
                }
                catch (InvalidOperationException ex) { Assert.That(ex.Message, Is.EqualTo("equipment source sentinel")); }
                Assert.That(BattleResolutionContext.TryCollect(source, row), Is.True, "退出装备来源后角色额度尚未占用");
                Assert.That(BattleResolutionContext.CanTriggerStatusAction(row,
                    new ScriptHookContext { Declaration = declaration }), Is.False, "延迟声明保留实际发生时的装备来源");
            }
        }
    }
}
