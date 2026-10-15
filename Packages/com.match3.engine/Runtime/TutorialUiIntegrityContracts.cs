using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Match3.Engine.Runtime
{
    public sealed class NeutralTouchRegion
    {
        public string ActionId { get; }
        public double X { get; }
        public double Y { get; }
        public double Width { get; }
        public double Height { get; }
        public string SpokenLabelKey { get; }

        public NeutralTouchRegion(string actionId, double x, double y,
            double width, double height, string spokenLabelKey,
            double minimumLogicalSize = 44)
        {
            if (string.IsNullOrWhiteSpace(actionId) ||
                string.IsNullOrWhiteSpace(spokenLabelKey))
                throw new ArgumentException("Touch target must carry action and spoken label.");
            if (new[] { x, y, width, height, minimumLogicalSize }.Any(v =>
                double.IsNaN(v) || double.IsInfinity(v)) ||
                minimumLogicalSize < 44 || minimumLogicalSize > 128 ||
                x < 0 || y < 0 ||
                width < minimumLogicalSize || height < minimumLogicalSize)
                throw new ArgumentOutOfRangeException(
                    nameof(width), "Touch hitboxes must be finite and 44pt minimum.");
            ActionId = actionId;
            X = x;
            Y = y;
            Width = width;
            Height = height;
            SpokenLabelKey = spokenLabelKey;
        }

        public bool Contains(double logicalX, double logicalY)
        {
            return !double.IsNaN(logicalX) && !double.IsNaN(logicalY) &&
                !double.IsInfinity(logicalX) && !double.IsInfinity(logicalY) &&
                logicalX >= X && logicalX < X + Width &&
                logicalY >= Y && logicalY < Y + Height;
        }
    }

    public static class NeutralTouchRouter
    {
        public static bool CanActivate(NeutralTouchRegion region,
            double logicalX, double logicalY,
            bool modalBlocking, bool tutorialBlocking)
        {
            if (region == null) throw new ArgumentNullException(nameof(region));
            return !modalBlocking && !tutorialBlocking &&
                region.Contains(logicalX, logicalY);
        }
    }

    // Static route-graph audit does not render or invent visual buttons.
    public sealed class UiGraphAuditReport
    {
        private readonly ReadOnlyCollection<UiSurfaceKind> _reachable;
        public IReadOnlyList<UiSurfaceKind> Reachable => _reachable;
        public int EnabledActionCount { get; }
        public int DisabledActionCount { get; }
        internal UiGraphAuditReport(IEnumerable<UiSurfaceKind> reachable,
            int enabled, int disabled)
        {
            _reachable = new ReadOnlyCollection<UiSurfaceKind>(
                reachable.OrderBy(x => (int)x).ToList());
            EnabledActionCount = enabled;
            DisabledActionCount = disabled;
        }
    }

    public static class UiGraphAuditor
    {
        public static UiGraphAuditReport Validate(
            IEnumerable<UiSurfaceState> states,
            IEnumerable<string> registeredCommands,
            bool backHandlerRegistered,
            bool closeHandlerRegistered)
        {
            if (states == null) throw new ArgumentNullException(nameof(states));
            if (registeredCommands == null)
                throw new ArgumentNullException(nameof(registeredCommands));
            var array = states.Take(18).ToArray();
            if (array.Length != 17 || array.Any(s => s == null) ||
                array.Select(s => s.Surface).Distinct().Count() != 17)
                throw new ArgumentException(
                    "Exactly one neutral UI state per all 17 surfaces required.");
            var commands = registeredCommands.Take(129).ToArray();
            if (commands.Length > 128 ||
                commands.Any(string.IsNullOrWhiteSpace) ||
                commands.Distinct(StringComparer.Ordinal).Count() != commands.Length)
                throw new ArgumentException("Invalid command handler registry.");
            var known = array.ToDictionary(s => s.Surface);
            var enabled = 0;
            var disabled = 0;
            foreach (var state in array)
            foreach (var action in state.Actions.Values)
            {
                if (!action.Visible) continue;
                if (!action.Enabled)
                {
                    disabled++;
                    if (string.IsNullOrWhiteSpace(action.DisabledReason))
                        throw new InvalidOperationException(
                            "Visible disabled action has no accessible reason.");
                    continue;
                }
                enabled++;
                switch (action.Kind)
                {
                    case UiActionKind.Navigate:
                        if (!action.Destination.HasValue ||
                            !known.ContainsKey(action.Destination.Value))
                            throw new InvalidOperationException(
                                "Visible navigation route has no target state.");
                        break;
                    case UiActionKind.Command:
                        if (!commands.Contains(action.CommandKey,
                            StringComparer.Ordinal))
                            throw new InvalidOperationException(
                                "Visible command has no registered handler.");
                        break;
                    case UiActionKind.Back:
                        if (!backHandlerRegistered)
                            throw new InvalidOperationException(
                                "Visible Back has no handler.");
                        break;
                    case UiActionKind.Close:
                        if (!closeHandlerRegistered)
                            throw new InvalidOperationException(
                                "Visible Close has no handler.");
                        break;
                    default:
                        throw new InvalidOperationException("Unknown UI action type.");
                }
            }
            var visited = new HashSet<UiSurfaceKind> { UiSurfaceKind.Home };
            var frontier = new Queue<UiSurfaceKind>();
            frontier.Enqueue(UiSurfaceKind.Home);
            while (frontier.Count > 0)
            {
                var state = known[frontier.Dequeue()];
                foreach (var target in state.Actions.Values.Where(a =>
                    a.Visible && a.Enabled &&
                    a.Kind == UiActionKind.Navigate))
                {
                    if (visited.Add(target.Destination.Value))
                        frontier.Enqueue(target.Destination.Value);
                }
            }
            if (visited.Count != 17)
                throw new InvalidOperationException(
                    "An enabled neutral screen has no route from Home.");
            return new UiGraphAuditReport(visited, enabled, disabled);
        }
    }

    public enum RewardPanelState
    {
        Locked = 0, Available = 1, Claimed = 2, Expired = 3
    }

    public sealed class RewardPresentationPlan
    {
        public RewardPanelState State { get; }
        public bool CanClaim => State == RewardPanelState.Available;
        public bool ShowCountdown { get; }
        public long RemainingSeconds { get; }
        internal RewardPresentationPlan(RewardPanelState state,
            bool showCountdown, long remainingSeconds)
        {
            State = state;
            ShowCountdown = showCountdown;
            RemainingSeconds = remainingSeconds;
        }
    }

    public static class HonestRewardProjection
    {
        // authoritativeEarned/Claimed MUST originate from the existing
        // transaction ledger, not animation callbacks or image metadata.
        // No synthetic urgency from unverified offers/expiry timestamps.
        public static RewardPresentationPlan Project(
            bool authoritativeEarned,
            bool authoritativeClaimed,
            long nowUnixSeconds,
            long? verifiedExpiryUnixSeconds = null)
        {
            if (nowUnixSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(nowUnixSeconds));
            if (authoritativeClaimed && !authoritativeEarned)
                throw new InvalidOperationException(
                    "An unearned reward cannot appear claimed.");
            if (authoritativeClaimed)
                return new RewardPresentationPlan(
                    RewardPanelState.Claimed, false, 0);
            if (!authoritativeEarned)
                return new RewardPresentationPlan(
                    RewardPanelState.Locked, false, 0);
            if (!verifiedExpiryUnixSeconds.HasValue)
                return new RewardPresentationPlan(
                    RewardPanelState.Available, false, 0);
            if (verifiedExpiryUnixSeconds.Value < 0)
                throw new ArgumentOutOfRangeException(nameof(verifiedExpiryUnixSeconds));
            var left = verifiedExpiryUnixSeconds.Value - nowUnixSeconds;
            if (left <= 0)
                return new RewardPresentationPlan(
                    RewardPanelState.Expired, false, 0);
            return new RewardPresentationPlan(
                RewardPanelState.Available, true, left);
        }
    }

    public sealed class NeutralHudReadability
    {
        public TextFitReport LabelFit { get; }
        public bool ReducedMotion { get; }
        public bool AudioMuted { get; }
        public int GoalRemaining { get; }
        public int MovesRemaining { get; }

        public NeutralHudReadability(string spokenGoalLabel,
            int goalRemaining, int movesRemaining,
            int textWidthCharacters, int maxLines,
            bool reducedMotion, bool audioMuted)
        {
            if (string.IsNullOrWhiteSpace(spokenGoalLabel))
                throw new ArgumentException(
                    "Readable goal label required.", nameof(spokenGoalLabel));
            if (goalRemaining < 0 || movesRemaining < 0)
                throw new ArgumentOutOfRangeException(nameof(goalRemaining));
            LabelFit = NeutralTextFit.Check(
                spokenGoalLabel, textWidthCharacters, maxLines);
            if (!LabelFit.Fits)
                throw new InvalidOperationException(
                    "Goal text would exceed neutral layout bounds.");
            GoalRemaining = goalRemaining;
            MovesRemaining = movesRemaining;
            ReducedMotion = reducedMotion;
            AudioMuted = audioMuted;
        }
    }
}
