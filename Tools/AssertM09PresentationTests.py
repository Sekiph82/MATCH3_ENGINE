#!/usr/bin/env python3
import sys
import xml.etree.ElementTree as ET

EXPECTED = {
    "M09_001_BoardVisualsAreStablePooledNeutralAndFallbackSafe",
    "M09_002_PresentationVfxProjectionIsOrderedReplaySafeAndMissingKeyFallsBack",
    "M09_003_AnimationArbitrationIsBoundedPriorityStableSkipSafeAndReducedMotionSafe",
    "M09_004_UiRoutingIsTypedAccessibleAndActivationReplaySafe",
    "M09_005_ResponsiveLayoutEnforcesSafeAreaTouchTargetsAndTextOverflowPolicy",
    "M09_006_AudioAbstractionUsesTypedKeysSavedSettingsReplaySafetyAndMissingCueSuppression",
    "M09_007_FallbackResolverNeverNeedsGameplayAuthorityAndRejectsMalformedCatalogs",
    "M09_008_PresentationBudgetsReportDeterministicTierSpecificOveragesWithoutClaimingDeviceProfiling",
    "M09_009_AudioConcurrencyIsBoundedPriorityCooldownReplaySafeAndDucksCrossfadesMusic",
    "M09_010_HapticsAreOptionalRateLimitedReplaySafeAndReducedEffectsSuppressesOutput",
}

def main(paths):
    found = {}
    for path in paths:
        root = ET.parse(path).getroot()
        for case in root.findall(".//test-case"):
            fullname = case.get("fullname", "")
            if ("PresentationCoreContractTests." in fullname or
                    "PresentationPlatformContractTests." in fullname or
                    "PresentationFeedbackContractTests." in fullname):
                short = fullname.rsplit(".", 1)[-1]
                found.setdefault(short, []).append(case.get("result"))
    if set(found) != EXPECTED or any(v != ["Passed"] for v in found.values()):
        raise SystemExit(
            "FAIL: M09 presentation cases missing, skipped, duplicated or failed: "
            + repr(found)
        )
    print("UNITY_M09_001_010_PRESENTATION=PASS DISCOVERED=%d SKIPPED=0" % len(found))

if __name__ == "__main__":
    if len(sys.argv) < 2:
        raise SystemExit("usage: AssertM09PresentationTests.py <nunit-xml> [...]")
    main(sys.argv[1:])
