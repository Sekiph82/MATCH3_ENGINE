using System;
using System.Collections.Generic;
using System.Linq;
using Match3.Engine.Runtime.Economy;
using Match3.Engine.Runtime.LiveOps;
using NUnit.Framework;
using R = Match3.Engine.Runtime.Economy.RewardLedgerReasons;

namespace Match3.Engine.Tests.Editor
{
    // ENG-M16-011.
    public sealed class RewardLedgerTests
    {
        private const string Game = "Coral_Rescue";
        private const string Player = "player-1";
        private static readonly DateTime T0 = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

        private static Dictionary<string, long> Coins(long n) => new Dictionary<string, long> { ["Coins"] = n };

        private static RewardLedgerRequest Req(string source, string instance, IReadOnlyDictionary<string, long> amounts,
            DateTime? at = null, DateTime? expires = null, string reference = null, string game = Game, string player = Player) =>
            new RewardLedgerRequest(game, player, source, instance, amounts, at ?? T0, expires, reference);

        private static RewardLedger Ledger(string game = Game, IReadOnlyDictionary<string, long> opening = null) =>
            new RewardLedger(game, Player, opening);

        private static RewardLedger Restore(string snapshot)
        {
            var r = RewardLedger.FromSnapshot(snapshot);
            Assert.That(r.Succeeded, Is.True, r.Reason);
            return r.Ledger;
        }

        // ---- key scheme ---------------------------------------------------------------------------

        [Test]
        public void Key_IsLengthPrefixedOrdinal_AndUnambiguous()
        {
            Assert.That(RewardLedgerKey.Derive("Coral_Rescue", "p", "first_clear", "L1"),
                Is.EqualTo("rgk1|12:Coral_Rescue|1:p|11:first_clear|2:L1|"));
            Assert.That(RewardLedgerKey.Derive(Game, "a|1:b", "daily", "c"),
                Is.Not.EqualTo(RewardLedgerKey.Derive(Game, "a", "daily", "b|1:c")));
            Assert.That(RewardLedgerKey.Derive(Game, "P", "daily", "x"), Is.Not.EqualTo(RewardLedgerKey.Derive(Game, "p", "daily", "x")));
        }

        // ---- TryGrant -----------------------------------------------------------------------------

        [Test]
        public void TryGrant_Applied_UpdatesBalanceAndRecordsKey()
        {
            var l = Ledger();
            var r = l.TryGrant(Req(RewardSources.FirstClear, "level-1", new Dictionary<string, long> { ["Coins"] = 50, ["Boosters"] = 2 }));
            Assert.That(r.Outcome, Is.EqualTo(RewardLedgerOutcome.Applied));
            Assert.That(r.Record.Key, Is.EqualTo(RewardLedgerKey.Derive(Game, Player, "first_clear", "level-1")));
            Assert.That(l.Balance(CurrencyType.Coins), Is.EqualTo(50));
            Assert.That(l.Balance(CurrencyType.Boosters), Is.EqualTo(2));
            Assert.That(l.Count, Is.EqualTo(1));
        }

        [Test]
        public void ReplayStorm_SameKey1000Times_GrantsOnce_ReturnsOriginal()
        {
            var l = Ledger();
            var first = l.TryGrant(Req(RewardSources.Chest, "chest-7", Coins(25), T0)).Record;
            for (var i = 1; i <= 1000; i++)
            {
                var r = l.TryGrant(Req(RewardSources.Chest, "chest-7", Coins(25), T0.AddSeconds(i)));
                Assert.That(r.Outcome, Is.EqualTo(RewardLedgerOutcome.Duplicate));
                Assert.That(r.Record, Is.SameAs(first));
                Assert.That(r.Record.GrantedUtc, Is.EqualTo(T0));
            }
            Assert.That(l.Balance(CurrencyType.Coins), Is.EqualTo(25));
            Assert.That(l.Count, Is.EqualTo(1));
        }

