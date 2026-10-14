#!/usr/bin/env python3
import sys
import xml.etree.ElementTree as ET

EXPECTED = {
    "M07_001_GoalRegistrySupportsAtomicCompositeAndRejectsDuplicateMissingOrCyclicGraphs",
    "M07_002_MoveAndTimeBudgetsUseCommittedTurnsMonotonicReceiptsAndReplaySafeIds",
    "M07_003_ScoreAndStarRulesAreDeterministicBoundedAndStrictlyValidated",
    "M07_004_FinalSettleAndGoalCompletionWinBeforeBudgetExhaustionLose",
}

def main(paths):
    found = {}
    for path in paths:
        root = ET.parse(path).getroot()
        for case in root.findall(".//test-case"):
            fullname = case.get("fullname", "")
            if "GoalScoreContractTests." in fullname:
                short = fullname.rsplit(".", 1)[-1]
                found.setdefault(short, []).append(case.get("result"))
    if set(found) != EXPECTED or any(v != ["Passed"] for v in found.values()):
        raise SystemExit("FAIL: M07 goal/score cases missing, skipped, duplicated or failed: " + repr(found))
    print("UNITY_M07_001_004_GOALS_SCORE=PASS DISCOVERED=%d SKIPPED=0" % len(found))

if __name__ == "__main__":
    if len(sys.argv) < 2:
        raise SystemExit("usage: AssertM07GoalScoreTests.py <nunit-xml> [...]")
    main(sys.argv[1:])
