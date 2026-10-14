using System;
using Match3.Engine.Runtime;
using NUnit.Framework;

namespace Match3.Engine.Tests
{
    public sealed class IdleHintPolicyTests
    {
        private static CandidateScanResult Candidates(bool legal = true)
        {
            var first = new BoardPosition(0, 0);
            var second = new BoardPosition(0, 1);
            var third = new BoardPosition(1, 0);
            var b = new BoardTopology(2, 2, occupants: new[]
            {
                new BoardOccupant(first, "A"), new BoardOccupant(second, "B"),
                new BoardOccupant(third, "C"),
                new BoardOccupant(new BoardPosition(1, 1), "D")
            });
            return CanonicalMoveCandidates.Find(b,
                move => legal && (move.Target.Equals(second) || move.Target.Equals(third)));
        }

        private static bool Ask(IdleHintPolicy policy, long now, CandidateScanResult candidates,
            EnginePhase phase = EnginePhase.Ready, bool enabled = true,
            bool tutorial = false, bool modal = false, bool ad = false,
            bool animating = false, bool lowPower = true)
        {
            SwapCandidate hint;
            return policy.TrySelect(now, phase, enabled, tutorial, modal, ad, animating,
                lowPower, candidates, out hint);
        }

        [Test]
        public void TrueIdleDeadlineEmitsOneDeterministicSdkLegalHint()
        {
            var first = new IdleHintPolicy(5000, 20261014u);
            var second = new IdleHintPolicy(5000, 20261014u);
            first.RecordAction(10000);
            second.RecordAction(10000);
            Assert.That(Ask(first, 14999, Candidates()), Is.False);
            Assert.That(Ask(second, 14999, Candidates()), Is.False);
            SwapCandidate a, b;
            Assert.That(first.TrySelect(15000, EnginePhase.Ready, true,
                false, false, false, false, true, Candidates(), out a), Is.True);
            Assert.That(second.TrySelect(15000, EnginePhase.Ready, true,
                false, false, false, false, true, Candidates(), out b), Is.True);
            Assert.That(a.Equals(b), Is.True);
            Assert.That(Ask(first, 15001, Candidates()), Is.False);
        }

        [Test]
        public void ModalTutorialAdsAnimationPauseAndPowerPolicyAllSuppressHints()
        {
            var h = new IdleHintPolicy(1000, 1u);
            h.RecordAction(1000);
            var moves = Candidates();
            Assert.That(Ask(h, 3000, moves, EnginePhase.Paused), Is.False);
            Assert.That(Ask(h, 3000, moves, EnginePhase.Resolving), Is.False);
            Assert.That(Ask(h, 3000, moves, enabled:false), Is.False);
            Assert.That(Ask(h, 3000, moves, tutorial:true), Is.False);
            Assert.That(Ask(h, 3000, moves, modal:true), Is.False);
            Assert.That(Ask(h, 3000, moves, ad:true), Is.False);
            Assert.That(Ask(h, 3000, moves, animating:true), Is.False);
            Assert.That(Ask(h, 3000, moves, lowPower:false), Is.False);
            Assert.That(Ask(h, 3000, moves), Is.True);
        }

        [Test]
        public void MissingOrZeroSdkLegalMovesNeverProduceFabricatedHint()
        {
            var h = new IdleHintPolicy(1000, 99);
            h.RecordAction(1);
            Assert.That(Ask(h, 90000, null), Is.False);
            Assert.That(Ask(h, 90000, Candidates(false)), Is.False);
            Assert.That(Ask(h, 90000, Candidates()), Is.True);
        }

        [Test]
        public void RealPlayerActionResetsIdleAndReleasesOneHintPerAction()
        {
            var h = new IdleHintPolicy(1000, 10);
            h.RecordAction(0);
            Assert.That(Ask(h, 1000, Candidates()), Is.True);
            Assert.That(Ask(h, 9999, Candidates()), Is.False);
            h.RecordAction(10000);
            Assert.That(Ask(h, 10010, Candidates()), Is.False);
            Assert.That(Ask(h, 11000, Candidates()), Is.True);
        }

        [Test]
        public void HintTimerCannotGoBackwardsOrUseInvalidUnseededConfiguration()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new IdleHintPolicy(0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new IdleHintPolicy(600001, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new IdleHintPolicy(1000, 0));
            var h = new IdleHintPolicy(1000, 1);
            h.RecordAction(300);
            Assert.Throws<ArgumentOutOfRangeException>(() => h.RecordAction(299));
            Assert.That(Ask(h, 299, Candidates()), Is.False);
            Assert.That(Ask(h, 1299, Candidates()), Is.False);
            Assert.That(Ask(h, 1300, Candidates()), Is.True);
        }
    }
}
