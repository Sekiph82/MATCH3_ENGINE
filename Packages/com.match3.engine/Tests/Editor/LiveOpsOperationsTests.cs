using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Match3.Engine.Runtime.LiveOps;
using NUnit.Framework;
using R = Match3.Engine.Runtime.LiveOps.ConfigReleaseReasons;

namespace Match3.Engine.Tests.Editor
{
    // ENG-M20-009 operations and approvals: ES256 sign/verify, Dev -> Staging -> Prod promotion, two-stage economy
    // approvals, signed rollback, hash-chained release log and the fail-closed client verifier.
    // Keys are generated at runtime per test run; only public keys are pinned (in memory). No key material is committed.
    public sealed class LiveOpsOperationsTests
    {
        private const string Game = "Coral_Rescue";
        private static readonly DateTime T0 = new DateTime(2026, 11, 12, 9, 0, 0, DateTimeKind.Utc);

        private sealed class TestKey
        {
            public readonly string Id;
            public readonly byte[] Private;
            public readonly byte[] Public;

            public TestKey(string id)
            {
                Id = id;
                EcdsaP256.GenerateKey(out Private, out Public);
            }

            public byte[] Sign(byte[] payload) => EcdsaP256.Sign(Private, payload);
        }

        private static readonly TestKey K1 = new TestKey("ops-2026a");
        private static readonly TestKey K2 = new TestKey("ops-2026b");

        private static TrustedUtcAnchor At(double hours) => TrustedUtcAnchor.Server(T0.AddHours(hours));

        private static ConfigTrustedKeys Keys(params TestKey[] keys) => Keys(keys, null);

        private static ConfigTrustedKeys Keys(IEnumerable<TestKey> keys, IEnumerable<string> revoked) =>
            new ConfigTrustedKeys(keys.Select(k => new KeyValuePair<string, byte[]>(k.Id, k.Public)), revoked);

        private static ConfigReleasePipeline Pipeline(TestKey key = null, string game = Game)
        {
            key = key ?? K1;
            return new ConfigReleasePipeline(game, key.Id, key.Sign);
        }

        internal static string Bundle(long version, string coins = "100", string difficulty = "normal", string game = Game,
            long schema = 1, string eventId = "w46", Dictionary<string, object> extra = null)
        {
            var doc = new Dictionary<string, object>
            {
                ["format"] = ConfigBundle.Format,
                ["gameId"] = game,
                ["version"] = version,
                ["schemaVersion"] = schema,
                ["remoteConfig"] = new Dictionary<string, object> { ["CoinRewardAmount"] = coins, ["GameplayDifficulty"] = difficulty },
                ["calendars"] = new Dictionary<string, object> { ["weekly"] = new Dictionary<string, object> { ["eventId"] = eventId } },
                ["featureFlags"] = new Dictionary<string, object> { ["SeasonalEvent"] = true }
            };
            foreach (var kv in extra ?? new Dictionary<string, object>()) doc[kv.Key] = kv.Value;
            return CanonicalJson.Serialize(doc);
        }

        private static readonly string[] OneApprover = { "rev-1" };
        private static readonly string[] TwoApprovers = { "rev-1", "rev-2" };

        /// <summary>Promotes the bundle Dev -> Staging -> Prod; returns the Prod result.</summary>
        private static ConfigOperationResult ToProd(ConfigReleasePipeline p, string bundle, double hours, string[] approvers)
        {
            Assert.IsTrue(p.Promote(bundle, ConfigEnvironment.Dev, "author-1", null, At(hours)).Succeeded);
            Assert.IsTrue(p.Promote(bundle, ConfigEnvironment.Staging, "author-1", null, At(hours)).Succeeded);
            return p.Promote(bundle, ConfigEnvironment.Prod, "author-1", approvers, At(hours));
        }

        private static ConfigAcceptResult Accept(ConfigOperationResult op, ConfigClientState state, ConfigTrustedKeys keys = null,
            double hours = 1, string currentBundle = null) =>
            ConfigBundleVerifier.TryAccept(op.BundleText, op.SignatureBlock, keys ?? Keys(K1), state, At(hours), currentBundle);

        private static ConfigClientState Prod(string game = Game) => new ConfigClientState(game, ConfigEnvironment.Prod);

        /// <summary>A record the trusted key really signed but the pipeline would never emit (insider/bug cases).</summary>
        private static string Forge(TestKey key, string kind, ConfigEnvironment env, long sequence, ConfigBundle bundle,
            long from, string author, string[] approvers, bool economy, DateTime issued, string alg = ConfigReleaseRecord.Algorithm,
            long? claimedVersion = null)
        {
            var version = claimedVersion ?? bundle.Version;
            var unsigned = new ConfigReleaseRecord(key.Id, kind, bundle.GameId, env, sequence, version, bundle.Sha256, from,
                author, approvers, economy, issued, null, alg);
            return new ConfigReleaseRecord(key.Id, kind, bundle.GameId, env, sequence, version, bundle.Sha256, from, author,
                approvers, economy, issued, key.Sign(unsigned.SigningPayload()), alg).ToCanonical();
        }

