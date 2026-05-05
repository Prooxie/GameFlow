# Autofire Next — patch bundle integration guide

This bundle is **a patch set**, not a full repo. Every file here is meant to drop into the same path inside your existing `Autofire-Next` checkout.

> ⚠️ I worked from your test logs and feature list — I did **not** have access to your repo. Treat every file as a starting point: if your existing class has fields or methods I didn't anticipate, you'll need to merge by hand. Compile errors on first build are normal and expected.

---

## 1. What's in the bundle

```
.github/workflows/ci.yml            ← new — runs build+test on every push/PR
README.md                           ← replace existing (or merge if you have custom sections)
src/
  Autofire.App/
    App.axaml                       ← replace
    App.axaml.cs                    ← replace (fixes shutdown crash, item #10)
    Assets/Styles/AppTheme.axaml    ← replace (DynamicResource for theme switching)
    Services/AppThemeService.cs     ← new
    ViewModels/AppThemeOption.cs    ← new
    ViewModels/ShellViewModel.cs    ← merge (theme support, 33 ms timer, item #1, #2, #3)
    Views/ControllerSurface.axaml   ← replace (4 silhouettes, no arrows, item #5)
    Views/MappingEditorView.axaml   ← replace (centered Save rule, item #4, Lua editor)
    Views/ShellWindow.axaml         ← merge (theme combo, refresh button removed)
    Views/ShellWindow.axaml.cs      ← merge
  Autofire.Core/
    Models/Rules/ButtonComboRule.cs ← new (joy2key combo rule)
    Pipeline/ButtonComboExecutor.cs ← new (combo runtime executor)
    Scripting/LuaScriptEngine.cs    ← new (MoonSharp-based Lua sandbox, item #14)
  Autofire.Infrastructure/
    Localization/Resources/en.json  ← merge (added ProviderStatus_* keys, item #2)
    Localization/Resources/cs.json  ← merge (added Czech translations)
    Runtime/InputDeviceCatalog.cs   ← replace (locale-aware status strings, item #2)
    Runtime/DefaultOutputSinkFactory.cs                 ← merge (DS5 routing)
    Runtime/Providers/Sdl3InputProvider.cs              ← replace (stable, item #6)
    Runtime/Providers/LinuxEvdevInputProvider.cs        ← new (item #8)
    Runtime/Providers/MacOsCoreHidInputProvider.cs      ← new (item #9)
    Runtime/ViGEm/ViGEmDualSenseOutputSink.cs           ← new (item #11)
tests/
  Autofire.Core.Tests/
    StickPulseSchedulerTests.cs           ← replace (test fix, was failing on CI)
    ControllerMappingPipelineTests.cs     ← replace (test fix, was failing on CI)
```

---

## 2. Pre-flight: your existing types I'm assuming exist

I worked blind — these types are referenced but **not** included in the patch because you already have them in your repo:

- `ButtonId` enum (with `South`, `East`, `West`, `North`, `Back`, `Start`, `Guide`, `LeftStick`, `RightStick`, `LeftShoulder`, `RightShoulder`, `DpadUp/Down/Left/Right`, `Touchpad`, `None`)
- `StickId` enum
- `StickVector` struct with `Zero` static + `X`, `Y` fields
- `ButtonState` static helper exposing `CreateEmptyMap()` and `Clone(ButtonState[])`
- `ControllerSnapshot` record with `Buttons`, `LeftStick`, `RightStick`, `LeftTrigger`, `RightTrigger`, `Empty`, and `IsPressed(ButtonId)`
- `MappingRule` base, `ScriptRule` derived
- `ProfileDocument` / `UiOptions` (you may need to add a `Theme` field — see step 4)
- `ILocalizationService` with `string this[string key]` indexer + `event CultureChanged`
- `IInputProvider`, `IOutputSink`, `IOutputSinkFactory`
- The various existing `*Sink` and `*Provider` classes I extend

If the bundle's Lua engine, combo executor, etc. fail to compile because one of these doesn't match my assumption, the fix is mechanical (rename the field/method).

---

## 3. NuGet packages you need to add

Add these to the matching `.csproj` files. The minimum set is:

**`src/Autofire.Core/Autofire.Core.csproj`** — for the Lua engine:
```xml
<PackageReference Include="MoonSharp" Version="2.0.0" />
```

