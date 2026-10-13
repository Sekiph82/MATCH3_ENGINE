using System;
using System.Collections.Generic;
using System.Linq;
using Match3.Engine.Runtime;
using NUnit.Framework;

namespace Match3.Engine.Tests
{
    public sealed class EngineAuthorityContractTests
    {
        private static readonly EngineVersion V1 = new EngineVersion(1, 0, 0);

        private sealed class Unit : IPluginUnit
        {
            public Unit(string id) { Id = id; }
            public string Id { get; }
        }

        private sealed class Factory : IPluginFactory
        {
            private readonly string _id;
            public Factory(string id) { _id = id; }
            public IPluginUnit Create() { return new Unit(_id); }
        }

        [Test]
        public void EnabledPluginFactoryCreatesNeutralUnitWithoutOwningBoard()
        {
            var profile = new GameProfile("Coral_Rescue", "placeholder.coral", ProfileAgeRule.General,
                ProfileContentChannel.Development, new[] { new FeatureFlag("collection", V1) });
            var registry = new PluginRegistry();
            registry.Register(new PluginDescriptor(PluginKind.Goal, "goal.collection", "collection",
                V1, new Factory("goal.collection")));

            var result = registry.Create(profile, PluginKind.Goal, "goal.collection");
            Assert.That(result.Id, Is.EqualTo("goal.collection"));
            Assert.That(registry.Count, Is.EqualTo(1));
        }

        [Test]
        public void DisabledPluginAndWrongKindCannotResolveFactory()
        {
            var disabled = GameProfileCatalog.LoadPlaceholders()[0];
            var enabled = new GameProfile("Coral_Rescue", "placeholder.coral", ProfileAgeRule.General,
                ProfileContentChannel.Development, new[] { new FeatureFlag("egg", V1) });
            var registry = new PluginRegistry();
            registry.Register(new PluginDescriptor(PluginKind.Obstacle, "obstacle.egg", "egg",
                V1, new Factory("obstacle.egg")));
            Assert.Throws<InvalidOperationException>(() =>
                registry.Create(disabled, PluginKind.Obstacle, "obstacle.egg"));
            Assert.Throws<InvalidOperationException>(() =>
                registry.Create(enabled, PluginKind.Booster, "obstacle.egg"));
            Assert.Throws<KeyNotFoundException>(() =>
                registry.Create(enabled, PluginKind.Obstacle, "missing"));
        }

        [Test]
        public void DuplicatePluginIdAndIncorrectFactoryOutputFailClosed()
        {
            var registry = new PluginRegistry();
            registry.Register(new PluginDescriptor(PluginKind.Goal, "duplicate", "collection",
                V1, new Factory("duplicate")));
            Assert.Throws<InvalidOperationException>(() =>
                registry.Register(new PluginDescriptor(PluginKind.Booster, "duplicate", "collection",
                    V1, new Factory("duplicate"))));
            Assert.Throws<ArgumentException>(() =>
                new PluginDescriptor(PluginKind.Special, "s", "unapproved", V1, new Factory("s")));
            Assert.Throws<ArgumentNullException>(() =>
                new PluginDescriptor(PluginKind.Goal, "x", "collection", V1, null));

            registry.Register(new PluginDescriptor(PluginKind.Special, "wrong.output", "egg",
                V1, new Factory("different.output")));
            var enabled = new GameProfile("Wonder_Pop", "placeholder.wonder",
                ProfileAgeRule.General, ProfileContentChannel.Development,
                new[] { new FeatureFlag("egg", V1) });
            Assert.Throws<InvalidOperationException>(() =>
                registry.Create(enabled, PluginKind.Special, "wrong.output"));
        }

