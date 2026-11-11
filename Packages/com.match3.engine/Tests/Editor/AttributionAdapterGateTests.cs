// ENG-M18-007 AttributionAdapterGate tests
using System;
using NUnit.Framework;
using Match3.Engine.Runtime.Analytics;

namespace Match3.Engine.Tests.Editor
{
    [TestFixture]
    public class AttributionAdapterGateTests
    {
        // --- AttributionToken ---

        [Test]
        public void Token_NullGameId_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                AttributionToken.Create(null, AttributionChannel.Organic, true));
        }

        [Test]
        public void Token_EmptyGameId_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                AttributionToken.Create(string.Empty, AttributionChannel.Organic, true));
        }

        [Test]
        public void Token_Create_SetsAllProperties()
        {
            var t = AttributionToken.Create("Coral_Rescue", AttributionChannel.PaidSearch, false);
            Assert.AreEqual("Coral_Rescue", t.GameId);
            Assert.AreEqual(AttributionChannel.PaidSearch, t.Channel);
            Assert.IsFalse(t.TrackingAllowed);
        }

        [Test]
        public void Token_IsOrganic_TrueOnlyForOrganic()
        {
            Assert.IsTrue(AttributionToken.Create("Coral_Rescue", AttributionChannel.Organic, true).IsOrganic);
            Assert.IsFalse(AttributionToken.Create("Coral_Rescue", AttributionChannel.PaidSearch, true).IsOrganic);
            Assert.IsFalse(AttributionToken.Create("Coral_Rescue", AttributionChannel.PaidSocial, true).IsOrganic);
            Assert.IsFalse(AttributionToken.Create("Coral_Rescue", AttributionChannel.CrossPromo, true).IsOrganic);
        }

        [Test]
        public void Token_IsPaidChannel_FalseOnlyForOrganic()
        {
            Assert.IsFalse(AttributionToken.Create("Coral_Rescue", AttributionChannel.Organic, true).IsPaidChannel);
            Assert.IsTrue(AttributionToken.Create("Coral_Rescue", AttributionChannel.PaidSearch, true).IsPaidChannel);
            Assert.IsTrue(AttributionToken.Create("Coral_Rescue", AttributionChannel.PaidSocial, true).IsPaidChannel);
            Assert.IsTrue(AttributionToken.Create("Coral_Rescue", AttributionChannel.CrossPromo, true).IsPaidChannel);
        }

        // --- AttributionPolicy ---

        [Test]
        public void Policy_NullDescription_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => AttributionPolicy.Safe(null));
        }

        [Test]
        public void Policy_EmptyDescription_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => AttributionPolicy.Safe(string.Empty));
        }

        [Test]
        public void Policy_Safe_SetsCorrectValues()
        {
            var p = AttributionPolicy.Safe("test");
            Assert.IsFalse(p.AllowCrossAppTracking);
            Assert.AreEqual("test", p.PolicyDescription);
        }

        [Test]
        public void Policy_Default_IsCorrect()
        {
            var d = AttributionPolicy.Default;
            Assert.IsFalse(d.AllowCrossAppTracking);
            Assert.AreEqual("Cross-app tracking disabled; explicit consent required", d.PolicyDescription);
        }

        // --- AttributionAdapterGate.CanRecordAttribution ---

        [Test]
        public void Gate_WonderPop_BlockedByGuard1()
        {
            var token = AttributionToken.Create("Wonder_Pop", AttributionChannel.Organic, true);
            var policy = AttributionPolicy.Safe("desc");
            var (eligible, reason) = AttributionAdapterGate.CanRecordAttribution("Wonder_Pop", token, policy);
            Assert.IsFalse(eligible);
            Assert.AreEqual("WonderPopAttributionProhibited", reason);
        }

        [Test]
        public void Gate_WonderPop_OrdinalCaseSensitive_LowercaseUnknownGame()
        {
            // Ordinal catalog lookup: a case variant of Wonder_Pop is not a catalog ID, so it fails closed with UnknownGame (never eligible, never the Wonder_Pop reason).
            var token = AttributionToken.Create("wonder_pop", AttributionChannel.Organic, true);
            var policy = AttributionPolicy.Safe("desc");
            var (eligible, reason) = AttributionAdapterGate.CanRecordAttribution("wonder_pop", token, policy);
            Assert.IsFalse(eligible);
            Assert.AreEqual("UnknownGame", reason);
        }

        [Test]
        public void Gate_TrackingNotAllowed_BlockedByGuard2()
        {
            var token = AttributionToken.Create("Coral_Rescue", AttributionChannel.Organic, false);
            var policy = AttributionPolicy.Safe("desc");
            var (eligible, reason) = AttributionAdapterGate.CanRecordAttribution("Coral_Rescue", token, policy);
            Assert.IsFalse(eligible);
            Assert.AreEqual("TrackingNotAllowed", reason);
        }

        [Test]
        public void Gate_PaidSearch_CrossAppDisabled_BlockedByGuard3()
        {
            var token = AttributionToken.Create("Coral_Rescue", AttributionChannel.PaidSearch, true);
            var policy = AttributionPolicy.Safe("desc"); // AllowCrossAppTracking=false
            var (eligible, reason) = AttributionAdapterGate.CanRecordAttribution("Coral_Rescue", token, policy);
            Assert.IsFalse(eligible);
            Assert.AreEqual("CrossAppTrackingDisabled", reason);
        }

        [Test]
        public void Gate_PaidSocial_CrossAppDisabled_BlockedByGuard3()
        {
            var token = AttributionToken.Create("Coral_Rescue", AttributionChannel.PaidSocial, true);
            var policy = AttributionPolicy.Safe("desc");
            var (eligible, reason) = AttributionAdapterGate.CanRecordAttribution("Coral_Rescue", token, policy);
            Assert.IsFalse(eligible);
            Assert.AreEqual("CrossAppTrackingDisabled", reason);
        }

        [Test]
        public void Gate_CrossPromo_CrossAppDisabled_BlockedByGuard3()
        {
            var token = AttributionToken.Create("Coral_Rescue", AttributionChannel.CrossPromo, true);
            var policy = AttributionPolicy.Safe("desc");
            var (eligible, reason) = AttributionAdapterGate.CanRecordAttribution("Coral_Rescue", token, policy);
            Assert.IsFalse(eligible);
            Assert.AreEqual("CrossAppTrackingDisabled", reason);
        }

        [Test]
        public void Gate_Organic_CrossAppDisabled_IsEligible()
        {
            // Organic is not a paid channel — Guard 3 must NOT fire
            var token = AttributionToken.Create("Coral_Rescue", AttributionChannel.Organic, true);
            var policy = AttributionPolicy.Safe("desc"); // AllowCrossAppTracking=false
            var (eligible, _) = AttributionAdapterGate.CanRecordAttribution("Coral_Rescue", token, policy);
            Assert.IsTrue(eligible);
        }

        private static readonly AttributionPolicy[] AllConstructiblePolicies =
            { AttributionPolicy.Safe("desc"), AttributionPolicy.Default, default(AttributionPolicy) };

        [Test]
        public void Gate_PaidSearch_BlockedUnderEveryConstructiblePolicy()
        {
            // No factory can enable cross-app tracking; default(struct) normalizes to Default.
            var token = AttributionToken.Create("Coral_Rescue", AttributionChannel.PaidSearch, true);
            foreach (var policy in AllConstructiblePolicies)
            {
                var (eligible, reason) = AttributionAdapterGate.CanRecordAttribution("Coral_Rescue", token, policy);
                Assert.IsFalse(eligible);
                Assert.AreEqual("CrossAppTrackingDisabled", reason);
            }
        }

        [Test]
        public void Gate_NoConsent_BlockedUnderEveryConstructiblePolicy()
        {
            var token = AttributionToken.Create("Coral_Rescue", AttributionChannel.Organic, false);
            foreach (var policy in AllConstructiblePolicies)
            {
                var (eligible, reason) = AttributionAdapterGate.CanRecordAttribution("Coral_Rescue", token, policy);
                Assert.IsFalse(eligible);
                Assert.AreEqual("TrackingNotAllowed", reason);
                Assert.IsTrue(AttributionAdapterGate.RequiresConsent(policy));
            }
        }

        [Test]
        public void Gate_DefaultStructPolicy_FailsClosed()
        {
            var token = AttributionToken.Create("Coral_Rescue", AttributionChannel.PaidSocial, true);
            var (e1, expected) = AttributionAdapterGate.CanRecordAttribution("Coral_Rescue", token, AttributionPolicy.Default);
            var (e2, reason) = AttributionAdapterGate.CanRecordAttribution("Coral_Rescue", token, default(AttributionPolicy));
            Assert.IsFalse(e1);
            Assert.IsFalse(e2);
            Assert.AreEqual(expected, reason);
        }

        [Test]
        public void Gate_GameIdMismatch_Blocked()
        {
            var token = AttributionToken.Create("Wonder_Pop", AttributionChannel.Organic, true);
            var (eligible, reason) = AttributionAdapterGate.CanRecordAttribution("Coral_Rescue", token, AttributionPolicy.Default);
            Assert.IsFalse(eligible);
            Assert.AreEqual("GameIdMismatch", reason);
        }

        [Test]
        public void Gate_WonderPopParam_TokenDiffers_GameIdMismatch()
        {
            var token = AttributionToken.Create("Coral_Rescue", AttributionChannel.Organic, true);
            var (eligible, reason) = AttributionAdapterGate.CanRecordAttribution("Wonder_Pop", token, AttributionPolicy.Default);
            Assert.IsFalse(eligible);
            Assert.AreEqual("GameIdMismatch", reason);
        }

        // --- Helpers ---

        [Test]
        public void IsOrganicInstall_ReturnsTokenIsOrganic()
        {
            var organic = AttributionToken.Create("Coral_Rescue", AttributionChannel.Organic, true);
            var paid = AttributionToken.Create("Coral_Rescue", AttributionChannel.PaidSearch, true);
            Assert.IsTrue(AttributionAdapterGate.IsOrganicInstall(organic));
            Assert.IsFalse(AttributionAdapterGate.IsOrganicInstall(paid));
        }

        [Test]
        public void RequiresConsent_AlwaysTrue()
        {
            var p = AttributionPolicy.Safe("desc");
            Assert.IsTrue(AttributionAdapterGate.RequiresConsent(p));
        }

        // --- AttributionLog ---

        [Test]
        public void Log_Record_NullGameId_ThrowsArgumentException()
        {
            var log = new AttributionLog();
            Assert.Throws<ArgumentException>(() => log.Record(null, AttributionChannel.Organic));
        }

        [Test]
        public void Log_Record_EmptyGameId_ThrowsArgumentException()
        {
            var log = new AttributionLog();
            Assert.Throws<ArgumentException>(() => log.Record(string.Empty, AttributionChannel.Organic));
        }

        [Test]
        public void Log_Record_IncrementsCount()
        {
            var log = new AttributionLog();
            Assert.AreEqual(0, log.Count);
            log.Record("Coral_Rescue", AttributionChannel.Organic);
            Assert.AreEqual(1, log.Count);
            log.Record("Garage_Legends", AttributionChannel.PaidSearch);
            Assert.AreEqual(2, log.Count);
        }

        [Test]
        public void Log_HasEntry_ReturnsTrueForRecordedGame()
        {
            var log = new AttributionLog();
            log.Record("Coral_Rescue", AttributionChannel.Organic);
            Assert.IsTrue(log.HasEntry("Coral_Rescue"));
            Assert.IsFalse(log.HasEntry("Garage_Legends"));
        }

        [Test]
        public void Log_Clear_ResetsCountAndEntries()
        {
            var log = new AttributionLog();
            log.Record("Coral_Rescue", AttributionChannel.Organic);
            log.Clear();
            Assert.AreEqual(0, log.Count);
            Assert.IsFalse(log.HasEntry("Coral_Rescue"));
        }

        [Test]
        public void Log_HasEntry_OrdinalCaseSensitive()
        {
            var log = new AttributionLog();
            log.Record("Wonder_Pop", AttributionChannel.Organic);
            Assert.IsTrue(log.HasEntry("Wonder_Pop"));
            Assert.IsFalse(log.HasEntry("wonder_pop"));
        }


        [Test]
        public void NullOrEmptyGameId_DefaultRecord_GameIdMissing()
        {
            foreach (var gameId in new[] { null, string.Empty })
            {
                var (eligible, reason) = AttributionAdapterGate.CanRecordAttribution(gameId, default(AttributionToken), default(AttributionPolicy));
                Assert.IsFalse(eligible);
                Assert.AreEqual("GameIdMissing", reason);
            }
        }

        [Test]
        public void UnknownOrCaseVariantGameId_DefaultRecord_UnknownGame()
        {
            foreach (var gameId in new[] { "Unknown_Game", "coral_rescue", "CORAL_RESCUE", " Coral_Rescue", "Coral_Rescue ", "wonder_pop", "WONDER_POP" })
            {
                var (eligible, reason) = AttributionAdapterGate.CanRecordAttribution(gameId, default(AttributionToken), default(AttributionPolicy));
                Assert.IsFalse(eligible);
                Assert.AreEqual("UnknownGame", reason);
            }
        }
    }
}
