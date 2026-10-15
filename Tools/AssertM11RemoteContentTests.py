#!/usr/bin/env python3
"""Fail-closed M11 Unity NUnit test-discovery gate: exact names and no skips."""
import sys
import xml.etree.ElementTree as ET

EXPECTED = {
    "M11_001_ManifestV1IsCanonicalAndRejectsInvalidSchemaAndPaths",
    "M11_002_AuthenticManifestSignatureAndKeyRotationAreRequired",
    "M11_009_ExecutablePayloadsAndUnsafeTypeMasqueradesAreRejected",
    "M11_010_GamePlatformAndChannelIsolationFailClosed",
    "M11_011_IncompatibleVersionExpiryAndRevokedKeyFail",
    "M11_004_IncrementalCacheRequiresExactBytesAndVerifiedDigest",
    "M11_005_SignedCompleteStageRefusesCorruptionMissingAndExtras",
    "M11_005_PlannerPublishesOnlyAfterVerifiedHostAcknowledges",
    "M11_006_VerifiedOfflineSelectionHasFailClosedEmptyState",
    "M11_007_SignedSequencePreventsDowngradeAndRecoverKeepsHighWater",
    "M11_007_PrepareFailurePointerConflictAndCancellationFailClosed",
    "M11_013_PatchStatesShowOnlyVerifiedBoundedProgress",
    "M11_014_UnverifiedRemoteMetadataAndChildOfferAreBlocked",
    "M11_003_AuthenticatedBoundedDownloadNeverUsesWriteCredentials",
    "M11_003_MalformedOriginPathAndPreCanceledRequestsFailClosed",
    "M11_011_TransientNetworkFaultRetriesWithinFixedCap",
    "M11_011_BadSignatureIsRejectedBeforeAnyTransport",
    "M11_003_FullFetchStageIsBoundedAndProgressIsVerifiedOnly",
    "M11_001_CanonicalWireRoundTripRejectsNoncanonicalOrMalformedInput",
    "M11_002_VerifiedWireRejectsTamperingAndWrongGame",
    "M11_012_DisabledNeighborProjectionRequiresSignedVerifiedProfile",
    "M11_012_NewSignedManifestCanReenableWithoutForgingRewards",
    "M11_008_OnlySignedTitlePlatformCatalogAndBundlesCanBeBound",
    "M11_005_DurableFilesAreRevalidatedAfterFreshHostRestart",
    "M11_007_DurableCrashFallbackPreservesAntiRollbackSequence",
    "M11_011_InterruptedStagingAndPointerTamperNeverActivateContent",
    "M11_011_InvalidStageDoesNotTouchDiskAndOrphanQuotaFailsClosed",
    "M11_006_SignedPackagedStarterLoadsForFiveNeutralProfiles",
    "M11_006_PackagedPayloadTamperAndRevokedKeyCannotBoot",
    "M11_006_RevokedInstalledReleaseFallsBackToSeparateSignedStarter",
    "M11_013_VerifiedProgressAndCommitAreSeparatelyHonest",
    "M11_013_DuplicateReceiptCannotFabricateHundredPercentOrReward",
    "M11_013_OfflineFallbackRequiresFreshValidSignedContent",
    "M11_013_UnityPatchPanelNeverInventsSignedStatusOrGameAccess",
    "M11_013_UnityPatchPanelShowsOnlyAuthenticatedOfflinePlay",
}
SUITES = {
    "RemoteContentManifestContractTests",
    "RemoteContentReleaseContractTests",
    "RemoteActivationHostContractTests",
    "RemoteContentDownloadContractTests",
    "RemoteManifestCodecAndLevelTests",
    "RemoteAddressablesContractTests",
    "RemoteDurableActivationHostTests",
    "RemotePackagedStarterContractTests",
    "RemotePatchFlowContractTests",
    "NeutralPatchBootstrapViewPlayModeTests",
}

def check(paths):
    if len(paths) != 2:
        raise SystemExit("usage: AssertM11RemoteContentTests.py <editmode.xml> <playmode.xml>")
    found = {}
    for file_path in paths:
        root = ET.parse(file_path).getroot()
        if root.tag != "test-run":
            raise SystemExit("FAIL: M11 evidence is not real NUnit XML")
        if int(root.get("failed", "-1")) != 0 or int(root.get("skipped", "-1")) != 0:
            raise SystemExit("FAIL: any failed/skipped test invalidates M11 batch")
        for item in root.findall(".//test-case"):
            name = item.get("fullname", "")
            segments = name.split(".")
            if len(segments) > 1 and segments[-2] in SUITES:
                short = segments[-1]
                if not short.startswith("M11_"):
                    raise SystemExit("FAIL: unexpected case in M11 suite: " + name)
                found.setdefault(short, []).append(item.get("result"))
    if set(found) != EXPECTED or any(v != ["Passed"] for v in found.values()):
        raise SystemExit("FAIL: M11 evidence mismatch missing=%r extra=%r invalid=%r" %
                         (sorted(EXPECTED - set(found)), sorted(set(found) - EXPECTED),
                          {k: v for k, v in found.items() if v != ["Passed"]}))
    print("UNITY_M11_001_014_REMOTE_CONTENT=PASS DISCOVERED=%d SKIPPED=0" % len(found))

if __name__ == "__main__":
    check(sys.argv[1:])
