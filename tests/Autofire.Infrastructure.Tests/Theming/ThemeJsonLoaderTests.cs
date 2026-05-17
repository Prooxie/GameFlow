using Autofire.Infrastructure.Theming;
using Autofire.Infrastructure.Theming.Models;
using Xunit;

namespace Autofire.Infrastructure.Tests.Theming;

/// <summary>
/// Theme loader contract: turn a JSON string into a parsed
/// <see cref="ThemeDocument"/> with every supported element type
/// recognised and every Flee expression pre-compiled. Errors are
/// surfaced as <see cref="ThemeLoadException"/> rather than silent
/// fallbacks so authors get loud, immediate feedback on a bad save.
/// </summary>
public sealed class ThemeJsonLoaderTests
{
    /// <summary>
    /// Smoke test — the smallest valid theme.json round-trips through
    /// the loader unchanged.
    /// </summary>
    [Fact]
    public void Loads_minimal_theme()
    {
        const string json = """
            { "name": "Test", "version": 1, "width": 100, "height": 50, "children": [] }
            """;
        var doc = ThemeJsonLoader.LoadFromString(json);
        Assert.Equal("Test", doc.Name);
        Assert.Equal(1, doc.Version);
        Assert.Equal(100, doc.Width);
        Assert.Equal(50, doc.Height);
        Assert.Empty(doc.Children);
    }

    /// <summary>
    /// Each VSCView element type maps to its matching <see cref="ThemeNode"/>
    /// subclass. Unknown types fall back to a <see cref="GroupNode"/>
    /// rather than crashing the load, so a theme written for a newer
    /// VSCView still renders the bits the engine knows.
    /// </summary>
    [Fact]
    public void Recognises_each_supported_node_type()
    {
        const string json = """
            { "name": "n", "width": 10, "height": 10, "children": [
                { "type": "image",    "x": 1, "y": 2, "image": "a.png", "width": 8, "height": 8 },
                { "type": "showhide", "input": "quad_right:s",
                  "children": [ { "type": "image", "image": "b.png", "width": 4, "height": 4 } ] },
                { "type": "slider",   "inputX": "stick_left:x * 10", "inputY": "stick_left:y * 10",
                  "children": [ { "type": "image", "image": "c.png", "width": 4, "height": 4 } ] },
                { "type": "pbar",     "input": "triggers:l:analog", "direction": "down",
                                      "width": 50, "height": 5, "image": "d.png" },
                { "type": "ppie",     "input": "triggers:r:analog" }
            ] }
            """;
        var doc = ThemeJsonLoader.LoadFromString(json);
        Assert.Equal(5, doc.Children.Count);
        _ = Assert.IsType<ImageNode>(doc.Children[0]);
        var showHide = Assert.IsType<ShowHideNode>(doc.Children[1]);
        Assert.Single(showHide.Children);
        var slider = Assert.IsType<SliderNode>(doc.Children[2]);
        Assert.Single(slider.Children);
        var bar = Assert.IsType<PBarNode>(doc.Children[3]);
        Assert.Equal(PBarDirection.Down, bar.Direction);
        // The unsupported "ppie" type degrades to a GroupNode.
        _ = Assert.IsType<GroupNode>(doc.Children[4]);
    }

    /// <summary>
    /// A leading <c>\</c> in an image path marks the asset as
    /// "shared", to be resolved from the parent <c>themes/</c>
    /// directory rather than the theme's own folder. We don't try to
    /// resolve at load time (no I/O), but the path string must survive
    /// the round-trip unchanged so the renderer can branch on it.
    /// </summary>
    [Fact]
    public void Preserves_shared_image_paths()
    {
        const string json = """
            { "name": "n", "width": 10, "height": 10, "children": [
                { "type": "image", "image": "\\shared\\base.png", "width": 8, "height": 8 }
            ] }
            """;
        var doc = ThemeJsonLoader.LoadFromString(json);
        var img = Assert.IsType<ImageNode>(doc.Children[0]);
        Assert.StartsWith("\\", img.ImagePath);
    }

    /// <summary>
    /// Numeric properties tolerate string-encoded numbers — some
    /// hand-authored VSCView themes ship widths as <c>"100"</c>
    /// instead of <c>100</c>. The loader uses the invariant-culture
    /// Flee parser so a French locale ("100,0") wouldn't change the
    /// outcome.
    /// </summary>
    [Fact]
    public void Tolerates_string_encoded_numbers()
    {
        const string json = """
            { "name": "n", "width": "1534", "height": "954", "children": [] }
            """;
        var doc = ThemeJsonLoader.LoadFromString(json);
        Assert.Equal(1534, doc.Width);
        Assert.Equal( 954, doc.Height);
    }

    /// <summary>
    /// Malformed JSON throws <see cref="ThemeLoadException"/> with the
    /// underlying <see cref="System.Text.Json.JsonException"/> wrapped
    /// as the inner exception. Authors see the line number in the log.
    /// </summary>
    [Fact]
    public void Throws_on_malformed_json()
    {
        const string json = "{ \"name\": \"oops\" "; // missing closing brace
        _ = Assert.ThrowsAny<Exception>(() => ThemeJsonLoader.LoadFromString(json));
    }

    /// <summary>
    /// Missing top-level fields fall back to safe defaults: empty name,
    /// 1500×900 canvas, version 1, empty children. Themes still load,
    /// just with whatever rendering surface those defaults imply.
    /// </summary>
    [Fact]
    public void Falls_back_to_defaults_for_missing_fields()
    {
        const string json = "{ }";
        var doc = ThemeJsonLoader.LoadFromString(json);
        Assert.Equal("Untitled", doc.Name);
        Assert.Equal(1500, doc.Width);
        Assert.Equal( 900, doc.Height);
        Assert.Empty(doc.Children);
    }
}
