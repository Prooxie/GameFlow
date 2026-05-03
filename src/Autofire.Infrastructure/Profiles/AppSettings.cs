namespace Autofire.Infrastructure.Profiles;

public sealed record AppSettings
{
    public string ActiveProfileId { get; init; } = "speedrunner-default";
    public string SelectedCulture { get; init; } = "en";
}
