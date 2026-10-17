// R26: MINIMAL editor-only adaptation of the existing working source:
// Tools/M11R2HostedSmoke.cs.txt (Git blob fb159dfa8ff5c1c39c4872c52e72c67ee7f6d85f).
// Public Cloudflare R2 QA fixture, read-only. No signing private key,
// no file writes or credentials, no auto-CI, no runtime/player dependency.
// Intentionally NOT an NUnit test. Run only by explicit local Unity
// -batchmode -executeMethod Match3R2ReadOnlyLiveProbe.Run command.
using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Match3.Engine.Runtime;
using UnityEditor;
using UnityEngine;

public static class Match3R2ReadOnlyLiveProbe
{
        private const string ExpectedBody =
            "{\"neutral\":\"match3-r2-qa-v1\",\"level\":1}";
        private const string ExpectedDigest =
            "0160ce606a68def670f4f74600c6a6c08fd91dae2ad9e2caedab3e827de42e53";
        private const string Origin =
            "https://pub-39632e9ddf304be7b407919f185cceac.r2.dev/";
        private const string FixturePath = "qa/neutral-v2.json";
        private const string PinnedQaModulusBase64 =
            "rEhpHM6pi7K9QJWueacRKZkTVrhOBkXLMq9aHjfCaCYFUjC9BfVhbDNpxi4WDxxfEPgjdJP4b3JFbwcka8yKE5qYIeLPuLEg6nmIuYTAZ3W/YJlbqnZ+I+3WJvGroQkPtyRg/uI9WbSoXa7e7uh0m4crqsfbnrN6eMbiShO8BKFgH1KlpCzNjgoR+XjEa2gFv+Vsjx2bImxKwUtMp2fjqbnkoNpXcA8/gc1kidHn0nDgBlV0EPD1EZmgxEZ1Ntw74cVndJQUbI6grZwOePa/zMCrzvCa7W+nsLndc2rZKMstBwU4Lk892bY+IdBTKV5kKKYuib2IJM5TxfttOXZfEQ==";
        private const string PinnedCanonicalSha256 =
            "0af60d84ddf9a140008b4dc2d751bbe027a0c177c5d4b97e416eb5e92e6b15ac";
    [Serializable]
    private sealed class QaSignedEnvelope
    {
        public int schema;
        public string manifest_b64;
        public string signature_b64;
        public string key_id;
    }

    // Manual real TLS/HTTPS receipt, never silently invoked during the
    // ordinary 17 EditMode / 280 PlayMode offline regression suites.
    public static void Run()
    {
        if (!Application.isBatchMode || BuildPipeline.isBuildingPlayer)
            throw new InvalidOperationException(
                "R26 public read-only QA may run only in an explicit Editor batch.");

        // Local test calls are time-bound and cannot read credentials or
        // upload anything to R2. An old fixture expiry is an honest FAIL,
        // never a reason to bypass verification with a hardcoded old clock.
        using (var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(60)))
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var transport = new RemoteHttpsReadTransport();
            var origin = new RemoteReadOrigin(Origin);
            var expected = Encoding.UTF8.GetBytes(ExpectedBody);
            Require(expected.Length == 39 &&
                RemoteContentSafety.Sha256(expected) == ExpectedDigest,
                "Pinned QA fixture expectation changed.");

            // Read real canonical manifest and detached RSA signature carried
            // in the historically deployed public QA JSON envelope.
            // This endpoint is NOT the shipping player's distinct
            // manifest.canonical + manifest.sig convention.
            var envelopeUrl = origin.ForPath("qa/signed-manifest-v1.json");
            var response = transport.FetchAsync(
                envelopeUrl, 2048, timeout.Token)
                .GetAwaiter().GetResult();
            Require(response.HttpStatus == 200,
                "Public read-only QA envelope unavailable.");

            var envelope = JsonUtility.FromJson<QaSignedEnvelope>(
                Encoding.UTF8.GetString(response.Bytes));
            Require(envelope != null && envelope.schema == 1 &&
                envelope.key_id == "match3-qa-pub-v1" &&
                !string.IsNullOrEmpty(envelope.manifest_b64) &&
                !string.IsNullOrEmpty(envelope.signature_b64),
                "Unexpected signed QA envelope schema/key.");
            var wire = Convert.FromBase64String(envelope.manifest_b64);
            var signature = Convert.FromBase64String(envelope.signature_b64);
            Require(wire.Length == 196 &&
                RemoteContentSafety.Sha256(wire) == PinnedCanonicalSha256,
                "Remote QA manifest changed from the exact pinned digest.");
            var trusted = new RemotePublicSigningKey(
                "match3-qa-pub-v1",
                Convert.FromBase64String(PinnedQaModulusBase64),
                Convert.FromBase64String("AQAB"), 0, 1823472000);
            var engine = new EngineVersion(1, 0, 0);

            // Real current date is essential. Passing the obsolete frozen
            // test timestamp could authenticate an expired signed release.
            var verified = RemoteManifestCodec.ParseVerified(
                wire, signature, new[] { trusted },
                "Coral_Rescue", RemotePlatform.Android,
                RemoteChannel.FamilyTest, engine, now);
            Require(verified.ReleaseId == "match3-qa-v1" &&
                verified.Entries.Count == 1 &&
                verified.Entries[0].Path == FixturePath &&
                verified.Entries[0].Sha256 == ExpectedDigest,
                "Authenticated QA manifest does not match pinned content.");

            // Reuse genuine Engine download verification. This performs
            // a separate REAL bounded HTTPS GET and checks SHA-256.
            var receipt = RemoteDownloadCoordinator.FetchVerifiedAsync(
                verified, signature, new[] { trusted }, origin, transport,
                verified.Entries[0], verified.GameId, verified.Platform,
                verified.Channel, engine, now, timeout.Token,
                maxAttempts: 2).GetAwaiter().GetResult();
            Require(receipt.Path == FixturePath &&
                receipt.Bytes.Length == expected.Length &&
                RemoteContentSafety.Sha256(receipt.Bytes) == ExpectedDigest,
                "Remote HTTPS payload failed signed digest verification.");

            // A modified RSA signature must fail without any new HTTP GET.
            var forged = (byte[])signature.Clone();
            forged[0] ^= 0x80;
            var rejected = false;
            try
            {
                RemoteManifestCodec.ParseVerified(
                    wire, forged, new[] { trusted }, verified.GameId,
                    verified.Platform, verified.Channel, engine, now);
            }
            catch (InvalidOperationException)
            {
                rejected = true;
            }
            Require(rejected,
                "Tampered QA signature was incorrectly accepted.");

            Debug.Log(
                "ENG_M11_R26_LIVE_R2_READ_ONLY=PASS " +
                "TLS_HTTPS_GET=2 RSA_SHA256=PASS PAYLOAD_SHA256=PASS " +
                "TAMPER_REJECTED=PASS " +
                "SHIPPING_PLAYER_TLS=NOT_RUN " +
                "OFFICIAL_ADDRESSABLES=NOT_RUN");
        }
    }

    private static void Require(bool accepted, string message)
    {
        if (!accepted) throw new InvalidOperationException(message);
    }
}
