// ENG-M13-011 | External sample rights and architecture intake
// Policy catalog for the four evaluated external sources.
// No code from any external source is copied here; this is a pure policy record.
namespace Match3.Engine.Runtime.ExternalSamplePolicy
{
    /// <summary>License classification for an external sample source.</summary>
    public enum LicenseKind
    {
        /// <summary>License not identified; treat as proprietary.</summary>
        Unknown,
        /// <summary>No explicit license found; copyright retained by author.</summary>
        ProprietaryNoReuse,
        /// <summary>MIT license; reuse permitted with attribution after provenance review.</summary>
        MitAttribution,
        /// <summary>Creative Commons BY-NC-ND; NonCommercial + NoDerivatives prohibit game use.</summary>
        CCByNcNd,
        /// <summary>Unity Gaming Services sample; UGS Terms of Service review required before reuse.</summary>
        UgsReviewRequired,
    }

    /// <summary>Record for a single evaluated external source.</summary>
    public sealed class ExternalSource
    {
        public string Name { get; }
        public string SourceUrl { get; }
        public LicenseKind License { get; }
        public string Notes { get; }

        public ExternalSource(string name, string sourceUrl, LicenseKind license, string notes)
        {
            Name = name;
            SourceUrl = sourceUrl;
            License = license;
            Notes = notes;
        }
    }

    /// <summary>
    /// Audit helpers and catalog for ENG-M13-011 external sample intake policy.
    /// </summary>
    public static class ExternalSamplePolicyAudit
    {
        // ── Known source catalog ─────────────────────────────────────────────
        public static readonly ExternalSource[] Sources = new[]
        {
            new ExternalSource(
                name: "Unity GemHunterMatch3",
                sourceUrl: "https://assetstore.unity.com or UGS sample repository",
                license: LicenseKind.UgsReviewRequired,
                notes: "Reference-only; UGS Terms of Service governs. Confirm applicable license before any file reuse."
            ),
            new ExternalSource(
                name: "thefcan Match3 (GitHub, no explicit license)",
                sourceUrl: "https://github.com/thefcan",
                license: LicenseKind.ProprietaryNoReuse,
                notes: "No LICENSE file detected; copyright retained by author. Do not copy until explicit permissive license identified."
            ),
            new ExternalSource(
                name: "Alperen MIT Match3",
                sourceUrl: "https://github.com/alperentvs (record exact URL and commit SHA at reuse time)",
                license: LicenseKind.MitAttribution,
                notes: "MIT license; attribution required. Record author, URL, and license in every reused file."
            ),
            new ExternalSource(
                name: "CC BY-NC-ND Gem-Match3",
                sourceUrl: "CC BY-NC-ND licensed project (record exact URL at reuse time)",
                license: LicenseKind.CCByNcNd,
                notes: "NonCommercial + NoDerivatives prohibit game use. No code or images may be copied."
            ),
        };

        // ── Policy helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Returns true only for MIT-licensed sources where attribution is provided.
        /// All other license kinds are not approved for copy without further review.
        /// </summary>
        public static bool IsReuseAllowed(LicenseKind license) =>
            license == LicenseKind.MitAttribution;

        /// <summary>
        /// Returns the required policy steps before a source of this kind may be used.
        /// </summary>
        public static string[] GetRequirements(LicenseKind license)
        {
            switch (license)
            {
                case LicenseKind.MitAttribution:
                    return new[]
                    {
                        "Create TASKS.md entry with source URL, commit SHA, and specific files.",
                        "Record attribution plan: author name, URL, and MIT license notice in each reused file.",
                        "Owner approval in TASKS.md before commit.",
                    };
                case LicenseKind.UgsReviewRequired:
                    return new[]
                    {
                        "Contact Unity support to obtain written confirmation of applicable UGS license.",
                        "Record confirmed license terms and specific files in TASKS.md.",
                        "Owner approval required before any file is copied.",
                    };
                case LicenseKind.ProprietaryNoReuse:
                    return new[]
                    {
                        "Identify and document an explicit permissive license from the author.",
                        "Create TASKS.md entry with identified license and specific files.",
                        "Owner approval required before any file is copied.",
                    };
                case LicenseKind.CCByNcNd:
                    return new[]
                    {
                        "CC BY-NC-ND prohibits commercial use and derivative works.",
                        "No path to permitted reuse in a commercial game product exists under this license.",
                        "Do not copy any file. Seek an alternative source with a permissive license.",
                    };
                case LicenseKind.Unknown:
                default:
                    return new[]
                    {
                        "License is unknown; treat as proprietary.",
                        "Identify the license before any further evaluation.",
                        "Owner approval required before any file is copied.",
                    };
            }
        }
    }
}
