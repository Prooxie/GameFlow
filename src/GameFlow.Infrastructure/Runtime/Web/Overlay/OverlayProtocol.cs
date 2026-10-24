using System.Globalization;
using System.Text;
using System.Text.Json;
using GameFlow.Core.Models;
using GameFlow.Infrastructure.Theming;

namespace GameFlow.Infrastructure.Runtime.Web.Overlay;

/// <summary>
/// Wire format for the OBS overlay socket. Two message shapes, and the
/// asymmetry between them is the point:
///
/// <list type="bullet">
/// <item><b>program</b> — the compiled theme. Sent once, on connect, and
///   it is large (a controller theme is a few hundred nodes).</item>
/// <item><b>frame</b> — an array of numbers and nothing else. Sent at the
///   snapshot rate, and it is tiny.</item>
/// </list>
///
/// <para>
/// Frames are built with a <see cref="StringBuilder"/> rather than
/// <c>JsonSerializer</c> because this runs per frame per connected
/// browser source, and the payload is a flat list of doubles — there is
/// no structure to serialize. The program, sent once, uses the
/// serializer.
/// </para>
///
/// <para>
/// Every number is written with <see cref="CultureInfo.InvariantCulture"/>.
/// On a machine with a comma decimal separator — which is where this was
/// written — the default would emit <c>0,5</c> and produce JSON that
/// parses as two array elements, silently shifting every value slot after
/// it. That failure would only appear on some users' machines.
/// </para>
/// </summary>
public static class OverlayProtocol
{
    private static readonly IReadOnlyDictionary<int, string> KeyLabels = Enumerable.Range(1, 255)
        .Append(Input.KeyboardVirtualKeys.NumpadEnter).ToDictionary(key => key, Input.VirtualKeyNames.GetName);
    /// <summary>
    /// Most touch contacts in one frame. The app has five finger colours and
    /// draws a fingertip for at most five contacts.
    /// </summary>
    public const int MaxTouchContacts = 5;

    private static readonly JsonSerializerOptions ProgramJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// The compiled theme, as the page's <c>program</c> message. Image
    /// paths are replaced by their asset-route URLs — the page never sees
    /// a filesystem path, and an index is all it can ask for.
    /// </summary>
    public static string BuildProgram(OverlayProgram program, IReadOnlyDictionary<int, string>? controlLabels = null,
        IReadOnlyList<string>? axisLabels = null)
    {
        ArgumentNullException.ThrowIfNull(program);

        var payload = new
        {
            type = "program",
            themeId = program.ThemeId,
            themeName = program.ThemeName,
            width = program.Width,
            height = program.Height,
            // Root-relative, not relative. The page is reachable as both
            // /overlay and /overlay/, and a bare "asset?..." resolves
            // against the directory — which is the site root for the
            // first spelling and /overlay/ for the second. That is a
            // theme whose art silently 404s depending on whether the URL
            // pasted into OBS happened to carry a trailing slash.
            images = Enumerable
                .Range(0, program.ImagePaths.Count)
                .Select(i => $"/overlay/asset?theme={Uri.EscapeDataString(program.ThemeId)}&i={i}")
                .ToArray(),
            nodes = program.Nodes,
            inputSurface = program.InputSurface,
            controlLabels,
            axisLabels,
            keyLabels = program.InputSurface is null ? null : KeyLabels,
            touchRegion = program.TouchRegion,
            // Flat [x0, y0, x1, y1, ...] in theme units: the page turns it
            // straight into a clip path, and a hundredth of a theme pixel
            // is far below anything a canvas can show.
            touchOutline = program.TouchOutline?
                .SelectMany(point => new[] { Math.Round(point.X, 2), Math.Round(point.Y, 2) })
                .ToArray()
        };

        return JsonSerializer.Serialize(payload, ProgramJson);
    }

    /// <summary>
    /// A message the page shows instead of a controller. Used for the one
    /// case worth explaining rather than failing silently — no theme to
    /// draw — because a transparent page that renders nothing is
    /// indistinguishable from a browser source pointed at the wrong URL.
    /// </summary>
    public static string BuildError(string message) =>
        JsonSerializer.Serialize(new { type = "error", message }, ProgramJson);

