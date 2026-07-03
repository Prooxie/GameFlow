using Microsoft.Extensions.Logging;
#if HIDMAESTRO_SDK
using HIDMaestro; // HMContext, HMController, HMProfile, HMGamepadState, HMButton, HMHat, HMOutputPacket
using Autofire.Core.Models;
using Autofire.Infrastructure.Runtime.Templates;
#endif

namespace Autofire.Infrastructure.Runtime.HidMaestro;

/// <summary>
/// Output sink that emits a virtual controller through HIDMaestro
/// (https://github.com/hifihedgehog/HIDMaestro) — a user-mode virtual
/// game-controller platform for Windows. It presents as real hardware
/// to DirectInput, XInput, SDL3, the browser Gamepad API and
/// WGI/GameInput, with no kernel driver, EV certificate, or reboot
/// (UMDF2 + a locally-trusted self-signed cert). MIT-licensed.
/// On the Autofire pipeline it is the intended replacement for the
/// ViGEm sinks on Windows.
///
/// <para><b>Status:</b> scaffold (no-op) until the HIDMaestro.Core SDK
/// is referenced and the per-frame input call is wired. The real
/// integration uses the SDK's managed API — NOT a
/// <c>\\.\HidMaestroN</c> file handle (an earlier scaffold guessed
/// that; it does not exist).</para>
///
/// <para><b>Real SDK shape</b> (from HIDMaestro.Core, .NET 10):</para>
/// <list type="bullet">
/// <item>Create an <c>HMContext</c> once per process. First run calls
/// <c>HMContext.InstallDriver</c> (extracts + self-signs + installs the
/// UMDF2 driver; requires elevation). ~18s cold, ~200ms warm.</item>
/// <item>Pick a profile by id via <c>ctx.GetProfile("xbox-360-wired")</c>
/// / <c>"dualsense"</c> / <c>"xbox-series-xs-bt"</c> (225 profiles), or
/// author one with <c>HMProfileBuilder</c> + <c>HidDescriptorBuilder</c>.</item>
/// <item><c>using var ctrl = ctx.CreateController(profile)</c> — an
/// <c>HMController</c> (IDisposable); dispose removes the device.</item>
/// <item><b>Per frame</b>: write the controller's HID input report into
/// its per-controller shared-memory section. The exact submit method on
/// <c>HMController</c> must be confirmed against
/// <c>example/SdkDemo/Program.cs</c> in the HIDMaestro repo (the README
/// documents the shared-memory layout — SeqNo+DataSize+Data[256] — but
/// not the managed method name). Isolate that ONE call here.</item>
/// <item>Rumble/FFB arrives via <c>HMController.OutputReceived</c>;
/// forwarding it to a physical pad is the consumer's job.</item>
/// </list>
///
/// <para><b>Build/runtime requirements the host machine must satisfy</b>
/// (cannot be vendored from this build environment): the HIDMaestro.Core
/// package/assembly + its embedded driver binaries, Windows 10/11 x64,
/// and elevation on first run. Windows-only — see the factory for the
/// non-Windows path.</para>
/// </summary>
#if HIDMAESTRO_SDK
// ── Real implementation ──
// Active only when the project defines the HIDMAESTRO_SDK compile symbol
// AND references the HIDMaestro.Core assembly. Modeled on the verified
// example/SdkDemo/Program.cs:
//   ctx.LoadDefaultProfiles(); ctx.InstallDriver();
//   using var ctrl = ctx.CreateController(ctx.GetProfile("xbox-360-wired"));
//   ctrl.OutputReceived += (controller, packet) => { ... }; // rumble/FFB
//   ctrl.SubmitState(in state);  // sticks [-1,1], triggers [0,1]
// Verified: HMGamepadState.{LeftStickX/Y,RightStickX/Y,LeftTrigger,
// RightTrigger,Buttons(HMButton flags),Hat(HMHat)}; HMHat.{None,North,
// NorthEast,East,SouthEast,South,SouthWest,West,NorthWest}.
// STILL INFERRED (flagged inline): HMButton spellings for Back/Start/
// thumb-clicks, the HMOutputPacket type name + OutputReceived delegate
// shape, and the rumble byte offsets. Confirm against your SDK / PadForge.

