using System.Text.Json.Serialization;
using GameFlow.Core.Enums;

namespace GameFlow.Core.Models.Rules;

/// <summary>
/// Touchpad → stick / D-pad / mouse mapping. The moment a finger
/// touches down, that position becomes the anchor for the stick and
/// D-pad modes — like a phone game's virtual joystick appearing
/// wherever you first tap, not pinned to a fixed spot. Mouse mode is
/// different: it reads frame-to-frame movement rather than
/// anchor-relative distance, the way a laptop touchpad actually works
/// (see <see cref="Pipeline.ControllerMappingPipeline"/>'s touchpad
/// pass for exactly why).
///
/// <para>
/// All three are independently toggleable and can run at once — "every
/// toggle saves per slot" in the source description. Mouse output
/// reaches the OS cursor via <c>IMouseOutputWriter</c> in
/// GameFlow.Infrastructure (SendInput on Windows), consuming
/// <see cref="Pipeline.ControllerFrameResult.MouseDeltaX"/>/
/// <see cref="Pipeline.ControllerFrameResult.MouseDeltaY"/> — this rule
/// only computes the delta, it doesn't move anything itself.
/// </para>
/// </summary>
public sealed record TouchpadMapRule : MappingRule
{
    [JsonPropertyName("stickEnabled")]
    public bool StickEnabled { get; init; } = true;

    [JsonPropertyName("targetStick")]
    public StickId TargetStick { get; init; } = StickId.Right;

    /// <summary>How much anchor-relative travel (in normalized touchpad-surface units) maps to full stick deflection. Higher = less physical travel needed for full deflection.</summary>
    [JsonPropertyName("stickSensitivity")]
    public float StickSensitivity { get; init; } = 2.5f;

    [JsonPropertyName("dpadEnabled")]
    public bool DpadEnabled { get; init; }

    /// <summary>Anchor-relative distance (normalized touchpad-surface units) a finger must travel before any D-pad direction registers — avoids jitter near the anchor firing spurious presses.</summary>
    [JsonPropertyName("dpadDeadzoneRadius")]
    public float DpadDeadzoneRadius { get; init; } = 0.05f;

    /// <summary>True: 8-way (diagonals hold two adjacent buttons at once). False: 4-way cardinal only.</summary>
    [JsonPropertyName("dpadEightWay")]
    public bool DpadEightWay { get; init; } = true;

    [JsonPropertyName("mouseEnabled")]
    public bool MouseEnabled { get; init; }

    /// <summary>Per-axis sensitivity multiplier. 1.0 is the pipeline's documented reference scale (see MouseDeltaReferencePixels); higher = more cursor travel per unit of finger movement.</summary>
    [JsonPropertyName("mouseSensitivityX")]
    public float MouseSensitivityX { get; init; } = 1.0f;

    [JsonPropertyName("mouseSensitivityY")]
    public float MouseSensitivityY { get; init; } = 1.0f;

    [JsonPropertyName("invertMouseX")]
    public bool InvertMouseX { get; init; }

    [JsonPropertyName("invertMouseY")]
    public bool InvertMouseY { get; init; }
}
