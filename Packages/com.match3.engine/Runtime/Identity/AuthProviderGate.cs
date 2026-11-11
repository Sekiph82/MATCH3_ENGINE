using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime.Identity
{
    public enum AuthProvider
    {
        None,
        Apple,
        Google
    }

    public readonly struct AuthToken
    {
        public AuthProvider Provider { get; }
        public string TokenValue { get; }
        public string GameId { get; }

        private AuthToken(AuthProvider provider, string tokenValue, string gameId)
        {
            Provider = provider;
            TokenValue = tokenValue;
            GameId = gameId;
        }

        public bool IsValid =>
            Provider != AuthProvider.None &&
            !string.IsNullOrEmpty(TokenValue) &&
            !string.IsNullOrEmpty(GameId);

        public static AuthToken FromPlatform(AuthProvider provider, string tokenValue, string gameId)
        {
            if (provider == AuthProvider.None)
                throw new ArgumentException("Provider must not be None.", nameof(provider));
            if (string.IsNullOrEmpty(tokenValue))
                throw new ArgumentException("TokenValue must not be null or empty.", nameof(tokenValue));
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("GameId must not be null or empty.", nameof(gameId));
            return new AuthToken(provider, tokenValue, gameId);
        }

        public static AuthToken Empty => new AuthToken();
    }

    public readonly struct AuthProviderPolicy
    {
        public bool AllowPlatformLogin { get; }
        public bool ExposeTokenToGameLogic { get; }
        public string PolicyDescription { get; }

        public AuthProviderPolicy(bool allowPlatformLogin, bool exposeTokenToGameLogic, string policyDescription)
        {
            if (string.IsNullOrEmpty(policyDescription))
                throw new ArgumentException("PolicyDescription must not be null or empty.", nameof(policyDescription));
            AllowPlatformLogin = allowPlatformLogin;
            ExposeTokenToGameLogic = exposeTokenToGameLogic;
            PolicyDescription = policyDescription;
        }

        public static AuthProviderPolicy Safe(string desc) =>
            new AuthProviderPolicy(true, false, desc);

        public static AuthProviderPolicy Default =>
            Safe("Platform login permitted; provider tokens must never be exposed to game logic");
    }

    public readonly struct AuthEligibilityResult
    {
        public bool IsEligible { get; }
        public string Reason { get; }

        public AuthEligibilityResult(bool isEligible, string reason)
        {
            IsEligible = isEligible;
            Reason = reason;
        }
    }

    public static class AuthProviderGate
    {
        public static AuthEligibilityResult CanUsePlatformLogin(
            string gameId,
            AuthProvider provider,
            AuthProviderPolicy policy)
        {
            // Fail closed: a missing game context is never eligible
            if (string.IsNullOrEmpty(gameId))
                return new AuthEligibilityResult(false, "GameIdMissing");

            // Guard 1 FIRST (CRITICAL): Wonder_Pop is prohibited
            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal))
                return new AuthEligibilityResult(false, "WonderPopAuthProhibited");

            // Guard 2: platform login disabled by policy
            if (!policy.AllowPlatformLogin)
                return new AuthEligibilityResult(false, "PlatformLoginDisabled");

            // Guard 3: no provider selected
            if (provider == AuthProvider.None)
                return new AuthEligibilityResult(false, "NoProviderSelected");

            return new AuthEligibilityResult(true, "Eligible");
        }

        // ponytail: always false by design — tokens must never be exposed to game logic
        public static bool IsTokenSafeForGameLogic(AuthProviderPolicy policy) => false;
    }

    public sealed class AuthSessionCache
    {
        private readonly Dictionary<string, AuthToken> _store = new Dictionary<string, AuthToken>();

        public void Store(AuthToken token)
        {
            _store[token.GameId] = token;
        }

        public bool TryGet(string gameId, out AuthToken token) =>
            _store.TryGetValue(gameId, out token);

        public void Invalidate(string gameId) =>
            _store.Remove(gameId);

        public void Clear() =>
            _store.Clear();

        public int Count => _store.Count;
    }
}
