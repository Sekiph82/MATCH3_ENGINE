using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Match3.Engine.Runtime;
using Match3.Engine.Runtime.LiveOps;
using NUnit.Framework;
using UnityEngine;
using R = Match3.Engine.Runtime.LiveOps.ContentMetadataReasons;

namespace Match3.Engine.Tests.Editor
{
    // ENG-M20-010. Fixtures under Fixtures/ContentMetadata were produced outside the
    // Engine (Python json.dumps canonical digests), so digest agreement is a
    // cross-producer check, not the Engine verifying itself.
    public sealed class ContentMetadataReceiverTests
    {
        private static readonly ContentKind[] AllKinds = { ContentKind.World, ContentKind.Level, ContentKind.Event };

        private static string Fixture(string name)
        {
            var root = Directory.GetParent(Application.dataPath).FullName;
            var path = Path.Combine(root, "Packages", "com.match3.engine", "Tests", "Editor",
                "Fixtures", "ContentMetadata", name);
            Assert.That(File.Exists(path), Is.True, "missing fixture " + path);
            return File.ReadAllText(path);
        }

        private static ContentMetadataReceiver Coral(IEnumerable<ContentKind> kinds = null,
            IEnumerable<string> worlds = null, IEnumerable<string> levels = null) =>
            new ContentMetadataReceiver("Coral_Rescue", kinds ?? AllKinds, worlds, levels);

        private static string Doc(string items, string game = "Coral_Rescue", string schema = "1.0", string extra = "") =>
            "{\"schemaVersion\":\"" + schema + "\",\"producerId\":\"inline\",\"gameId\":\"" + game + "\"" + extra +
            ",\"items\":[" + items + "]}";

        private static string World(string id, int version, int order) =>
            "{\"contentKind\":\"World\",\"contentId\":\"" + id + "\",\"contentVersion\":" + version +
            ",\"payload\":{\"order\":" + order + "}}";

        private static string Level(string id, string world, int order, string extraPayload = "") =>
            "{\"contentKind\":\"Level\",\"contentId\":\"" + id + "\",\"contentVersion\":1,\"payload\":{\"worldId\":\"" + world +
            "\",\"order\":" + order + ",\"moves\":20,\"objectives\":[{\"kind\":\"CollectPiece\",\"targetId\":\"piece.red\",\"count\":10}]," +
            "\"obstacleIds\":[]" + extraPayload + "}}";

        private static string Event(string id, string start, string end, string levels) =>
            "{\"contentKind\":\"Event\",\"contentId\":\"" + id + "\",\"contentVersion\":1,\"payload\":{\"startUtc\":\"" + start +
            "\",\"endUtc\":\"" + end + "\",\"levelIds\":[" + levels + "]}}";

        // ---- valid canonical fixtures --------------------------------------------

        [Test]
        public void ValidWorldFixture_AcceptsAndRecordsIntegrityVerifiedAndNoDigest()
        {
            var rx = Coral();
            var result = rx.Receive(Fixture("valid_world.json"));

            Assert.That(result.Accepted, Is.True, result.Summary);
            Assert.That(result.Reason, Is.EqualTo(R.Accepted));
            Assert.That(result.Items[0].Integrity, Is.EqualTo(ContentIntegrity.DigestVerified));
            Assert.That(result.Items[1].Integrity, Is.EqualTo(ContentIntegrity.NoDigest));
            Assert.That(rx.Applied.Keys, Is.EquivalentTo(new[] { "cr.world.reef", "cr.world.kelp" }));
            Assert.That(rx.Applied["cr.world.kelp"].Order, Is.EqualTo(2));
            Assert.That(rx.Applied["cr.world.reef"].ProducerId, Is.EqualTo("fixture-producer/py"));
            Assert.That(result.Summary, Does.Contain("noDigest=1"));
        }

