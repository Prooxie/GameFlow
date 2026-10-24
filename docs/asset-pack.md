# Controller artwork

Bundled controller themes live in `themes`.
The application copies them into its data folder on startup.
Content hashes detect updates. Staged replacement and backups protect earlier files.

Each theme needs a `theme.json` manifest with working image references.
The theme compiler tests every bundled manifest.
An image alone does not supply control geometry or add a hardware protocol.

The input prompt library lives in `src/GameFlow.App/Assets/InputAssets`.
Its manifest records 216 prompt vectors and 27 icon vectors from the supplied source packs.
The application embeds the originals and PNG conversions.
Shared positional prompts identify mapping-picker buttons.
Controller family icons identify game controllers on the Devices page.

To regenerate the imported library:

1. Keep the supplied `Controllers` directory beside the application repository.
2. Install or select a `sharp` module for the import tool.
3. Run the importer with explicit source and module paths.
4. Run the UI resource and theme tests.

```powershell
node scripts/Import-InputAssets.mjs ../Controllers /path/to/node_modules/sharp
dotnet test --solution GameFlow.sln -c Release
```

The [inventory](asset-audit.json) also compares supplied controller PNG files with bundled theme files.
It does not claim that every upstream template has a selectable theme.
Upstream source images that no shipped manifest references are not runtime dependencies.

The optional legacy overlay loader accepts PNG, JPG, and JPEG files in the data folder's `controller-overlays` directory.
Its supported filename aliases are documented in `ControllerOverlayAssetLoader.cs`.
Normal controller surfaces use complete themes instead of this fallback.

Keep the [artwork license](../licenses/AL2009man-LICENSE.txt) when you redistribute these resources.
