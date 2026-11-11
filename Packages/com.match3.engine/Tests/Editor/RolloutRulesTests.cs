using System;
using System.Collections.Generic;
using NUnit.Framework;
using Match3.Engine.Runtime.LiveOps;

namespace Match3.Engine.Tests.Editor
{
    public class RolloutRulesTests
    {
        private static readonly Platform[] Mobile = { Platform.iOS, Platform.Android };
        private static readonly string[] Games = { "Coral_Rescue" };

        private static RolloutRule Rule(int percent = 100, string min = null, string max = null,
            Platform[] platforms = null, string[] regions = null, string id = "rule-1", string salt = "salt-r")
        {
            return RolloutRule.Create(id, 1, Games, min, max, platforms ?? Mobile, regions ?? new string[0], percent, salt);
        }

        private static RolloutContext Ctx(string playerId = "p1", string app = "1.2.3", Platform platform = Platform.iOS,
            string region = "US", bool child = false, string game = "Coral_Rescue")
        {
            return new RolloutContext(game, playerId, app, platform, region, child);
        }

        // ---- SemVer ----

        [Test]
        public void SemVer_OrderingFollowsSemVer2Precedence()
        {
            string[] ordered =
            {
                "0.9.9", "1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-alpha.beta", "1.0.0-beta", "1.0.0-beta.2",
                "1.0.0-beta.11", "1.0.0-rc.1", "1.0.0", "1.0.1", "1.2.0", "1.10.0", "2.0.0"
            };
            for (int i = 0; i < ordered.Length; i++)
            for (int j = 0; j < ordered.Length; j++)
            {
                int c = SemVer.Parse(ordered[i]).CompareTo(SemVer.Parse(ordered[j]));
                Assert.AreEqual(Math.Sign(i.CompareTo(j)), Math.Sign(c), ordered[i] + " vs " + ordered[j]);
            }
        }

        [Test]
        public void SemVer_ParsesComponents()
        {
            var v = SemVer.Parse("12.3.45-rc.1");
            Assert.AreEqual(12, v.Major);
            Assert.AreEqual(3, v.Minor);
            Assert.AreEqual(45, v.Patch);
            Assert.AreEqual("rc.1", v.Prerelease);
            Assert.IsNull(SemVer.Parse("1.0.0").Prerelease);
            Assert.AreEqual("12.3.45-rc.1", v.ToString());
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("1")]
        [TestCase("1.2")]
        [TestCase("1.2.3.4")]
        [TestCase("01.2.3")]
        [TestCase("1.2.3-")]
        [TestCase("1.2.3-alpha..1")]
        [TestCase("1.2.3-01")]
        [TestCase("1.2.3+build")]
        [TestCase("v1.2.3")]
        [TestCase(" 1.2.3")]
        [TestCase("1.-2.3")]
        [TestCase("1.2.x")]
        [TestCase("99999999999.0.0")]
        [TestCase("1.2.3-al_pha")]
        public void SemVer_Invalid_TryParseFalse(string text)
        {
            Assert.IsFalse(SemVer.TryParse(text, out _));
            Assert.Throws<FormatException>(() => SemVer.Parse(text));
        }

        // ---- Rule construction ----

        [Test]
        public void Rule_ExcludeChildProfilesIsAlwaysTrue()
        {
            Assert.IsTrue(Rule().ExcludeChildProfiles);
            Assert.IsNull(typeof(RolloutRule).GetProperty("ExcludeChildProfiles").SetMethod, "not configurable");
        }

