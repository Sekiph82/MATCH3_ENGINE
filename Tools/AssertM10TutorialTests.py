#!/usr/bin/env python3
"""Fail closed on missing, skipped, duplicated, or non-passing actual M10 Unity tests.

Requires real Unity-generated NUnit editmode/playmode XML. This audit does not
claim that synthetic XML is runtime evidence, and it consumes no themed art.
"""
import sys
import xml.etree.ElementTree as ET

EXPECTED = {
    "M10_001_FtueCatalogRejectsInvalidStatesAndDuplicateStepKeys",
    "M10_002_ThreeLevelLessonWindowAndAllProfilesHaveSameNeutralCues",
    "M10_003_SixShapesAreIdentifiableWithoutColor",
    "M10_004_WonderChildRestrictionsCannotBeOverriddenByCaller",
    "M10_005_LocalizationIsEnglishTurkishCompleteAndBounded",
    "M10_005_TextFitFlagsLongContentWithoutDroppingIt",
    "M10_006_RestoredMidLevelStepDoesNotDuplicatePresentation",
    "M10_006_CorruptedCrossProfileOrForeignSchemaFailClosed",
    "M10_007_TouchTargetsRespectFiniteBoundsAndBlocking",
    "M10_008_NeutralHudIsReadableWithoutAnimationOrAudio",
    "M10_009_ProgressIsOrderedAndCannotBeForcedWhenUiUnavailable",
    "M10_009_IntroductionRequiresApprovedCapabilityAndEarnedLevel",
    "M10_009_FeatureSwitchOffDoesNotEraseEarnedIntroduction",
    "M10_010_ReceiptReplayAndSaveCopyCannotDoubleFire",
    "M10_010_RejectCompletionWithoutPresentationAndCrossGameSave",
    "M10_010_IntroductionsPersistThroughRestartAndCannotCrossGame",
    "M10_010_FirstClaimAndUnlockCeremoniesFireExactlyOnce",
    "M10_010_VersionedRevealsAndUnknownReceiptsFailSafely",
    "M10_011_InclusionAndDefaultDeniedSocialAcrossProfiles",
    "M10_011_ScreenReaderFocusOrderHasLabelsAndReasonedDisabledStates",
    "M10_011_SafeHintsAndChildNoLossDoNotRequireAds",
    "M10_012_SeventeenSurfaceGraphRejectsUnreachableDeadActions",
    "M10_013_NoFakeClaimsOrUrgencyFromUnverifiedRewardStates",
}
SUITES = {
    "TutorialFtueContractTests",
    "TutorialAccessContractTests",
    "TutorialCheckpointContractTests",
    "TutorialUiIntegrityContractTests",
    "TutorialFeaturePacingTests",
    "TutorialCeremonyAndFocusTests",
}


def main(paths):
    if len(paths) != 2:
        raise SystemExit("usage: AssertM10TutorialTests.py <editmode.xml> <playmode.xml>")
    found = {}
    for path in paths:
        root = ET.parse(path).getroot()
        if root.tag != "test-run":
            raise SystemExit("FAIL: M10 evidence is not NUnit test-run XML")
        if int(root.get("failed", "-1")) != 0 or int(root.get("skipped", "-1")) != 0:
            raise SystemExit("FAIL: NUnit XML has failed or skipped tests")
        for case in root.findall(".//test-case"):
            fullname = case.get("fullname", "")
            segments = fullname.split(".")
            if len(segments) >= 2 and segments[-2] in SUITES:
                short = segments[-1]
                if not short.startswith("M10_"):
                    raise SystemExit("FAIL: unexpected non-M10 test in frozen M10 suites: " + fullname)
                found.setdefault(short, []).append(case.get("result"))
    if set(found) != EXPECTED or any(values != ["Passed"] for values in found.values()):
        missing = EXPECTED - set(found)
        unexpected = set(found) - EXPECTED
        invalid = {k: v for k, v in found.items() if v != ["Passed"]}
        raise SystemExit("FAIL: M10 Unity test evidence mismatch; missing=%r unexpected=%r invalid=%r" %
                         (sorted(missing), sorted(unexpected), invalid))
    print("UNITY_M10_001_013_NEUTRAL_TUTORIAL=PASS DISCOVERED=%d SKIPPED=0" % len(found))


if __name__ == "__main__":
    main(sys.argv[1:])
