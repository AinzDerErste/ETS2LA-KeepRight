using System.Diagnostics;
using System.Text.Json;

namespace KeepRight;

/// <summary>The Debug switch (saved in KeepRight.json) and the debug log it enables.</summary>
internal class PluginConfig
{
    static readonly string ConfigFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ETS2LA", "KeepRight.json");
    static readonly string DebugLogFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ETS2LA", "keepright-debug.log");

    class Stored { public bool Debug { get; set; } }

    public bool Debug { get; private set; }
    readonly Stopwatch sinceDebugLog = Stopwatch.StartNew();

    public void Load()
    {
        try { if (File.Exists(ConfigFile)) Debug = JsonSerializer.Deserialize<Stored>(File.ReadAllText(ConfigFile))?.Debug ?? false; } catch { }
    }

    public void SetDebug(bool value)
    {
        Debug = value;
        try { File.WriteAllText(ConfigFile, JsonSerializer.Serialize(new Stored { Debug = value })); } catch { }
    }

    /// <summary>At most one line per second, only in debug mode. The file is restarted at 5 MB.</summary>
    public void DebugLog(string line)
    {
        if (!Debug || sinceDebugLog.Elapsed.TotalSeconds < 1) return;
        sinceDebugLog.Restart();
        try
        {
            if (File.Exists(DebugLogFile) && new FileInfo(DebugLogFile).Length > 5_000_000) File.Delete(DebugLogFile);
            File.AppendAllText(DebugLogFile, $"{DateTime.Now:HH:mm:ss} {line}{Environment.NewLine}");
        }
        catch { }
    }
}
