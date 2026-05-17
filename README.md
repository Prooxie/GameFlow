<div align="center">

# ⊕ Autofire Next

**Cross-platform gamepad tooling for speedrunners and power users.**
Autofire, remapping, stick shaping, freeze macros, and virtual controller output — all in one clean UI.

Built with **.NET 10** · **Avalonia UI** · **ViGEm**

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20%7C%20macOS-informational)](https://github.com/Prooxie/Autofire-Next)
[![Made by Proxy Darkness](https://img.shields.io/badge/made%20by-Proxy%20Darkness-blueviolet)](https://buymeacoffee.com/ProxyDarkness)

---

[![Buy Me a Coffee](https://img.buymeacoffee.com/button-api/?text=Buy%20me%20a%20coffee&emoji=&slug=ProxyDarkness&button_colour=BD5FFF&font_colour=ffffff&font_family=Cookie&outline_colour=000000&coffee_colour=FFDD00)](https://buymeacoffee.com/ProxyDarkness)
*No pressure — using and sharing the project already helps a lot. Thank you!*

</div>

---

## What is Autofire Next?

Autofire Next is a desktop application that sits between your physical gamepad and any game or application.
It reads your physical controller, transforms its inputs according to a saved **profile**, and writes the result to a **virtual controller** that the game sees.

This project started first as a script to save players from doing repetitive tasks that would damage both their controllers and their hands.
The script wasn't a great alternative and barely worked at all.

Later that became a Python application that tried to address pain points from Joy2Key, DSX/DS4Windows and Gamepad Viewer.
Python relied heavily on libraries and was painful to maintain — but it served as the architecture prototype for the C# application you see now.

Transformations include:

- **Autofire / rapid fire** — pulse any button or stick at a configurable rate
- **Button remapping** — re-route any button to any other button
- **Stick shaping** — deadzone, full-at threshold, and blend-mode control
- **Freeze last direction** — latch the stick vector at the moment a button is pressed
- **Profile system** — create, duplicate, rename, import, and export JSON profiles
- **Themed controller surfaces** — VSCView-compatible skins for live visual feedback on both input and output
- **Click-to-map** — pick any button, trigger, or analog stick by clicking the rendered controller
- **Live diagnostics** — real-time frame-age, input snapshot, and provider status at 60 Hz

---

## Features at a Glance

| Category | Details |
|---|---|
| **Autofire** | Button and stick autofire with configurable hold / release timing and jitter-resistant hysteresis |
| **Remapping** | Button-to-button remap with optional source suppression |
| **Stick shaping** | Per-stick deadzone and full-at threshold, applied directly to virtual output |
| **Freeze macro** | Captures stick vector on rising edge of activation button; optional pulse-while-frozen mode |
| **Profiles** | JSON-based, per-profile polling rate, input/output provider, controller style and rename support |
| **Input providers** | XInput (Windows), SDL3 unified (all platforms), Demo preview, None |
| **Output providers** | ViGEm Xbox 360, ViGEm DualShock 4, Preview (no virtual device) |
| **Planned output** | Linux uinput, macOS CoreHID |
| **Controller surfaces** | Side-by-side physical and virtual panels, both with live feedback driven by their respective snapshots |
| **Skin variants** | 18 shipped skins across DualSense, DualShock 4 V1 / V2, Xbox Series X, Xbox 360 — switchable per-panel without leaving the dashboard |
| **Click-to-map** | Hover for an amber outline on any mappable element, click to lock the selection and open the mapping editor |
| **Translations** | English, Czech, German, Spanish, French, Italian, Polish, Russian |

---

## Requirements

### All platforms

| Requirement | Version |
|---|---|
| [.NET SDK](https://dotnet.microsoft.com/download) | 10.0 or later |
| Git | Any recent version |

### Windows — virtual controller output

To create a virtual Xbox 360 or DualShock 4 controller, the **ViGEm Bus driver** must be installed:

> **[Download ViGEm Bus → vigembusdriver.com](https://vigembusdriver.com/)**

Without it the application starts normally but the output provider falls back to **Preview** mode (no virtual device is created; the transformed state is shown in the dashboard only).

### Linux — input

SDL3 unified input works out of the box on most distributions.
For joystick / gamepad access without `sudo`, add your user to the `input` group:

```bash
sudo usermod -aG input $USER
# then log out and back in
```

The Linux virtual output provider (`uinput`) is planned. Place SDL3's shared library (`libSDL3.so`) in a system library path or next to the application binary.

### macOS

Place `libSDL3.dylib` in the application directory or a system library path (`/usr/local/lib`).
The macOS virtual output provider (`CoreHID`) is planned.

### SDL3 native library

SDL3 unified input requires the SDL3 native library for your platform:

| Platform | File |
|---|---|
| Windows | `SDL3.dll` (place next to `Autofire.App.exe`) |
| Linux | `libSDL3.so` (system path or app directory) |
| macOS | `libSDL3.dylib` (system path or app directory) |

Download from [libsdl.org](https://libsdl.org/) or build from source.
An optional `gamecontrollerdb.txt` ([SDL_GameControllerDB](https://github.com/mdqinc/SDL_GameControllerDB)) can be placed in the application directory to extend the built-in mapping database.

---

## Quick Start

### 1. Clone

```bash
git clone https://github.com/Prooxie/Autofire-Next.git
cd Autofire-Next
```

### 2. Build

```bash
dotnet build
```

### 3. Run

```bash
dotnet run --project src/Autofire.App
```

On first launch the application creates a default profile under:

| Platform | Location |
|---|---|
| Windows | `%LOCALAPPDATA%\AutofireNext\` |
| Linux | `~/.local/share/AutofireNext/` |
| macOS | `~/Library/Application Support/AutofireNext/` |

The bundled controller skins (DualSense, DualShock 4, Xbox Series X, Xbox 360) are mirrored from the build output into the `themes/` subfolder of that location on first run, so the variant picker is populated before you even pick a profile.

---

## Configuration

### appsettings.json

`src/Autofire.App/appsettings.json` controls runtime behaviour:

```json
{
  "Runtime": {
    "EnableViGEm": true,
    "EnableExperimentalGameInput": false,
    "DashboardRefreshHz": 60
  }
}
```

| Key | Default | Description |
|---|---|---|
| `EnableViGEm` | `true` | Enable ViGEm virtual controller output on Windows |
| `EnableExperimentalGameInput` | `false` | Enable Microsoft GameInput provider (Windows, experimental, out-of-process isolation required) |
| `DashboardRefreshHz` | `60` | UI refresh rate for the live dashboard and diagnostics panel |

### Profiles

Profiles are JSON files stored in the application data directory.
An example is included at:

```
samples/SpeedrunnerDefault.profile.json
```

Each profile defines:

- Input and output provider selection
- Per-controller polling rate (30 – 1000 Hz)
- All mapping rules (autofire, remap, threshold, freeze)
- Controller surface display style
- Preferred input device ID
- Preferred skin variant per controller style
- Panel background colour (incl. transparent / chroma-key presets)

Profiles can be created, duplicated, renamed, imported, and exported from the **Profiles** tab without touching JSON directly.

---

## Controller Surfaces & Themes

The dashboard shows two controller renders side-by-side:

- **Physical input** — driven by your live hardware (XInput / SDL3 / GameInput snapshot)
- **Virtual output** — driven by the post-transform state that the virtual device emits

Both panels render the same VSCView-compatible theme format and animate from their own snapshots, so you can see, for example, that "L1 on the physical controller" is becoming "R2 on the virtual output" in real time. When emulation is disabled the dashboard collapses to a single physical panel.

### Bundled skins

18 variants ship with the app:

| Style | Variants |
|---|---|
| **DualSense** | Cosmic Red · Galactic Purple · Midnight Black · Nova Pink · Starlight Blue · White |
| **DualShock 4 V1** | Black |
| **DualShock 4 V2** | Glacier White · Gold · Jet Black · Magma Red · Midnight Blue |
| **Xbox Series X** | Black · Blue · Red · DMC5 |
| **Xbox 360** | Black · White |

Each panel has a **Skin** picker independent of the other, and your choices persist per controller style in the active profile.

### Click-to-map

Hover the cursor over any mappable element on either panel:

- A 2 px amber outline traces the element's hit area, and the cursor changes to a Hand
- Clicking locks the selection (3 px outline + translucent fill) and opens the **Control Mapping** editor for that button / trigger / stick
- Clicking the inner ~30 % of an analog stick selects the **stick click** (L3 / R3); clicking the outer ring selects the **analog axes** (per-stick deadzone, threshold, autofire, freeze)

The element id resolves to the same logical name the profile editor uses (`South`, `LeftShoulder`, `DpadUp`, `LeftStick`, `LeftStick.Button`, `LeftTrigger.Button`, `Touchpad`, …), so anything you map via click is identical to anything you map via the regular **Profiles → Add rule** flow.

### Custom themes

Skins live in:

| Platform | Location |
|---|---|
| Windows | `%LOCALAPPDATA%\AutofireNext\themes\<style>\<variant>\theme.json` |
| Linux | `~/.local/share/AutofireNext/themes/<style>/<variant>/theme.json` |
| macOS | `~/Library/Application Support/AutofireNext/themes/<style>/<variant>/theme.json` |

The schema is VSCView-compatible (`image`, `showhide`, `pbar`, `slider`, `group` nodes). The included [`themes/_generate.py`](themes/_generate.py) script regenerates the bundled `theme.json` files from a single source-of-truth Python definition — handy when you want to add a new variant for an existing controller. Drop a folder containing a `theme.json` plus its referenced PNGs into the `themes/` directory and it'll appear in the Skin picker on the next launch.

### Panel background

A presets dropdown lets you switch the panel background between dark, pure black, transparent, and two common chroma-key tones (green `#00B140`, blue `#0047BB`) — useful for OBS scene compositing.

---

## Input Providers

| ID | Platform | Status | Notes |
|---|---|---|---|
| `sdl` | All | Stable | SDL3 unified; supports mapped gamepads and joystick fallback. Recommended default. |
| `xinput` | Windows | Stable | Native Windows XInput. Up to 4 simultaneous Xbox-compatible controllers. |
| `demo` | All | Stable | Animated preview source for UI testing. No hardware required. |
| `none` | All | Stable | Disables live input. Dashboard stays idle. |
| `gameinput` | Windows | Experimental | Microsoft GameInput. Disabled by default. Requires out-of-process interop isolation. |

---

## Output Providers

| ID | Platform | Status | Notes |
|---|---|---|---|
| `vigem-xbox360` | Windows | Stable | Virtual Xbox 360 controller via ViGEm Bus. |
| `vigem-ds4` | Windows | Stable | Virtual DualShock 4 controller via ViGEm Bus. |
| `preview` | All | Stable | No virtual device. Shows transformed state in the dashboard only. |
| `uinput` | Linux | Planned | Kernel uinput virtual device. Implementation in progress. |
| `corehid` | macOS | Planned | IOHIDUserDevice virtual device. Implementation in progress. |

---

## Project Structure

```
Autofire-Next/
├── src/
│   ├── Autofire.App              # Avalonia UI, ViewModels, Views, Localization
│   ├── Autofire.Core             # Domain models, pipeline, rule types, schedulers
│   └── Autofire.Infrastructure   # SDL3, XInput, ViGEm, theme engine, profile persistence, runtime
├── themes/                       # VSCView-compatible skin manifests + assets
├── samples/                      # Example profile JSON files
├── docs/                         # Architecture notes, GameInput integration guide
└── scripts/                      # Publish and packaging scripts
```

---

## Architecture Overview

```
Physical controller
        │  (SDL3 / XInput / GameInput)
        ▼
 InputSource.ReadAsync()
        │
        ▼
 ControllerMappingPipeline
   ├── StickThresholdRule   (deadzone / full-at)
   ├── ButtonRemapRule      (source → target)
   ├── ButtonAutofireRule   (binary pulse scheduler)
   ├── StickAutofireRule    (stick pulse scheduler + hysteresis)
   └── FreezeLastDirectionRule  (rising-edge capture + optional pulse)
        │
        ▼
 OutputSink.WriteAsync()
        │  (ViGEm Xbox360 / DS4 / Preview / uinput / CoreHID)
        ▼
  Virtual controller seen by games
```

All rules are stored in the profile JSON and applied in order every polling tick.
The UI reads from a lock-free `RuntimeSnapshotStore` on a 16 ms timer (≈ 60 Hz) without blocking the pipeline.

---

## Localization

The UI is fully translated into:

🇬🇧 English · 🇨🇿 Czech · 🇩🇪 German · 🇪🇸 Spanish · 🇫🇷 French · 🇮🇹 Italian · 🇵🇱 Polish · 🇷🇺 Russian

Language can be changed live from the header bar without restarting the application.

---

## Contributing

Contributions are welcome.

1. Fork the repository
2. Create a branch: `git checkout -b feature/my-feature`
3. Commit your changes
4. Open a Pull Request

Please keep commits focused and include a short description of what changed and why.

---

## Documentation

Additional technical documentation is available in the `docs/` directory:

- `docs/MicrosoftGameInput.md` — GameInput provider integration notes
- `docs/GameInputSmokeTest.md` — Manual smoke-test procedure for GameInput

---

## Acknowledgements

Autofire Next stands on the shoulders of a lot of excellent open-source work.

### Libraries and frameworks

| Project | Author / maintainer | License | What it provides |
|---|---|---|---|
| [Avalonia UI](https://avaloniaui.net/) | AvaloniaUI and contributors | MIT | The cross-platform UI framework powering every window, view, and themed surface |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | .NET Foundation / Microsoft | MIT | `RelayCommand`, `AsyncRelayCommand`, `ObservableObject` and the rest of the MVVM helpers used throughout the view-models |
| [Serilog](https://serilog.net/) | Nicholas Blumhardt and contributors | Apache 2.0 | Structured logging across the runtime, infrastructure, and UI layers |
| [Microsoft.Extensions.*](https://github.com/dotnet/runtime) | Microsoft / .NET Foundation | MIT | Hosting, dependency injection, logging abstractions, options, configuration |
| [ViGEm Bus driver](https://github.com/nefarius/ViGEmBus) | Benjamin "Nefarius" Höglinger-Stelzer | BSD-3-Clause | The Windows kernel driver that materialises virtual Xbox 360 / DualShock 4 controllers |
| [ViGEm.NET (Nefarius.ViGEm.Client)](https://github.com/nefarius/ViGEm.NET) | Benjamin "Nefarius" Höglinger-Stelzer | MIT | The managed C# client we link against to talk to the ViGEm bus |
| [SDL3](https://libsdl.org/) | Sam Lantinga and contributors | Zlib | The native cross-platform input library powering the `sdl` provider |
| [SDL_GameControllerDB](https://github.com/mdqinc/SDL_GameControllerDB) | Maintained by mdqinc and contributors | Zlib | Optional extra controller mapping definitions read at runtime |
| [Skia / SkiaSharp](https://github.com/mono/SkiaSharp) | Microsoft / Google (via Avalonia) | MIT / BSD | The 2D drawing engine Avalonia uses to rasterise the themed controller surfaces |

### Theme format & assets

The themed controller surfaces (skin variants, click-to-map hit testing, live overlay rendering) implement a subset of the [**VSCView**](https://github.com/Nielk1/VSCView) theme.json schema, originally created by **John "Nielk1" Andersen**. Many of the bundled PNG assets for the DualSense, DualShock 4, Xbox Series X, and Xbox 360 skins originate from the VSCView project and its template pack. We're grateful for the format and the asset work — they made it possible to render real-looking controller silhouettes without redrawing everything from scratch. If you're redistributing or modifying the bundled themes, please consult VSCView's license and credit accordingly.

### Tooling

| Tool | Purpose |
|---|---|
| [GitHub Actions](https://github.com/features/actions) | CI matrix across Windows / Linux / macOS, plus tagged-release publish to GitHub Releases |
| [softprops/action-gh-release](https://github.com/softprops/action-gh-release) | Release-creation action used in the publish pipeline |
| [.NET 10 SDK](https://dotnet.microsoft.com/) | Compiler, runtime, MSBuild, NuGet — all the way down |

If you spot anything missing from this list — especially a transitive component whose license should be acknowledged — please open an issue and we'll add it.

---

## License

This project is licensed under the **[MIT License](LICENSE)**.
Third-party components retain their own licenses; see the **Acknowledgements** section above and the `NOTICE` / `THIRD-PARTY-NOTICES.md` file when present.

---

## Credits

Checkout my other projects on GitHub, [YouTube](https://www.youtube.com/@ProxyDarkness) and [Twitch](https://www.twitch.tv/ProxyDarkness).

---

## Thanks to

**Nazzareno96** — keeping me sane during development, beta testing
[twitch.tv/nazzareno96](https://www.twitch.tv/nazzareno96)

**NoobKillerRoof** — voicing real hardware/software pain points that inspired this project, beta testing
[twitch.tv/noobkillerroof](https://www.twitch.tv/noobkillerroof)
