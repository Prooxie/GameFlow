# Platform matrix

## Current implementation status

| Feature | Windows | Linux | macOS |
| --- | --- | --- | --- |
| Avalonia desktop UI | Implemented | Implemented | Implemented |
| Demo input provider | Implemented | Implemented | Implemented |
| Idle input provider (`none`) | Implemented | Implemented | Implemented |
| Microsoft.GameInput input provider | Implemented for Windows, requires runtime validation | N/A | N/A |
| Preview output sink | Implemented | Implemented | Implemented |
| Native virtual output provider | Planned | Planned (`uinput`) | Planned (`CoreHID`) |
| Exact DSX-style per-controller skin | Deferred | Deferred | Deferred |

## Why the native providers are split

Input and virtual output capabilities are platform-specific. The shared engine remains the same, but the adapter layer changes:

- Windows is targeting `Microsoft.GameInput` for native input once the interop is stable.
- Linux will commonly use the kernel input subsystem and `uinput`.
- macOS will use GameController and/or CoreHID style APIs depending on the feature.

## Current Windows scope

The Windows path now prefers Microsoft.GameInput. Demo preview and idle input remain available as explicit user-selectable providers.

## Recommendation

Keep the stabilized dashboard and inline inspector as the default shell, then add:

1. safe generated GameInput bindings
2. real device enumeration and multi-device routing
3. native output providers per platform
4. exact controller-specific visual skins
