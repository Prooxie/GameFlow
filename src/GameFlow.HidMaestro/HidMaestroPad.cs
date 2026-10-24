using System.Runtime.Versioning;
using GameFlow.Core.Models;
using GameFlow.Infrastructure.Runtime.HidMaestro;
using HIDMaestro;
using Microsoft.Extensions.Logging;

namespace GameFlow.HidMaestro;

/// <summary>
/// One live virtual pad: an <c>HMController</c> with its state writer and
/// rumble router. It reuses one <c>HMGamepadState</c>, so a frame allocates
/// nothing after the first.
/// </summary>
/// <remarks>
/// Submit runs on the runtime tick thread only. The rumble events arrive on
/// the SDK's output thread, through the router.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class HidMaestroPad : IHidMaestroPad
{
    private readonly HMController controller;
    private readonly HidMaestroStateWriter writer;
    private readonly HidMaestroRumbleRouter router;
    private HMGamepadState state;
    private int disposed;

    /// <summary>
    /// Wraps <paramref name="controller"/>, which was created from
    /// <paramref name="profile"/>. The identity comes from the deployed
    /// profile, so it is the identity Windows sees.
    /// </summary>
    public HidMaestroPad(HMController controller, HMProfile profile, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(logger);

        this.controller = controller;
        writer = new HidMaestroStateWriter(profile);
        router = new HidMaestroRumbleRouter(logger);

        ProfileId = profile.Id;
        ProfileName = profile.Name;
        HardwareSignature = (profile.VendorId, profile.ProductId);
        CarriesExtendedInput = HidMaestroSession.CanArmExtendedInput(profile);

        controller.OutputReceived += router.OnOutputReceived;
        controller.OutputDecoded += router.OnOutputDecoded;
    }

    /// <inheritdoc />
    public string ProfileId { get; }

    /// <inheritdoc />
    public string ProfileName { get; }

    /// <inheritdoc />
    public (ushort Vid, ushort Pid) HardwareSignature { get; }

    /// <inheritdoc />
    /// <remarks>True only when the profile can arm its extended report, not when it merely declares one.</remarks>
    public bool CarriesExtendedInput { get; }

    /// <inheritdoc />
    public event Action<double, double>? RumbleReceived
    {
        add => router.RumbleReceived += value;
        remove => router.RumbleReceived -= value;
    }

    /// <inheritdoc />
    public event Action<GameFlow.Infrastructure.Runtime.Effects.GameLighting>? LightingReceived
    {
        add => router.LightingReceived += value;
        remove => router.LightingReceived -= value;
    }

    /// <summary>
    /// Writes one frame. An SDK exception propagates, because the sink counts
    /// failed frames to decide when a pad is broken.
    /// </summary>
    public void Submit(ControllerSnapshot snapshot)
    {
        writer.Fill(ref state, snapshot);
        controller.SubmitState(in state);
    }

    /// <summary>
    /// Stops the output events, then removes the device. A second call does
    /// nothing.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        controller.OutputReceived -= router.OnOutputReceived;
        controller.OutputDecoded -= router.OnOutputDecoded;
        controller.Dispose();
    }
}
