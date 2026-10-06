# Keep Right (ETS2LA plugin)

Author: Legatum - plugin id `legatum.keepright`

Keeps the truck in the right-most lane and overtakes slower vehicles automatically.
It doesn't steer by itself: it sets the indicator like a driver would, and the
existing Pathfinding plugin does the lane change.

- Moves back to the right lane when not overtaking.
- Overtakes a vehicle that is noticeably slower than the set speed (e.g. 70 in a
  90 zone), but only if the left lane is free next to and behind the truck and no
  junction is coming up.
- Respects the route: stays left when the navigation needs a left lane at the next
  junction, and doesn't drift into exit or merging lanes.
- Only starts a lane change when there is enough road left for a smooth manoeuvre.
- Shows a green/yellow band on the target lane while overtaking, plus a small
  status window explaining what it's doing and why.
- Optional: drives through automatic toll gates without stopping (needs a small
  ETS2LA core addition, otherwise it's skipped).

Works with a stock ETS2LA build (Pathfinding required).

## What it does

- **Keep right:** when the truck is not in the right-most lane, it moves one lane
  right, unless:
  - the route needs a lane to the left at the next junction (checked up to 1000 m
    ahead, using the alternative prefab paths between the same nodes),
  - something changes on the right within 300 m (exit or on-ramp lane appears,
    or the right lane ends),
  - a vehicle is next to or just behind us on the right, or a slower one is
    ahead on the right (we are overtaking it),
  - the road piece we are on ends too soon for a calm lane change
    (Pathfinding changes lanes within the current piece; needs max(120 m, 6 s)),
  - speed is below ~30 km/h, an indicator is on, or we just changed lanes.
- **Overtake:** when a vehicle in our lane within 80 m is at least 4 m/s
  (~15 km/h) slower than the desired speed, it moves one lane left, if:
  - a lane to the left exists on our own carriageway,
  - no junction or lane-count change comes within 500 m,
  - the left lane is clear next to us (35 m behind / 25 m ahead), no faster
    vehicle closes in from behind within 6 s, and no slower vehicle is ahead in it,
  - there is enough road for the lane change (same rule as above).
- **Display:** a band on the target lane while overtaking (green = moving there,
  yellow = overtake wanted but not possible right now) and a small "Keep Right"
  window with the current state and reason. A "Debug" checkbox shows lane data and
  writes `keepright-debug.log`.

Lanes are compared by their real position (lateral offset in travel direction),
not by Pathfinding's lane numbers, so it works on both sides of a road.

## Optional extras

If the ETS2LA build has them, the plugin uses (looked up at runtime, the plugin
runs without them):

- `ARRenderer.Draw3DLineWithGradient(..., bool fadeNear)`: band without the near
  fade, so it is visible close to the truck.
- `SemaphoreProvider.MarkDriveThroughGate(int id, Vector3 pos)`: automatic toll
  barriers (semaphore profile type `barrier_automatic`, the semaphore id is the
  index into the profile's `type` list) are reported as open until 20 m before
  them, so cruise control drives through instead of stopping.

## Requirements

- ETS2LA 3.x with the plugins **Pathfinding** (dependency) and, for overtaking,
  **Adaptive Cruise Control** / Lane Assist.
- Settings: `~/.config/ETS2LA/KeepRight.json` (Linux) - only the Debug switch.

## Install

Copy `KeepRight.dll` directly into the ETS2LA `Plugins` folder (not a sub-folder):

- Linux: `~/.local/share/ETS2LA/Plugins/KeepRight.dll`
- Windows: the `Plugins` folder of the ETS2LA install directory

Restart ETS2LA and enable "Keep Right" in the plugin manager.

## Build

```sh
./build.sh                                  # against the local ETS2LA build
./build.sh /path/to/ETS2LA/bin/folder       # against another ETS2LA build
```

Output: `dist/KeepRight.dll` (and `dist/KeepRight.zip`). The project references
the ETS2LA assemblies from `ETS2LABin` and `PathLib.dll` from `PathLibDir`
(see `KeepRight.csproj`); they are compile-time only and not shipped.

## Tuning

All limits are constants in `Tuning.cs` (`MinSecondsInLane`, `OvertakeAdvantage`,
`RouteLookahead`, `JunctionLookahead`, `MinLaneChangeMetres`, ...).

## Code layout

| File | Purpose |
|---|---|
| `KeepRightPlugin.cs` | Plugin entry: enable/disable, the per-tick decision, indicator |
| `Tuning.cs` | All limits |
| `RoadGeometry.cs` | Lane geometry (lateral offsets, lane sampling, remaining length) |
| `RouteRules.cs` | May we move right / overtake, is there room for a lane change |
| `TrafficWatch.cs` | Vehicle ahead, right side clear, left side clear |
| `LaneBand.cs` | Band on the target lane |
| `StatusDisplay.cs` | Status, overlay window, band drawing |
| `TollGates.cs` | Automatic toll barriers (optional extra) |
| `PluginConfig.cs` | Debug switch and debug log |