        [Test]
        public void Rule_InvalidInputs_Throw()
        {
            Assert.Throws<ArgumentException>(() => RolloutRule.Create("", 1, Games, null, null, Mobile, new string[0], 50, "s"));
            Assert.Throws<ArgumentOutOfRangeException>(() => RolloutRule.Create("r", 0, Games, null, null, Mobile, new string[0], 50, "s"));
            Assert.Throws<ArgumentException>(() => RolloutRule.Create("r", 1, Games, null, null, Mobile, new string[0], 50, ""));
            Assert.Throws<ArgumentOutOfRangeException>(() => RolloutRule.Create("r", 1, Games, null, null, Mobile, new string[0], -1, "s"));
            Assert.Throws<ArgumentOutOfRangeException>(() => RolloutRule.Create("r", 1, Games, null, null, Mobile, new string[0], 101, "s"));
            Assert.Throws<ArgumentException>(() => RolloutRule.Create("r", 1, Games, "1.x", null, Mobile, new string[0], 50, "s"));
            Assert.Throws<ArgumentException>(() => RolloutRule.Create("r", 1, Games, null, "nope", Mobile, new string[0], 50, "s"));
            Assert.Throws<ArgumentException>(() => RolloutRule.Create("r", 1, Games, "2.0.0", "1.0.0", Mobile, new string[0], 50, "s"));
            Assert.Throws<ArgumentException>(() => RolloutRule.Create("r", 1, Games, null, null, new Platform[0], new string[0], 50, "s"));
            Assert.Throws<ArgumentException>(() => RolloutRule.Create("r", 1, Games, null, null, null, new string[0], 50, "s"));
            Assert.Throws<ArgumentOutOfRangeException>(() => RolloutRule.Create("r", 1, Games, null, null, new[] { (Platform)42 }, new string[0], 50, "s"));
            Assert.Throws<ArgumentException>(() => RolloutRule.Create("r", 1, Games, null, null, Mobile, null, 50, "s"));
            Assert.Throws<ArgumentException>(() => RolloutRule.Create("r", 1, null, null, null, Mobile, new string[0], 50, "s"));
            Assert.Throws<ArgumentException>(() => RolloutRule.Create("r", 1, new string[0], null, null, Mobile, new string[0], 50, "s"));
            Assert.Throws<ArgumentException>(() => RolloutRule.Create("r", 1, new string[] { null }, null, null, Mobile, new string[0], 50, "s"));
            Assert.Throws<ArgumentException>(() => RolloutRule.Create("r", 1, new[] { "Coral_Rescue", "" }, null, null, Mobile, new string[0], 50, "s"));
            foreach (var bad in new[] { "us", "USA", "U", "", null, "U1", "\u00DCS" })
                Assert.Throws<ArgumentException>(() => RolloutRule.Create("r", 1, Games, null, null, Mobile, new[] { bad }, 50, "s"), "region " + bad);
        }

        [Test]
        public void Rule_CollectionsAreCopies()
        {
            var games = new List<string> { "Coral_Rescue" };
            var plats = new List<Platform> { Platform.iOS };
            var regs = new List<string> { "US" };
            var r = RolloutRule.Create("r", 1, games, null, null, plats, regs, 100, "s");
            games.Add("Dino_Islands");
            plats.Add(Platform.Android);
            regs.Add("DE");
            Assert.AreEqual(1, r.GameIds.Count);
            Assert.AreEqual("GameNotInRule", RolloutEvaluator.Evaluate(r, Ctx(game: "Dino_Islands")).Reason);
            Assert.AreEqual(1, r.Platforms.Count);
            Assert.AreEqual(1, r.Regions.Count);
            Assert.AreEqual("PlatformExcluded", RolloutEvaluator.Evaluate(r, Ctx(platform: Platform.Android)).Reason);
            Assert.AreEqual("RegionExcluded", RolloutEvaluator.Evaluate(r, Ctx(region: "DE")).Reason);
        }

        // ---- Evaluation ----

        [Test]
        public void Evaluate_ChildAlwaysExcluded_EvenAtFullRolloutAndAllMatching()
        {
            var r = Rule(100, null, null, new[] { Platform.iOS, Platform.Android, Platform.Editor, Platform.Other });
            for (int i = 0; i < 500; i++)
            {
                var d = RolloutEvaluator.Evaluate(r, Ctx("kid" + i, child: true));
                Assert.IsFalse(d.Included);
                Assert.AreEqual("ChildProfileExcluded", d.Reason);
            }
            // Child check precedes all others except the catalog game guard (e.g. invalid version).
            Assert.AreEqual("ChildProfileExcluded", RolloutEvaluator.Evaluate(r, Ctx(app: "garbage", child: true)).Reason);
        }

