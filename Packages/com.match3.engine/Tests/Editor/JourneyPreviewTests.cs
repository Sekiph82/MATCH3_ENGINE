using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Match3.Engine.Runtime;
using Match3.Engine.Runtime.LiveOps;
using NUnit.Framework;
using UnityEngine;

namespace Match3.Engine.Tests.Editor
{
    // ENG-M20-014. Fixture journey_two_worlds_v1.json (13 levels: reef 1-7, kelp 1-6, worlds listed out of
    // order) was generated outside the Engine with Python canonical digests and is fed through the real
    // ContentMetadataReceiver; the preview only ever sees accepted metadata.
    public sealed class JourneyPreviewTests
    {
        private static readonly ContentKind[] Kinds = { ContentKind.World, ContentKind.Level, ContentKind.Event };
        private const string FixtureName = "journey_two_worlds_v1.json";

        private static string Fixture(string name)
        {
            var root = Directory.GetParent(Application.dataPath).FullName;
            var path = Path.Combine(root, "Packages", "com.match3.engine", "Tests", "Editor",
                "Fixtures", "JourneyPreview", name);
            Assert.That(File.Exists(path), Is.True, "missing fixture " + path);
            return File.ReadAllText(path);
        }

        private static ContentMetadataReceiver Load(string json, IEnumerable<string> knownWorlds = null)
        {
            var rx = new ContentMetadataReceiver("Coral_Rescue", Kinds, knownWorlds);
            var result = rx.Receive(json);
            Assert.That(result.Accepted, Is.True, result.Summary);
            return rx;
        }

        private static ContentMetadataReceiver Journey() => Load(Fixture(FixtureName));

        private static string L(int n) => "jx.level." + n.ToString("D4");

        private static JourneySnapshot Done(params int[] levels) =>
            new JourneySnapshot(levels.Select(L));

        private static string Doc(string items) =>
            "{\"schemaVersion\":\"1.0\",\"producerId\":\"inline\",\"gameId\":\"Coral_Rescue\",\"items\":[" + items + "]}";

        private static string World(string id, int order) =>
            "{\"contentKind\":\"World\",\"contentId\":\"" + id + "\",\"contentVersion\":1,\"payload\":{\"order\":" + order + "}}";

        private static string Level(string id, string world, int order, int version = 1,
            string objective = "{\"kind\":\"CollectPiece\",\"targetId\":\"piece.red\",\"count\":10}",
            string obstacles = "", string extra = "") =>
            "{\"contentKind\":\"Level\",\"contentId\":\"" + id + "\",\"contentVersion\":" + version +
            ",\"payload\":{\"worldId\":\"" + world + "\",\"order\":" + order + ",\"moves\":20,\"objectives\":[" +
            objective + "],\"obstacleIds\":[" + obstacles + "]" + extra + "}}";

        private static string Describe(JourneyStripView v) =>
            v.GameId + "|" + v.Status + "|" + v.TotalJourneyLevels + "|" +
            string.Join(",", v.Nodes.Select(n => n.Position + ":" + n.LevelId + ":" + n.WorldId + ":" + n.State +
                ":" + n.IsWorldEnd + ":" + n.IsCheckpoint)) + "|" +
            (v.Next == null ? "-" : v.Next.LevelId + ":" + v.Next.ContentVersion + ":" + v.Next.Moves + ":" +
                string.Join(";", v.Next.Objectives.Select(o => o.Kind + "/" + o.TargetId + "/" + o.Count)) + ":" +
                string.Join(";", v.Next.ObstacleIds) + ":" + string.Join(";", v.Next.NewObstacleIds)) + "|" +
            string.Join(",", v.UnplacedLevelIds);

        // ---- fixture journeys ------------------------------------------------------------

