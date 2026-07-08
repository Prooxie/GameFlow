using GameFlow.Core.Enums;
using GameFlow.Infrastructure.Configuration;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Serilog;

namespace GameFlow.App.Services;

/// <summary>
/// Resolves the optional controller-overlay image asset for a given
/// <see cref="ControllerVisualStyle"/> and turns it into an Avalonia
/// <see cref="Bitmap"/> ready for binding to <c>Image.Source</c>.
///
/// <para>
/// The loader tries multiple sources in order; the first hit wins:
/// </para>
/// <list type="number">
/// <item><b>User folder</b>:
/// <c>{LocalAppData}/GAMEFLOW/controller-overlays/</c> (or whatever
/// <see cref="AppPaths.ControllerOverlaysDirectory"/> resolves to).
/// This is what the user fills in by dropping files from the
/// AL2009man Gamepad-Asset-Pack — see <c>docs/asset-pack.md</c> for
/// the step-by-step.</item>
/// <item><b>Bundled avares://</b> resource embedded in the app at
/// build time (currently empty by design — keeps the bundle small,
/// users opt in by dropping their own assets).</item>
/// </list>
///
/// <para>
/// Each style maps to a list of acceptable filename stems
/// (e.g. <c>"playstation5"</c>, <c>"ps5"</c>, <c>"dualsense"</c>). For
/// each stem the loader probes <c>.png</c>, <c>.jpg</c>, and
/// <c>.jpeg</c> in turn. SVG files are not loaded directly here
/// (Avalonia's built-in <see cref="Bitmap"/> expects rasters); users
/// with the SVG-only variants from the asset pack should export a
/// PNG with Inkscape.
/// </para>
///
/// <para>
/// When no file is found at any source the loader returns
/// <see langword="null"/>. The view-model treats null as "no overlay
/// available" and the AXAML falls back to the existing programmatic
/// silhouette.
/// </para>
///
/// <para>
/// Asset attribution: the recommended source pack is
/// <see href="https://github.com/AL2009man/Gamepad-Asset-Pack">AL2009man's
/// Gamepad-Asset-Pack</see>, MIT licensed.
/// </para>
/// </summary>
public static class ControllerOverlayAssetLoader
{
    /// <summary>
    /// Returns the bitmap for <paramref name="style"/> if a matching
    /// file exists in any known source, otherwise <see langword="null"/>.
    /// </summary>
    public static Bitmap? TryLoad(ControllerVisualStyle style)
    {
        var stems = ResolveFileStems(style);
        if (stems.Count == 0)
        {
            return null;
        }

        // ─── Source 1: user-writable controller-overlays folder ────────
        var userDir = AppPaths.ControllerOverlaysDirectory;
        foreach (var stem in stems)
        {
            foreach (var ext in CandidateExtensions)
            {
                var path = Path.Combine(userDir, $"{stem}{ext}");
                if (!File.Exists(path))
                {
                    continue;
                }

                try
                {
                    var bitmap = new Bitmap(path);
                    Log.Debug(
                        "Loaded controller overlay for {Style} from user folder: {Path} ({W}x{H}).",
                        style, path, bitmap.PixelSize.Width, bitmap.PixelSize.Height);
                    return bitmap;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex,
                        "Could not load user-supplied overlay {Path} for {Style}; trying next.",
                        path, style);
                }
            }
        }

        // ─── Source 2: bundled avares:// resource (fallback) ──────────
        foreach (var stem in stems)
        {
            var uri = new Uri($"avares://GameFlow.App/Assets/ControllerOverlays/{stem}.png");
            try
            {
                using var stream = AssetLoader.Open(uri);
                var bitmap = new Bitmap(stream);
                Log.Debug(
                    "Loaded controller overlay for {Style} from bundled resource: {Uri} ({W}x{H}).",
                    style, uri, bitmap.PixelSize.Width, bitmap.PixelSize.Height);
                return bitmap;
            }
            catch (FileNotFoundException)
            {
                // Expected on a fresh install — try the next stem.
            }
            catch (Exception ex)
            {
                Log.Warning(ex,
                    "Could not load bundled overlay resource {Uri} for {Style}; trying next.",
                    uri, style);
            }
        }

        // Nothing matched. The VM's HasOverlayImage binding goes false,
        // and the AXAML's programmatic-art layer (ShowProgrammaticArt)
        // takes over. Logging at Information so users searching their
        // logs for "overlay" can find a clear data point.
        Log.Information(
            "No controller overlay asset found for {Style}. Looked in user folder {UserDir} and bundled resources. Falling back to programmatic art.",
            style, userDir);
        return null;
    }

    /// <summary>
    /// Returns the absolute path of the user-writable controller-overlays
    /// folder, creating it on first call. Surfaced here so the UI can
    /// point users at the right folder.
    /// </summary>
    public static string GetUserOverlayDirectory() => AppPaths.ControllerOverlaysDirectory;

    /// <summary>
    /// File extensions probed (in order) for each filename stem. PNG
    /// first because that's the format the asset pack ships in.
    /// </summary>
    private static readonly string[] CandidateExtensions = [".png", ".jpg", ".jpeg"];

    /// <summary>
    /// Maps a <see cref="ControllerVisualStyle"/> to the list of
    /// filename stems to probe (without extension). Listed in
    /// preference order — first match wins. Aliases cover common
    /// naming conventions across asset packs (VSCView filenames,
    /// marketing names, short tags).
    /// </summary>
    private static IReadOnlyList<string> ResolveFileStems(ControllerVisualStyle style) => style switch
    {
        ControllerVisualStyle.Xbox =>
            ["xbox", "xboxseries", "xseries", "xboxone", "xbox360"],
        ControllerVisualStyle.PlayStation4 =>
            ["playstation4", "ps4", "dualshock4", "dualshock"],
        ControllerVisualStyle.PlayStation5 =>
            ["playstation5", "ps5", "dualsense"],
        ControllerVisualStyle.PlayStation3 =>
            ["playstation3", "ps3", "dualshock3"],
        _ => [],
    };
}
