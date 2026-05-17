using System.Collections.Concurrent;
using Autofire.App.ViewModels;
using Autofire.Core.Models;
using Autofire.Infrastructure.Theming;
using Autofire.Infrastructure.Theming.Flee;
using Autofire.Infrastructure.Theming.Models;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Serilog;

namespace Autofire.App.Views;

/// <summary>
/// Avalonia control that paints a parsed VSCView-compatible
/// <see cref="ThemeDocument"/> against a live
/// <see cref="ControllerSnapshot"/>. The host pushes <see cref="ActiveTheme"/>
/// and snapshot updates imperatively via <see cref="UpdateState"/>;
/// no styled-property bindings are involved on the hot path.
///
/// <para>
/// The control also fires <see cref="Clicked"/> on pointer-pressed with
/// the click position translated into theme-local coordinates, so the
/// host can hit-test the click against the theme's interactive elements
/// via <see cref="Autofire.App.ViewModels.ThemeHitTester"/>. This is the
/// foundation for the "click a button on the controller to map it"
/// workflow.
/// </para>
/// </summary>
public sealed class ThemeSurface : Control
{
    /// <summary>
    /// Bitmap cache keyed on the absolute resolved path of the image.
    /// Stores <see langword="null"/> for paths that failed to load so a
    /// single missing PNG does not cause the whole tree to throw on
    /// every render tick. Shared across instances because two
    /// ThemeSurfaces (physical + virtual) typically reuse the same
    /// base PNG.
    /// </summary>
    private static readonly ConcurrentDictionary<string, Bitmap?> BitmapCache = new();

    private readonly ControllerStateSymbols symbols = new();

    private InstalledTheme? activeTheme;
    private ControllerSnapshot snapshot = ControllerSnapshot.Empty();

    // One-shot diagnostic log so we can see (in the Serilog file)
    // whether a theme was ever loaded for this control.
    private bool firstThemeLogged;
    private bool firstNullLogged;
    private bool firstButtonPressLogged;

    // Throttled feedback-diagnostic state. Once per second per surface
    // we dump the snapshot's pressed-button count + the eval result of
    // every top-level showhide expression, so users diagnosing "no live
    // feedback" can confirm whether the snapshot is reaching the
    // surface and whether the Flee symbol table is resolving the
    // expected variables.
    private DateTime lastFeedbackDiagnostic = DateTime.MinValue;

    public ThemeSurface()
    {
        // Intentionally NOT focusable — pointer events (Move/Pressed/
        // Exited) fire regardless of focusability, but Focusable=true
        // would put the surface into the Tab cycle and let it steal
        // keyboard focus from neighbouring controls when clicked. That
        // turned out to break the variant ComboBox's popup: opening the
        // dropdown would briefly transfer focus to the surface, the
        // popup would lose its keyboard chain, and the next pointer
        // event would close the popup before the user could select an
        // item. Click-to-map works fine without focus.
        Focusable = false;
        // Control doesn't expose a Background styled property like
        // Panel/Border do — but we still need the full bounds to
        // catch pointer-pressed for click-to-map. The Render method
        // below draws a transparent fill across Bounds before any
        // theme art, which gives Avalonia a hit-testable region.
    }

    /// <summary>
    /// Theme currently being rendered, or <see langword="null"/> when
    /// no theme is set. Renamed from <c>Theme</c> to avoid colliding
    /// with the inherited <see cref="StyledElement.Theme"/> styled
    /// property.
    /// </summary>
    public InstalledTheme? ActiveTheme
    {
        get => activeTheme;
        set
        {
            if (ReferenceEquals(activeTheme, value)) { return; }
            activeTheme = value;

            if (value is not null && !firstThemeLogged)
            {
                firstThemeLogged = true;
                Log.Information(
                    "ThemeSurface received theme '{Name}' from {Dir} ({Children} root child(ren), canvas {W}x{H}).",
                    value.Document.Name, value.Document.BaseDirectory,
                    value.Document.Children.Count,
                    value.Document.Width, value.Document.Height);
            }
            else if (value is null && !firstNullLogged)
            {
                firstNullLogged = true;
                Log.Information("ThemeSurface received null theme.");
            }
            InvalidateVisual();
        }
    }

    /// <summary>Pushes a new snapshot and schedules a redraw.</summary>
    public void UpdateState(ControllerSnapshot newSnapshot)
    {
        snapshot = newSnapshot;
        if (activeTheme is not null)
        {
            InvalidateVisual();
        }
    }

