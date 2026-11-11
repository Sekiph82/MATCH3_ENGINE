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
    // - The journey stops at the first missing level order inside a world (ContentGap); the missing level is
    //   never invented. Levels whose world metadata is absent cannot be ordered and are listed as unplaced.
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
            var worldOrder = items.Where(i => i.Kind == ContentKind.World)
                .ToDictionary(i => i.ContentId, i => i.Order, StringComparer.Ordinal);
            var levels = items.Where(i => i.Kind == ContentKind.Level).ToList();

            var placed = levels.Where(l => worldOrder.ContainsKey(l.WorldId))
                .OrderBy(l => worldOrder[l.WorldId])
                .ThenBy(l => l.Order)
                .ThenBy(l => l.ContentId, StringComparer.Ordinal)
                .ToList();
            var unplaced = levels.Where(l => !worldOrder.ContainsKey(l.WorldId))
                .Select(l => l.ContentId)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();

            // Truncate at the first missing level order inside a world.
            var journey = new List<ContentMetadataItem>();
            var gap = false;
            for (var i = 0; i < placed.Count; i++)
            {
                var expected = i > 0 && placed[i - 1].WorldId == placed[i].WorldId ? placed[i - 1].Order + 1 : 1;
                if (placed[i].Order != expected) { gap = true; break; }
                journey.Add(placed[i]);
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
                    // World end is judged on all placed content, so a gap never fakes a world boundary.
                    var worldEnd = i + 1 == placed.Count || placed[i + 1].WorldId != level.WorldId;
                    nodes.Add(new JourneyNode(i + 1, level.ContentId, level.WorldId, state, worldEnd));
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
}
