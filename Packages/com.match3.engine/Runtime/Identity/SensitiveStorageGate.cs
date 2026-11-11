using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime.Identity
{
    public enum StorageClassification
    {
        Public,
        Internal,
        Sensitive
    }

    public readonly struct StorageEntry
    {
        public string GameId { get; }
        public StorageClassification Classification { get; }
        public string KeyName { get; }

        private StorageEntry(string gameId, StorageClassification classification, string keyName)
        {
            GameId = gameId;
            Classification = classification;
            KeyName = keyName;
        }

        public bool IsSensitive => Classification == StorageClassification.Sensitive;

        public static StorageEntry Create(string gameId, StorageClassification classification, string keyName)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("GameId must not be null or empty.", nameof(gameId));
            if (string.IsNullOrEmpty(keyName))
                throw new ArgumentException("KeyName must not be null or empty.", nameof(keyName));
            return new StorageEntry(gameId, classification, keyName);
        }
    }

    public readonly struct SensitiveStoragePolicy
    {
        public bool RequireEncryption { get; }
        public bool AllowPlaintextSensitive { get; }
        public string PolicyDescription { get; }

        private SensitiveStoragePolicy(bool requireEncryption, bool allowPlaintextSensitive, string policyDescription)
        {
            RequireEncryption = requireEncryption;
            AllowPlaintextSensitive = allowPlaintextSensitive;
            PolicyDescription = policyDescription;
        }

        public static SensitiveStoragePolicy Safe(string description)
        {
            if (string.IsNullOrEmpty(description))
                throw new ArgumentException("PolicyDescription must not be null or empty.", nameof(description));
            return new SensitiveStoragePolicy(true, false, description);
        }

        public static SensitiveStoragePolicy Default =>
            Safe("Encryption required; plaintext sensitive storage prohibited");
    }

    public static class SensitiveStorageGate
    {
        public static (bool eligible, string reason) CanStoreSensitive(
            string gameId, StorageEntry entry, SensitiveStoragePolicy policy)
        {
            // Guard 1 (FIRST): Wonder_Pop is structurally ineligible
            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal))
                return (false, "WonderPopStorageProhibited");

            // Guard 2: plaintext sensitive prohibited
            if (entry.IsSensitive && policy.AllowPlaintextSensitive)
                return (false, "PlaintextSensitiveProhibited");

            return (true, "Eligible");
        }

        public static bool RequiresEncryption(SensitiveStoragePolicy policy) =>
            policy.RequireEncryption;

        public static bool IsSecureEntry(StorageEntry entry, SensitiveStoragePolicy policy) =>
            !entry.IsSensitive || policy.RequireEncryption;
    }

    public sealed class StorageAccessLog
    {
        private readonly List<(StorageEntry entry, bool allowed, string reason)> _records =
            new List<(StorageEntry, bool, string)>();

        public int Count => _records.Count;

        public void Record(StorageEntry entry, bool allowed, string reason)
        {
            if (string.IsNullOrEmpty(reason))
                throw new ArgumentException("Reason must not be null or empty.", nameof(reason));
            _records.Add((entry, allowed, reason));
        }

        public void Clear() => _records.Clear();

        public bool HasEntry(string gameId)
        {
            foreach (var r in _records)
                if (string.Equals(r.entry.GameId, gameId, StringComparison.Ordinal))
                    return true;
            return false;
        }
    }
}
