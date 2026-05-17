using Microsoft.Extensions.Logging;

namespace Autofire.Infrastructure.Runtime.X360ce;

/// <summary>
/// Input source that reads from devices presented by x360ce — a
/// DirectInput-to-XInput translation shim that lets non-XInput
/// gamepads (most legacy and many third-party pads) appear as
/// Xbox-class controllers to applications and to Autofire.
///
/// <para><b>Status:</b> scaffold only. Implementation needs:</para>
/// <list type="bullet">
/// <item>x360ce.dll loaded into the process (or its app-side .ini
/// pointing at our process).</item>
/// <item>Mapping x360ce's per-slot virtual-XInput state into
/// <see cref="Autofire.Core.Models.ControllerSnapshot"/>.</item>
/// <item>Hot-plug detection through the x360ce notification
/// interface (or polling the slot table).</item>
/// </list>
///
/// <para>
/// Implementation pointers: <c>x360ce.App.x360ce.dll</c> exposes a
/// managed wrapper around the native <c>x360ce.dll</c>. When wiring
/// up, prefer the wrapper and fall back to direct PInvoke only if
/// the wrapper is not redistributable in your build pipeline.
/// </para>
/// </summary>
public sealed class X360ceInputSource : ScaffoldedInputSourceBase
{
    public X360ceInputSource(
        InputDeviceCatalog inputDeviceCatalog,
        ILogger<X360ceInputSource> logger)
        : base(
            inputDeviceCatalog,
            logger,
            "x360ce (DirectInput → XInput)",
            "Requires x360ce runtime DLL — not yet operational. Falls back to no-op.")
    {
    }
}
