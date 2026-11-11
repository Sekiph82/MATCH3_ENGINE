using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Match3.Engine.Runtime.LiveOps
{
    // ENG-M20-014: ten-level journey strip and next-level preview, schema "journey-preview/1.0".
    //
    // Read-only projection of content already accepted by ContentMetadataReceiver (ENG-M20-010 World/Level
    // metadata; no new content kind) plus the player's completed levels from the ENG-M08-015 JourneySnapshot.
    // - Canonical order: world Order, then level Order inside the world (ordinal content ID tie-break).
    // - The journey stops (ContentGap) at the first skipped world order, world with no levels, or missing level
    //   order inside a world; missing content is never invented. Levels whose world metadata is absent cannot be
    //   ordered and are listed as unplaced.
    // - Producer "x-" extension fields are covered by the receiver's payloadSha256 (digest over the whole
    //   payload, so they change PayloadSha256) but are never surfaced in any view.
    // - Progress across strips: ENG-M08-015 JourneyPlanner is scoped to one 10-level JourneyDefinition, so
    //   JourneyProgressAdapter merges the per-strip snapshots (or pass the persisted completed-level set directly).
    // - Strip = the aligned block of ten levels holding the current level (or the last block at end of content).
    // - Teaser = the current level's own objectives/obstacles/moves exactly as published; "new" obstacles are
    //   the ones not present in any earlier journey level. No scores, grants, prices, offers or paywalls:
    //   every view type is sealed, get-only and has no public constructor.
    public enum JourneyNodeState { Completed, Current, Locked }

    public enum JourneyStatus
    {
        Active,
        EndOfContent,
        ContentGap,
        NoContent
    }

    public sealed class JourneyNode
    {
        public int Position { get; }
        public string LevelId { get; }
        public string WorldId { get; }
        public JourneyNodeState State { get; }
        public bool IsWorldEnd { get; }
        public bool IsCheckpoint { get; }

        internal JourneyNode(int position, string levelId, string worldId, JourneyNodeState state,
            bool isWorldEnd)
        {
            Position = position;
            LevelId = levelId;
            WorldId = worldId;
            State = state;
            IsWorldEnd = isWorldEnd;
            IsCheckpoint = isWorldEnd || position % JourneyPreviewBuilder.StripLength == 0;
        }
    }

    public sealed class NextLevelTeaser
    {
        public string LevelId { get; }
        public string WorldId { get; }
        public int ContentVersion { get; }
        public string PayloadSha256 { get; }
        public ContentIntegrity Integrity { get; }
        public int Moves { get; }
        public IReadOnlyList<LevelObjective> Objectives { get; }
        public IReadOnlyList<string> ObstacleIds { get; }
        public IReadOnlyList<string> NewObstacleIds { get; }

        internal NextLevelTeaser(ContentMetadataItem level, IReadOnlyList<string> newObstacleIds)
        {
            LevelId = level.ContentId;
            WorldId = level.WorldId;
            ContentVersion = level.ContentVersion;
            PayloadSha256 = level.PayloadSha256;
            Integrity = level.Integrity;
            Moves = level.Moves;
            Objectives = level.Objectives;
            ObstacleIds = level.ObstacleIds;
            NewObstacleIds = newObstacleIds;
        }
    }

    public sealed class JourneyStripView
    {
        public const string SchemaVersion = "journey-preview/1.0";

        public string GameId { get; }
        public JourneyStatus Status { get; }
        public int TotalJourneyLevels { get; }
        public IReadOnlyList<JourneyNode> Nodes { get; }
        /// <summary>Null unless Status is Active.</summary>
        public NextLevelTeaser Next { get; }
        public IReadOnlyList<string> UnplacedLevelIds { get; }

        internal JourneyStripView(string gameId, JourneyStatus status, int total, IList<JourneyNode> nodes,
            NextLevelTeaser next, IList<string> unplaced)
        {
            GameId = gameId;
            Status = status;
            TotalJourneyLevels = total;
            Nodes = new ReadOnlyCollection<JourneyNode>(nodes);
            Next = next;
            UnplacedLevelIds = new ReadOnlyCollection<string>(unplaced);
        }
    }

    public static class JourneyPreviewBuilder
    {
        public const int StripLength = 10;

        public static JourneyStripView Build(ContentMetadataReceiver content, JourneySnapshot progress)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            if (progress == null) throw new ArgumentNullException(nameof(progress));

            var items = content.Applied.Values.ToList();
            var worlds = items.Where(i => i.Kind == ContentKind.World)
                .OrderBy(w => w.Order).ThenBy(w => w.ContentId, StringComparer.Ordinal).ToList();
            var worldIds = new HashSet<string>(worlds.Select(w => w.ContentId), StringComparer.Ordinal);
            var levelsByWorld = items.Where(i => i.Kind == ContentKind.Level && worldIds.Contains(i.WorldId))
                .GroupBy(l => l.WorldId, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.OrderBy(l => l.Order)
                    .ThenBy(l => l.ContentId, StringComparer.Ordinal).ToList(), StringComparer.Ordinal);
            var unplaced = items.Where(i => i.Kind == ContentKind.Level && !worldIds.Contains(i.WorldId))
                .Select(l => l.ContentId)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();
            // World end = last published level of its world, so a gap never fakes a world boundary.
            var worldEnds = new HashSet<string>(levelsByWorld.Values.Select(l => l.Last().ContentId),
                StringComparer.Ordinal);

            // Truncate at the first missing world order, world without levels, or missing level order.
            var journey = new List<ContentMetadataItem>();
            var gap = false;
            for (var w = 0; w < worlds.Count && !gap; w++)
            {
                if (worlds[w].Order != w + 1 || !levelsByWorld.TryGetValue(worlds[w].ContentId, out var worldLevels))
                {
                    gap = true;
                    break;
                }
                for (var i = 0; i < worldLevels.Count; i++)
                {
                    if (worldLevels[i].Order != i + 1) { gap = true; break; }
                    journey.Add(worldLevels[i]);
                }
            }

            var completed = new HashSet<string>(progress.CompletedLevelIds, StringComparer.Ordinal);
            var current = journey.FindIndex(l => !completed.Contains(l.ContentId));
            var status = current >= 0 ? JourneyStatus.Active
                : gap ? JourneyStatus.ContentGap
                : journey.Count == 0 ? JourneyStatus.NoContent
                : JourneyStatus.EndOfContent;

            var nodes = new List<JourneyNode>();
            if (journey.Count > 0)
            {
                var anchor = current >= 0 ? current : journey.Count - 1;
                var start = anchor / StripLength * StripLength;
                for (var i = start; i < Math.Min(start + StripLength, journey.Count); i++)
                {
                    var level = journey[i];
                    var state = completed.Contains(level.ContentId) ? JourneyNodeState.Completed
                        : i == current ? JourneyNodeState.Current
                        : JourneyNodeState.Locked;
                    nodes.Add(new JourneyNode(i + 1, level.ContentId, level.WorldId, state,
                        worldEnds.Contains(level.ContentId)));
                }
            }

            NextLevelTeaser next = null;
            if (current >= 0)
            {
                var seen = new HashSet<string>(journey.Take(current).SelectMany(l => l.ObstacleIds),
                    StringComparer.Ordinal);
                var fresh = journey[current].ObstacleIds.Where(o => !seen.Contains(o)).ToList();
                next = new NextLevelTeaser(journey[current], new ReadOnlyCollection<string>(fresh));
            }

            return new JourneyStripView(content.GameId, status, journey.Count, nodes, next, unplaced);
        }
    }

    public static class JourneyProgressAdapter
    {
        /// <summary>
        /// Merges ENG-M08-015 per-strip (JourneyDefinition, JourneySnapshot) pairs of one game/profile/mode into
        /// the completed-level set the preview reads. Each snapshot is validated against its own strip with
        /// JourneyPlanner (throws ArgumentException on foreign levels/records).
        /// </summary>
        public static JourneySnapshot CompletedAcrossStrips(string gameId,
            IEnumerable<(JourneyDefinition Definition, JourneySnapshot Snapshot)> strips)
        {
            if (strips == null) throw new ArgumentNullException(nameof(strips));
            var completed = new HashSet<string>(StringComparer.Ordinal);
            JourneyDefinition first = null;
            foreach (var (definition, snapshot) in strips)
            {
                if (definition == null || snapshot == null) throw new ArgumentNullException(nameof(strips));
                first = first ?? definition;
                if (!StringComparer.Ordinal.Equals(definition.GameId, gameId) ||
                    !StringComparer.Ordinal.Equals(definition.ProfileId, first.ProfileId) ||
                    !StringComparer.Ordinal.Equals(definition.ModeId, first.ModeId))
                    throw new ArgumentException("Journey strips must share one game/profile/mode.", nameof(strips));
                JourneyPlanner.UpcomingLevel(definition, snapshot); // validation only
                completed.UnionWith(snapshot.CompletedLevelIds);
            }
            return new JourneySnapshot(completed);
        }
    }
}
