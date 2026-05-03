# Architecture

## Layers

### 1. Autofire.Core

Pure domain code. No file system, no UI, no platform APIs.

Responsibilities:

- Represent controller state.
- Represent profiles and mapping rules.
- Run autofire pulse schedules.
- Apply deadzone and amplification transforms.
- Merge remap/freeze/autofire rules into a virtual output frame.

### 2. Autofire.Infrastructure

Application services and persistence.

Responsibilities:

- Read and write JSON profiles.
- Track the active profile.
- Load PO-based translations.
- Host background runtime services.
- Expose live runtime snapshots to the UI.
- Create platform providers from profile-selected ids.

### 3. Autofire.App

Avalonia desktop shell.

Responsibilities:

- Splash window at startup.
- Dashboard showing physical and virtual controller surfaces.
- Inline control inspector instead of a floating config window.
- Profile editor.
- Diagnostics and provider roadmap.
- Language switching.

## Runtime flow

```text
ProfileDocument
     |
     v
IInputSourceFactory ----> IInputSource ----> ControllerMappingPipeline ----> IOutputSink ----> RuntimeSnapshotStore
                                                     |
                                                     v
                                                Avalonia UI
```

## Provider model

The runtime no longer depends on one hard-coded input provider. Each profile selects provider ids such as:

- `demo`
- `none`
- `gameinput`
- `preview`

At runtime, `RuntimeCoordinator` asks the factories to create the appropriate implementations. This makes it possible to add Windows, Linux, and macOS adapters without changing the mapping pipeline.

## Current Windows provider status

`GameInputInputSource` remains in the tree as the first native Windows provider branch, but it is disabled by default in configuration while the interop layer is being hardened.

Current safe behavior:

- the dashboard can request `gameinput`
- the factory prefers Microsoft.GameInput on Windows and returns an idle provider instead of silently falling back to demo input when native input is unavailable
- if not enabled, the runtime stays on the safe preview path
- if re-enabled later, the runtime can still fall back to the demo provider when initialization fails

## Rule ordering

1. Threshold/deadzone rules prepare source sticks.
2. Button remap and button turbo rules update button state.
3. Stick autofire rules apply repeating vector output.
4. Freeze-last-direction rules can override or blend onto the target stick after autofire.

## Why the pipeline is stateful

Autofire and freeze behavior depend on prior frames:

- pulse phase
- last non-zero direction
- post-switch settle windows

That state belongs in the shared engine, not in the UI or a platform adapter.

## Extensibility points

### Input providers

Implement `IInputSource` to read controller state from:

- GameInput on Windows
- evdev / SDL on Linux
- GameController / HID on macOS

### Output providers

Implement `IOutputSink` to emit virtual state to:

- preview UI only
- native virtual devices
- test harnesses
- file-based trace recording

### Future scripting

A future plugin boundary can compile or sandbox custom macros while still feeding the same `ControllerMappingPipeline`.
