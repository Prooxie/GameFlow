using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using GameFlow.App.Converters;
using GameFlow.App.ViewModels;
using GameFlow.Core.Models;
using GameFlow.Infrastructure.Runtime.Input;

namespace GameFlow.App.Views;

internal static class InputSurfaceViewBuilder
{
    private static readonly VkPressedToBrushConverter Keys = new()
    {
        Active = Brush.Parse(InputSurfaceLayout.Active),
        Idle = Brush.Parse(InputSurfaceLayout.Idle)
    };
    private static readonly BoolToActiveBrushConverter Buttons = new()
    {
        Active = Brush.Parse(InputSurfaceLayout.Active),
        Idle = Brush.Parse(InputSurfaceLayout.Idle)
    };
    private static readonly VkPressedToBrushConverter KeyText = new()
    { Active = Brush.Parse("#102418"), Idle = Brush.Parse(InputSurfaceLayout.Foreground) };
    private static readonly VkPressedToBrushConverter KeyHint = new()
    { Active = Brush.Parse("#102418"), Idle = Brush.Parse(InputSurfaceLayout.Accent) };
    private static readonly VkPressedToBrushConverter KeySymbol = new()
    { Active = Brush.Parse("#102418"), Idle = Brush.Parse("#adb6c3") };
    private static readonly BoolToActiveBrushConverter ButtonText = new()
    { Active = Brush.Parse("#102418"), Idle = Brush.Parse(InputSurfaceLayout.Foreground) };
    private static readonly BoolToActiveBrushConverter ButtonHint = new()
    { Active = Brush.Parse("#102418"), Idle = Brush.Parse(InputSurfaceLayout.Accent) };

    public static void Populate(Canvas canvas, InputSurfaceLayout layout, IKeyboardMouseVisualState? model)
    {
        canvas.Children.Clear();
        canvas.Width = layout.Width;
        canvas.Height = layout.Height;
        foreach (var item in layout.Elements)
        {
            var control = Create(item, model);
            Canvas.SetLeft(control, item.X);
            Canvas.SetTop(control, item.Y);
            canvas.Children.Add(control);
        }
    }

    private static Control Create(InputSurfaceElement item, IKeyboardMouseVisualState? model)
    {
        var cap = new Border
        {
            Width = item.Width,
            Height = item.Height,
            CornerRadius = new CornerRadius(item.Radius),
            BorderBrush = Brush.Parse(item.Bound ? InputSurfaceLayout.Accent : InputSurfaceLayout.Border),
            BorderThickness = new Thickness(item.Kind == "label" || item.Kind == "motion" ? 0 : 1),
            Background = Brush.Parse(item.Kind switch
            {
                "panel" => InputSurfaceLayout.Surface,
                "body" => InputSurfaceLayout.Body,
                "label" or "motion" => "Transparent",
                _ => InputSurfaceLayout.Idle
            })
        };
        if (item.Kind == "panel") { return cap; }

        if (item.Kind == "key")
        {
            cap.Tag = item.Key;
            cap.Bind(Border.BackgroundProperty, new Binding(nameof(IKeyboardMouseVisualState.PressedKeysSet))
            { Converter = Keys, ConverterParameter = item.Key.ToString("X", CultureInfo.InvariantCulture) });
        }
        else
        {
            cap.Tag = item.Mouse.ToString();
            var property = MouseProperty(item.Flag);
            if (property is not null) { cap.Bind(Border.BackgroundProperty, new Binding(property) { Converter = Buttons }); }
        }

        if (item.Kind != "label")
        {
            cap.Cursor = new Cursor(StandardCursorType.Hand);
            cap.PointerPressed += (_, e) =>
            {
                if (model is not null && e.GetCurrentPoint(cap).Properties.IsLeftButtonPressed)
                {
                    model.RequestInputMapping(item.Key, item.Mouse);
                    e.Handled = true;
                }
            };
        }

        if (item.Kind == "motion")
        {
            var arrow = new Line
            {
                StartPoint = new Point(30, 30),
                Stroke = Brush.Parse(InputSurfaceLayout.Accent),
                StrokeThickness = 3,
                StrokeLineCap = PenLineCap.Round
            };
            arrow.Bind(Line.EndPointProperty, new Binding(nameof(IKeyboardMouseVisualState.MouseAimEndPoint)));
            var compass = new Canvas { Width = 60, Height = 60 };
            var ring = new Ellipse { Width = 50, Height = 50, Stroke = Brush.Parse(InputSurfaceLayout.Border), StrokeThickness = 1 };
            Canvas.SetLeft(ring, 5); Canvas.SetTop(ring, 5);
            compass.Children.Add(ring);
            compass.Children.Add(arrow);
            cap.Child = compass;
        }
        else if (item.Kind != "body")
        {
            var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var small = item.Kind == "key" && item.Width <= KeyboardPhysicalLayout.KeyWidth && item.Label.Length >= 5;
            var title = new TextBlock
            {
                Text = item.Label,
                FontSize = small ? 8.25 : 10.5,
                FontWeight = FontWeight.SemiBold,
                LineHeight = (small ? 8.25 : 10.5) * 1.2,
                Foreground = Brush.Parse(InputSurfaceLayout.Foreground),
                FontFamily = new FontFamily("Consolas, monospace"),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            BindTextColor(title, item, false);
            labels.Children.Add(title);
            if (!string.IsNullOrEmpty(item.Hint))
            {
                var hint = new TextBlock
                {
                    Text = item.Hint,
                    FontSize = 7.5,
                    LineHeight = 8,
                    FontFamily = new FontFamily("Consolas, monospace"),
                    Foreground = Brush.Parse(item.Bound ? InputSurfaceLayout.Accent : InputSurfaceLayout.Foreground),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = item.Width - 4,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                BindTextColor(hint, item, true);
                labels.Children.Add(hint);
            }
            cap.Child = labels;
        }
        return cap;
    }

    private static void BindTextColor(TextBlock text, InputSurfaceElement item, bool hint)
    {
        if (item.Key > 0)
        {
            text.Bind(TextBlock.ForegroundProperty, new Binding(nameof(IKeyboardMouseVisualState.PressedKeysSet))
            {
                Converter = hint ? item.Bound ? KeyHint : KeySymbol : KeyText,
                ConverterParameter = item.Key.ToString("X", CultureInfo.InvariantCulture)
            });
            return;
        }
        var property = MouseProperty(item.Flag);
        if (property is not null)
        {
            text.Bind(TextBlock.ForegroundProperty, new Binding(property) { Converter = hint ? ButtonHint : ButtonText });
        }
    }

    private static string? MouseProperty(MouseButtonFlags flag) => flag switch
    {
        MouseButtonFlags.Left => nameof(IKeyboardMouseVisualState.IsMouseLeftDown),
        MouseButtonFlags.Right => nameof(IKeyboardMouseVisualState.IsMouseRightDown),
        MouseButtonFlags.Middle => nameof(IKeyboardMouseVisualState.IsMouseMiddleDown),
        MouseButtonFlags.Back => nameof(IKeyboardMouseVisualState.IsMouseButton4Down),
        MouseButtonFlags.Forward => nameof(IKeyboardMouseVisualState.IsMouseButton5Down),
        MouseButtonFlags.WheelUp => nameof(IKeyboardMouseVisualState.IsMouseScrollUp),
        MouseButtonFlags.WheelDown => nameof(IKeyboardMouseVisualState.IsMouseScrollDown),
        _ => null
    };
}
