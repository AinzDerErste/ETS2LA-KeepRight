using System.Numerics;
using ETS2LA.Game.Data;
using PathLib;
using TruckLib;
using TruckLib.ScsMap;

namespace KeepRight;

/// <summary>The lane functions of a ParsedRoad or ParsedRoadList, which have no common interface.</summary>
internal record RoadView(Func<Side, int> LaneCount, Func<float, Side, int, OrientedPoint> Lane,
                         Func<Vector3, float> Factor, Func<Vector3, int> Best, PlannedItem Item);

/// <summary>
///  Lane geometry. Pathfinding's lane numbers say nothing about left/right on their own, so lanes are compared
///  by where they really are: lateral offset relative to our travel direction (positive = right).
/// </summary>
internal static class RoadGeometry
{
    public static RoadView? View(PlannedItem p) => p.item switch
    {
        ParsedRoad r => new RoadView(r.GetLaneCount, (t, s, i) => r.InterpolateLane(t, s, i), r.GetFactorForPoint, v => r.GetBestLaneFor(v, false, true), p),
        ParsedRoadList l => new RoadView(l.GetLaneCount, (t, s, i) => l.InterpolateLane(t, s, i), l.GetFactorForPoint, v => l.GetBestLaneFor(v, false, true), p),
        _ => null,
    };

    public static Side SideOf(int laneIndex) => laneIndex >= 0 ? Side.Right : Side.Left;
    public static int LaneOf(int laneIndex) => Math.Abs(laneIndex) - 1;
    public static bool Backwards(PlannedItem item) => (int)item.direction == 1;   // t runs against our travel direction

    /// <summary>The side of road piece `p` our carriageway is on, given that we are on `firstSide` of `first`.</summary>
    public static Side SameCarriageway(PlannedItem first, Side firstSide, PlannedItem p) =>
        (int)p.direction == (int)first.direction ? firstSide : (firstSide == Side.Right ? Side.Left : Side.Right);

    /// <summary>Lateral position (positive = right of travel) of every lane on this side at t, relative to lane 0.</summary>
    public static float[] Lateral(RoadView v, Side side, float t)
    {
        int n = v.LaneCount(side);
        var lat = new float[n];
        if (n == 0) return lat;
        float t2 = Math.Clamp(t + 0.02f, 0f, 1f), t1 = Math.Min(t, t2 - 0.01f);
        Vector3 a = v.Lane(t1, side, 0).Position, b = v.Lane(t2, side, 0).Position;
        var fwd = new Vector2(b.X - a.X, b.Z - a.Z);
        if (Backwards(v.Item)) fwd = -fwd;
        if (fwd.LengthSquared() < 1e-6f) return lat;
        fwd = Vector2.Normalize(fwd);
        var right = new Vector2(-fwd.Y, fwd.X);   // x = east, z = south
        for (int i = 0; i < n; i++)
        {
            Vector3 p = v.Lane(t1, side, i).Position;
            lat[i] = Vector2.Dot(new Vector2(p.X - a.X, p.Z - a.Z), right);
        }
        return lat;
    }

    public static int RightLaneCount(RoadView v, Side side, int lane, float t)
    {
        float[] lat = Lateral(v, side, t);
        return lat.Count(x => x > lat[lane] + 2f);
    }

    public static float Remaining(PlannedItem item, float t) => item.GetLength() * (Backwards(item) ? t : 1f - t);

    /// <summary>Metres left on the road piece we are on.</summary>
    public static float RemainingOnCurrent(PlannedPathData path, Vector3 pos)
    {
        var item = path.PlannedPath[0];
        var v = View(item);
        return v == null ? 0f : Remaining(item, Math.Clamp(v.Factor(pos), 0f, 1f));
    }

    /// <summary>
    ///  Points of lane `other` in travel direction, `step` m apart, that are at least `minGap` m away from lane `own`.
    ///  Distance is measured along the lane, the t parameter of a road list is not even in distance.
    ///  The gap check matters for lanes that split off: they start next to ours and only later move outwards.
    /// </summary>
    public static List<Vector3> SampleDivergedLane(RoadView v, Side side, int own, int other, float t0, bool backwards, float maxDist, float step, float minGap)
    {
        float dir = backwards ? -1f : 1f;
        var pts = new List<Vector3>();
        Vector3 prev = v.Lane(t0, side, other).Position;
        float acc = 0f, next = 0f;
        for (int i = 0; i <= 4000 && acc <= maxDist; i++)
        {
            float t = t0 + dir * i * 0.0005f;
            if (t < 0f || t > 1f) break;
            Vector3 po = v.Lane(t, side, other).Position;
            acc += i == 0 ? 0f : Vector3.Distance(po, prev);
            prev = po;
            if (acc < next) continue;
            next += step;
            Vector3 pa = v.Lane(t, side, own).Position;
            float gap = MathF.Sqrt((po.X - pa.X) * (po.X - pa.X) + (po.Z - pa.Z) * (po.Z - pa.Z));
            if (gap >= minGap || pts.Count > 0) pts.Add(po);
        }
        return pts;
    }
}