        [Test]
        public void EventStreamIsOrderedReadOnlyAndDoesNotLetObserverFailuresAlterAuthority()
        {
            var journal = new GameplayEventJournal();
            var seen = new List<long>();
            journal.Subscribe(e => { throw new InvalidOperationException("provider unavailable"); });
            journal.Subscribe(e => seen.Add(e.Sequence));
            journal.Append(new GameplayEvent(1, GameplayEventKind.Swap, "1,2"));
            journal.Append(new GameplayEvent(2, GameplayEventKind.Clear, "3"));
            Assert.That(seen, Is.EqualTo(new long[] { 1, 2 }));
            Assert.That(journal.ObserverFailures, Is.EqualTo(2));
            Assert.That(journal.Events.Count, Is.EqualTo(2));
            Assert.That(journal.Events[0].Payload, Is.EqualTo("1,2"));
            Assert.That(typeof(GameplayEvent).GetProperty("Payload").CanWrite, Is.False);
            var list = (IList<GameplayEvent>)journal.Events;
            Assert.Throws<NotSupportedException>(() =>
                list.Add(new GameplayEvent(3, GameplayEventKind.Spawn, "X")));
            Assert.That(journal.Events.Count, Is.EqualTo(2));
        }

        [Test]
        public void EventProtocolRejectsOutOfOrderAndAnythingAfterWinOrLoss()
        {
            var journal = new GameplayEventJournal();
            Assert.Throws<InvalidOperationException>(() =>
                journal.Append(new GameplayEvent(2, GameplayEventKind.Clear, "bad")));
            journal.Append(new GameplayEvent(1, GameplayEventKind.Won, "ok"));
            Assert.Throws<InvalidOperationException>(() =>
                journal.Append(new GameplayEvent(2, GameplayEventKind.Reward, "unearned")));
            Assert.That(journal.Events.Count, Is.EqualTo(1));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new GameplayEvent(0, GameplayEventKind.Swap, ""));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new GameplayEvent(1, (GameplayEventKind)99, ""));
        }

        [Test]
        public void StateMachineSupportsOnlyAuthorizedLifecycleAndTerminalOnce()
        {
            var state = new EngineStateMachine();
            Assert.That(state.Phase, Is.EqualTo(EnginePhase.Boot));
            Assert.Throws<InvalidOperationException>(() =>
                state.TryAdvance(state.Generation, EnginePhase.Won));
            Assert.That(state.TryAdvance(0, EnginePhase.Loading), Is.True);
            Assert.That(state.TryAdvance(0, EnginePhase.Ready), Is.True);
            Assert.That(state.TryAdvance(0, EnginePhase.Swapping), Is.True);
            Assert.That(state.TryAdvance(0, EnginePhase.Resolving), Is.True);
            Assert.That(state.TryAdvance(0, EnginePhase.Won), Is.True);
            Assert.Throws<InvalidOperationException>(() =>
                state.TryAdvance(state.Generation, EnginePhase.Ready));
            Assert.Throws<InvalidOperationException>(() => state.CancelPending());
            Assert.That(typeof(IReadOnlyGamePhase).GetProperty("Phase").CanWrite, Is.False);
        }

        [Test]
        public void PauseResumeAndCancellationFenceDiscardStaleAsyncCompletion()
        {
            var state = new EngineStateMachine();
            state.TryAdvance(0, EnginePhase.Loading);
            state.TryAdvance(0, EnginePhase.Ready);
            state.TryAdvance(0, EnginePhase.Swapping);
            state.Pause();
            Assert.That(state.Phase, Is.EqualTo(EnginePhase.Paused));
            Assert.Throws<InvalidOperationException>(() =>
                state.TryAdvance(0, EnginePhase.Resolving));
            state.Resume();
            Assert.That(state.Phase, Is.EqualTo(EnginePhase.Swapping));
            var oldGeneration = state.Generation;
            var newGeneration = state.CancelPending();
            Assert.That(newGeneration, Is.GreaterThan(oldGeneration));
            Assert.That(state.Phase, Is.EqualTo(EnginePhase.Ready));
            Assert.That(state.TryAdvance(oldGeneration, EnginePhase.Resolving), Is.False);
            Assert.That(state.TryAdvance(newGeneration, EnginePhase.Swapping), Is.True);
            Assert.Throws<InvalidOperationException>(() => state.Resume());
        }
    }
}
