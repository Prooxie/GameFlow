using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using GameFlow.App.ViewModels;
using GameFlow.Core.Enums;
using GameFlow.Infrastructure.Theming;

namespace GameFlow.App.Views;

/// <summary>Hosts controller art and forwards view-model changes during the same UI refresh.</summary>
public partial class ControllerSurface : UserControl
{
    // A control has no constructor injection, so this one reaches the
    // process-wide registry directly rather than through DI. It is the
    // same instance the overlay server is handed — see ThemeRegistry.Shared.
    private static ThemeRegistry SharedRegistry => ThemeRegistry.Shared;

    private ControllerVisualStateViewModel? boundViewModel;
    private ThemeSurface? surface;
    private TextBlock? noThemeMessage;

    public ControllerSurface()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        EffectiveViewportChanged += (_, _) => PushVisualState();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// Click-to-edit on the VIRTUAL panel. Delegates the decision to the
    /// view model (<see cref="ControllerVisualStateViewModel.RequestEdit"/>),
    /// which no-ops unless this really is an editable virtual panel — so a
    /// stray click on a physical panel can never open an editor for the
    /// wrong thing.
    /// </summary>
    private void OnPanelPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (GameFlow.App.Services.ControllerPanelInput.IsControlInteraction(e.Source)) { return; }
        if (DataContext is ControllerVisualStateViewModel viewModel && viewModel.IsEditable)
        {
            viewModel.RequestEdit();
            e.Handled = true;
        }
    }

    /// <summary>Hand cursor on editable panels only, so the affordance matches the behaviour.</summary>
    private void ApplyEditableCursor(ControllerVisualStateViewModel? viewModel)
    {
        if (this.FindControl<Border>("PanelRoot") is { } root)
        {
            root.Cursor = viewModel?.IsEditable == true
                ? new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
                : Avalonia.Input.Cursor.Default;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        surface = this.FindControl<ThemeSurface>("ThemedSurface");
        noThemeMessage = this.FindControl<TextBlock>("NoThemeMessage");

        if (surface is not null)
        {
            surface.Clicked += OnSurfaceClicked;
        }

        OnDataContextChanged(this, EventArgs.Empty);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        UnsubscribeViewModel();

        if (surface is not null)
        {
            surface.Clicked -= OnSurfaceClicked;
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        // Detach any handlers from a previous binding so the registry
        // doesn't end up wired into two VMs.
        UnsubscribeViewModel();
        boundViewModel = DataContext as ControllerVisualStateViewModel;
        if (boundViewModel is not null && TopLevel.GetTopLevel(this) is not null)
        {
            boundViewModel.VisualStateChanged += OnVisualStateChanged;
            boundViewModel.PropertyChanged += OnVisualPropertyChanged;
        }

        // Reflect this panel's editability in the cursor as soon as it's
        // bound — otherwise the hand cursor would lag a panel swap.
        ApplyEditableCursor(boundViewModel);

        // Hand the registry over to the VM so its
        // AvailableThemeVariants list populates and SelectedThemeVariant
        // resolves to a real theme. The VM's setter triggers
        // RefreshActiveTheme internally.
        if (boundViewModel is not null && boundViewModel.ThemeRegistry is null)
        {
            boundViewModel.ThemeRegistry = SharedRegistry;
        }
        PushVisualState();
    }

    /// <summary>Releases subscriptions when a panel changes owner or leaves the visual tree.</summary>
    private void UnsubscribeViewModel()
    {
        if (boundViewModel is null) { return; }
        boundViewModel.VisualStateChanged -= OnVisualStateChanged;
        boundViewModel.PropertyChanged -= OnVisualPropertyChanged;
    }

    /// <summary>Forwards a new snapshot without a second polling interval.</summary>
    private void OnVisualStateChanged(object? sender, EventArgs e) => PushVisualState();

    /// <summary>Updates theme choices and lighting even when input stays unchanged.</summary>
    private void OnVisualPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ControllerVisualStateViewModel.ActiveTheme)
            or nameof(ControllerVisualStateViewModel.LightColor)
            or nameof(ControllerVisualStateViewModel.IsPhysicalView))
        {
            PushVisualState();
        }
    }

    private void OnSurfaceClicked(object? sender, ThemeClickEventArgs e)
    {
        // Forward the (X, Y) in theme-local coordinates to the VM's
        // hit-tester / SelectElement pipeline.
        var vm = boundViewModel;
        if (vm is null) { return; }

        if (vm.ClickAtCommand.CanExecute(null))
        {
            vm.ClickAtCommand.Execute((e.X, e.Y));
        }
    }

    private void PushVisualState()
    {
        var vm = boundViewModel;
        if (vm is null || surface is null) { return; }

        // Skip everything when this surface isn't actually on screen.
        // Hidden panels (e.g. the virtual panel while emulation is off)
        // and collapsed surfaces still receive timer ticks because they
        // remain attached to the visual tree; pushing state to a surface
        // that won't render is pure waste and 30x/sec adds up when
        // several surfaces are alive at once.
        if (!IsEffectivelyVisible) { return; }

        if (!vm.IsThemedStyle)
        {
            // Keyboard and mouse pictures bind to the view model directly;
            // the theme surface is hidden, so feeding it is wasted work and
            // its "no theme installed" note does not apply.
            if (noThemeMessage is not null) { noThemeMessage.IsVisible = false; }
            return;
        }

        // The VM has already resolved its visual style and theme via
        // RefreshActiveTheme(); we just need to push the result.
        surface.ActiveTheme = vm.ActiveTheme;
        surface.IsPhysicalView = vm.IsPhysicalView;
        surface.UpdateLightColor(vm.LightColor);
        surface.UpdateState(vm.RawSnapshot);

        if (noThemeMessage is not null)
        {
            // Only once a lookup has actually run. A null ActiveTheme on its
            // own also means "not resolved yet", and treating the two the
            // same put "no controller theme installed" on every panel during
            // startup — longest on the physical ones, whose style depends on
            // the detected device and so settles last.
            noThemeMessage.IsVisible = vm.HasResolvedTheme && vm.ActiveTheme is null;
        }
    }
}