        [Test]
        public void ValidLevelFixture_ResolvesKnownWorldAndToleratesXExtension()
        {
            var rx = Coral(worlds: new[] { "cr.world.reef" });
            var result = rx.Receive(Fixture("valid_level.json"));

            Assert.That(result.Accepted, Is.True, result.Summary);
            var level = rx.Applied["cr.level.0001"];
            Assert.That(level.Kind, Is.EqualTo(ContentKind.Level));
            Assert.That(level.ContentVersion, Is.EqualTo(3));
            Assert.That(level.WorldId, Is.EqualTo("cr.world.reef"));
            Assert.That(level.Moves, Is.EqualTo(25));
            Assert.That(level.Objectives.Select(o => o.Kind), Is.EqualTo(new[] { GoalKind.CollectPiece, GoalKind.ClearObstacle }));
            Assert.That(level.Objectives[1].Count, Is.EqualTo(12));
            Assert.That(level.ObstacleIds, Is.EqualTo(new[] { "obstacle.ice" }));
            Assert.That(level.Integrity, Is.EqualTo(ContentIntegrity.DigestVerified));
            // Digest produced by Python json.dumps(sort_keys, compact) over the payload incl. the x- field.
            Assert.That(level.PayloadSha256, Is.EqualTo("9f0fbd8a6e0ef538c59eb63d1b0be191697c27867841e91be7d6e645d2bd994b"));
        }

        [Test]
        public void ValidLevelFixture_WithoutKnownWorld_IsDanglingAndNothingApplied()
        {
            var rx = Coral();
            var result = rx.Receive(Fixture("valid_level.json"));

            Assert.That(result.Accepted, Is.False);
            Assert.That(result.Items[0].Reason, Is.EqualTo(R.DanglingReference));
            Assert.That(rx.Applied, Is.Empty);
        }

        [Test]
        public void ValidEventFixture_AcceptsSameBatchReferencesWithPinnedSummary()
        {
            var rx = Coral();
            var result = rx.Receive(Fixture("valid_event.json"));

            Assert.That(result.Accepted, Is.True, result.Summary);
            var ev = rx.Applied["cr.event.winter"];
            Assert.That(ev.StartUtc, Is.EqualTo(new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc)));
            Assert.That(ev.EndUtc, Is.EqualTo(new DateTime(2026, 12, 8, 0, 0, 0, DateTimeKind.Utc)));
            Assert.That(ev.StartUtc.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(ev.LevelIds, Is.EqualTo(new[] { "cr.level.0001", "cr.level.0002" }));
            Assert.That(result.Summary, Is.EqualTo(
                "ACCEPTED reason=Accepted game=Coral_Rescue producer=fixture-producer/py items=4 rejected=0 noDigest=0\n" +
                "[0] World/cr.world.reef Accepted DigestVerified\n" +
                "[1] Level/cr.level.0001 Accepted DigestVerified\n" +
                "[2] Level/cr.level.0002 Accepted DigestVerified\n" +
                "[3] Event/cr.event.winter Accepted DigestVerified"));
            Assert.That(Coral().Receive(Fixture("valid_event.json")).Summary, Is.EqualTo(result.Summary));
        }

        // ---- malformed canonical fixtures ----------------------------------------

        private static IEnumerable<TestCaseData> MalformedFixtures()
        {
            yield return new TestCaseData("wrong_game.json", R.GameMismatch, -1, null, "expected Coral_Rescue");
            yield return new TestCaseData("unknown_kind.json", R.ItemRejected, 1, R.UnknownContentKind, "Offer");
            yield return new TestCaseData("bad_schema_major.json", R.SchemaMajorUnsupported, -1, null, "2.0");
            yield return new TestCaseData("digest_mismatch.json", R.ItemRejected, 0, R.DigestMismatch, "computed ");
            yield return new TestCaseData("dangling_level_ref.json", R.ItemRejected, 2, R.DanglingReference, "levelId cr.level.9999");
            yield return new TestCaseData("inverted_window.json", R.ItemRejected, 2, R.EventWindowInverted, "startUtc >= endUtc");
            yield return new TestCaseData("forbidden_price_field.json", R.ItemRejected, 0, R.ForbiddenMonetizationField, "x-priceUsd");
            yield return new TestCaseData("unknown_top_level_field.json", R.UnknownField, -1, null, "signedBy");
            yield return new TestCaseData("duplicate_content_id.json", R.ItemRejected, 1, R.DuplicateContentId, "cr.world.reef");
            yield return new TestCaseData("unknown_payload_field.json", R.ItemRejected, 0, R.UnknownPayloadField, "difficulty");
            yield return new TestCaseData("non_integer_number.json", R.MalformedDocument, -1, null, "non-integer number");
            yield return new TestCaseData("duplicate_json_key.json", R.MalformedDocument, -1, null, "duplicate key order");
        }

