using System;
using System.Collections.Generic;
using System.Linq;
using Match3.Engine.Runtime;
using NUnit.Framework;

namespace Match3.Engine.Tests
{
    public sealed class SpecialEffectPlannerTests
    {
        private static BoardPosition P(int r, int c, int layer = 0) =>
            new BoardPosition(r, c, layer);

        private static SpecialBoardSnapshot Full(int rows, int columns,
            Func<int, int, SpecialCellState> factory = null)
        {
            var cells = new List<SpecialCellState>();
            for (var r = 0; r < rows; r++)
                for (var c = 0; c < columns; c++)
                    cells.Add(factory == null
                        ? new SpecialCellState(P(r, c), "A")
                        : factory(r, c));
            return new SpecialBoardSnapshot(rows, columns, cells);
        }

        private static SpecialBoardSnapshot ColorBoard()
        {
            var rows = new[] { "AAB", "BBA", "CAC" };
            var cells = new List<SpecialCellState>();
            for (var r = 0; r < rows.Length; r++)
                for (var c = 0; c < rows[r].Length; c++)
                    cells.Add(new SpecialCellState(P(r, c), rows[r][c].ToString()));
            return new SpecialBoardSnapshot(3, 3, cells);
        }

        [Test]
        public void LineSpecialDamagesEligibleCellsHonorsImmunityAndStopsAtBlockingObstacle()
        {
            var board = Full(1, 5, (r, c) =>
            {
                if (c == 1) return new SpecialCellState(P(r, c), "B", immune: true);
                if (c == 3) return new SpecialCellState(P(r, c), "D",
                    obstacleLayers: 2, stopsLinePropagation: true);
                return new SpecialCellState(P(r, c), "A");
            });

            var plan = SpecialEffectPlanner.Resolve(board,
                new SpecialActivation(SpecialActivationKind.HorizontalLine, P(0, 2)));

            Assert.That(plan.Hits.Select(x => x.Position).ToArray(),
                Is.EqualTo(new[] { P(0, 2), P(0, 0), P(0, 3) }));
            Assert.That(plan.Hits.Any(x => x.Position.Equals(P(0, 1))), Is.False);
            Assert.That(plan.Hits.Any(x => x.Position.Equals(P(0, 4))), Is.False);
            var obstacle = plan.Hits.Single(x => x.Position.Equals(P(0, 3)));
            Assert.That(obstacle.ObstacleLayersBefore, Is.EqualTo(2));
            Assert.That(obstacle.ObstacleLayersAfter, Is.EqualTo(1));

            var verticalBoard = Full(5, 1, (r, c) =>
            {
                if (r == 1) return new SpecialCellState(P(r, c), "B", immune: true);
                if (r == 3) return new SpecialCellState(P(r, c), "D",
                    obstacleLayers: 2, stopsLinePropagation: true);
                return new SpecialCellState(P(r, c), "A");
            });
            var vertical = SpecialEffectPlanner.Resolve(verticalBoard,
                new SpecialActivation(SpecialActivationKind.VerticalLine, P(2, 0)));
            Assert.That(vertical.Hits.Select(x => x.Position).ToArray(),
                Is.EqualTo(new[] { P(2, 0), P(0, 0), P(3, 0) }));
            Assert.That(vertical.Hits.Any(x => x.Position.Equals(P(1, 0))), Is.False);
            Assert.That(vertical.Hits.Any(x => x.Position.Equals(P(4, 0))), Is.False);

            var lockedBoard = Full(1, 3, (r, col) =>
                new SpecialCellState(P(r, col), "A", locked: col == 1));
            var lockedLine = SpecialEffectPlanner.Resolve(lockedBoard,
                new SpecialActivation(SpecialActivationKind.HorizontalLine, P(0, 0)));
            Assert.That(lockedLine.Hits.Select(x => x.Position).ToArray(),
                Is.EqualTo(new[] { P(0, 0), P(0, 2) }));
            Assert.That(lockedLine.Hits.Any(x => x.Position.Equals(P(0, 1))), Is.False);
        }

