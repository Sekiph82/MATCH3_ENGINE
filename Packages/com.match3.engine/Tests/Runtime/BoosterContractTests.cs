using System;
using System.Collections.Generic;
using System.Linq;
using Match3.Engine.Runtime;
using NUnit.Framework;

namespace Match3.Engine.Tests
{
    public sealed class BoosterContractTests
    {
        private static BoardPosition P(int row, int col, int layer = 0) =>
            new BoardPosition(row, col, layer);

        private static BoosterEconomySnapshot Economy(
            BoosterKind kind, int amount)
        {
            return new BoosterEconomySnapshot(
                new Dictionary<BoosterKind, int> { { kind, amount } });
        }

        private static SpecialBoardSnapshot SpecialBoard()
        {
            var cells = new List<SpecialCellState>();
            var colors = new[] { "red", "blue", "red" };
            for (var row = 0; row < 3; row++)
                for (var col = 0; col < 3; col++)
                    cells.Add(new SpecialCellState(
                        P(row, col), colors[col],
                        obstacleLayers: (row == 1 && col == 1) ? 1 : 0,
                        goalPriority: row + col));
            return new SpecialBoardSnapshot(3, 3, cells);
        }

        [Test]
        public void M06_001_PrelevelReservationAndPlacementAreDeterministicAndPreviewDoesNotSpend()
        {
            var economy = Economy(BoosterKind.Area, 1);
            var reserved = BoosterEconomyPlanner.Reserve(
                economy, BoosterKind.Area, "prelevel-1");
            Assert.That(reserved.Result.Balance(BoosterKind.Area), Is.EqualTo(1));
            Assert.That(reserved.Result.Available(BoosterKind.Area), Is.EqualTo(0));
            Assert.That(reserved.Receipt.State,
                Is.EqualTo(BoosterTransactionState.Reserved));

            var board = new BoardTopology(2, 3,
                holes: new[] { P(0,2) },
                locked: new[] { P(0,1) },
                occupants: new[]
                {
                    new BoardOccupant(P(0,0), "A"),
                    new BoardOccupant(P(0,1), "LOCKED"),
                    new BoardOccupant(P(1,0), "B"),
                    new BoardOccupant(P(1,1), "C"),
                    new BoardOccupant(P(1,2), "D")
                });
            var a = PrelevelBoosterPlanner.Plan(
                board, BoosterKind.Area, 123u);
            var b = PrelevelBoosterPlanner.Plan(
                board, BoosterKind.Area, 123u);
            Assert.That(a.Position, Is.EqualTo(b.Position));
            Assert.That(board.IsLocked(a.Position), Is.False);
            Assert.That(board.IsHole(a.Position), Is.False);
            Assert.That(board.HasOccupant(a.Position), Is.True);
            Assert.That(a.SpecialKind, Is.EqualTo(SpecialActivationKind.Area));
            Assert.That(economy.Balance(BoosterKind.Area), Is.EqualTo(1));

            var manyA = PrelevelBoosterPlanner.PlanMany(
                board,
                new[] { BoosterKind.Area, BoosterKind.HorizontalLine },
                456u);
            var manyB = PrelevelBoosterPlanner.PlanMany(
                board,
                new[] { BoosterKind.Area, BoosterKind.HorizontalLine },
                456u);
            Assert.That(manyA.Select(x => x.Position).ToArray(),
                Is.EqualTo(manyB.Select(x => x.Position).ToArray()));
            Assert.That(manyA.Select(x => x.Position).Distinct().Count(),
                Is.EqualTo(2));

            var committed = BoosterEconomyPlanner.Confirm(
                reserved.Result, "prelevel-1");
            Assert.That(committed.Result.Balance(BoosterKind.Area), Is.EqualTo(0));
            Assert.That(BoosterEconomyPlanner.Confirm(
                committed.Result, "prelevel-1").Receipt.Applied, Is.False);

            Assert.Throws<InvalidOperationException>(() =>
                BoosterEconomyPlanner.Reserve(
                    committed.Result, BoosterKind.Area, "prelevel-2"));
            Assert.Throws<ArgumentException>(() =>
                PrelevelBoosterPlanner.Plan(
                    board, BoosterKind.Hammer, 123u));

            var noCandidate = new BoardTopology(1, 1,
                locked: new[] { P(0,0) },
                occupants: new[] { new BoardOccupant(P(0,0), "LOCKED") });
            Assert.Throws<InvalidOperationException>(() =>
                PrelevelBoosterPlanner.Plan(
                    noCandidate, BoosterKind.Area, 123u));
        }

