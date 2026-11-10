using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime.Commerce
{
    public enum AdFormatKind
    {
        Rewarded,
        Interstitial,
        Banner
    }

    public readonly struct AdFormatConfig
    {
        public AdFormatKind Kind { get; }
        public bool IsEnabled { get; }
        public string GameId { get; }

        public AdFormatConfig(AdFormatKind kind, bool isEnabled, string gameId)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("GameId must not be null or empty.", nameof(gameId));

            Kind = kind;
            // Wonder Pop is always disabled regardless of requested state
            IsEnabled = string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal) ? false : isEnabled;
            GameId = gameId;
        }

        public static AdFormatConfig Disabled(AdFormatKind kind, string gameId) =>
            new AdFormatConfig(kind, false, gameId);

        public static AdFormatConfig Enabled(AdFormatKind kind, string gameId) =>
            new AdFormatConfig(kind, true, gameId);
    }

    public sealed class AdFormatRegistry
    {
        private readonly Dictionary<(string, AdFormatKind), AdFormatConfig> _configs =
            new Dictionary<(string, AdFormatKind), AdFormatConfig>();

        public void Register(AdFormatConfig config)
        {
            if (string.IsNullOrEmpty(config.GameId))
                throw new ArgumentException("config.GameId must not be null or empty.", nameof(config));
            _configs[(config.GameId, config.Kind)] = config;
        }

        public AdFormatConfig? GetConfig(string gameId, AdFormatKind kind)
        {
            if (_configs.TryGetValue((gameId, kind), out var cfg))
                return cfg;
            return null;
        }

        public bool IsEnabled(string gameId, AdFormatKind kind)
        {
            var cfg = GetConfig(gameId, kind);
            return cfg.HasValue && cfg.Value.IsEnabled;
        }
    }

    public sealed class AdNaturalBreakPolicy
    {
        private readonly HashSet<string> _allowed;

        public AdNaturalBreakPolicy(IReadOnlyList<string> allowedBreakPointIds)
        {
            if (allowedBreakPointIds == null)
                throw new ArgumentNullException(nameof(allowedBreakPointIds));
            _allowed = new HashSet<string>(allowedBreakPointIds, StringComparer.Ordinal);
        }

        public bool IsNaturalBreak(string breakPointId) =>
            !string.IsNullOrEmpty(breakPointId) && _allowed.Contains(breakPointId);

        public static AdNaturalBreakPolicy AllBreaksBlocked { get; } =
            new AdNaturalBreakPolicy(new string[0]);
    }
}