        [Test]
        public void AreaRadiusIsConfigurableAndBombOverlapNeverDoubleHitsACell()
        {
            var board = Full(5, 5);
            var one = SpecialEffectPlanner.Resolve(board,
                new SpecialActivation(SpecialActivationKind.Area, P(2, 2), radius: 1));
            Assert.That(one.Hits.Count, Is.EqualTo(9));
            Assert.That(one.Hits.Select(x => x.Position).Distinct().Count(), Is.EqualTo(9));

            var combined = SpecialEffectPlanner.Resolve(board,
                new SpecialActivation(SpecialActivationKind.Area, P(2, 2), radius: 1),
                new SpecialActivation(SpecialActivationKind.Area, P(2, 2), radius: 1));
            Assert.That(combined.Hits.Count, Is.EqualTo(25));
            Assert.That(combined.Hits.Select(x => x.Position).Distinct().Count(), Is.EqualTo(25));

            var edge = SpecialEffectPlanner.Resolve(board,
                new SpecialActivation(SpecialActivationKind.Area, P(0, 0), radius: 2));
            Assert.That(edge.Hits.Count, Is.EqualTo(9));
            Assert.That(edge.Hits.All(x => x.Position.Row <= 2 && x.Position.Column <= 2), Is.True);
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new SpecialActivation(SpecialActivationKind.Area, P(0, 0), radius: 0));
        }

        [Test]
        public void ColorSpecialChoosesDeterministicEligibleColorAndCanTargetExplicitNormalColor()
        {
            var cells = new[]
            {
                new SpecialCellState(P(0,0), "A"),
                new SpecialCellState(P(0,1), "A"),
                new SpecialCellState(P(0,2), "B"),
                new SpecialCellState(P(1,0), "B"),
                new SpecialCellState(P(1,1), "B", immune: true),
                new SpecialCellState(P(1,2), null)
            };
            var board = new SpecialBoardSnapshot(2, 3, cells);
            var auto = SpecialEffectPlanner.Resolve(board,
                new SpecialActivation(SpecialActivationKind.Color, P(1, 2)));
            Assert.That(auto.SelectedColor, Is.EqualTo("A"));
            Assert.That(auto.Hits.Count, Is.EqualTo(2));
            Assert.That(auto.Hits.All(x =>
                cells.Single(c => c.Position.Equals(x.Position)).ContentId == "A"), Is.True);

            var explicitB = SpecialEffectPlanner.ResolveColorWithNormal(board,
                new SpecialActivation(SpecialActivationKind.Color, P(1, 2)), P(0, 2));
            Assert.That(explicitB.SelectedColor, Is.EqualTo("B"));
            Assert.That(explicitB.Hits.Count, Is.EqualTo(2));
            Assert.That(explicitB.Hits.Any(x => x.Position.Equals(P(1, 1))), Is.False);

            var missing = SpecialEffectPlanner.Resolve(board,
                new SpecialActivation(SpecialActivationKind.Color, P(1, 2),
                    targetColor: "MISSING"));
            Assert.That(missing.SelectedColor, Is.EqualTo("MISSING"));
            Assert.That(missing.Hits, Is.Empty);
            Assert.Throws<ArgumentException>(() =>
                SpecialEffectPlanner.ResolveColorWithNormal(board,
                    new SpecialActivation(SpecialActivationKind.Color, P(1, 2)), P(1, 1)));
        }

        [Test]
        public void RocketPairsAndRocketBombProduceStableUniqueCrossDamage()
        {
            var board = Full(5, 5);
            var cross = SpecialEffectPlanner.Resolve(board,
                new SpecialActivation(SpecialActivationKind.HorizontalLine, P(2, 2)),
                new SpecialActivation(SpecialActivationKind.VerticalLine, P(2, 2)));
            Assert.That(cross.Hits.Count, Is.EqualTo(9));
            Assert.That(cross.Hits.Select(x => x.Position).Distinct().Count(), Is.EqualTo(9));

            var parallel = SpecialEffectPlanner.Resolve(board,
                new SpecialActivation(SpecialActivationKind.HorizontalLine, P(2, 2)),
                new SpecialActivation(SpecialActivationKind.HorizontalLine, P(1, 2)));
            Assert.That(parallel.Hits.Count, Is.EqualTo(10));

            var rocketBomb = SpecialEffectPlanner.Resolve(board,
                new SpecialActivation(SpecialActivationKind.HorizontalLine, P(2, 2)),
                new SpecialActivation(SpecialActivationKind.Area, P(2, 2)));
            Assert.That(rocketBomb.Hits.Count, Is.EqualTo(21));
            Assert.That(rocketBomb.Hits.Select(x => x.Position).Distinct().Count(), Is.EqualTo(21));
        }

