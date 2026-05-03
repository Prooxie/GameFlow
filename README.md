# Autofire Next

A modern cross-platform desktop application for configuring **autofire (rapid fire)**, input remapping, and gamepad behavior.

Built with **.NET** and **Avalonia UI**, focused on performance, flexibility, and clean architecture.

---

## Features

* Autofire / rapid fire configuration
* Button remapping
* JSON-based profiles
* Gamepad input testing tools
* Cross-platform UI (Windows, Linux, macOS)

---

## Requirements

### General

* [.NET SDK](https://dotnet.microsoft.com/) (matching `net10.0`)
* Git

---

### Windows (important)

For full functionality (virtual controller output), install:

* **ViGEmBus Driver**
  https://vigembusdriver.com/

This is required for:

* virtual Xbox controller emulation
* advanced input remapping

---

### Optional / Planned integrations

* Microsoft GameInput (see `docs/MicrosoftGameInput.md`)
* HID-compatible controllers

---

## Quick Start

### Clone repository

```bash
git clone https://github.com/<your-username>/AutofireNext.git
cd AutofireNext
```

---

### Build

```bash
dotnet build
```

---

### Run

```bash
dotnet run --project src/Autofire.App
```

---

## Configuration

The application uses:

* `appsettings.json` (in `Autofire.App`)
* JSON profiles (see `samples/`)

Example:

```bash
samples/SpeedrunnerDefault.profile.json
```

Profiles define:

* button mappings
* autofire behavior
* timing parameters

---

## Project Structure

```text
src/
├── Autofire.App            # Avalonia UI
├── Autofire.Core           # Domain logic
├── Autofire.Infrastructure # Input + system integrations
```

Additional folders:

* `docs/` – architecture & technical notes
* `samples/` – example configurations
* `scripts/` – publishing scripts

---

## Publishing

### Linux / macOS

```bash
./scripts/publish.sh
```

### Windows (PowerShell)

```powershell
./scripts/publish.ps1
```

You can customize runtime identifiers (RID), e.g.:

* `win-x64`
* `linux-x64`
* `osx-x64`

---

## Development

Run in development mode:

```bash
dotnet run --project src/Autofire.App
```

CI is configured via GitHub Actions:

```text
.github/workflows/ci.yml
```

---

## Gamepad & Input

The project works with:

* ViGEmBus (virtual controllers)
* Microsoft GameInput (experimental / documented)

See:

* `docs/MicrosoftGameInput.md`
* `docs/GameInputSmokeTest.md`

---

## Contributing

Contributions are welcome:

1. Fork the repository
2. Create a branch (`feature/...`)
3. Commit changes
4. Open a Pull Request

---

## License

This project is licensed under the **MIT License**.

---

## Roadmap Ideas

* GUI mapping editor (partial implementation exists)
* profile import/export UI
* better real-time input visualization
* plugin system

---

## Author

Proxy Darkness (Me)

---
