using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace GenshinTurnBased.Tests.EditMode
{
    public sealed class ChartPublicationTests
    {
        private string root;
        private string Source => Path.Combine(root, "source");
        private string Live => Path.Combine(root, "live");
        private string Work => Path.Combine(root, "work");

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Library", "ChartPublicationTests", Guid.NewGuid().ToString("N"));
            Write(Source, "Units/hero.xlsx", "new");
            Write(Live, "Units/hero.xlsx", "old");
        }

        [TearDown] public void TearDown() => Directory.Delete(root, true);

        [Test]
        public void SuccessPreservesAssetIdentityAndRemovesOnlyRetiredWorkbooks()
        {
            Write(Source, "EnemySkill.xlsx", "legacy");
            Write(Source, "Units/~$hero.xlsx", "lock");
            Write(Live, "Units.meta", "folder GUID");
            Write(Live, "Units/hero.xlsx.meta", "hero GUID");
            Write(Live, "retired.xlsx", "retired");
            Write(Live, "retired.xlsx.meta", "retired GUID");
            Write(Live, "other.json", "config");
            int count = SyncExcelFiles.Publish(Source, Live, Work, candidate =>
            {
                Assert.That(Read(candidate, "Units/hero.xlsx"), Is.EqualTo("new"));
                Assert.That(Read(Live, "Units/hero.xlsx"), Is.EqualTo("old"));
            });
            Assert.That(count, Is.EqualTo(1));
            Assert.That(Read(Live, "Units/hero.xlsx"), Is.EqualTo("new"));
            Assert.That(Read(Live, "Units/hero.xlsx.meta"), Is.EqualTo("hero GUID"));
            Assert.That(Read(Live, "Units.meta"), Is.EqualTo("folder GUID"));
            Assert.That(Read(Live, "other.json"), Is.EqualTo("config"));
            foreach (string missing in new[] { "retired.xlsx", "retired.xlsx.meta", "EnemySkill.xlsx", "Units/~$hero.xlsx" })
                Assert.That(File.Exists(Path.Combine(Live, missing)), Is.False, missing);
        }

        [Test]
        public void StructuralPreflightRejectsMissingCoreAndUnreadableExtraWithoutTouchingLive()
        {
            var error = Assert.Throws<InvalidDataException>(() => SyncExcelFiles.Publish(Source, Live, Work, SyncExcelFiles.ValidateCandidate));
            StringAssert.Contains("Characters.xlsx", error.Message);
            StringAssert.Contains("hero.xlsx", error.Message);
            Assert.That(Read(Live, "Units/hero.xlsx"), Is.EqualTo("old"));
        }

        [Test]
        public void FailedPromotionRestoresPreviousVersion()
        {
            Assert.Throws<DirectoryNotFoundException>(() => SyncExcelFiles.Publish(Source, Live, Work,
                candidate => Directory.Move(candidate, Path.Combine(root, "removed-candidate"))));
            Assert.That(Read(Live, "Units/hero.xlsx"), Is.EqualTo("old"));
        }

        [Test]
        public void InterruptedSwapRestoresLiveBeforeCheckingNextCandidate()
        {
            Directory.CreateDirectory(Work);
            Directory.Move(Live, Path.Combine(Work, "previous"));
            Assert.Throws<InvalidDataException>(() => SyncExcelFiles.Publish(Source, Live, Work, candidate => throw new InvalidDataException()));
            Assert.That(Read(Live, "Units/hero.xlsx"), Is.EqualTo("old"));
        }

        private static string Read(string directory, string relative) => File.ReadAllText(Path.Combine(directory, relative));

        private static void Write(string directory, string relative, string value)
        {
            string path = Path.Combine(directory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, value);
        }
    }
}