        [Test]
        public void BombBombCascadeTriggersEmbeddedSpecialButNeverDuplicatesDamage()
        {
            var board = Full(5, 5, (r, c) => new SpecialCellState(P(r, c), "A",
                special: r == 2 && c == 3 ? SpecialActivationKind.Area :
                    (SpecialActivationKind?)null));
            var plan = SpecialEffectPlanner.Resolve(board,
                new SpecialActivation(SpecialActivationKind.Area, P(2, 2)),
                new SpecialActivation(SpecialActivationKind.Area, P(2, 2)));

            Assert.That(plan.Hits.Count, Is.EqualTo(25));
            Assert.That(plan.Hits.Select(x => x.Position).Distinct().Count(), Is.EqualTo(25));
            Assert.That(plan.Activations.Any(x =>
                x.Kind == SpecialActivationKind.Area && x.Origin.Equals(P(2, 3))), Is.True);
            Assert.That(plan.Bounded, Is.True);
        }

        [Test]
        public void ColorCombinesWithNormalRocketBombAndColorWithoutDuplicateHits()
        {
            var board = ColorBoard();
            var color = new SpecialActivation(SpecialActivationKind.Color, P(2, 1));

            var normal = SpecialEffectPlanner.ResolveColorWithNormal(board, color, P(0, 2));
            Assert.That(normal.SelectedColor, Is.EqualTo("B"));
            Assert.That(normal.Hits.Count, Is.EqualTo(3));

            var rocket = SpecialEffectPlanner.Resolve(board, color,
                new SpecialActivation(SpecialActivationKind.HorizontalLine, P(0, 2)));
            Assert.That(rocket.SelectedColor, Is.EqualTo("B"));
            Assert.That(rocket.Conversions.Count, Is.EqualTo(3));
            Assert.That(rocket.Conversions.All(x =>
                x.ToKind == SpecialActivationKind.HorizontalLine), Is.True);
            Assert.That(rocket.Hits.Select(x => x.Position).Distinct().Count(),
                Is.EqualTo(rocket.Hits.Count));

            var bomb = SpecialEffectPlanner.Resolve(board, color,
                new SpecialActivation(SpecialActivationKind.Area, P(0, 2)));
            Assert.That(bomb.Conversions.Count, Is.EqualTo(3));
            Assert.That(bomb.Conversions.All(x =>
                x.ToKind == SpecialActivationKind.Area), Is.True);
            Assert.That(bomb.Hits.Select(x => x.Position).Distinct().Count(),
                Is.EqualTo(bomb.Hits.Count));

            var all = SpecialEffectPlanner.Resolve(board, color,
                new SpecialActivation(SpecialActivationKind.Color, P(0, 0)));
            Assert.That(all.SelectedColor, Is.EqualTo("*"));
            Assert.That(all.Hits.Count, Is.EqualTo(9));
        }

        [Test]
        public void ChainOrderIsStableAndActivationBudgetFailsClosedBeforeInfiniteReaction()
        {
            var board = Full(1, 4, (r, c) =>
                new SpecialCellState(P(r, c), "A",
                    special: SpecialActivationKind.HorizontalLine));
            var plan = SpecialEffectPlanner.Resolve(board,
                new SpecialActivation(SpecialActivationKind.HorizontalLine, P(0, 0)),
                seed: 9u, maximumActivations: 32);
            Assert.That(plan.Activations.Select(x => x.Origin).ToArray(),
                Is.EqualTo(new[] { P(0, 0), P(0, 1), P(0, 2), P(0, 3) }));
            Assert.That(plan.Hits.Count, Is.EqualTo(4));
            Assert.Throws<InvalidOperationException>(() =>
                SpecialEffectPlanner.Resolve(board,
                    new SpecialActivation(SpecialActivationKind.HorizontalLine, P(0, 0)),
                    seed: 9u, maximumActivations: 2));
        }