        [Test]
        public void NewPlayer_FirstStripOfTen_WithCheckpointsAndTruthfulTeaser()
        {
            var rx = Journey();
            var v = JourneyPreviewBuilder.Build(rx, new JourneySnapshot());

            Assert.AreEqual(JourneyStatus.Active, v.Status);
            Assert.AreEqual("Coral_Rescue", v.GameId);
            Assert.AreEqual(13, v.TotalJourneyLevels);
            Assert.AreEqual(Enumerable.Range(1, 10), v.Nodes.Select(n => n.Position));
            Assert.AreEqual(Enumerable.Range(1, 10).Select(L), v.Nodes.Select(n => n.LevelId));
            Assert.AreEqual(JourneyNodeState.Current, v.Nodes[0].State);
            Assert.That(v.Nodes.Skip(1).All(n => n.State == JourneyNodeState.Locked));
            // World boundary after reef level 7, strip checkpoint at position 10.
            Assert.AreEqual(new[] { 7, 10 }, v.Nodes.Where(n => n.IsCheckpoint).Select(n => n.Position));
            Assert.AreEqual(new[] { 7 }, v.Nodes.Where(n => n.IsWorldEnd).Select(n => n.Position));
            Assert.AreEqual("jx.world.kelp", v.Nodes[7].WorldId);

            var level = rx.Applied[L(1)];
            Assert.AreEqual(L(1), v.Next.LevelId);
            Assert.AreEqual(level.PayloadSha256, v.Next.PayloadSha256);
            Assert.AreEqual(ContentIntegrity.DigestVerified, v.Next.Integrity);
            Assert.AreEqual(20, v.Next.Moves);
            Assert.AreEqual("CollectPiece/piece.red/10",
                string.Join(";", v.Next.Objectives.Select(o => o.Kind + "/" + o.TargetId + "/" + o.Count)));
            CollectionAssert.IsEmpty(v.Next.NewObstacleIds);
            CollectionAssert.IsEmpty(v.UnplacedLevelIds);
        }

        [Test]
        public void Teaser_ObjectivesAndObstacles_AreExactlyTheNextLevelsContent()
        {
            var rx = Journey();
            for (var completedCount = 0; completedCount < 13; completedCount++)
            {
                var v = JourneyPreviewBuilder.Build(rx, Done(Enumerable.Range(1, completedCount).ToArray()));
                var level = rx.Applied[L(completedCount + 1)];
                Assert.AreEqual(level.ContentId, v.Next.LevelId);
                Assert.AreEqual(level.Moves, v.Next.Moves);
                CollectionAssert.AreEqual(level.Objectives, v.Next.Objectives);
                CollectionAssert.AreEqual(level.ObstacleIds, v.Next.ObstacleIds);
                CollectionAssert.IsSubsetOf(v.Next.NewObstacleIds, level.ObstacleIds);
                var earlier = Enumerable.Range(1, completedCount)
                    .SelectMany(n => rx.Applied[L(n)].ObstacleIds).ToList();
                CollectionAssert.AreEqual(level.ObstacleIds.Where(o => !earlier.Contains(o)), v.Next.NewObstacleIds);
            }
        }

        [Test]
        public void MidWorld_NewObstacleIntroduced_OnlyWhenGenuinelyNew()
        {
            var rx = Journey();
            var reef6 = JourneyPreviewBuilder.Build(rx, Done(1, 2, 3, 4, 5));
            Assert.AreEqual(L(6), reef6.Next.LevelId);
            CollectionAssert.AreEqual(new[] { "obstacle.ice", "obstacle.crate" }, reef6.Next.ObstacleIds);
            CollectionAssert.AreEqual(new[] { "obstacle.crate" }, reef6.Next.NewObstacleIds);

            var reef5 = JourneyPreviewBuilder.Build(rx, Done(1, 2, 3, 4));
            CollectionAssert.IsEmpty(reef5.Next.NewObstacleIds); // ice already met on level 4
        }

        [Test]
        public void SecondStrip_AcrossWorldBoundary_ShortAtEndOfContent()
        {
            var v = JourneyPreviewBuilder.Build(Journey(), Done(Enumerable.Range(1, 10).ToArray()));
            Assert.AreEqual(JourneyStatus.Active, v.Status);
            Assert.AreEqual(new[] { 11, 12, 13 }, v.Nodes.Select(n => n.Position));
            Assert.AreEqual(JourneyNodeState.Current, v.Nodes[0].State);
            Assert.AreEqual(new[] { false, false, true }, v.Nodes.Select(n => n.IsWorldEnd));
            Assert.AreEqual(L(11), v.Next.LevelId);
            CollectionAssert.AreEqual(new[] { "obstacle.vine" }, v.Next.NewObstacleIds);
        }

        [Test]
        public void EndOfContent_NoTeaser_LastStripAllCompleted()
        {
            var v = JourneyPreviewBuilder.Build(Journey(), Done(Enumerable.Range(1, 13).ToArray()));
            Assert.AreEqual(JourneyStatus.EndOfContent, v.Status);
            Assert.IsNull(v.Next);
            Assert.AreEqual(new[] { 11, 12, 13 }, v.Nodes.Select(n => n.Position));
            Assert.That(v.Nodes.All(n => n.State == JourneyNodeState.Completed));
        }

