using GameFlow.Core.Formulas;
using Xunit;

namespace GameFlow.Core.Tests;

public sealed class FormulaExpressionTests
{
    private static float Eval(string expression, params float[] sources)
    {
        var compiled = FormulaExpression.Compile(expression, sources.Length, out var error);
        Assert.True(compiled is not null, $"compile failed: {error}");
        return compiled!(sources);
    }

    [Theory]
    [InlineData("s1 - s2", 1f, 0f, 1f)]
    [InlineData("s1 - s2", 0f, 1f, -1f)]
    [InlineData("s1 + s2 * 2", 1f, 2f, 5f)]        // precedence: * before +
    [InlineData("(s1 + s2) * 2", 1f, 2f, 6f)]      // parentheses override
    [InlineData("-s1", 0.5f, 0f, -0.5f)]
    [InlineData("s1 * 0.7 + s2 * 0.3", 1f, 0f, 0.7f)]
    public void Compile_ArithmeticAndPrecedence(string expr, float s1, float s2, float expected)
    {
        Assert.Equal(expected, Eval(expr, s1, s2), precision: 4);
    }

    [Theory]
    [InlineData("s1 > 0.4", 0.5f, 1f)]
    [InlineData("s1 > 0.4", 0.3f, 0f)]
    [InlineData("s1 == 1", 1f, 1f)]
    [InlineData("s1 != 1", 1f, 0f)]
    public void Compile_ComparisonsReturnOneOrZero(string expr, float s1, float expected)
    {
        Assert.Equal(expected, Eval(expr, s1));
    }

    [Fact]
    public void Compile_IfSelectsBranchByCondition()
    {
        Assert.Equal(0.8f, Eval("if(s1 > 0.5, s2, 0)", 1f, 0.8f), precision: 4);
        Assert.Equal(0f, Eval("if(s1 > 0.5, s2, 0)", 0f, 0.8f), precision: 4);
    }

    [Fact]
    public void Compile_LogicOperators()
    {
        Assert.Equal(1f, Eval("s1 and s2", 1f, 1f));
        Assert.Equal(0f, Eval("s1 and s2", 1f, 0f));
        Assert.Equal(1f, Eval("s1 or s2", 0f, 1f));
        Assert.Equal(1f, Eval("not s1", 0f));
        Assert.Equal(0f, Eval("!s1", 1f));
    }

    [Fact]
    public void Compile_ClampAndMinMaxAndAbs()
    {
        Assert.Equal(1f, Eval("clamp(s1 + s2, 0, 1)", 0.7f, 0.7f), precision: 4);
        Assert.Equal(0.7f, Eval("max(s1, s2)", 0.7f, 0.2f), precision: 4);
        Assert.Equal(0.2f, Eval("min(s1, s2)", 0.7f, 0.2f), precision: 4);
        Assert.Equal(0.5f, Eval("abs(s1)", -0.5f), precision: 4);
    }

    [Fact]
    public void Compile_DivisionByZeroYieldsZeroNotInfinity()
    {
        Assert.Equal(0f, Eval("s1 / s2", 1f, 0f));
    }

    [Fact]
    public void Compile_SourceIndexPastCount_FailsLoudly()
    {
        var compiled = FormulaExpression.Compile("s3", sourceCount: 2, out var error);
        Assert.Null(compiled);
        Assert.Contains("s3", error);
    }

    [Fact]
    public void Compile_UnknownIdentifier_FailsLoudly()
    {
        var compiled = FormulaExpression.Compile("bogus + 1", sourceCount: 1, out var error);
        Assert.Null(compiled);
        Assert.NotNull(error);
    }

    [Fact]
    public void EveryStarterRecipe_Compiles()
    {
        foreach (var recipe in FormulaRecipes.All)
        {
            // Recipes reference at most s2; compile against two sources.
            var compiled = FormulaExpression.Compile(recipe.Expression, sourceCount: 2, out var error);
            Assert.True(compiled is not null, $"Recipe '{recipe.Name}' failed to compile: {error}");
        }
    }
}