        [Test]
        public void Evaluate_GameScoping_OrdinalAndAfterChildExclusion()
        {
            var r = RolloutRule.Create("rule-g", 1, new[] { "Coral_Rescue", "Dino_Islands" }, null, null, Mobile, new string[0], 100, "s");
            Assert.IsTrue(RolloutEvaluator.Evaluate(r, Ctx(game: "Coral_Rescue")).Included);
            Assert.IsTrue(RolloutEvaluator.Evaluate(r, Ctx(game: "Dino_Islands")).Included);
            // Non-catalog / case-variant IDs (Ordinal) fail closed first, before child and rule-scope checks.
            foreach (var g in new[] { "coral_rescue", "Coral_Rescue ", "Coral", "Unknown_Game" })
            {
                Assert.AreEqual("UnknownGame", RolloutEvaluator.Evaluate(r, Ctx(game: g)).Reason, g);
                Assert.AreEqual("UnknownGame", RolloutEvaluator.Evaluate(r, Ctx(game: g, child: true)).Reason, g);
            }
            foreach (var g in new[] { "Wonder_Pop", "Garage_Legends" })
            {
                var d = RolloutEvaluator.Evaluate(r, Ctx(game: g));
                Assert.IsFalse(d.Included, g);
                Assert.AreEqual("GameNotInRule", d.Reason, g);
            }
            // Order (catalog games): child exclusion first, then rule game scope, before any version/platform/region/percent check.
            Assert.AreEqual("ChildProfileExcluded", RolloutEvaluator.Evaluate(r, Ctx(game: "Wonder_Pop", child: true)).Reason);
            Assert.AreEqual("GameNotInRule", RolloutEvaluator.Evaluate(Rule(0, "9.0.0", null, new[] { Platform.Android }, new[] { "CA" }),
                Ctx(game: "Wonder_Pop", app: "garbage", platform: Platform.iOS, region: "US")).Reason);
            CollectionAssert.AreEquivalent(new[] { "Coral_Rescue", "Dino_Islands" }, r.GameIds);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("1.2")]
        [TestCase("latest")]
        public void Evaluate_InvalidAppVersion_Excluded(string app)
        {
            var d = RolloutEvaluator.Evaluate(Rule(), Ctx(app: app));
            Assert.IsFalse(d.Included);
            Assert.AreEqual("InvalidAppVersion", d.Reason);
        }

        [TestCase("1.2.0", true)]
        [TestCase("1.2.0-rc.1", false)]
        [TestCase("1.1.99", false)]
        [TestCase("1.5.0", true)]
        [TestCase("2.0.0", true)]
        [TestCase("2.0.0-beta", true)]
        [TestCase("2.0.1", false)]
        public void Evaluate_VersionBoundsAreInclusive(string app, bool inRange)
        {
            var d = RolloutEvaluator.Evaluate(Rule(100, "1.2.0", "2.0.0"), Ctx(app: app));
            Assert.AreEqual(inRange, d.Included, app);
            Assert.AreEqual(inRange ? "Included" : "AppVersionOutOfRange", d.Reason);
        }

        [Test]
        public void Evaluate_OpenEndedBounds()
        {
            Assert.IsTrue(RolloutEvaluator.Evaluate(Rule(100, "1.0.0"), Ctx(app: "99.0.0")).Included);
            Assert.AreEqual("AppVersionOutOfRange", RolloutEvaluator.Evaluate(Rule(100, "1.0.0"), Ctx(app: "0.9.0")).Reason);
            Assert.IsTrue(RolloutEvaluator.Evaluate(Rule(100, null, "1.0.0"), Ctx(app: "0.0.1")).Included);
            Assert.AreEqual("AppVersionOutOfRange", RolloutEvaluator.Evaluate(Rule(100, null, "1.0.0"), Ctx(app: "1.0.1")).Reason);
        }

        [Test]
        public void Evaluate_PlatformFilter()
        {
            var r = Rule(100, null, null, new[] { Platform.Android });
            Assert.IsTrue(RolloutEvaluator.Evaluate(r, Ctx(platform: Platform.Android)).Included);
            foreach (var p in new[] { Platform.iOS, Platform.Editor, Platform.Other, (Platform)42 })
                Assert.AreEqual("PlatformExcluded", RolloutEvaluator.Evaluate(r, Ctx(platform: p)).Reason, p.ToString());
        }

