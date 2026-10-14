#!/usr/bin/env python3
import sys
import xml.etree.ElementTree as ET

EXPECTED = {
    "M08_001_SharedGraphSchemaValidatesFiveGameIdentityReferencesAndCycles",
    "M08_002_NavigationFollowsCommonRouteAndRejectsMismatchIllegalOrDuplicateTransitions",
    "M08_003_UnlockRulesAreCanonicalStarGatedMonotonicAndReplaySafe",
    "M08_004_HeartsReconcileOfflineTimeClampRollbackCapAndExactlyOnceLedger",
    "M08_005_GameEconomyIsPerGameIdempotentAndRejectsCrossGameSourceReuse",
    "M08_006_DailyRewardsClaimOnceClampRollbackAndHonorMissedDayPolicy",
    "M08_007_DailyWeeklyTasksDeduplicateProgressResetAndClaimExactlyOnce",
    "M08_008_RestorationMilestonesUseNeutralProgressAndThemeBindingsOnly",
    "M08_009_SaveMigrationBacksUpHashesSequentiallyAndFailsAtomically",
    "M08_010_PrivacyIsOptInChildSafeReplaySafeAndNeverBlocksLocalPlay",
}

def main(paths):
    found = {}
    for path in paths:
        root = ET.parse(path).getroot()
        for case in root.findall(".//test-case"):
            fullname = case.get("fullname", "")
            if ("ProgressionGraphContractTests." in fullname or
                    "ProgressionServicesContractTests." in fullname):
                short = fullname.rsplit(".", 1)[-1]
                found.setdefault(short, []).append(case.get("result"))
    if set(found) != EXPECTED or any(v != ["Passed"] for v in found.values()):
        raise SystemExit(
            "FAIL: M08 progression cases missing, skipped, duplicated or failed: "
            + repr(found)
        )
    print("UNITY_M08_001_010_PROGRESSION=PASS DISCOVERED=%d SKIPPED=0" % len(found))

if __name__ == "__main__":
    if len(sys.argv) < 2:
        raise SystemExit("usage: AssertM08ProgressionTests.py <nunit-xml> [...]")
    main(sys.argv[1:])
