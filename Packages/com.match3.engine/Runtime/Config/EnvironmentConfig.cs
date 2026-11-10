// ENG-M13-008 | Config secrets and environments
// Design principle: private server signing keys NEVER reside in Unity builds.
// Only public client IDs (safe to bundle) are exposed here.
// All secrets (signing keys, Worker secrets, D1 tokens) live exclusively in
// Cloudflare Worker environment variables and are never committed to source.
using System;

namespace Match3.Engine.Runtime.Config
{
    /// <summary>Deployment environment for config and API boundary selection.</summary>
    public enum Environment
    {
        Dev     = 0,
        Staging = 1,
        Prod    = 2
    }

    /// <summary>
    /// Read-only config boundary exposed to Unity client builds.
    /// Only public client IDs and base URLs belong here — no server signing keys.
    /// </summary>
    public interface IEnvironmentConfig
    {
        /// <summary>Active deployment environment.</summary>
        Environment Environment { get; }

        /// <summary>
        /// Public client API key scoped to <paramref name="gameId"/>.
        /// Safe to bundle — no signing authority, rate-limited, read-only catalog access.
        /// </summary>
        string GetPublicApiKey(string gameId);

        /// <summary>
        /// Base URL for Cloudflare Worker endpoints (no trailing slash).
        /// Example: https://match3-dev.workers.dev
        /// </summary>
        string GetCloudflareWorkerBaseUrl();

        /// <summary>Returns true only when Environment == Prod.</summary>
        bool IsProduction { get; }
    }

    /// <summary>
    /// Development stub — returns placeholder strings that contain no real credentials.
    /// Use in Editor, local tests, and CI where no real backend is reachable.
    /// </summary>
    public sealed class NullEnvironmentConfig : IEnvironmentConfig
    {
        public Environment Environment => Environment.Dev;

        public string GetPublicApiKey(string gameId) =>
            $"dev-public-key-{gameId ?? "unknown"}";

        public string GetCloudflareWorkerBaseUrl() =>
            "https://localhost:8787";

        public bool IsProduction => false;
    }

    /// <summary>
    /// Validates that an IEnvironmentConfig value does not contain embedded server secrets.
    /// Call this at startup to catch accidental secret embedding during development.
    ///
    /// Patterns rejected:
    ///   - "sk-" prefix (OpenAI / generic signing key convention)
    ///   - Bare hex strings longer than 32 characters (looks like a real API UUID/token)
    ///
    /// Private server signing keys must NEVER appear in a Unity build; they belong
    /// exclusively in Cloudflare Worker environment variables.
    /// </summary>
    public static class EnvironmentConfigValidator
    {
        // ponytail: simple regex-free scan; upgrade to Regex if pattern list grows.
        private static readonly string[] _secretPrefixes = { "sk-" };
        private const int MaxSafeHexLength = 32;

        /// <summary>
        /// Validates all string fields returned by <paramref name="config"/>.
        /// Throws <see cref="InvalidOperationException"/> on the first detected secret pattern.
        /// </summary>
        public static void Validate(IEnvironmentConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            Check(config.GetPublicApiKey("*validate*"), nameof(IEnvironmentConfig.GetPublicApiKey));
            Check(config.GetCloudflareWorkerBaseUrl(), nameof(IEnvironmentConfig.GetCloudflareWorkerBaseUrl));
        }

        private static void Check(string value, string fieldName)
        {
            if (string.IsNullOrEmpty(value)) return;

            foreach (var prefix in _secretPrefixes)
            {
                if (value.Contains(prefix))
                    throw new InvalidOperationException(
                        $"[ENG-M13-008] Secret pattern '{prefix}' detected in {fieldName}. " +
                        "Server signing keys must not be embedded in Unity builds.");
            }

            if (IsLikelySecretToken(value))
                throw new InvalidOperationException(
                    $"[ENG-M13-008] Value in {fieldName} resembles a raw API token " +
                    $"(bare hex/alphanumeric run > {MaxSafeHexLength} chars). " +
                    "Public client IDs should be short and namespaced.");
        }

        // Returns true when the value contains a contiguous hex/alphanumeric run
        // longer than MaxSafeHexLength (heuristic for embedded UUID/token).
        private static bool IsLikelySecretToken(string value)
        {
            int run = 0;
            foreach (char c in value)
            {
                if ((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))
                    run++;
                else
                    run = 0;

                if (run > MaxSafeHexLength) return true;
            }
            return false;
        }
    }
}
