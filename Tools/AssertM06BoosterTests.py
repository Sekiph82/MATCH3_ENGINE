#!/usr/bin/env python3
import sys
import xml.etree.ElementTree as ET

EXPECTED = {
    "M06_001_PrelevelReservationAndPlacementAreDeterministicAndPreviewDoesNotSpend",
    "M06_002_HammerValidatesTargetAndProducesExactlyOneCellWithoutChargingRejectedAction",
    "M06_003_LineAreaAndColorBoostersAreDeterministicTypedAndDeduplicated",
    "M06_004_ShuffleAndSwapAreSeededLegalAndPreserveTopologyAndMultiset",
    "M06_005_BoosterSpecialChainsDelegateToCanonicalM03EffectPlanner",
    "M06_006_EconomyReserveConfirmRefundAndGrantAreExactlyOnce",
    "M06_007_PresentationPreviewConfirmCancelAndSkipCannotChangeLogicalHash",
    "M06_008_ChildProfileSafetyRejectsPaidRewardedSilentAndUnconfirmedSpend",
    "M06_009_AdvancedBoostersShareOneRequestLifecycleAndExactlyOnceChargePath",
    "M06_010_InterruptionInvalidatesStaleCommitAndRecoveryRefundsExactlyOnce",
    "M06_011_ScoreToPowerIsAlternativeModeOnlyBoundedAndReplayProof",
}

def main(paths):
    found = {}
    for path in paths:
        root = ET.parse(path).getroot()
        for case in root.findall(".//test-case"):
            fullname = case.get("fullname", "")
            if "BoosterContractTests." in fullname:
                short = fullname.rsplit(".", 1)[-1]
                found.setdefault(short, []).append(case.get("result"))
    if set(found) != EXPECTED or any(value != ["Passed"] for value in found.values()):
        raise SystemExit(
            "FAIL: M06 booster contracts missing, skipped, duplicated or failed: "
            + repr(found)
        )
    print("UNITY_M06_001_011_BOOSTERS=PASS DISCOVERED=%d SKIPPED=0" % len(found))

if __name__ == "__main__":
    if len(sys.argv) < 2:
        raise SystemExit("usage: AssertM06BoosterTests.py <nunit-xml> [...]")
    main(sys.argv[1:])