        private static ConfigBundle Parsed(string text)
        {
            Assert.IsTrue(ConfigBundle.TryParse(text, out var b, out var why), why);
            return b;
        }

        private static string Hex(byte[] b) => string.Concat(b.Select(x => x.ToString("x2")));

        private static byte[] FromHex(string h) =>
            Enumerable.Range(0, h.Length / 2).Select(i => Convert.ToByte(h.Substring(2 * i, 2), 16)).ToArray();

        // ---------- ES256 primitive ----------

        [Test]
        public void Ecdsa_MatchesRfc6979KnownAnswer_AndRejectsTampering()
        {
            // RFC 6979 A.2.5, P-256 / SHA-256, message "sample" (cross-checked against .NET CNG ECDsa offline).
            var d = FromHex("c9afa9d845ba75166b5c215767b1d6934e50c3db36e89b127b8a622b120f6721");
            var k = FromHex("a6e3c57dd01abe90086538398355dd4c3b17aa873382b0f24d6129493d8aad60");
            var msg = Encoding.ASCII.GetBytes("sample");
            var pub = EcdsaP256.PublicKeyOf(d);
            Assert.AreEqual("04" + "60fed4ba255a9d31c961eb74c6356d68c049b8923b61fa6ce669622e60f29fb6" +
                            "7903fe1008b8bc99a41ae9e95628bc64f2f1b20c2d7e9f5177a3c294d4462299", Hex(pub));
            var sig = EcdsaP256.SignWithNonce(d, msg, k);
            Assert.AreEqual("efd48b2aacb6a8fd1140dd9cd45e81d69d2c877b56aaf991c34d0ea84eaf3716" +
                            "f7cb1c942d657c41d436c7a1b6e29f65f3e900dbb9aff4064dc4ab2f843acda8", Hex(sig));
            Assert.IsTrue(EcdsaP256.Verify(pub, msg, sig));

            Assert.IsFalse(EcdsaP256.Verify(pub, Encoding.ASCII.GetBytes("samplE"), sig), "message changed");
            for (var i = 0; i < 64; i += 21)
            {
                var t = (byte[])sig.Clone();
                t[i] ^= 0x01;
                Assert.IsFalse(EcdsaP256.Verify(pub, msg, t), "signature byte " + i);
            }
            Assert.IsFalse(EcdsaP256.Verify(K1.Public, msg, sig), "other key");
            Assert.IsFalse(EcdsaP256.Verify(pub, msg, sig.Take(63).ToArray()), "short signature");
            Assert.IsFalse(EcdsaP256.Verify(pub, msg, new byte[64]), "r = s = 0");
            var n = FromHex("ffffffff00000000ffffffffffffffffbce6faada7179e84f3b9cac2fc632551");
            Assert.IsFalse(EcdsaP256.Verify(pub, msg, n.Concat(sig.Skip(32)).ToArray()), "r = n");
            Assert.IsFalse(EcdsaP256.Verify(pub, null, sig));
            Assert.IsFalse(EcdsaP256.Verify(null, msg, sig));
        }

        [Test]
        public void Ecdsa_RejectsInvalidPublicKeys_AndRoundTripsGeneratedKeys()
        {
            var offCurve = (byte[])K1.Public.Clone();
            offCurve[64] ^= 0x01;
            var compressed = (byte[])K1.Public.Clone();
            compressed[0] = 0x02;
            var xTooBig = Enumerable.Repeat((byte)0xff, 65).ToArray();
            xTooBig[0] = 4;
            foreach (var bad in new[] { offCurve, compressed, xTooBig, K1.Public.Take(64).ToArray(), new byte[65], null })
                Assert.IsFalse(EcdsaP256.IsValidPublicKey(bad));
            Assert.IsTrue(EcdsaP256.IsValidPublicKey(K1.Public));
            Assert.AreEqual(Hex(K1.Public), Hex(EcdsaP256.PublicKeyOf(K1.Private)));

            var msg = Encoding.UTF8.GetBytes("bundle-payload");
            var sig = K2.Sign(msg);
            Assert.IsTrue(EcdsaP256.Verify(K2.Public, msg, sig));
            Assert.IsFalse(EcdsaP256.Verify(K1.Public, msg, sig));
            Assert.IsFalse(EcdsaP256.Verify(offCurve, msg, K1.Sign(msg)));
            Assert.Throws<ArgumentException>(() => EcdsaP256.Sign(new byte[32], msg), "zero private scalar");
        }

