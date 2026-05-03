## Support

If you find this project useful, you can support its development here:

[![Buy Me a Coffee](https://user-images.githubusercontent.com/1286821/181085373-12eee197-187a-4438-90fe-571ac6d68900.png)](https://buymeacoffee.com/ProxyDarkness)

No pressure. your support is always appreciated, but just using and sharing the project already helps a lot. Thank you!


# Autofire Next

A modern cross-platform desktop application for configuring **autofire (rapid fire)**, input remapping, and gamepad behavior.

Built with **.NET** and **Avalonia UI**, focused on performance, flexibility, and clean architecture.

---

## Features

* Virtual drivers for support for any device
* Autofire / rapid fire configuration
* Button remapping
* JSON-based profiles
* Cross-platform UI (Windows, Linux, macOS) // Linux and MacOS are not tested!
* Multi-language

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
git clone https://github.com/Prooxie/Autofire-Next.git
cd Autofire-Next
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

## Author

Proxy Darkness (Me) - Feel free to checkout my other projects, my videos or my stream!

## Thanks to

Nazzareno96 

Keeping me sane in development - Betatesting

https://www.twitch.tv/nazzareno96


NoobKillerRoof  

For voicing his problems with current hardware / software and inspiring me to do this - Betatesting

https://www.twitch.tv/noobkillerroof

---
