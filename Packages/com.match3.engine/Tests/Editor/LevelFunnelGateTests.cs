// LevelFunnelGateTests.cs — ENG-M18-004
using NUnit.Framework;
using Match3.Engine.Runtime.Analytics;
using System;

namespace Match3.Engine.Tests.Editor
{
    [TestFixture]
    public class LevelFunnelGateTests
    {
        // ---- LevelAttempt validation ----

        [Test]
        public void LevelAttempt_NullGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() => LevelAttempt.CreateFail(null, 0, 1, FailCause.OutOfMoves));
        }

        [Test]
        public void LevelAttempt_EmptyGameId_Throws()
        {
            Assert.Throws<ArgumentException>(() => LevelAttempt.CreateFail("", 0, 1, FailCause.OutOfMoves));
        }

        [Test]
        public void LevelAttempt_NegativeLevelId_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => LevelAttempt.CreateFail("GameA", -1, 1, FailCause.OutOfMoves));
        }

        [Test]
        public void LevelAttempt_ZeroAttemptNumber_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => LevelAttempt.CreateFail("GameA", 0, 0, FailCause.OutOfMoves));
        }

        [Test]
        public void LevelAttempt_NegativeAttemptNumber_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => LevelAttempt.CreateFail("GameA", 0, -1, FailCause.OutOfMoves));
        }

        // ---- CreateFail factory ----

        [Test]
        public void CreateFail_SetsWasCompletedFalse()
        {
            var a = LevelAttempt.CreateFail("GameA", 1, 1, FailCause.TimeExpired);
            Assert.IsFalse(a.WasCompleted);
        }

        [Test]
        public void CreateFail_SetsCauseCorrectly()
        {
            var a = LevelAttempt.CreateFail("GameA", 1, 1, FailCause.PlayerQuit);
            Assert.AreEqual(FailCause.PlayerQuit, a.Cause);
        }

        // ---- CreateComplete factory ----

        [Test]
        public void CreateComplete_SetsWasCompletedTrue()
        {
            var a = LevelAttempt.CreateComplete("GameA", 2, 3);
            Assert.IsTrue(a.WasCompleted);
        }

        [Test]
        public void CreateComplete_SetsExpectedFields()
        {
            var a = LevelAttempt.CreateComplete("GameB", 5, 2);
            Assert.AreEqual("GameB", a.GameId);
            Assert.AreEqual(5, a.LevelId);
            Assert.AreEqual(2, a.AttemptNumber);
        }

        // ---- IsBoosterDependent ----

        [Test]
        public void IsBoosterDependent_AlwaysFalse_Fail()
        {
            var a = LevelAttempt.CreateFail("GameA", 0, 1, FailCause.OutOfMoves);
            Assert.IsFalse(a.IsBoosterDependent);
        }

        [Test]
        public void IsBoosterDependent_AlwaysFalse_Complete()
        {
            var a = LevelAttempt.CreateComplete("GameA", 0, 1);
            Assert.IsFalse(a.IsBoosterDependent);
        }

        // ---- Wonder_Pop guard (Guard 1 ordinal) ----

        [Test]
        public void CanTrackAttempt_WonderPop_ExactCase_Blocked()
        {
            var attempt = LevelAttempt.CreateFail("Wonder_Pop", 1, 1, FailCause.OutOfMoves);
            var policy = FunnelPolicy.Default;
            var (eligible, reason) = LevelFunnelGate.CanTrackAttempt("Wonder_Pop", attempt, policy);
            Assert.IsFalse(eligible);
            Assert.AreEqual("WonderPopFunnelProhibited", reason);
        }

        [Test]
        public void CanTrackAttempt_WonderPop_Lowercase_NotBlocked()
        {
            var attempt = LevelAttempt.CreateFail("wonder_pop", 1, 1, FailCause.OutOfMoves);
            var policy = FunnelPolicy.Default;
            var (eligible, _) = LevelFunnelGate.CanTrackAttempt("wonder_pop", attempt, policy);
            Assert.IsTrue(eligible);
        }

        // ---- ExceedsTrackingLimit guard ----

        [Test]
        public void CanTrackAttempt_AtLimit_Eligible()
        {
            var policy = new FunnelPolicy(10, false, "test policy");
            var attempt = LevelAttempt.CreateFail("GameA", 1, 10, FailCause.OutOfMoves);
            var (eligible, _) = LevelFunnelGate.CanTrackAttempt("GameA", attempt, policy);
            Assert.IsTrue(eligible);
        }

        [Test]
        public void CanTrackAttempt_OverLimit_Blocked()
        {
            var policy = new FunnelPolicy(10, false, "test policy");
            var attempt = LevelAttempt.CreateFail("GameA", 1, 11, FailCause.OutOfMoves);
            var (eligible, reason) = LevelFunnelGate.CanTrackAttempt("GameA", attempt, policy);
            Assert.IsFalse(eligible);
            Assert.AreEqual("ExceedsTrackingLimit", reason);
        }

        // ---- IsCompletionAttempt ----

        [Test]
        public void IsCompletionAttempt_WasCompleted_True()
        {
            var a = LevelAttempt.CreateComplete("GameA", 1, 1);
            Assert.IsTrue(LevelFunnelGate.IsCompletionAttempt(a));
        }

        [Test]
        public void IsCompletionAttempt_WasFailed_False()
        {
            var a = LevelAttempt.CreateFail("GameA", 1, 1, FailCause.OutOfMoves);
            Assert.IsFalse(LevelFunnelGate.IsCompletionAttempt(a));
        }

        // ---- RequiresBoosterMetrics ----

        [Test]
        public void RequiresBoosterMetrics_False_WhenDisabled()
        {
            Assert.IsFalse(LevelFunnelGate.RequiresBoosterMetrics(FunnelPolicy.Default));
        }

        [Test]
        public void RequiresBoosterMetrics_True_WhenEnabled()
        {
            var policy = new FunnelPolicy(50, true, "booster enabled policy");
            Assert.IsTrue(LevelFunnelGate.RequiresBoosterMetrics(policy));
        }

        // ---- FunnelPolicy Safe and Default ----

        [Test]
        public void Safe_SetsMaxAttempts50()
        {
            var p = FunnelPolicy.Safe("desc");
            Assert.AreEqual(50, p.MaxAttemptsToTrack);
        }

        [Test]
        public void Safe_DisablesBoosterMetrics()
        {
            var p = FunnelPolicy.Safe("desc");
            Assert.IsFalse(p.IncludeBoosterMetrics);
        }

        [Test]
        public void Default_IsCorrect()
        {
            var d = FunnelPolicy.Default;
            Assert.AreEqual(50, d.MaxAttemptsToTrack);
            Assert.IsFalse(d.IncludeBoosterMetrics);
            Assert.IsFalse(string.IsNullOrEmpty(d.PolicyDescription));
        }

        // ---- FunnelPolicy validation ----

        [Test]
        public void FunnelPolicy_NullDescription_Throws()
        {
            Assert.Throws<ArgumentException>(() => new FunnelPolicy(10, false, null));
        }

        [Test]
        public void FunnelPolicy_EmptyDescription_Throws()
        {
            Assert.Throws<ArgumentException>(() => new FunnelPolicy(10, false, ""));
        }

        [Test]
        public void FunnelPolicy_ZeroMaxAttempts_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new FunnelPolicy(0, false, "desc"));
        }

        [Test]
        public void FunnelPolicy_NegativeMaxAttempts_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new FunnelPolicy(-1, false, "desc"));
        }

        // ---- FunnelAccumulator ----

        [Test]
        public void Accumulator_NeverRecorded_AttemptCountZero()
        {
            var acc = new FunnelAccumulator();
            Assert.AreEqual(0, acc.GetAttemptCount(99));
        }

        [Test]
        public void Accumulator_NeverRecorded_CompletionRateZero()
        {
            var acc = new FunnelAccumulator();
            Assert.AreEqual(0.0, acc.GetCompletionRate(99));
        }

        [Test]
        public void Accumulator_RecordAttempt_NegativeLevelId_Throws()
        {
            var acc = new FunnelAccumulator();
            Assert.Throws<ArgumentOutOfRangeException>(() => acc.RecordAttempt(-1, false));
        }

        [Test]
        public void Accumulator_RecordAttempts_CountsCorrectly()
        {
            var acc = new FunnelAccumulator();
            acc.RecordAttempt(1, false);
            acc.RecordAttempt(1, true);
            acc.RecordAttempt(1, false);
            Assert.AreEqual(3, acc.GetAttemptCount(1));
            Assert.AreEqual(1, acc.GetCompletionCount(1));
        }

        [Test]
        public void Accumulator_CompletionRate_CorrectFraction()
        {
            var acc = new FunnelAccumulator();
            acc.RecordAttempt(5, true);
            acc.RecordAttempt(5, false);
            acc.RecordAttempt(5, false);
            acc.RecordAttempt(5, false);
            Assert.AreEqual(0.25, acc.GetCompletionRate(5), 1e-9);
        }

        [Test]
        public void Accumulator_Clear_ResetsAll()
        {
            var acc = new FunnelAccumulator();
            acc.RecordAttempt(1, true);
            acc.Clear();
            Assert.AreEqual(0, acc.GetAttemptCount(1));
            Assert.AreEqual(0.0, acc.GetCompletionRate(1));
        }
    }
}
