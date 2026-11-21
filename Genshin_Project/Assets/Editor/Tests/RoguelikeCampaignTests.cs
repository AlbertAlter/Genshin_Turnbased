using System.Linq;
using System.IO;
using NUnit.Framework;
using Roguelike.Tests.RouteGeneration;
using UnityEngine;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class RoguelikeCampaignTests
    {
        [TestCase(307452039), TestCase(1), TestCase(-1), Category("RealConfig")]
        public void FormalRoutePreservesCoverageAndAllGenerationConstraints(int seed)
        {
            var loader = new RouteConfigLoader();
            var stages = loader.Load(Path.Combine(Application.streamingAssetsPath, "Data/Roguelike/StageConfig.xlsx"));
            var settings = new RouteGenerationSettings();
            settings.Apply(loader.LoadRouteUiRules(Path.Combine(Application.streamingAssetsPath, "Data/UI/Rouge_Route/StageRule.xlsx")));
            var generated = new RouteMapGenerator(settings, new SystemBattleRandomSource(unchecked(seed * 397 ^ 1))).Generate(stages);
            Assert.That(generated.Success, Is.True, generated.Error);
            Assert.That(RouteMapGenerator.Validate(generated.Map, out string error, stages), Is.True, error);
            Assert.That(generated.Map.Stages, Has.Count.EqualTo(stages.Count));
        }
        [Test, Category("RealConfig")]
        public void NewRunRegistersStartingEquipmentAndKeepsPresetsIndependent()
        {
            HookTestDataManager data = null;
            try
            {
                data = HookTestEnv.CreateDataManager(loadRealData: true);
                var snapshot = RoguelikeStartingLoadout.Load(data);
                var original = snapshot.PartyCharacterIds.ToArray();
                Assert.That(original, Is.EqualTo(new[] { 1009, 1010, 1011, 1014 }));
                Assert.That(snapshot.ActivePartyPreset, Is.EqualTo(1));
                Assert.That(snapshot.Inventory.Weapons.Select(weapon => weapon.InstanceId).Distinct().Count(), Is.EqualTo(4));
                var nextRun = RoguelikeStartingLoadout.Load(data);
                Assert.That(nextRun.Inventory.Weapons.Select(w => w.InstanceId)
                    .Intersect(snapshot.Inventory.Weapons.Select(w => w.InstanceId)), Is.Empty);
                foreach (var character in snapshot.Characters)
                {
                    var equipped = snapshot.Inventory.Weapons.Single(weapon => weapon.InstanceId == character.EquippedWeaponInstanceId);
                    Assert.That(data.WeaponAttributesDict[equipped.Weapon.WeaponID].Star, Is.EqualTo(3));
                }
                Assert.That(snapshot.Inventory.Artifacts, Is.Empty);
                Assert.That(snapshot.Inventory.Items.Single(item => item.ItemId == RoguelikeWishService.FateItemId).Quantity, Is.EqualTo(30));
                var runtime = new RoguelikeChapterRuntime(snapshot);
                runtime.SetParty(new[] { 1011, 1014 }, 2);
                var saved = RoguelikeSaveJson.DeepClone(runtime.ExportSnapshot());
                Assert.That(saved.PartyPresets.Single(preset => preset.Number == 1).Members, Is.EqualTo(original));
                Assert.That(saved.PartyPresets.Single(preset => preset.Number == 2).Members, Is.EqualTo(saved.PartyCharacterIds));
            }
            finally { if (data != null) HookTestEnv.DestroyDataManager(data); }
        }
    }
}