        [TestCaseSource(nameof(MalformedFixtures))]
        public void MalformedFixture_RejectsWholeBatchWithReason(string file, string batchReason, int itemIndex, string itemReason, string detail)
        {
            var rx = Coral();
            var result = rx.Receive(Fixture(file));

            Assert.That(result.Accepted, Is.False, result.Summary);
            Assert.That(result.Reason, Is.EqualTo(batchReason), result.Summary);
            Assert.That(result.Summary, Does.StartWith("REJECTED reason=" + batchReason));
            if (itemIndex >= 0)
            {
                Assert.That(result.Items[itemIndex].Reason, Is.EqualTo(itemReason), result.Summary);
                Assert.That(result.Items[itemIndex].Detail, Does.Contain(detail), result.Summary);
                Assert.That(result.Items.Count(i => !i.Accepted), Is.EqualTo(1), result.Summary);
            }
            else
            {
                Assert.That(result.Detail, Does.Contain(detail), result.Summary);
                Assert.That(result.Items, Is.Empty);
            }
            Assert.That(rx.Applied, Is.Empty, "atomic batch: nothing applied");
        }

        [Test]
        public void RejectedBatch_DoesNotDisturbPreviouslyAppliedContent()
        {
            var rx = Coral();
            Assert.That(rx.Receive(Fixture("valid_world.json")).Accepted, Is.True);
            var before = rx.Applied.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();

            var result = rx.Receive(Fixture("dangling_level_ref.json"));

            Assert.That(result.Accepted, Is.False);
            // valid_world already holds cr.world.reef v1 with the same payload -> Unchanged, still not applied as a batch.
            Assert.That(result.Items[0].Reason, Is.EqualTo(R.Unchanged));
            Assert.That(result.Items[1].Reason, Is.EqualTo(R.Accepted));
            Assert.That(rx.Applied.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray(), Is.EqualTo(before));
            Assert.That(rx.Applied.ContainsKey("cr.level.0001"), Is.False);
        }

        // ---- authorization rules ---------------------------------------------------

        [Test]
        public void KindOutsideGameAllowList_RejectsBatch()
        {
            var rx = Coral(kinds: new[] { ContentKind.World, ContentKind.Level });
            var result = rx.Receive(Fixture("valid_event.json"));

            Assert.That(result.Accepted, Is.False);
            Assert.That(result.Items[3].Reason, Is.EqualTo(R.ContentKindNotAllowed));
            Assert.That(rx.Applied, Is.Empty);
        }

        [Test]
        public void SameFixtureForOtherGameReceiver_IsGameMismatch()
        {
            var rx = new ContentMetadataReceiver("Wonder_Pop", AllKinds);
            var result = rx.Receive(Fixture("valid_world.json"));
            Assert.That(result.Reason, Is.EqualTo(R.GameMismatch));
            Assert.That(rx.Applied, Is.Empty);
        }

        [Test]
        public void UnknownGameReceiver_Throws()
        {
            Assert.Throws<ArgumentException>(() => new ContentMetadataReceiver("Unknown_Game", AllKinds));
            Assert.Throws<ArgumentException>(() => new ContentMetadataReceiver("Coral_Rescue", AllKinds, new[] { "bad id" }));
        }

