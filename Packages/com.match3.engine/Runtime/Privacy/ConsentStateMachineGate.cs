using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime.Privacy
{
    public enum ConsentState
    {
        NotRequested,
        Pending,
        Granted,
        Denied,
        Revoked
    }

    public readonly struct ConsentRecord
    {
        public string GameId { get; }
        public ConsentState State { get; }
        public bool IsMinor { get; }
        public bool IsRestrictedRegion { get; }

        private ConsentRecord(string gameId, ConsentState state, bool isMinor, bool isRestrictedRegion)
        {
            GameId = gameId;
            State = state;
            IsMinor = isMinor;
            IsRestrictedRegion = isRestrictedRegion;
        }

        public static ConsentRecord Create(string gameId, ConsentState state, bool isMinor, bool isRestrictedRegion)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("gameId cannot be null or empty", nameof(gameId));
            return new ConsentRecord(gameId, state, isMinor, isRestrictedRegion);
        }

        public bool RequiresParentalGate => IsMinor;
        public bool IsConsentActive => State == ConsentState.Granted;
    }

    public readonly struct ConsentPolicy
    {
        public bool RequireExplicitConsent { get; }
        public bool BlockMinorsWithoutParentalGate { get; }
        public bool BlockRestrictedRegions { get; }
        public string PolicyDescription { get; }

        private ConsentPolicy(bool requireExplicitConsent, bool blockMinorsWithoutParentalGate, bool blockRestrictedRegions, string policyDescription)
        {
            RequireExplicitConsent = requireExplicitConsent;
            BlockMinorsWithoutParentalGate = blockMinorsWithoutParentalGate;
            BlockRestrictedRegions = blockRestrictedRegions;
            PolicyDescription = policyDescription;
        }

        public static ConsentPolicy Safe(string desc)
        {
            if (string.IsNullOrEmpty(desc))
                throw new ArgumentException("PolicyDescription cannot be null or empty", nameof(desc));
            return new ConsentPolicy(true, true, true, desc);
        }

        public static readonly ConsentPolicy Default = Safe("Default consent policy - require explicit consent, block minors and restricted regions");
    }

    public static class ConsentStateMachineGate
    {
        public static bool CanTransitionState(string gameId, ConsentRecord record, ConsentPolicy policy, ConsentState targetState, out string reason)
        {
            if (policy.PolicyDescription == null) policy = ConsentPolicy.Default;

            if (string.IsNullOrEmpty(gameId))
            {
                reason = "GameIdMissing";
                return false;
            }
            if (!GameProfileCatalog.IsKnownId(gameId))
            {
                reason = "UnknownGame";
                return false;
            }

            if (!string.Equals(gameId, record.GameId, StringComparison.Ordinal))
            {
                reason = "GameIdMismatch";
                return false;
            }

            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal))
            {
                reason = "WonderPopConsentProhibited";
                return false;
            }

            if (policy.BlockMinorsWithoutParentalGate && record.IsMinor)
            {
                reason = "MinorRequiresParentalGate";
                return false;
            }

            if (policy.BlockRestrictedRegions && record.IsRestrictedRegion)
            {
                reason = "RestrictedRegionBlocked";
                return false;
            }

            if (policy.RequireExplicitConsent && targetState == ConsentState.Granted && record.State == ConsentState.NotRequested)
            {
                reason = "ExplicitConsentRequired";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        public static bool RequiresParentalGate(ConsentRecord record) => record.RequiresParentalGate;
        public static bool IsConsentActive(ConsentRecord record) => record.IsConsentActive;
    }

    public sealed class ConsentLog
    {
        private readonly List<string> _entries = new List<string>();

        public void Record(string gameId, string transition)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("gameId cannot be null or empty", nameof(gameId));
            if (string.IsNullOrEmpty(transition))
                throw new ArgumentException("transition cannot be null or empty", nameof(transition));
            _entries.Add(gameId + ":" + transition);
        }

        public int Count => _entries.Count;
        public void Clear() => _entries.Clear();
        public bool HasEntry(string gameId) => _entries.Exists(e => e.StartsWith(gameId + ":", StringComparison.Ordinal));
    }
}