        [Test]
        public void ConflictingDuplicate_RejectedIdempotencyConflict_NoStateChange()
        {
            var l = Ledger();
            l.TryGrant(Req(RewardSources.Daily, "2026-11-01", Coins(10), reference: "ev-1"));
            var before = l.ToSnapshot();
            Assert.That(l.TryGrant(Req(RewardSources.Daily, "2026-11-01", Coins(11), reference: "ev-1")).Reason, Is.EqualTo(R.IdempotencyConflict));
            Assert.That(l.TryGrant(Req(RewardSources.Daily, "2026-11-01", Coins(10), reference: "ev-2")).Reason, Is.EqualTo(R.IdempotencyConflict));
            Assert.That(l.TryGrant(Req(RewardSources.Daily, "2026-11-01", Coins(10), expires: T0.AddDays(1), reference: "ev-1")).Reason,
                Is.EqualTo(R.IdempotencyConflict));
            Assert.That(l.ToSnapshot(), Is.EqualTo(before));
        }

        [Test]
        public void InterleavedSourcesAndReplays_BalanceEqualsSumOfUniqueGrants()
        {
            var l = Ledger();
            var rng = new SplitMix64(42);
            var unique = new HashSet<string>(StringComparer.Ordinal);
            long expected = 0;
            for (var i = 0; i < 2000; i++)
            {
                var source = RewardSources.All[(int)(rng.Next() % 12)]; // excludes ad_ssv (index 12)
                var instance = "i" + rng.Next() % 40;
                var amount = source.Length + instance.Length; // deterministic per key => replays are not conflicts
                var r = l.TryGrant(Req(source, instance, Coins(amount), T0.AddMinutes(i)));
                if (unique.Add(source + "/" + instance))
                {
                    Assert.That(r.Outcome, Is.EqualTo(RewardLedgerOutcome.Applied));
                    expected += amount;
                }
                else Assert.That(r.Outcome, Is.EqualTo(RewardLedgerOutcome.Duplicate));
            }
            Assert.That(l.Count, Is.EqualTo(unique.Count));
            Assert.That(l.Balance("Coins"), Is.EqualTo(expected));
            // Same instance id under another source or player is a different key.
            Assert.That(l.TryGrant(Req(RewardSources.Daily, "i0", Coins(1), player: "player-2")).Reason, Is.EqualTo(R.LedgerOwnerMismatch));
        }

        [Test]
        public void InvalidRequests_RejectedWithoutStateChange()
        {
            var l = Ledger();
            var before = l.ToSnapshot();
            Assert.That(l.TryGrant(Req("loot_box", "x", Coins(1))).Reason, Is.EqualTo(R.UnknownRewardSource));
            Assert.That(l.TryGrant(Req(RewardSources.Daily, "", Coins(1))).Reason, Is.EqualTo(R.InvalidSourceInstance));
            Assert.That(l.TryGrant(Req(RewardSources.Daily, new string('x', 129), Coins(1))).Reason, Is.EqualTo(R.InvalidSourceInstance));
            Assert.That(l.TryGrant(Req(RewardSources.Daily, "x", Coins(0))).Reason, Is.EqualTo(R.InvalidAmounts));
            Assert.That(l.TryGrant(Req(RewardSources.Daily, "x", Coins(-5))).Reason, Is.EqualTo(R.InvalidAmounts));
            Assert.That(l.TryGrant(Req(RewardSources.Daily, "x", new Dictionary<string, long>())).Reason, Is.EqualTo(R.InvalidAmounts));
            Assert.That(l.TryGrant(Req(RewardSources.Daily, "x", new Dictionary<string, long> { ["bad id"] = 1 })).Reason, Is.EqualTo(R.InvalidAmounts));
            Assert.That(l.TryGrant(Req(RewardSources.Daily, "x", Coins(RewardLedger.MaxAmountPerAsset + 1))).Reason, Is.EqualTo(R.InvalidAmounts));
            Assert.That(l.TryGrant(Req(RewardSources.Daily, "x", Coins(1), new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Local))).Reason,
                Is.EqualTo(R.NonUtcTimestamp));
            Assert.That(l.TryGrant(Req(RewardSources.Daily, "x", Coins(1), new DateTime(2026, 11, 1))).Reason, Is.EqualTo(R.NonUtcTimestamp));
            Assert.That(l.TryGrant(Req(RewardSources.Weekly, "w45", Coins(1), T0, T0.AddTicks(-1))).Reason, Is.EqualTo(R.SourceExpired));
            Assert.That(l.TryGrant(Req(RewardSources.Daily, "x", Coins(1), reference: "bad\nref")).Reason, Is.EqualTo(R.InvalidReference));
            Assert.That(l.TryGrant(Req(RewardSources.Daily, "x", Coins(1), game: "Dino_Islands")).Reason, Is.EqualTo(R.LedgerOwnerMismatch));
            Assert.That(l.ToSnapshot(), Is.EqualTo(before));
            Assert.Throws<ArgumentException>(() => new RewardLedger("Unknown_Game", Player));
        }