        [TestCase("storeProductId")]
        [TestCase("priceUsd")]
        [TestCase("iapCredential")]
        public void ForbiddenKeyAtTopLevel_RejectsBatch(string key)
        {
            var result = Coral().Receive(Doc(World("cr.world.reef", 1, 1), extra: ",\"" + key + "\":\"x\""));
            Assert.That(result.Reason, Is.EqualTo(R.ForbiddenMonetizationField));
        }

        [TestCase("price", true)]
        [TestCase("x-priceTier", true)]
        [TestCase("sku", true)]
        [TestCase("iapId", true)]
        [TestCase("IAPId", true)]
        [TestCase("SKU", true)]
        [TestCase("storeProductId", true)]
        [TestCase("product_id", true)]
        [TestCase("apiKey", true)]
        [TestCase("receiptData", true)]
        [TestCase("authToken", true)]
        [TestCase("SKUs", true)]
        [TestCase("IAPs", true)]
        [TestCase("iaps", true)]
        [TestCase("storeSKUs", true)]
        [TestCase("productIds", true)]
        [TestCase("inAppItem", true)]
        [TestCase("paymentRef", true)]
        [TestCase("billingId", true)]
        [TestCase("transactionId", true)]
        [TestCase("paid", true)]
        [TestCase("bearerToken", true)]
        [TestCase("authHeader", true)]
        [TestCase("authorization", true)]
        [TestCase("storeproductid", true)]
        // Compact lowercase single-word forms (QA A1): caught by compact-substring matching.
        [TestCase("accesstoken", true)]
        [TestCase("access_token", true)]
        [TestCase("clientsecret", true)]
        [TestCase("authheader", true)]
        [TestCase("skuid", true)]
        [TestCase("x-sku-id", true)]
        [TestCase("iapid", true)]
        [TestCase("paidcoins", true)]
        [TestCase("x-paidCurrency", true)]
        [TestCase("paidgems", true)]
        [TestCase("paiditem", true)]
        [TestCase("hardcurrency", true)]
        [TestCase("premiumcurrency", true)]
        [TestCase("realcurrency", true)]
        [TestCase("creditcard", true)]
        [TestCase("cardNumber", true)]
        [TestCase("walletaddress", true)]
        [TestCase("iban", true)]
        [TestCase("x-cvv", true)]
        [TestCase("passwd", true)]
        [TestCase("password", true)]
        [TestCase("pwd", true)]
        [TestCase("x-pwdHint", true)]
        [TestCase("accesskey", true)]
        [TestCase("signingkey", true)]
        [TestCase("privatekey", true)]
        // Documented accepted fail-closed false positives.
        [TestCase("tokenCount", true)]
        [TestCase("x-secretLevel", true)]
        [TestCase("x-torchBearer", true)]
        [TestCase("x-pricelessGem", true)]
        // Allowed: soft-currency and benign words; short terms stay word-only.
        [TestCase("x-mediaPath", false)]
        [TestCase("x-coinCost", false)]
        [TestCase("gems", false)]
        [TestCase("authorId", false)]
        [TestCase("authorName", false)]
        [TestCase("producerId", false)]
        [TestCase("obstacleIds", false)]
        [TestCase("targetId", false)]
        [TestCase("objectives", false)]
        public void ForbiddenKeyDetection_MatchesWordsAndCompactSubstrings(string key, bool forbidden)
        {
            Assert.That(ContentMetadataReceiver.IsForbiddenKey(key), Is.EqualTo(forbidden));
        }

        [Test]
        public void Summary_NoDigestCountsOnlyAcceptedItems()
        {
            // Item 0 accepted without digest; item 1 rejected (bad id, never parsed -> NoDigest integrity).
            var result = Coral().Receive(Doc(World("cr.world.reef", 1, 1) + "," + World("bad id", 1, 2)));

            Assert.That(result.Accepted, Is.False, result.Summary);
            Assert.That(result.Items[1].Accepted, Is.False);
            Assert.That(result.Items[1].Integrity, Is.EqualTo(ContentIntegrity.NoDigest));
            Assert.That(result.Summary, Does.Contain(" items=2 rejected=1 noDigest=1"));
        }

