using GameFlow.Infrastructure.Theming.Flee;
using Xunit;

namespace GameFlow.Infrastructure.Tests.Runtime.Theming;

/// <summary>Tests for <see cref="FleeVariables"/>.</summary>
public sealed class FleeVariablesTests
{
    [Theory]
    [InlineData("quad_right:s", "quad_right:s")]
    [InlineData("home", "home")]
    [InlineData("bumpers:l OR bumpers:r", "bumpers:l")]
    [InlineData("not menu:l", "menu:l")]
    [InlineData("If(triggers:l:analog > 0, 1, 0)", "triggers:l:analog")]
    [InlineData("2 * stick_left:x", "stick_left:x")]
    public void FirstNameExpressionReturnsFirstVariable(string source, string expected)
    {
        var expression = FleeParser.Parse(source);

        var name = FleeVariables.FirstName(expression);

        Assert.Equal(expected, name);
    }

    [Fact]
    public void FirstNameLiteralReturnsNull()
    {
        var expression = FleeParser.Parse("1 + 2");

        var name = FleeVariables.FirstName(expression);

        Assert.Null(name);
    }
}
