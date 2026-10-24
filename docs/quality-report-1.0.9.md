# GameFlow 1.0.9 quality report

Review date: 24 October 2026.
The starting branch was `ux-fixes-2026-10` at `ba51981`, the confirmed 1.0.8 release commit.
The review preserved the starting patch in `.cache/release-review-1.0.9` before changes.
The sibling HIDMaestro source was not changed or built.

## Automated evidence

| Check | Result |
| --- | --- |
| Locked solution restore | Passed. |
| Release build with warnings as errors | Passed with zero warnings. |
| Repository formatting | Passed with no remaining formatter changes. |
| Complete .NET suite | 1,568 passed. No failures or skips. |
| Chromium phone, overlay, and website checks | 12 passed. |
| Translation rejection fixtures | 12 passed, including valid input and expected rejection cases. |
| Bundled theme image resolution | All 28 manifests passed. |
| Imported input resources | All 243 PNG resources decoded from the embedded manifest. |
| SDK maintenance fixtures | Report, runtime copy, empty inventories, cache protection, and modified-checkout rejection passed. |
| Effects allocation regression | Idle production fell from 248 bytes per pass to zero after warmup. |
| Website layout | Desktop images and local anchors passed. The 390-pixel phone viewport had no horizontal overflow. |
| GitHub workflow syntax | CI and Pages passed Actionlint 1.7.12. |

The focused regressions failed before their corresponding fixes.
The review removed an unused reflection bridge that called deleted methods and prevented compilation.
The typed HIDMaestro adapter remains the SDK boundary.

## Assets and execution

All shipped theme manifests resolve their images through the actual browser overlay compiler.
The application embeds 216 prompt vectors and 27 connection/controller icon vectors, plus their PNG conversions.
Mapping pickers use shared positional prompts. The Devices page uses controller family icons.
The converter caches decoded images and releases them at shutdown.
The embedded manifest records each source path and SHA-256 hash.

The supplied controller pack contains 593 PNG files.
Of these, 367 have an identical bundled copy.
The other 226 source images have no byte-identical bundled copy.
The shipped theme manifests have no unresolved image references.
The [asset inventory](asset-audit.json) records this distinction for each source image.
This review does not claim that every upstream image has a selectable theme.

## Translations

All eight catalogs have the same keys, nonempty values, and matching format argument sets.
The review migrated remaining application status, editor, calibration, setup, and settings messages to those catalogs.
Live UI regressions switch through all eight languages.
Rule summaries share the translated inspector formatter.

The build rejects missing keys, malformed catalogs, empty translations, copied long English prose, and mismatched arguments.
It also checks exposed enums, XAML literals, browser attributes, fallback helper keys, and direct C# status assignments.
The checks cannot prove translation quality or detect every possible dynamically generated string.
User names, device names, key legends, scripts, protocol errors, and SDK diagnostics retain their source text.

## Coding and performance limits

Compilation and the automated checks validate the current behavior.
They do not certify complete compliance with every design rule in CODING.md.
The existing App project directly references Core and Infrastructure, which conflicts with the API-only frontend rule.
Several existing files contain multiple types or incomplete XML documentation.
These structural differences remain visible. A separate architecture change needs an agreed application boundary.

The review measured and removed idle effects collection allocations.
The existing typed state-writer regression also checks allocation-free steady-state submission.
No claim is made that every application path has optimal performance.

## Documentation and captures

README, WIKI, the translation guide, the OBS guide, and the release guide describe the current workflows.
The tracked project page is `docs/index.html`.
README and the page use current captures of actual application controls with isolated demonstration data.
The captures show Preview output. They do not prove physical hardware behavior.
Older audit files remain historical records and link to this report.

## Release and external verification

The release scripts prepare Windows x64, Linux x64, macOS ARM64, and macOS x64 archives with SHA-256 files.
The numeric tag `1.0.9` starts the hosted release workflow after the maintainer pushes it.
The Pages workflow deploys the tracked documentation page after it reaches `master`.
The maintainer must enable GitHub Actions as the Pages publishing source if it is not enabled.
The Pages API returned 404 during this review. No live site deployment is claimed.

The following checks remain external:

- Real controller output and feedback in games.
- Physical hot-plug, disconnect cleanup, and laptop input enumeration.
- Real mobile browsers, phone motion, and OBS Browser sources.
- macOS hardware input and output.
- Local Firefox execution. Its binary cannot start on the review computer.
- Hosted platform, Firefox, release, and Pages jobs after the maintainer pushes the commits and tag.
