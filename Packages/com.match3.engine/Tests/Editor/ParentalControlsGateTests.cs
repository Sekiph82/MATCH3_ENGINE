using System;
using NUnit.Framework;
using Match3.Engine.Runtime.Privacy;

namespace Match3.Engine.Tests.Editor
{
    [TestFixture]
    public class ParentalControlsGateTests
    {
        [Test]
        public void ParentalGateRecord_Create_NullGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() => ParentalGateRecord.Create(null, ParentalGateAction.ChatAccess, false, false));
        }

        [Test]
        public void ParentalGateRecord_Create_EmptyGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() => ParentalGateRecord.Create(string.Empty, ParentalGateAction.ChatAccess, false, false));
        }

        [Test]
        public void ParentalGateRecord_Create_Valid_Succeeds()
        {
            var record = ParentalGateRecord.Create("GameA", ParentalGateAction.ChatAccess, false, false);
            Assert.AreEqual("GameA", record.GameId);
            Assert.AreEqual(ParentalGateAction.ChatAccess, record.Action);
        }

        [Test]
        public void ParentalGateRecord_RequiresParentalApproval_ExternalLink_True()
        {
            var record = ParentalGateRecord.Create("GameA", ParentalGateAction.ExternalLinkOpen, false, false);
            Assert.IsTrue(record.RequiresParentalApproval);
        }

        [Test]
        public void ParentalGateRecord_RequiresParentalApproval_PurchaseFlow_True()
        {
            var record = ParentalGateRecord.Create("GameA", ParentalGateAction.PurchaseFlow, false, false);
            Assert.IsTrue(record.RequiresParentalApproval);
        }

        [Test]
        public void ParentalGateRecord_RequiresParentalApproval_AccountCreation_True()
        {
            var record = ParentalGateRecord.Create("GameA", ParentalGateAction.AccountCreation, false, false);
            Assert.IsTrue(record.RequiresParentalApproval);
        }

        [Test]
        public void ParentalGateRecord_RequiresParentalApproval_SocialShare_True()
        {
            var record = ParentalGateRecord.Create("GameA", ParentalGateAction.SocialShare, false, false);
            Assert.IsTrue(record.RequiresParentalApproval);
        }

        [Test]
        public void ParentalGateRecord_RequiresParentalApproval_ChatAccess_False()
        {
            var record = ParentalGateRecord.Create("GameA", ParentalGateAction.ChatAccess, false, false);
            Assert.IsFalse(record.RequiresParentalApproval);
        }

        [Test]
        public void ParentalGateRecord_RequiresParentalApproval_SettingsAccess_False()
        {
            var record = ParentalGateRecord.Create("GameA", ParentalGateAction.SettingsAccess, false, false);
            Assert.IsFalse(record.RequiresParentalApproval);
        }

        [Test]
        public void ParentalGateRecord_IsGated_RestrictedProfile_True()
        {
            var record = ParentalGateRecord.Create("GameA", ParentalGateAction.ChatAccess, false, true);
            Assert.IsTrue(record.IsGated);
        }

        [Test]
        public void ParentalGateRecord_IsGated_NotRestricted_False()
        {
            var record = ParentalGateRecord.Create("GameA", ParentalGateAction.ChatAccess, false, false);
            Assert.IsFalse(record.IsGated);
        }

        [Test]
        public void ParentalControlsPolicy_Safe_Values_Correct()
        {
            var policy = ParentalControlsPolicy.Safe("Test policy");
            Assert.IsTrue(policy.EnforceParentalGate);
            Assert.IsTrue(policy.BlockExternalLinksWithoutApproval);
            Assert.AreEqual("Test policy", policy.PolicyDescription);
        }

        [Test]
        public void ParentalControlsPolicy_Default_NotNull()
        {
            var policy = ParentalControlsPolicy.Default;
            Assert.IsNotNull(policy.PolicyDescription);
            Assert.IsTrue(policy.EnforceParentalGate);
        }

        [Test]
        public void ParentalControlsPolicy_NullDescription_Throws()
        {
            Assert.Throws<ArgumentException>(() => ParentalControlsPolicy.Safe(null));
        }

        [Test]
        public void CanPerformAction_WonderPop_ExternalLink_NoApproval_Blocked()
        {
            var record = ParentalGateRecord.Create("Wonder_Pop", ParentalGateAction.ExternalLinkOpen, false, false);
            var policy = ParentalControlsPolicy.Default;
            var result = ParentalControlsGate.CanPerformAction("Wonder_Pop", record, policy, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("WonderPopExternalLinkGated", reason);
        }

        [Test]
        public void CanPerformAction_WonderPop_ExternalLink_HasApproval_NotBlocked()
        {
            var record = ParentalGateRecord.Create("Wonder_Pop", ParentalGateAction.ExternalLinkOpen, true, false);
            var policy = ParentalControlsPolicy.Default;
            var result = ParentalControlsGate.CanPerformAction("Wonder_Pop", record, policy, out var reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanPerformAction_WonderPop_Purchase_SafePolicy_Blocked()
        {
            var record = ParentalGateRecord.Create("Wonder_Pop", ParentalGateAction.PurchaseFlow, false, false);
            var policy = ParentalControlsPolicy.Default;
            var result = ParentalControlsGate.CanPerformAction("Wonder_Pop", record, policy, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("ParentalApprovalRequired", reason);
        }

        [Test]
        public void CanPerformAction_WonderPop_Lowercase_ExternalLink_NotBlocked()
        {
            var record = ParentalGateRecord.Create("wonder_pop", ParentalGateAction.ExternalLinkOpen, false, false);
            var policy = ParentalControlsPolicy.Safe("Test");
            var result = ParentalControlsGate.CanPerformAction("wonder_pop", record, policy, out var reason);
            Assert.IsFalse(result);
            Assert.AreNotEqual("WonderPopExternalLinkGated", reason);
        }

        [Test]
        public void CanPerformAction_SafePolicy_ExternalLink_NoApproval_Blocked()
        {
            var record = ParentalGateRecord.Create("OtherGame", ParentalGateAction.ExternalLinkOpen, false, false);
            var policy = ParentalControlsPolicy.Default;
            var result = ParentalControlsGate.CanPerformAction("OtherGame", record, policy, out var reason);
            Assert.IsFalse(result);
            Assert.That(reason == "ParentalApprovalRequired" || reason == "ExternalLinkBlocked");
        }

        [Test]
        public void CanPerformAction_HasApproval_ExternalLink_Eligible()
        {
            var record = ParentalGateRecord.Create("OtherGame", ParentalGateAction.ExternalLinkOpen, true, false);
            var policy = ParentalControlsPolicy.Default;
            var result = ParentalControlsGate.CanPerformAction("OtherGame", record, policy, out var reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanPerformAction_ChatAccess_SafePolicy_Eligible()
        {
            var record = ParentalGateRecord.Create("OtherGame", ParentalGateAction.ChatAccess, false, false);
            var policy = ParentalControlsPolicy.Default;
            var result = ParentalControlsGate.CanPerformAction("OtherGame", record, policy, out var reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void CanPerformAction_SettingsAccess_SafePolicy_Eligible()
        {
            var record = ParentalGateRecord.Create("OtherGame", ParentalGateAction.SettingsAccess, false, false);
            var policy = ParentalControlsPolicy.Default;
            var result = ParentalControlsGate.CanPerformAction("OtherGame", record, policy, out var reason);
            Assert.IsTrue(result);
            Assert.AreEqual(string.Empty, reason);
        }

        [Test]
        public void RequiresParentalApproval_Helper_Purchase_True()
        {
            var record = ParentalGateRecord.Create("GameA", ParentalGateAction.PurchaseFlow, false, false);
            Assert.IsTrue(ParentalControlsGate.RequiresParentalApproval(record));
        }

        [Test]
        public void RequiresParentalApproval_Helper_Chat_False()
        {
            var record = ParentalGateRecord.Create("GameA", ParentalGateAction.ChatAccess, false, false);
            Assert.IsFalse(ParentalControlsGate.RequiresParentalApproval(record));
        }

        [Test]
        public void ParentalControlLog_Record_And_Count()
        {
            var log = new ParentalControlLog();
            log.Record("GameA", "ExternalLinkOpen");
            log.Record("GameA", "PurchaseFlow");
            Assert.AreEqual(2, log.Count);
        }

        [Test]
        public void ParentalControlLog_HasEntry_True()
        {
            var log = new ParentalControlLog();
            log.Record("GameA", "ChatAccess");
            Assert.IsTrue(log.HasEntry("GameA"));
        }

        [Test]
        public void ParentalControlLog_Clear_ResetsCount()
        {
            var log = new ParentalControlLog();
            log.Record("GameA", "ChatAccess");
            log.Clear();
            Assert.AreEqual(0, log.Count);
        }

        [Test]
        public void ParentalControlLog_Record_NullGameId_Throws()
        {
            var log = new ParentalControlLog();
            Assert.Throws<ArgumentException>(() => log.Record(null, "ChatAccess"));
        }

        // QA remediation: fail-closed default policy, GameId mismatch guard, log argument validation
        [Test]
        public void CanPerformAction_DefaultStructPolicy_FailsClosed()
        {
            var record = ParentalGateRecord.Create("GameA", ParentalGateAction.PurchaseFlow, false, false);
            var expected = ParentalControlsGate.CanPerformAction("GameA", record, ParentalControlsPolicy.Default, out var expectedReason);
            var result = ParentalControlsGate.CanPerformAction("GameA", record, default(ParentalControlsPolicy), out var reason);
            Assert.IsFalse(expected);
            Assert.IsFalse(result);
            Assert.AreEqual("ParentalApprovalRequired", expectedReason);
            Assert.AreEqual(expectedReason, reason);
        }

        [Test]
        public void CanPerformAction_GameIdMismatch_Blocked()
        {
            var record = ParentalGateRecord.Create("Wonder_Pop", ParentalGateAction.ExternalLinkOpen, false, false);
            var result = ParentalControlsGate.CanPerformAction("Coral_Rescue", record, ParentalControlsPolicy.Default, out var reason);
            Assert.IsFalse(result);
            Assert.AreEqual("GameIdMismatch", reason);
        }

        [Test]
        public void ParentalControlLog_Record_NullOrEmptySecondArg_Throws()
        {
            var log = new ParentalControlLog();
            Assert.Throws<System.ArgumentException>(() => log.Record("GameA", null));
            Assert.Throws<System.ArgumentException>(() => log.Record("GameA", string.Empty));
            Assert.AreEqual(0, log.Count);
        }

        [Test]
        public void ParentalControlLog_HasEntry_IsOrdinal()
        {
            var log = new ParentalControlLog();
            log.Record("Wonder_Pop", "evt");
            Assert.IsTrue(log.HasEntry("Wonder_Pop"));
            Assert.IsFalse(log.HasEntry("wonder_pop"));
            // Soft hyphen (U+00AD) is ignorable under culture-sensitive comparison; Ordinal must not match it.
            log.Record("Game\u00ADA", "evt");
            Assert.IsTrue(log.HasEntry("Game\u00ADA"));
            Assert.IsFalse(log.HasEntry("GameA"));
        }
    }
}