    /// <summary>
    /// One frame: every value slot evaluated against
    /// <paramref name="snapshot"/>, the lightbar colour, and each
    /// trailpad's contact positions.
    /// </summary>
    /// <param name="program">The compiled theme whose slots are evaluated.</param>
    /// <param name="symbols">
    /// Reused across frames by the caller — rebinding a snapshot is a
    /// field assignment, while a fresh symbol table per frame per source
    /// would allocate for nothing.
    /// </param>
    /// <param name="snapshot">The controller state this frame reports.</param>
    /// <param name="lightColor">
    /// The slot's lightbar colour as <c>#AARRGGBB</c>, or null when the
    /// slot has lighting switched off.
    /// </param>
    public static string BuildFrame(
        OverlayProgram program,
        ControllerStateSymbols symbols,
        ControllerSnapshot snapshot,
        string? lightColor)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(symbols);
        ArgumentNullException.ThrowIfNull(snapshot);

        symbols.UpdateSnapshot(snapshot);

        var builder = new StringBuilder(256);
        _ = builder.Append("{\"type\":\"frame\",\"v\":[");
        for (var i = 0; i < program.ValueSlots.Count; i++)
        {
            if (i > 0) { _ = builder.Append(','); }
            AppendNumber(builder, program.ValueSlots[i].Evaluate(symbols));
        }
        _ = builder.Append(']');

        AppendTrailPads(builder, program, symbols);
        AppendTouchContacts(builder, program, snapshot);
        AppendInputs(builder, program, snapshot);

        var css = OverlayProgramBuilder.ToCssColor(lightColor);
        if (css is not null)
        {
            _ = builder.Append(",\"light\":\"").Append(css).Append('"');
        }