        // ---------- bundle ----------

        [Test]
        public void Bundle_IsCanonical_AndRejectsBadContentWithStableReasons()
        {
            var b = Parsed(Bundle(1));
            Assert.AreEqual(64, b.Sha256.Length);
            var spaced = Bundle(1).Replace(",", " ,\n ").Replace(":", " : ");
            Assert.AreEqual(b.Sha256, Parsed(spaced).Sha256, "digest is over the canonical form");
            Assert.AreEqual("100", b.RemoteConfig(RemoteConfigKey.CoinRewardAmount));

            void Bad(string text, string reason) =>
                Assert.AreEqual(reason, ConfigBundle.TryParse(text, out _, out var why) ? "accepted" : why, text);
            Bad(null, R.MalformedBundle);
            Bad("[1]", R.MalformedBundle);
            Bad("{\"format\":", R.MalformedBundle);
            Bad(Bundle(1, extra: new Dictionary<string, object> { ["price"] = 1L }), R.UnknownBundleField);
            Bad(Bundle(1).Replace(ConfigBundle.Format, "m3-config-bundle/2"), R.UnsupportedBundleFormat);
            Bad(Bundle(1, game: "Candy_Clone"), R.UnknownGame);
            Bad(Bundle(0), R.InvalidVersion);
            Bad(Bundle(1, schema: 0), R.SchemaVersionTooOld);
            Bad(Bundle(1, extra: new Dictionary<string, object> { ["remoteConfig"] = new Dictionary<string, object> { ["PlayerEmail"] = "x" } }),
                R.UnknownConfigKey);
            Bad(Bundle(1, extra: new Dictionary<string, object> { ["remoteConfig"] = new Dictionary<string, object> { ["0"] = "x" } }),
                R.UnknownConfigKey);
            Bad(Bundle(1, extra: new Dictionary<string, object> { ["remoteConfig"] = new Dictionary<string, object> { ["CoinRewardAmount"] = 5L } }),
                R.MalformedBundle);
            Bad(Bundle(1, extra: new Dictionary<string, object> { ["offers"] = new List<object>() }), R.MalformedBundle);
            Bad(Bundle(1, extra: new Dictionary<string, object> { ["featureFlags"] = new Dictionary<string, object> { ["Lootboxes"] = true } }),
                R.UnknownConfigKey);
            // Policy gates still apply inside a signed bundle.
            Bad(Bundle(1, game: "Wonder_Pop", extra: new Dictionary<string, object>
                { ["featureFlags"] = new Dictionary<string, object> { ["TournamentMode"] = true } }), "WonderPopTournamentProhibited");
            Assert.IsTrue(ConfigBundle.TryParse(Bundle(1, game: "Wonder_Pop", extra: new Dictionary<string, object>
                { ["featureFlags"] = new Dictionary<string, object> { ["TournamentMode"] = false } }), out _, out _), "kill switch is fine");
        }

        [Test]
        public void EconomyClassification_UsesLeafDiff_AndBroadEconomyWords()
        {
            var v1 = Parsed(Bundle(1));
            Assert.IsFalse(ConfigBundle.IsEconomyChange(v1, Parsed(Bundle(2, difficulty: "hard"))));
            Assert.IsFalse(ConfigBundle.IsEconomyChange(v1, Parsed(Bundle(2, eventId: "w47"))));
            Assert.IsTrue(ConfigBundle.IsEconomyChange(v1, Parsed(Bundle(2, coins: "150"))));
            CollectionAssert.AreEqual(new[] { "remoteConfig/CoinRewardAmount" }, ConfigBundle.ChangedPaths(v1, Parsed(Bundle(2, coins: "150"))));
            Assert.IsTrue(ConfigBundle.IsEconomyChange(null, v1), "first release: every leaf counts, CoinRewardAmount is economy");

            foreach (var p in new[] { "offers/starter", "economy/x", "calendars/w46/rewardCoins", "rollouts/r1/dailyCap",
                         "experiments/e1/priceTier", "calendars/prizePool", "experiments/gem_bonus", "rollouts/XPBoosterAmount" })
                Assert.IsTrue(ConfigBundle.IsEconomyPath(p), p);
            foreach (var p in new[] { "remoteConfig/GameplayDifficulty", "calendars/weekly/eventId", "featureFlags/SeasonalEvent",
                         "rollouts/capability", "experiments/escape" })
                Assert.IsFalse(ConfigBundle.IsEconomyPath(p), p);
        }

        // ---------- promotion and approvals ----------

