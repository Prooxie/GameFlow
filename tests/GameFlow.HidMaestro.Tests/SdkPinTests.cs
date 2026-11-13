using Xunit;

namespace GameFlow.HidMaestro.Tests;

public sealed class SdkPinTests
{
    [WindowsFact]
    public void ReferencedSdk_IsThePinnedVersion() =>
        Assert.Equal(new Version(2, 1, 0, 0), typeof(HIDMaestro.HMContext).Assembly.GetName().Version);
}
