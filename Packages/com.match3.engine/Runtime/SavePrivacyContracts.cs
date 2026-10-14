using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Match3.Engine.Runtime
{
    public sealed class SaveEnvelope
    {
        public int SchemaVersion { get; }
        public string Payload { get; }

        public SaveEnvelope(int schemaVersion, string payload)
        {
            if (schemaVersion < 1 || schemaVersion > 100000)
                throw new ArgumentOutOfRangeException(nameof(schemaVersion));
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));
            if (Encoding.UTF8.GetByteCount(payload) > 1024 * 1024)
                throw new ArgumentException(
                    "Save payload exceeds one-megabyte contract limit.",
                    nameof(payload));
            SchemaVersion = schemaVersion;
            Payload = payload;
        }
    }

    public sealed class SaveMigrationStep
    {
        public string StepId { get; }
        public int FromVersion { get; }
        public int ToVersion { get; }
        public Func<string, string> Transform { get; }

        public SaveMigrationStep(string stepId, int fromVersion,
            int toVersion, Func<string, string> transform)
        {
            if (string.IsNullOrWhiteSpace(stepId))
                throw new ArgumentException(
                    "Migration step ID required.", nameof(stepId));
            if (fromVersion < 1 || toVersion != fromVersion + 1)
                throw new ArgumentException(
                    "Migration step must advance exactly one schema version.");
            Transform = transform ??
                throw new ArgumentNullException(nameof(transform));
            StepId = stepId;
            FromVersion = fromVersion;
            ToVersion = toVersion;
        }
    }

    public sealed class SaveMigrationPlan
    {
        private readonly ReadOnlyCollection<string> _appliedStepIds;

        public SaveEnvelope Backup { get; }
        public string BackupSha256 { get; }
        public SaveEnvelope Result { get; }
        public bool Applied { get; }
        public IReadOnlyList<string> AppliedStepIds => _appliedStepIds;

        internal SaveMigrationPlan(SaveEnvelope backup,
            string backupSha256, SaveEnvelope result,
            bool applied, IEnumerable<string> appliedStepIds)
        {
            Backup = backup ??
                throw new ArgumentNullException(nameof(backup));
            Result = result ??
                throw new ArgumentNullException(nameof(result));
            if (string.IsNullOrWhiteSpace(backupSha256))
                throw new ArgumentException(
                    "Backup digest required.", nameof(backupSha256));
            BackupSha256 = backupSha256;
            Applied = applied;
            _appliedStepIds = new ReadOnlyCollection<string>(
                appliedStepIds.ToList());
        }
    }

    public static class SaveMigrationPlanner
    {
        public static SaveMigrationPlan Migrate(
            SaveEnvelope envelope, int targetVersion,
            IEnumerable<SaveMigrationStep> steps)
        {
            if (envelope == null)
                throw new ArgumentNullException(nameof(envelope));
            if (targetVersion < 1 || targetVersion > 100000)
                throw new ArgumentOutOfRangeException(nameof(targetVersion));
            if (envelope.SchemaVersion > targetVersion)
                throw new NotSupportedException(
                    "Save schema is newer than supported target.");
            if (steps == null)
                throw new ArgumentNullException(nameof(steps));

            var stepArray = steps.Take(129).ToArray();
            if (stepArray.Length > 128 ||
                stepArray.Any(x => x == null))
                throw new ArgumentException(
                    "Invalid migration step collection.", nameof(steps));
            if (stepArray.GroupBy(
                    x => x.StepId, StringComparer.Ordinal)
                .Any(x => x.Count() > 1) ||
                stepArray.GroupBy(x => x.FromVersion)
                .Any(x => x.Count() > 1))
                throw new ArgumentException(
                    "Duplicate migration step identity/version.",
                    nameof(steps));

            var backupHash = HashEnvelope(envelope);
            if (envelope.SchemaVersion == targetVersion)
                return new SaveMigrationPlan(
                    envelope, backupHash, envelope,
                    false, new string[0]);

            var byFrom = stepArray.ToDictionary(
                x => x.FromVersion, x => x);
            var version = envelope.SchemaVersion;
            var payload = envelope.Payload;
            var applied = new List<string>();

            while (version < targetVersion)
            {
                SaveMigrationStep step;
                if (!byFrom.TryGetValue(version, out step) ||
                    step.ToVersion > targetVersion)
                    throw new InvalidOperationException(
                        "Missing migration step from schema " + version + ".");

                var first = step.Transform(payload);
                var second = step.Transform(payload);
                if (first == null || second == null)
                    throw new InvalidOperationException(
                        "Migration transform returned null.");
                if (!StringComparer.Ordinal.Equals(first, second))
                    throw new InvalidOperationException(
                        "Migration transform is nondeterministic.");
                if (Encoding.UTF8.GetByteCount(first) > 1024 * 1024)
                    throw new InvalidOperationException(
                        "Migrated save payload exceeds size limit.");

                payload = first;
                version = step.ToVersion;
                applied.Add(step.StepId);
            }

            return new SaveMigrationPlan(
                envelope,
                backupHash,
                new SaveEnvelope(version, payload),
                true,
                applied);
        }

        public static string HashEnvelope(SaveEnvelope envelope)
        {
            if (envelope == null)
                throw new ArgumentNullException(nameof(envelope));
            var canonical =
                envelope.SchemaVersion + "|" + envelope.Payload;
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(
                    Encoding.UTF8.GetBytes(canonical));
                var builder = new StringBuilder(bytes.Length * 2);
                foreach (var b in bytes)
                    builder.Append(b.ToString("x2"));
                return builder.ToString();
            }
        }
    }

    public enum AnalyticsConsent
    {
        Unknown = 0,
        Denied = 1,
        Granted = 2
    }

    public sealed class PrivacyProfilePolicy
    {
        public GameProfile Profile { get; }
        public bool IsChildProfile { get; }
        public bool ChildAnalyticsAllowed { get; }
        public bool ChildSocialAllowed { get; }

        public PrivacyProfilePolicy(GameProfile profile,
            bool isChildProfile,
            bool childAnalyticsAllowed = false,
            bool childSocialAllowed = false)
        {
            Profile = profile ??
                throw new ArgumentNullException(nameof(profile));
            IsChildProfile = isChildProfile;
            ChildAnalyticsAllowed = childAnalyticsAllowed;
            ChildSocialAllowed = childSocialAllowed;
        }
    }

    public sealed class PrivacyConsentReceipt
    {
        public string SourceId { get; }
        public AnalyticsConsent Consent { get; }
        public bool SocialRequested { get; }

        public PrivacyConsentReceipt(string sourceId,
            AnalyticsConsent consent, bool socialRequested)
        {
            if (string.IsNullOrWhiteSpace(sourceId))
                throw new ArgumentException(
                    "Privacy source ID required.", nameof(sourceId));
            if (!Enum.IsDefined(typeof(AnalyticsConsent), consent))
                throw new ArgumentOutOfRangeException(nameof(consent));
            SourceId = sourceId;
            Consent = consent;
            SocialRequested = socialRequested;
        }
    }

    public sealed class PrivacySnapshot
    {
        private readonly ReadOnlyDictionary<string, PrivacyConsentReceipt> _ledger;

        public string GameId { get; }
        public AnalyticsConsent Consent { get; }
        public bool AnalyticsEnabled { get; }
        public bool SocialEnabled { get; }
        public bool LocalPlayAllowed => true;
        public IReadOnlyDictionary<string, PrivacyConsentReceipt> Ledger =>
            _ledger;

        public PrivacySnapshot(string gameId,
            AnalyticsConsent consent = AnalyticsConsent.Unknown,
            bool analyticsEnabled = false,
            bool socialEnabled = false,
            IEnumerable<PrivacyConsentReceipt> ledger = null)
        {
            if (!GameProfileCatalog.IsKnownId(gameId))
                throw new ArgumentException(
                    "Unknown game ID.", nameof(gameId));
            if (!Enum.IsDefined(typeof(AnalyticsConsent), consent))
                throw new ArgumentOutOfRangeException(nameof(consent));
            if (consent != AnalyticsConsent.Granted &&
                (analyticsEnabled || socialEnabled))
                throw new ArgumentException(
                    "Denied/unknown consent cannot enable telemetry or social.");

            var map =
                new Dictionary<string, PrivacyConsentReceipt>(
                    StringComparer.Ordinal);
            foreach (var receipt in (ledger ??
                Enumerable.Empty<PrivacyConsentReceipt>()).Take(100001))
            {
                if (map.Count >= 100000)
                    throw new ArgumentException(
                        "Privacy ledger limit exceeded.", nameof(ledger));
                if (receipt == null ||
                    map.ContainsKey(receipt.SourceId))
                    throw new ArgumentException(
                        "Invalid privacy receipt.", nameof(ledger));
                map.Add(receipt.SourceId, receipt);
            }

            GameId = gameId;
            Consent = consent;
            AnalyticsEnabled = analyticsEnabled;
            SocialEnabled = socialEnabled;
            _ledger =
                new ReadOnlyDictionary<string, PrivacyConsentReceipt>(map);
        }
    }

    public sealed class PrivacyPlan
    {
        public PrivacySnapshot Result { get; }
        public bool Applied { get; }

        internal PrivacyPlan(PrivacySnapshot result, bool applied)
        {
            Result = result;
            Applied = applied;
        }
    }

    public static class PrivacyPlanner
    {
        public static PrivacyPlan ApplyConsent(
            PrivacyProfilePolicy policy,
            PrivacySnapshot snapshot,
            string sourceId,
            AnalyticsConsent consent,
            bool requestSocial)
        {
            if (policy == null)
                throw new ArgumentNullException(nameof(policy));
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            if (!StringComparer.Ordinal.Equals(
                policy.Profile.Id, snapshot.GameId))
                throw new InvalidOperationException(
                    "Privacy policy and save belong to different games.");
            if (string.IsNullOrWhiteSpace(sourceId))
                throw new ArgumentException(
                    "Privacy source ID required.", nameof(sourceId));
            if (!Enum.IsDefined(typeof(AnalyticsConsent), consent))
                throw new ArgumentOutOfRangeException(nameof(consent));

            PrivacyConsentReceipt existing;
            if (snapshot.Ledger.TryGetValue(sourceId, out existing))
            {
                if (existing.Consent != consent ||
                    existing.SocialRequested != requestSocial)
                    throw new InvalidOperationException(
                        "Privacy source ID is bound to another decision.");
                return new PrivacyPlan(snapshot, false);
            }

            var analytics = consent == AnalyticsConsent.Granted &&
                (!policy.IsChildProfile ||
                    policy.ChildAnalyticsAllowed);
            var social = consent == AnalyticsConsent.Granted &&
                requestSocial &&
                (!policy.IsChildProfile ||
                    policy.ChildSocialAllowed);

            var receipt = new PrivacyConsentReceipt(
                sourceId, consent, requestSocial);
            return new PrivacyPlan(
                new PrivacySnapshot(
                    snapshot.GameId,
                    consent,
                    analytics,
                    social,
                    snapshot.Ledger.Values.Concat(
                        new[] { receipt })),
                true);
        }
    }
}