        [Test]
        public void Promotion_FollowsDevStagingProd_AndEachClientOnlyAcceptsItsEnvironment()
        {
            var p = Pipeline();
            var b1 = Bundle(1);
            Assert.AreEqual(R.NotInPreviousEnvironment, p.Promote(b1, ConfigEnvironment.Staging, "author-1", null, At(1)).Reason);
            Assert.AreEqual(R.NotInPreviousEnvironment, p.Promote(b1, ConfigEnvironment.Prod, "author-1", TwoApprovers, At(1)).Reason);
            var dev = p.Promote(b1, ConfigEnvironment.Dev, "author-1", null, At(1));
            Assert.IsTrue(dev.Succeeded, dev.Reason);
            Assert.AreEqual(R.NotInPreviousEnvironment, p.Promote(b1, ConfigEnvironment.Prod, "author-1", TwoApprovers, At(1)).Reason);
            var staging = p.Promote(b1, ConfigEnvironment.Staging, "author-1", null, At(1));
            Assert.IsTrue(staging.Succeeded, staging.Reason);
            // A different digest under the same version cannot jump to Prod.
            Assert.AreEqual(R.NotInPreviousEnvironment, p.Promote(Bundle(1, coins: "999"), ConfigEnvironment.Prod, "author-1",
                TwoApprovers, At(1)).Reason);
            var prod = p.Promote(b1, ConfigEnvironment.Prod, "author-1", TwoApprovers, At(1));
            Assert.IsTrue(prod.Succeeded, prod.Reason);
            Assert.AreEqual(b1, p.Live(ConfigEnvironment.Prod));
            Assert.AreEqual(3, p.Log.Entries.Count);

            Assert.IsTrue(Accept(dev, new ConfigClientState(Game, ConfigEnvironment.Dev)).Accepted);
            Assert.IsTrue(Accept(staging, new ConfigClientState(Game, ConfigEnvironment.Staging)).Accepted);
            var accepted = Accept(prod, Prod());
            Assert.IsTrue(accepted.Accepted, accepted.Reason);
            Assert.AreEqual(1, accepted.State.BundleVersion);
            Assert.AreEqual(Parsed(b1).Sha256, accepted.State.BundleSha256);
            Assert.AreEqual(R.WrongEnvironment, Accept(staging, Prod()).Reason, "a staging record never reaches prod clients");
            Assert.AreEqual(R.WrongEnvironment, Accept(prod, new ConfigClientState(Game, ConfigEnvironment.Staging)).Reason);
            Assert.AreEqual(R.WrongGame, Accept(prod, Prod("Dino_Islands")).Reason);
            Assert.AreEqual(R.WrongGame, p.Promote(Bundle(2, game: "Dino_Islands"), ConfigEnvironment.Dev, "author-1", null, At(1)).Reason);
            Assert.AreEqual(R.VersionNotMonotonic, p.Promote(b1, ConfigEnvironment.Dev, "author-1", null, At(2)).Reason);
        }

        [Test]
        public void ProdApprovals_OneForNonEconomy_TwoDistinctForEconomy_AuthorNeverApproves()
        {
            var p = Pipeline();
            Assert.IsTrue(ToProd(p, Bundle(1), 1, TwoApprovers).Succeeded, "first release carries coin amounts: economy");

            var difficulty = Bundle(2, difficulty: "hard");
            Assert.AreEqual(R.InsufficientApprovals, ToProd(p, difficulty, 2, new string[0]).Reason);
            Assert.AreEqual(R.AuthorCannotApprove, p.Promote(difficulty, ConfigEnvironment.Prod, "author-1", new[] { "author-1" }, At(2)).Reason);
            Assert.AreEqual(R.InvalidApproverId, p.Promote(difficulty, ConfigEnvironment.Prod, "author-1", new[] { "jane@example.com" }, At(2)).Reason);
            Assert.AreEqual(R.InvalidApproverId, p.Promote(difficulty, ConfigEnvironment.Prod, "author 1", OneApprover, At(2)).Reason);
            var nonEconomy = p.Promote(difficulty, ConfigEnvironment.Prod, "author-1", OneApprover, At(2));
            Assert.IsTrue(nonEconomy.Succeeded, nonEconomy.Reason);
            Assert.IsFalse(nonEconomy.Record.EconomyChange);

            var coins = Bundle(3, difficulty: "hard", coins: "150");
            Assert.AreEqual(R.InsufficientApprovals, ToProd(p, coins, 3, OneApprover).Reason);
            Assert.AreEqual(R.DuplicateApprover, p.Promote(coins, ConfigEnvironment.Prod, "author-1", new[] { "rev-1", "rev-1" }, At(3)).Reason);
            Assert.AreEqual(R.AuthorCannotApprove, p.Promote(coins, ConfigEnvironment.Prod, "author-1", new[] { "rev-1", "author-1" }, At(3)).Reason);
            var economy = p.Promote(coins, ConfigEnvironment.Prod, "author-1", TwoApprovers, At(3));
            Assert.IsTrue(economy.Succeeded, economy.Reason);
            Assert.IsTrue(economy.Record.EconomyChange);
            CollectionAssert.AreEqual(TwoApprovers, economy.Record.Approvers);
            StringAssert.Contains("\"approvers\":[\"rev-1\",\"rev-2\"]", economy.SignatureBlock, "approvals are inside the signed block");
            Assert.IsFalse(economy.SignatureBlock.Contains("@") || p.Log.Export().Contains("@"), "no e-mail style ids anywhere");

            // The client re-checks the approval block even though the trusted key signed it.
            var s = Prod();
            var b3 = Parsed(coins);
            Assert.AreEqual(R.InsufficientApprovals, ConfigBundleVerifier.TryAccept(coins,
                Forge(K1, "release", ConfigEnvironment.Prod, 9, b3, 0, "author-1", OneApprover, true, T0), Keys(K1), s, At(4)).Reason);
            Assert.AreEqual(R.AuthorCannotApprove, ConfigBundleVerifier.TryAccept(coins,
                Forge(K1, "release", ConfigEnvironment.Prod, 9, b3, 0, "author-1", new[] { "author-1", "rev-2" }, true, T0), Keys(K1), s, At(4)).Reason);
            Assert.AreEqual(R.InsufficientApprovals, ConfigBundleVerifier.TryAccept(coins,
                Forge(K1, "release", ConfigEnvironment.Prod, 9, b3, 0, "author-1", new string[0], false, T0), Keys(K1), s, At(4)).Reason);
            // Non-prod environments do not require approvers.
            Assert.IsTrue(ConfigBundleVerifier.TryAccept(coins, Forge(K1, "release", ConfigEnvironment.Staging, 9, b3, 0, "author-1",
                new string[0], true, T0), Keys(K1), new ConfigClientState(Game, ConfigEnvironment.Staging), At(4)).Accepted);
        }

