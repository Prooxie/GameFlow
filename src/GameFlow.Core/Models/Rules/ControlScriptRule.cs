namespace GameFlow.Core.Models.Rules;

/// <summary>
/// Stores per-control script configuration in the profile.
/// Runtime execution is intentionally handled by higher layers / future providers.
/// </summary>
public sealed record ControlScriptRule : MappingRule
{
    /// <summary>
    /// UI control key, for example: South, LeftStick, LeftStick.Button, RightTrigger.Analog.
    /// </summary>
    public string ControlKey { get; init; } = string.Empty;

    /// <summary>
    /// User-defined script body associated with the control.
    /// </summary>
    public string ScriptCode { get; init; } = string.Empty;

    /// <summary>
    /// Future-facing flag indicating that the original source input should be suppressed
    /// when this scripted control path is active.
    /// </summary>
    public bool SuppressSourceInput { get; init; }
}
