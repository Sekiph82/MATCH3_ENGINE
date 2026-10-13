using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Match3.Engine.Runtime
{
    // These events describe resolved authoritative outcomes. They do NOT
    // authorize a renderer, ad SDK or analytics adapter to mutate the game.
    public enum GameplayEventKind
    {
        Swap = 0, Clear = 1, Spawn = 2, Cascade = 3,
        Won = 4, Lost = 5, Reward = 6
    }

    public sealed class GameplayEvent
    {
        public long Sequence { get; }
        public GameplayEventKind Kind { get; }
        public string Payload { get; }

        public GameplayEvent(long sequence, GameplayEventKind kind, string payload)
        {
            if (sequence < 1) throw new ArgumentOutOfRangeException(nameof(sequence));
            if (!Enum.IsDefined(typeof(GameplayEventKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            Sequence = sequence;
            Kind = kind;
            Payload = payload ?? string.Empty;
        }
    }

    public interface IReadOnlyEventHistory
    {
        IReadOnlyList<GameplayEvent> Events { get; }
    }

    public sealed class GameplayEventJournal : IReadOnlyEventHistory
    {
        private readonly List<GameplayEvent> _events = new List<GameplayEvent>();
        private readonly ReadOnlyCollection<GameplayEvent> _readOnlyEvents;
        private readonly List<Action<GameplayEvent>> _subscribers = new List<Action<GameplayEvent>>();
        private bool _terminal;

        public GameplayEventJournal()
        {
            _readOnlyEvents = _events.AsReadOnly();
        }

        public IReadOnlyList<GameplayEvent> Events => _readOnlyEvents;
        public int ObserverFailures { get; private set; }

        public void Subscribe(Action<GameplayEvent> observer)
        {
            if (observer == null) throw new ArgumentNullException(nameof(observer));
            _subscribers.Add(observer);
        }

        public void Unsubscribe(Action<GameplayEvent> observer)
        {
            _subscribers.Remove(observer);
        }

        // Internal: only code in the authoritative engine assembly can publish.
        // Presentation and third-party callbacks only receive IReadOnlyEventHistory.
        internal void Append(GameplayEvent next)
        {
            if (next == null) throw new ArgumentNullException(nameof(next));
            if (_terminal) throw new InvalidOperationException("No events after terminal outcome.");
            if (next.Sequence != _events.Count + 1)
                throw new InvalidOperationException("Event sequence must be contiguous and monotonic.");
            _events.Add(next);
            if (next.Kind == GameplayEventKind.Won || next.Kind == GameplayEventKind.Lost)
                _terminal = true;

            // A broken observer must never rewind or invalidate the already
            // committed authoritative event sequence.
            foreach (var observer in _subscribers.ToArray())
            {
                try { observer(next); }
                catch (Exception) { ObserverFailures++; }
            }
        }
    }

    public enum EnginePhase
    {
        Boot = 0, Loading = 1, Ready = 2, Swapping = 3,
        Resolving = 4, Paused = 5, Won = 6, Lost = 7
    }

    public interface IReadOnlyGamePhase
    {
        EnginePhase Phase { get; }
        int Generation { get; }
    }

    public sealed class EngineStateMachine : IReadOnlyGamePhase
    {
        private EnginePhase _phase = EnginePhase.Boot;
        private EnginePhase _resumePhase;
        private int _generation;

        public EnginePhase Phase => _phase;
        public int Generation => _generation;

        // Returned false means an async result belongs to an obsolete run.
        internal bool TryAdvance(int observedGeneration, EnginePhase next)
        {
            if (observedGeneration != _generation) return false;
            if (!Enum.IsDefined(typeof(EnginePhase), next))
                throw new ArgumentOutOfRangeException(nameof(next));
            if (!IsLegal(_phase, next))
                throw new InvalidOperationException("Illegal engine transition: " + _phase + " to " + next);
            _phase = next;
            return true;
        }

        internal void Pause()
        {
            if (_phase != EnginePhase.Ready && _phase != EnginePhase.Swapping &&
                _phase != EnginePhase.Resolving)
                throw new InvalidOperationException("Cannot pause this phase.");
            _resumePhase = _phase;
            _phase = EnginePhase.Paused;
        }

        internal void Resume()
        {
            if (_phase != EnginePhase.Paused)
                throw new InvalidOperationException("Engine is not paused.");
            _phase = _resumePhase;
        }

        internal int CancelPending()
        {
            if (_phase == EnginePhase.Won || _phase == EnginePhase.Lost)
                throw new InvalidOperationException("Cannot reopen a terminal game.");
            checked { _generation++; }
            if (_phase == EnginePhase.Swapping || _phase == EnginePhase.Resolving ||
                _phase == EnginePhase.Paused)
                _phase = EnginePhase.Ready;
            return _generation;
        }

        private static bool IsLegal(EnginePhase before, EnginePhase after)
        {
            switch (before)
            {
                case EnginePhase.Boot: return after == EnginePhase.Loading;
                case EnginePhase.Loading: return after == EnginePhase.Ready;
                case EnginePhase.Ready: return after == EnginePhase.Swapping;
                case EnginePhase.Swapping: return after == EnginePhase.Resolving;
                case EnginePhase.Resolving:
                    return after == EnginePhase.Ready ||
                           after == EnginePhase.Won || after == EnginePhase.Lost;
                default: return false;
            }
        }
    }
}
