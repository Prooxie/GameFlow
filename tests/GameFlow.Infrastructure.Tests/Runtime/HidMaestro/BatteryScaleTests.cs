using GameFlow.Core.Models;
using GameFlow.Infrastructure.Runtime.HidMaestro;
using Xunit;

namespace GameFlow.Infrastructure.Tests.Runtime.HidMaestro;

/// <summary>
/// Checks the SDK battery capacity on its documented zero-to-ten scale.
/// </summary>
public sealed class BatteryScaleTests
{
    private static ControllerSnapshot WithBattery(int? percent) =>
        ControllerSnapshot.Empty() with { BatteryPercent = percent };

    [Theory]
    [InlineData(100, 10)]
    [InlineData(95, 9)]
    [InlineData(75, 7)]
    [InlineData(65, 6)]
    [InlineData(25, 2)]
    [InlineData(5, 0)]
    [InlineData(0, 0)]
    public void BatteryLevelForPercentageUsesSonyCapacity(int percent, byte expected) =>
        Assert.Equal(expected, HidMaestroSubmission.BatteryLevelFor(WithBattery(percent)));

    /// <summary>
    /// A wired pad, a keyboard, or nothing at all. Reporting full is what
    /// a wired controller says about itself; reporting zero is what the
    /// unwritten field used to say, and it reads as nearly flat.
    /// </summary>
    [Fact]
    public void ASourceWithNoBatteryReportsFull() =>
        Assert.Equal(10, HidMaestroSubmission.BatteryLevelFor(WithBattery(null)));

    /// <summary>Nothing may exceed the scale, whatever a backend reports.</summary>
    [Theory]
    [InlineData(255)]
    [InlineData(1000)]
    [InlineData(-40)]
    public void OutOfRangeInputStaysInsideTheScale(int percent)
    {
        var level = HidMaestroSubmission.BatteryLevelFor(WithBattery(percent));
        Assert.InRange(level, (byte)0, (byte)10);
    }
}
