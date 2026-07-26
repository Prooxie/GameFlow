namespace GameFlow.Core.Formulas;

/// <summary>One entry in the starter-recipe catalog: a name, what it does, and the formula (with the source roles it expects).</summary>
public sealed record FormulaRecipe(string Name, string Description, string Expression, string SourceHints);

/// <summary>
/// The ten starter recipes for the Formula combine mode — the catalog
/// the (future) drag-and-drop editor lists for one-click insertion.
/// Lives in Core rather than the App layer so tests can verify every
/// recipe actually compiles, forever: a recipe that stops parsing is a
/// broken promise in the UI.
/// </summary>
public static class FormulaRecipes
{
    public static IReadOnlyList<FormulaRecipe> All { get; } =
    [
        new("Two buttons → one axis",
            "s1 pushes positive, s2 pushes negative — a D-pad pair or two keys become an analog axis.",
            "s1 - s2", "s1 = positive button, s2 = negative button"),

        new("Strongest input wins",
            "Whichever source is pushed hardest drives the output.",
            "max(s1, s2)", "s1, s2 = any two analog sources"),

        new("Both required (gate)",
            "Output follows s2, but only while s1 is held — a safety/modifier gate.",
            "if(s1 > 0.5, s2, 0)", "s1 = gate button, s2 = the value to pass through"),

        new("Blend 50/50",
            "The average of two sources — two people steering one wheel.",
            "(s1 + s2) / 2", "s1, s2 = the two inputs to blend"),

        new("Weighted blend",
            "70% of s1 plus 30% of s2 — a main input with a trim input.",
            "s1 * 0.7 + s2 * 0.3", "s1 = main, s2 = trim"),

        new("Boost while held",
            "s1 passes through at half strength normally, full strength while s2 is held — walk/sprint.",
            "if(s2 > 0.5, s1, s1 * 0.5)", "s1 = movement axis, s2 = sprint button"),

        new("Invert",
            "Flips a source's direction — inverted look on one axis.",
            "-s1", "s1 = the axis to invert"),

        new("Threshold to digital",
            "Analog in, clean 0-or-1 out — a trigger becomes a button at 40% pull.",
            "s1 > 0.4", "s1 = the analog source"),

        new("Sum, capped",
            "Two sources stack up to (never past) full press.",
            "clamp(s1 + s2, 0, 1)", "s1, s2 = the sources to stack"),

        new("Deadzone re-map",
            "Ignores the first 15% of travel, rescaling the rest to a full 0..1 sweep.",
            "clamp((abs(s1) - 0.15) / 0.85, 0, 1)", "s1 = the analog source"),
    ];
}