        [Test]
        public void M06_002_HammerValidatesTargetAndProducesExactlyOneCellWithoutChargingRejectedAction()
        {
            var board = new SpecialBoardSnapshot(1, 3, new[]
            {
                new SpecialCellState(P(0,0), "red"),
                new SpecialCellState(P(0,1), "blue", immune: true),
                new SpecialCellState(P(0,2), "green", locked: true)
            });

            var valid = BoosterEffectPlanner.Plan(board,
                new BoosterActionRequest("hammer-ok", BoosterKind.Hammer, P(0,0)));
            Assert.That(valid.HitPositions, Is.EqualTo(new[] { P(0,0) }));
            Assert.That(valid.CanonicalSpecialPlan, Is.Null);

            var economy = Economy(BoosterKind.Hammer, 1);
            var adult = new BoosterProfilePolicy(
                false, true, true, true);

            Assert.Throws<InvalidOperationException>(() =>
                BoosterActionLifecycle.PrepareEffect(
                    economy, adult, BoosterAcquisitionSource.Owned, true,
                    board,
                    new BoosterActionRequest(
                        "hammer-bad", BoosterKind.Hammer, P(0,1))));
            Assert.That(economy.Balance(BoosterKind.Hammer), Is.EqualTo(1));
            Assert.That(economy.Transactions.ContainsKey("hammer-bad"), Is.False);

            Assert.Throws<InvalidOperationException>(() =>
                BoosterActionLifecycle.PrepareEffect(
                    economy, adult, BoosterAcquisitionSource.Owned, true,
                    board,
                    new BoosterActionRequest(
                        "hammer-locked", BoosterKind.Hammer, P(0,2))));
        }

        [Test]
        public void M06_003_LineAreaAndColorBoostersAreDeterministicTypedAndDeduplicated()
        {
            var board = SpecialBoard();
            var requests = new[]
            {
                new BoosterActionRequest("h", BoosterKind.HorizontalLine, P(1,1), seed: 77u),
                new BoosterActionRequest("v", BoosterKind.VerticalLine, P(1,1), seed: 77u),
                new BoosterActionRequest("a", BoosterKind.Area, P(1,1), radius: 1, seed: 77u),
                new BoosterActionRequest("c", BoosterKind.Color, P(1,1),
                    targetColor: "red", seed: 77u)
            };

            foreach (var request in requests)
            {
                var first = BoosterEffectPlanner.Plan(board, request);
                var second = BoosterEffectPlanner.Plan(board, request);
                Assert.That(first.LogicalHash, Is.EqualTo(second.LogicalHash));
                Assert.That(first.CanonicalSpecialPlan, Is.Not.Null);
                Assert.That(first.HitPositions.Distinct().Count(),
                    Is.EqualTo(first.HitPositions.Count));
                Assert.That(first.LogicalHash,
                    Is.EqualTo(SpecialGoldenHasher.HashEffectPlan(
                        first.CanonicalSpecialPlan)));
            }
        }