        [Test]
        public void GoldenCorpusLocksThreeFourFiveLTCasesAndRocketPairTransition()
        {
            var matchPlans = SpecialMatchPlanner.Plan(new[]
            {
                new MatchRunProjection(MatchRunAxis.Horizontal, "A",
                    new[] { P(0,0), P(0,1), P(0,2) }),
                new MatchRunProjection(MatchRunAxis.Horizontal, "B",
                    new[] { P(1,0), P(1,1), P(1,2), P(1,3) }),
                new MatchRunProjection(MatchRunAxis.Horizontal, "C",
                    new[] { P(2,0), P(2,1), P(2,2), P(2,3), P(2,4) }),
                new MatchRunProjection(MatchRunAxis.Horizontal, "D",
                    new[] { P(3,0), P(3,1), P(3,2) }),
                new MatchRunProjection(MatchRunAxis.Vertical, "D",
                    new[] { P(3,0), P(4,0), P(5,0) }),
                new MatchRunProjection(MatchRunAxis.Horizontal, "E",
                    new[] { P(6,0), P(6,1), P(6,2) }),
                new MatchRunProjection(MatchRunAxis.Vertical, "E",
                    new[] { P(6,1), P(7,1), P(8,1) })
            });
            var matchHash = SpecialGoldenHasher.HashMatchPlans(matchPlans);
            Assert.That(matchHash,
                Is.EqualTo("2b4cc56855bd574d56640cc90eb5078c9aec26727eed1f6c01791c6390dbd316"));
            Assert.That(SpecialGoldenHasher.CorpusVersion, Is.EqualTo("ENG-M03-GOLDEN-V1"));
            Assert.That(SpecialGoldenHasher.BindToCorpusVersion(matchHash),
                Is.EqualTo("61a05b64be14d8d427528a12aa6a0a2dadf4fd6fc4499fe86f180634903de882"));
            Assert.That(SpecialGoldenHasher.BindToCorpusVersion(
                    matchHash, "ENG-M03-GOLDEN-V2"),
                Is.Not.EqualTo(SpecialGoldenHasher.BindToCorpusVersion(matchHash)));

            var cross = SpecialEffectPlanner.Resolve(Full(3, 3),
                new SpecialActivation(SpecialActivationKind.HorizontalLine, P(1, 1)),
                new SpecialActivation(SpecialActivationKind.VerticalLine, P(1, 1)));
            var crossHash = SpecialGoldenHasher.HashEffectPlan(cross);
            Assert.That(crossHash,
                Is.EqualTo("fdf090be5de2825f0163f7cb4d9f41bfcdab3074dcfce050fae6db983876dbb8"));
            Assert.That(SpecialGoldenHasher.BindToCorpusVersion(crossHash),
                Is.EqualTo("6ec9af3bd39be42466d9efdfe4bf7a23082fd6643556ff183d89b0259c39f98e"));
            var tampered = SpecialEffectPlanner.Resolve(Full(3, 3),
                new SpecialActivation(SpecialActivationKind.HorizontalLine, P(0, 1)),
                new SpecialActivation(SpecialActivationKind.VerticalLine, P(0, 1)));
            Assert.That(SpecialGoldenHasher.HashEffectPlan(tampered), Is.Not.EqualTo(crossHash));
        }

