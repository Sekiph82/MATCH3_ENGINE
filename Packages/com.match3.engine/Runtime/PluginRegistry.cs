using System;
using System.Collections.Generic;

namespace Match3.Engine.Runtime
{
    public enum PluginKind { Obstacle = 0, Goal = 1, Special = 2, Booster = 3 }

    // Plugins produce domain-neutral instances. Factories are supplied by a
    // host adapter, not by the pure model or the solver inside this assembly.
    public interface IPluginUnit
    {
        string Id { get; }
    }

    public interface IPluginFactory
    {
        IPluginUnit Create();
    }

    public sealed class PluginDescriptor
    {
        public PluginKind Kind { get; }
        public string Id { get; }
        public string CapabilityId { get; }
        public EngineVersion Version { get; }
        public IPluginFactory Factory { get; }

        public PluginDescriptor(PluginKind kind, string id, string capabilityId,
            EngineVersion version, IPluginFactory factory)
        {
            if (!Enum.IsDefined(typeof(PluginKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Plugin ID is required.", nameof(id));
            if (!FeatureCatalog.IsKnown(capabilityId))
                throw new ArgumentException("Unknown plugin capability.", nameof(capabilityId));
            EngineCompatibility.RequireCapabilityVersion(version);
            Kind = kind;
            Id = id;
            CapabilityId = capabilityId;
            Version = version;
            Factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }
    }

    public sealed class PluginRegistry
    {
        private readonly Dictionary<string, PluginDescriptor> _registered =
            new Dictionary<string, PluginDescriptor>(StringComparer.Ordinal);

        public int Count => _registered.Count;

        // A global ID identifies exactly one plugin, including its kind.
        public void Register(PluginDescriptor descriptor)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            if (_registered.ContainsKey(descriptor.Id))
                throw new InvalidOperationException("Plugin ID collision: " + descriptor.Id);
            _registered.Add(descriptor.Id, descriptor);
        }

        // A disabled capability is NOT secretly instantiable. Registry lookup
        // cannot mutate or inspect a live SDK board.
        public IPluginUnit Create(GameProfile profile, PluginKind kind, string id)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (!_registered.TryGetValue(id ?? string.Empty, out var descriptor))
                throw new KeyNotFoundException("Plugin is not registered: " + id);
            if (descriptor.Kind != kind)
                throw new InvalidOperationException("Plugin kind mismatch.");
            if (!profile.IsEnabled(descriptor.CapabilityId, descriptor.Version))
                throw new InvalidOperationException("Plugin capability is not enabled.");
            var unit = descriptor.Factory.Create();
            if (unit == null || unit.Id != descriptor.Id)
                throw new InvalidOperationException("Plugin factory returned an invalid instance.");
            return unit;
        }
    }
}