        [Test]
        public void Overflow_Rejected_NoPartialMultiAssetApply()
        {
            var l = Ledger(opening: new Dictionary<string, long> { ["Coins"] = 0, ["Hearts"] = long.MaxValue - 5 });
            var r = l.TryGrant(Req(RewardSources.Chest, "c1", new Dictionary<string, long> { ["Coins"] = 100, ["Hearts"] = 10 }));
            Assert.That(r.Reason, Is.EqualTo(R.BalanceOverflow));
            Assert.That(l.Balance("Coins"), Is.EqualTo(0));
            Assert.That(l.Balance("Hearts"), Is.EqualTo(long.MaxValue - 5));
            Assert.That(l.Count, Is.EqualTo(0));
            Assert.That(l.TryGrant(Req(RewardSources.Chest, "c1", new Dictionary<string, long> { ["Hearts"] = 5 })).Outcome, Is.EqualTo(RewardLedgerOutcome.Applied));
            Assert.That(l.Balance("Hearts"), Is.EqualTo(long.MaxValue));
            Assert.That(l.TryGrant(Req(RewardSources.Chest, "c2", new Dictionary<string, long> { ["Hearts"] = 1 })).Reason, Is.EqualTo(R.BalanceOverflow));
        }

        // ---- child / paid policy ------------------------------------------------------------------

        [Test]
        public void ChildProfile_PaidGrantsRejected_FreeGrantsAllowed()
        {
            var l = Ledger("Wonder_Pop");
            Assert.That(l.TryGrant(Req(RewardSources.LevelWin, "l1", new Dictionary<string, long> { ["PremiumGems"] = 5 }, game: "Wonder_Pop")).Reason,
                Is.EqualTo(R.PaidGrantNotAllowed));
            Assert.That(l.TryGrant(Req(RewardSources.Purchase, "order-1", Coins(500), game: "Wonder_Pop")).Reason, Is.EqualTo(R.PaidGrantNotAllowed));
            Assert.That(l.TryGrant(Req(RewardSources.AdServerSideVerified, "ad-1", Coins(5), game: "Wonder_Pop")).Reason, Is.EqualTo(R.AdRewardNotAllowed));
            Assert.That(l.TryGrant(Req(RewardSources.FirstClear, "l1", Coins(5), game: "Wonder_Pop")).Outcome, Is.EqualTo(RewardLedgerOutcome.Applied));
            Assert.That(l.Balance(CurrencyType.PremiumGems), Is.EqualTo(0));
            Assert.That(l.Count, Is.EqualTo(1));
        }

        [Test]
        public void MonetizedProfile_PaidGrantsAccepted()
        {
            var l = Ledger();
            Assert.That(l.TryGrant(Req(RewardSources.Purchase, "order-1", new Dictionary<string, long> { ["PremiumGems"] = 100 })).Outcome,
                Is.EqualTo(RewardLedgerOutcome.Applied));
            Assert.That(l.TryGrant(Req(RewardSources.AdServerSideVerified, "ad-1", Coins(5))).Outcome, Is.EqualTo(RewardLedgerOutcome.Applied));
            Assert.That(l.Balance(CurrencyType.PremiumGems), Is.EqualTo(100));
        }

        // ---- persistence --------------------------------------------------------------------------

        private static RewardLedger Populated()
        {
            var l = Ledger(opening: new Dictionary<string, long> { ["Coins"] = 1000 });
            l.TryGrant(Req(RewardSources.FirstClear, "level-1", Coins(50)));
            l.TryGrant(Req(RewardSources.Weekly, "2026-W45", Coins(30), T0, T0.AddDays(3), "week-claim"));
            l.TryGrant(Req(RewardSources.Purchase, "order|1", new Dictionary<string, long> { ["PremiumGems"] = 100 }, reference: "é-receipt"));
            return l;
        }