        [Test]
        public void GoldenSpecialPairMatrixLocksEverySupportedTransitionIncludingSquareTargetedLine()
        {
            var board = ColorBoard();
            var color = new SpecialActivation(SpecialActivationKind.Color, P(2, 1));
            var targeted = new SpecialActivation(SpecialActivationKind.Targeted, P(2, 2));
            var hashes = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("HH", SpecialGoldenHasher.HashEffectPlan(
                    SpecialEffectPlanner.Resolve(board,
                        new SpecialActivation(SpecialActivationKind.HorizontalLine, P(1, 1)),
                        new SpecialActivation(SpecialActivationKind.HorizontalLine, P(0, 1))))),
                new KeyValuePair<string, string>("HV", SpecialGoldenHasher.HashEffectPlan(
                    SpecialEffectPlanner.Resolve(board,
                        new SpecialActivation(SpecialActivationKind.HorizontalLine, P(1, 1)),
                        new SpecialActivation(SpecialActivationKind.VerticalLine, P(1, 1))))),
                new KeyValuePair<string, string>("HA", SpecialGoldenHasher.HashEffectPlan(
                    SpecialEffectPlanner.Resolve(board,
                        new SpecialActivation(SpecialActivationKind.HorizontalLine, P(1, 1)),
                        new SpecialActivation(SpecialActivationKind.Area, P(1, 1))))),
                new KeyValuePair<string, string>("AA", SpecialGoldenHasher.HashEffectPlan(
                    SpecialEffectPlanner.Resolve(board,
                        new SpecialActivation(SpecialActivationKind.Area, P(1, 1)),
                        new SpecialActivation(SpecialActivationKind.Area, P(1, 1))))),
                new KeyValuePair<string, string>("CN", SpecialGoldenHasher.HashEffectPlan(
                    SpecialEffectPlanner.ResolveColorWithNormal(board, color, P(0, 2)))),
                new KeyValuePair<string, string>("CH", SpecialGoldenHasher.HashEffectPlan(
                    SpecialEffectPlanner.Resolve(board, color,
                        new SpecialActivation(SpecialActivationKind.HorizontalLine, P(0, 2))))),
                new KeyValuePair<string, string>("CA", SpecialGoldenHasher.HashEffectPlan(
                    SpecialEffectPlanner.Resolve(board, color,
                        new SpecialActivation(SpecialActivationKind.Area, P(0, 2))))),
                new KeyValuePair<string, string>("CC", SpecialGoldenHasher.HashEffectPlan(
                    SpecialEffectPlanner.Resolve(board, color,
                        new SpecialActivation(SpecialActivationKind.Color, P(0, 0))))),
                new KeyValuePair<string, string>("TH", SpecialGoldenHasher.HashEffectPlan(
                    SpecialEffectPlanner.Resolve(board, targeted,
                        new SpecialActivation(SpecialActivationKind.HorizontalLine, P(0, 0)),
                        seed: 5u))),
                new KeyValuePair<string, string>("TA", SpecialGoldenHasher.HashEffectPlan(
                    SpecialEffectPlanner.Resolve(board, targeted,
                        new SpecialActivation(SpecialActivationKind.Area, P(0, 0)),
                        seed: 5u))),
                new KeyValuePair<string, string>("TC", SpecialGoldenHasher.HashEffectPlan(
                    SpecialEffectPlanner.Resolve(board, targeted,
                        new SpecialActivation(SpecialActivationKind.Color, P(0, 0)),
                        seed: 5u)))
            };

            var squareBoard = Full(3, 3, (r, col) =>
                new SpecialCellState(P(r, col), "A",
                    goalPriority: r == 1 && col == 1 ? 5 : 0));
            var enabled = new GameProfile("Coral_Rescue", "neutral",
                ProfileAgeRule.General, ProfileContentChannel.Development,
                new[]
                {
                    new FeatureFlag("match_2x2", EngineCompatibility.MinimumCapability),
                    new FeatureFlag("targeted_missile", EngineCompatibility.MinimumCapability)
                });
            var square = OptionalSquareSpecialDetector.Detect(squareBoard, enabled,
                preferredAnchors: new[] { P(1, 1) }).Single();
            Assert.That(square.Anchor, Is.EqualTo(P(1, 1)));
            var squareLine = SpecialEffectPlanner.Resolve(squareBoard,
                new SpecialActivation(SpecialActivationKind.Targeted, square.Anchor),
                new SpecialActivation(SpecialActivationKind.HorizontalLine, P(2, 2)),
                seed: 5u);
            Assert.That(squareLine.Activations.Any(x =>
                x.Kind == SpecialActivationKind.HorizontalLine &&
                x.Origin.Equals(P(1, 1))), Is.True);
            Assert.That(squareLine.Hits.Select(x => x.Position).Distinct().Count(),
                Is.EqualTo(squareLine.Hits.Count));
            hashes.Add(new KeyValuePair<string, string>("SQH",
                SpecialGoldenHasher.HashEffectPlan(squareLine)));

