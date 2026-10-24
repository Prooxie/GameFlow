# Exercise the build-time localisation gate against isolated fixtures.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('GameFlow-localization-check-' + [guid]::NewGuid().ToString('N'))
$catalogues = Join-Path $fixture 'src/GameFlow.App/Localization'
$build = Join-Path $fixture 'build'
New-Item -ItemType Directory -Path $catalogues, $build -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repo 'build/Localization.targets'), (Join-Path $repo 'build/LocalizationCheck.cs') -Destination $build
Get-ChildItem -LiteralPath (Join-Path $repo 'src/GameFlow.App/Localization') -Filter '*.po' | Copy-Item -Destination $catalogues
$project = Join-Path $fixture 'Checks.proj'
[IO.File]::WriteAllText($project, '<Project><Import Project="build/Localization.targets" /></Project>')
$code = Join-Path $fixture 'src/Input.cs'
$view = Join-Path $fixture 'src/View.axaml'
$csPath = Join-Path $catalogues 'cs.po'
$enPath = Join-Path $catalogues 'en.po'
$originalCs = [IO.File]::ReadAllText($csPath)
$originalEn = [IO.File]::ReadAllText($enPath)
$baseline = 'public enum ButtonId { None, South }'
function Set-FixtureDefaults {
    [IO.File]::WriteAllText($csPath, $originalCs)
    [IO.File]::WriteAllText($enPath, $originalEn)
    [IO.File]::WriteAllText($code, $baseline)
    [IO.File]::WriteAllText($view, '<TextBlock Text="{DynamicResource Ui.UiClose}" />')
}
function Assert-Check([string]$Case, [string]$ExpectedCode = '') {
    $output = & dotnet msbuild $project /t:ValidateLocalization /nologo /v:quiet 2>&1
    $codeValue = $LASTEXITCODE
    if (($ExpectedCode -eq '' -and $codeValue -ne 0) -or
        ($ExpectedCode -ne '' -and ($codeValue -eq 0 -or ($output -join "`n") -notmatch $ExpectedCode))) {
        throw "$Case failed. Expected '$ExpectedCode', exit $codeValue. Fixture retained at $fixture.`n$($output -join "`n")"
    }
    Write-Host "PASS: $Case"
    Set-FixtureDefaults
}
Set-FixtureDefaults
Assert-Check 'valid catalogues'
[IO.File]::WriteAllText($csPath, [regex]::Replace($originalCs, 'msgid "ButtonId.South"\r?\nmsgstr "[^\r\n]*"\r?\n?', ''))
Assert-Check 'missing translation' 'GFLOC002'
[IO.File]::WriteAllText($csPath, [regex]::Replace($originalCs, '(msgid "InputMapCapturePrompt"\r?\n)msgstr "[^\r\n]*"', '$1msgstr "Chybny text."'))
Assert-Check 'mismatched format arguments' 'GFLOC003'
[IO.File]::AppendAllText($enPath, "`nmsgid `"UiClose`"`nmsgstr `"Close`"`n")
Assert-Check 'duplicate key' 'GFLOC001'
[IO.File]::AppendAllText($enPath, "`nmsgid `"BrokenEntry`"`n")
Assert-Check 'malformed PO entry' 'GFLOC001'
[IO.File]::WriteAllText($code, 'var label = UiTextResources.Get("MissingOnPurpose");')
Assert-Check 'unknown C# resource' 'GFLOC005'
[IO.File]::WriteAllText($view, '<TextBlock Text="Untranslated label" />')
Assert-Check 'hardcoded XAML text' 'GFLOC006'
[IO.File]::WriteAllText($code, 'public enum ButtonId { None, South, NewButtonOnPurpose }')
Assert-Check 'untranslated enum value' 'GFLOC007'
[IO.File]::WriteAllText($code, 'const string html = """<span data-i18n="MissingOnPurpose">Test</span>""";')
Assert-Check 'unknown browser resource' 'GFLOC005'
[IO.File]::WriteAllText($code, 'var label = L("MissingOnPurpose", "fallback");')
Assert-Check 'unknown helper resource' 'GFLOC005'
[IO.File]::WriteAllText($code, 'var label = Loc("MissingOnPurpose", "fallback");')
Assert-Check 'unknown fallback resource' 'GFLOC005'
[IO.File]::WriteAllText($code, 'StatusText = "Hardcoded status message";')
Assert-Check 'hardcoded C# status' 'GFLOC008'
Write-Host "All 12 checks passed. Isolated fixture: $fixture"
$global:LASTEXITCODE = 0
