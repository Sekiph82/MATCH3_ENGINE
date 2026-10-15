using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Match3.Engine.Runtime
{
    // UI-only projection from signed metadata. Real unlocks, stars, scores and
    // gameplay rewards remain exclusively in existing progression planners.
    public sealed class RemoteLevelPresentation
    {
        private readonly ReadOnlyCollection<int> _playableNeighborIds;
        public int LevelId { get; }
        public bool Disabled { get; }
        public string ReasonKey { get; }
        public IReadOnlyList<int> PlayableNeighborIds => _playableNeighborIds;
        public bool CanGrantReward => false;
        public bool CanForgeCompletedState => false;

        internal RemoteLevelPresentation(int levelId, bool disabled,
            IEnumerable<int> neighbors)
        {
            LevelId = levelId;
            Disabled = disabled;
            ReasonKey = disabled ? "level.remote_disabled" : null;
            _playableNeighborIds = new ReadOnlyCollection<int>(
                neighbors.ToArray());
        }
    }

    public static class RemoteDisabledLevelPlanner
    {
        private static bool IsDisabled(RemoteVerifiedRelease verified, int levelId) =>
            verified.Manifest.MetaCommands.Any(x =>
                x.Kind == RemoteMetaKind.DisabledLevel &&
                x.NeutralId == "level." + levelId &&
                !x.Enable);

        public static RemoteLevelPresentation Evaluate(
            GameProfile profile, RemoteVerifiedRelease verified,
            int levelId, IEnumerable<int> authoritativeUnlockedNeighbors)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (verified == null) throw new ArgumentNullException(nameof(verified));
            if (profile.Id != verified.Manifest.GameId)
                throw new InvalidOperationException(
                    "Foreign game's disabled-level manifest rejected.");
            if (levelId < 1 || levelId > 1000000)
                throw new ArgumentOutOfRangeException(nameof(levelId));
            if (authoritativeUnlockedNeighbors == null)
                throw new ArgumentNullException(nameof(authoritativeUnlockedNeighbors));
            var neighbors = authoritativeUnlockedNeighbors.Take(65).ToArray();
            if (neighbors.Length > 64 ||
                neighbors.Any(n => n < 1 || n > 1000000 || n == levelId) ||
                neighbors.Distinct().Count() != neighbors.Length)
                throw new ArgumentException(
                    "Untrusted/oversized reachable-neighbor candidates.");
            var disabled = IsDisabled(verified, levelId);
            return new RemoteLevelPresentation(
                levelId, disabled,
                neighbors.Where(n => !IsDisabled(verified, n)));
        }
    }
}
