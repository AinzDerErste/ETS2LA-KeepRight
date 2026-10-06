using System.Numerics;
using ETS2LA.Game.Data;
using PathLib;
using TruckLib.ScsMap;
using static KeepRight.RoadGeometry;

namespace KeepRight;

/// <summary>Route and road checks: may we move right, may we move left to overtake, is there room for it.</summary>
internal static class RouteRules
{
    /// <summary>Null when moving one lane right is fine, otherwise the reason it is not.</summary>
    public static string? ForbidsRight(PlannedPathData path, Vector3 pos)
    {
        var items = path.PlannedPath;
        var cur = View(items[0]);
        if (cur == null) return "no road";
        Side side = SideOf(items[0].laneIndex);
        int lane = LaneOf(items[0].laneIndex);
        if (lane < 0 || lane >= cur.LaneCount(side)) return "unknown lane";

        float t0 = Math.Clamp(cur.Factor(pos), 0f, 1f);
        float[] lat = Lateral(cur, side, t0);
        if (!lat.Any(x => x > lat[lane] + 2f)) return "already right-most";

        // The planner is already changing lanes for the route somewhere ahead, don't fight it.
        if (items.Any(it => it.targetLaneIndex != 0)) return "route needs a lane change";

        // Something changes on our right ahead: an exit or on-ramp lane appears (also as a lane variant of the
        // piece we are on) or the lane right of us ends. Changes on the left don't matter for moving right.
        int baseRight = RightLaneCount(cur, side, lane, t0);
        float dist = 0f;   // metres to the start of items[j]
        for (int j = 0; j < items.Count && dist <= Tuning.RouteLookahead; j++)
        {
            var v = j == 0 ? cur : View(items[j]);
            float remaining = j == 0 ? Remaining(items[0], t0) : items[j].GetLength();
            if (v == null) { dist += remaining; continue; }   // junctions are checked below
            Side sideJ = j == 0 ? side : SameCarriageway(items[0], side, items[j]);
            int count = v.LaneCount(sideJ);
            int laneJ = j == 0 ? lane : items[j].laneIndex != 0 ? LaneOf(items[j].laneIndex) : Math.Min(lane, count - 1);
            if (count == 0 || laneJ < 0 || laneJ >= count) { dist += remaining; continue; }
            bool back = Backwards(items[j]);
            float tEnd = back ? 0.02f : 0.98f, tStart = j == 0 ? t0 : back ? 0.98f : 0.02f;
            if (RightLaneCount(v, sideJ, laneJ, tEnd) != baseRight || RightLaneCount(v, sideJ, laneJ, tStart) != baseRight)
                return "exit/merge on the right ahead";
            dist += remaining;
        }

        // Next junction: Pathfinding plans from the lane we are in, so the planned path alone always "needs" our
        // lane. The alternative paths through the same junction tell whether it also works from further right.
        dist = Remaining(items[0], t0);
        for (int k = 1; k < items.Count && dist <= Tuning.JunctionLookahead; k++)
        {
            if (items[k].item is not ParsedPrefab) { dist += items[k].GetLength(); continue; }
            var road = View(items[k - 1]);
            var planned = items[k].prefabPath;
            if (road == null || planned == null) return "junction ahead";
            Side roadSide = SameCarriageway(items[0], side, items[k - 1]);
            float[] l = Lateral(road, roadSide, Backwards(items[k - 1]) ? 0.05f : 0.95f);

            int StartLane(PrefabPath p)
            {
                var start = p.Interpolate(0.01f);
                if (start == null) return -1;
                int best = road.Best(start.Value.Position);
                return best == 0 || SideOf(best) != roadSide ? -1 : LaneOf(best);
            }

            int plannedLane = StartLane(planned);
            if (plannedLane < 0 || plannedLane >= l.Length) return "junction ahead";
            bool plannedIsRightMost = !l.Any(x => x > l[plannedLane] + 2f);
            bool rightAlternative = items[k].otherPrefabPaths.Select(StartLane)
                .Any(s => s >= 0 && s < l.Length && l[s] > l[plannedLane] + 2f);
            if (!plannedIsRightMost && !rightAlternative) return "route uses a left lane at the next junction";
            break;
        }
        return null;
    }

    /// <summary>Is there a lane to our left on our carriageway, and if so, does the road ahead allow overtaking.</summary>
    public static (bool HasLeft, string? Reason) LeftLaneCheck(PlannedPathData path, Vector3 pos)
    {
        var items = path.PlannedPath;
        var cur = View(items[0]);
        if (cur == null) return (false, null);
        Side side = SideOf(items[0].laneIndex);
        int lane = LaneOf(items[0].laneIndex);
        if (lane < 0 || lane >= cur.LaneCount(side)) return (false, null);

        float t0 = Math.Clamp(cur.Factor(pos), 0f, 1f);
        float[] lat = Lateral(cur, side, t0);
        if (!lat.Any(x => x < lat[lane] - 2f)) return (false, null);

        if (items.Any(it => it.targetLaneIndex != 0)) return (true, "route needs a lane change");
        float dist = Remaining(items[0], t0);
        for (int j = 1; j < items.Count && dist <= Tuning.OvertakeLookahead; j++)
        {
            if (items[j].item is ParsedPrefab) return (true, "junction ahead");
            var v = View(items[j]);
            if (v != null && v.LaneCount(SameCarriageway(items[0], side, items[j])) != cur.LaneCount(side)) return (true, "lane count changes ahead");
            dist += items[j].GetLength();
        }
        return (true, null);
    }

    /// <summary>Null when the piece we are on has enough road left for a calm lane change, otherwise the reason.</summary>
    public static string? LaneChangeRoomProblem(PlannedPathData path, Vector3 pos, float speed)
    {
        float need = Math.Max(Tuning.MinLaneChangeMetres, speed * Tuning.MinLaneChangeSeconds);
        float left = RemainingOnCurrent(path, pos);
        return left < need ? $"not enough road for a lane change ({left:F0}/{need:F0} m)" : null;
    }
}
