# Controller Overlays

This folder is consumed by `Autofire.App.Services.ControllerOverlayAssetLoader`.
Drop PNG files here named exactly as below to give each
`ControllerVisualStyle` a custom base image instead of the
programmatic XAML silhouette:

| Filename            | Bound to              | Source asset (typical)              |
|---------------------|-----------------------|-------------------------------------|
| `xbox.png`          | `Xbox`                | Xbox One or Series X\|S overlay     |
| `playstation4.png`  | `PlayStation4`        | DualShock 4 overlay                 |
| `playstation5.png`  | `PlayStation5`        | DualSense overlay                   |
| `playstation3.png`  | `PlayStation3` (TBD)  | (no overlay in upstream pack today) |

Files missing from this folder are silently ignored — the controller
surface falls back to the original programmatic art. There's no
manifest to update; the loader checks for each file by name.

## Recommended asset source

[**AL2009man / Gamepad-Asset-Pack**](https://github.com/AL2009man/Gamepad-Asset-Pack)
— MIT licensed; attribution is required and is given in the project
top-level README.

Run one of the helper scripts at the repository root to download the
zip, extract it, and stage the files here automatically:

- Windows / PowerShell: `tools/download-controller-assets.ps1`
- Linux / macOS: `tools/download-controller-assets.sh`

## Image dimensions

The XAML places each overlay inside a 540×320 Canvas with
`Stretch="Uniform"`. Source images are typically 1000–2000 px wide
SVG/PNG renders; they scale down cleanly. The button-overlay markers
that show live press state are positioned in the original Canvas
coordinate space and may not pixel-align with the asset-pack image's
button positions. They still convey "this button is pressed" via
colour glow; per-style position calibration is a follow-up.
