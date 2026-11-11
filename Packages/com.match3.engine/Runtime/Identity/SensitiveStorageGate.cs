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
        // True only when the value is written through an encrypting store (Keychain/Keystore-backed).
        public bool IsEncrypted { get; }

        private StorageEntry(string gameId, StorageClassification classification, string keyName, bool isEncrypted)
        {
            GameId = gameId;
            Classification = classification;
            KeyName = keyName;
            IsEncrypted = isEncrypted;
        }

        public bool IsSensitive => Classification == StorageClassification.Sensitive;

        // Unencrypted entry (IsEncrypted = false). Sensitive entries created this way are always rejected.
        public static StorageEntry Create(string gameId, StorageClassification classification, string keyName) =>
            Create(gameId, classification, keyName, isEncrypted: false);

        public static StorageEntry Create(string gameId, StorageClassification classification, string keyName, bool isEncrypted)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("GameId must not be null or empty.", nameof(gameId));
            if (string.IsNullOrEmpty(keyName))
                throw new ArgumentException("KeyName must not be null or empty.", nameof(keyName));
            return new StorageEntry(gameId, classification, keyName, isEncrypted);
        }
    }

    public readonly struct SensitiveStoragePolicy
    {
        [Obsolete("Ignored: sensitive entries always require encryption, under every policy.")]
        public bool RequireEncryption => true;
        [Obsolete("Ignored: plaintext sensitive storage is always prohibited, under every policy.")]
        public bool AllowPlaintextSensitive => false;
        public string PolicyDescription { get; }

        private SensitiveStoragePolicy(string policyDescription)
        {
            PolicyDescription = policyDescription;
        }

        public static SensitiveStoragePolicy Safe(string description)
        {
            if (string.IsNullOrEmpty(description))
                throw new ArgumentException("PolicyDescription must not be null or empty.", nameof(description));
            return new SensitiveStoragePolicy(description);
        }

        public static SensitiveStoragePolicy Default =>
            Safe("Encryption required; plaintext sensitive storage prohibited");
    }

    public static class SensitiveStorageGate
    {
        public static (bool eligible, string reason) CanStoreSensitive(
            string gameId, StorageEntry entry, SensitiveStoragePolicy policy)
        {
            if (policy.PolicyDescription == null) policy = SensitiveStoragePolicy.Default;

            if (!string.Equals(gameId, entry.GameId, StringComparison.Ordinal))
                return (false, "GameIdMismatch");

            // Wonder_Pop is structurally ineligible
            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal))
                return (false, "WonderPopStorageProhibited");

            // Unconditional: a sensitive entry is eligible ONLY when encrypted, under every policy
            if (entry.IsSensitive && !entry.IsEncrypted)
                return (false, "PlaintextSensitiveProhibited");

            return (true, "Eligible");
        }

        // Encryption of sensitive entries is unconditional; the policy cannot waive it.
        public static bool RequiresEncryption(SensitiveStoragePolicy policy) => true;

        public static bool IsSecureEntry(StorageEntry entry, SensitiveStoragePolicy policy) =>
            !entry.IsSensitive || entry.IsEncrypted;
    }

    public sealed class StorageAccessLog
    {
        private readonly List<(StorageEntry entry, bool allowed, string reason)> _records =
            new List<(StorageEntry, bool, string)>();

        public int Count => _records.Count;

        public void Record(StorageEntry entry, bool allowed, string reason)
        {
            if (string.IsNullOrEmpty(entry.GameId))
                throw new ArgumentException("Entry GameId must not be null or empty.", nameof(entry));
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