        [Test]
        public void Snapshot_RoundTrip_KeepsKeysBalancesAndCanonicalText()
        {
            var l = Populated();
            var snap = l.ToSnapshot();
            var restored = Restore(snap);
            Assert.That(restored.ToSnapshot(), Is.EqualTo(snap));
            Assert.That(restored.Balance("Coins"), Is.EqualTo(1080));
            Assert.That(restored.Balance("PremiumGems"), Is.EqualTo(100));
            // Restart: replays after restore are still duplicates.
            Assert.That(restored.TryGrant(Req(RewardSources.FirstClear, "level-1", Coins(50), T0.AddDays(9))).Outcome, Is.EqualTo(RewardLedgerOutcome.Duplicate));
            Assert.That(restored.TryGrant(Req(RewardSources.Purchase, "order|1", new Dictionary<string, long> { ["PremiumGems"] = 100 },
                reference: "é-receipt")).Outcome, Is.EqualTo(RewardLedgerOutcome.Duplicate));
            Assert.That(restored.Balance("Coins"), Is.EqualTo(1080));
            Assert.That(snap.Split('\n').Length, Is.EqualTo(4));
            StringAssert.StartsWith("{\"format\":\"m3-reward-ledger\",\"gameId\":\"Coral_Rescue\"", snap);
        }

        [Test]
        public void Snapshot_Tampered_Rejected()
        {
            var snap = Populated().ToSnapshot();
            Assert.That(RewardLedger.FromSnapshot(snap.Replace("\"Coins\":50", "\"Coins\":5000")).Reason, Is.EqualTo(R.IntegrityMismatch));
            Assert.That(RewardLedger.FromSnapshot(snap.Replace("\"Coins\":1000", "\"Coins\":9000")).Reason, Is.EqualTo(R.IntegrityMismatch));
            var lines = snap.Split('\n');
            Assert.That(RewardLedger.FromSnapshot(string.Join("\n", lines.Take(3))).Reason, Is.EqualTo(R.IntegrityMismatch), "dropped record");
            Assert.That(RewardLedger.FromSnapshot(snap + "\n" + lines[1]).Reason, Is.EqualTo(R.IntegrityMismatch), "duplicated record");
            Assert.That(RewardLedger.FromSnapshot(snap.Replace("\"version\":1", "\"version\":2")).Reason, Is.EqualTo(R.UnsupportedSnapshotVersion));
            Assert.That(RewardLedger.FromSnapshot(snap.Replace("{\"format\"", "{ \"format\"")).Reason, Is.EqualTo(R.NonCanonicalSnapshot));
            Assert.That(RewardLedger.FromSnapshot(snap.Substring(0, snap.Length - 3)).Reason, Is.EqualTo(R.MalformedSnapshot));
            Assert.That(RewardLedger.FromSnapshot("").Reason, Is.EqualTo(R.MalformedSnapshot));
            Assert.That(RewardLedger.FromSnapshot(null).Reason, Is.EqualTo(R.MalformedSnapshot));
            Assert.That(RewardLedger.FromSnapshot("[]").Reason, Is.EqualTo(R.MalformedSnapshot));
        }

        [Test]
        public void Snapshot_RehashedPaidInjectionForChildProfile_StillRejected()
        {
            // An attacker can recompute an unkeyed SHA-256; policy is re-enforced on every restored record.
            var donor = Ledger();
            donor.TryGrant(Req(RewardSources.Purchase, "order-1", new Dictionary<string, long> { ["PremiumGems"] = 100 }));
            var forged = donor.ToSnapshot().Split('\n');
            var header = (Dictionary<string, object>)CanonicalJson.Parse(Ledger("Wonder_Pop").ToSnapshot());
            header.Remove("sha256");
            header["sha256"] = CanonicalJson.Sha256Hex(new List<object> { header, new List<object> { CanonicalJson.Parse(forged[1]) } });
            var text = CanonicalJson.Serialize(header) + "\n" + forged[1];
            Assert.That(RewardLedger.FromSnapshot(text).Reason, Is.EqualTo(R.MalformedSnapshot + ":" + R.PaidGrantNotAllowed));
        }

