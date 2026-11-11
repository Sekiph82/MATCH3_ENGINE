using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime.Privacy
{
    public enum PrivacyTestProfile
    {
        ChildProfile,
        AdDisabled,
        ConsentDenied,
        ConsentGranted,
        GdprSubject,
        StandardUser
    }

    public readonly struct MatrixTestRecord
    {
        public string GameId { get; }
        public PrivacyTestProfile Profile { get; }
        public bool HasUnexpectedTransfer { get; }
        public bool IsNetworkCallInstrumented { get; }

        private MatrixTestRecord(string gameId, PrivacyTestProfile profile, bool hasUnexpectedTransfer, bool isNetworkCallInstrumented)
        {
            GameId = gameId;
            Profile = profile;
            HasUnexpectedTransfer = hasUnexpectedTransfer;
            IsNetworkCallInstrumented = isNetworkCallInstrumented;
        }

        public static MatrixTestRecord Create(string gameId, PrivacyTestProfile profile, bool hasUnexpectedTransfer, bool isNetworkCallInstrumented)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("gameId cannot be null or empty", nameof(gameId));
            return new MatrixTestRecord(gameId, profile, hasUnexpectedTransfer, isNetworkCallInstrumented);
        }

        public bool IsRestrictedProfile =>
            Profile == PrivacyTestProfile.ChildProfile ||
            Profile == PrivacyTestProfile.AdDisabled ||
            Profile == PrivacyTestProfile.ConsentDenied;

        public bool RequiresInstrumentation => !IsNetworkCallInstrumented;
    }

    public readonly struct PrivacyTestMatrixPolicy
    {
        public bool BlockUnexpectedTransfers { get; }
        public bool RequireInstrumentation { get; }
        public string PolicyDescription { get; }

        private PrivacyTestMatrixPolicy(bool blockUnexpectedTransfers, bool requireInstrumentation, string policyDescription)
        {
            BlockUnexpectedTransfers = blockUnexpectedTransfers;
            RequireInstrumentation = requireInstrumentation;
            PolicyDescription = policyDescription;
        }

        public static PrivacyTestMatrixPolicy Safe(string desc)
        {
            if (string.IsNullOrEmpty(desc))
                throw new ArgumentException("PolicyDescription cannot be null or empty", nameof(desc));
            return new PrivacyTestMatrixPolicy(true, true, desc);
        }

        public static readonly PrivacyTestMatrixPolicy Default =
            Safe("Default privacy test matrix policy - block unexpected transfers, require instrumentation");
    }

    public static class PrivacyTestMatrixGate
    {
        public static bool CanPassTestMatrix(string gameId, MatrixTestRecord record, PrivacyTestMatrixPolicy policy, out string reason)
        {
            if (policy.PolicyDescription == null) policy = PrivacyTestMatrixPolicy.Default;

            if (string.IsNullOrEmpty(gameId))
            {
                reason = "GameIdMissing";
                return false;
            }

            if (!string.Equals(gameId, record.GameId, StringComparison.Ordinal))
            {
                reason = "GameIdMismatch";
                return false;
            }

            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal) && record.IsRestrictedProfile && record.HasUnexpectedTransfer)
            {
                reason = "WonderPopUnexpectedTransferBlocked";
                return false;
            }

            if (policy.BlockUnexpectedTransfers && record.HasUnexpectedTransfer)
            {
                reason = "UnexpectedTransferDetected";
                return false;
            }

            if (policy.RequireInstrumentation && record.RequiresInstrumentation)
            {
                reason = "NetworkInstrumentationRequired";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        public static bool IsRestrictedProfile(MatrixTestRecord record) => record.IsRestrictedProfile;

        public static bool RequiresInstrumentation(MatrixTestRecord record) => record.RequiresInstrumentation;
    }

    public sealed class PrivacyTestMatrixLog
    {
        private readonly List<string> _entries = new List<string>();

        public void Record(string gameId, string profileName)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("gameId cannot be null or empty", nameof(gameId));
            if (string.IsNullOrEmpty(profileName))
                throw new ArgumentException("profileName cannot be null or empty", nameof(profileName));
            _entries.Add(gameId + ":" + profileName);
        }

        public int Count => _entries.Count;

        public void Clear() => _entries.Clear();

        public bool HasEntry(string gameId) => _entries.Exists(e => e.StartsWith(gameId + ":", StringComparison.Ordinal));
    }
}
