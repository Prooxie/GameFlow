using Autofire.Core.Enums;

namespace Autofire.Core.Models;

public static class ButtonState
{
    public static IReadOnlyDictionary<ButtonId, bool> CreateEmptyMap()
    {
        return Enum.GetValues<ButtonId>().ToDictionary(button => button, _ => false);
    }

    public static Dictionary<ButtonId, bool> Clone(IReadOnlyDictionary<ButtonId, bool> source)
    {
        var clone = Enum.GetValues<ButtonId>().ToDictionary(button => button, button => false);

        foreach (var pair in source)
        {
            clone[pair.Key] = pair.Value;
        }

        return clone;
    }
}