public sealed class HidMaestroOutputSink : IOutputSink, Autofire.Infrastructure.Runtime.Slots.IConfigurableOutputSink, IRumbleFeedbackSource
{
    private readonly ILogger<HidMaestroOutputSink> logger;
    private readonly DeviceTemplateStore templateStore;
    private readonly object gate = new();

    private HMContext? context;
    private HMController? controller;
    private HMProfile? profile;
    private VirtualControllerKind currentKind = VirtualControllerKind.Xbox360;
    private bool connected;
    private bool disposed;

    public HidMaestroOutputSink(ILogger<HidMaestroOutputSink> logger, DeviceTemplateStore templateStore)
    {
        this.logger = logger;
        this.templateStore = templateStore;
    }

    public string DisplayName => "HIDMaestro virtual controller";

    /// <summary>Raised when the consuming game sends rumble (low, high) in 0–1.</summary>
    public event Action<double, double>? RumbleReceived;

    public (ushort Vid, ushort Pid)? OwnedHardwareSignature =>
        profile is null ? null : (profile.VendorId, profile.ProductId);

    /// <summary>
    /// Applies a device's output template — picks the virtual-controller
    /// profile to present. Called by the runtime/factory when it knows
    /// which device/slot this sink serves (per-device routing lands in
    /// Phase 3). Switching kind tears down and recreates the controller.
    /// </summary>
    public void Configure(DeviceOutputTemplate template)
    {
        if (template is null)
        {
            return;
        }
        lock (gate)
        {
            if (template.OutputKind == currentKind && connected)
            {
                return;
            }
            currentKind = template.OutputKind;
            TeardownController();
        }
    }

    public ValueTask WriteAsync(ControllerSnapshot snapshot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        HMController? active;
        lock (gate)
        {
            if (disposed)
            {
                return ValueTask.CompletedTask;
            }
            EnsureConnected();
            active = controller;
        }

        if (active is null)
        {
            return ValueTask.CompletedTask;
        }

        // HMGamepadState: sticks [-1,1], triggers [0,1] (direct fields).
        var state = BuildState(snapshot);
        active.SubmitState(in state);
        return ValueTask.CompletedTask;
    }

    private void EnsureConnected()
    {
        if (connected)
        {
            return;
        }
        try
        {
            context ??= new HMContext();
            _ = context.LoadDefaultProfiles();   // load embedded catalog before GetProfile
            context.InstallDriver();             // no-op when already installed; needs elevation on first run
            var profileId = HidMaestroProfiles.ResolveProfileId(currentKind);
            profile = context.GetProfile(profileId)
                ?? throw new InvalidOperationException($"HIDMaestro profile '{profileId}' not found.");
            controller = context.CreateController(profile);
            controller.OutputReceived += OnOutputReceived;   // game rumble/haptics/FFB → physical pad
            connected = true;
            logger.LogInformation("HIDMaestro controller created for profile {ProfileId}.", profile.Id);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to create HIDMaestro controller; output disabled this tick.");
            TeardownController();
        }
    }

    // ── SDK-version-specific helpers (reconcile with your HMaestroVirtualController) ──

    // ── State mapping (verified HMGamepadState surface) ──

    private static HMGamepadState BuildState(ControllerSnapshot s)
    {
        // Sticks [-1,+1] (Autofire already uses this, Y+ = up); triggers
        // [0,1]. If a target shows inverted Y on hardware, negate the
        // LeftStickY/RightStickY lines.
        return new HMGamepadState
        {
            LeftStickX  = Math.Clamp(s.LeftStick.X,  -1f, 1f),
            LeftStickY  = Math.Clamp(s.LeftStick.Y,  -1f, 1f),
            RightStickX = Math.Clamp(s.RightStick.X, -1f, 1f),
            RightStickY = Math.Clamp(s.RightStick.Y, -1f, 1f),
            LeftTrigger  = Math.Clamp(s.LeftTrigger,  0f, 1f),
            RightTrigger = Math.Clamp(s.RightTrigger, 0f, 1f),
            Buttons = MapButtons(s),
            Hat = MapHat(s),
        };
    }