        // ---- pruning ------------------------------------------------------------------------------

        [Test]
        public void Prune_FoldsOnlyExpiredPastRetention_NeverReplayable_AndBlocksSettledReplays()
        {
            var l = new RewardLedger(Game, Player, null, new RewardRetentionPolicy(180, new Dictionary<string, int> { [RewardSources.Daily] = 30 }));
            l.TryGrant(Req(RewardSources.FirstClear, "level-1", Coins(1)));              // replayable: never pruned
            l.TryGrant(Req(RewardSources.Daily, "d1", Coins(10), T0, T0.AddDays(1)));       // prunable after 31 days
            l.TryGrant(Req(RewardSources.Weekly, "w1", Coins(100), T0, T0.AddDays(7)));     // prunable after 187 days
            Assert.That(l.Prune(T0.AddDays(31).AddTicks(-1)), Is.EqualTo(0));
            Assert.That(l.Prune(T0.AddDays(31)), Is.EqualTo(1));
            Assert.That(l.Prune(T0.AddDays(10)), Is.EqualTo(0), "horizon is monotonic");
            Assert.That(l.Prune(T0.AddDays(5000)), Is.EqualTo(1));
            Assert.That(l.Count, Is.EqualTo(1));
            Assert.That(l.SettledCount, Is.EqualTo(2));
            Assert.That(l.Balance("Coins"), Is.EqualTo(111));
            Assert.That(l.TryGrant(Req(RewardSources.Daily, "d1", Coins(10), T0, T0.AddDays(1))).Reason, Is.EqualTo(R.SourceSettled));
            Assert.That(l.TryGrant(Req(RewardSources.FirstClear, "level-1", Coins(1), T0.AddDays(5000))).Outcome, Is.EqualTo(RewardLedgerOutcome.Duplicate));
            var restored = Restore(l.ToSnapshot());
            Assert.That(restored.Balance("Coins"), Is.EqualTo(111));
            Assert.That(restored.SettledThroughUtc, Is.EqualTo(T0.AddDays(5000)));
            Assert.That(restored.TryGrant(Req(RewardSources.Weekly, "w1", Coins(100), T0, T0.AddDays(7))).Reason, Is.EqualTo(R.SourceSettled));
        }

        // ---- merge --------------------------------------------------------------------------------

        [Test]
        public void Merge_UnionOfKeys_SameKeyGrantedOnce_BalanceIsStartPlusUniqueSum()
        {
            var opening = new Dictionary<string, long> { ["Coins"] = 500 };
            var cloud = Ledger(opening: opening);
            var local = Ledger(opening: opening);
            cloud.TryGrant(Req(RewardSources.FirstClear, "level-1", Coins(50), T0));
            cloud.TryGrant(Req(RewardSources.Daily, "d1", Coins(10), T0));
            local.TryGrant(Req(RewardSources.FirstClear, "level-1", Coins(50), T0.AddHours(2))); // same grant, offline on device
            local.TryGrant(Req(RewardSources.Chest, "c1", Coins(7), T0));
            var m = RewardLedger.Merge(cloud, local);
            Assert.That(m.Succeeded, Is.True, m.Reason);
            Assert.That(m.ConflictedKeys, Is.Empty);
            Assert.That(m.Ledger.Count, Is.EqualTo(3));
            Assert.That(m.Ledger.Balance("Coins"), Is.EqualTo(500 + 50 + 10 + 7));
            m.Ledger.TryGetRecord(RewardLedgerKey.Derive(Game, Player, "first_clear", "level-1"), out var rec);
            Assert.That(rec.GrantedUtc, Is.EqualTo(T0), "earliest grant time kept");
            Assert.That(m.Ledger.TryGrant(Req(RewardSources.Chest, "c1", Coins(7))).Outcome, Is.EqualTo(RewardLedgerOutcome.Duplicate));
        }

