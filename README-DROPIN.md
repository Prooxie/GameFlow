# Autofire-Next build fix — drop-in bundle (no patch tool needed)

The `git apply` errors you've been hitting are line-ending related: my patches
were generated on Linux (LF), your tree is Windows (CRLF). Mixing them with
git is fragile.

This bundle skips git entirely. **Just copy the files over your tree.** They
already have Windows line endings.

## How to apply

1. **Reset your working tree to a clean baseline first.** From your repo root
   `d:\Development\C#\AutofireNext\Autofire-Next`:

   ```
   git status
   git stash
   git checkout dev
   git pull
   ```

   You said you've been doing `git apply -R` already — that should have left
   you at baseline, but verify with `git status` that there are no remaining
   local edits before continuing. (One edit you DO want to keep: the
   `ButtonId.None` value you added to the enum yourself. That's not in this
   bundle. If `git stash` hides it, do `git stash pop` afterwards.)

2. **Copy this bundle's files over your tree.** From the unzipped bundle's
   root folder, copy everything *into* your repo root, overwriting in place.
   In Windows Explorer: select all files/folders inside this bundle, drag
   into your repo root, choose "Replace files in destination".

   In a terminal from this folder:

   ```
   xcopy /E /Y * d:\Development\C#\AutofireNext\Autofire-Next\
   ```

3. **Delete two duplicate files manually** (they are not in this bundle, but
   they should be removed from your tree — they're leftover duplicates of
   files in `src/Autofire.App/Views/`):

   ```
   del d:\Development\C#\AutofireNext\Autofire-Next\src\ControllerSurface.axaml
   del d:\Development\C#\AutofireNext\Autofire-Next\src\MappingEditorView.axaml
   ```

4. **Build.**

   ```
   dotnet restore
   dotnet build --configuration Release
   dotnet test --no-build --configuration Release
   ```

## What's in this bundle

Files that were modified across the three build-fix rounds:

| File | Round | Why |
|------|-------|-----|
| `Directory.Build.props` | 1 | `GenerateDocumentationFile=true`, deterministic builds, Source Link, assembly metadata |
| `Directory.Packages.props` | 1 | Added `MoonSharp 2.0.0` and `Microsoft.Extensions.Logging.Abstractions 10.0.0` |
| `NuGet.config` | 1 (new) | `<clear/>`s inherited package sources for reproducible restore |
| `global.json` | 1 (new) | Pins SDK to 10.0.100 with `latestFeature` roll-forward |
| `.github/workflows/ci.yml` | 1 | NuGet caching, scoped permissions, concurrency cancel, dev branch |
| `src/Autofire.Core/Autofire.Core.csproj` | 1 | Added `MoonSharp` and `Microsoft.Extensions.Logging.Abstractions` package references |
| `src/Autofire.Infrastructure/Autofire.Infrastructure.csproj` | 1 | Excluded the three `Runtime/Providers/*` files that reference a non-existent `IInputProvider` interface |
| `src/Autofire.Core/Pipeline/ButtonComboExecutor.cs` | 2 | `ButtonState[]` → `bool[]` (CS0719: array elements can't be a static type) |
| `src/Autofire.Core/Scripting/LuaScriptEngine.cs` | 2 | `ScriptRule` → `ControlScriptRule` (CS0246), `ButtonState[]` → `bool[]` (CS0719), fixed `<50 ms` in XML doc (CS1570) |
| `src/Autofire.Infrastructure/Runtime/InputDeviceCatalog.cs` | 3 | Migrated from `DetectedInputDevice` to `InputDeviceInfo`; added the three methods (`ReplaceDevices`, `SetProviderStatus`, `SetIgnoredDeviceIds`) every input source already calls |

Also included for reference: `BUILD_FIX_NOTES.md`.

## If `dotnet build` still fails after this

Paste the new error output. Each round so far has uncovered the next layer of
issues that were hidden behind the previous one. We're getting close — Core
already builds for you, so once Infrastructure builds the App layer is the
last thing left.