        [Test]
        public void M06_004_ShuffleAndSwapAreSeededLegalAndPreserveTopologyAndMultiset()
        {
            var board = new BoardTopology(2, 3,
                holes: new[] { P(0,2) },
                locked: new[] { P(1,2) },
                occupants: new[]
                {
                    new BoardOccupant(P(0,0), "A"),
                    new BoardOccupant(P(0,1), "B"),
                    new BoardOccupant(P(1,0), "C"),
                    new BoardOccupant(P(1,1), "D"),
                    new BoardOccupant(P(1,2), "LOCK")
                });

            var shuffleRequest = new BoosterActionRequest(
                "shuffle", BoosterKind.Shuffle, P(0,0), seed: 321u);
            var first = BoosterBoardTransformPlanner.Plan(board, shuffleRequest);
            var second = BoosterBoardTransformPlanner.Plan(board, shuffleRequest);
            Assert.That(first.LogicalHash, Is.EqualTo(second.LogicalHash));
            Assert.That(first.Occupants[P(1,2)], Is.EqualTo("LOCK"));
            Assert.That(first.Occupants.Values.OrderBy(x => x, StringComparer.Ordinal),
                Is.EqualTo(board.Occupants.Values
                    .OrderBy(x => x, StringComparer.Ordinal)));
            Assert.That(first.ChangedPositions.All(x =>
                !board.IsLocked(x) && !board.IsHole(x)), Is.True);

            var swap = BoosterBoardTransformPlanner.Plan(board,
                new BoosterActionRequest(
                    "swap", BoosterKind.Swap, P(0,0), P(0,1)));
            Assert.That(swap.Occupants[P(0,0)], Is.EqualTo("B"));
            Assert.That(swap.Occupants[P(0,1)], Is.EqualTo("A"));
            Assert.That(board.Occupants[P(0,0)], Is.EqualTo("A"));

            Assert.Throws<InvalidOperationException>(() =>
                BoosterBoardTransformPlanner.Plan(board,
                    new BoosterActionRequest(
                        "bad-swap", BoosterKind.Swap, P(0,0), P(1,1))));
        }

        [Test]
        public void M06_005_BoosterSpecialChainsDelegateToCanonicalM03EffectPlanner()
        {
            var board = SpecialBoard();
            var request = new BoosterActionRequest(
                "rocket", BoosterKind.HorizontalLine, P(1,1), seed: 91u);
            var booster = BoosterEffectPlanner.Plan(board, request);
            var direct = SpecialEffectPlanner.Resolve(board,
                new SpecialActivation(
                    SpecialActivationKind.HorizontalLine, P(1,1)),
                null, 91u);

            Assert.That(booster.LogicalHash,
                Is.EqualTo(SpecialGoldenHasher.HashEffectPlan(direct)));
            Assert.That(booster.CanonicalSpecialPlan.Hits
                .Select(x => x.Position).ToArray(),
                Is.EqualTo(direct.Hits.Select(x => x.Position).ToArray()));
            Assert.That(booster.CanonicalSpecialPlan.Activations
                .Select(x => x.Kind).ToArray(),
                Is.EqualTo(direct.Activations.Select(x => x.Kind).ToArray()));
        }

