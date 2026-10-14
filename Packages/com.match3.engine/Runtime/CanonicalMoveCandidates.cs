using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Match3.Engine.Runtime
{
    public readonly struct SwapCandidate : IEquatable<SwapCandidate>
    {
        public BoardPosition Source { get; }
        public BoardPosition Target { get; }

        public SwapCandidate(BoardPosition source, BoardPosition target)
        {
            Source = source;
            Target = target;
        }

        public bool Equals(SwapCandidate other) =>
            Source.Equals(other.Source) && Target.Equals(other.Target);
        public override bool Equals(object other) =>
            other is SwapCandidate candidate && Equals(candidate);
        public override int GetHashCode()
        {
            unchecked { return (Source.GetHashCode() * 397) ^ Target.GetHashCode(); }
        }
    }

    public sealed class CandidateScanResult
    {
        public IReadOnlyList<SwapCandidate> Accepted { get; }
        public int CandidatesChecked { get; }
        public bool HasMoves => Accepted.Count > 0;

        internal CandidateScanResult(List<SwapCandidate> accepted, int checkedCount)
        {
            Accepted = new ReadOnlyCollection<SwapCandidate>(accepted.ToArray());
            CandidatesChecked = checkedCount;
        }
    }

    // Enumerates possible *geometrical* swaps only. The supplied predicate
    // MUST be wired to the pinned Match3-SDK's authoritative match/special
    // validator by the host. No custom color matching occurs here.
    public static class CanonicalMoveCandidates
    {
        public static CandidateScanResult Find(
            BoardTopology topology,
            Func<SwapCandidate, bool> sdkAuthoritativeValidator,
            int maximumCandidates = 32768)
        {
            if (topology == null) throw new ArgumentNullException(nameof(topology));
            if (sdkAuthoritativeValidator == null)
                throw new ArgumentNullException(nameof(sdkAuthoritativeValidator));
            if (maximumCandidates < 1)
                throw new ArgumentOutOfRangeException(nameof(maximumCandidates));

            var accepted = new List<SwapCandidate>();
            var count = 0;
            foreach (var from in topology.ActivePositions())
            {
                // Stable order: layer, row, column, then RIGHT followed by DOWN.
                // Never report both directions for the same edge.
                if (from.Column + 1 < topology.Columns)
                    Check(new SwapCandidate(from,
                        new BoardPosition(from.Row, from.Column + 1, from.Layer)));
                if (from.Row + 1 < topology.Rows)
                    Check(new SwapCandidate(from,
                        new BoardPosition(from.Row + 1, from.Column, from.Layer)));
            }
            return new CandidateScanResult(accepted, count);

            void Check(SwapCandidate candidate)
            {
                if (!topology.IsSwapCandidate(candidate.Source, candidate.Target))
                    return;
                if (++count > maximumCandidates)
                    throw new InvalidOperationException("Candidate scan budget exceeded.");
                // Throwing/absent SDK validation is deliberately fail-closed.
                if (sdkAuthoritativeValidator(candidate)) accepted.Add(candidate);
            }
        }
    }
}
