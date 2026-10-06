using System.Numerics;
using ETS2LA.Game.SDK;

namespace KeepRight;

/// <summary>
///  Checks against the game's traffic. Positions are compared in the truck's frame: `heading` is the travel
///  direction on the ground (x = east, z = south), "along" is ahead of us, "side" is to the right.
/// </summary>
internal static class TrafficWatch
{
    static IEnumerable<(TrafficVehicle Vehicle, float Along, float Side)> Around(Vector3 pos, Vector2 heading)
    {
        var traffic = TrafficProvider.Current.GetCurrentTrafficData();
        if (traffic == null) yield break;
        var right = new Vector2(-heading.Y, heading.X);
        foreach (var v in traffic.vehicles)
        {
            if (v.isTrailer) continue;
            var d = new Vector2(v.Position.X - pos.X, v.Position.Z - pos.Z);
            yield return (v, Vector2.Dot(d, heading), Vector2.Dot(d, right));
        }
    }

    /// <summary>Nearest vehicle ahead in our own lane, or null.</summary>
    public static TrafficVehicle? FindLeader(Vector3 pos, Vector2 heading) =>
        Around(pos, heading)
            .Where(x => x.Along >= 3f && x.Along <= Tuning.OvertakeLeaderRange && Math.Abs(x.Side) <= 2.2f)
            .OrderBy(x => x.Along)
            .Select(x => x.Vehicle)
            .FirstOrDefault();

    /// <summary>A vehicle on the right next to us / just passed, or a slower one ahead we are catching up with.</summary>
    public static bool RightSideBlocked(Vector3 pos, Vector2 heading, float ownSpeed)
    {
        foreach (var (v, along, side) in Around(pos, heading))
        {
            if (side < Tuning.SideMin || side > Tuning.SideMax) continue;
            if (along > -Tuning.BackLength && along < Tuning.FrontLength) return true;
            if (along >= Tuning.FrontLength && along < Tuning.ClosingLength && v.speed < ownSpeed - 1f) return true;
        }
        return false;
    }

    /// <summary>A vehicle on the left next to us, coming up fast from behind, or slower ahead in the left lane.</summary>
    public static bool LeftSideBlocked(Vector3 pos, Vector2 heading, float ownSpeed, float desiredSpeed)
    {
        foreach (var (v, along, rightSide) in Around(pos, heading))
        {
            float side = -rightSide;
            if (side < Tuning.SideMin || side > Tuning.SideMax) continue;
            if (along > -Tuning.LeftBackLength && along < Tuning.LeftFrontLength) return true;
            if (along <= -Tuning.LeftBackLength && v.speed > ownSpeed && -along / (v.speed - ownSpeed) < Tuning.LeftBackSeconds) return true;
            if (along >= Tuning.LeftFrontLength && along < Tuning.OvertakeLeaderRange * 1.5f && v.speed < desiredSpeed - 1f) return true;
        }
        return false;
    }
}