        [Test]
        public void M06_006_EconomyReserveConfirmRefundAndGrantAreExactlyOnce()
        {
            var start = Economy(BoosterKind.Hammer, 2);
            var reserved = BoosterEconomyPlanner.Reserve(
                start, BoosterKind.Hammer, "spend-1");
            Assert.That(reserved.Result.Balance(BoosterKind.Hammer), Is.EqualTo(2));
            Assert.That(reserved.Result.Available(BoosterKind.Hammer), Is.EqualTo(1));

            var duplicateReserve = BoosterEconomyPlanner.Reserve(
                reserved.Result, BoosterKind.Hammer, "spend-1");
            Assert.That(duplicateReserve.Receipt.Applied, Is.False);

            var committed = BoosterEconomyPlanner.Confirm(
                reserved.Result, "spend-1");
            Assert.That(committed.Result.Balance(BoosterKind.Hammer), Is.EqualTo(1));
            Assert.That(BoosterEconomyPlanner.Confirm(
                committed.Result, "spend-1").Receipt.Applied, Is.False);

            var refunded = BoosterEconomyPlanner.Refund(
                committed.Result, "spend-1");
            Assert.That(refunded.Result.Balance(BoosterKind.Hammer), Is.EqualTo(2));
            Assert.That(BoosterEconomyPlanner.Refund(
                refunded.Result, "spend-1").Receipt.Applied, Is.False);

            var granted = BoosterEconomyPlanner.Grant(
                refunded.Result, BoosterKind.Hammer, "reward-1", 2);
            Assert.That(granted.Result.Balance(BoosterKind.Hammer), Is.EqualTo(4));
            Assert.That(BoosterEconomyPlanner.Grant(
                granted.Result, BoosterKind.Hammer, "reward-1", 2)
                .Receipt.Applied, Is.False);

            Assert.Throws<InvalidOperationException>(() =>
                BoosterEconomyPlanner.Grant(
                    granted.Result, BoosterKind.Area, "reward-1", 2));

            var concurrentStart = Economy(BoosterKind.Hammer, 2);
            var reserveA = BoosterEconomyPlanner.Reserve(
                concurrentStart, BoosterKind.Hammer, "concurrent-a");
            var reserveB = BoosterEconomyPlanner.Reserve(
                reserveA.Result, BoosterKind.Hammer, "concurrent-b");
            var confirmA = BoosterEconomyPlanner.Confirm(
                reserveB.Result, "concurrent-a");
            Assert.That(confirmA.Result.Transactions.ContainsKey("concurrent-b"),
                Is.True);
            Assert.That(confirmA.Result.Transactions["concurrent-b"].State,
                Is.EqualTo(BoosterTransactionState.Reserved));
            Assert.That(confirmA.Result.Balance(BoosterKind.Hammer), Is.EqualTo(1));
            Assert.That(confirmA.Result.Available(BoosterKind.Hammer), Is.EqualTo(0));

            Assert.Throws<ArgumentException>(() =>
                new BoosterEconomySnapshot(
                    new Dictionary<BoosterKind, int>
                    {
                        { BoosterKind.Hammer, 0 }
                    },
                    new[]
                    {
                        new BoosterTransaction(
                            "bad-reserve", BoosterKind.Hammer, 1,
                            BoosterTransactionState.Reserved)
                    }));
        }

        [Test]
        public void M06_007_PresentationPreviewConfirmCancelAndSkipCannotChangeLogicalHash()
        {
            var board = SpecialBoard();
            var logical = BoosterEffectPlanner.Plan(board,
                new BoosterActionRequest(
                    "fx", BoosterKind.Area, P(1,1), radius: 1, seed: 5u));

            var preview = BoosterPresentationPlanner.Project(
                logical, "fx", BoosterPresentationPhase.Preview);
            var confirmed = BoosterPresentationPlanner.Project(
                logical, "fx", BoosterPresentationPhase.Confirmed);
            var cancelled = BoosterPresentationPlanner.Project(
                logical, "fx", BoosterPresentationPhase.Cancelled);
            var skipped = BoosterPresentationPlanner.Project(
                logical, "fx", BoosterPresentationPhase.Confirmed, true);

            Assert.That(preview.LogicalHash, Is.EqualTo(logical.LogicalHash));
            Assert.That(confirmed.LogicalHash, Is.EqualTo(logical.LogicalHash));
            Assert.That(cancelled.LogicalHash, Is.EqualTo(logical.LogicalHash));
            Assert.That(skipped.LogicalHash, Is.EqualTo(logical.LogicalHash));
            Assert.That(preview.Events.Single().PresentationKey,
                Does.EndWith(".preview"));
            Assert.That(confirmed.Events.Single().PresentationKey,
                Does.EndWith(".confirmed"));
            Assert.That(cancelled.Events.Single().PresentationKey,
                Does.EndWith(".cancelled"));
            Assert.That(skipped.Events, Is.Empty);
        }

