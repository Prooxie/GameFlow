# Release scope

This document describes the scope of GameFlow 1.0.9.
The [README](../README.md) describes setup and common tasks.
The [WIKI](../WIKI.md) contains the detailed reference.

## Application

- Map supported gamepads, keyboards, mice, and phone browsers through saved profiles.
- Configure up to 16 slots with separate input assignments and output templates.
- Provide mapping rules, device settings, motion mapping, and touchpad mapping.
- Configure physical controller effects where the device reports support.
- Provide browser controller input and read-only OBS overlays.
- Provide eight UI languages and controller artwork.

## Platform boundaries

Windows virtual gamepad output uses the optional HIDMaestro SDK.
Linux and macOS provide Preview gamepad output.
Native input and mouse output depend on platform permissions and device support.
Artwork does not add a hardware protocol or prove device compatibility.

## Verification boundaries

Automated checks cover mapping, persistence, protocol handling, UI resources, and browser behavior.
Physical controllers, phone browsers, OBS, and macOS hardware need separate checks.
The [quality report](quality-report-1.0.9.md) records the evidence and remaining limits.
The [worklist](../TODO.md) records open verification and coding work.
