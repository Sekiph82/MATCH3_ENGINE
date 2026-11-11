// ENG-M16-010 ServerEmulatorGate integration test contract
using NUnit.Framework;
using Match3.Engine.Runtime.Economy;

namespace Match3.Engine.Tests.Editor
{
    [TestFixture]
    public class ServerEmulatorGateTests
    {
        // 1. Enum has exactly 7 values
        [Test]
        public void EmulatorScenario_HasSevenValues()
        {
            var values = System.Enum.GetValues(typeof(EmulatorScenario));
            Assert.AreEqual(7, values.Length);
        }

        // 2. All 7 enum members present
        [Test]
        public void EmulatorScenario_AllExpectedMembersPresent()
        {
            foreach (var name in new[] { "HappyPath", "NetworkPartition", "RetryOnFailure", "DuplicateCallback",
                                         "ConcurrentDevices", "DataRecovery", "FraudRejection" })
                Assert.IsTrue(System.Enum.IsDefined(typeof(EmulatorScenario), name), name);
        }

        // 3. EmulatorResult.Success factory
        [Test]
        public void EmulatorResult_Success_SetsSucceededTrueAndEmptyReason()
        {
            var result = EmulatorResult.Success(EmulatorScenario.HappyPath);
            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(string.Empty, result.FailureReason);
            Assert.AreEqual(EmulatorScenario.HappyPath, result.Scenario);
        }

        // 4. EmulatorResult.Failure factory
        [Test]
        public void EmulatorResult_Failure_SetsSucceededFalseAndReason()
        {
            var result = EmulatorResult.Failure(EmulatorScenario.NetworkPartition, "timeout");
            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual("timeout", result.FailureReason);
            Assert.AreEqual(EmulatorScenario.NetworkPartition, result.Scenario);
        }

        // 5. EmulatorResult.Failure rejects null/empty reason
        [Test]
        public void EmulatorResult_Failure_ThrowsOnNullReason()
        {
            Assert.Throws<System.ArgumentException>(() => EmulatorResult.Failure(EmulatorScenario.HappyPath, null));
            Assert.Throws<System.ArgumentException>(() => EmulatorResult.Failure(EmulatorScenario.HappyPath, ""));
        }

        // 6. ServerEmulatorPolicy.Default is Offline with UseRealBackend=false and AllowFraudFixtures=true
        [Test]
        public void ServerEmulatorPolicy_DefaultIsOfflinePolicy()
        {
            var policy = ServerEmulatorPolicy.Default;
            Assert.IsFalse(policy.UseRealBackend);
            Assert.IsTrue(policy.AllowFraudFixtures);
            Assert.IsFalse(string.IsNullOrEmpty(policy.PolicyDescription));
        }

        // 7. CanRunScenario Wonder_Pop blocked FIRST (guard order)
        [Test]
        public void CanRunScenario_WonderPop_IneligibleWithWonderPopReason()
        {
            var policy = ServerEmulatorPolicy.Default;
            var (eligible, reason) = ServerEmulatorGate.CanRunScenario("Wonder_Pop", EmulatorScenario.HappyPath, policy);
            Assert.IsFalse(eligible);
            Assert.AreEqual("WonderPopEmulatorProhibited", reason);
        }

        // 8. Wonder_Pop check is Ordinal (case-sensitive) — different case is NOT blocked by guard 1
        [Test]
        public void CanRunScenario_WonderPopCaseDiffers_IsUnknownGame()
        {
            var policy = ServerEmulatorPolicy.Default;
            // Ordinal: "wonder_pop" is not a catalog ID, so it fails closed as UnknownGame
            var (eligible, reason) = ServerEmulatorGate.CanRunScenario("wonder_pop", EmulatorScenario.HappyPath, policy);
            Assert.IsFalse(eligible);
            Assert.AreEqual("UnknownGame", reason);
        }

        // 9. CanRunScenario real backend blocked
        [Test]
        public void CanRunScenario_RealBackendPolicy_Ineligible()
        {
            var policy = new ServerEmulatorPolicy(true, false, "real backend policy");
            var (eligible, reason) = ServerEmulatorGate.CanRunScenario("Coral_Rescue", EmulatorScenario.HappyPath, policy);
            Assert.IsFalse(eligible);
            Assert.AreEqual("RealBackendNotPermitted", reason);
        }

