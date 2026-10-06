namespace KeepRight;

/// <summary>All limits in one place. Distances in metres, speeds in m/s, times in seconds.</summary>
internal static class Tuning
{
    // Keep right
    public const float MinSecondsInLane = 6f;        // don't hop back right right after a lane change
    public const float MinSecondsBetweenTries = 5f;
    public const float MinSpeed = 8f;                // ~30 km/h

    // Vehicles on the right next to us / just passed / slower ahead = we are overtaking them, stay left.
    public const float SideMin = 1.5f, SideMax = 6f, BackLength = 40f, FrontLength = 25f;
    public const float ClosingLength = 100f;

    // Exits / lane changes on the right are checked this far ahead, junctions further.
    // (A junction too close makes Pathfinding pull us back left a few seconds later.)
    public const float RouteLookahead = 300f;
    public const float JunctionLookahead = 1000f;

    // Pathfinding changes lanes within the current road piece. If that ends soon the change gets squeezed
    // into a few metres = violent steering.
    public const float MinLaneChangeSeconds = 6f, MinLaneChangeMetres = 120f;

    // Overtaking: vehicle in our lane this much slower than the desired speed and this close ahead.
    public const float OvertakeAdvantage = 4f, OvertakeLeaderRange = 80f;
    public const float OvertakeLookahead = 500f;
    public const float LeftBackLength = 35f, LeftFrontLength = 25f, LeftBackSeconds = 6f;

    // Band on the target lane, about as wide as a lane (4.5 m) minus a small gap.
    public const float HalfLaneWidth = 2.1f;
}
