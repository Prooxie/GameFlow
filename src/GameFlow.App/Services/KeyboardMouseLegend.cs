using Avalonia.Threading;
using GameFlow.Infrastructure.Runtime.Input;

namespace GameFlow.App.Services;

/// <summary>
/// The binding map the keyboard and mouse pictures label themselves from.
/// </summary>
/// <remarks>
/// A static seam for the same reason as <c>ThemeRegistry.Shared</c>: the
/// pictures are controls, built by XAML with no constructor injection, and
/// they appear in several places (Devices page, dashboard, the setup
/// guide, the binding editor). Every one of them must show the same legend,
/// and must update the moment a binding changes.
/// </remarks>
public static class KeyboardMouseLegend
{
    private static KeyboardMouseMapStore? store;

    /// <summary>The current map; the built-in layout until a store is attached.</summary>
    public static CompiledKeyboardMouseMap Current => store?.Compiled ?? CompiledKeyboardMouseMap.Default;
    public static CompiledKeyboardMouseMap ForDevice(string? id) => store?.CompiledFor(id) ?? CompiledKeyboardMouseMap.Default;

    /// <summary>Raised on the UI thread after the map changes.</summary>
    public static event EventHandler? Changed;

    /// <summary>Connects the legend to the persisted map. Called once at startup.</summary>
    public static void Attach(KeyboardMouseMapStore mapStore)
    {
        ArgumentNullException.ThrowIfNull(mapStore);
        if (store is not null)
        {
            store.Changed -= OnStoreChanged;
        }

        store = mapStore;
        store.Changed += OnStoreChanged;
        OnStoreChanged(null, EventArgs.Empty);
    }

    public static void Detach()
    {
        if (store is not null) { store.Changed -= OnStoreChanged; }
        store = null;
    }

    private static void OnStoreChanged(object? sender, EventArgs e)
    {
        var current = store;
        Dispatcher.UIThread.Post(() =>
        {
            if (current is not null && ReferenceEquals(current, store)) { Changed?.Invoke(null, EventArgs.Empty); }
        });
    }
}
