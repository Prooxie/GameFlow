namespace GameFlow.Infrastructure.Runtime;

public sealed record ProviderIdentity(
    string Key,
    string DisplayName,
    string Purpose,
    bool IsImplemented,
    string Notes);
