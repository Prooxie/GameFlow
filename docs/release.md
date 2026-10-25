# Release maintenance

Version 1.0.9 uses the numeric Git tag `1.0.9`.
The CI workflow also accepts older tags with a `v` prefix.
The tag must point to the confirmed release commit on `master`.

## Validate source

1. Restore packages in locked mode.
2. Build with warnings as errors.
3. Run the complete .NET suite.
4. Run the translation rejection fixtures.
5. Run Chromium and Firefox browser checks where those browsers can start.
6. Inspect application captures and check local documentation links.
7. Check the public archive contents.

```powershell
dotnet restore GameFlow.sln --locked-mode
dotnet build GameFlow.sln -c Release --no-restore -warnaserror
dotnet test --solution GameFlow.sln -c Release --no-build
pwsh ./scripts/Test-LocalizationChecks.ps1
pwsh ./scripts/Test-HidMaestroMaintenance.ps1
pwsh ./scripts/Test-ReleasePackaging.ps1
dotnet format GameFlow.sln --verify-no-changes --no-restore --severity warn
```

Read [the browser commands](../README.md#build-and-test).
Record unavailable platform and hardware checks in the release notes.
A passing build does not prove hardware behavior.

## Build local release archives

Use PowerShell 7.3 or later.
The script reads the version from `Directory.Build.props`.

```powershell
pwsh ./scripts/Publish-Release.ps1
```

The default output is `artifacts/releases/1.0.9`.
The script creates four self-contained archives and four SHA-256 files.
It refuses to reuse a populated publish directory.
Select another `-OutputDirectory` for another validation run.

```powershell
pwsh ./scripts/Publish-Release.ps1 -Runtime win-x64 -OutputDirectory ./artifacts/windows-check
```

The archives include documentation, screenshots, dependency notices, and license files.
The Unix archives retain executable permission for `GameFlow`, including when Windows creates the archive.
The optional HIDMaestro runtime remains separate.

Check each checksum against its archive before distribution.
Extract the complete Windows archive into an isolated folder for a startup check.
Use an isolated `GAMEFLOW_DATA_DIRECTORY` for that check.

## Push and hosted publishing

The release review prepares commits and a local tag.
The maintainer pushes them when ready.

```powershell
git push origin master
git push origin 1.0.9
```

The tag starts the release workflow.
Hosted jobs build and test on Windows, Linux, and macOS.
Browser checks run on Linux with Chromium and Firefox.
The release job verifies all four archive checksums before it creates the GitHub release.
Local cross-publishing does not replace those hosted platform tests.

## Project page

The tracked page is [index.html](index.html).
Its screenshots are in `docs/images`. Its brand assets are in `docs/brand`.
The sibling `Gameflow Page/index.html` supplied the initial page source.
Update the tracked page for future releases.

The Pages workflow deploys `docs` after changes reach `master`.
Select **GitHub Actions** in the repository Pages settings before the first deployment.
This setting follows the [GitHub Pages workflow guide](https://docs.github.com/en/pages/getting-started-with-github-pages/using-custom-workflows-with-github-pages).
Repository permissions and environment rules can require maintainer action.
The workflow cannot deploy until the maintainer pushes it.

## Optional SDK maintenance

`Update-HIDMaestro.ps1` is an explicit maintainer tool.
It can update and build a separate HIDMaestro clone.
Use a separate SDK clone for mutation steps.

Use `-WhatIf` to inspect intended actions.
Use `-SkipUpdate -SkipBuild -SkipCopy` for a report-only run.
Do not include the SDK in GameFlow release archives without an explicit distribution decision.
Do not overwrite the pinned build cache during a release review.
The script help describes its version checks, copy destinations, and cache override.
