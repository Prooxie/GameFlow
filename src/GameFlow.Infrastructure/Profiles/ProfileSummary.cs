namespace GameFlow.Infrastructure.Profiles;

public sealed record ProfileSummary(string Id, string Name)
{
    public override string ToString()
    {
        return Name;
    }
}
