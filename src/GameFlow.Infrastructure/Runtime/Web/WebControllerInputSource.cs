using GameFlow.Core.Models;

namespace GameFlow.Infrastructure.Runtime.Web;

/// <summary>
/// Presents one connected phone as an ordinary <see cref="IInputSource"/>,
/// so a web pad can be assigned to a slot and mapped exactly like a
/// physical controller — every existing rule type (shift layers, SOCD,
/// multi-source rows, gyro) works on it with no special-casing, because
/// by the time the pipeline sees it, it's just a snapshot.
/// </summary>
public sealed class WebControllerInputSource(WebControllerHub hub, int padIndex) : IInputSource
{
    private readonly WebControllerHub hub = hub;
    private readonly int padIndex = padIndex;

    public string DisplayName => $"Web Controller #{padIndex + 1}";

    public ValueTask<ControllerSnapshot> ReadAsync(CancellationToken cancellationToken)
        => ValueTask.FromResult(hub.GetSnapshot(padIndex));

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// Publishes connected phones to the device catalog so they appear in
/// the slot device picker alongside physical pads.
/// </summary>
public static class WebControllerDeviceScanner
{
    public const string SourceKey = "web-controller";

    public static string BuildDeviceId(int padIndex) => $"web-pad-{padIndex}";

    /// <summary>Returns one catalog entry per currently-connected phone. Empty when nobody is connected — pads shouldn't clutter the picker before anyone has opened the page.</summary>
    public static IReadOnlyList<InputDeviceInfo> Scan(WebControllerHub hub)
    {
        var connected = hub.GetConnectedPads();
        var results = new List<InputDeviceInfo>(connected.Count);
        foreach (var padIndex in connected)
        {
            results.Add(new InputDeviceInfo(
                Id: BuildDeviceId(padIndex),
                DisplayName: $"Web Controller #{padIndex + 1}",
                IsGamepad: true,
                Category: DeviceCategory.Gamepad));
        }
        return results;
    }

    /// <summary>Parses a pad index back out of a catalog id, for the factory. Returns -1 when the id isn't a web pad.</summary>
    public static int TryParsePadIndex(string deviceId)
    {
        const string prefix = "web-pad-";
        if (!deviceId.StartsWith(prefix, StringComparison.Ordinal))
        {
            return -1;
        }
        return int.TryParse(deviceId.AsSpan(prefix.Length), out var index)
            && index >= 0 && index < WebControllerHub.MaxPads
            ? index
            : -1;
    }
}
