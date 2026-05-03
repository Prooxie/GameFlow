# Microsoft.GameInput provider

This branch now treats `Microsoft.GameInput` as the primary Windows input provider instead of a hidden experimental preview path.

## Current behavior
- Uses the native `gameinput.dll` entry point.
- Registers a device callback to discover connected gamepads.
- Reads friendly device metadata through `IGameInputDevice::GetDeviceInfo` when available.
- Publishes detected controllers to the dashboard so a user can select which controller should be read.
- Returns an idle state instead of silently falling back to demo input when native input is unavailable.

## Current limitations
- The provider still uses handwritten interop and therefore must be validated carefully against the installed GameInput runtime on the target Windows machine.
- The current snapshot model only exposes standard gamepad data. Touchpad finger coordinates, motion sensors, battery state, and haptics are not surfaced yet.
- Virtual controller output is still preview-only in this branch.

## Windows setup note
The GameInput redistributable still needs to be present on the target machine. The NuGet package includes the redistributable, but it is not installed automatically.