        [Test]
        public void Evaluate_RegionFilter()
        {
            var r = Rule(100, null, null, null, new[] { "US", "CA" });
            Assert.IsTrue(RolloutEvaluator.Evaluate(r, Ctx(region: "US")).Included);
            Assert.IsTrue(RolloutEvaluator.Evaluate(r, Ctx(region: "CA")).Included);
            foreach (var reg in new[] { "DE", "us", null, "", "USA" })
                Assert.AreEqual("RegionExcluded", RolloutEvaluator.Evaluate(r, Ctx(region: reg)).Reason, "region " + reg);

            var all = Rule();
            Assert.IsTrue(RolloutEvaluator.Evaluate(all, Ctx(region: "DE")).Included, "empty region set = all regions");
            Assert.IsTrue(RolloutEvaluator.Evaluate(all, Ctx(region: null)).Included);
        }

        [Test]
        public void Evaluate_ReasonOrder_VersionBeforePlatformBeforeRegionBeforePercent()
        {
            var r = Rule(0, "2.0.0", null, new[] { Platform.Android }, new[] { "CA" });
            Assert.AreEqual("AppVersionOutOfRange", RolloutEvaluator.Evaluate(r, Ctx(app: "1.0.0", platform: Platform.iOS, region: "US")).Reason);
            Assert.AreEqual("PlatformExcluded", RolloutEvaluator.Evaluate(r, Ctx(app: "2.0.0", platform: Platform.iOS, region: "US")).Reason);
            Assert.AreEqual("RegionExcluded", RolloutEvaluator.Evaluate(r, Ctx(app: "2.0.0", platform: Platform.Android, region: "US")).Reason);
            Assert.AreEqual("OutsideRolloutPercent", RolloutEvaluator.Evaluate(r, Ctx(app: "2.0.0", platform: Platform.Android, region: "CA")).Reason);
        }

        [Test]
        public void Evaluate_PercentZeroNobody_HundredEveryone()
        {
            var zero = Rule(0);
            var full = Rule(100);
            for (int i = 0; i < 2000; i++)
            {
                var ctx = Ctx("player-" + i);
                var z = RolloutEvaluator.Evaluate(zero, ctx);
                Assert.IsFalse(z.Included);
                Assert.AreEqual("OutsideRolloutPercent", z.Reason);
                var f = RolloutEvaluator.Evaluate(full, ctx);
                Assert.IsTrue(f.Included);
                Assert.AreEqual("Included", f.Reason);
            }
        }

        [Test]
        public void Evaluate_PercentShareIsApproximatelyPercent()
        {
            var r = Rule(25);
            int included = 0;
            const int n = 40000;
            for (int i = 0; i < n; i++)
                if (RolloutEvaluator.Evaluate(r, Ctx("synthetic-" + i)).Included) included++;
            Assert.AreEqual(0.25, included / (double)n, 0.02);
        }

        [Test]
        public void Evaluate_RaisingPercentNeverRemovesIncludedPlayer()
        {
            const int n = 5000;
            var previouslyIncluded = new bool[n];
            for (int pct = 0; pct <= 100; pct++)
            {
                var r = Rule(pct);
                for (int i = 0; i < n; i++)
                {
                    bool inc = RolloutEvaluator.Evaluate(r, Ctx("player-" + i)).Included;
                    if (previouslyIncluded[i]) Assert.IsTrue(inc, "player-" + i + " dropped at " + pct + "%");
                    previouslyIncluded[i] = inc;
                }
            }
        }

        [Test]
        public void Evaluate_IsDeterministic_AndRuleVersionDoesNotReshuffle()
        {
            var v1 = RolloutRule.Create("rule-x", 1, Games, null, null, Mobile, new string[0], 40, "s");
            var v2 = RolloutRule.Create("rule-x", 7, Games, null, null, Mobile, new string[0], 40, "s");
            var otherSalt = RolloutRule.Create("rule-x", 1, Games, null, null, Mobile, new string[0], 40, "t");
            int differsBySalt = 0;
            for (int i = 0; i < 2000; i++)
            {
                var ctx = Ctx("player-" + i);
                bool a = RolloutEvaluator.Evaluate(v1, ctx).Included;
                Assert.AreEqual(a, RolloutEvaluator.Evaluate(v1, ctx).Included);
                Assert.AreEqual(a, RolloutEvaluator.Evaluate(v2, ctx).Included);
                if (a != RolloutEvaluator.Evaluate(otherSalt, ctx).Included) differsBySalt++;
            }
            Assert.Greater(differsBySalt, 0, "a new salt draws an independent cohort");
        }

