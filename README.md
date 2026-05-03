
<div align="center">

<img src="https://user-images.githubusercontent.com/1286821/181085373-12eee197-187a-4438-90fe-571ac6d68900.png" width="0" height="0" />

# ⊕ Autofire Next

**Cross-platform gamepad tooling for speedrunners and power users.**  
Autofire, remapping, stick shaping, freeze macros, and virtual controller output — all in one clean UI.

Built with **.NET 10** · **Avalonia UI** · **ViGEm**

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20%7C%20macOS-informational)](https://github.com/Prooxie/Autofire-Next)
[![Made by Proxy Darkness](https://img.shields.io/badge/made%20by-Proxy%20Darkness-blueviolet)](https://buymeacoffee.com/ProxyDarkness)

---

[![Buy Me a Coffee](https://user-images.githubusercontent.com/1286821/181085373-12eee197-187a-4438-90fe-571ac6d68900.png)](https://buymeacoffee.com/ProxyDarkness)
*No pressure — using and sharing the project already helps a lot. Thank you!*

</div>

---

## What is Autofire Next?

Autofire Next is a desktop application that sits between your physical gamepad and any game or application.
It reads your physical controller, transforms its inputs according to a saved **profile**, and writes the result to a **virtual controller** that the game sees.

Transformations include:

- **Autofire / rapid fire** — pulse any button or stick at a configurable rate
- **Button remapping** — re-route any button to any other button
- **Stick shaping** — deadzone, full-at threshold, and blend-mode control
- **Freeze last direction** — latch the stick vector at the moment a button is pressed
- **Profile system** — create, duplicate, rename, import, and export JSON profiles
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
| **UI** | Dual controller surface (physical + virtual side-by-side), live diagnostics, 8-language UI |
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

Profiles can be created, duplicated, renamed, imported, and exported from the **Profiles** tab without touching JSON directly.

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
│   └── Autofire.Infrastructure   # SDL3, XInput, ViGEm, profile persistence, runtime
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

## License

This project is licensed under the **[MIT License](LICENSE)**.

---

## Author

**Proxy Darkness** — feel free to check out my other projects, videos, and stream.

[Youtube](https://www.youtube.com/@ProxyDarkness)

[Twitch](https://www.twitch.tv/ProxyDarkness)

---

## Thanks to

**Nazzareno96** — keeping me sane during development, beta testing  
[twitch.tv/nazzareno96](https://www.twitch.tv/nazzareno96)

**NoobKillerRoof** — voicing real hardware/software pain points that inspired this project, beta testing  
[twitch.tv/noobkillerroof](https://www.twitch.tv/noobkillerroof)

