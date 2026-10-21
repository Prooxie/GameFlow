namespace GameFlow.Infrastructure.Runtime.HidMaestro;

/// <summary>Why the HIDMaestro backend can or cannot create virtual pads right now.</summary>
public enum HidMaestroState
{
    /// <summary>The SDK is loaded and the process may create pads.</summary>
    Ready,

    /// <summary>The platform is not Windows, so HIDMaestro cannot run at all.</summary>
    NotWindows,

    /// <summary>The SDK assembly was not found next to the application.</summary>
    SdkMissing,

    /// <summary>The SDK assembly was found, but its version is not one this build supports.</summary>
    SdkVersionUnsupported,

    /// <summary>The SDK is loaded, but creating pads needs an elevated process.</summary>
    NotElevated,

    /// <summary>The backend failed for another reason. <see cref="HidMaestroStatus.Description"/> says why.</summary>
    Failed,
}

/// <summary>
/// The backend's availability as a state plus a human-readable reason.
/// A catalog can be listed without elevation, so the two capabilities
/// are separate.
/// </summary>
public sealed record HidMaestroStatus(HidMaestroState State, string Description)
{
    /// <summary>True when <see cref="IHidMaestroBackend.TryCreatePad"/> can succeed.</summary>
    public bool CanCreatePads => State == HidMaestroState.Ready;

    /// <summary>True when <see cref="IHidMaestroBackend.GetCatalog"/> returns real profiles.</summary>
    public bool CanListCatalog => State is HidMaestroState.Ready or HidMaestroState.NotElevated;
}
