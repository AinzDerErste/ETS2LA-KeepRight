using System.Diagnostics;
using System.Numerics;
using ETS2LA.Backend.Events;
using ETS2LA.Game.Data;
using ETS2LA.Game.Output;
using ETS2LA.Game.Telemetry;
using ETS2LA.Logging;
using ETS2LA.Shared;
using ETS2LA.State;
using PathLib;

namespace KeepRight;

/// <summary>
///  Keeps the truck in the right-most lane and overtakes slower vehicles. It never steers itself: it switches
///  the indicator on like a driver would, and Pathfinding does the lane change.
/// </summary>
public class KeepRightPlugin : Plugin
{
    public override float TickRate => 10f;

    public override PluginInformation Info => new()
    {
        Id = "legatum.keepright",
        Name = "Keep Right",
        Description = "Keeps the truck in the right lane and overtakes slower vehicles, while respecting the route, exits, traffic and junctions. Works on top of Pathfinding by using the indicator, like a driver would.",
        AuthorName = "Legatum",
        Version = "0.2.0",
        SupportedETS2LA = ">=3.4.36",
        Tags = new[] { "Utility", "Navigation" },
        Dependencies = { "tumppi066.pathfinding" },
    };

    readonly PluginConfig config = new();
    readonly StatusDisplay display;

    PlannedPathData? path;
    GameTelemetryData? telemetry;
    Action<PlannedPathData>? onPath;
    Action<GameTelemetryData>? onTelemetry;

    Vector3 lastPos;
    Vector2 heading = new(0, -1);   // from our own movement, avoids guessing the telemetry rotation convention

    readonly Stopwatch sinceLaneChange = Stopwatch.StartNew();
    readonly Stopwatch sinceTry = Stopwatch.StartNew();
    readonly Stopwatch sinceMove = Stopwatch.StartNew();
    readonly Stopwatch sinceStatusLog = Stopwatch.StartNew();

    bool triggered, triggeredLeft;
    Kind moveKind = Kind.Moving;
    string moveText = "moving right";
    string? lastReason;
    LaneBand? band;   // band of this tick (only built while overtaking is wanted)

    public KeepRightPlugin() => display = new StatusDisplay(config);

    public override void OnEnable()
    {
        base.OnEnable();
        config.Load();
        Logger.Info($"KeepRight: extras - band without near fade: {(StatusDisplay.NearFadeOptional ? "yes" : "no")}, drive-through toll gates: {(TollGates.Supported ? "yes" : "no")}.");
        display.Register();
        onPath = d => path = d;
        onTelemetry = d => telemetry = d;
        Events.Current.Subscribe("Pathfinding.PlannedPathData", onPath);
        Events.Current.Subscribe(GameTelemetry.Current.EventString, onTelemetry);
    }

    public override void OnDisable()
    {
        base.OnDisable();
        display.Unregister();
        if (onPath != null) Events.Current.Unsubscribe("Pathfinding.PlannedPathData", onPath);
        if (onTelemetry != null) Events.Current.Unsubscribe(GameTelemetry.Current.EventString, onTelemetry);
    }

    public override void Tick()
    {
        band = null;
        // If the indicator is still on 10 s after we switched it on, nobody picked it up: switch it off again.
        if (triggered && sinceTry.Elapsed.TotalSeconds > 10 && telemetry != null)
        {
            triggered = false;
            if (triggeredLeft ? telemetry.truckBool.blinkerLeftActive : telemetry.truckBool.blinkerRightActive) ToggleIndicator(triggeredLeft);
        }
        if (path == null || telemetry == null) { Idle("waiting for data"); return; }
        if (path.HasError || path.PlannedPath.Count == 0) { Idle("no planned path: " + path.ErrorMessage); return; }

        Vector3 pos = telemetry.truckPlacement.coordinate.ToVector3();
        TollGates.MarkAutomaticGates(path, pos);
        if (!ApplicationState.Current.EnableAssists) { Idle("assists off"); return; }

        var delta = new Vector2(pos.X - lastPos.X, pos.Z - lastPos.Z);
        if (delta.Length() > 0.5f) { heading = Vector2.Normalize(delta); lastPos = pos; }

        PlannedItem current = path.PlannedPath[0];
        bool indicatorOn = telemetry.truckBool.blinkerLeftActive || telemetry.truckBool.blinkerRightActive;
        if (current.targetLaneIndex != 0 || indicatorOn) sinceLaneChange.Restart();

        // Junctions, tolls and borders (prefabs) are left alone.
        if (current.item is not (ParsedRoad or ParsedRoadList)) { Idle("not on a plain road (junction/toll)"); return; }
        if (current.targetLaneIndex != 0) { Idle("lane change running"); return; }
        if (config.Debug) ShowDebug(LaneBand.ForNeighbour(path, pos, heading, toLeft: false, out var text), text);
        if (current.laneIndex == 0) { Idle("no lane"); return; }
        if (indicatorOn) { Idle("indicator on"); return; }
        if (telemetry.truckFloat.speed < Tuning.MinSpeed) { Idle("too slow"); return; }
        if (sinceLaneChange.Elapsed.TotalSeconds < Tuning.MinSecondsInLane) { Idle("just changed lane"); return; }
        if (sinceTry.Elapsed.TotalSeconds < Tuning.MinSecondsBetweenTries) { Idle("cooling down"); return; }

        if (TryOvertake(pos)) return;

        string? reason = RouteRules.ForbidsRight(path, pos);
        if (reason != lastReason) { lastReason = reason; if (reason != null) Logger.Info($"KeepRight: staying in lane ({reason})."); }
        reason ??= TrafficWatch.RightSideBlocked(pos, heading, telemetry.truckFloat.speed) ? "overtaking (vehicle on the right)" : null;
        reason ??= RouteRules.LaneChangeRoomProblem(path, pos, telemetry.truckFloat.speed);
        if (reason != null) { Idle(reason); return; }

        Logger.Info($"KeepRight: lane {current.laneIndex} -> moving right.");
        StartLaneChange(left: false, Kind.Moving, "moving right");
    }