        // 10. RequiresIsolatedFixtures always true
        [Test]
        public void RequiresIsolatedFixtures_AlwaysTrue()
        {
            Assert.IsTrue(ServerEmulatorGate.RequiresIsolatedFixtures(ServerEmulatorPolicy.Default));
            var realBackend = new ServerEmulatorPolicy(true, false, "desc");
            Assert.IsTrue(ServerEmulatorGate.RequiresIsolatedFixtures(realBackend));
        }

        // 11. IsFraudScenario only true for FraudRejection
        [Test]
        public void IsFraudScenario_OnlyTrueForFraudRejection()
        {
            Assert.IsTrue(ServerEmulatorGate.IsFraudScenario(EmulatorScenario.FraudRejection));
            Assert.IsFalse(ServerEmulatorGate.IsFraudScenario(EmulatorScenario.HappyPath));
            Assert.IsFalse(ServerEmulatorGate.IsFraudScenario(EmulatorScenario.NetworkPartition));
            Assert.IsFalse(ServerEmulatorGate.IsFraudScenario(EmulatorScenario.DataRecovery));
        }

        // 12. EmulatorFixtureRegistry register, get, clear, count
        [Test]
        public void EmulatorFixtureRegistry_RegisterGetClearCount()
        {
            var registry = new EmulatorFixtureRegistry();
            Assert.AreEqual(0, registry.Count);

            registry.Register(EmulatorScenario.HappyPath, "happy fixture");
            Assert.AreEqual(1, registry.Count);
            Assert.IsTrue(registry.TryGet(EmulatorScenario.HappyPath, out var desc));
            Assert.AreEqual("happy fixture", desc);

            registry.Register(EmulatorScenario.FraudRejection, "fraud fixture");
            Assert.AreEqual(2, registry.Count);

            Assert.IsFalse(registry.TryGet(EmulatorScenario.NetworkPartition, out _));

            registry.Clear();
            Assert.AreEqual(0, registry.Count);
            Assert.IsFalse(registry.TryGet(EmulatorScenario.HappyPath, out _));
        }

        // 13. EmulatorFixtureRegistry rejects null/empty fixtureDescription
        [Test]
        public void EmulatorFixtureRegistry_Register_ThrowsOnNullOrEmptyDescription()
        {
            var registry = new EmulatorFixtureRegistry();
            Assert.Throws<System.ArgumentException>(() => registry.Register(EmulatorScenario.HappyPath, null));
            Assert.Throws<System.ArgumentException>(() => registry.Register(EmulatorScenario.HappyPath, ""));
        }

        // 14. ServerEmulatorPolicy constructor throws on null/empty description
        [Test]
        public void ServerEmulatorPolicy_ThrowsOnNullOrEmptyDescription()
        {
            Assert.Throws<System.ArgumentException>(() => new ServerEmulatorPolicy(false, true, null));
            Assert.Throws<System.ArgumentException>(() => new ServerEmulatorPolicy(false, true, ""));
        }


        [Test]
        public void CanRunScenario_NullOrEmptyGameId_FailsClosed()
        {
            foreach (var gameId in new[] { null, string.Empty })
            {
                var (eligible, reason) = ServerEmulatorGate.CanRunScenario(gameId, EmulatorScenario.HappyPath, ServerEmulatorPolicy.Default);
                Assert.IsFalse(eligible, gameId);
                Assert.AreEqual("GameIdMissing", reason);
            }
        }

        [Test]
        public void CanRunScenario_NonCatalogGameId_UnknownGame()
        {
            foreach (var unknownId in new[] { "Unknown_Game", "coral_rescue", "CORAL_RESCUE", " Coral_Rescue", "Coral_Rescue " })
            {
                var (eligible, reason) = ServerEmulatorGate.CanRunScenario(unknownId, EmulatorScenario.HappyPath, ServerEmulatorPolicy.Default);
                Assert.IsFalse(eligible, unknownId);
                Assert.AreEqual("UnknownGame", reason);
            }
        }
    }
}
