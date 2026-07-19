namespace GameFlow.Core.Models;

public sealed record ProfileDocument
{
    public string Id { get; init; } = "speedrunner-default";
    public string Name { get; init; } = "Speedrunner Default";
    public int Version { get; init; } = 4;
    public int PollingRateHz { get; init; } = 250;
    public string InputProvider { get; init; } = "sdl";

    /// <summary>
    /// The output backend id. Defaults to the platform's single real
    /// backend — <c>"hidmaestro"</c> on Windows — so a brand-new profile
    /// creates an actual virtual device out of the box. The historical
    /// default of <c>"preview"</c> meant a fresh install read input
    /// perfectly and yet never emitted a controller, which presented as
    /// "HIDMaestro doesn't create a virtual controller" when in fact
    /// HIDMaestro was never being asked to.
    /// </summary>
    public string OutputProvider { get; init; } = OperatingSystem.IsWindows() ? "hidmaestro" : "preview";
    public string PreferredInputDeviceId { get; init; } = string.Empty;
    public UiPreferences Ui { get; init; } = new();
    public IReadOnlyList<MappingRule> Rules { get; init; } = [];
}
