using System.Numerics;
using ETS2LA.Game.Data;
using PathLib;
using TruckLib.ScsMap;
using static KeepRight.RoadGeometry;

namespace KeepRight;

/// <summary>Band covering a lane: left edge, right edge and centre line, world positions in travel order.</summary>
internal record LaneBand(Vector3[] Left, Vector3[] Right, Vector3[] Center)
{
    /// <summary>
    ///  Band on the lane left (or right) of ours for the next ~300 m, or null when there is no such lane.
    ///  `debugText` describes the lanes found, for the debug view.
    /// </summary>
    public static LaneBand? ForNeighbour(PlannedPathData path, Vector3 pos, Vector2 heading, bool toLeft, out string? debugText)
    {
        debugText = null;
        try
        {
            var item = path.PlannedPath[0];
            var cur = View(item);
            if (cur == null) return null;
            Side side = SideOf(item.laneIndex);
            int lane = LaneOf(item.laneIndex);
            if (lane < 0 || lane >= cur.LaneCount(side)) return null;

            float t0 = Math.Clamp(cur.Factor(pos), 0f, 1f);
            float[] lat = Lateral(cur, side, t0);
            int n = -1;
            for (int i = 0; i < lat.Length; i++)
            {
                if (toLeft) { if (lat[i] < lat[lane] - 2f && (n < 0 || lat[i] > lat[n])) n = i; }
                else if (lat[i] > lat[lane] + 2f && (n < 0 || lat[i] < lat[n])) n = i;
            }

            debugText = $"item {item.item.GetType().Name} len {item.GetLength():F0}m t0 {t0:F2} dir {(int)item.direction}\n" +
                        $"laneIndex {item.laneIndex} side {side} lane {lane} lanes {cur.LaneCount(side)}\n" +
                        $"lateral(right+) {string.Join(" ", lat.Select((x, i) => $"{i}:{x:F1}"))}\n" +
                        $"{(toLeft ? "left" : "right")} neighbour {n}  heading {heading.X:F2},{heading.Y:F2}";
            if (n < 0) return null;

            return FromCentre(SampleDivergedLane(cur, side, lane, n, t0, Backwards(item), 300f, 5f, 4f));
        }
        catch
        {
            return null;   // purely visual, must never affect the driving logic
        }
    }

    static LaneBand? FromCentre(List<Vector3> pts)
    {
        var l = new List<Vector3>(); var r = new List<Vector3>(); var c = new List<Vector3>();
        for (int i = 0; i < pts.Count; i++)
        {
            Vector3 a = pts[Math.Max(i - 1, 0)], b = pts[Math.Min(i + 1, pts.Count - 1)];
            var fwd = new Vector2(b.X - a.X, b.Z - a.Z);
            if (fwd.LengthSquared() < 1e-6f) continue;
            fwd = Vector2.Normalize(fwd);
            var half = new Vector3(-fwd.Y, 0, fwd.X) * Tuning.HalfLaneWidth;   // to the right of travel
            var p = pts[i] with { Y = pts[i].Y + 0.1f };
            l.Add(p - half); r.Add(p + half); c.Add(p);
        }
        return l.Count < 2 ? null : new LaneBand(l.ToArray(), r.ToArray(), c.ToArray());
    }
}
