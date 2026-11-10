// ENG-M13-011 | External sample rights and architecture intake — EditMode tests
using Match3.Engine.Runtime.ExternalSamplePolicy;
using NUnit.Framework;

namespace Match3.Engine.Tests.Editor
{
    public sealed class ExternalSamplePolicyAuditTests
    {
        // ── 1. MIT attribution: only license kind approved for reuse ─────────
        [Test]
        public void MitAttribution_IsReuseAllowed_ReturnsTrue()
        {
            Assert.IsTrue(ExternalSamplePolicyAudit.IsReuseAllowed(LicenseKind.MitAttribution));
        }

        // ── 2. No explicit license (proprietary): reuse not allowed ──────────
        [Test]
        public void ProprietaryNoLicense_IsReuseAllowed_ReturnsFalse()
        {
            Assert.IsFalse(ExternalSamplePolicyAudit.IsReuseAllowed(LicenseKind.ProprietaryNoReuse));
        }

        // ── 3. CC BY-NC-ND: NonCommercial+NoDerivatives prohibit game use ────
        [Test]
        public void CCByNcNd_IsReuseAllowed_ReturnsFalse()
        {
            Assert.IsFalse(ExternalSamplePolicyAudit.IsReuseAllowed(LicenseKind.CCByNcNd));
        }

        // ── 4. UGS review required: not approved until license confirmed ──────
        [Test]
        public void UgsReviewRequired_IsReuseAllowed_ReturnsFalse()
        {
            Assert.IsFalse(ExternalSamplePolicyAudit.IsReuseAllowed(LicenseKind.UgsReviewRequired));
        }

        // ── 5. Known sources catalog: exactly 4 entries, none auto-approved ──
        [Test]
        public void KnownSources_CatalogHasFourEntries_NoneAutoApproved()
        {
            var sources = ExternalSamplePolicyAudit.Sources;
            Assert.AreEqual(4, sources.Length, "Catalog must list exactly 4 known sources.");
            foreach (var src in sources)
            {
                Assert.IsFalse(
                    ExternalSamplePolicyAudit.IsReuseAllowed(src.License) && src.License != LicenseKind.MitAttribution,
                    $"Source '{src.Name}' has an unexpected auto-approved non-MIT license.");
            }
        }

        // ── 6. MIT requirements list is non-empty and mentions attribution ────
        [Test]
        public void MitAttribution_GetRequirements_MentionsAttribution()
        {
            var reqs = ExternalSamplePolicyAudit.GetRequirements(LicenseKind.MitAttribution);
            Assert.IsNotEmpty(reqs);
            bool mentionsAttribution = System.Array.Exists(reqs,
                r => r.IndexOf("attribution", System.StringComparison.OrdinalIgnoreCase) >= 0);
            Assert.IsTrue(mentionsAttribution, "MIT requirements must mention attribution.");
        }

        // ── 7. CCByNcNd requirements advise against reuse ───────────────────
        [Test]
        public void CCByNcNd_GetRequirements_AdvisesNoReuse()
        {
            var reqs = ExternalSamplePolicyAudit.GetRequirements(LicenseKind.CCByNcNd);
            Assert.IsNotEmpty(reqs);
            bool advisesNoReuse = System.Array.Exists(reqs,
                r => r.IndexOf("prohibit", System.StringComparison.OrdinalIgnoreCase) >= 0
                  || r.IndexOf("Do not copy", System.StringComparison.OrdinalIgnoreCase) >= 0);
            Assert.IsTrue(advisesNoReuse, "CC BY-NC-ND requirements must advise against copying.");
        }
    }
}
