namespace Autofire.Core.Models;

public sealed record ProfileDocument
{
    public string Id { get; init; } = "speedrunner-default";
    public string Name { get; init; } = "Speedrunner Default";
    public int Version { get; init; } = 4;
    public int PollingRateHz { get; init; } = 250;
    public string InputProvider { get; init; } = "sdl";
    public string OutputProvider { get; init; } = "preview";
    public string PreferredInputDeviceId { get; init; } = string.Empty;
    public UiPreferences Ui { get; init; } = new();
    public IReadOnlyList<MappingRule> Rules { get; init; } = [];
}
