using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Match3.Engine.Runtime
{
    public sealed class PresentationPluginManifest
    {
        public string PluginId { get; }
        public string Version { get; }
        public string LicenseId { get; }
        public bool Optional { get; }
        public bool GameplayAuthority { get; }
        public bool RewardAuthority { get; }
        public bool SaveAuthority { get; }

        public PresentationPluginManifest(string pluginId,
            string version, string licenseId, bool optional,
            bool gameplayAuthority = false,
            bool rewardAuthority = false,
            bool saveAuthority = false)
        {
            if (string.IsNullOrWhiteSpace(pluginId))
                throw new ArgumentException(
                    "Presentation plugin ID required.", nameof(pluginId));
            if (string.IsNullOrWhiteSpace(version))
                throw new ArgumentException(
                    "Presentation plugin version lock required.",
                    nameof(version));
            if (string.IsNullOrWhiteSpace(licenseId))
                throw new ArgumentException(
                    "Presentation plugin license record required.",
                    nameof(licenseId));
            if (gameplayAuthority || rewardAuthority || saveAuthority)
                throw new ArgumentException(
                    "Presentation plugin cannot own gameplay, reward or save authority.");

            PluginId = pluginId;
            Version = version;
            LicenseId = licenseId;
            Optional = optional;
            GameplayAuthority = gameplayAuthority;
            RewardAuthority = rewardAuthority;
            SaveAuthority = saveAuthority;
        }
    }

    public sealed class PresentationPluginResolution
    {
        public PresentationPluginManifest Manifest { get; }
        public bool Available { get; }
        public bool UsesFallback { get; }
        public string Warning { get; }

        internal PresentationPluginResolution(
            PresentationPluginManifest manifest,
            bool available, bool usesFallback, string warning)
        {
            Manifest = manifest;
            Available = available;
            UsesFallback = usesFallback;
            Warning = warning;
        }
    }

    public static class PresentationPluginResolver
    {
        public static PresentationPluginResolution Resolve(
            PresentationPluginManifest manifest,
            IEnumerable<KeyValuePair<string, string>> installedVersions)
        {
            if (manifest == null)
                throw new ArgumentNullException(nameof(manifest));
            if (installedVersions == null)
                throw new ArgumentNullException(nameof(installedVersions));

            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in installedVersions)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) ||
                    string.IsNullOrWhiteSpace(pair.Value) ||
                    map.ContainsKey(pair.Key))
                    throw new ArgumentException(
                        "Invalid installed plugin version map.",
                        nameof(installedVersions));
                if (map.Count >= 256)
                    throw new ArgumentException(
                        "Installed plugin version map exceeds bound.");
                map.Add(pair.Key, pair.Value);
            }

            string installed;
            if (!map.TryGetValue(manifest.PluginId, out installed))
            {
                if (!manifest.Optional)
                    throw new InvalidOperationException(
                        "Required presentation plugin is absent.");
                return new PresentationPluginResolution(
                    manifest, false, true,
                    "plugin-missing:" + manifest.PluginId);
            }

            if (!StringComparer.Ordinal.Equals(
                installed, manifest.Version))
            {
                if (!manifest.Optional)
                    throw new InvalidOperationException(
                        "Required presentation plugin version mismatch.");
                return new PresentationPluginResolution(
                    manifest, false, true,
                    "plugin-version-mismatch:" + manifest.PluginId);
            }

            return new PresentationPluginResolution(
                manifest, true, false, null);
        }
    }

    public sealed class PresentationPluginShippingReport
    {
        private readonly ReadOnlyCollection<string> _violations;

        public IReadOnlyList<string> Violations => _violations;
        public bool Pass => _violations.Count == 0;

        internal PresentationPluginShippingReport(
            IEnumerable<string> violations)
        {
            _violations = new ReadOnlyCollection<string>(
                violations.OrderBy(x => x, StringComparer.Ordinal).ToList());
        }
    }

    public static class PresentationPluginShippingScanner
    {
        public static PresentationPluginShippingReport Scan(
            IEnumerable<string> packagePaths)
        {
            if (packagePaths == null)
                throw new ArgumentNullException(nameof(packagePaths));

            var paths = packagePaths.Take(10001).ToArray();
            if (paths.Length > 10000 ||
                paths.Any(string.IsNullOrWhiteSpace))
                throw new ArgumentException(
                    "Invalid plugin shipping path inventory.",
                    nameof(packagePaths));

            var violations = new List<string>();
            foreach (var raw in paths)
            {
                var normalized = raw.Replace('\\', '/');
                var segments = normalized.Split('/');
                if (segments.Any(x =>
                        StringComparer.OrdinalIgnoreCase.Equals(x, "Editor") ||
                        StringComparer.OrdinalIgnoreCase.Equals(x, "Examples") ||
                        StringComparer.OrdinalIgnoreCase.Equals(x, "Examples~") ||
                        StringComparer.OrdinalIgnoreCase.Equals(x, "Samples") ||
                        StringComparer.OrdinalIgnoreCase.Equals(x, "Samples~") ||
                        StringComparer.OrdinalIgnoreCase.Equals(x, "Debug")))
                    violations.Add(normalized);
            }

            return new PresentationPluginShippingReport(violations);
        }
    }

    public enum SourceArtKind
    {
        RasterAtlas = 0,
        ScalableVector = 1
    }

    public enum SourceArtLicenseClass
    {
        Original = 0,
        MIT = 1,
        CommercialLicensed = 2,
        LgplQmlReferenceOnly = 3,
        Unknown = 4
    }

    public sealed class SourceArtDescriptor
    {
        public string SourceId { get; }
        public SourceArtKind Kind { get; }
        public SourceArtLicenseClass LicenseClass { get; }
        public int SourceWidth { get; }
        public int SourceHeight { get; }
        public string ShapeKey { get; }
        public string AccessibilityKey { get; }
        public long EstimatedRuntimeBytes { get; }
        public bool ContainsExecutableOrQmlCode { get; }

        public SourceArtDescriptor(string sourceId,
            SourceArtKind kind, SourceArtLicenseClass licenseClass,
            int sourceWidth, int sourceHeight,
            string shapeKey, string accessibilityKey,
            long estimatedRuntimeBytes,
            bool containsExecutableOrQmlCode = false)
        {
            if (string.IsNullOrWhiteSpace(sourceId))
                throw new ArgumentException(
                    "Source-art ID required.", nameof(sourceId));
            if (!Enum.IsDefined(typeof(SourceArtKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (!Enum.IsDefined(typeof(SourceArtLicenseClass), licenseClass))
                throw new ArgumentOutOfRangeException(nameof(licenseClass));
            if (sourceWidth < 1 || sourceWidth > 32768 ||
                sourceHeight < 1 || sourceHeight > 32768)
                throw new ArgumentOutOfRangeException(nameof(sourceWidth));
            if (string.IsNullOrWhiteSpace(shapeKey))
                throw new ArgumentException(
                    "Source-art shape key required.", nameof(shapeKey));
            if (string.IsNullOrWhiteSpace(accessibilityKey))
                throw new ArgumentException(
                    "Source-art accessibility key required.",
                    nameof(accessibilityKey));
            if (estimatedRuntimeBytes < 1 ||
                estimatedRuntimeBytes > 1024L * 1024L * 1024L)
                throw new ArgumentOutOfRangeException(
                    nameof(estimatedRuntimeBytes));

            SourceId = sourceId;
            Kind = kind;
            LicenseClass = licenseClass;
            SourceWidth = sourceWidth;
            SourceHeight = sourceHeight;
            ShapeKey = shapeKey;
            AccessibilityKey = accessibilityKey;
            EstimatedRuntimeBytes = estimatedRuntimeBytes;
            ContainsExecutableOrQmlCode = containsExecutableOrQmlCode;
        }
    }

    public sealed class SourceArtPolicy
    {
        private readonly ReadOnlyCollection<SourceArtLicenseClass>
            _allowedLicenses;

        public long MaximumRuntimeBytes { get; }
        public double MaximumRasterUpscale { get; }
        public IReadOnlyList<SourceArtLicenseClass> AllowedLicenses =>
            _allowedLicenses;

        public SourceArtPolicy(long maximumRuntimeBytes,
            double maximumRasterUpscale,
            IEnumerable<SourceArtLicenseClass> allowedLicenses)
        {
            if (maximumRuntimeBytes < 1 ||
                maximumRuntimeBytes > 1024L * 1024L * 1024L)
                throw new ArgumentOutOfRangeException(
                    nameof(maximumRuntimeBytes));
            if (double.IsNaN(maximumRasterUpscale) ||
                double.IsInfinity(maximumRasterUpscale) ||
                maximumRasterUpscale < 1 ||
                maximumRasterUpscale > 8)
                throw new ArgumentOutOfRangeException(
                    nameof(maximumRasterUpscale));
            if (allowedLicenses == null)
                throw new ArgumentNullException(nameof(allowedLicenses));

            var licenses = allowedLicenses.Distinct().ToArray();
            if (licenses.Length < 1 ||
                licenses.Any(x =>
                    !Enum.IsDefined(typeof(SourceArtLicenseClass), x) ||
                    x == SourceArtLicenseClass.LgplQmlReferenceOnly ||
                    x == SourceArtLicenseClass.Unknown))
                throw new ArgumentException(
                    "Source-art policy may allow only approved reusable licenses.",
                    nameof(allowedLicenses));

            MaximumRuntimeBytes = maximumRuntimeBytes;
            MaximumRasterUpscale = maximumRasterUpscale;
            _allowedLicenses =
                new ReadOnlyCollection<SourceArtLicenseClass>(
                    licenses.OrderBy(x => (int)x).ToList());
        }
    }

    public sealed class SourceArtRenderRequest
    {
        public SourceArtDescriptor Source { get; }
        public int TargetWidth { get; }
        public int TargetHeight { get; }

        public SourceArtRenderRequest(
            SourceArtDescriptor source,
            int targetWidth, int targetHeight)
        {
            Source = source ??
                throw new ArgumentNullException(nameof(source));
            if (targetWidth < 1 || targetWidth > 32768 ||
                targetHeight < 1 || targetHeight > 32768)
                throw new ArgumentOutOfRangeException(nameof(targetWidth));
            TargetWidth = targetWidth;
            TargetHeight = targetHeight;
        }
    }

    public sealed class SourceArtRenderPlan
    {
        public string SourceId { get; }
        public SourceArtKind Kind { get; }
        public double ScaleX { get; }
        public double ScaleY { get; }
        public string ShapeKey { get; }
        public string AccessibilityKey { get; }
        public bool Scalable { get; }
        public bool WithinRuntimeBudget { get; }

        internal SourceArtRenderPlan(SourceArtDescriptor source,
            double scaleX, double scaleY,
            bool withinRuntimeBudget)
        {
            SourceId = source.SourceId;
            Kind = source.Kind;
            ScaleX = scaleX;
            ScaleY = scaleY;
            ShapeKey = source.ShapeKey;
            AccessibilityKey = source.AccessibilityKey;
            Scalable = source.Kind == SourceArtKind.ScalableVector;
            WithinRuntimeBudget = withinRuntimeBudget;
        }
    }

    public static class SourceArtRenderAdapter
    {
        public static SourceArtRenderPlan Project(
            SourceArtPolicy policy,
            SourceArtRenderRequest request)
        {
            if (policy == null)
                throw new ArgumentNullException(nameof(policy));
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            var source = request.Source;
            if (!policy.AllowedLicenses.Contains(source.LicenseClass))
                throw new InvalidOperationException(
                    "Source-art license is not approved for reusable runtime use.");
            if (source.ContainsExecutableOrQmlCode)
                throw new InvalidOperationException(
                    "Presentation source-art adapter cannot import executable/QML code.");
            if (source.EstimatedRuntimeBytes > policy.MaximumRuntimeBytes)
                throw new InvalidOperationException(
                    "Source-art runtime byte budget exceeded.");

            var sourceAspect =
                (double)source.SourceWidth / source.SourceHeight;
            var targetAspect =
                (double)request.TargetWidth / request.TargetHeight;
            if (Math.Abs(sourceAspect - targetAspect) >
                Math.Max(0.000001, sourceAspect * 0.000001))
                throw new InvalidOperationException(
                    "Source-art render request would distort the authored shape.");

            var scaleX = (double)request.TargetWidth / source.SourceWidth;
            var scaleY = (double)request.TargetHeight / source.SourceHeight;
            if (source.Kind == SourceArtKind.RasterAtlas &&
                Math.Max(scaleX, scaleY) > policy.MaximumRasterUpscale)
                throw new InvalidOperationException(
                    "Raster source would exceed maximum upscale quality bound.");

            return new SourceArtRenderPlan(
                source, scaleX, scaleY, true);
        }
    }
}