        [Test]
        public void M06_008_ChildProfileSafetyRejectsPaidRewardedSilentAndUnconfirmedSpend()
        {
            var child = BoosterProfilePolicy.ChildSafe();
            Assert.DoesNotThrow(() => BoosterSafetyPolicy.ValidateUse(
                child, BoosterAcquisitionSource.Owned, true, false));
            Assert.DoesNotThrow(() => BoosterSafetyPolicy.ValidateUse(
                child, BoosterAcquisitionSource.FreeReward, true, false));

            Assert.Throws<InvalidOperationException>(() =>
                BoosterSafetyPolicy.ValidateUse(
                    child, BoosterAcquisitionSource.Paid, true, false));
            Assert.Throws<InvalidOperationException>(() =>
                BoosterSafetyPolicy.ValidateUse(
                    child, BoosterAcquisitionSource.RewardedAd, true, false));
            Assert.Throws<InvalidOperationException>(() =>
                BoosterSafetyPolicy.ValidateUse(
                    child, BoosterAcquisitionSource.Owned, false, false));
            Assert.Throws<InvalidOperationException>(() =>
                BoosterSafetyPolicy.ValidateUse(
                    child, BoosterAcquisitionSource.Owned, true, true));
        }

        [Test]
        public void M06_009_AdvancedBoostersShareOneRequestLifecycleAndExactlyOnceChargePath()
        {
            var board = SpecialBoard();
            foreach (var kind in new[]
            {
                BoosterKind.HorizontalLine,
                BoosterKind.VerticalLine,
                BoosterKind.Area,
                BoosterKind.Color,
                BoosterKind.TargetedMissile
            })
            {
                var id = "advanced-" + kind;
                var request = new BoosterActionRequest(
                    id, kind, P(1,1),
                    kind == BoosterKind.TargetedMissile
                        ? (BoardPosition?)P(0,0) : null,
                    radius: 1,
                    targetColor: kind == BoosterKind.Color ? "red" : null,
                    seed: 17u);
                var economy = Economy(kind, 1);
                var adult = new BoosterProfilePolicy(
                    false, true, true, true);
                var prepared = BoosterActionLifecycle.PrepareEffect(
                    economy, adult, BoosterAcquisitionSource.Owned, true,
                    board, request);
                Assert.That(prepared.EffectPlan.CanonicalSpecialPlan, Is.Not.Null);
                Assert.That(prepared.ReservedEconomy.Available(kind), Is.EqualTo(0));

                var committed = BoosterActionLifecycle.Commit(
                    prepared, prepared.ReservedEconomy,
                    new BoosterActionJournalSnapshot());
                Assert.That(committed.Economy.Balance(kind), Is.EqualTo(0));
                Assert.That(committed.Journal.AppliedActions.ContainsKey(id),
                    Is.True);
                Assert.That(BoosterEconomyPlanner.Confirm(
                    committed.Economy, id).Receipt.Applied, Is.False);
            }

            var shuffleEconomy = Economy(BoosterKind.Shuffle, 1);
            var shuffleAdult = new BoosterProfilePolicy(
                false, true, true, true);
            var transformBoard = new BoardTopology(1, 3,
                occupants: new[]
                {
                    new BoardOccupant(P(0,0), "A"),
                    new BoardOccupant(P(0,1), "B"),
                    new BoardOccupant(P(0,2), "C")
                });
            var shufflePrepared = BoosterActionLifecycle.PrepareTransform(
                shuffleEconomy, shuffleAdult,
                BoosterAcquisitionSource.Owned, true,
                transformBoard,
                new BoosterActionRequest(
                    "advanced-shuffle", BoosterKind.Shuffle, P(0,0),
                    seed: 17u));
            Assert.That(shufflePrepared.TransformPlan.Kind,
                Is.EqualTo(BoosterKind.Shuffle));
            Assert.That(shufflePrepared.ReservedEconomy.Available(
                BoosterKind.Shuffle), Is.EqualTo(0));
            var shuffleCommitted = BoosterActionLifecycle.Commit(
                shufflePrepared, shufflePrepared.ReservedEconomy,
                new BoosterActionJournalSnapshot());
            Assert.That(shuffleCommitted.Economy.Balance(BoosterKind.Shuffle),
                Is.EqualTo(0));
            Assert.That(shuffleCommitted.Journal.AppliedActions
                .ContainsKey("advanced-shuffle"), Is.True);
        }