        [Test]
        public void Client_RejectsAnUndeclaredEconomyChange_WhenItHoldsTheCurrentBundle()
        {
            var b1 = Bundle(1);
            var b2 = Bundle(2, coins: "500");
            var s1 = new ConfigClientState(Game, ConfigEnvironment.Prod, 1, 1, 1, Parsed(b1).Sha256);
            var mislabeled = Forge(K1, "release", ConfigEnvironment.Prod, 2, Parsed(b2), 0, "author-1", OneApprover, false, T0);
            Assert.AreEqual(R.EconomyChangeUndeclared, ConfigBundleVerifier.TryAccept(b2, mislabeled, Keys(K1), s1, At(1), b1).Reason);
            // A last-known-good text that does not match the state digest is ignored, never trusted.
            Assert.IsTrue(ConfigBundleVerifier.TryAccept(b2, mislabeled, Keys(K1), s1, At(1), Bundle(1, coins: "500")).Accepted);
            var labeled = Forge(K1, "release", ConfigEnvironment.Prod, 2, Parsed(b2), 0, "author-1", TwoApprovers, true, T0);
            Assert.IsTrue(ConfigBundleVerifier.TryAccept(b2, labeled, Keys(K1), s1, At(1), b1).Accepted);
        }

        // ---------- client verification ----------

