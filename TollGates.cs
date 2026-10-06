using System.Numerics;
using System.Reflection;
using ETS2LA.Game.SDK;
using ETS2LA.Game.SiiFiles;
using ETS2LA.State;
using PathLib;

namespace KeepRight;

/// <summary>
///  Cruise control stops in front of every closed gate, but automatic toll barriers only open when the truck is
///  close. Gates on our route whose semaphore type is "barrier_automatic" are reported to the semaphore provider,
///  which shows them as open until the truck is 20 m away.
///  That needs `SemaphoreProvider.MarkDriveThroughGate`, which not every ETS2LA build has; without it this does nothing.
/// </summary>
internal static class TollGates
{
    static readonly MethodInfo? MarkDriveThroughGate =
        typeof(SemaphoreProvider).GetMethod("MarkDriveThroughGate", new[] { typeof(int), typeof(Vector3) });

    public static bool Supported => MarkDriveThroughGate != null;

    public static void MarkAutomaticGates(PlannedPathData path, Vector3 pos)
    {
        if (MarkDriveThroughGate == null) return;
        try
        {
            foreach (var gate in path.GetNextSemaphores(pos))
            {
                var sem = gate.Semaphore;
                var types = ProfileTypes(sem.Profile.ToString());
                if (types == null || types.Length == 0) continue;
                // The semaphore id is the index into the profile's type list (single-entry profiles apply to all).
                int index = types.Length == 1 ? 0 : (int)sem.SemaphoreId;
                if (index < 0 || index >= types.Length || !types[index].Contains("barrier_automatic")) continue;
                MarkDriveThroughGate.Invoke(SemaphoreProvider.Current, new object[] { (int)sem.SemaphoreId, gate.GetWorldOrientedPoint().Position });
            }
        }
        catch { }
    }

    // Semaphore profile name -> its "type" list from the game's semaphore_profile files ("barrier_manual", ...).
    static Dictionary<string, string[]>? profiles;
    static object? profilesSource;

    static string[]? ProfileTypes(string profile)
    {
        var fs = ApplicationState.Current.RunningGame?.GetFileSystem();
        if (fs == null) return null;
        if (profiles == null || !ReferenceEquals(profilesSource, fs))
        {
            var cache = new Dictionary<string, string[]>();
            foreach (var file in fs.GetFiles("/def/world/").Where(f => f.EndsWith(".sii") && f.Contains("semaphore_profile")))
            {
                var sii = SiiFileHandler.Current.GetSiiFile(file);
                if (sii == null) continue;
                foreach (var unit in sii.Units)
                    if (unit.Attributes.TryGetValue("type", out var t) && t is not string && t is System.Collections.IEnumerable list)
                        cache[unit.Name.Split('.').Last()] = list.Cast<object?>()
                            .Select(e => (e?.ToString() ?? "").Replace("LinkPointer { Value = ", "").Replace(" }", "").Trim()).ToArray();
            }
            profiles = cache;
            profilesSource = fs;
        }
        profiles.TryGetValue(profile, out var types);
        return types;
    }
}
