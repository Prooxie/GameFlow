using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using GameFlow.App.Services;
using GameFlow.App.ViewModels;
using GameFlow.Core.Enums;
using GameFlow.Infrastructure.Runtime.Input;

namespace GameFlow.App.Views;

/// <summary>The same native mouse geometry and legends that OBS receives.</summary>
public partial class MouseSurface : UserControl
{
    private bool attached;
    private INotifyPropertyChanged? legendContext;

    public MouseSurface()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += (_, _) => BindLegendContext();
        ApplyLegend();
    }

    private void BindLegendContext()
    {
        if (legendContext is not null) { legendContext.PropertyChanged -= OnLegendContextChanged; }
        legendContext = attached ? DataContext as INotifyPropertyChanged : null;
        if (legendContext is not null) { legendContext.PropertyChanged += OnLegendContextChanged; }
        ApplyLegend();
    }

    private void OnLegendContextChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == "MouseDeviceId") { ApplyLegend(); }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        attached = true;
        BindLegendContext();
        KeyboardMouseLegend.Changed += OnLegendChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        attached = false;
        BindLegendContext();
        KeyboardMouseLegend.Changed -= OnLegendChanged;
    }

    private void OnLegendChanged(object? sender, EventArgs e) => ApplyLegend();

    private void ApplyLegend()
    {
        var model = DataContext as IKeyboardMouseVisualState;
        var layout = InputSurfacePresentation.Create(ControllerVisualStyle.Mouse, model?.KeyboardDeviceId, model?.MouseDeviceId);
        InputSurfaceViewBuilder.Populate(this.FindControl<Canvas>("InputCanvas")!, layout, model);
    }
}