        [Test]
        public void Client_FailsClosed_OnKeysSignatureDigestTimeAndReplay()
        {
            var p = Pipeline();
            var op = ToProd(p, Bundle(1), 1, TwoApprovers);
            var s = Prod();
            Assert.IsTrue(Accept(op, s).Accepted);

            Assert.AreEqual(R.UnknownKey, Accept(op, s, Keys(K2)).Reason);
            Assert.AreEqual(R.RevokedKey, Accept(op, s, Keys(new[] { K1, K2 }, new[] { K1.Id })).Reason);
            Assert.AreEqual(R.NoTrustedKeys, Accept(op, s, Keys(new[] { K1 }, new[] { K1.Id })).Reason);
            Assert.AreEqual(R.NoTrustedKeys, ConfigBundleVerifier.TryAccept(op.BundleText, op.SignatureBlock, null, s, At(1)).Reason);
            Assert.AreEqual(R.ClientStateRequired, ConfigBundleVerifier.TryAccept(op.BundleText, op.SignatureBlock, Keys(K1), null, At(1)).Reason);
            Assert.AreEqual(R.TrustedTimeAnchorRequired, ConfigBundleVerifier.TryAccept(op.BundleText, op.SignatureBlock, Keys(K1), s,
                TrustedUtcAnchor.DeviceClock(T0.AddHours(1))).Reason);

            // Signed fields cannot be edited: any change breaks the signature.
            foreach (var edit in new[] { ("\"sequence\":1", "\"sequence\":7"), ("\"rev-2\"", "\"rev-3\""),
                         ("\"economyChange\":true", "\"economyChange\":false"), ("\"bundleVersion\":1", "\"bundleVersion\":2") })
                Assert.AreEqual(R.BadSignature, ConfigBundleVerifier.TryAccept(op.BundleText,
                    op.SignatureBlock.Replace(edit.Item1, edit.Item2), Keys(K1), s, At(1)).Reason, edit.Item1);
            Assert.AreEqual(R.BadSignature, ConfigBundleVerifier.TryAccept(op.BundleText,
                op.SignatureBlock.Replace("\"keyId\":\"ops-2026a\"", "\"keyId\":\"ops-2026b\""), Keys(K1, K2), s, At(1)).Reason);
            Assert.AreEqual(R.MalformedSignatureBlock, ConfigBundleVerifier.TryAccept(op.BundleText, "", Keys(K1), s, At(1)).Reason);
            Assert.AreEqual(R.MalformedSignatureBlock, ConfigBundleVerifier.TryAccept(op.BundleText, null, Keys(K1), s, At(1)).Reason);
            Assert.AreEqual(R.MalformedSignatureBlock, ConfigBundleVerifier.TryAccept(op.BundleText,
                op.SignatureBlock.Replace("{", "{\"extra\":1,"), Keys(K1), s, At(1)).Reason);
            Assert.AreEqual(R.UnsupportedRecordFormat, ConfigBundleVerifier.TryAccept(op.BundleText,
                op.SignatureBlock.Replace(ConfigReleaseRecord.Format, "m3-config-release/9"), Keys(K1), s, At(1)).Reason);
            Assert.AreEqual(R.UnsupportedAlgorithm, ConfigBundleVerifier.TryAccept(op.BundleText,
                Forge(K1, "release", ConfigEnvironment.Prod, 1, Parsed(op.BundleText), 0, "author-1", TwoApprovers, true, T0, "none"),
                Keys(K1), s, At(1)).Reason);
            Assert.AreEqual(R.MalformedSignatureBlock, ConfigBundleVerifier.TryAccept(op.BundleText,
                op.SignatureBlock.Replace("\"signature\":\"", "\"signature\":\"!!"), Keys(K1), s, At(1)).Reason);

            // Bundle swap / tamper: the digest binds the content.
            Assert.AreEqual(R.DigestMismatch, ConfigBundleVerifier.TryAccept(Bundle(1, coins: "100000"), op.SignatureBlock, Keys(K1), s, At(1)).Reason);
            Assert.AreEqual(R.SchemaVersionTooOld, ConfigBundleVerifier.TryAccept(Bundle(1, schema: 0), op.SignatureBlock, Keys(K1), s, At(1)).Reason);
            Assert.AreEqual(R.MalformedBundle, ConfigBundleVerifier.TryAccept("not json", op.SignatureBlock, Keys(K1), s, At(1)).Reason);
            var v2 = Bundle(2);
            Assert.AreEqual(R.VersionMismatch, ConfigBundleVerifier.TryAccept(v2, Forge(K1, "release", ConfigEnvironment.Prod, 1,
                Parsed(v2), 0, "author-1", TwoApprovers, true, T0, claimedVersion: 5), Keys(K1), s, At(1)).Reason);

            // Issue time vs trusted anchor (device clock is never consulted).
            Assert.AreEqual(R.IssuedInFuture, ConfigBundleVerifier.TryAccept(op.BundleText, op.SignatureBlock, Keys(K1), s,
                TrustedUtcAnchor.Server(T0.AddHours(1) - ConfigBundleVerifier.MaxClockSkew - TimeSpan.FromSeconds(1))).Reason);
            Assert.IsTrue(ConfigBundleVerifier.TryAccept(op.BundleText, op.SignatureBlock, Keys(K1), s,
                TrustedUtcAnchor.Server(T0.AddHours(1) - ConfigBundleVerifier.MaxClockSkew)).Accepted, "skew boundary is inclusive");

            // Replay of an accepted block.
            var after = Accept(op, s).State;
            Assert.AreEqual(R.StaleSequence, Accept(op, after).Reason);
            Assert.AreSame(after, Accept(op, after).State, "a rejection keeps the current (last-known-good) state");
        }

        [Test]
        public void KeyRotation_BothActiveKeysVerify_RevokedKeyRejectsEvenValidSignatures()
        {
            var s = Prod();
            var old = ToProd(Pipeline(K1), Bundle(1), 1, TwoApprovers);
            var fresh = ToProd(Pipeline(K2), Bundle(1), 1, TwoApprovers);
            var rotating = Keys(K1, K2);
            Assert.IsTrue(Accept(old, s, rotating).Accepted);
            Assert.IsTrue(Accept(fresh, s, rotating).Accepted);
            var afterRevoke = Keys(new[] { K1, K2 }, new[] { K1.Id });
            Assert.AreEqual(R.RevokedKey, Accept(old, s, afterRevoke).Reason);
            Assert.IsTrue(Accept(fresh, s, afterRevoke).Accepted);
            Assert.Throws<ArgumentException>(() => Keys(K1, K1), "duplicate key id");
            Assert.Throws<ArgumentException>(() => new ConfigTrustedKeys(new[] { new KeyValuePair<string, byte[]>("k", new byte[65]) }));
            Assert.Throws<ArgumentException>(() => new ConfigTrustedKeys(new[] { new KeyValuePair<string, byte[]>("bad id", K1.Public) }));
        }

