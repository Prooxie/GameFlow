namespace GameFlow.Infrastructure.Theming.Flee;

/// <summary>
/// Reads variable names out of a parsed Flee expression.
/// </summary>
/// <remarks>
/// A theme tells which control an element shows through the expression of its
/// <c>showhide</c> node, for example <c>"quad_right:s"</c>. The hit tester and the
/// touchpad locator use the first variable as that control.
/// </remarks>
public static class FleeVariables
{
    /// <summary>
    /// The first variable name in source order, or null when the expression has none.
    /// For <c>"bumpers:l or bumpers:r"</c> the result is <c>"bumpers:l"</c>.
    /// </summary>
    /// <remarks>
    /// Any name counts, also a name with no colon: every Sony pack names the PS button <c>home</c>.
    /// </remarks>
    public static string? FirstName(FleeNode node)
    {
        switch (node)
        {
            case VariableNode variable:
                return variable.Name;

            case UnaryNode unary:
                return FirstName(unary.Operand);

            case BinaryNode binary:
                return FirstName(binary.Left) ?? FirstName(binary.Right);

            case CallNode call:
                foreach (var argument in call.Arguments)
                {
                    var name = FirstName(argument);
                    if (name is not null)
                    {
                        return name;
                    }
                }

                return null;

            default:
                return null;
        }
    }
}
