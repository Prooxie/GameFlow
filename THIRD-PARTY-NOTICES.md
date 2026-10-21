# Third-party notices

GameFlow source is MIT licensed; see `LICENSE`. Third-party components retain
their own licenses and copyright notices.

Release archives include `dependencies.json` with exact resolved NuGet package
versions, license metadata and package links. The `licenses/` directory contains
the license and notice files supplied by those packages, their NuGet manifests,
and the additional notices listed below. Framework and native components may
carry further notices in their own packages; this inventory is not a claim that
every bundled native subcomponent has been independently audited.

| Component | License / attribution | Source |
| --- | --- | --- |
| AL2009man's controller artwork | MIT, copyright Al. Lopez; full notice in `licenses/AL2009man-LICENSE.txt` | https://github.com/AL2009man/Gamepad-Asset-Pack |
| VSCView theme materials and format | MIT, copyright John Klein; full notice in `licenses/VSCView-LICENSE.txt` | https://github.com/Nielk1/VSCView |
| MoonSharp 2.0 | BSD-style license, including its KopiLua notices; full text in `licenses/MoonSharp-LICENSE.txt` | https://github.com/moonsharp-devs/moonsharp |
| SDL3 native binaries | zlib; notice copied from `SDL3-CS.Native` during packaging | https://github.com/libsdl-org/SDL |
| Avalonia, SkiaSharp, HarfBuzzSharp and Microsoft.Extensions | See exact package metadata and accompanying native notices | `dependencies.json` |
| OrchardCore.Localization | BSD-3-Clause | https://github.com/OrchardCMS/OrchardCore |
| Serilog and its integrations | Apache-2.0 | https://github.com/serilog |

Controller, console and game names and logos belong to their respective owners.
The asset pack's upstream documentation distinguishes community overlay use
from commercial game distribution; review those guidelines before repackaging
the art for another purpose. The original upstream revision of each bundled
theme has not been recorded in this repository.

HIDMaestro is an optional, separately obtained dependency. It is not included in
the GameFlow archives; retain its upstream license when distributing it yourself.
