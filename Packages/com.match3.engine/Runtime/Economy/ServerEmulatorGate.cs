// ENG-M16-010 ServerEmulatorGate — stdlib only, no Unity/NUnit/SDK imports
using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime.Economy
{
    public enum EmulatorScenario
    {
        HappyPath,
        NetworkPartition,
        RetryOnFailure,
        DuplicateCallback,
        ConcurrentDevices,
        DataRecovery,
        FraudRejection
    }

    public readonly struct EmulatorResult
    {
        public EmulatorScenario Scenario { get; }
        public bool Succeeded { get; }
        public string FailureReason { get; }

        private EmulatorResult(EmulatorScenario scenario, bool succeeded, string failureReason)
        {
            Scenario = scenario;
            Succeeded = succeeded;
            FailureReason = failureReason;
        }

        public static EmulatorResult Success(EmulatorScenario scenario)
            => new EmulatorResult(scenario, true, string.Empty);

        public static EmulatorResult Failure(EmulatorScenario scenario, string reason)
        {
            if (string.IsNullOrEmpty(reason))
                throw new ArgumentException("reason must not be null or empty", nameof(reason));
            return new EmulatorResult(scenario, false, reason);
        }
    }

    public readonly struct ServerEmulatorPolicy
    {
        public bool UseRealBackend { get; }
        public bool AllowFraudFixtures { get; }
        public string PolicyDescription { get; }

        public ServerEmulatorPolicy(bool useRealBackend, bool allowFraudFixtures, string policyDescription)
        {
            if (string.IsNullOrEmpty(policyDescription))
                throw new ArgumentException("policyDescription must not be null or empty", nameof(policyDescription));
            UseRealBackend = useRealBackend;
            AllowFraudFixtures = allowFraudFixtures;
            PolicyDescription = policyDescription;
        }

        public static ServerEmulatorPolicy Offline(string desc)
            => new ServerEmulatorPolicy(false, true, desc);

        public static readonly ServerEmulatorPolicy Default =
            Offline("Server emulator uses local fixtures; no real backend required for integration tests");
    }

    public static class ServerEmulatorGate
    {
        public static (bool IsEligible, string Reason) CanRunScenario(
            string gameId,
            EmulatorScenario scenario,
            ServerEmulatorPolicy policy)
        {
            // Guard 1 FIRST (CRITICAL): Wonder_Pop is prohibited
            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal))
                return (false, "WonderPopEmulatorProhibited");

            // Guard 2: real backend not permitted for emulator
            if (policy.UseRealBackend)
                return (false, "RealBackendNotPermitted");

            return (true, string.Empty);
        }

        public static bool RequiresIsolatedFixtures(ServerEmulatorPolicy policy) => true;

        public static bool IsFraudScenario(EmulatorScenario scenario)
            => scenario == EmulatorScenario.FraudRejection;
    }

    public sealed class EmulatorFixtureRegistry
    {
        private readonly Dictionary<EmulatorScenario, string> _fixtures = new Dictionary<EmulatorScenario, string>();

        public void Register(EmulatorScenario scenario, string fixtureDescription)
        {
            if (string.IsNullOrEmpty(fixtureDescription))
                throw new ArgumentException("fixtureDescription must not be null or empty", nameof(fixtureDescription));
            _fixtures[scenario] = fixtureDescription;
        }

        public bool TryGet(EmulatorScenario scenario, out string fixtureDescription)
            => _fixtures.TryGetValue(scenario, out fixtureDescription);

        public void Clear() => _fixtures.Clear();

        public int Count => _fixtures.Count;
    }
}
