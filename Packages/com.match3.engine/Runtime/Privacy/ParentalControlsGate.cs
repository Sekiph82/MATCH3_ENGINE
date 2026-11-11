using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime.Privacy
{
    public enum ParentalGateAction
    {
        ExternalLinkOpen,
        PurchaseFlow,
        AccountCreation,
        SocialShare,
        ChatAccess,
        SettingsAccess
    }

    public readonly struct ParentalGateRecord
    {
        public string GameId { get; }
        public ParentalGateAction Action { get; }
        public bool HasParentalApproval { get; }
        public bool IsRestrictedProfile { get; }

        private ParentalGateRecord(string gameId, ParentalGateAction action, bool hasParentalApproval, bool isRestrictedProfile)
        {
            GameId = gameId;
            Action = action;
            HasParentalApproval = hasParentalApproval;
            IsRestrictedProfile = isRestrictedProfile;
        }

        public static ParentalGateRecord Create(string gameId, ParentalGateAction action, bool hasParentalApproval, bool isRestrictedProfile)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("gameId cannot be null or empty", nameof(gameId));
            return new ParentalGateRecord(gameId, action, hasParentalApproval, isRestrictedProfile);
        }

        public bool RequiresParentalApproval =>
            Action == ParentalGateAction.ExternalLinkOpen ||
            Action == ParentalGateAction.PurchaseFlow ||
            Action == ParentalGateAction.AccountCreation ||
            Action == ParentalGateAction.SocialShare;

        public bool IsGated => IsRestrictedProfile;
    }

    public readonly struct ParentalControlsPolicy
    {
        public bool EnforceParentalGate { get; }
        public bool BlockExternalLinksWithoutApproval { get; }
        public string PolicyDescription { get; }

        private ParentalControlsPolicy(bool enforceParentalGate, bool blockExternalLinksWithoutApproval, string policyDescription)
        {
            EnforceParentalGate = enforceParentalGate;
            BlockExternalLinksWithoutApproval = blockExternalLinksWithoutApproval;
            PolicyDescription = policyDescription;
        }

        public static ParentalControlsPolicy Safe(string desc)
        {
            if (string.IsNullOrEmpty(desc))
                throw new ArgumentException("PolicyDescription cannot be null or empty", nameof(desc));
            return new ParentalControlsPolicy(true, true, desc);
        }

        public static readonly ParentalControlsPolicy Default = Safe("Default parental controls policy - enforce parental gate, block external links");
    }

    public static class ParentalControlsGate
    {
        public static bool CanPerformAction(string gameId, ParentalGateRecord record, ParentalControlsPolicy policy, out string reason)
        {
            if (policy.PolicyDescription == null) policy = ParentalControlsPolicy.Default;

            if (string.IsNullOrEmpty(gameId))
            {
                reason = "GameIdMissing";
                return false;
            }
            if (!GameProfileCatalog.IsKnownId(gameId))
            {
                reason = "UnknownGame";
                return false;
            }

            if (!string.Equals(gameId, record.GameId, StringComparison.Ordinal))
            {
                reason = "GameIdMismatch";
                return false;
            }

            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal) &&
                record.Action == ParentalGateAction.ExternalLinkOpen && !record.HasParentalApproval)
            {
                reason = "WonderPopExternalLinkGated";
                return false;
            }

            if (policy.EnforceParentalGate && record.RequiresParentalApproval && !record.HasParentalApproval)
            {
                reason = "ParentalApprovalRequired";
                return false;
            }

            if (policy.BlockExternalLinksWithoutApproval && record.Action == ParentalGateAction.ExternalLinkOpen && !record.HasParentalApproval)
            {
                reason = "ExternalLinkBlocked";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        public static bool RequiresParentalApproval(ParentalGateRecord record) => record.RequiresParentalApproval;

        public static bool IsGated(ParentalGateRecord record) => record.IsGated;
    }

    public sealed class ParentalControlLog
    {
        private readonly List<string> _entries = new List<string>();

        public void Record(string gameId, string actionName)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("gameId cannot be null or empty", nameof(gameId));
            if (string.IsNullOrEmpty(actionName))
                throw new ArgumentException("actionName cannot be null or empty", nameof(actionName));
            _entries.Add(gameId + ":" + actionName);
        }

        public int Count => _entries.Count;

        public void Clear() => _entries.Clear();

        public bool HasEntry(string gameId) => _entries.Exists(e => e.StartsWith(gameId + ":", StringComparison.Ordinal));
    }
}