        [Test]
        public void Evaluate_NullArgs_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => RolloutEvaluator.Evaluate(null, Ctx()));
            Assert.Throws<ArgumentNullException>(() => RolloutEvaluator.Evaluate(Rule(), null));
            Assert.Throws<ArgumentException>(() => new RolloutContext("", "p", "1.0.0", Platform.iOS, "US", false));
            Assert.Throws<ArgumentException>(() => new RolloutContext("g", "", "1.0.0", Platform.iOS, "US", false));
        }

        [Test]
        public void Context_ExposesOnlyNonSensitiveAttributes()
        {
            var names = new List<string>();
            foreach (var p in typeof(RolloutContext).GetProperties()) names.Add(p.Name);
            names.Sort(StringComparer.Ordinal);
            CollectionAssert.AreEqual(new[] { "AppVersion", "GameId", "IsChildProfile", "Platform", "PlayerId", "RegionCode" }, names);
        }

        // ---- Audit log ----

        private static readonly DateTime T0 = new DateTime(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);

        [Test]
        public void Audit_AppendsInOrder_AndRejectsNonIncreasingVersion()
        {
            var log = new RolloutAuditLog();
            log.Append("rule-1", 1, "ops", T0, "create 5%");
            log.Append("rule-1", 2, "ops", T0.AddHours(1), "expand to 25%");
            log.Append("rule-2", 1, "ops", T0, "create");
            log.Append("rule-1", 5, "ops", T0.AddHours(2), "expand to 50%");

            Assert.Throws<ArgumentException>(() => log.Append("rule-1", 5, "ops", T0, "dup"));
            Assert.Throws<ArgumentException>(() => log.Append("rule-1", 3, "ops", T0, "older"));
            Assert.Throws<ArgumentException>(() => log.Append("rule-2", 1, "ops", T0, "dup"));

            Assert.AreEqual(4, log.Entries.Count);
            Assert.AreEqual("rule-1", log.Entries[3].RuleId);
            Assert.AreEqual(5, log.Entries[3].Version);
            Assert.AreEqual("expand to 50%", log.Entries[3].Summary);
            Assert.AreEqual(T0.AddHours(2), log.Entries[3].UtcTimestamp);
        }

        [Test]
        public void Audit_InvalidEntries_RejectedWithoutSideEffects()
        {
            var log = new RolloutAuditLog();
            Assert.Throws<ArgumentException>(() => log.Append("", 1, "ops", T0, "s"));
            Assert.Throws<ArgumentOutOfRangeException>(() => log.Append("r", 0, "ops", T0, "s"));
            Assert.Throws<ArgumentException>(() => log.Append("r", 1, "", T0, "s"));
            Assert.Throws<ArgumentException>(() => log.Append("r", 1, "ops", T0, ""));
            Assert.Throws<ArgumentException>(() => log.Append("r", 1, "ops", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Local), "s"));
            Assert.Throws<ArgumentException>(() => log.Append("r", 1, "ops", new DateTime(2026, 1, 1), "s"));
            Assert.AreEqual(0, log.Entries.Count);
            log.Append("r", 1, "ops", T0, "s");
            Assert.AreEqual(1, log.Entries.Count);
        }

        [Test]
        public void Audit_ViewIsReadOnly_NoDeleteOrUpdateApi()
        {
            var log = new RolloutAuditLog();
            log.Append("r", 1, "ops", T0, "s");
            Assert.Throws<NotSupportedException>(() => ((IList<RolloutAuditEntry>)log.Entries).RemoveAt(0));
            Assert.Throws<NotSupportedException>(() => ((IList<RolloutAuditEntry>)log.Entries).Clear());
            var publicMethods = new List<string>();
            foreach (var m in typeof(RolloutAuditLog).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
                if (!m.IsSpecialName) publicMethods.Add(m.Name);
            CollectionAssert.AreEqual(new[] { "Append" }, publicMethods);
        }
    }
}
