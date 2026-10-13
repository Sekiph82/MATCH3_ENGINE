using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Match3.Engine.Runtime
{
    // Shared profile/feature policy is engine-independent. It must not know
    // Unity UI, game brands, SDK board internals or product artwork.
    public readonly struct EngineVersion : IEquatable<EngineVersion>
    {
        public int Major { get; }
        public int Minor { get; }
        public int Patch { get; }

        public EngineVersion(int major, int minor, int patch)
        {
            if (major < 0 || minor < 0 || patch < 0)
                throw new ArgumentOutOfRangeException(nameof(major), "Version segments cannot be negative.");
            Major = major;
            Minor = minor;
            Patch = patch;
        }

        public bool IsCompatibleWith(EngineVersion required)
        {
            return Major == required.Major && (Minor > required.Minor ||
                (Minor == required.Minor && Patch >= required.Patch));
        }

        public bool Equals(EngineVersion other)
        {
            return Major == other.Major && Minor == other.Minor && Patch == other.Patch;
        }

        public override bool Equals(object value) => value is EngineVersion other && Equals(other);
        public override int GetHashCode()
        {
            unchecked { return ((Major * 397) ^ Minor) * 397 ^ Patch; }
        }
        public override string ToString() => Major + "." + Minor + "." + Patch;
    }

    public static class EngineCompatibility
    {
        public const int ProfileSchemaVersion = 1;
        public static readonly EngineVersion Api = new EngineVersion(1, 0, 0);
        public static readonly EngineVersion MinimumCapability = new EngineVersion(1, 0, 0);

        public static void RequireSupportedSchema(int schema)
        {
            if (schema != ProfileSchemaVersion)
                throw new NotSupportedException("Unsupported game profile schema: " + schema);
        }

        public static void RequireCapabilityVersion(EngineVersion version)
        {
            if (version.Major != MinimumCapability.Major || version.Minor > 0)
                throw new NotSupportedException("Unsupported feature capability version: " + version);
        }
    }

    public static class FeatureCatalog
    {
        private static readonly ReadOnlyCollection<string> AllIds = Array.AsReadOnly(new[]
        {
            "timed", "endless", "collection", "match_2x2",
            "targeted_missile", "conveyor", "portal", "countdown_bomb",
            "egg", "daily", "weekly", "catch_up", "achievements"
        });
        public static IReadOnlyList<string> SupportedIds => AllIds;

        public static bool IsKnown(string id)
        {
            return !string.IsNullOrEmpty(id) && AllIds.Contains(id);
        }
    }

    public readonly struct FeatureFlag : IEquatable<FeatureFlag>
    {
        public string Id { get; }
        public EngineVersion Version { get; }

        public FeatureFlag(string id, EngineVersion version)
        {
            if (!FeatureCatalog.IsKnown(id))
                throw new ArgumentException("Unknown feature capability.", nameof(id));
            EngineCompatibility.RequireCapabilityVersion(version);
            Id = id;
            Version = version;
        }
        public bool Equals(FeatureFlag other) => Id == other.Id && Version.Equals(other.Version);
        public override bool Equals(object value) => value is FeatureFlag other && Equals(other);
        public override int GetHashCode() => (Id ?? string.Empty).GetHashCode() ^ Version.GetHashCode();
    }

    public enum ProfileAgeRule { General = 0, Teen = 1, Mature = 2 }
    public enum ProfileContentChannel { Development = 0, FamilyTest = 1, Release = 2 }

    public sealed class GameProfile
    {
        public string Id { get; }
        public string ThemeBindingKey { get; }
        public ProfileAgeRule AgeRule { get; }
        public ProfileContentChannel ContentChannel { get; }
        public int SchemaVersion { get; }
        public IReadOnlyList<FeatureFlag> EnabledFeatures { get; }

        public GameProfile(string id, string themeBindingKey,
            ProfileAgeRule ageRule, ProfileContentChannel channel,
            IEnumerable<FeatureFlag> enabled = null,
            int schemaVersion = EngineCompatibility.ProfileSchemaVersion)
        {
            EngineCompatibility.RequireSupportedSchema(schemaVersion);
            if (!GameProfileCatalog.IsKnownId(id))
                throw new ArgumentException("Unknown game identifier.", nameof(id));
            if (string.IsNullOrWhiteSpace(themeBindingKey))
                throw new ArgumentException("A neutral theme binding key is required.", nameof(themeBindingKey));
            if (!Enum.IsDefined(typeof(ProfileAgeRule), ageRule))
                throw new ArgumentOutOfRangeException(nameof(ageRule));
            if (!Enum.IsDefined(typeof(ProfileContentChannel), channel))
                throw new ArgumentOutOfRangeException(nameof(channel));

            var flags = enabled == null ? Array.Empty<FeatureFlag>() : enabled.ToArray();
            if (flags.Any(x => !FeatureCatalog.IsKnown(x.Id)))
                throw new ArgumentException("Invalid enabled feature.", nameof(enabled));
            if (flags.GroupBy(x => x.Id, StringComparer.Ordinal).Any(g => g.Count() > 1))
                throw new ArgumentException("Duplicate enabled feature.", nameof(enabled));
            if (flags.Any(x => x.Id == "timed") && flags.Any(x => x.Id == "endless"))
                throw new ArgumentException("Timed and endless are mutually exclusive.", nameof(enabled));

            Id = id;
            ThemeBindingKey = themeBindingKey;
            AgeRule = ageRule;
            ContentChannel = channel;
            SchemaVersion = schemaVersion;
            EnabledFeatures = Array.AsReadOnly(flags);
        }

        public bool IsEnabled(string capabilityId, EngineVersion requiredVersion)
        {
            if (!FeatureCatalog.IsKnown(capabilityId))
                throw new ArgumentException("Unknown capability ID.", nameof(capabilityId));
            EngineCompatibility.RequireCapabilityVersion(requiredVersion);
            return EnabledFeatures.Any(c => c.Id == capabilityId && c.Version.IsCompatibleWith(requiredVersion));
        }
    }

    public static class GameProfileCatalog
    {
        private static readonly ReadOnlyCollection<string> KnownIds = Array.AsReadOnly(new[]
        {
            "Coral_Rescue", "Garage_Legends", "Dino_Islands",
            "The_Fractured_Realms", "Wonder_Pop"
        });

        public static IReadOnlyList<string> Ids => KnownIds;
        public static bool IsKnownId(string id) => !string.IsNullOrEmpty(id) && KnownIds.Contains(id);

        // In M01 all optional gameplay/monetization features default OFF until
        // an owner-approved game configuration explicitly enables them.
        public static IReadOnlyList<GameProfile> LoadPlaceholders()
        {
            var profiles = KnownIds.Select(id => new GameProfile(
                id, "placeholder." + id.ToLowerInvariant(), ProfileAgeRule.General,
                ProfileContentChannel.Development)).ToArray();
            if (profiles.Length != 5 || profiles.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != 5)
                throw new InvalidOperationException("Five distinct placeholder profiles are required.");
            return Array.AsReadOnly(profiles);
        }

        public static GameProfile RequireProfile(IReadOnlyList<GameProfile> profiles, string id)
        {
            if (profiles == null) throw new ArgumentNullException(nameof(profiles));
            if (!IsKnownId(id)) throw new ArgumentException("Unknown game profile.", nameof(id));
            var matches = profiles.Where(x => x != null && x.Id == id).ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException("Missing or duplicate game profile: " + id);
            return matches[0];
        }
    }
}
