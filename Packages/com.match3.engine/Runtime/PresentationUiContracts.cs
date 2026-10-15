using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Match3.Engine.Runtime
{
    public enum UiSurfaceKind
    {
        Home = 0,
        WorldMap = 1,
        Hud = 2,
        Results = 3,
        Pause = 4,
        Settings = 5,
        Shop = 6,
        DailyRewards = 7,
        Tasks = 8,
        Fail = 9,
        Life = 10,
        Booster = 11,
        Collection = 12,
        Achievements = 13,
        Store = 14,
        Account = 15,
        Consent = 16
    }

    public enum UiActionKind
    {
        Navigate = 0,
        Command = 1,
        Back = 2,
        Close = 3
    }

    public sealed class UiActionDefinition
    {
        public string ActionId { get; }
        public UiSurfaceKind Surface { get; }
        public UiActionKind Kind { get; }
        public bool Visible { get; }
        public bool Enabled { get; }
        public string DisabledReason { get; }
        public UiSurfaceKind? Destination { get; }
        public string CommandKey { get; }

        public UiActionDefinition(string actionId, UiSurfaceKind surface,
            UiActionKind kind, bool visible, bool enabled,
            UiSurfaceKind? destination = null, string commandKey = null,
            string disabledReason = null)
        {
            if (string.IsNullOrWhiteSpace(actionId))
                throw new ArgumentException("UI action ID required.", nameof(actionId));
            if (!Enum.IsDefined(typeof(UiSurfaceKind), surface))
                throw new ArgumentOutOfRangeException(nameof(surface));
            if (!Enum.IsDefined(typeof(UiActionKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (destination.HasValue &&
                !Enum.IsDefined(typeof(UiSurfaceKind), destination.Value))
                throw new ArgumentOutOfRangeException(nameof(destination));
            if (!visible && enabled)
                throw new ArgumentException(
                    "Hidden UI action cannot be enabled.", nameof(enabled));
            if (visible && !enabled &&
                string.IsNullOrWhiteSpace(disabledReason))
                throw new ArgumentException(
                    "Visible disabled UI action requires an accessible reason.",
                    nameof(disabledReason));
            if (enabled && !string.IsNullOrWhiteSpace(disabledReason))
                throw new ArgumentException(
                    "Enabled UI action cannot carry a disabled reason.",
                    nameof(disabledReason));

            if (kind == UiActionKind.Navigate)
            {
                if (!destination.HasValue)
                    throw new ArgumentException(
                        "Navigate action requires a destination.", nameof(destination));
                if (!string.IsNullOrWhiteSpace(commandKey))
                    throw new ArgumentException(
                        "Navigate action cannot also be a command.", nameof(commandKey));
            }
            else if (kind == UiActionKind.Command)
            {
                if (destination.HasValue)
                    throw new ArgumentException(
                        "Command action cannot have a destination.", nameof(destination));
                if (string.IsNullOrWhiteSpace(commandKey))
                    throw new ArgumentException(
                        "Command action requires a command key.", nameof(commandKey));
            }
            else
            {
                if (destination.HasValue || !string.IsNullOrWhiteSpace(commandKey))
                    throw new ArgumentException(
                        "Back/Close actions cannot declare command or destination.");
            }

            ActionId = actionId;
            Surface = surface;
            Kind = kind;
            Visible = visible;
            Enabled = enabled;
            DisabledReason = disabledReason;
            Destination = destination;
            CommandKey = commandKey;
        }

        internal string Signature =>
            ((int)Surface) + "|" + ((int)Kind) + "|" +
            (Visible ? "1" : "0") + "|" + (Enabled ? "1" : "0") + "|" +
            (Destination.HasValue ? ((int)Destination.Value).ToString() : "") +
            "|" + (CommandKey ?? "") + "|" + (DisabledReason ?? "");
    }

    public sealed class UiSurfaceState
    {
        private readonly ReadOnlyDictionary<string, UiActionDefinition> _actions;

        public string StateId { get; }
        public UiSurfaceKind Surface { get; }
        public IReadOnlyDictionary<string, UiActionDefinition> Actions => _actions;

        public UiSurfaceState(string stateId, UiSurfaceKind surface,
            IEnumerable<UiActionDefinition> actions)
        {
            if (string.IsNullOrWhiteSpace(stateId))
                throw new ArgumentException("UI state ID required.", nameof(stateId));
            if (!Enum.IsDefined(typeof(UiSurfaceKind), surface))
                throw new ArgumentOutOfRangeException(nameof(surface));
            if (actions == null) throw new ArgumentNullException(nameof(actions));

            var array = actions.Take(129).ToArray();
            if (array.Length > 128 || array.Any(x => x == null))
                throw new ArgumentException(
                    "UI surface action limit exceeded or contains null.",
                    nameof(actions));

            var map = new Dictionary<string, UiActionDefinition>(
                StringComparer.Ordinal);
            foreach (var action in array)
            {
                if (action.Surface != surface)
                    throw new ArgumentException(
                        "UI action belongs to another surface.", nameof(actions));
                if (map.ContainsKey(action.ActionId))
                    throw new ArgumentException(
                        "Duplicate UI action ID on surface.", nameof(actions));
                map.Add(action.ActionId, action);
            }

            StateId = stateId;
            Surface = surface;
            _actions = new ReadOnlyDictionary<string, UiActionDefinition>(map);
        }
    }

    public sealed class UiActivation
    {
        public string ActivationId { get; }
        public string StateId { get; }
        public UiSurfaceKind Surface { get; }
        public string ActionId { get; }

        public UiActivation(string activationId, string stateId,
            UiSurfaceKind surface, string actionId)
        {
            if (string.IsNullOrWhiteSpace(activationId))
                throw new ArgumentException(
                    "UI activation ID required.", nameof(activationId));
            if (string.IsNullOrWhiteSpace(stateId))
                throw new ArgumentException("UI state ID required.", nameof(stateId));
            if (!Enum.IsDefined(typeof(UiSurfaceKind), surface))
                throw new ArgumentOutOfRangeException(nameof(surface));
            if (string.IsNullOrWhiteSpace(actionId))
                throw new ArgumentException("UI action ID required.", nameof(actionId));

            ActivationId = activationId;
            StateId = stateId;
            Surface = surface;
            ActionId = actionId;
        }

        internal string Signature =>
            StateId + "|" + ((int)Surface) + "|" + ActionId;
    }

    public sealed class UiRouteSnapshot
    {
        private readonly ReadOnlyDictionary<string, string> _activationSignatures;

        public UiSurfaceKind CurrentSurface { get; }
        public IReadOnlyDictionary<string, string> ActivationSignatures =>
            _activationSignatures;

        public UiRouteSnapshot(UiSurfaceKind currentSurface,
            IEnumerable<KeyValuePair<string, string>> activationSignatures = null)
        {
            if (!Enum.IsDefined(typeof(UiSurfaceKind), currentSurface))
                throw new ArgumentOutOfRangeException(nameof(currentSurface));

            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in activationSignatures ??
                Enumerable.Empty<KeyValuePair<string, string>>())
            {
                if (string.IsNullOrWhiteSpace(pair.Key) ||
                    string.IsNullOrWhiteSpace(pair.Value) ||
                    map.ContainsKey(pair.Key))
                    throw new ArgumentException(
                        "Invalid UI activation ledger.",
                        nameof(activationSignatures));
                if (map.Count >= 100000)
                    throw new ArgumentException(
                        "UI activation ledger limit exceeded.",
                        nameof(activationSignatures));
                map.Add(pair.Key, pair.Value);
            }

            CurrentSurface = currentSurface;
            _activationSignatures =
                new ReadOnlyDictionary<string, string>(map);
        }
    }

    public sealed class UiRoutePlan
    {
        public UiRouteSnapshot Result { get; }
        public bool Applied { get; }
        public UiActionKind Kind { get; }
        public UiSurfaceKind? Destination { get; }
        public string CommandKey { get; }

        internal UiRoutePlan(UiRouteSnapshot result, bool applied,
            UiActionKind kind, UiSurfaceKind? destination, string commandKey)
        {
            Result = result;
            Applied = applied;
            Kind = kind;
            Destination = destination;
            CommandKey = commandKey;
        }
    }

    public static class UiRouter
    {
        public static UiRoutePlan Activate(UiRouteSnapshot route,
            UiSurfaceState surfaceState, UiActivation activation)
        {
            if (route == null) throw new ArgumentNullException(nameof(route));
            if (surfaceState == null)
                throw new ArgumentNullException(nameof(surfaceState));
            if (activation == null)
                throw new ArgumentNullException(nameof(activation));

            if (!StringComparer.Ordinal.Equals(
                surfaceState.StateId, activation.StateId))
                throw new InvalidOperationException(
                    "UI activation references a stale or foreign state.");

            string priorSignature;
            if (route.ActivationSignatures.TryGetValue(
                activation.ActivationId, out priorSignature))
            {
                if (!StringComparer.Ordinal.Equals(
                    priorSignature, activation.Signature))
                    throw new InvalidOperationException(
                        "UI activation ID is bound to another payload.");

                UiActionDefinition replayAction;
                if (!surfaceState.Actions.TryGetValue(
                    activation.ActionId, out replayAction))
                    throw new InvalidOperationException(
                        "Replayed UI activation references unknown action.");

                return new UiRoutePlan(
                    route, false, replayAction.Kind,
                    replayAction.Destination, replayAction.CommandKey);
            }

            if (route.CurrentSurface != surfaceState.Surface ||
                activation.Surface != surfaceState.Surface)
                throw new InvalidOperationException(
                    "UI route, state and activation surfaces must match.");

            UiActionDefinition action;
            if (!surfaceState.Actions.TryGetValue(
                activation.ActionId, out action))
                throw new ArgumentException(
                    "Unknown UI action.", nameof(activation));
            if (!action.Visible)
                throw new InvalidOperationException(
                    "Hidden UI action cannot be activated.");
            if (!action.Enabled)
                throw new InvalidOperationException(
                    "Disabled UI action cannot be activated: " +
                    action.DisabledReason);

            var signatures = route.ActivationSignatures.ToDictionary(
                x => x.Key, x => x.Value, StringComparer.Ordinal);
            if (signatures.Count >= 100000)
                throw new InvalidOperationException(
                    "UI activation ledger limit reached.");
            signatures.Add(activation.ActivationId, activation.Signature);

            var destination = action.Kind == UiActionKind.Navigate
                ? action.Destination.Value
                : route.CurrentSurface;

            return new UiRoutePlan(
                new UiRouteSnapshot(destination, signatures),
                true,
                action.Kind,
                action.Destination,
                action.CommandKey);
        }
    }
}
