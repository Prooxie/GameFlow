namespace GameFlow.Core.Models.Rules;

/// <summary>
/// A Lua-scripted control, executed each tick by
/// <see cref="Pipeline.ControllerMappingPipeline"/> via
/// <see cref="Scripting.LuaScriptEngine"/>. See that engine's class
/// comment for the full <c>ctx</c> API (ctx.press, ctx.set_left, etc.)
/// and the sandbox it runs under.
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