    /// <summary>
    /// When true, the surface renders ONLY the controller's base image
    /// (the first <see cref="ImageNode"/> child of the theme document)
    /// and skips every <c>showhide</c>/<c>pbar</c>/active overlay. This
    /// fulfils the "physical view = original model only, no live
    /// feedback" rule used for the input-side panel. The virtual panel
    /// keeps the full live-feedback render.
    /// </summary>
    public bool IsPhysicalView
    {
        get => isPhysicalView;
        set
        {
            if (isPhysicalView == value) { return; }
            isPhysicalView = value;
            InvalidateVisual();
        }
    }
    private bool isPhysicalView;

    /// <summary>
    /// Fires when the user clicks anywhere on the surface. Carries the
    /// click position in theme-local (canvas) coordinates so the host
    /// can hit-test it against the theme's interactive elements without
    /// having to know about display scaling, letterboxing or DPI.
    /// </summary>
    public event EventHandler<ThemeClickEventArgs>? Clicked;

    /// <summary>
    /// Last transform applied in Render — captured here so
    /// OnPointerPressed can invert it without re-walking the document.
    /// (uniformScale, offsetX, offsetY)
    /// </summary>
    private (double Scale, double OffsetX, double OffsetY) lastTransform;

    // ─── Hover + click-to-map highlight state ────────────────────────────
    //
    // Painted on top of all the regular theme content at the end of
    // Render. Hover = a thin outline around whatever interactive area
    // the cursor is currently over (click-to-map affordance). Selected
    // = a thicker outline with a translucent fill, persisted until the
    // user clicks somewhere else. Mutation is internal to the surface
    // — Clicked still fires for the host so the VM's existing
    // SelectElement pipeline runs in parallel.

    private ThemeHitResult? hoveredHit;
    private ThemeHitResult? selectedHit;

