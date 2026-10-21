using Xunit;

namespace GameFlow.HidMaestro.Tests;

/// <summary>A fact that is skipped when the OS is not Windows.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "Needs Windows.";
        }
    }
}
