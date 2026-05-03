# Migration plan from the Python prototype

This rewrite keeps the **behavioral intent** of the Python code while replacing the packaging, UI stack, and runtime hosting model.

## Python to .NET mapping

| Python module | Purpose | .NET replacement |
| --- | --- | --- |
| `autofire/core/engine.py` | stick pulse scheduler | `Autofire.Core/Pipeline/StickPulseScheduler.cs` |
| `autofire/core/runtime.py` | per-frame loop | `Autofire.Infrastructure/Runtime/RuntimeCoordinator.cs` |
| `autofire/io/mapping.py` | deadzone/amplify/clamp | `Autofire.Core/Models/StickVector.cs` |
| `autofire/gui/profile_store.py` | JSON profiles/settings | `Autofire.Infrastructure/Profiles/JsonProfileRepository.cs` |
| `autofire/gui/app.py` | desktop shell | `Autofire.App` Avalonia UI |
| `autofire/overlay/*` | feedback overlay | in-app dashboard preview for v1 |

## Preserved behaviors

- Right-stick to left-stick autofire pulse.
- 128 ms hold / 32 ms release defaults.
- Freeze-last-direction with L1.
- Profile-driven settings and JSON persistence.
- Separation between physical input and virtual output.

## Intentional changes

- The GUI is now Avalonia instead of Qt/PySide.
- Background work is hosted by `IHostedService`.
- Localization is PO-based instead of hard-coded UI text.
- Native device providers are behind interfaces so the engine can remain cross-platform.
- The shipping scaffold now includes both a demo provider and a first Windows GameInput provider.

## Completed migration steps

1. Shared mapping engine and JSON profile format.
2. Avalonia shell with preview dashboard.
3. Demo input source for engine/UI iteration.
4. Windows `Microsoft.GameInput` input provider.

## Recommended next implementation steps

1. Add device enumeration and per-device selection on top of GameInput.
2. Add a privileged native virtual output provider per platform.
3. Add rule editors for every polymorphic rule type.
4. Add optional WebSocket overlay streaming once device fidelity requirements are clear.
