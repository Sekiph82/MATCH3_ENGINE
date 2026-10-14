#!/usr/bin/env python3
import sys
import xml.etree.ElementTree as ET

EXPECTED = {
    "M07_001_GoalRegistrySupportsAtomicCompositeAndRejectsDuplicateMissingOrCyclicGraphs",
    "M07_002_MoveAndTimeBudgetsUseCommittedTurnsMonotonicReceiptsAndReplaySafeIds",
    "M07_003_ScoreAndStarRulesAreDeterministicBoundedAndStrictlyValidated",
    "M07_004_FinalSettleAndGoalCompletionWinBeforeBudgetExhaustionLose",
    "M07_005_PostWinLeftoverConversionIsConfigurableBoundedAndRewardAuthorityNeutral",
    "M07_006_ProgressStreamKeepsHudAndSummaryInLockstepAndRejectsOutOfOrderEvents",
    "M07_007_LevelGoalValidationRejectsUnavailableAndImpossibleTargetsBeforePlay",
    "M07_008_SeededQaProducesHundredsOfValidFixturesWithStableAggregateHash",
    "M07_009_FirstClearRewardsAreExactlyOnceAndReplayOnlyImprovesBestPolicy",
    "M07_010_SkipAndReducedMotionSuppressPresentationOnlyNotFinalScoreStarsOrRewards",
}

def main(paths):
    found = {}
    for path in paths:
        root = ET.parse(path).getroot()
        for case in root.findall(".//test-case"):
            fullname = case.get("fullname", "")
            if ("GoalScoreContractTests." in fullname or
                    "GoalProgressContractTests." in fullname):
                short = fullname.rsplit(".", 1)[-1]
                found.setdefault(short, []).append(case.get("result"))
    if set(found) != EXPECTED or any(v != ["Passed"] for v in found.values()):
        raise SystemExit("FAIL: M07 goal/score cases missing, skipped, duplicated or failed: " + repr(found))
    print("UNITY_M07_001_010_GOALS_SCORE=PASS DISCOVERED=%d SKIPPED=0" % len(found))

if __name__ == "__main__":
    if len(sys.argv) < 2:
        raise SystemExit("usage: AssertM07GoalScoreTests.py <nunit-xml> [...]")
    main(sys.argv[1:])