        _ = builder.Append('}');
        return builder.ToString();
    }

    private static void AppendInputs(StringBuilder builder, OverlayProgram program, ControllerSnapshot snapshot)
    {
        // Keep identifiers numeric on the wire; labels are sent once with the layout.
        builder.Append(",\"buttons\":[");
        var first = true;
        foreach (var button in snapshot.Buttons)
        {
            if (!first) { builder.Append(','); }
            first = false;
            builder.Append(((int)button).ToString(CultureInfo.InvariantCulture));
        }
        builder.Append(']');
        builder.Append(",\"activeAxes\":[");
        first = true;
        AddAxis(0, Active(snapshot.LeftStick.X) || Active(snapshot.LeftStick.Y));
        AddAxis(1, Active(snapshot.RightStick.X) || Active(snapshot.RightStick.Y));
        AddAxis(2, Active(snapshot.LeftTrigger));
        AddAxis(3, Active(snapshot.RightTrigger));
        AddAxis(4, program.InputSurface is not null && (Active(snapshot.MouseMotion.X) || Active(snapshot.MouseMotion.Y)));
        builder.Append(']');
        if (program.InputSurface is null) { return; }
        builder.Append(",\"keys\":[");
        first = true;
        foreach (var key in snapshot.PressedKeys)
        {
            if (!Input.KeyboardVirtualKeys.IsSupported(key)) { continue; }
            if (!first) { builder.Append(','); }
            first = false;
            builder.Append(key.ToString(CultureInfo.InvariantCulture));
        }
        builder.Append("],\"mouse\":").Append(((int)snapshot.MouseButtons).ToString(CultureInfo.InvariantCulture));
        builder.Append(",\"motion\":[");
        AppendNumber(builder, float.IsFinite(snapshot.MouseMotion.X) ? Math.Clamp(snapshot.MouseMotion.X, -1, 1) : 0);
        builder.Append(',');
        AppendNumber(builder, float.IsFinite(snapshot.MouseMotion.Y) ? Math.Clamp(snapshot.MouseMotion.Y, -1, 1) : 0);
        builder.Append(']');

        static bool Active(float value) => float.IsFinite(value) && Math.Abs(value) > .05f;
        void AddAxis(int id, bool active)
        {
            if (!active) { return; }
            if (!first) { builder.Append(','); }
            first = false;
            builder.Append(id.ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>
    /// Per trailpad, the evaluated marker position, or <c>null</c> when
    /// that node's visibility expression says the finger is not down.
    ///
    /// <para>
    /// One position per NODE, not per finger. A theme that shows two
    /// fingers declares two trailpads, each reading its own contact —
    /// the shipped DualSense theme has one on <c>touch_center:0:*</c> and
    /// one on <c>touch_center:1:*</c> — so evaluating each node against
    /// the unmodified snapshot places every finger correctly on its own.
    /// </para>
    ///
    /// <para>
    /// It is worth saying what this deliberately does NOT do, because the
    /// app looks like it does the opposite.
    /// <c>ThemeSurface.DrawAdditionalContacts</c> re-evaluates ONE
    /// trailpad once per finger with that finger substituted in as
    /// contact 0. That is not the theme's semantics; it is dashboard
    /// chrome — per-finger coloured rings, drawn so a user mapping a
    /// gesture can tell fingers apart — layered over the theme. Copying
    /// it here would make trailpad 0 draw every finger at the wrong
    /// place and trailpad 1 draw nothing at all, since after substitution
    /// no contact 1 exists to satisfy its expression.
    /// </para>
    /// </summary>
    private static void AppendTrailPads(
        StringBuilder builder,
        OverlayProgram program,
        ControllerStateSymbols symbols)
    {
        if (program.TrailPads.Count == 0)
        {
            return;
        }

        _ = builder.Append(",\"pads\":[");
        for (var p = 0; p < program.TrailPads.Count; p++)
        {
            if (p > 0) { _ = builder.Append(','); }

            var pad = program.TrailPads[p];
            if (pad.Visible.Evaluate(symbols) == 0)
            {
                _ = builder.Append("null");
                continue;
            }

            _ = builder.Append('[');
            AppendNumber(builder, pad.X.Evaluate(symbols));
            _ = builder.Append(',');
            AppendNumber(builder, pad.Y.Evaluate(symbols));
            _ = builder.Append(']');
        }
        _ = builder.Append(']');
    }

    /// <summary>
    /// The touch contacts, for a program with a <see cref="OverlayProgram.TouchRegion"/>.
    /// Format: <c>"touch":[[x,y,finger,pressure],...]</c>. X, Y and pressure are 0 to 1.
    /// The finger is the hardware finger slot, which sets the colour on the page.
    /// </summary>
    /// <remarks>
    /// An empty list is sent too. It tells the page that each finger is up, so the
    /// trails can cool. At most <see cref="MaxTouchContacts"/> contacts are sent.
    /// </remarks>
    private static void AppendTouchContacts(StringBuilder builder, OverlayProgram program, ControllerSnapshot snapshot)
    {
        if (program.TouchRegion is null)
        {
            return;
        }

        var contacts = snapshot.TouchContacts;
        var count = Math.Min(contacts.Count, MaxTouchContacts);

        _ = builder.Append(",\"touch\":[");
        for (var i = 0; i < count; i++)
        {
            if (i > 0) { _ = builder.Append(','); }

            var contact = contacts[i];
            _ = builder.Append('[');
            AppendUnit(builder, contact.X);
            _ = builder.Append(',');
            AppendUnit(builder, contact.Y);
            _ = builder.Append(',').Append(contact.FingerIndex.ToString(CultureInfo.InvariantCulture)).Append(',');
            AppendUnit(builder, contact.Pressure);
            _ = builder.Append(']');
        }
        _ = builder.Append(']');
    }

    /// <summary>Writes a value clamped to 0..1. A non-finite value becomes 0.</summary>
    private static void AppendUnit(StringBuilder builder, float value) =>
        AppendNumber(builder, float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : 0f);

    /// <summary>
    /// Writes a JSON number. Non-finite values become 0: a theme can
    /// divide by a control that happens to read zero, and <c>NaN</c> is
    /// not valid JSON — it would break the parse for the whole frame
    /// rather than misplacing one element.
    /// </summary>
    private static void AppendNumber(StringBuilder builder, double value)
    {
        if (!double.IsFinite(value))
        {
            _ = builder.Append('0');
            return;
        }

        _ = builder.Append(Math.Round(value, 3).ToString("0.###", CultureInfo.InvariantCulture));
    }
}
