using GameFlow.Core.Enums;

namespace GameFlow.Core.Models;

public static class ButtonState
{
    /// <summary>
    /// Cached once. <see cref="Enum.GetValues{T}()"/> is reflection-backed
    /// and returns a FRESH array on every call — this used to run twice
    /// per pipeline tick per slot, so at 1000 Hz across 16 slots it was
    /// ~32k reflection calls and 32k throwaway arrays per second, purely
    /// to enumerate a list that cannot change at runtime.
    /// </summary>
    private static readonly ButtonId[] AllButtons = Enum.GetValues<ButtonId>();

    /// <summary>Number of distinct <see cref="ButtonId"/> values; also the dictionary capacity used below.</summary>
    public static int Count => AllButtons.Length;

    /// <summary>The full set of button ids, in declaration order. Treat as read-only.</summary>
    public static IReadOnlyList<ButtonId> All => AllButtons;

    public static IReadOnlyDictionary<ButtonId, bool> CreateEmptyMap()
    {
        // Presized so the dictionary never rehashes while filling, and
        // built with a plain loop rather than ToDictionary's two lambdas
        // (each of which allocated a closure per call).
        var map = new Dictionary<ButtonId, bool>(AllButtons.Length);
        foreach (var button in AllButtons)
        {
            map[button] = false;
        }
        return map;
    }

    public static Dictionary<ButtonId, bool> Clone(IReadOnlyDictionary<ButtonId, bool> source)
    {
        var clone = new Dictionary<ButtonId, bool>(AllButtons.Length);
        foreach (var button in AllButtons)
        {
            clone[button] = false;
        }

        foreach (var pair in source)
        {
            clone[pair.Key] = pair.Value;
        }

        return clone;
    }

    /// <summary>
    /// Copies <paramref name="source"/> into an EXISTING dictionary,
    /// resetting every key first. Lets a caller that ticks in a loop
    /// reuse one dictionary for the life of the loop instead of
    /// allocating a fresh one per frame.
    /// </summary>
    public static void CopyInto(IReadOnlyDictionary<ButtonId, bool> source, Dictionary<ButtonId, bool> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        foreach (var button in AllButtons)
        {
            destination[button] = false;
        }
        foreach (var pair in source)
        {
            destination[pair.Key] = pair.Value;
        }
    }
}
