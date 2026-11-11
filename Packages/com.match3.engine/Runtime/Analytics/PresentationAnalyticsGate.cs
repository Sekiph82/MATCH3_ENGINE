using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime.Analytics
{
    public enum PresentationEventKind
    {
        TutorialShown,
        TutorialCompleted,
        TutorialSkipped,
        BannerDisplayed,
        PopupShown,
        PopupDismissed,
        RewardVideoShown,
        RewardVideoClaimed,
        ThemeUnlocked,
        SeasonPassShown
    }

    public readonly struct PresentationRecord
    {
        public string GameId { get; }
        public PresentationEventKind EventKind { get; }
        public bool IsTestSession { get; }
        public bool IsConsentDenied { get; }

        private PresentationRecord(string gameId, PresentationEventKind eventKind, bool isTestSession, bool isConsentDenied)
        {
            GameId = gameId;
            EventKind = eventKind;
            IsTestSession = isTestSession;
            IsConsentDenied = isConsentDenied;
        }

        public static PresentationRecord Create(string gameId, PresentationEventKind eventKind, bool isTestSession, bool isConsentDenied)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("gameId cannot be null or empty.", nameof(gameId));
            return new PresentationRecord(gameId, eventKind, isTestSession, isConsentDenied);
        }

        public bool IsEligibleSession => !IsTestSession && !IsConsentDenied;
    }

    public readonly struct PresentationAnalyticsPolicy
    {
        public bool AllowTestSessionEvents { get; }
        public bool AllowConsentDeniedEvents { get; }
        public string PolicyDescription { get; }

        private PresentationAnalyticsPolicy(bool allowTestSessionEvents, bool allowConsentDeniedEvents, string policyDescription)
        {
            if (string.IsNullOrEmpty(policyDescription))
                throw new ArgumentException("PolicyDescription cannot be null or empty.", nameof(policyDescription));
            AllowTestSessionEvents = allowTestSessionEvents;
            AllowConsentDeniedEvents = allowConsentDeniedEvents;
            PolicyDescription = policyDescription;
        }

        public static PresentationAnalyticsPolicy Safe(string desc)
        {
            return new PresentationAnalyticsPolicy(false, false, desc);
        }

        public static PresentationAnalyticsPolicy AllowTest(string desc)
        {
            return new PresentationAnalyticsPolicy(true, false, desc);
        }

        public static PresentationAnalyticsPolicy AllowConsentDenied(string desc)
        {
            return new PresentationAnalyticsPolicy(false, true, desc);
        }

        public static readonly PresentationAnalyticsPolicy Default =
            Safe("Default presentation policy - exclude test sessions and consent-denied players");
    }

    public static class PresentationAnalyticsGate
    {
        public static bool CanRecordEvent(string gameId, PresentationRecord record, PresentationAnalyticsPolicy policy, out string reason)
        {
            if (policy.PolicyDescription == null) policy = PresentationAnalyticsPolicy.Default;

            if (!string.Equals(gameId, record.GameId, StringComparison.Ordinal))
            {
                reason = "GameIdMismatch";
                return false;
            }

            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal))
            {
                reason = "WonderPopPresentationProhibited";
                return false;
            }

            if (!policy.AllowTestSessionEvents && record.IsTestSession)
            {
                reason = "TestSessionExcluded";
                return false;
            }

            if (!policy.AllowConsentDeniedEvents && record.IsConsentDenied)
            {
                reason = "ConsentDeniedExcluded";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        public static bool IsEligibleSession(PresentationRecord record) => record.IsEligibleSession;

        public static bool IsConsentDenied(PresentationRecord record) => record.IsConsentDenied;
    }

    public sealed class PresentationEventLog
    {
        private readonly List<string> _entries = new List<string>();

        public void Record(string gameId, string eventName)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("gameId cannot be null or empty.", nameof(gameId));
            if (string.IsNullOrEmpty(eventName))
                throw new ArgumentException("eventName cannot be null or empty.", nameof(eventName));
            _entries.Add(gameId + ":" + eventName);
        }

        public int Count => _entries.Count;

        public void Clear() => _entries.Clear();

        public bool HasEntry(string gameId) => _entries.Exists(e => e.StartsWith(gameId + ":", StringComparison.Ordinal));
    }
}
