using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using GameFlow.Infrastructure.Runtime.Web;

namespace GameFlow.App.Views;

/// <summary>
/// Draws a <see cref="QrCode"/> as dark modules on white, with the
/// four-module quiet zone the standard requires, scaled to whole device
/// pixels per module so a camera sees crisp edges.
/// </summary>
/// <remarks>
/// Always black on white whatever the app theme: phone cameras read
/// inverted (light-on-dark) codes unreliably, and a dark-themed code is
/// exactly that.
/// </remarks>
public sealed class QrCodeView : Control
{
    public static readonly StyledProperty<QrCode?> CodeProperty =
        AvaloniaProperty.Register<QrCodeView, QrCode?>(nameof(Code));

    private const int QuietZone = 4;

    static QrCodeView()
    {
        AffectsRender<QrCodeView>(CodeProperty);
        AffectsMeasure<QrCodeView>(CodeProperty);
    }

    public QrCode? Code
    {
        get => GetValue(CodeProperty);
        set => SetValue(CodeProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var side = Math.Min(
            double.IsInfinity(availableSize.Width) ? 240 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 240 : availableSize.Height);
        return new Size(side, side);
    }

    public override void Render(DrawingContext context)
    {
        var code = Code;
        var side = Math.Min(Bounds.Width, Bounds.Height);
        if (code is null || side <= 0)
        {
            return;
        }

        var modulesAcross = code.Size + (QuietZone * 2);
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;

        // Whole device pixels per module: a fractional size anti-aliases
        // every edge into grey, which is what makes a code hard to read.
        var devicePixelsPerModule = Math.Max(1, Math.Floor(side * scaling / modulesAcross));
        var module = devicePixelsPerModule / scaling;
        var drawn = module * modulesAcross;
        var origin = new Point(Math.Round((Bounds.Width - drawn) / 2 * scaling) / scaling,
                               Math.Round((Bounds.Height - drawn) / 2 * scaling) / scaling);

        context.FillRectangle(Brushes.White, new Rect(origin, new Size(drawn, drawn)));
        for (var y = 0; y < code.Size; y++)
        {
            for (var x = 0; x < code.Size; x++)
            {
                if (code[x, y])
                {
                    context.FillRectangle(Brushes.Black, new Rect(
                        origin.X + ((x + QuietZone) * module),
                        origin.Y + ((y + QuietZone) * module),
                        module,
                        module));
                }
            }
        }
    }
}
