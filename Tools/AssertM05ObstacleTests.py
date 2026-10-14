#!/usr/bin/env python3
import sys
import xml.etree.ElementTree as ET

EXPECTED = {
    "M05_001_LayerModelOrdersCanonicalLayersRejectsDuplicatesAndInvalidDurability",
    "M05_002_MultiHitTransitionsClampOverkillAndEmitOneRemovalReceipt",
    "M05_003_ImmobilizerBlocksUntilItsOwnLayerIsRemovedWithoutMovingPiece",
    "M05_004_SpreadOccursOnlyOncePerCommittedPlayerTurnAndIsSeededBounded",
    "M05_005_GeneratorAndCollectorAreSeededBoundedBlockedAndExactlyOnce",
    "M05_006_SpecialDamageKindsHonorPoliciesAndDeduplicateSamePhase",
    "M05_007_GoalHookUsesStableObstacleIdentityAndReplaySafeReceiptId",
    "M05_008_MetadataSchemaRoundTripsCanonicallyAndRejectsMalformedRegistry",
    "M05_009_FiveSkinBindingsShareNeutralMechanicsAndContainNoGameplayFields",
    "M05_010_CountdownConsumesOnlyUniqueCommittedPlayerTurnsAndHonorsWinPrecedence",
    "M05_011_MysteryEggStagesAndSeededRevealAreDeterministicAndOneShot",
    "M05_012_BeamAbsorbStopAndAreaPolicyAreIndependentWithPhaseDedup",
    "M05_013_IngredientExitRequiresCorrectIdentityAndReachableNeutralTopology",
    "M05_014_CrossProductMatrixAllowsDeclaredSupportAndRejectsAmbiguousPairsBeforePlay",
}

def main(paths):
    found = {}
    for path in paths:
        root = ET.parse(path).getroot()
        for case in root.findall(".//test-case"):
            fullname = case.get("fullname", "")
            if ("ObstacleLifecycleContractTests." in fullname or
                    "ObstacleFlowContractTests." in fullname):
                short = fullname.rsplit(".", 1)[-1]
                found.setdefault(short, []).append(case.get("result"))
    if set(found) != EXPECTED or any(value != ["Passed"] for value in found.values()):
        raise SystemExit(
            "FAIL: M05 obstacle contracts missing, skipped, duplicated or failed: "
            + repr(found)
        )
    print("UNITY_M05_001_014_OBSTACLES=PASS DISCOVERED=%d SKIPPED=0" % len(found))

if __name__ == "__main__":
    if len(sys.argv) < 2:
        raise SystemExit("usage: AssertM05ObstacleTests.py <nunit-xml> [...]")
    main(sys.argv[1:])