    /// <summary>
    /// Cached hand cursor used when the pointer is over a mappable
    /// element. Allocating a new <see cref="Cursor"/> every move tick
    /// was wasteful — this stays alive for the process lifetime.
    /// </summary>
    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);

    /// <summary>
    /// Outline / fill colour for the click-to-map highlight. Bright
    /// amber so it contrasts cleanly with the typical dark UI as well
    /// as the cyan-teal of the active-press overlays in the asset
    /// pack.
    /// </summary>
    private static readonly Color HighlightStrokeColor = Color.FromArgb(0xFF, 0xFF, 0xC3, 0x00);

    /// <summary>
    /// Translucent variant of <see cref="HighlightStrokeColor"/> used
    /// to fill the selected element's rect. Alpha is kept low so the
    /// underlying art (button glyph, trigger, etc.) stays legible
    /// through the highlight.
    /// </summary>
    private static readonly Color HighlightFillColor = Color.FromArgb(0x55, 0xFF, 0xC3, 0x00);

    /// <summary>Pre-built solid amber brush — fill for the selected element's rect.</summary>
    private static readonly SolidColorBrush HighlightFillBrush = new(HighlightFillColor);

    /// <summary>Pre-built thicker stroke pen used for the selection outline.</summary>
    private static readonly Pen HighlightSelectedPen =
        new(new SolidColorBrush(HighlightStrokeColor), 3);

    /// <summary>Pre-built thinner stroke pen used for the hover outline only.</summary>
    private static readonly Pen HighlightHoverPen =
        new(new SolidColorBrush(HighlightStrokeColor), 2);

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        // Always defer to the parent's allocation. Returning the theme's
        // huge native size (1534x954) tells the layout system the
        // control wants that much space, which in an unconstrained
        // panel makes the whole window overflow. Stretch alignment in
        // the parent then gives us whatever space is actually free.
        var w = double.IsInfinity(availableSize.Width)  ? 480 : availableSize.Width;
        var h = double.IsInfinity(availableSize.Height) ? 280 : availableSize.Height;
        return new Size(w, h);
    }

    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        base.Render(context);

        // Transparent fill: gives the control a hit-test surface so
        // OnPointerPressed fires on clicks anywhere in Bounds, not
        // just on rendered theme art. Zero visual cost.
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));

        var theme = activeTheme;
        if (theme is null) { return; }

        symbols.UpdateSnapshot(snapshot);

        // One-shot diagnostic: confirm the first time we see live
        // button data flow into the surface. Useful for debugging
        // "no feedback" reports — if this never fires while the user
        // is actively pressing buttons, the bug is upstream in the
        // input pipeline / VM update chain, NOT in the theme engine.
        if (!firstButtonPressLogged && snapshot.Buttons.Count(kv => kv.Value) > 0)
        {
            firstButtonPressLogged = true;
            Log.Information(
                "ThemeSurface[{Mode}] first live button press: device={Device} pressed={Pressed}",
                isPhysicalView ? "physical" : "virtual",
                snapshot.DeviceName,
                snapshot.Buttons.Count(kv => kv.Value));
        }

        // Throttled feedback diagnostic: once per second per surface,
        // log snapshot state + showhide eval results so the user can
        // confirm input → symbols → expression chain. Cheap (handful
        // of expression evals) and gated by a wall-clock check, so it
        // never costs anything on the render hot path.
        var now = DateTime.UtcNow;
        if ((now - lastFeedbackDiagnostic).TotalSeconds >= 1.0)
        {
            lastFeedbackDiagnostic = now;
            var pressed = snapshot.Buttons.Count(kv => kv.Value);
            var samples = new System.Text.StringBuilder();
            foreach (var node in theme.Document.Children)
            {
                if (node is ShowHideNode show && samples.Length < 200)
                {
                    var val = show.Input.Evaluate(symbols);
                    if (samples.Length > 0) { samples.Append(", "); }
                    var ast = show.Input;
                    var varName = ast is VariableNode v ? v.Name : ast.GetType().Name;
                    samples.Append(varName).Append('=').Append(val);
                }
            }
            Log.Information(
                "ThemeSurface[{Mode}] tick: device={Device} pressed={Pressed}/{Total} L=({LX:F2},{LY:F2}) R=({RX:F2},{RY:F2}) LT={LT:F2} RT={RT:F2} showhide=[{Samples}]",
                isPhysicalView ? "physical" : "virtual",
                snapshot.DeviceName,
                pressed, snapshot.Buttons.Count,
                snapshot.LeftStick.X, snapshot.LeftStick.Y,
                snapshot.RightStick.X, snapshot.RightStick.Y,
                snapshot.LeftTrigger, snapshot.RightTrigger,
                samples.ToString());
        }

        var doc = theme.Document;
        if (doc.Width <= 0 || doc.Height <= 0) { return; }

        var scaleX = Bounds.Width / doc.Width;
        var scaleY = Bounds.Height / doc.Height;
        var uniform = Math.Min(scaleX, scaleY);
        if (uniform <= 0) { return; }

        var renderedW = doc.Width * uniform;
        var renderedH = doc.Height * uniform;
        var offsetX = (Bounds.Width - renderedW) / 2;
        var offsetY = (Bounds.Height - renderedH) / 2;

        // Capture transform so OnPointerPressed can invert it without
        // re-walking the document. Stored in display (control) pixels.
        lastTransform = (uniform, offsetX, offsetY);

        // High-quality scaling for the controller art. Avalonia defaults to a
        // fast/low-quality bitmap filter; for our static-ish 30 Hz surface the
        // CPU cost is negligible compared to the visual gain. EdgeMode.Antialias
        // smooths the implicit transform edges that the PNG's alpha channel
        // crosses at non-1:1 scales.
        using (context.PushRenderOptions(new RenderOptions
        {
            BitmapInterpolationMode = BitmapInterpolationMode.HighQuality,
            EdgeMode = EdgeMode.Antialias,
        }))
        using (context.PushTransform(Matrix.CreateScale(uniform, uniform) *
                                     Matrix.CreateTranslation(offsetX, offsetY)))
        {
            // Both physical and virtual panels render the full theme.
            // Each surface independently consumes its own snapshot
            // (physical = input source, virtual = output emitted to
            // ViGEm), so feedback animates naturally in each panel from
            // its respective source. The IsPhysicalView flag is kept
            // on the surface for potential future use (e.g. a
            // "passive view" toggle) but it no longer gates render
            // output — users have asked for live feedback on both
            // sides.
            foreach (var node in doc.Children)
            {
                RenderNode(context, node, theme);
            }

            // Click-to-map highlights painted on top so they're never
            // occluded by overlay images. Selected first (so the
            // outline appears on top of its own fill), hover on top of
            // selected so the user always sees the cursor-anchored
            // outline. If the cursor is currently over the selected
            // element we paint only the selected highlight (avoids
            // doubled outlines).
            if (selectedHit is not null)
            {
                // 3px stroke / 30% fill — strong "you've picked this"
                // affordance that persists until a new pick.
                context.DrawRectangle(HighlightFillBrush, HighlightSelectedPen, selectedHit.Bounds);
            }
            if (hoveredHit is not null &&
                hoveredHit.ElementId != selectedHit?.ElementId)
            {
                // 2px outline only — a lighter touch since hover is
                // ephemeral. No fill, so the underlying button glyph
                // stays visible.
                context.DrawRectangle(null, HighlightHoverPen, hoveredHit.Bounds);
            }
        }
    }

    /// <summary>
    /// Pointer-moved handler — runs the hit-tester at the cursor and
    /// updates <see cref="hoveredHit"/> if the result changes.
    /// Invalidates only on actual change so we don't redraw every
    /// mouse-move event at the cursor's native polling rate.
    /// </summary>
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (activeTheme is null) { return; }
        if (lastTransform.Scale <= 0) { return; }

        var p = e.GetPosition(this);
        var localX = (p.X - lastTransform.OffsetX) / lastTransform.Scale;
        var localY = (p.Y - lastTransform.OffsetY) / lastTransform.Scale;

        var doc = activeTheme.Document;
        ThemeHitResult? newHit = null;
        if (localX >= 0 && localY >= 0 && localX <= doc.Width && localY <= doc.Height)
        {
            newHit = ThemeHitTester.TryHit(doc, localX, localY);
        }

        if (newHit?.ElementId != hoveredHit?.ElementId)
        {
            hoveredHit = newHit;
            // Cursor affordance — Hand when over a mappable element,
            // default arrow otherwise. The user sees "this is
            // clickable" before they even press.
            Cursor = newHit is not null ? HandCursor : Cursor.Default;
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Pointer-exited handler — clears the hover highlight when the
    /// cursor leaves the surface entirely.
    /// </summary>
    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (hoveredHit is not null)
        {
            hoveredHit = null;
            Cursor = Cursor.Default;
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Pointer-pressed handler translates the click into theme-local
    /// (canvas) coordinates and fires <see cref="Clicked"/>. Uses the
    /// transform captured by the last <see cref="Render"/> pass — if
    /// the surface hasn't rendered yet, the click is ignored.
    /// </summary>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (Clicked is null) { return; }
        if (activeTheme is null) { return; }
        if (lastTransform.Scale <= 0) { return; }

        // Only respond to primary-button clicks; let middle/right
        // bubble through for context-menu binding elsewhere.
        var pointerProps = e.GetCurrentPoint(this).Properties;
        if (!pointerProps.IsLeftButtonPressed) { return; }

        var p = e.GetPosition(this);
        var localX = (p.X - lastTransform.OffsetX) / lastTransform.Scale;
        var localY = (p.Y - lastTransform.OffsetY) / lastTransform.Scale;

        var doc = activeTheme.Document;
        if (localX < 0 || localY < 0 || localX > doc.Width || localY > doc.Height)
        {
            return;
        }

        // Update the persistent selection highlight. Clicking a
        // mappable element sets it as the new selection; clicking
        // empty space (no hit) clears the selection. The change is
        // local to the surface, but Clicked still fires so the host
        // VM's existing SelectElement pipeline runs as well.
        var newSelection = ThemeHitTester.TryHit(doc, localX, localY);
        if (newSelection?.ElementId != selectedHit?.ElementId)
        {
            selectedHit = newSelection;
            InvalidateVisual();
        }

        Clicked.Invoke(this, new ThemeClickEventArgs(localX, localY));
        e.Handled = true;
    }

    /// <summary>
    /// Render walker used exclusively for the physical-view panel.
    /// Draws every passive element (base images, lightbar, trigger
    /// bodies, stick wells at rest) and skips every active-feedback
    /// element (<see cref="ShowHideNode"/> button overlays,
    /// <see cref="PBarNode"/> trigger fills, and the click overlays
    /// nested inside <see cref="SliderNode"/>). The slider's own
    /// deflection translation is also dropped so the stick sits in
    /// its neutral position regardless of live input.
    ///
    /// <para>
    /// This sits next to <see cref="RenderNode"/> rather than gating
    /// behaviour with an <c>if (isPhysicalView)</c> per case branch
    /// because the rules differ at every node type, and a single
    /// flag-checking walker would be harder to reason about than two
    /// small parallel walkers.
    /// </para>
    /// </summary>
    private void RenderNodeStaticOnly(DrawingContext ctx, ThemeNode node, InstalledTheme owner)
    {
        var translation = Matrix.CreateTranslation(node.X, node.Y);
        var transform = node.Rotation != 0
            ? Matrix.CreateRotation(node.Rotation * Math.PI / 180.0) * translation
            : translation;

        switch (node)
        {
            case ShowHideNode:
            case PBarNode:
                // Active feedback — skip entirely in physical view.
                return;

            case ImageNode image:
                using (ctx.PushTransform(transform))
                {
                    DrawImage(ctx, image, owner);
                    foreach (var child in image.Children)
                    {
                        RenderNodeStaticOnly(ctx, child, owner);
                    }
                }
                return;

            case SliderNode slider:
                // Render at neutral position — drop the InputX/InputY
                // deflection that the virtual view applies, so the
                // stick sits in its well regardless of live input.
                using (ctx.PushTransform(transform))
                {
                    foreach (var child in slider.Children)
                    {
                        RenderNodeStaticOnly(ctx, child, owner);
                    }
                }
                return;

            default:
                using (ctx.PushTransform(transform))
                {
                    foreach (var child in node.Children)
                    {
                        RenderNodeStaticOnly(ctx, child, owner);
                    }
                }
                return;
        }
    }

    private void RenderNode(DrawingContext ctx, ThemeNode node, InstalledTheme owner)
    {
        var translation = Matrix.CreateTranslation(node.X, node.Y);
        var transform = node.Rotation != 0
            ? Matrix.CreateRotation(node.Rotation * Math.PI / 180.0) * translation
            : translation;

        switch (node)
        {
            case ShowHideNode show:
                if (show.Input.Evaluate(symbols) == 0) { return; }
                using (ctx.PushTransform(transform))
                {
                    foreach (var child in show.Children) { RenderNode(ctx, child, owner); }
                }
                return;

            case SliderNode slider:
            {
                var t =
                    Matrix.CreateTranslation(slider.InputX.Evaluate(symbols),
                                             slider.InputY.Evaluate(symbols)) *
                    transform;
                var rotR = slider.InputR.Evaluate(symbols);
                if (rotR != 0)
                {
                    t = Matrix.CreateRotation(rotR * Math.PI / 180.0) * t;
                }
                using (ctx.PushTransform(t))
                {
                    foreach (var child in slider.Children) { RenderNode(ctx, child, owner); }
                }
                return;
            }

            case ImageNode image:
            {
                using (ctx.PushTransform(transform))
                {
                    DrawImage(ctx, image, owner);
                    foreach (var child in image.Children) { RenderNode(ctx, child, owner); }
                }
                return;
            }

            case PBarNode bar:
                using (ctx.PushTransform(transform))
                {
                    RenderPBar(ctx, bar, owner);
                    foreach (var child in bar.Children) { RenderNode(ctx, child, owner); }
                }
                return;

            default:
                using (ctx.PushTransform(transform))
                {
                    foreach (var child in node.Children) { RenderNode(ctx, child, owner); }
                }
                return;
        }
    }

    /// <summary>
    /// Bitmap-only draw for an <see cref="ImageNode"/> — does NOT recurse
    /// into the node's children. Used both by <see cref="RenderNode"/>
    /// (which then handles children itself) and the physical-only
    /// render branch (which deliberately stops at the leaf).
    ///
    /// <para>
    /// Caller is responsible for pushing the node's own
    /// translation/rotation transform — this method draws into the
    /// already-translated coordinate space.
    /// </para>
    /// </summary>
    private static void DrawImage(DrawingContext ctx, ImageNode image, InstalledTheme owner)
    {
        var bmp = LoadBitmap(image.ImagePath, owner);
        if (bmp is null) { return; }

        var w  = image.Width  > 0 ? image.Width  : bmp.PixelSize.Width;
        var h  = image.Height > 0 ? image.Height : bmp.PixelSize.Height;
        var dx = image.Center ? -w / 2 : 0;
        var dy = image.Center ? -h / 2 : 0;
        ctx.DrawImage(bmp, new Rect(dx, dy, w, h));
    }

    private void RenderPBar(DrawingContext ctx, PBarNode bar, InstalledTheme owner)
    {
        var value = bar.Input.Evaluate(symbols);
        var min   = bar.Min.Evaluate(symbols);
        var max   = bar.Max.Evaluate(symbols);
        var range = max - min;
        var ratio = range == 0 ? 0 : Math.Clamp((value - min) / range, 0, 1);

        var w = bar.Width;
        var h = bar.Height;
        var dx = bar.Center ? -w / 2 : 0;
        var dy = bar.Center ? -h / 2 : 0;

        Rect fillRect = bar.Direction switch
        {
            PBarDirection.Right => new Rect(dx,             dy,             w * ratio, h),
            PBarDirection.Left  => new Rect(dx + w * (1-ratio), dy,         w * ratio, h),
            PBarDirection.Up    => new Rect(dx,             dy + h * (1-ratio), w,     h * ratio),
            PBarDirection.Down  => new Rect(dx,             dy,             w,         h * ratio),
            _                   => new Rect(dx,             dy,             w * ratio, h),
        };

        if (!string.IsNullOrEmpty(bar.ImagePath))
        {
            var bmp = LoadBitmap(bar.ImagePath, owner);
            if (bmp is not null)
            {
                using (ctx.PushClip(fillRect))
                {
                    ctx.DrawImage(bmp, new Rect(dx, dy, w, h));
                }
            }
            return;
        }

        var bgBrush = HexBrush(bar.Background);
        var fgBrush = HexBrush(bar.Foreground);
        if (bgBrush is not null) { ctx.FillRectangle(bgBrush, new Rect(dx, dy, w, h)); }
        if (fgBrush is not null && ratio > 0) { ctx.FillRectangle(fgBrush, fillRect); }
    }

    /// <summary>
    /// Resolves and loads an image. Returns <see langword="null"/> on
    /// any failure so a single missing or corrupt PNG does NOT abort
    /// the whole render pass. The null is cached so subsequent ticks
    /// don't repeatedly re-attempt the same broken path.
    /// </summary>
    private static Bitmap? LoadBitmap(string imagePath, InstalledTheme owner)
    {
        if (string.IsNullOrWhiteSpace(imagePath)) { return null; }

        string? absolute;
        if (imagePath.StartsWith('\\') || imagePath.StartsWith('/'))
        {
            var root = owner.Document.ThemesRootDirectory;
            if (string.IsNullOrEmpty(root)) { return null; }
            absolute = Path.Combine(root, imagePath.TrimStart('\\', '/'));
        }
        else
        {
            var baseDir = owner.Document.BaseDirectory;
            if (string.IsNullOrEmpty(baseDir)) { return TryLoadAvares(imagePath); }
            absolute = Path.Combine(baseDir, imagePath);
        }

        absolute = Path.GetFullPath(absolute);
        return BitmapCache.GetOrAdd(absolute, p =>
        {
            try
            {
                if (!File.Exists(p))
                {
                    Log.Warning("Theme image not found on disk: {Path}", p);
                    return null;
                }
                var bmp = new Bitmap(p);
                Log.Debug("Loaded theme image {Path} ({W}x{H}).",
                    p, bmp.PixelSize.Width, bmp.PixelSize.Height);
                return bmp;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not load theme image {Path}.", p);
                return null;
            }
        });
    }

    private static Bitmap? TryLoadAvares(string relative)
    {
        try
        {
            var uri = new Uri($"avares://Autofire.App/Assets/Themes/{relative.Replace('\\', '/')}");
            using var stream = AssetLoader.Open(uri);
            return new Bitmap(stream);
        }
        catch { return null; }
    }

    private static IBrush? HexBrush(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) { return null; }
        try
        {
            var clean = hex.TrimStart('#');
            if (clean.Length == 6) { clean = "FF" + clean; }
            if (clean.Length != 8) { return null; }
            var argb = Convert.ToUInt32(clean, 16);
            return new SolidColorBrush(Color.FromUInt32(argb));
        }
        catch { return null; }
    }
}

/// <summary>
/// Carries a click position in theme-local (canvas) coordinates, i.e.
/// the same coordinate space the theme.json's <c>x</c>/<c>y</c> fields
/// use. The host typically funnels this into
/// <see cref="Autofire.App.ViewModels.ThemeHitTester"/> to resolve it
/// to a logical button id.
/// </summary>
public sealed class ThemeClickEventArgs(double x, double y) : EventArgs
{
    /// <summary>Theme-local X coordinate of the click.</summary>
    public double X { get; } = x;

    /// <summary>Theme-local Y coordinate of the click.</summary>
    public double Y { get; } = y;
}