**`src/Autofire.Infrastructure/Autofire.Infrastructure.csproj`** — for SDL3:
```xml
<PackageReference Include="SDL3-CS" Version="3.2.*" />
```
*(If you're already using a different SDL3 wrapper, leave that and adapt the namespace `using SDL;` at the top of `Sdl3InputProvider.cs` to match.)*

ViGEm.Client should already be present since you have the DS4 sink — the new DS5 sink uses the same package.

```bash
cd /path/to/Autofire-Next
dotnet add src/Autofire.Core/Autofire.Core.csproj package MoonSharp
dotnet add src/Autofire.Infrastructure/Autofire.Infrastructure.csproj package SDL3-CS
```

---

## 4. ProfileDocument schema additions

If your `UiOptions` (or whatever holds UI prefs in `ProfileDocument`) doesn't already have a `Theme` field, add one:

```csharp
public sealed record UiOptions
{
    // ...your existing fields...
    public string Theme { get; init; } = "CyberBlue";   // <-- add this
}
```

The `ShellViewModel` will read/write this whenever the user changes the theme combobox.

---

## 5. DI registration

In your `HostBuilderFactory` (or wherever `IServiceCollection` is configured), register the new services:

```csharp
services.AddSingleton<AppThemeService>();
services.AddSingleton<LuaScriptEngine>();

// Input providers — register the new ones alongside your existing XInput / Demo:
services.AddSingleton<Sdl3InputProvider>();
if (OperatingSystem.IsLinux())
    services.AddSingleton<LinuxEvdevInputProvider>();
if (OperatingSystem.IsMacOS())
    services.AddSingleton<MacOsCoreHidInputProvider>();

// Output sinks — register the new DS5 sink:
services.AddSingleton<ViGEmDualSenseOutputSink>();
```

---

## 6. Run the tests locally first

Before you push, verify the test fixes work locally:

```bash
cd /path/to/Autofire-Next
dotnet test Autofire.Next.sln --configuration Release
```

The two previously failing tests should now pass. If they don't, the most likely cause is that the test names or member names I assumed don't match yours — open both files in `tests/Autofire.Core.Tests/` and compare with your existing pre-patch versions.

---

## 7. Push to GitHub & cut a release

Once everything compiles and tests pass:

```bash
cd /path/to/Autofire-Next

# Stage every change
git add -A
git status                               # eyeball it first

# Commit
git commit -m "feat: themes, locale fixes, DS5 output, Lua scripting, PS3 silhouette, button combos, CI workflow"

# Push to your default branch
git push origin main                     # or master/develop

# Cut a release — your existing .github/workflows/release.yml fires on tag
git tag -a v1.0.0 -m "v1.0.0 — themes, Lua, DS5, PS3, combos"
git push origin v1.0.0
```

Watch the workflow: **GitHub → Actions → release** (your existing one) **and ci** (new). When both go green, the release with the four self-contained binaries appears at **GitHub → Releases**.

---

## 8. Things I had to defer / be honest about

- **macOS CoreHID input reading** — the provider initialises and prompts for permission, but reading per-element state requires a Swift bridge dylib for the input-value callback typedef. The current implementation returns `Empty` snapshots. Functional CoreHID is a follow-up.
- **Linux uinput *output*** — the new file is the *input* path (reading `/dev/input/event*`). Virtual uinput output (i.e. emitting a virtual gamepad on Linux) is on the roadmap but not in this bundle.
- **DS5 output** — I emit through the ViGEm DualShock4 transport with DS5 product strings, because Nefarius's ViGEm Bus does not yet ship a native DS5 target type. Most games treat this correctly, but games that strictly check the HID report descriptor for DS5 will still see DS4. When a native DS5 type ships, it's a one-line change in `ViGEmDualSenseOutputSink.cs`.
- **Microsoft.GameInput** — left disabled per your request (#7). To opt in, set `Runtime:EnableExperimentalGameInput=true` in `appsettings.json`.

---

## 9. What I cannot do

- **Push to GitHub on your behalf.** I have no repo URL, no credentials, and `git push` from my sandbox would not be safe even if I had them. The commands in section 7 are the path you take.
- **Verify the patch actually compiles.** I do not have your repo to build against. Expect to fix some namespace / member-name mismatches on the first compile pass — the patch is structurally correct but I worked from inference.

If the first compile turns up errors, paste them and I'll fix them up.
