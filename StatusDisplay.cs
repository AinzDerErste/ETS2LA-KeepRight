using System.Numerics;
using System.Reflection;
using ETS2LA.Overlay;
using ETS2LA.Overlay.AR;
using Hexa.NET.ImGui;

namespace KeepRight;

internal enum Kind { Inactive, RightMost, Waiting, Blocked, Moving, Overtake }

/// <summary>What the overlay shows. Replaced as a whole, so the render thread never sees half an update.</summary>
internal record Status(Kind Kind, string Text, LaneBand? Band);

/// <summary>The "Keep Right" overlay window and the band on the target lane while overtaking.</summary>
internal class StatusDisplay
{
    // ETS2LA builds that have it offer a band overload without the fade close to the camera.
    static readonly MethodInfo? DrawWithoutNearFade = typeof(ARRenderer).GetMethod("Draw3DLineWithGradient",
        new[] { typeof(IReadOnlyList<ARCoordinate>), typeof(IReadOnlyList<ARCoordinate>), typeof(uint), typeof(float), typeof(uint), typeof(float), typeof(bool) });

    public static bool NearFadeOptional => DrawWithoutNearFade != null;

    readonly PluginConfig config;
    readonly WindowDefinition window = new() { Title = "Keep Right", Alpha = 0.7f };
    readonly ARRenderCallback arCallback = new() { Definition = new ARRendererDefinition { Name = "KeepRight Target Lane" } };

    volatile Status status = new(Kind.Inactive, "starting", null);
    volatile string debugText = "no data yet";

    public StatusDisplay(PluginConfig config) => this.config = config;

    public Status Status { get => status; set => status = value; }
    public string DebugText { set => debugText = value; }

    public void Register()
    {
        arCallback.Render3D = RenderBand;
        OverlayHandler.Current.AR.RegisterRenderCallback(arCallback);
        OverlayHandler.Current.RegisterWindow(window, DrawWindow);
    }

    public void Unregister()
    {
        OverlayHandler.Current.AR.UnregisterRenderCallback(arCallback.Definition.Name);
        OverlayHandler.Current.UnregisterWindow(window);
    }

    void DrawWindow()
    {
        var s = status;
        var green = new Vector4(0.24f, 0.86f, 0.52f, 1f);
        var grey = new Vector4(0.6f, 0.6f, 0.6f, 1f);
        var (color, label) = s.Kind switch
        {
            Kind.Moving => (green, "Moving right"),
            Kind.RightMost => (green, "Right lane"),
            Kind.Waiting => (green with { W = 0.8f }, "Moving right soon"),
            Kind.Overtake => (green, "Overtaking"),
            Kind.Blocked => (new Vector4(1f, 0.69f, 0.13f, 1f), "Staying in lane"),
            _ => (grey, "Inactive"),
        };
        ImGui.TextColored(color, label);
        ImGui.TextColored(grey, s.Text);
        bool debug = config.Debug;
        if (ImGui.Checkbox("Debug", ref debug)) config.SetDebug(debug);
        if (config.Debug) ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), debugText);
    }

    void RenderBand()
    {
        var s = status;
        if (s.Band == null) return;
        // RGBA. Green = overtaking now, yellow = overtaking wanted but not possible right now.
        uint color = s.Kind == Kind.Overtake ? 0x3DDC8488u : 0xFFB02088u;
        var center = ToAR(s.Band.Center);
        // Two strips, edge -> centre, like the Lane Assist band: bright on both outer edges, glow fading inwards.
        DrawStrip(ToAR(s.Band.Left), center, color);
        DrawStrip(ToAR(s.Band.Right), center, color);
    }

    static List<ARCoordinate> ToAR(Vector3[] points) => points.Select(v => new ARCoordinate(v, ARCoordinateCenter.World)).ToList();

    static void DrawStrip(List<ARCoordinate> outer, List<ARCoordinate> center, uint color)
    {
        var ar = OverlayHandler.Current.AR;
        if (DrawWithoutNearFade != null)
            DrawWithoutNearFade.Invoke(ar, new object[] { outer, center, color, 0f, 0u, -1f, false });
        else
            ar.Draw3DLineWithGradient(outer, center, color, 0f, 0u, -1f);
    }
}
