# Round 1 — Build fix notes

This commit unbreaks `dotnet build` on a clean checkout. It does **not** add
any features yet — those are in subsequent rounds (prerequisites checker,
update prompt, settings menu, asset pack swap, doc-comment pass).

## What was wrong

Three independent reasons the build failed before this commit:

### 1. `Autofire.Core` had no package references at all
`src/Autofire.Core/Scripting/LuaScriptEngine.cs` opens with:

```csharp
using Microsoft.Extensions.Logging;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Loaders;
```

…but `Autofire.Core.csproj` had zero `<PackageReference>` items, and
`Directory.Packages.props` had no `MoonSharp` entry. So the project couldn't
compile.

### 2. Three files reference a non-existent `IInputProvider` interface
The live abstraction in `src/Autofire.Infrastructure/Runtime/IInputSource.cs`
is `IInputSource`. But these three files declare `: IInputProvider`:

* `Runtime/Providers/Sdl3InputProvider.cs`
* `Runtime/Providers/LinuxEvdevInputProvider.cs`
* `Runtime/Providers/MacOsCoreHidInputProvider.cs`

`Sdl3InputProvider.cs` additionally `using SDL;` from the SDL3-CS *managed*
bindings — but only `SDL3-CS.Native` (native binaries) is referenced.

These look like a parallel-track refactor that landed half-merged. They are
now excluded from compilation alongside the Sdl3Interop files that were
already excluded, with a comment explaining what needs to happen to bring
them back.

### 3. Two duplicate `.axaml` files at the `src/` root
`src/ControllerSurface.axaml` and `src/MappingEditorView.axaml` were
duplicates of files already in `src/Autofire.App/Views/`. They were not
referenced by any csproj — just clutter — but they show up in IDE search
results and confuse maintainers. Deleted.

## What I changed

| File | Change |
|------|--------|
| `Directory.Packages.props` | Added `MoonSharp 2.0.0` and `Microsoft.Extensions.Logging.Abstractions 10.0.0`. |
| `Directory.Build.props` | Added `GenerateDocumentationFile=true` (preparing for the Doxygen pass), assembly metadata, `Version` fallback, Source Link on CI. |
| `src/Autofire.Core/Autofire.Core.csproj` | Added the two `<PackageReference>`s it actually needs. |
| `src/Autofire.Infrastructure/Autofire.Infrastructure.csproj` | Extended the existing `<Compile Remove>` block to also exclude the three broken provider files. Added a long comment describing the WIP state. |
| `NuGet.config` *(new)* | `<clear/>` inherited package sources for reproducible restore. |
| `global.json` *(new)* | Pin SDK to `10.0.100` with `latestFeature` roll-forward. |
| `.github/workflows/ci.yml` | NuGet caching keyed on `Directory.Packages.props`, restore lock-mode fallback, scoped per-job permissions, concurrency cancellation, `dev` branch in trigger list, version embed from git tag. |
| `src/ControllerSurface.axaml`, `src/MappingEditorView.axaml` | Deleted (duplicates). |

## How to verify

```bash
git checkout dev
git apply 0001-fix-build-unbreak-dotnet-build-on-clean-checkout.patch
dotnet restore
dotnet build --configuration Release
dotnet test --configuration Release --no-build
```

If you'd rather not delete the duplicate `.axaml` files yet, use the smaller
`0001-fix-build-minimal.patch` instead — same fixes, no deletions.

## What was *intentionally* not done in this round

* The `Runtime/Providers/*.cs` files are still on disk, just excluded.
  Bringing them back is a separate task (it requires either renaming
  `IInputProvider` → `IInputSource` and rewriting the methods, or adding
  the `SDL3-CS` package and renaming the interface, or deleting them).
* No new features yet — see "What's next" below.

## What's next

| Round | Topic |
|-------|-------|
| 2 | Prerequisites checker (ViGEmBus, GameInput redist, .NET runtime) + update checker with Skip / Don't ask / Install dialog |
| 3 | Settings window (log level, resolution, polling Hz, paths, language, theme), plumbed through `ProfileSession` and `appsettings.json` |
| 4 | Replace controller artwork with the AL2009man Gamepad-Asset-Pack |
| 5 | Logging gap-fill + XML documentation comments on all public types |