        [Test]
        public void LockedLevels_OutOfOrderCompletion_AndForeignProgressIgnored()
        {
            var progress = new JourneySnapshot(new[] { L(1), L(2), L(4), "other.game.level", "jx.level.9999" });
            var v = JourneyPreviewBuilder.Build(Journey(), progress);
            Assert.AreEqual(new[]
            {
                JourneyNodeState.Completed, JourneyNodeState.Completed, JourneyNodeState.Current,
                JourneyNodeState.Completed, JourneyNodeState.Locked
            }, v.Nodes.Take(5).Select(n => n.State));
            Assert.AreEqual(L(3), v.Next.LevelId);
            Assert.That(v.Nodes.All(n => n.LevelId.StartsWith("jx.level.", StringComparison.Ordinal) &&
                                         n.LevelId != "jx.level.9999"));
        }

        // ---- boundary content -------------------------------------------------------------

        [Test]
        public void MissingNextLevel_JourneyStopsAtGap_NeverInventsIt()
        {
            var rx = Load(Doc(World("jx.world.a", 1) + "," + Level("jx.a.1", "jx.world.a", 1) + "," +
                Level("jx.a.2", "jx.world.a", 2) + "," + Level("jx.a.4", "jx.world.a", 4)));

            var active = JourneyPreviewBuilder.Build(rx, new JourneySnapshot());
            Assert.AreEqual(JourneyStatus.Active, active.Status);
            Assert.AreEqual(2, active.TotalJourneyLevels);
            Assert.AreEqual(new[] { "jx.a.1", "jx.a.2" }, active.Nodes.Select(n => n.LevelId));
            Assert.IsFalse(active.Nodes[1].IsWorldEnd, "a gap must not fake a world boundary");

            var blocked = JourneyPreviewBuilder.Build(rx, new JourneySnapshot(new[] { "jx.a.1", "jx.a.2" }));
            Assert.AreEqual(JourneyStatus.ContentGap, blocked.Status);
            Assert.IsNull(blocked.Next);
            Assert.That(blocked.Nodes.All(n => n.LevelId != "jx.a.4"));
        }

        [Test]
        public void WorldMissingFirstLevel_IsGapAtWorldBoundary()
        {
            var rx = Load(Doc(World("jx.world.a", 1) + "," + World("jx.world.b", 2) + "," +
                Level("jx.a.1", "jx.world.a", 1) + "," + Level("jx.b.2", "jx.world.b", 2)));
            var v = JourneyPreviewBuilder.Build(rx, new JourneySnapshot(new[] { "jx.a.1" }));
            Assert.AreEqual(JourneyStatus.ContentGap, v.Status);
            Assert.IsTrue(v.Nodes.Single().IsWorldEnd);
        }

        [Test]
        public void LevelInShippedWorldWithoutWorldMetadata_IsUnplaced()
        {
            var rx = Load(Doc(World("jx.world.a", 1) + "," + Level("jx.a.1", "jx.world.a", 1) + "," +
                Level("jx.s.1", "jx.world.shipped", 1)), new[] { "jx.world.shipped" });
            var v = JourneyPreviewBuilder.Build(rx, new JourneySnapshot());
            CollectionAssert.AreEqual(new[] { "jx.s.1" }, v.UnplacedLevelIds);
            Assert.AreEqual(new[] { "jx.a.1" }, v.Nodes.Select(n => n.LevelId));
        }

        [Test]
        public void NoContent_EmptyReceiverOrWorldsOnly()
        {
            var empty = JourneyPreviewBuilder.Build(new ContentMetadataReceiver("Coral_Rescue", Kinds), new JourneySnapshot());
            Assert.AreEqual(JourneyStatus.NoContent, empty.Status);
            CollectionAssert.IsEmpty(empty.Nodes);
            Assert.IsNull(empty.Next);

            var worldsOnly = JourneyPreviewBuilder.Build(Load(Doc(World("jx.world.a", 1))), new JourneySnapshot());
            Assert.AreEqual(JourneyStatus.NoContent, worldsOnly.Status);
        }

        [Test]
        public void ContentUpdate_TeaserFollowsNewVersion()
        {
            var rx = Load(Doc(World("jx.world.a", 1) + "," + Level("jx.a.1", "jx.world.a", 1)));
            Assert.AreEqual(1, JourneyPreviewBuilder.Build(rx, new JourneySnapshot()).Next.ContentVersion);

            var update = rx.Receive(Doc(Level("jx.a.1", "jx.world.a", 1, 2,
                "{\"kind\":\"ClearObstacle\",\"targetId\":\"obstacle.ice\",\"count\":9}", "\"obstacle.ice\"")));
            Assert.That(update.Accepted, Is.True, update.Summary);
            var v = JourneyPreviewBuilder.Build(rx, new JourneySnapshot());
            Assert.AreEqual(2, v.Next.ContentVersion);
            Assert.AreEqual(GoalKind.ClearObstacle, v.Next.Objectives.Single().Kind);
            Assert.AreEqual(9, v.Next.Objectives.Single().Count);
            CollectionAssert.AreEqual(new[] { "obstacle.ice" }, v.Next.NewObstacleIds);
        }

