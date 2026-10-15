using System;
using System.Linq;
using Match3.Engine.Runtime;
using NUnit.Framework;

namespace Match3.Engine.Tests
{
    public sealed class PlayerSurfaceArtworkReviewTests
    {
        private static byte[] OnePixelPng()
        {
            return Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/6WQAAAAASUVORK5CYII=");
        }

        private static PlayerSurfaceMatrix Matrix()
        {
            var rows =
                from gameId in GameProfileCatalog.Ids
                from UiSurfaceKind screen in Enum.GetValues(typeof(UiSurfaceKind))
                from PlayerSurfaceStateKind state in Enum.GetValues(typeof(PlayerSurfaceStateKind))
                select new PlayerSurfaceBinding(
                    gameId, screen, state,
                    "master." + gameId + "." + screen,
                    new[] { "title" }, new[] { "back" });
            return new PlayerSurfaceMatrix(rows);
        }

        [Test]
        public void M09_014_ArtworkEvidenceCoverageDoesNotConstituteOwnerDeviceApproval()
        {
            var matrix = Matrix();
            var required = matrix.Bindings.Values.Select(x => x.MasterAssetKey)
                .Distinct(StringComparer.Ordinal).ToArray();
            var artwork = required.Select(key => new PlayerSurfaceArtworkProof(
                key, "Assets/PresentationMasters/" + key + ".png",
                OnePixelPng())).ToArray();
            var pending = PlayerSurfaceArtworkReview.Inspect(
                matrix, artwork.Take(artwork.Length - 1));
            Assert.That(pending.ReadyForHumanVisualReview, Is.False);
            Assert.That(pending.VerifiedMasterCount, Is.EqualTo(required.Length - 1));
            Assert.That(pending.MissingMasterKeys,
                Is.EqualTo(new[] { required.Last() }));

            var complete = PlayerSurfaceArtworkReview.Inspect(matrix, artwork);
            Assert.That(complete.ReadyForHumanVisualReview, Is.True);
            Assert.That(complete.RequiredMasterCount, Is.EqualTo(required.Length));
            Assert.That(artwork[0].Sha256.Length, Is.EqualTo(64));
            Assert.That(artwork[0].Width, Is.EqualTo(1));
            Assert.That(artwork[0].Height, Is.EqualTo(1));

            var actualSignoff = PlayerSurfaceGate.Evaluate(
                matrix, new PlayerSurfaceApproval(false));
            Assert.That(actualSignoff.FinalApproved, Is.False);
        }

        [Test]
        public void M09_014_ArtworkReviewValidatesFileIdentityAndCoverage()
        {
            var matrix = Matrix();
            var key = matrix.Bindings.Values.First().MasterAssetKey;
            var artwork = new PlayerSurfaceArtworkProof(
                key, "Assets/PresentationMasters/master.png", OnePixelPng());

            Assert.Throws<ArgumentException>(() =>
                PlayerSurfaceArtworkReview.Inspect(
                    matrix, new[] { artwork, artwork }));
            Assert.Throws<ArgumentException>(() =>
                PlayerSurfaceArtworkReview.Inspect(
                    matrix, new[] { new PlayerSurfaceArtworkProof(
                        "unexpected.key",
                        "Assets/PresentationMasters/other.png", OnePixelPng()) }));
            Assert.Throws<ArgumentException>(() =>
                new PlayerSurfaceArtworkProof(
                    key, "Assets/PresentationMasters/../invalid.png",
                    OnePixelPng()));

            var altered = OnePixelPng();
            altered[0] = 0;
            Assert.Throws<ArgumentException>(() =>
                new PlayerSurfaceArtworkProof(
                    key, "Assets/PresentationMasters/bad.png", altered));
        }
    }
}
