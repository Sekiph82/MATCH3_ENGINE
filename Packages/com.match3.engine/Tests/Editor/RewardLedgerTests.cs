using System;
using System.Collections.Generic;
using System.Linq;
using Match3.Engine.Runtime.Economy;
using Match3.Engine.Runtime.LiveOps;
using NUnit.Framework;
using M08 = Match3.Engine.Runtime;
using R = Match3.Engine.Runtime.Economy.RewardLedgerReasons;

// These tests deliberately exercise the [Obsolete] one-argument TryGrant(request) compatibility overload.
#pragma warning disable 0618
namespace Match3.Engine.Tests.Editor
{
    // ENG-M16-011.
    public sealed class RewardLedgerTests
    {
        private const string Game = "Coral_Rescue";
        private const string Player = "player-1";
        private static readonly DateTime T0 = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
        private static readonly TrustedUtcAnchor Now = TrustedUtcAnchor.Server(T0);

        private static Dictionary<string, long> Coins(long n) => new Dictionary<string, long> { ["Coins"] = n };
        private static TrustedUtcAnchor At(DateTime utc) => TrustedUtcAnchor.Server(utc);

        private static RewardLedgerRequest Req(string source, string instance, IReadOnlyDictionary<string, long> amounts,
            DateTime? at = null, DateTime? expires = null, string reference = null, string game = Game, string player = Player) =>
            new RewardLedgerRequest(game, player, source, instance, amounts, at ?? T0, expires, reference);

        private static RewardLedgerGrantResult G(RewardLedger l, RewardLedgerRequest r) => l.TryGrant(r, Now);

        private static RewardLedger Ledger(string game = Game, IReadOnlyDictionary<string, long> opening = null) =>
            new RewardLedger(game, Player, opening);

        private static RewardLedger Restore(string snapshot)
        {
            var r = RewardLedger.FromSnapshot(snapshot);
            Assert.That(r.Succeeded, Is.True, r.Reason);
            return r.Ledger;
        }

        private static RewardLedger Merged(RewardLedger a, RewardLedger b)
        {
            var r = RewardLedger.Merge(a, b);
            Assert.That(r.Succeeded, Is.True, r.Reason);
            return r.Ledger;
        }

        private static string Forge(Dictionary<string, object> header, params string[] lines)
        {
            header.Remove("sha256");
            var sha = CanonicalJson.Sha256Hex(new List<object> { header, lines.Select(CanonicalJson.Parse).ToList() });
            header["sha256"] = sha;
            return CanonicalJson.Serialize(header) + string.Concat(lines.Select(l => "\n" + l));
        }

        private static Dictionary<string, object> HeaderOf(RewardLedger l) =>
            (Dictionary<string, object>)CanonicalJson.Parse(l.ToSnapshot().Split('\n')[0]);

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
            var first = G(l, Req(RewardSources.Weekly, "2026-W45", Coins(25), T0, T0.AddDays(3))).Record;
            for (var i = 1; i <= 1000; i++)
            {
                var r = l.TryGrant(Req(RewardSources.Weekly, "2026-W45", Coins(25), T0.AddSeconds(i), T0.AddDays(3)), At(T0.AddSeconds(i)));
                Assert.That(r.Outcome, Is.EqualTo(RewardLedgerOutcome.Duplicate));
                Assert.That(r.Record, Is.SameAs(first));
            }
            Assert.That(l.Balance(CurrencyType.Coins), Is.EqualTo(25));
            Assert.That(l.Count, Is.EqualTo(1));
        }

