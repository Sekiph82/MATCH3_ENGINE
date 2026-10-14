using System;
using System.Collections.Generic;
using System.Linq;
using Match3.Engine.Runtime;
using NUnit.Framework;

namespace Match3.Engine.Tests
{
    public sealed class TransportContractTests
    {
        private static BoardPosition P(int r, int c, int layer = 0) =>
            new BoardPosition(r, c, layer);

        [Test]
        public void ConveyorMovesAcyclicChainSimultaneouslyInStableOrder()
        {
            var board = new BoardTopology(1, 4,
                occupants: new[]
                {
                    new BoardOccupant(P(0,0), "A"),
                    new BoardOccupant(P(0,1), "B")
                });
            var edges = new[]
            {
                new ConveyorEdge(P(0,0), P(0,1), 0),
                new ConveyorEdge(P(0,1), P(0,2), 0)
            };

            var plan = ConveyorPlanner.Plan(board, board.Occupants, edges);

            Assert.That(plan.Occupants.Count, Is.EqualTo(2));
            Assert.That(plan.Occupants[P(0,1)], Is.EqualTo("A"));
            Assert.That(plan.Occupants[P(0,2)], Is.EqualTo("B"));
            Assert.That(plan.Moves.Count, Is.EqualTo(2));
            Assert.That(plan.Moves[0].Source, Is.EqualTo(P(0,0)));
            Assert.That(plan.Moves[1].Source, Is.EqualTo(P(0,1)));
            Assert.That(board.Occupants[P(0,0)], Is.EqualTo("A"));
        }

        [Test]
        public void ConveyorCollisionUsesPriorityAndRejectsCyclesLocksAndAmbiguousSources()
        {
            var board = new BoardTopology(2, 3,
                locked: new[] { P(1,2) },
                occupants: new[]
                {
                    new BoardOccupant(P(0,0), "A"),
                    new BoardOccupant(P(1,1), "B")
                });
            var collision = ConveyorPlanner.Plan(board, board.Occupants, new[]
            {
                new ConveyorEdge(P(0,0), P(0,1), 5),
                new ConveyorEdge(P(1,1), P(0,1), 1)
            });
            Assert.That(collision.Moves.Count, Is.EqualTo(1));
            Assert.That(collision.Moves[0].Source, Is.EqualTo(P(1,1)));
            Assert.That(collision.Occupants[P(0,1)], Is.EqualTo("B"));
            Assert.That(collision.Occupants[P(0,0)], Is.EqualTo("A"));

            Assert.Throws<ArgumentException>(() =>
                ConveyorPlanner.Plan(new BoardTopology(1,2,
                    occupants: new[]
                    {
                        new BoardOccupant(P(0,0), "A"),
                        new BoardOccupant(P(0,1), "B")
                    }),
                    new Dictionary<BoardPosition,string>
                    {
                        { P(0,0), "A" }, { P(0,1), "B" }
                    },
                    new[]
                    {
                        new ConveyorEdge(P(0,0), P(0,1)),
                        new ConveyorEdge(P(0,1), P(0,0))
                    }));

            Assert.Throws<ArgumentException>(() =>
                ConveyorPlanner.Plan(board, board.Occupants,
                    new[] { new ConveyorEdge(P(0,0), P(1,2)) }));

            Assert.Throws<ArgumentException>(() =>
                ConveyorPlanner.Plan(board, board.Occupants,
                    new[]
                    {
                        new ConveyorEdge(P(0,0), P(0,1)),
                        new ConveyorEdge(P(0,0), P(1,0))
                    }));
        }

        [Test]
        public void ConveyorSchemaRoundTripsCanonicallyAndRejectsVersionDrift()
        {
            var edges = new[]
            {
                new ConveyorEdge(P(1,0), P(1,1), 2),
                new ConveyorEdge(P(0,0), P(0,1), 1)
            };
            var encoded = ConveyorSchema.Encode(edges);
            var decoded = ConveyorSchema.Decode(encoded);
            Assert.That(ConveyorSchema.Encode(decoded), Is.EqualTo(encoded));
            Assert.That(encoded, Does.StartWith("v1|0,0,0>0,0,1@1"));
            Assert.Throws<FormatException>(() => ConveyorSchema.Decode("v2|"));
            Assert.Throws<FormatException>(() => ConveyorSchema.Decode("v1|bad"));
        }

        [Test]
        public void PortalTeleportsAtMostOncePerPhaseAndBlockedPairDoesNotOverwrite()
        {
            var board = new BoardTopology(2, 3,
                occupants: new[] { new BoardOccupant(P(0,0), "A") });
            var registry = new PortalRegistry(new[]
            {
                new PortalPair("p1", P(0,0), P(1,2))
            });
            var plan = PortalPlanner.Plan(board, board.Occupants, registry);
            Assert.That(plan.Moves.Count, Is.EqualTo(1));
            Assert.That(plan.Moves[0].Source, Is.EqualTo(P(0,0)));
            Assert.That(plan.Moves[0].Target, Is.EqualTo(P(1,2)));
            Assert.That(plan.Occupants.Single().Key, Is.EqualTo(P(1,2)));

            var blockedBoard = new BoardTopology(2, 3,
                occupants: new[]
                {
                    new BoardOccupant(P(0,0), "A"),
                    new BoardOccupant(P(1,2), "B")
                });
            var blocked = PortalPlanner.Plan(blockedBoard, blockedBoard.Occupants, registry);
            Assert.That(blocked.Moves, Is.Empty);
            Assert.That(blocked.Occupants[P(0,0)], Is.EqualTo("A"));
            Assert.That(blocked.Occupants[P(1,2)], Is.EqualTo("B"));
        }

        [Test]
        public void PortalRegistryAndSchemaRejectMalformedPairsAndRoundTripStableIds()
        {
            var registry = new PortalRegistry(new[]
            {
                new PortalPair("b", P(0,2), P(1,2)),
                new PortalPair("a", P(0,0), P(1,0))
            });
            var encoded = PortalSchema.Encode(registry);
            var decoded = PortalSchema.Decode(encoded);
            Assert.That(PortalSchema.Encode(decoded), Is.EqualTo(encoded));
            Assert.That(encoded, Does.StartWith("v1|a="));

            Assert.Throws<ArgumentException>(() =>
                new PortalRegistry(new[]
                {
                    new PortalPair("a", P(0,0), P(1,0)),
                    new PortalPair("a", P(0,1), P(1,1))
                }));
            Assert.Throws<ArgumentException>(() =>
                new PortalRegistry(new[]
                {
                    new PortalPair("a", P(0,0), P(1,0)),
                    new PortalPair("b", P(0,0), P(1,1))
                }));
            Assert.Throws<ArgumentException>(() =>
                new PortalPair("same", P(0,0), P(0,0)));
            Assert.Throws<FormatException>(() => PortalSchema.Decode("v2|"));
            Assert.Throws<ArgumentException>(() => PortalSchema.Encode(
                new PortalRegistry(new[] { new PortalPair("bad;id", P(0,0), P(1,0)) })));
        }
    }
}