        [Test]
        public void VersionsAreMonotonic_ExceptThroughASignedRollbackFromTheCurrentVersion()
        {
            var p = Pipeline();
            var v1 = ToProd(p, Bundle(1), 1, TwoApprovers);
            var v2 = ToProd(p, Bundle(2, difficulty: "hard"), 2, OneApprover);
            var v3 = ToProd(p, Bundle(3, difficulty: "expert"), 3, OneApprover);
            var s = Accept(v1, Prod()).State;
            s = Accept(v2, s, hours: 2).State;
            s = Accept(v3, s, hours: 3).State;
            Assert.AreEqual(3, s.BundleVersion);

            // An older release with a fresh sequence, really signed by the trusted key, is still a rollback attack.
            var b1 = Parsed(Bundle(1));
            Assert.AreEqual(R.VersionNotMonotonic, ConfigBundleVerifier.TryAccept(Bundle(1),
                Forge(K1, "release", ConfigEnvironment.Prod, 10, b1, 0, "author-1", TwoApprovers, true, T0), Keys(K1), s, At(4)).Reason);
            Assert.AreEqual(R.StaleSequence, Accept(v1, s, hours: 4).Reason);

            // Pipeline rollback guards.
            Assert.AreEqual(R.UnknownRollbackTarget, p.Rollback(ConfigEnvironment.Prod, 7, "author-1", OneApprover, At(4)).Reason);
            Assert.AreEqual(R.RollbackNotOlder, p.Rollback(ConfigEnvironment.Prod, 3, "author-1", OneApprover, At(4)).Reason);
            Assert.AreEqual(R.InsufficientApprovals, p.Rollback(ConfigEnvironment.Prod, 2, "author-1", null, At(4)).Reason);
            Assert.AreEqual(R.NothingToRollBack, Pipeline().Rollback(ConfigEnvironment.Prod, 1, "author-1", OneApprover, At(4)).Reason);
            var rollback = p.Rollback(ConfigEnvironment.Prod, 2, "author-1", OneApprover, At(4));
            Assert.IsTrue(rollback.Succeeded, rollback.Reason);
            Assert.IsTrue(rollback.Record.IsRollback);
            Assert.AreEqual(3, rollback.Record.RollbackFromVersion);
            Assert.AreEqual(Bundle(2, difficulty: "hard"), p.Live(ConfigEnvironment.Prod), "last-known-good restored server side");

            var stale = Accept(v2, Prod(), hours: 2).State; // a client that never saw v3
            Assert.AreEqual(R.RollbackBaseMismatch, Accept(rollback, stale, hours: 4).Reason);
            var rolled = Accept(rollback, s, hours: 4);
            Assert.IsTrue(rolled.Accepted, rolled.Reason);
            Assert.AreEqual(2, rolled.State.BundleVersion);
            Assert.AreEqual(3, rolled.State.HighestBundleVersion, "high-water mark survives the rollback");
            Assert.AreEqual(R.StaleSequence, Accept(v3, rolled.State, hours: 4).Reason, "the rolled-back release cannot be re-served");
            Assert.AreEqual(R.StaleSequence, Accept(rollback, rolled.State, hours: 4).Reason, "rollback record replay");
            Assert.AreEqual(R.VersionNotMonotonic, ConfigBundleVerifier.TryAccept(v3.BundleText,
                Forge(K1, "release", ConfigEnvironment.Prod, 20, Parsed(v3.BundleText), 0, "author-1", OneApprover, false, T0),
                Keys(K1), rolled.State, At(4)).Reason, "v3 under a new sequence is still below the high-water mark");
            Assert.AreEqual(R.RollbackBaseMismatch, ConfigBundleVerifier.TryAccept(v3.BundleText,
                Forge(K1, "rollback", ConfigEnvironment.Prod, 21, Parsed(v3.BundleText), 4, "author-1", OneApprover, false, T0),
                Keys(K1), rolled.State, At(4)).Reason, "a rollback record must name the client's current version");
            Assert.AreEqual(R.MalformedSignatureBlock, ConfigBundleVerifier.TryAccept(v3.BundleText,
                Forge(K1, "rollback", ConfigEnvironment.Prod, 21, Parsed(v3.BundleText), 2, "author-1", OneApprover, false, T0),
                Keys(K1), rolled.State, At(4)).Reason, "a 'rollback' to a newer version is malformed");
            Assert.AreEqual(R.VersionNotMonotonic, p.Promote(Bundle(3, difficulty: "expert"), ConfigEnvironment.Dev, "author-1", null, At(5)).Reason);

            var v4 = ToProd(p, Bundle(4, difficulty: "normal"), 5, OneApprover);
            Assert.IsTrue(v4.Succeeded, v4.Reason);
            Assert.IsTrue(Accept(v4, rolled.State, hours: 5).Accepted, "the next forward release continues above the high-water mark");
        }

