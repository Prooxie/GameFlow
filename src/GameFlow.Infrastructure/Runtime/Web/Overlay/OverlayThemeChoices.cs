using System.Collections.Concurrent;
using GameFlow.Infrastructure.Theming;

namespace GameFlow.Infrastructure.Runtime.Web.Overlay;

/// <summary>
/// The theme that each dashboard panel shows now, per slot and side.
/// </summary>
/// <remarks>
/// <para>
/// The dashboard writes here when a panel changes its theme: the user picks a
/// skin, or the slot type changes and the panel loads the theme for the new
/// style. <see cref="OverlayFeed"/> reads here, so an overlay URL without a
/// <c>theme</c> parameter shows the same skin as the app.
/// </para>
/// <para>
/// Runtime state only. Nothing here is saved to disk. The UI thread writes,
/// and the web server threads read, so the map is concurrent.
/// </para>
/// </remarks>
public sealed class OverlayThemeChoices
{
    private readonly ConcurrentDictionary<(string SlotId, bool Physical), InstalledTheme> themes = new();

    /// <summary>
    /// Records the theme that a panel shows. A null theme removes the record.
    /// </summary>
    /// <param name="slotId">Id of the slot that the panel shows.</param>
    /// <param name="physical">True for the physical side, false for the virtual side.</param>
    /// <param name="theme">The theme that the panel draws, or null when it draws none.</param>
    public void Set(string slotId, bool physical, InstalledTheme? theme)
    {
        ArgumentNullException.ThrowIfNull(slotId);

        if (theme is null)
        {
            _ = themes.TryRemove((slotId, physical), out _);
            return;
        }

        themes[(slotId, physical)] = theme;
    }

    /// <summary>The theme that the panel shows, or null when no panel recorded one.</summary>
    public InstalledTheme? Get(string slotId, bool physical)
    {
        ArgumentNullException.ThrowIfNull(slotId);
        return themes.TryGetValue((slotId, physical), out var theme) ? theme : null;
    }

    /// <summary>Removes both sides of a slot. Called when its dashboard panel closes.</summary>
    public void Remove(string slotId)
    {
        ArgumentNullException.ThrowIfNull(slotId);
        _ = themes.TryRemove((slotId, false), out _);
        _ = themes.TryRemove((slotId, true), out _);
    }
}