        [Test]
        public void ConflictingDuplicate_RejectedIdempotencyConflict_NoStateChange()
        {
            var l = Ledger();
            G(l, Req(RewardSources.Daily, "2026-11-01", Coins(10), expires: T0.AddDays(1), reference: "ev-1"));
            var before = l.ToSnapshot();
            Assert.That(G(l, Req(RewardSources.Daily, "2026-11-01", Coins(11), expires: T0.AddDays(1), reference: "ev-1")).Reason, Is.EqualTo(R.IdempotencyConflict));
            Assert.That(G(l, Req(RewardSources.Daily, "2026-11-01", Coins(10), expires: T0.AddDays(1), reference: "ev-2")).Reason, Is.EqualTo(R.IdempotencyConflict));
            Assert.That(G(l, Req(RewardSources.Daily, "2026-11-01", Coins(10), expires: T0.AddDays(2), reference: "ev-1")).Reason, Is.EqualTo(R.IdempotencyConflict));
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
                var source = RewardSources.All[(int)(rng.Next() % (ulong)RewardSources.All.Count)];
                var instance = "i" + rng.Next() % 40;
                var amount = source.Length + instance.Length; // a function of the key, so replays are never conflicts
                var expires = RewardSources.IsWindowed(source) ? T0.AddDays(1) : (DateTime?)null;
                var r = l.TryGrant(Req(source, instance, Coins(amount), T0.AddMinutes(i), expires), At(T0.AddMinutes(i % 60)));
                if (unique.Add(source + "/" + instance))
                {
                    Assert.That(r.Outcome, Is.EqualTo(RewardLedgerOutcome.Applied), source + " " + r.Reason);
                    expected += amount;
                }
                else Assert.That(r.Outcome, Is.EqualTo(RewardLedgerOutcome.Duplicate));
            }
            Assert.That(l.Count, Is.EqualTo(unique.Count));
            Assert.That(l.Balance("Coins"), Is.EqualTo(expected));
            // The same instance id under different sources maps to different keys (both were granted above).
            Assert.That(RewardLedgerKey.Derive(Game, Player, "daily", "i0"), Is.Not.EqualTo(RewardLedgerKey.Derive(Game, Player, "chest", "i0")));
            // A request for another player never touches this ledger.
            Assert.That(l.TryGrant(Req(RewardSources.Chest, "i0", Coins(1), player: "player-2")).Reason, Is.EqualTo(R.LedgerOwnerMismatch));
        }

        [Test]
        public void InvalidRequests_RejectedWithoutStateChange()
        {
            var l = Ledger();
            var before = l.ToSnapshot();
            Assert.That(l.TryGrant(Req("loot_box", "x", Coins(1))).Reason, Is.EqualTo(R.UnknownRewardSource));
            Assert.That(l.TryGrant(Req(RewardSources.Chest, "", Coins(1))).Reason, Is.EqualTo(R.InvalidSourceInstance));
            Assert.That(l.TryGrant(Req(RewardSources.Chest, new string('x', 129), Coins(1))).Reason, Is.EqualTo(R.InvalidSourceInstance));
            Assert.That(l.TryGrant(Req(RewardSources.Chest, "x", Coins(0))).Reason, Is.EqualTo(R.InvalidAmounts));
            Assert.That(l.TryGrant(Req(RewardSources.Chest, "x", Coins(-5))).Reason, Is.EqualTo(R.InvalidAmounts));
            Assert.That(l.TryGrant(Req(RewardSources.Chest, "x", new Dictionary<string, long>())).Reason, Is.EqualTo(R.InvalidAmounts));
            Assert.That(l.TryGrant(Req(RewardSources.Chest, "x", Coins(RewardLedger.MaxAmountPerAsset + 1))).Reason, Is.EqualTo(R.InvalidAmounts));
            Assert.That(l.TryGrant(Req(RewardSources.Chest, "x", Coins(1), new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Local))).Reason,
                Is.EqualTo(R.NonUtcTimestamp));
            Assert.That(l.TryGrant(Req(RewardSources.Chest, "x", Coins(1), new DateTime(2026, 11, 1))).Reason, Is.EqualTo(R.NonUtcTimestamp));
            Assert.That(l.TryGrant(Req(RewardSources.Chest, "x", Coins(1), reference: "bad\nref")).Reason, Is.EqualTo(R.InvalidReference));
            Assert.That(l.TryGrant(Req(RewardSources.Chest, "x", Coins(1), game: "Dino_Islands")).Reason, Is.EqualTo(R.LedgerOwnerMismatch));
            Assert.That(l.ToSnapshot(), Is.EqualTo(before));
            Assert.Throws<ArgumentException>(() => new RewardLedger("Unknown_Game", Player));
        }

        [Test]
        public void AssetRegistry_IsOrdinal_NoCaseOrSpellingEvasion()
        {
            var l = Ledger("Wonder_Pop");
            foreach (var evasive in new[] { "premiumgems", "Premium_Gems", "PREMIUMGEMS", "coins", "Gems" })
                Assert.That(l.TryGrant(Req(RewardSources.Chest, "c-" + evasive, new Dictionary<string, long> { [evasive] = 5 }, game: "Wonder_Pop")).Reason,
                    Is.EqualTo(R.InvalidAmounts), evasive);
            Assert.That(l.Count, Is.EqualTo(0));
            Assert.Throws<ArgumentException>(() => Ledger(opening: new Dictionary<string, long> { ["coins"] = 1 }));
        }

        [Test]
        public void ExpiryPolicy_WindowedRequireExpiry_PermanentForbidIt()
        {
            var l = Ledger();
            foreach (var s in RewardSources.Windowed)
                Assert.That(G(l, Req(s, "x", Coins(1))).Reason, Is.EqualTo(R.ExpiryRequired), s);
            foreach (var s in RewardSources.All.Except(RewardSources.Windowed))
                Assert.That(G(l, Req(s, "x", Coins(1), expires: T0.AddDays(1))).Reason, Is.EqualTo(R.ExpiryNotAllowed), s);
            Assert.That(l.Count, Is.EqualTo(0));
        }

        [Test]
        public void WindowedGrant_RequiresServerAnchor_ExpiryJudgedByAnchorNotCallerTime()
        {
            var l = Ledger();
            var req = Req(RewardSources.Weekly, "w45", Coins(5), T0, T0.AddDays(7));
            Assert.That(l.TryGrant(req, default(TrustedUtcAnchor)).Reason, Is.EqualTo(R.TrustedTimeAnchorRequired));
            Assert.That(l.TryGrant(req, TrustedUtcAnchor.DeviceClock(T0)).Reason, Is.EqualTo(R.TrustedTimeAnchorRequired));
            // Compatibility overload: GrantedUtc is the caller-asserted server time.
            Assert.That(l.TryGrant(Req(RewardSources.Weekly, "w46", Coins(5), T0.AddDays(8), T0.AddDays(7))).Reason, Is.EqualTo(R.SourceExpired));
            // Caller claims an in-window GrantedUtc, but the trusted anchor is after expiry.
            Assert.That(l.TryGrant(req, At(T0.AddDays(7).AddTicks(1))).Reason, Is.EqualTo(R.SourceExpired));
            Assert.That(l.Count, Is.EqualTo(0));
            Assert.That(l.TryGrant(req, At(T0.AddDays(7))).Outcome, Is.EqualTo(RewardLedgerOutcome.Applied));
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

        // ---- family-safe / paid policy ------------------------------------------------------------

        [Test]
        public void FamilySafeProfile_PaidGrantsRejected_FreeGrantsAllowed()
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
        public void FamilySafeProfile_PaidOpeningAndSettledTotalsRejected()
        {
            Assert.Throws<ArgumentException>(() => Ledger("Wonder_Pop", new Dictionary<string, long> { ["PremiumGems"] = 5 }));
            Assert.That(Ledger("Wonder_Pop", new Dictionary<string, long> { ["PremiumGems"] = 0, ["Coins"] = 5 }).Balance("Coins"), Is.EqualTo(5));

            var header = HeaderOf(Ledger("Wonder_Pop"));
            header["opening"] = new Dictionary<string, object> { ["PremiumGems"] = 5L };
            Assert.That(RewardLedger.FromSnapshot(Forge(header)).Reason, Is.EqualTo(R.MalformedSnapshot));

            var tomb = "{\"amounts\":{\"PremiumGems\":5},\"digest\":\"" + new string('a', 64) +
                       "\",\"instance\":\"w1\",\"source\":\"weekly\",\"state\":\"settled\"}";
            Assert.That(RewardLedger.FromSnapshot(Forge(HeaderOf(Ledger("Wonder_Pop")), tomb)).Reason,
                Is.EqualTo(R.MalformedSnapshot + ":" + R.PaidGrantNotAllowed));

            // A re-hashed live IAP record is re-checked on restore too.
            var donor = Ledger();
            donor.TryGrant(Req(RewardSources.Purchase, "order-1", new Dictionary<string, long> { ["PremiumGems"] = 100 }));
            Assert.That(RewardLedger.FromSnapshot(Forge(HeaderOf(Ledger("Wonder_Pop")), donor.ToSnapshot().Split('\n')[1])).Reason,
                Is.EqualTo(R.MalformedSnapshot + ":" + R.PaidGrantNotAllowed));
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
            G(l, Req(RewardSources.FirstClear, "level-1", Coins(50)));
            G(l, Req(RewardSources.Weekly, "2026-W45", Coins(30), T0, T0.AddDays(3), "week-claim"));
            G(l, Req(RewardSources.Purchase, "order|1", new Dictionary<string, long> { ["PremiumGems"] = 100 }, reference: "é-receipt"));
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
            Assert.That(RewardLedger.FromSnapshot(snap.Replace("\"version\":2", "\"version\":3")).Reason, Is.EqualTo(R.UnsupportedSnapshotVersion));
            Assert.That(RewardLedger.FromSnapshot(snap.Replace("{\"format\"", "{ \"format\"")).Reason, Is.EqualTo(R.NonCanonicalSnapshot));
            Assert.That(RewardLedger.FromSnapshot(snap.Substring(0, snap.Length - 3)).Reason, Is.EqualTo(R.MalformedSnapshot));
            Assert.That(RewardLedger.FromSnapshot("").Reason, Is.EqualTo(R.MalformedSnapshot));
            Assert.That(RewardLedger.FromSnapshot(null).Reason, Is.EqualTo(R.MalformedSnapshot));
            Assert.That(RewardLedger.FromSnapshot("[]").Reason, Is.EqualTo(R.MalformedSnapshot));
        }

        // ---- settlement (prune) -------------------------------------------------------------------

        [Test]
        public void Prune_TombstonesOnlyExpiredWindowed_NeverPermanent_SettledKeyNeverReapplied()
        {
            var l = new RewardLedger(Game, Player, null, new RewardRetentionPolicy(180, new Dictionary<string, int> { [RewardSources.Daily] = 30 }));
            G(l, Req(RewardSources.FirstClear, "level-1", Coins(1)));
            G(l, Req(RewardSources.Daily, "d1", Coins(10), T0, T0.AddDays(1)));
            G(l, Req(RewardSources.Weekly, "w1", Coins(100), T0, T0.AddDays(7)));
            Assert.Throws<ArgumentException>(() => l.Prune(TrustedUtcAnchor.DeviceClock(T0.AddDays(5000))));
            Assert.Throws<ArgumentException>(() => l.Prune(default(TrustedUtcAnchor)));
            Assert.That(l.Prune(At(T0.AddDays(31).AddTicks(-1))), Is.EqualTo(0));
            Assert.That(l.Prune(At(T0.AddDays(31))), Is.EqualTo(1));
            Assert.That(l.Prune(At(T0.AddDays(5000))), Is.EqualTo(1));
            Assert.That(l.Count, Is.EqualTo(1));
            Assert.That(l.SettledCount, Is.EqualTo(2));
            Assert.That(l.Balance("Coins"), Is.EqualTo(111));
            Assert.That(l.ContainsKey(RewardLedgerKey.Derive(Game, Player, "weekly", "w1")), Is.True);

            // QA M1 sequences: re-presenting a settled key never applies, whatever its expiry.
            Assert.That(G(l, Req(RewardSources.Daily, "d1", Coins(10), T0, T0.AddDays(1))).Reason, Is.EqualTo(R.KeySettled));
            Assert.That(G(l, Req(RewardSources.Daily, "d1", Coins(10), T0, T0.AddDays(9))).Reason, Is.EqualTo(R.KeySettled));
            Assert.That(G(l, Req(RewardSources.Daily, "d1", Coins(10), T0)).Reason, Is.EqualTo(R.ExpiryRequired));
            Assert.That(l.TryGrant(Req(RewardSources.Weekly, "w1", Coins(100), T0.AddDays(5000), T0.AddDays(5001)), At(T0.AddDays(5000))).Reason,
                Is.EqualTo(R.KeySettled));
            Assert.That(l.TryGrant(Req(RewardSources.FirstClear, "level-1", Coins(1), T0.AddDays(5000))).Outcome, Is.EqualTo(RewardLedgerOutcome.Duplicate));
            Assert.That(l.Balance("Coins"), Is.EqualTo(111));

            var restored = Restore(l.ToSnapshot());
            Assert.That(restored.Balance("Coins"), Is.EqualTo(111));
            Assert.That(restored.SettledCount, Is.EqualTo(2));
            Assert.That(G(restored, Req(RewardSources.Weekly, "w1", Coins(100), T0, T0.AddDays(7))).Reason, Is.EqualTo(R.KeySettled));
        }

        // ---- merge --------------------------------------------------------------------------------

        [Test]
        public void Merge_UnionOfKeys_SameKeyGrantedOnce_BalanceIsStartPlusUniqueSum()
        {
            var opening = new Dictionary<string, long> { ["Coins"] = 500 };
            var cloud = Ledger(opening: opening);
            var local = Ledger(opening: opening);
            G(cloud, Req(RewardSources.FirstClear, "level-1", Coins(50), T0));
            G(cloud, Req(RewardSources.Daily, "d1", Coins(10), T0, T0.AddDays(1)));
            G(local, Req(RewardSources.FirstClear, "level-1", Coins(50), T0.AddHours(2))); // same grant, offline on device
            G(local, Req(RewardSources.Chest, "c1", Coins(7), T0));
            var m = RewardLedger.Merge(cloud, local);
            Assert.That(m.ConflictedKeys, Is.Empty);
            Assert.That(m.Ledger.Count, Is.EqualTo(3));
            Assert.That(m.Ledger.Balance("Coins"), Is.EqualTo(500 + 50 + 10 + 7));
            m.Ledger.TryGetRecord(RewardLedgerKey.Derive(Game, Player, "first_clear", "level-1"), out var rec);
            Assert.That(rec.GrantedUtc, Is.EqualTo(T0), "earliest grant time kept");
            Assert.That(m.Ledger.TryGrant(Req(RewardSources.Chest, "c1", Coins(7))).Outcome, Is.EqualTo(RewardLedgerOutcome.Duplicate));
        }

        [Test]
        public void Merge_QaSequence_EmptyPrunedReplica_LosesNothing()
        {
            var a = Ledger();
            G(a, Req(RewardSources.Weekly, "w1", Coins(100), T0, T0.AddDays(7)));
            var b = Ledger();
            Assert.That(b.Prune(At(T0.AddDays(5000))), Is.EqualTo(0));
            Assert.That(Merged(a, b).Balance("Coins"), Is.EqualTo(100));
            Assert.That(Merged(b, a).Balance("Coins"), Is.EqualTo(100));
            a.Prune(At(T0.AddDays(5000)));
            Assert.That(Merged(a, b).Balance("Coins"), Is.EqualTo(100), "settled grant survives merge");
            Assert.That(Merged(b, a).SettledCount, Is.EqualTo(1));
        }

        [Test]
        public void Merge_QaSequence_BothPrunedAtDifferentTimes_EachKeyOnce()
        {
            var a = Ledger();
            G(a, Req(RewardSources.Weekly, "w1", Coins(100), T0, T0.AddDays(7)));
            G(a, Req(RewardSources.Daily, "d1", Coins(10), T0, T0.AddDays(1)));
            G(a, Req(RewardSources.FirstClear, "fc", Coins(1)));
            var b = Restore(a.ToSnapshot());
            G(b, Req(RewardSources.Tournament, "t1", Coins(50), T0, T0.AddDays(3)));
            Assert.That(a.Prune(At(T0.AddDays(190))), Is.EqualTo(2)); // w1, d1
            Assert.That(b.Prune(At(T0.AddDays(182))), Is.EqualTo(1)); // d1 only
            var m = RewardLedger.Merge(a, b);
            Assert.That(m.ConflictedKeys, Is.Empty);
            Assert.That(m.Ledger.Balance("Coins"), Is.EqualTo(161));
            Assert.That(m.Ledger.SettledCount, Is.EqualTo(2), "w1 settled on one side, live on the other: counted once as settled");
            Assert.That(Merged(b, a).ToSnapshot(), Is.EqualTo(m.Ledger.ToSnapshot()));
        }

        [Test]
        public void Merge_ConflictFlagged_CountedOnce_LiveAndTombstone()
        {
            var a = Ledger();
            var b = Ledger();
            G(a, Req(RewardSources.Tournament, "s1", Coins(100), T0, T0.AddDays(7), "rank-1"));
            G(b, Req(RewardSources.Tournament, "s1", Coins(300), T0, T0.AddDays(7), "rank-1"));
            var key = RewardLedgerKey.Derive(Game, Player, "tournament", "s1");
            var live = RewardLedger.Merge(a, b);
            Assert.That(live.ConflictedKeys, Is.EqualTo(new[] { key }));
            Assert.That(live.Ledger.Balance("Coins"), Is.EqualTo(100).Or.EqualTo(300));
            a.Prune(At(T0.AddDays(500)));
            var mixed = RewardLedger.Merge(a, b);
            Assert.That(mixed.ConflictedKeys, Is.EqualTo(new[] { key }));
            Assert.That(mixed.Ledger.Balance("Coins"), Is.EqualTo(100).Or.EqualTo(300));
            Assert.That(RewardLedger.Merge(b, a).Ledger.ToSnapshot(), Is.EqualTo(mixed.Ledger.ToSnapshot()));
        }

        [Test]
        public void Merge_Property_CommutativeAssociativeIdempotent_MatchesBalanceOracle()
        {
            var sources = new[] { RewardSources.Daily, RewardSources.Weekly, RewardSources.Tournament, RewardSources.FirstClear,
                RewardSources.Chest, RewardSources.CatchUp };
            foreach (var seed in new ulong[] { 1, 2, 3, 4, 5, 6 })
            {
                var opening = Coins(500);
                var s = new[] { Ledger(opening: opening), Ledger(opening: opening), Ledger(opening: opening) };
                var oracle = new Dictionary<string, long>(StringComparer.Ordinal);
                var rng = new SplitMix64(seed);
                for (var i = 0; i < 240; i++)
                {
                    var src = sources[(int)(rng.Next() % (ulong)sources.Length)];
                    var n = (int)(rng.Next() % 25);
                    var amount = 1 + src.Length * 7 + n;                       // a function of the key: no conflicts
                    var expires = RewardSources.IsWindowed(src) ? T0.AddDays(1 + n % 2) : (DateTime?)null;
                    var r = G(s[(int)(rng.Next() % 3)], Req(src, "k" + n, Coins(amount), T0.AddMinutes((long)(rng.Next() % 50)), expires));
                    Assert.That(r.Outcome, Is.Not.EqualTo(RewardLedgerOutcome.Rejected), r.Reason);
                    oracle[src + "/k" + n] = amount;
                }
                s[(int)(seed % 3)].Prune(At(T0.AddDays(181.5)));                // settles the day-1 windows only
                s[(int)((seed + 1) % 3)].Prune(At(T0.AddDays(400)));           // settles every windowed record
                var expected = 500 + oracle.Values.Sum();
                string M(RewardLedger x, RewardLedger y) => Merged(x, y).ToSnapshot();
                var ctx = "seed " + seed;
                Assert.That(M(s[0], s[1]), Is.EqualTo(M(s[1], s[0])), ctx);
                Assert.That(M(s[1], s[2]), Is.EqualTo(M(s[2], s[1])), ctx);
                Assert.That(M(Merged(s[0], s[1]), s[2]), Is.EqualTo(M(s[0], Merged(s[1], s[2]))), ctx);
                Assert.That(M(Merged(s[0], s[2]), s[1]), Is.EqualTo(M(s[0], Merged(s[2], s[1]))), ctx);
                foreach (var l in s) Assert.That(M(l, l), Is.EqualTo(l.ToSnapshot()), ctx + " idempotent");
                var all = Merged(Merged(s[0], s[1]), s[2]);
                Assert.That(all.Balance("Coins"), Is.EqualTo(expected), ctx + " balance oracle");
                Assert.That(all.Count + all.SettledCount, Is.EqualTo(oracle.Count), ctx);
                Assert.That(RewardLedger.Merge(Merged(s[0], s[1]), s[2]).ConflictedKeys, Is.Empty, ctx);
                Assert.That(Restore(all.ToSnapshot()).Balance("Coins"), Is.EqualTo(expected), ctx + " restore");
            }
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

        // ---- ENG-M08 UnifiedRewardPlanner integration --------------------------------------------

        [Test]
        public void M08Adapter_SameSourceEventThroughBothPaths_CreditedOnce()
        {
            var m08 = new M08.RewardGrantRequest("firstclear:L9", Game, M08.RewardSourceKind.FirstClear,
                new[] { new M08.RewardResourceDelta("coins", 40) });
            var plan = M08.UnifiedRewardPlanner.Apply(new M08.UnifiedRewardSnapshot(), m08);
            Assert.That(M08.UnifiedRewardPlanner.Apply(plan.Result, m08).Applied, Is.False);

            var l = Ledger();
            Assert.That(UnifiedRewardLedgerAdapter.TryGrant(l, m08, Now).Outcome, Is.EqualTo(RewardLedgerOutcome.Applied));
            Assert.That(UnifiedRewardLedgerAdapter.TryGrant(l, m08, At(T0.AddHours(1))).Outcome, Is.EqualTo(RewardLedgerOutcome.Duplicate));
            Assert.That(l.TryGrant(Req(RewardSources.FirstClear, "firstclear:L9", Coins(40), T0.AddHours(2))).Outcome,
                Is.EqualTo(RewardLedgerOutcome.Duplicate), "direct ledger path hits the same key");
            Assert.That(l.Balance("Coins"), Is.EqualTo(40));
            Assert.That(plan.Result.Economy.Balance(Game, "coins"), Is.EqualTo(40));
        }

        [Test]
        public void M08Adapter_FailsClosedOnUnmappedSourcesResourcesAndDebits()
        {
            var l = Ledger();
            M08.RewardGrantRequest Make(M08.RewardSourceKind kind, string resource, int delta) =>
                new M08.RewardGrantRequest("src-" + kind + resource + delta, Game, kind, new[] { new M08.RewardResourceDelta(resource, delta) });
            Assert.That(UnifiedRewardLedgerAdapter.TryGrant(l, Make(M08.RewardSourceKind.Gift, "coins", 5), Now).Reason,
                Is.EqualTo(UnifiedRewardLedgerAdapter.UnmappedRewardSource));
            Assert.That(UnifiedRewardLedgerAdapter.TryGrant(l, Make(M08.RewardSourceKind.Chest, "coins", -5), Now).Reason,
                Is.EqualTo(UnifiedRewardLedgerAdapter.DebitNotSupported));
            Assert.That(UnifiedRewardLedgerAdapter.TryGrant(l, Make(M08.RewardSourceKind.Chest, "booster.hammer", 1), Now).Reason,
                Is.EqualTo(UnifiedRewardLedgerAdapter.UnmappedResource));
            Assert.That(UnifiedRewardLedgerAdapter.TryGrant(l, Make(M08.RewardSourceKind.Daily, "coins", 5), Now).Reason, Is.EqualTo(R.ExpiryRequired));
            Assert.That(UnifiedRewardLedgerAdapter.TryGrant(l, Make(M08.RewardSourceKind.Daily, "coins", 6), Now, T0.AddDays(1)).Outcome,
                Is.EqualTo(RewardLedgerOutcome.Applied));
            Assert.That(l.Balance("Coins"), Is.EqualTo(6));
        }
    }
}
#pragma warning restore 0618