        [Test]
        public void M06_010_InterruptionInvalidatesStaleCommitAndRecoveryRefundsExactlyOnce()
        {
            var economy = Economy(BoosterKind.Hammer, 1);
            var reserved = BoosterEconomyPlanner.Reserve(
                economy, BoosterKind.Hammer, "interrupt-action");
            var committed = BoosterEconomyPlanner.Confirm(
                reserved.Result, "interrupt-action");
            Assert.That(committed.Result.Balance(BoosterKind.Hammer), Is.EqualTo(0));

            var gate = new BoosterActionGate();
            var stale = gate.Begin("interrupt-action");
            Assert.That(gate.Pause(), Is.True);
            Assert.That(gate.TryCommit(stale), Is.False);
            Assert.That(gate.PendingCount, Is.EqualTo(0));
            Assert.That(gate.Resume(), Is.True);

            var recovered = BoosterRecoveryPlanner.RecoverReservation(
                committed.Result, "interrupt-action");
            Assert.That(recovered.Result.Balance(BoosterKind.Hammer), Is.EqualTo(1));
            var replayRecovery = BoosterRecoveryPlanner.RecoverReservation(
                recovered.Result, "interrupt-action");
            Assert.That(replayRecovery.Receipt.Applied, Is.False);
            Assert.That(replayRecovery.Result.Balance(BoosterKind.Hammer),
                Is.EqualTo(1));

            var fresh = gate.Begin("fresh-action");
            gate.Interrupt();
            Assert.That(gate.TryCommit(fresh), Is.False);

            var guardedBoard = SpecialBoard();
            var adultPolicy = new BoosterProfilePolicy(false, true, true, true);
            var guardedPrepared = BoosterActionLifecycle.PrepareEffect(
                Economy(BoosterKind.Hammer, 1),
                adultPolicy, BoosterAcquisitionSource.Owned, true,
                guardedBoard,
                new BoosterActionRequest(
                    "guarded-action", BoosterKind.Hammer, P(0,0)));
            var guardedGate = new BoosterActionGate();
            var guardedToken = guardedGate.Begin("guarded-action");
            guardedGate.Interrupt();
            Assert.Throws<OperationCanceledException>(() =>
                BoosterActionLifecycle.CommitGuarded(
                    guardedPrepared, guardedPrepared.ReservedEconomy,
                    new BoosterActionJournalSnapshot(),
                    guardedGate, guardedToken));
            Assert.That(guardedPrepared.ReservedEconomy
                .Balance(BoosterKind.Hammer), Is.EqualTo(1));
            Assert.That(guardedPrepared.ReservedEconomy
                .Transactions["guarded-action"].State,
                Is.EqualTo(BoosterTransactionState.Reserved));

            var currentPrepared = BoosterActionLifecycle.PrepareEffect(
                Economy(BoosterKind.Hammer, 1),
                adultPolicy, BoosterAcquisitionSource.Owned, true,
                guardedBoard,
                new BoosterActionRequest(
                    "current-action", BoosterKind.Hammer, P(0,0)));
            var currentGate = new BoosterActionGate();
            var currentToken = currentGate.Begin("current-action");
            var guardedCommit = BoosterActionLifecycle.CommitGuarded(
                currentPrepared, currentPrepared.ReservedEconomy,
                new BoosterActionJournalSnapshot(),
                currentGate, currentToken);
            Assert.That(guardedCommit.Economy.Balance(BoosterKind.Hammer),
                Is.EqualTo(0));
            Assert.That(guardedCommit.Journal.AppliedActions
                .ContainsKey("current-action"), Is.True);

            var board = SpecialBoard();
            var adult = new BoosterProfilePolicy(false, true, true, true);
            var prepared = BoosterActionLifecycle.PrepareEffect(
                Economy(BoosterKind.Hammer, 1),
                adult, BoosterAcquisitionSource.Owned, true,
                board,
                new BoosterActionRequest(
                    "journal-action", BoosterKind.Hammer, P(0,0)));
            var journal = new BoosterActionJournalSnapshot();
            var firstCommit = BoosterActionLifecycle.Commit(
                prepared, prepared.ReservedEconomy, journal);
            Assert.That(firstCommit.Economy.Balance(BoosterKind.Hammer),
                Is.EqualTo(0));
            Assert.That(firstCommit.JournalChanged, Is.True);

            var replayCommit = BoosterActionLifecycle.Commit(
                prepared, firstCommit.Economy, firstCommit.Journal);
            Assert.That(replayCommit.EconomyChanged, Is.False);
            Assert.That(replayCommit.JournalChanged, Is.False);
            Assert.That(replayCommit.Economy.Balance(BoosterKind.Hammer),
                Is.EqualTo(0));

            Assert.Throws<InvalidOperationException>(() =>
                BoosterActionJournalPlanner.RecordApplied(
                    firstCommit.Journal, "journal-action",
                    new string('f', 64)));
        }