    private static HMButton MapButtons(ControllerSnapshot s)
    {
        // A/B/X/Y, bumpers, Guide, Share are confirmed HMButton names.
        // Back/Start and the thumb clicks follow XInput-standard naming —
        // CONFIRM these four against the HMButton enum in your SDK build.
        HMButton b = HMButton.None;
        void Set(ButtonId id, HMButton flag) { if (s.IsPressed(id)) b |= flag; }
        Set(ButtonId.South, HMButton.A);
        Set(ButtonId.East,  HMButton.B);
        Set(ButtonId.West,  HMButton.X);
        Set(ButtonId.North, HMButton.Y);
        Set(ButtonId.LeftShoulder,  HMButton.LeftBumper);
        Set(ButtonId.RightShoulder, HMButton.RightBumper);
        Set(ButtonId.Guide, HMButton.Guide);
        Set(ButtonId.Touchpad, HMButton.Share);     // DualSense Create/Share
        Set(ButtonId.Back,  HMButton.Back);          // ← confirm (View/Menu?)
        Set(ButtonId.Start, HMButton.Start);         // ← confirm
        Set(ButtonId.LeftStick,  HMButton.LeftThumb);   // ← confirm
        Set(ButtonId.RightStick, HMButton.RightThumb);  // ← confirm
        return b;
    }

    private static HMHat MapHat(ControllerSnapshot s)
    {
        bool up = s.IsPressed(ButtonId.DpadUp);
        bool down = s.IsPressed(ButtonId.DpadDown);
        bool left = s.IsPressed(ButtonId.DpadLeft);
        bool right = s.IsPressed(ButtonId.DpadRight);
        if (up && right) return HMHat.NorthEast;
        if (down && right) return HMHat.SouthEast;
        if (down && left) return HMHat.SouthWest;
        if (up && left) return HMHat.NorthWest;
        if (up) return HMHat.North;
        if (right) return HMHat.East;
        if (down) return HMHat.South;
        if (left) return HMHat.West;
        return HMHat.None;
    }

    // ── Output (rumble / haptics / FFB) ──
    // OutputReceived delivers the raw wire bytes the game sent to the
    // virtual pad; the consumer decodes + forwards. This is a best-effort
    // decode of the common rumble layout into normalized (low, high) →
    // RumbleReceived, which the slot runtime forwards to the physical
    // device. VERIFY the packet type name, the (controller, packet)
    // delegate shape, and these byte offsets against PadForge's output
    // handler for the profiles you emit.
    private void OnOutputReceived(HMController sender, HMOutputPacket packet)
    {
        var data = packet.Data;
        if (data is null || data.Length == 0)
        {
            return;
        }

        double low = 0, high = 0;
        if (data.Length >= 5)
        {
            // Common XUSB SET_STATE vibration: [type, size, 0, big, small, …]
            low  = data[3] / 255.0;
            high = data[4] / 255.0;
        }
        else if (data.Length >= 2)
        {
            low  = data[0] / 255.0;
            high = data[1] / 255.0;
        }

        if (low > 0 || high > 0)
        {
            RumbleReceived?.Invoke(low, high);
        }
    }

    private void TeardownController()
    {
        try
        {
            if (controller is not null)
            {
                controller.OutputReceived -= OnOutputReceived;
            }
            controller?.Dispose();
        }
        catch (Exception exception) { logger.LogDebug(exception, "HIDMaestro controller dispose error."); }
        controller = null;
        connected = false;
    }

    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (disposed)
            {
                return ValueTask.CompletedTask;
            }
            disposed = true;
            TeardownController();
            try { context?.Dispose(); }
            catch (Exception exception) { logger.LogDebug(exception, "HIDMaestro context dispose error."); }
            context = null;
        }
        return ValueTask.CompletedTask;
    }
}
#else
public sealed class HidMaestroOutputSink : ScaffoldedOutputSinkBase
{
    public HidMaestroOutputSink(ILogger<HidMaestroOutputSink> logger)
        : base(
            logger,
            "HIDMaestro virtual controller",
            "Define the HIDMAESTRO_SDK compile symbol and reference HIDMaestro.Core to activate the real sink. Until then, no-op.")
    {
    }
}
#endif