        [Test]
        public void ForbiddenKeyNestedInObjective_RejectsItem()
        {
            var level = Level("cr.level.0001", "cr.world.reef", 1)
                .Replace("\"count\":10}", "\"count\":10,\"x-skuHint\":\"gems\"}");
            var result = Coral(worlds: new[] { "cr.world.reef" }).Receive(Doc(level));
            Assert.That(result.Items[0].Reason, Is.EqualTo(R.ForbiddenMonetizationField));
        }

        [TestCase("1", R.SchemaVersionMalformed)]
        [TestCase("01.0", R.SchemaVersionMalformed)]
        [TestCase("1.0.0", R.SchemaVersionMalformed)]
        [TestCase("0.9", R.SchemaMajorUnsupported)]
        [TestCase("1.7", R.Accepted)]
        public void SchemaVersion_OnlyMajorOneAccepted(string schema, string expected)
        {
            var result = Coral().Receive(Doc(World("cr.world.reef", 1, 1), schema: schema));
            Assert.That(result.Reason, Is.EqualTo(expected), result.Summary);
        }

        [TestCase("1x", R.IdInvalid)]
        [TestCase("cr world", R.IdInvalid)]
        [TestCase("9abc", R.IdInvalid)]
        [TestCase("cr.world.ok", R.Accepted)]
        public void ContentIdFormat_IsValidated(string id, string expected)
        {
            var result = Coral().Receive(Doc(World(id, 1, 1)));
            Assert.That(result.Items[0].Reason, Is.EqualTo(expected), result.Summary);
        }

        [TestCase("2026-12-01T00:00:00+03:00")]
        [TestCase("2026-12-01 00:00:00Z")]
        [TestCase("2026-13-01T00:00:00Z")]
        public void EventWindow_RequiresExactUtc(string start)
        {
            var items = World("cr.world.reef", 1, 1) + "," + Level("cr.level.0001", "cr.world.reef", 1) + "," +
                Event("cr.event.one", start, "2026-12-08T00:00:00Z", "\"cr.level.0001\"");
            var result = Coral().Receive(Doc(items));
            Assert.That(result.Items[2].Reason, Is.EqualTo(R.EventWindowMalformed));
        }

        [Test]
        public void EventWindow_EqualStartAndEnd_IsInverted()
        {
            var items = World("cr.world.reef", 1, 1) + "," + Level("cr.level.0001", "cr.world.reef", 1) + "," +
                Event("cr.event.one", "2026-12-01T00:00:00Z", "2026-12-01T00:00:00Z", "\"cr.level.0001\"");
            Assert.That(Coral().Receive(Doc(items)).Items[2].Reason, Is.EqualTo(R.EventWindowInverted));
        }

        [Test]
        public void EventReferencingWorldIdAsLevel_IsDangling()
        {
            var items = World("cr.world.reef", 1, 1) + "," +
                Event("cr.event.one", "2026-12-01T00:00:00Z", "2026-12-08T00:00:00Z", "\"cr.world.reef\"");
            Assert.That(Coral().Receive(Doc(items)).Items[1].Reason, Is.EqualTo(R.DanglingReference));
        }

        [Test]
        public void EventReferencingKnownShippedLevel_Resolves()
        {
            var rx = Coral(levels: new[] { "cr.level.0500" });
            var result = rx.Receive(Doc(Event("cr.event.one", "2026-12-01T00:00:00Z", "2026-12-08T00:00:00Z", "\"cr.level.0500\"")));
            Assert.That(result.Accepted, Is.True, result.Summary);
        }