        // ---- determinism, read-only, no monetization --------------------------------------

        [Test]
        public void Deterministic_RegardlessOfItemDeliveryOrder()
        {
            var doc = (Dictionary<string, object>)CanonicalJson.Parse(Fixture(FixtureName));
            ((List<object>)doc["items"]).Reverse();
            var reversed = Load(CanonicalJson.Serialize(doc));
            var original = Journey();

            foreach (var count in new[] { 0, 3, 7, 10, 12, 13 })
            {
                var progress = Done(Enumerable.Range(1, count).ToArray());
                var a = Describe(JourneyPreviewBuilder.Build(original, progress));
                Assert.AreEqual(a, Describe(JourneyPreviewBuilder.Build(reversed, progress)));
                Assert.AreEqual(a, Describe(JourneyPreviewBuilder.Build(original, progress)));
            }
        }

        [Test]
        public void ScoreAuthorityDecoupled_PersonalBestsDoNotChangePreview()
        {
            var rx = Journey();
            var plain = new JourneySnapshot(new[] { L(1), L(2) });
            var withBests = new JourneySnapshot(new[] { L(1), L(2) }, new[] { L(1) }, new[] { "r1", "r2" },
                new[] { new PersonalBestRecord("Coral_Rescue|p1|classic|" + L(1) + "|2026-W46", 99999, "r1") });
            Assert.AreEqual(Describe(JourneyPreviewBuilder.Build(rx, plain)),
                Describe(JourneyPreviewBuilder.Build(rx, withBests)));
        }

        [Test]
        public void ExtensionFieldsInContent_NeverSurfaceInPreview()
        {
            var plain = Load(Doc(World("jx.world.a", 1) + "," + Level("jx.a.1", "jx.world.a", 1)));
            var decorated = Load(Doc(World("jx.world.a", 1) + "," + Level("jx.a.1", "jx.world.a", 1,
                extra: ",\"x-offerId\":\"bundle.big\",\"x-paywall\":true")));
            Assert.AreEqual(Describe(JourneyPreviewBuilder.Build(plain, new JourneySnapshot())),
                Describe(JourneyPreviewBuilder.Build(decorated, new JourneySnapshot())));

            var priced = new ContentMetadataReceiver("Coral_Rescue", Kinds).Receive(
                Doc(World("jx.world.a", 1) + "," + Level("jx.a.1", "jx.world.a", 1, extra: ",\"x-price\":199")));
            Assert.IsFalse(priced.Accepted, "price fields are rejected before they can reach the preview");
        }

        [Test]
        public void ViewTypes_AreSealedGetOnly_WithNoMonetizationScoreOrGrantFields()
        {
            var forbidden = new[]
            {
                "price", "offer", "sku", "paywall", "purchase", "iap", "currency", "store", "discount",
                "gem", "coin", "grant", "reward", "score", "rank", "timer", "expire"
            };
            foreach (var type in new[]
                     { typeof(JourneyStripView), typeof(JourneyNode), typeof(NextLevelTeaser), typeof(LevelObjective) })
            {
                Assert.IsTrue(type.IsSealed, type.Name);
                CollectionAssert.IsEmpty(type.GetConstructors(BindingFlags.Public | BindingFlags.Instance), type.Name);
                foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    Assert.IsNull(p.GetSetMethod(), type.Name + "." + p.Name);
                    Assert.IsFalse(forbidden.Any(f => p.Name.ToLowerInvariant().Contains(f)), type.Name + "." + p.Name);
                }
                CollectionAssert.IsEmpty(type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Where(m => !m.IsSpecialName), type.Name + " exposes mutators");
            }
            Assert.That(typeof(JourneyStripView).GetProperty(nameof(JourneyStripView.Nodes)).PropertyType,
                Is.EqualTo(typeof(IReadOnlyList<JourneyNode>)));
            Assert.AreEqual("journey-preview/1.0", JourneyStripView.SchemaVersion);
        }

        [Test]
        public void Build_NullArguments_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => JourneyPreviewBuilder.Build(null, new JourneySnapshot()));
            Assert.Throws<ArgumentNullException>(() => JourneyPreviewBuilder.Build(Journey(), null));
        }
    }
}
