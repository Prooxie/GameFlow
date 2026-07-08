<div align="center">

# ⊕ GameFlow

**Cross-platform gamepad tooling for speedrunners and power users.**
Autofire, remapping, stick shaping, freeze macros, keyboard/mouse-as-gamepad, and virtual controller output — all in one clean UI.

**v1.0.0 Beta** · Built with **.NET 10** · **Avalonia UI** · **SDL3** · **ViGEm** · **HIDMaestro**

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20%7C%20macOS-informational)](#requirements)
[![GitHub](https://img.shields.io/badge/GitHub-Prooxie%2FGameFlow-181717?logo=github)](https://github.com/Prooxie/GameFlow)
[![Made by Proxy Darkness](https://img.shields.io/badge/made%20by-Proxy%20Darkness-blueviolet)](https://buymeacoffee.com/ProxyDarkness)

---

[![Buy Me a Coffee](https://user-images.githubusercontent.com/1286821/181085373-12eee197-187a-4438-90fe-571ac6d68900.png)](https://buymeacoffee.com/ProxyDarkness)
*No pressure — using and sharing the project already helps a lot. Thank you!*

</div>

---

## What is GameFlow?

GameFlow is a desktop application that sits between your physical input (a gamepad, or your keyboard and mouse) and any game or application. It reads your physical input, transforms it according to a saved **profile**, and writes the result to one or more **virtual controllers** that the game sees.

This project started as a script to spare players from repetitive inputs that were damaging both their controllers and their hands — not a great solution, and it barely worked. It was later rebuilt in Python as **Autofire**, addressing gaps left by Joy2Key, DSX/DS4Windows, and Gamepad Viewer, but Python's dependency weight made it painful to maintain. That version served as the architecture prototype for the C#/.NET application you're looking at now, since renamed to GameFlow to avoid confusion between the *product* and *autofire* the individual *feature*.

Transformations include:

- **Autofire / rapid fire** — pulse any button or stick at a configurable rate
- **Button remapping** — re-route any button to any other button
- **Stick shaping** — deadzone, full-at threshold, and blend-mode control
- **Freeze last direction** — latch the stick vector at the moment a button is pressed
- **Keyboard & mouse as gamepad** (Windows) — map WASD, mouse look, and clicks straight into a virtual controller, no physical pad required
- **Multiple virtual controllers** — run several independent input → profile → output pipelines ("slots") at once, each with its own device assignment and output backend
- **Profile system** — create, duplicate, rename, import, and export JSON profiles
- **Live dashboard** — physical and virtual controllers rendered side by side, per slot, so you can see exactly what a profile changed

---

## Features at a Glance

| Category | Details |
|---|---|
| **Autofire** | Button and stick autofire with configurable hold/release timing and jitter-resistant hysteresis |
| **Remapping** | Button-to-button remap with optional source suppression |
| **Stick shaping** | Per-stick deadzone and full-at threshold, applied directly to virtual output |
| **Freeze macro** | Captures the stick vector on the rising edge of an activation button; optional pulse-while-frozen mode |
| **Keyboard & mouse input** | Windows Raw Input, synthesized into a full gamepad snapshot (movement, look, clicks, scroll) — no physical controller needed |
| **Multiple controllers** | Each "slot" is an independent input → profile → output pipeline; run as many simultaneously as you have devices and outputs for |
| **Profiles** | JSON-based, per-profile polling rate, provider selection, controller style, and rename support |
| **Input providers** | SDL3 unified (all platforms, gamepads + joysticks), Windows Raw Input (keyboard/mouse), Demo preview, None |
| **Output providers** | ViGEm Xbox 360 / DualShock 4 / DualSense (Windows), HIDMaestro (Windows, no kernel driver), Preview (no virtual device, any platform) |
| **Dashboard** | Physical and virtual controllers shown side by side — both the primary pipeline and every active slot — with a shared background/theme picker and a clear "virtual" badge on emitted devices |
| **UI** | 8-language interface, switchable live from the header without restarting |

---

## Requirements

### All platforms

| Requirement | Version |
|---|---|
| [.NET SDK](https://dotnet.microsoft.com/download) | 10.0 or later |
| Git | Any recent version |

### SDL3 native library (required everywhere)

Input — gamepads and joysticks on every platform — goes through SDL3, so the native library must be available next to the app or on the system library path:

| Platform | File | Location |
|---|---|---|
| Windows | `SDL3.dll` | Next to `GameFlow.App.exe` |
| Linux | `libSDL3.so` | System library path, or the app directory |
| macOS | `libSDL3.dylib` | System library path (e.g. `/usr/local/lib`), or the app directory |

Download from [libsdl.org](https://libsdl.org/) or build from source. An optional `gamecontrollerdb.txt` ([SDL_GameControllerDB](https://github.com/mdqinc/SDL_GameControllerDB)) can be placed in the application directory to extend the built-in gamepad mapping database.

### Windows — virtual controller output

GameFlow ships **two independent** ways to create a virtual controller on Windows. Pick either, per slot — they don't depend on each other, and one does not fall back to the other:

- **ViGEm Bus** — the classic route. Emits a virtual Xbox 360, DualShock 4, or DualSense (as a DS4-shaped device) controller.
  → **[Download ViGEm Bus → vigembusdriver.com](https://vigembusdriver.com/)**

- **[HIDMaestro](https://github.com/hifihedgehog/HIDMaestro)** — a newer, user-mode virtual controller platform: no kernel driver, no reboot. Drop **`HIDMaestro.Core.dll`** (plus its driver payload) next to `GameFlow.App.exe` and it activates automatically on next launch — no rebuild needed. The first run may prompt for elevation to install its (still driver-less, UMDF2) component.

If neither is available for a given slot's chosen output, that slot simply has no output — GameFlow will not silently substitute one provider for another, and the log and the slot's displayed name both say exactly why.

Without ViGEm Bus *and* HIDMaestro, the application still runs normally; any slot's output provider falls back to **Preview** (no virtual device — the transformed state is shown on the dashboard only).

### Linux / macOS

SDL3 unified input works out of the box for reading physical gamepads once the native library above is in place. Native virtual-controller **output** (`uinput` on Linux, `IOHIDUserDevice`/CoreHID on macOS) is not implemented yet — **Preview** is the only output provider on these platforms today. Keyboard/mouse-as-gamepad input is currently Windows-only (it's built on Windows' Raw Input API).

For joystick/gamepad access on Linux without `sudo`, add your user to the `input` group:

```bash
sudo usermod -aG input $USER
# then log out and back in
```

---

## Quick Start

### 1. Clone

```bash
git clone https://github.com/Prooxie/GameFlow.git
cd GameFlow
```

### 2. Build

```bash
dotnet build GameFlow.sln
```

### 3. Run

```bash
dotnet run --project src/GameFlow.App
```

On first launch the application creates a default profile under:

| Platform | Location |
|---|---|
| Windows | `%LOCALAPPDATA%\AutofireNext\` |
| Linux | `~/.local/share/AutofireNext/` |
| macOS | `~/Library/Application Support/AutofireNext/` |

> That folder is still named `AutofireNext` on purpose — it predates the rename to GameFlow, and changing it would orphan existing installs' profiles and settings. It's an internal path nobody types or sees; the app itself, everywhere you actually interact with it, says GameFlow.

---

## How to Use

### Create a profile

Open the **Profiles** tab and create a new profile, or start from the default. A profile bundles a polling rate, an input source, and every mapping rule (autofire, remap, thresholds, freeze) you define for it.

### Add a virtual controller (slot)

1. Go to the **Devices** tab and confirm your physical gamepad, keyboard, or mouse is listed under **Physical devices**.
2. Switch to the **Virtual controllers** panel and click **Add controller** to create a slot.
3. Assign the slot's input: pick the physical device from the list.
4. Open the slot's Template Editor and set:
   - **Output kind** — the virtual controller *shape* (Xbox 360 / DualShock 4 / DualSense / Generic).
   - **Output provider** — the *backend* that actually emits it (ViGEm Xbox 360/DS4/DS5, HIDMaestro, or Preview). This is the setting that matters for whether a real device is created — pick it explicitly per slot rather than leaving it on "(inherit from profile)," which follows the profile's own default and is easy to lose track of.
5. Toggle **Enabled** on the slot.

### Confirm it's live

The Dashboard shows a physical/virtual pair for the primary pipeline, plus one full-size physical/virtual pair per active slot — the virtual side carries a colored **VIRTUAL** badge so it's unmistakable which one is the emitted device. Move the physical controller (or type/click, for a keyboard/mouse slot) and confirm the virtual side mirrors it through whatever mapping you've applied.

Note that a slot's own virtual output is deliberately hidden from every input picker in the app — you can't accidentally (or deliberately) feed a virtual controller back in as input, direct or through another slot.

### Add mapping rules

Back in **Profiles**, use **+ Add rule** against any control to add autofire, remap, stick threshold, freeze-direction, or script rules. Rules apply in order, every polling tick, and take effect immediately — no separate "apply" step for rule changes.

### Customize the dashboard

The background picker (Dashboard tab) applies one background — a solid color or "follow the app theme" — across every physical/virtual pair at once, so comparisons stay visually consistent as you switch profiles, slots, or the app's own theme.

---

## Configuration

### appsettings.json

`src/GameFlow.App/appsettings.json` controls runtime behaviour:

```json
{
  "Runtime": {
    "DashboardRefreshHz": 165,
    "StartRuntimeOnLaunch": true,
    "DefaultCulture": "en",
    "EnableViGEm": true,
    "Updates": {
      "RepoOwner": "Prooxie",
      "RepoName": "GameFlow",
      "AssetNamePattern": "GameFlow-{tag}-{rid}.zip",
      "UserAgent": "GameFlow-UpdateChecker"
    }
  }
}
```

| Key | Default | Description |
|---|---|---|
| `DashboardRefreshHz` | `165` | UI refresh rate for the live dashboard and controller panels |
| `StartRuntimeOnLaunch` | `true` | Whether the input/output runtime starts automatically on launch |
| `DefaultCulture` | `en` | Fallback UI language before a user preference is saved |
| `EnableViGEm` | `true` | Enable the ViGEm virtual-controller output providers (Windows) |
| `Updates:RepoOwner` / `RepoName` | `Prooxie` / `GameFlow` | GitHub-releases update checker — enabled by default, pointed at this repo |

### Profiles

Profiles are JSON files stored in the application data directory (see [Quick Start](#quick-start)). Each profile defines:

- Input provider selection and per-controller polling rate (30–1000 Hz)
- All mapping rules (autofire, remap, threshold, freeze, script)
- Controller surface display style and dashboard background
- Slot definitions: assigned input device(s), output kind, and output provider

Profiles can be created, duplicated, renamed, imported, and exported from the **Profiles** tab without touching JSON directly.

---

## Input Providers

| ID | Platform | Notes |
|---|---|---|
| `sdl` | All | SDL3 unified — gamepads and joysticks on every platform, plus Raw Input keyboard/mouse synthesis on Windows. The default and recommended input provider. |
| `demo` | All | Animated preview source for UI testing. No hardware required. |
| `none` | All | Disables live input. Dashboard stays idle. |

Older profiles referencing retired providers (XInput, GameInput, x360ce, PS3, and similar) migrate to `sdl` automatically.

## Output Providers

| ID | Platform | Notes |
|---|---|---|
| `vigem-xbox360` | Windows | Virtual Xbox 360 controller via ViGEm Bus. |
| `vigem-ds4` | Windows | Virtual DualShock 4 controller via ViGEm Bus. |
| `vigem-ds5` | Windows | Virtual DualSense controller via ViGEm Bus (emitted as a DS4-shaped device). |
| `hidmaestro` | Windows | Virtual controller via [HIDMaestro](https://github.com/hifihedgehog/HIDMaestro) — no kernel driver. Activates automatically when `HIDMaestro.Core.dll` is present. |
| `preview` | All | No virtual device. Shows the transformed state on the dashboard only. |

---

## Project Structure

```
GameFlow/
├── src/
│   ├── GameFlow.App              # Avalonia UI, ViewModels, Views, Localization
│   ├── GameFlow.Core              # Domain models, pipeline, rule types, schedulers
│   └── GameFlow.Infrastructure    # SDL3, ViGEm, HIDMaestro, profile persistence, runtime
├── tests/
│   └── GameFlow.Core.Tests        # Unit tests for the mapping pipeline
└── .github/workflows/             # CI: build, test, and release packaging
```

---

## Architecture Overview

```
Physical input                        Keyboard / mouse (Windows)
   │  (SDL3 gamepad/joystick)              │  (Raw Input)
   ▼                                       ▼
InputSource.ReadAsync() / ReadDevice() ────┘
        │
        ▼
 ControllerMappingPipeline   (one per active slot, isolated)
   ├── StickThresholdRule       (deadzone / full-at)
   ├── ButtonRemapRule          (source → target)
   ├── ButtonAutofireRule       (binary pulse scheduler)
   ├── StickAutofireRule        (stick pulse scheduler + hysteresis)
   └── FreezeLastDirectionRule  (rising-edge capture + optional pulse)
        │
        ▼
 OutputSink.WriteAsync()
        │  (ViGEm Xbox360 / DS4 / DS5 · HIDMaestro · Preview)
        ▼
  Virtual controller seen by games — and hidden from GameFlow's
  own input list, so it can never be selected back in as a source.
```

Every slot's rules are stored in its profile's JSON and applied in order on every polling tick. The dashboard reads from lock-free snapshot stores on a timer, independent of the mapping pipeline's own tick rate, so the UI never blocks live input processing.

---

## Localization

The UI is fully translated into:

🇬🇧 English · 🇨🇿 Czech · 🇩🇪 German · 🇪🇸 Spanish · 🇫🇷 French · 🇮🇹 Italian · 🇵🇱 Polish · 🇷🇺 Russian

Language can be changed live from Settings without restarting the application.

---

## Contributing

Contributions are welcome.

1. Fork the repository
2. Create a branch: `git checkout -b feature/my-feature`
3. Commit your changes
4. Open a Pull Request

Please keep commits focused and include a short description of what changed and why.

---

## License

This project is licensed under the **[MIT License](LICENSE)**.

---

## Acknowledgments

- **[ViGEm Bus](https://github.com/nefarius/ViGEmBus)** by Nefarius — the virtual Xbox 360 / DualShock 4 / DualSense driver stack.
- **[HIDMaestro](https://github.com/hifihedgehog/HIDMaestro)** by hifihedgehog — the driver-less, user-mode virtual controller platform.
- **[SDL_GameControllerDB](https://github.com/mdqinc/SDL_GameControllerDB)** — the community gamepad mapping database SDL3 input builds on.
- **[VSCView THEMEENGINE](https://github.com/Nielk1/VSCView/blob/master/THEMEENGINE.md)** by Nielk1 — the controller theme format GameFlow's controller surfaces are compatible with.
- **[AL2009man's Gamepad Asset Pack & Prompt Asset Pack](https://github.com/AL2009man/Gamepad-Asset-Pack)** — controller and button-prompt art.

Check out my other projects on GitHub, [YouTube](https://www.youtube.com/@ProxyDarkness), and [Twitch](https://www.twitch.tv/ProxyDarkness).

---

## Thanks to

**Nazzareno96** — keeping me sane during development, beta testing
[twitch.tv/nazzareno96](https://www.twitch.tv/nazzareno96)

**NoobKillerRoof** — voicing real hardware/software pain points that inspired this project, beta testing
[twitch.tv/noobkillerroof](https://www.twitch.tv/noobkillerroof)