        [Test]
        public void DuplicateLevelOrderInWorld_Rejected()
        {
            var items = World("cr.world.reef", 1, 1) + "," + Level("cr.level.0001", "cr.world.reef", 1) + "," +
                Level("cr.level.0002", "cr.world.reef", 1);
            var result = Coral().Receive(Doc(items));
            Assert.That(result.Items[2].Reason, Is.EqualTo(R.DuplicateOrder));
        }

        [TestCase("\"moves\":0", R.ValueOutOfRange)]
        [TestCase("\"moves\":\"20\"", R.FieldTypeInvalid)]
        public void LevelValueRangeAndType_Validated(string replacement, string expected)
        {
            var level = Level("cr.level.0001", "cr.world.reef", 1).Replace("\"moves\":20", replacement);
            var result = Coral(worlds: new[] { "cr.world.reef" }).Receive(Doc(level));
            Assert.That(result.Items[0].Reason, Is.EqualTo(expected));
        }

        [Test]
        public void UnknownObjectiveKind_Rejected()
        {
            var level = Level("cr.level.0001", "cr.world.reef", 1).Replace("CollectPiece", "Composite");
            var result = Coral(worlds: new[] { "cr.world.reef" }).Receive(Doc(level));
            Assert.That(result.Items[0].Reason, Is.EqualTo(R.ValueOutOfRange));
        }

        [Test]
        public void MalformedDigestFormat_Rejected()
        {
            var world = World("cr.world.reef", 1, 1);
            var item = world.Substring(0, world.Length - 1) + ",\"payloadSha256\":\"ABC\"}";
            Assert.That(Coral().Receive(Doc(item)).Items[0].Reason, Is.EqualTo(R.DigestMalformed));
        }

        // ---- versioning against applied state -------------------------------------

        [Test]
        public void Redelivery_IsUnchanged_NewerVersionReplaces_RegressionAndConflictRejected()
        {
            var rx = Coral();
            Assert.That(rx.Receive(Doc(World("cr.world.reef", 2, 1))).Accepted, Is.True);

            var again = rx.Receive(Doc(World("cr.world.reef", 2, 1)));
            Assert.That(again.Accepted, Is.True);
            Assert.That(again.Items[0].Reason, Is.EqualTo(R.Unchanged));

            Assert.That(rx.Receive(Doc(World("cr.world.reef", 1, 1))).Items[0].Reason, Is.EqualTo(R.ContentVersionRegression));
            Assert.That(rx.Receive(Doc(World("cr.world.reef", 2, 5))).Items[0].Reason, Is.EqualTo(R.ContentVersionConflict));
            Assert.That(rx.Applied["cr.world.reef"].Order, Is.EqualTo(1));

            Assert.That(rx.Receive(Doc(World("cr.world.reef", 3, 5))).Accepted, Is.True);
            Assert.That(rx.Applied["cr.world.reef"].Order, Is.EqualTo(5));
            Assert.That(rx.Applied["cr.world.reef"].ContentVersion, Is.EqualTo(3));
        }

        [Test]
        public void EmptyAndMissingEnvelopeParts_Rejected()
        {
            Assert.That(Coral().Receive(Doc("")).Reason, Is.EqualTo(R.EmptyBatch));
            Assert.That(Coral().Receive("{\"schemaVersion\":\"1.0\",\"gameId\":\"Coral_Rescue\",\"items\":[]}").Reason,
                Is.EqualTo(R.MissingField));
            Assert.That(Coral().Receive("[]").Reason, Is.EqualTo(R.MalformedDocument));
            Assert.That(Coral().Receive(Doc(World("cr.world.reef", 1, 1)).Replace("\"inline\"", "\"has space\"")).Reason,
                Is.EqualTo(R.ProducerIdInvalid));
        }

        [Test]
        public void WorldIdLiterallyWorld_DoesNotCollideWithWorldOrderSlots()
        {
            var items = World("world", 1, 1) + "," + Level("cr.level.0001", "world", 1);
            var result = Coral().Receive(Doc(items));
            Assert.That(result.Accepted, Is.True, result.Summary);
        }

