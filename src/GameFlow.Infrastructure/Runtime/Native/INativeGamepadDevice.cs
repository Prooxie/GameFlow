using GameFlow.Core.Models;

namespace GameFlow.Infrastructure.Runtime.Native;

/// <summary>Owns one operating-system gamepad. Calls come from the serialized output pipeline.</summary>
public interface INativeGamepadDevice : IDisposable
{
    /// <summary>Writes a complete state. Throws when the operating system rejects the report.</summary>
    void Write(ControllerSnapshot snapshot);
}