            var matrixHash = SpecialGoldenHasher.HashNamedMatrix(hashes);
            Assert.That(matrixHash,
                Is.EqualTo("ea5816c5f26b53f932b823015ef4b78ad99a40a683e4eb314b4604c6d95c714d"));
            Assert.That(SpecialGoldenHasher.BindToCorpusVersion(matrixHash),
                Is.EqualTo("febdd294b4d2e3e9666c4f3e4500e60fcbd4bceda19ba797925beb6f0b72d49a"));
        }

        [Test]
        public void OptionalSquareGenerationIsProfileGatedDeterministicAndYieldsToSdkMatches()
        {
            var board = Full(2, 2);
            var disabled = new GameProfile("Coral_Rescue", "neutral",
                ProfileAgeRule.General, ProfileContentChannel.Development);
            Assert.That(OptionalSquareSpecialDetector.Detect(board, disabled), Is.Empty);

            var enabled = new GameProfile("Coral_Rescue", "neutral",
                ProfileAgeRule.General, ProfileContentChannel.Development,
                new[]
                {
                    new FeatureFlag("match_2x2", EngineCompatibility.MinimumCapability),
                    new FeatureFlag("targeted_missile", EngineCompatibility.MinimumCapability)
                });
            var before = board.Cells.OrderBy(x => x.Key.Row)
                .ThenBy(x => x.Key.Column)
                .Select(x => x.Key + "=" + x.Value.ContentId).ToArray();
            var candidates = OptionalSquareSpecialDetector.Detect(board, enabled,
                preferredAnchors: new[] { P(1, 1) });
            Assert.That(candidates.Count, Is.EqualTo(1));
            Assert.That(candidates[0].Anchor, Is.EqualTo(P(1, 1)));
            Assert.That(candidates[0].Cells.Count, Is.EqualTo(4));
            var after = board.Cells.OrderBy(x => x.Key.Row)
                .ThenBy(x => x.Key.Column)
                .Select(x => x.Key + "=" + x.Value.ContentId).ToArray();
            Assert.That(after, Is.EqualTo(before));

            Assert.That(OptionalSquareSpecialDetector.Detect(board, enabled,
                sdkReservedCells: new[] { P(0, 0) }), Is.Empty);

            var partial = new GameProfile("Coral_Rescue", "neutral",
                ProfileAgeRule.General, ProfileContentChannel.Development,
                new[] { new FeatureFlag("match_2x2", EngineCompatibility.MinimumCapability) });
            Assert.That(OptionalSquareSpecialDetector.Detect(board, partial), Is.Empty);

            var lockedSquare = Full(2, 2, (r, col) =>
                new SpecialCellState(P(r, col), "A", locked: r == 1 && col == 1));
            Assert.That(OptionalSquareSpecialDetector.Detect(lockedSquare, enabled), Is.Empty);
        }

        [Test]
        public void TargetedMissilePrioritizesGoalsUsesSeededTiesAndHandlesZeroTargetImmunityAndLayers()
        {
            var cells = new[]
            {
                new SpecialCellState(P(0,0), "A", goalPriority: 2),
                new SpecialCellState(P(0,1), "B", goalPriority: 2),
                new SpecialCellState(P(0,2), "C", obstacleLayers: 3),
                new SpecialCellState(P(1,0), "D", immune: true, goalPriority: 9),
                new SpecialCellState(P(1,1), null),
                new SpecialCellState(P(1,2), "A", locked: true, goalPriority: 8)
            };
            var board = new SpecialBoardSnapshot(2, 3, cells);
            var missile = new SpecialActivation(SpecialActivationKind.Targeted, P(1, 1));
            var a = SpecialEffectPlanner.Resolve(board, missile, seed: 77u);
            var b = SpecialEffectPlanner.Resolve(board, missile, seed: 77u);
            Assert.That(a.Hits.Count, Is.EqualTo(1));
            Assert.That(b.Hits[0].Position, Is.EqualTo(a.Hits[0].Position));
            Assert.That(new[] { P(0,0), P(0,1) }, Does.Contain(a.Hits[0].Position));
            Assert.That(a.Hits[0].Position, Is.Not.EqualTo(P(1,0)));
            Assert.That(a.Hits[0].Position, Is.Not.EqualTo(P(1,2)));

            Assert.Throws<InvalidOperationException>(() =>
                SpecialEffectPlanner.Resolve(board, missile,
                    seed: 77u, maximumTargetCandidates: 1));

            var immuneOnly = new SpecialBoardSnapshot(1, 1,
                new[] { new SpecialCellState(P(0,0), "A", immune: true) });
            Assert.That(SpecialEffectPlanner.Resolve(immuneOnly,
                new SpecialActivation(SpecialActivationKind.Targeted, P(0,0))).Hits,
                Is.Empty);

            var layered = new SpecialBoardSnapshot(1, 1,
                new[] { new SpecialCellState(P(0,0), null, obstacleLayers: 3) });
            var hit = SpecialEffectPlanner.Resolve(layered,
                new SpecialActivation(SpecialActivationKind.Targeted, P(0,0))).Hits.Single();
            Assert.That(hit.ObstacleLayersBefore, Is.EqualTo(3));
            Assert.That(hit.ObstacleLayersAfter, Is.EqualTo(2));
        }

        [Test]
        public void ExtendedTargetedCombosRetargetRocketBombAndColorWithUniqueMultiStageDamage()
        {
            var board = Full(3, 3, (r, c) => new SpecialCellState(P(r, c),
                (r + c) % 2 == 0 ? "A" : "B",
                goalPriority: r == 1 && c == 1 ? 5 : 0));
            var targeted = new SpecialActivation(SpecialActivationKind.Targeted, P(0, 0));

            var rocket = SpecialEffectPlanner.Resolve(board, targeted,
                new SpecialActivation(SpecialActivationKind.HorizontalLine, P(2, 2)),
                seed: 5u);
            Assert.That(rocket.Activations[0].Kind, Is.EqualTo(SpecialActivationKind.Targeted));
            Assert.That(rocket.Activations.Any(x =>
                x.Kind == SpecialActivationKind.HorizontalLine &&
                x.Origin.Equals(P(1, 1))), Is.True);

            var bomb = SpecialEffectPlanner.Resolve(board, targeted,
                new SpecialActivation(SpecialActivationKind.Area, P(2, 2)),
                seed: 5u);
            Assert.That(bomb.Activations.Any(x =>
                x.Kind == SpecialActivationKind.Area &&
                x.Origin.Equals(P(1, 1))), Is.True);

            var color = SpecialEffectPlanner.Resolve(board, targeted,
                new SpecialActivation(SpecialActivationKind.Color, P(2, 2)),
                seed: 5u);
            Assert.That(color.SelectedColor, Is.EqualTo("A"));
            Assert.That(color.Hits.Select(x => x.Position).Distinct().Count(),
                Is.EqualTo(color.Hits.Count));
            Assert.That(color.Activations.Count, Is.GreaterThanOrEqualTo(2));
        }

        [Test]
        public void WinFinalePresentationCannotChangeCommittedOutcomeOrFirstClearReward()
        {
            var committed = new CommittedWinSnapshot("win-0001", 1250, true);
            var plan = WinFinalePlanner.Build(committed, 3, true, 100);

            Assert.That(plan.Outcome, Is.SameAs(committed));
            Assert.That(plan.Outcome.AuthoritativeScore, Is.EqualTo(1250));
            Assert.That(plan.Outcome.FirstClearAwarded, Is.True);
            Assert.That(plan.PresentationScoreDelta, Is.EqualTo(300));
            Assert.That(plan.AuthoritativeFinalScore, Is.EqualTo(1550));
            Assert.That(plan.FinalScore, Is.EqualTo(1550));
            Assert.That(plan.AuthoritativeRewardDelta, Is.EqualTo(0));
            Assert.That(plan.Events.Count(x =>
                x.Kind == FinalePresentationKind.ConvertMoveToSpecial), Is.EqualTo(3));
            Assert.That(plan.Events.Last().Kind, Is.EqualTo(FinalePresentationKind.CelebrationVfx));

            var replay = WinFinalePlanner.Build(committed, 3, true, 100);
            Assert.That(replay.AuthoritativeFinalScore, Is.EqualTo(plan.AuthoritativeFinalScore));
            Assert.That(replay.Events.Select(x => new[] { x.Sequence, (int)x.Kind, x.Value }).ToArray(),
                Is.EqualTo(plan.Events.Select(x => new[] { x.Sequence, (int)x.Kind, x.Value }).ToArray()));

            // Skip/interruption/cancelled VFX is presentation-only: consuming zero
            // events does not change the already deterministic authoritative final score.
            var interruptedBeforePresentation = plan.AuthoritativeFinalScore;
            Assert.That(interruptedBeforePresentation, Is.EqualTo(1550));
            Assert.That(committed.AuthoritativeScore, Is.EqualTo(1250));
            Assert.That(committed.FirstClearAwarded, Is.True);
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                WinFinalePlanner.Build(committed, -1, true, 100));
        }
    }
}