        [Test]
        public void DuplicateWorldOrder_Rejected()
        {
            var result = Coral().Receive(Doc(World("cr.world.one", 1, 1) + "," + World("cr.world.two", 1, 1)));
            Assert.That(result.Items[1].Reason, Is.EqualTo(R.DuplicateOrder), result.Summary);
            Assert.That(result.Items[1].Detail, Does.Contain("cr.world.one"));
        }

        [Test]
        public void UnchangedRedelivery_KeepsStoredRecordProducerAndIntegrity()
        {
            var rx = Coral();
            Assert.That(rx.Receive(Fixture("valid_world.json")).Accepted, Is.True);
            var stored = rx.Applied["cr.world.reef"];
            Assert.That(stored.Integrity, Is.EqualTo(ContentIntegrity.DigestVerified));

            // Same id/version/payload, sent without a digest by a different producer.
            var again = rx.Receive(Doc(World("cr.world.reef", 1, 1)));
            Assert.That(again.Accepted, Is.True, again.Summary);
            Assert.That(again.Items[0].Reason, Is.EqualTo(R.Unchanged));
            Assert.That(rx.Applied["cr.world.reef"], Is.SameAs(stored));
            Assert.That(rx.Applied["cr.world.reef"].Integrity, Is.EqualTo(ContentIntegrity.DigestVerified));
            Assert.That(rx.Applied["cr.world.reef"].ProducerId, Is.EqualTo("fixture-producer/py"));
        }

        [Test]
        public void KindChangeForAppliedId_IsConflict()
        {
            var rx = Coral();
            Assert.That(rx.Receive(Doc(World("cr.thing.one", 1, 1))).Accepted, Is.True);
            var result = rx.Receive(Doc(Level("cr.thing.one", "cr.thing.one", 1)));
            Assert.That(result.Items[0].Reason, Is.EqualTo(R.ContentVersionConflict), result.Summary);
            Assert.That(rx.Applied["cr.thing.one"].Kind, Is.EqualTo(ContentKind.World));
        }

        [Test]
        public void UppercaseHexDigest_IsMalformed()
        {
            var world = World("cr.world.reef", 1, 1);
            var item = world.Substring(0, world.Length - 1) +
                ",\"payloadSha256\":\"A781679E01308CFEF90983A4C1350319A7E3993C3A3F5A8C8439781A326D7C8D\"}";
            Assert.That(Coral().Receive(Doc(item)).Items[0].Reason, Is.EqualTo(R.DigestMalformed));
        }

        // ---- canonical JSON (cross-producer golden) --------------------------------

        [Test]
        public void SizeLimit_IsEnforcedInUtf8Bytes()
        {
            const int limit = CanonicalJson.MaxUtf8Bytes;
            var asciiAtLimit = "{\"a\":\"" + new string('a', limit - 8) + "\"}";
            Assert.That(asciiAtLimit.Length, Is.EqualTo(limit));
            Assert.That(CanonicalJson.Parse(asciiAtLimit), Is.InstanceOf<Dictionary<string, object>>());
            Assert.Throws<FormatException>(() => CanonicalJson.Parse("{\"a\":\"" + new string('a', limit - 7) + "\"}"));
            // About half the limit in chars, but 2 UTF-8 bytes per char: over the byte limit.
            var multiByte = "{\"a\":\"" + new string('é', limit / 2) + "\"}";
            Assert.That(multiByte.Length, Is.LessThan(limit));
            var ex = Assert.Throws<FormatException>(() => CanonicalJson.Parse(multiByte));
            Assert.That(ex.Message, Does.Contain("UTF-8 bytes"));
        }

        [Test]
        public void SurrogatePair_RoundTripsAsPythonEscapes()
        {
            // Python: json.dumps({"a": "\U0001F600"}, sort_keys=True, separators=(",", ":"))
            const string expected = "{\"a\":\"\\ud83d\\ude00\"}";
            Assert.That(CanonicalJson.Serialize(CanonicalJson.Parse("{\"a\":\"\\ud83d\\ude00\"}")), Is.EqualTo(expected));
            Assert.That(CanonicalJson.Serialize(CanonicalJson.Parse("{\"a\":\"\U0001F600\"}")), Is.EqualTo(expected));
        }

