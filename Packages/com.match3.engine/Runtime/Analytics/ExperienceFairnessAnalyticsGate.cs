using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime.Analytics
{
    public enum ExperienceEventKind
    {
        LevelFirstAttemptWin,
        LevelAbandon,
        NeedAHandShown,
        NeedAHandUsed,
        HintShown,
        StreakMilestone,
        StarChestEarned,
        EarnedPackDuplicate,
        DailyChallenge,
        CatchUpTriggered
    }

    public readonly struct ExperienceRecord
    {
        public string GameId { get; }
        public ExperienceEventKind EventKind { get; }
        public bool IsTestBot { get; }
        public bool IsConsentDenied { get; }

        private ExperienceRecord(string gameId, ExperienceEventKind eventKind, bool isTestBot, bool isConsentDenied)
        {
            GameId = gameId;
            EventKind = eventKind;
            IsTestBot = isTestBot;
            IsConsentDenied = isConsentDenied;
        }

        public static ExperienceRecord Create(string gameId, ExperienceEventKind eventKind, bool isTestBot, bool isConsentDenied)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("gameId must not be null or empty.", nameof(gameId));
            return new ExperienceRecord(gameId, eventKind, isTestBot, isConsentDenied);
        }

        public bool IsEligiblePlayer => !IsTestBot && !IsConsentDenied;
    }

    public readonly struct ExperienceFairnessPolicy
    {
        // Only test-bot inclusion is relaxable; consent-denied players are always excluded (core invariant).
        public bool AllowTestBotEvents { get; }
        public string PolicyDescription { get; }

        private ExperienceFairnessPolicy(bool allowTestBotEvents, string policyDescription)
        {
            if (string.IsNullOrEmpty(policyDescription))
                throw new ArgumentException("PolicyDescription must not be null or empty.", nameof(policyDescription));
            AllowTestBotEvents = allowTestBotEvents;
            PolicyDescription = policyDescription;
        }

        public static ExperienceFairnessPolicy Safe(string desc)
        {
            return new ExperienceFairnessPolicy(false, desc);
        }

        public static ExperienceFairnessPolicy AllowTestBots(string desc)
        {
            return new ExperienceFairnessPolicy(true, desc);
        }

        public static readonly ExperienceFairnessPolicy Default =
            Safe("Default fairness policy - exclude test bots and consent-denied players");
    }

    public static class ExperienceFairnessGate
    {
        public static bool CanRecordEvent(string gameId, ExperienceRecord record, ExperienceFairnessPolicy policy, out string reason)
        {
            if (policy.PolicyDescription == null) policy = ExperienceFairnessPolicy.Default;

            if (!string.Equals(gameId, record.GameId, StringComparison.Ordinal))
            {
                reason = "GameIdMismatch";
                return false;
            }

            if (string.Equals(gameId, "Wonder_Pop", StringComparison.Ordinal))
            {
                reason = "WonderPopExperienceProhibited";
                return false;
            }

            if (!policy.AllowTestBotEvents && record.IsTestBot)
            {
                reason = "TestBotExcluded";
                return false;
            }

            // Unconditional: consent-denied players are never recorded, under any policy
            if (record.IsConsentDenied)
            {
                reason = "ConsentDeniedExcluded";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        public static bool IsEligiblePlayer(ExperienceRecord record) => record.IsEligiblePlayer;

        public static bool IsConsentDenied(ExperienceRecord record) => record.IsConsentDenied;
    }

    public sealed class ExperienceEventLog
    {
        private readonly List<string> _entries = new List<string>();

        public int Count => _entries.Count;

        public void Record(string gameId, string eventName)
        {
            if (string.IsNullOrEmpty(gameId))
                throw new ArgumentException("gameId must not be null or empty.", nameof(gameId));
            if (string.IsNullOrEmpty(eventName))
                throw new ArgumentException("eventName must not be null or empty.", nameof(eventName));
            _entries.Add(gameId + ":" + eventName);
        }

        public void Clear() => _entries.Clear();

        public bool HasEntry(string gameId) => _entries.Exists(e => e.StartsWith(gameId + ":", StringComparison.Ordinal));
    }
}
