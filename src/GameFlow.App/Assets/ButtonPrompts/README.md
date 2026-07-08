# Button Prompt Glyphs

This folder is consumed by `GameFlow.App.Services.ButtonPromptAssetLoader`.
Drop PNG files here under per-style subfolders to surface
controller-shaped button glyphs in places where the UI currently
shows letters (`A` / `B` / `X` / `Y`) or PlayStation symbols.

## Layout

```
ButtonPrompts/
├── xbox/
│   ├── south.png   ← A
│   ├── east.png    ← B
│   ├── west.png    ← X
│   ├── north.png   ← Y
│   ├── l1.png  l2.png  r1.png  r2.png
│   ├── share.png   options.png   guide.png
│   ├── l3.png      r3.png
│   └── dpad-up.png  dpad-down.png  dpad-left.png  dpad-right.png
├── playstation4/
│   └── ... same key set ...
└── playstation5/
    └── ... same key set ...
```

Files missing from this folder are silently ignored — the existing
text label is shown in their place.

## Status

The loader is wired up and ready, but **no XAML binding consumes it
yet**. Today the controller surface continues to render plain text
labels inside button overlays. A follow-up roadmap pass will pair
each label TextBlock with an Image bound through this loader so users
who install the prompt pack see proper glyphs.

## Recommended asset source

[**AL2009man / Gamepad-Prompt-Asset-Pack**](https://github.com/AL2009man/Gamepad-Prompt-Asset-Pack)
— MIT licensed; attribution is required and is given in the project
top-level README.

Run one of the helper scripts at the repository root to download the
zip, extract it, and stage the files here automatically:

- Windows / PowerShell: `tools/download-controller-assets.ps1`
- Linux / macOS: `tools/download-controller-assets.sh`