        [Test]
        public void Writer_EscapesControlCharsDelAndSlashLikePython()
        {
            // Python: json.dumps({"a": "\x01\x7f/\b\f\n\r\t\"\\"}, sort_keys=True, separators=(",", ":"))
            //       -> {"a":"\u0001\u007f/\b\f\n\r\t\"\\"}
            var value = new Dictionary<string, object> { { "a", "\u0001\u007f/\b\f\n\r\t\"\\" } };
            Assert.That(CanonicalJson.Serialize(value), Is.EqualTo("{\"a\":\"\\u0001\\u007f/\\b\\f\\n\\r\\t\\\"\\\\\"}"));
        }

        [Test]
        public void CanonicalSerializer_MatchesPythonJsonDumpsByteForByte()
        {
            // Pretty, reordered input; expected text and digest produced by Python
            // json.dumps(payload, sort_keys=True, separators=(",", ":")).
            const string input = "{ \"x-editorNote\": \"authoring tool note \\u00E9 \\\"quoted\\\"\", \"worldId\": \"cr.world.reef\",\n" +
                "  \"order\": 1, \"obstacleIds\": [\"obstacle.ice\"], \"moves\": 25, \"objectives\": [\n" +
                "  {\"targetId\": \"piece.red\", \"kind\": \"CollectPiece\", \"count\": 30},\n" +
                "  {\"count\": 12, \"kind\": \"ClearObstacle\", \"targetId\": \"obstacle.ice\"} ] }";
            const string expected = "{\"moves\":25,\"objectives\":[{\"count\":30,\"kind\":\"CollectPiece\",\"targetId\":\"piece.red\"}," +
                "{\"count\":12,\"kind\":\"ClearObstacle\",\"targetId\":\"obstacle.ice\"}],\"obstacleIds\":[\"obstacle.ice\"]," +
                "\"order\":1,\"worldId\":\"cr.world.reef\",\"x-editorNote\":\"authoring tool note \\u00e9 \\\"quoted\\\"\"}";
            var parsed = CanonicalJson.Parse(input);
            Assert.That(CanonicalJson.Serialize(parsed), Is.EqualTo(expected));
            Assert.That(CanonicalJson.Sha256Hex(parsed), Is.EqualTo("9f0fbd8a6e0ef538c59eb63d1b0be191697c27867841e91be7d6e645d2bd994b"));
            Assert.That(CanonicalJson.Sha256Hex(CanonicalJson.Parse("{\"order\":1}")),
                Is.EqualTo("a781679e01308cfef90983a4c1350319a7e3993c3a3f5a8c8439781a326d7c8d"));
        }

        [TestCase("{\"a\":1} x")]
        [TestCase("{\"a\":01}")]
        [TestCase("{\"a\":1e3}")]
        [TestCase("{\"a\":\"\\ud800\"}")]
        [TestCase("{\"a\":\"tab\there\"}")]
        [TestCase("{\"a b\":1}")]
        [TestCase("{\"a\":1,}")]
        [TestCase("{\"a\":99999999999999999999}")]
        [TestCase("{\"a\":tru}")]
        public void StrictParser_RejectsAmbiguousOrNonCanonicalInput(string text)
        {
            Assert.Throws<FormatException>(() => CanonicalJson.Parse(text));
        }

        [Test]
        public void StrictParser_RejectsExcessiveNesting()
        {
            var deep = new string('[', 40) + new string(']', 40);
            Assert.Throws<FormatException>(() => CanonicalJson.Parse(deep));
            Assert.That(CanonicalJson.Parse(new string('[', 10) + new string(']', 10)), Is.InstanceOf<List<object>>());
        }
    }
}