    /// <summary>
    ///  True when overtaking is wanted, so nothing else may happen this tick: it either starts the lane change
    ///  or reports why it can't right now.
    /// </summary>
    bool TryOvertake(Vector3 pos)
    {
        float own = telemetry!.truckFloat.speed;
        float desired = ApplicationState.Current.DesiredSpeed;
        if (desired < Tuning.MinSpeed) return false;
        var leader = TrafficWatch.FindLeader(pos, heading);
        if (leader == null || leader.speed > desired - Tuning.OvertakeAdvantage) return false;

        var (hasLeft, reason) = RouteRules.LeftLaneCheck(path!, pos);
        if (!hasLeft) return false;   // single lane, or only oncoming traffic beside us

        band = LaneBand.ForNeighbour(path!, pos, heading, toLeft: true, out var text);
        if (config.Debug) ShowDebug(band, text);
        reason ??= TrafficWatch.LeftSideBlocked(pos, heading, own, desired) ? "traffic on the left" : null;
        reason ??= RouteRules.LaneChangeRoomProblem(path!, pos, own);
        if (reason != null) { Idle("overtake wanted, " + reason); return true; }

        Logger.Info($"KeepRight: overtaking a vehicle going {leader.speed * 3.6f:F0} km/h (we want {desired * 3.6f:F0}).");
        StartLaneChange(left: true, Kind.Overtake, "overtaking");
        return true;
    }

    void StartLaneChange(bool left, Kind kind, string text)
    {
        sinceTry.Restart();
        sinceMove.Restart();
        triggered = true;
        triggeredLeft = left;
        moveKind = kind;
        moveText = text;
        display.Status = new Status(kind, text, kind == Kind.Overtake ? band : null);
        ToggleIndicator(left);
    }

    void ToggleIndicator(bool left) => Events.Current.Publish(GameOutput.Current.EventString, new ControlEvent
    {
        ChannelDefinition = new ControlChannelDefinition { Id = left ? "KeepRight.IndicateLeft" : "KeepRight.IndicateRight", Timeout = 0.1f },
        Properties = new ControlProperties { BooleanType = ControlBooleanType.TrueToToggle },
        Variables = left ? new ControlVariables { lblinker = true } : new ControlVariables { rblinker = true },
    });

    static Kind KindOf(string why) => why switch
    {
        "already right-most" => Kind.RightMost,
        "just changed lane" or "cooling down" => Kind.Waiting,
        "too slow" or "assists off" or "waiting for data" or "no lane" => Kind.Inactive,
        _ when why.StartsWith("no planned path") || why.StartsWith("not on a plain road") => Kind.Inactive,
        _ => Kind.Blocked,
    };

    /// <summary>Shows why nothing happens this tick (logged at most every 5 s).</summary>
    void Idle(string why)
    {
        bool moving = sinceMove.Elapsed.TotalSeconds < 4;   // keep showing a lane change that just started
        Kind kind = moving ? moveKind : KindOf(why);
        // Bands only while overtaking: green = moving left now, yellow = wanted but not possible right now.
        bool showBand = kind == Kind.Overtake || (kind == Kind.Blocked && why.StartsWith("overtake wanted"));
        display.Status = new Status(kind, moving ? moveText : why, showBand ? band : null);
        if (sinceStatusLog.Elapsed.TotalSeconds < 5) return;
        sinceStatusLog.Restart();
        Logger.Info($"KeepRight: idle ({why}).");
    }

    void ShowDebug(LaneBand? neighbour, string? text)
    {
        if (text == null) return;
        display.DebugText = text;
        var s = display.Status;
        config.DebugLog($"{text.Replace("\n", " | ")} | band pts {neighbour?.Center.Length ?? 0} | status {s.Kind} {s.Text}");
    }
}