        [Test]
        public void Merge_ConflictFlagged_CountedOnce()
        {
            var a = Ledger();
            var b = Ledger();
            a.TryGrant(Req(RewardSources.Tournament, "s1", Coins(100), T0, T0.AddDays(7), "rank-1"));
            b.TryGrant(Req(RewardSources.Tournament, "s1", Coins(300), T0, T0.AddDays(7), "rank-1"));
            var m = RewardLedger.Merge(a, b);
            Assert.That(m.ConflictedKeys, Is.EqualTo(new[] { RewardLedgerKey.Derive(Game, Player, "tournament", "s1") }));
            Assert.That(m.Ledger.Count, Is.EqualTo(1));
            Assert.That(m.Ledger.Balance("Coins"), Is.EqualTo(100).Or.EqualTo(300));
            Assert.That(RewardLedger.Merge(b, a).Ledger.ToSnapshot(), Is.EqualTo(m.Ledger.ToSnapshot()));
        }

        [Test]
        public void Merge_IsCommutativeAndAssociative()
        {
            var s = new[] { Ledger(), Ledger(), Ledger() };
            var rng = new SplitMix64(7);
            for (var i = 0; i < 300; i++)
            {
                var l = s[(int)(rng.Next() % 3)];
                var src = new[] { RewardSources.Daily, RewardSources.Weekly, RewardSources.FirstClear, RewardSources.Tournament }[(int)(rng.Next() % 4)];
                var inst = "k" + rng.Next() % 25;
                var amount = 1 + (long)(rng.Next() % 3);             // sometimes conflicting payloads
                var expires = src == RewardSources.FirstClear ? (DateTime?)null : T0.AddDays(1 + (long)(rng.Next() % 2));
                l.TryGrant(Req(src, inst, Coins(amount), T0.AddMinutes((long)(rng.Next() % 50)), expires));
            }
            Assert.That(s[1].Prune(T0.AddDays(181.5)), Is.GreaterThan(0)); // one device settled the day-1 windows only
            string M(RewardLedger x, RewardLedger y) => RewardLedger.Merge(x, y).Ledger.ToSnapshot();
            RewardLedger L(RewardLedger x, RewardLedger y) => RewardLedger.Merge(x, y).Ledger;
            Assert.That(M(s[0], s[1]), Is.EqualTo(M(s[1], s[0])));
            Assert.That(M(s[1], s[2]), Is.EqualTo(M(s[2], s[1])));
            Assert.That(M(L(s[0], s[1]), s[2]), Is.EqualTo(M(s[0], L(s[1], s[2]))));
            Assert.That(M(L(s[0], s[2]), s[1]), Is.EqualTo(M(s[0], L(s[2], s[1]))));
            Assert.That(M(s[0], s[0]), Is.EqualTo(s[0].ToSnapshot()), "idempotent");
        }

        [Test]
        public void Merge_SettledSegment_NoDoubleCount()
        {
            var a = Ledger();
            a.TryGrant(Req(RewardSources.Weekly, "w1", Coins(100), T0, T0.AddDays(7)));
            var b = Restore(a.ToSnapshot());
            a.Prune(T0.AddDays(200)); // a folded w1 into its settled segment
            var m = RewardLedger.Merge(a, b);
            Assert.That(m.Ledger.Balance("Coins"), Is.EqualTo(100));
            Assert.That(m.Ledger.Count, Is.EqualTo(0));
            Assert.That(m.Ledger.TryGrant(Req(RewardSources.Weekly, "w1", Coins(100), T0, T0.AddDays(7))).Reason, Is.EqualTo(R.SourceSettled));
        }

        [Test]
        public void Merge_RejectsDifferentOwnerOpeningOrRetention()
        {
            Assert.That(RewardLedger.Merge(Ledger(), new RewardLedger(Game, "player-2")).Reason, Is.EqualTo(R.MergeOwnerMismatch));
            Assert.That(RewardLedger.Merge(Ledger(), Ledger("Dino_Islands")).Reason, Is.EqualTo(R.MergeOwnerMismatch));
            Assert.That(RewardLedger.Merge(Ledger(), Ledger(opening: Coins(1))).Reason, Is.EqualTo(R.OpeningBalanceMismatch));
            Assert.That(RewardLedger.Merge(Ledger(), new RewardLedger(Game, Player, null, new RewardRetentionPolicy(30))).Reason,
                Is.EqualTo(R.RetentionPolicyMismatch));
        }
    }
}
