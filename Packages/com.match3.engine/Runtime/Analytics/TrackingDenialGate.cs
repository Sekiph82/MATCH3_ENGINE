using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime.Analytics
{
    public enum DenialReason
    {
        UserOptOut,
        AttDenied,
        OfflineMode,
        PolicyDefault
    }

    public readonly struct DenialRecord
    {
        public string GameId { get; }
        public DenialReason Reason { get; }
        public bool IsTemporary { get; }

        private DenialRecord(string gameId, DenialReason reason, bool isTemporary)
        {
            GameId = gameId;
            Reason = reason;
            IsTemporary = isTemporary;
        }

        public static DenialRecord Create(string gameId, DenialReason reason, bool isTemporary)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("gameId cannot be null or empty.", nameof(gameId));
            return new DenialRecord(gameId, reason, isTemporary);
        }

        public bool IsNetworkDenied => Reason == DenialReason.AttDenied || Reason == DenialReason.OfflineMode;
        public bool IsPermanentDenial => !IsTemporary;
    }

    public readonly struct DenialPolicy
    {
        public bool BlockOnAnyDenial { get; }
        public bool RequireExplicitUnblock { get; }
        public string PolicyDescription { get; }

        private DenialPolicy(bool blockOnAnyDenial, bool requireExplicitUnblock, string policyDescription)
        {
            BlockOnAnyDenial = blockOnAnyDenial;
            RequireExplicitUnblock = requireExplicitUnblock;
            PolicyDescription = policyDescription;
        }

        public static DenialPolicy Safe(string desc)
        {
            if (string.IsNullOrEmpty(desc))
                throw new ArgumentException("PolicyDescription cannot be null or empty.", nameof(desc));
            return new DenialPolicy(true, true, desc);
        }

        public static readonly DenialPolicy Default = Safe("Default denial policy - block all tracking on any denial");
    }

    public static class TrackingDenialGate
    {
        public static bool CanTrack(string gameId, DenialRecord record, DenialPolicy policy, out string reason)
        {
            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal))
            {
                reason = "WonderPopTrackingProhibited";
                return false;
            }

            if (policy.BlockOnAnyDenial && record.Reason != DenialReason.PolicyDefault)
            {
                reason = "TrackingDeniedByPolicy";
                return false;
            }

            if (record.IsNetworkDenied)
            {
                reason = "NetworkDenied";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        public static bool IsNetworkDenied(DenialRecord record) => record.IsNetworkDenied;
        public static bool RequiresExplicitUnblock(DenialPolicy policy) => policy.RequireExplicitUnblock;
    }

    public sealed class DenialLog
    {
        private readonly List<string> _entries = new List<string>();

        public void Record(string gameId, string denialReason)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("gameId cannot be null or empty.", nameof(gameId));
            _entries.Add(gameId + ":" + denialReason);
        }

        public int Count => _entries.Count;

        public void Clear() => _entries.Clear();

        public bool HasEntry(string gameId) => _entries.Exists(e => e.StartsWith(gameId + ":"));
    }
}