        // ---------- release log ----------

        [Test]
        public void ReleaseLog_IsHashChained_AndDetectsEditsDeletionsReorderingAndTruncation()
        {
            var p = Pipeline();
            ToProd(p, Bundle(1), 1, TwoApprovers);
            ToProd(p, Bundle(2, difficulty: "hard"), 2, OneApprover);
            Assert.IsTrue(p.Rollback(ConfigEnvironment.Prod, 1, "author-1", OneApprover, At(3)).Succeeded);
            var log = p.Log;
            Assert.AreEqual(7, log.Entries.Count);
            Assert.AreEqual(ConfigReleaseLogEntry.GenesisHash, log.Entries[0].PrevHash);
            for (var i = 1; i < log.Entries.Count; i++) Assert.AreEqual(log.Entries[i - 1].EntryHash, log.Entries[i].PrevHash);
            Assert.AreEqual("rollback", log.Entries[6].Kind);
            Assert.AreEqual(T0.AddHours(3), log.Entries[6].Utc);

            var text = log.Export();
            Assert.IsTrue(ConfigReleaseLog.TryImport(text, out var copy, out var why, log.HeadHash), why);
            Assert.AreEqual(text, copy.Export());

            var lines = text.Split('\n');
            string Import(string t, string head = null) => ConfigReleaseLog.TryImport(t, out _, out var r, head) ? "ok" : r;
            Assert.AreEqual(R.LogIntegrityMismatch, Import(text.Replace("\"rev-2\"", "\"rev-9\"")));
            Assert.AreEqual(R.LogChainBroken, Import(string.Join("\n", lines.Where((l, i) => i != 3))), "deleted entry");
            Assert.AreEqual(R.LogChainBroken, Import(string.Join("\n", new[] { lines[1], lines[0] }.Concat(lines.Skip(2)))), "reordered");
            Assert.AreEqual("ok", Import(string.Join("\n", lines.Take(5))), "truncation alone is invisible to the chain...");
            Assert.AreEqual(R.LogHeadMismatch, Import(string.Join("\n", lines.Take(5)), log.HeadHash), "...but not to the anchored head");
            Assert.AreEqual(R.MalformedLog, Import(text + "\n{}"));
            Assert.AreEqual(R.MalformedLog, Import(text.Replace(":", " : ")), "non-canonical");

            // Server time only, never backwards.
            Assert.AreEqual(R.ClockRollbackDetected, p.Promote(Bundle(5), ConfigEnvironment.Dev, "author-1", null, At(2)).Reason);
            Assert.AreEqual(R.TrustedTimeAnchorRequired, p.Promote(Bundle(5), ConfigEnvironment.Dev, "author-1", null,
                TrustedUtcAnchor.DeviceClock(T0.AddHours(9))).Reason);
            Assert.AreEqual(7, p.Log.Entries.Count, "rejected operations append nothing");
        }

        [Test]
        public void ClientState_RoundTripsCanonically_AndRejectsEditedOrInconsistentState()
        {
            var accepted = Accept(ToProd(Pipeline(), Bundle(1), 1, TwoApprovers), Prod()).State;
            var text = accepted.ToCanonical();
            Assert.IsTrue(ConfigClientState.TryParse(text, out var back));
            Assert.AreEqual(text, back.ToCanonical());
            Assert.IsFalse(ConfigClientState.TryParse(text.Replace("\"sequence\":1", "\"sequence\":0"), out _), "sequence without digest");
            Assert.IsFalse(ConfigClientState.TryParse(text.Replace("\"highestBundleVersion\":1", "\"highestBundleVersion\":0"), out _));
            Assert.IsFalse(ConfigClientState.TryParse(text.Replace("prod", "qa"), out _));
            Assert.IsFalse(ConfigClientState.TryParse(text.Replace(",", ", "), out _), "non-canonical");
            Assert.IsFalse(ConfigClientState.TryParse(null, out _));
            Assert.IsTrue(ConfigClientState.TryParse(Prod().ToCanonical(), out var fresh));
            Assert.IsNull(fresh.BundleSha256);
        }
    }
}
