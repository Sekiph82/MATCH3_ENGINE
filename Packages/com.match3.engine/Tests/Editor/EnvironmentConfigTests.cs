// ENG-M13-008 | Config secrets and environments — EditMode tests
using System;
using Match3.Engine.Runtime.Config;
using NUnit.Framework;

namespace Match3.Engine.Tests.Editor
{
    public sealed class EnvironmentConfigTests
    {
        // ── 1. NullConfig returns Dev environment ────────────────────────────
        [Test]
        public void NullConfig_ReturnsDevEnvironment()
        {
            var cfg = new NullEnvironmentConfig();
            Assert.AreEqual(Runtime.Config.Environment.Dev, cfg.Environment);
            Assert.IsFalse(cfg.IsProduction);
        }

        // ── 2. NullConfig placeholder values do NOT trigger the validator ────
        [Test]
        public void NullConfig_PlaceholderValues_PassValidator()
        {
            var cfg = new NullEnvironmentConfig();
            Assert.DoesNotThrow(() => EnvironmentConfigValidator.Validate(cfg),
                "NullEnvironmentConfig must not contain secret patterns.");
        }

        // ── 3. Validator rejects a string with "sk-" prefix ──────────────────
        [Test]
        public void Validator_RejectsSkPrefix_InPublicApiKey()
        {
            var cfg = new SecretKeyStub("sk-live-supersecret-key", null);
            var ex = Assert.Throws<InvalidOperationException>(
                () => EnvironmentConfigValidator.Validate(cfg));
            StringAssert.Contains("sk-", ex.Message);
        }

        // ── 4. Validator rejects an embedded UUID-style hex string >32 chars ─
        [Test]
        public void Validator_RejectsLongHexToken_InPublicApiKey()
        {
            // 33 consecutive hex chars — looks like an embedded API token
            var cfg = new SecretKeyStub("aabbccddeeff00112233445566778899a", null);
            var ex = Assert.Throws<InvalidOperationException>(
                () => EnvironmentConfigValidator.Validate(cfg));
            StringAssert.Contains("token", ex.Message);
        }

        // ── 5. Validator rejects "sk-" in base URL position ──────────────────
        [Test]
        public void Validator_RejectsSkPrefix_InBaseUrl()
        {
            var cfg = new SecretKeyStub(null, "https://workers.dev?auth=sk-secret");
            var ex = Assert.Throws<InvalidOperationException>(
                () => EnvironmentConfigValidator.Validate(cfg));
            StringAssert.Contains("sk-", ex.Message);
        }

        // ── 6. Validator accepts a normal short public key ────────────────────
        [Test]
        public void Validator_AcceptsShortPublicClientId()
        {
            // A realistic short public client ID: not hex-only, well under 32 chars
            var cfg = new SecretKeyStub("pub-wonderpop-dev-01", "https://localhost:8787");
            Assert.DoesNotThrow(() => EnvironmentConfigValidator.Validate(cfg));
        }

        // ── minimal in-line stub ─────────────────────────────────────────────
        private sealed class SecretKeyStub : IEnvironmentConfig
        {
            private readonly string _key;
            private readonly string _url;

            public SecretKeyStub(string key, string url)
            {
                _key = key ?? "placeholder";
                _url  = url  ?? "https://localhost:8787";
            }

            public Runtime.Config.Environment Environment => Runtime.Config.Environment.Dev;
            public bool IsProduction => false;
            public string GetPublicApiKey(string gameId) => _key;
            public string GetCloudflareWorkerBaseUrl() => _url;
        }
    }
}