        [Test]
        public void M06_011_ScoreToPowerIsAlternativeModeOnlyBoundedAndReplayProof()
        {
            var standard = new ScorePowerSnapshot(
                ScorePowerMode.StandardCampaign, 0, 10);
            var ignored = ScorePowerPlanner.Earn(
                standard, "score-1", 1000, 100);
            Assert.That(ignored.Applied, Is.False);
            Assert.That(ignored.Result.Meter, Is.EqualTo(0));
            Assert.Throws<InvalidOperationException>(() =>
                ScorePowerPlanner.Consume(standard, "use-1", 1));

            var timed = new ScorePowerSnapshot(
                ScorePowerMode.Timed, 0, 5);
            var earned = ScorePowerPlanner.Earn(
                timed, "score-1", 1000, 100);
            Assert.That(earned.Result.Meter, Is.EqualTo(5));
            Assert.That(earned.Delta, Is.EqualTo(5));

            var replayEarn = ScorePowerPlanner.Earn(
                earned.Result, "score-1", 1000, 100);
            Assert.That(replayEarn.Applied, Is.False);
            Assert.That(replayEarn.Result.Meter, Is.EqualTo(5));

            var consumed = ScorePowerPlanner.Consume(
                earned.Result, "use-1", 3);
            Assert.That(consumed.Result.Meter, Is.EqualTo(2));
            Assert.That(ScorePowerPlanner.Consume(
                consumed.Result, "use-1", 3).Applied, Is.False);
            Assert.Throws<InvalidOperationException>(() =>
                ScorePowerPlanner.Consume(
                    consumed.Result, "use-2", 3));

            var publicNames = typeof(ScorePowerSnapshot)
                .GetProperties()
                .Select(x => x.Name)
                .Concat(typeof(ScorePowerPlanner).GetMethods()
                    .Where(x => x.IsPublic && x.IsStatic)
                    .Select(x => x.Name))
                .Select(x => x.ToLowerInvariant())
                .ToArray();
            Assert.That(publicNames.Any(x =>
                x.Contains("paid") ||
                x.Contains("premium") ||
                x.Contains("currency") ||
                x.Contains("purchase")), Is.False);
        }
    }
}
