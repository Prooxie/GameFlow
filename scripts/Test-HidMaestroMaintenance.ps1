#Requires -Version 7.3
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$fixture = Join-Path $repository ('.cache/sdk-maintenance-' + [guid]::NewGuid().ToString('N'))
$application = Join-Path $fixture 'GameFlow'
$upstream = Join-Path $fixture 'upstream'
$sdkSource = Join-Path $upstream 'sdk/HIDMaestro.Core'
$scriptDirectory = Join-Path $application 'scripts'
$copyDestination = Join-Path $fixture 'runtime-copy'
New-Item -ItemType Directory -Force -Path $sdkSource, $scriptDirectory,
    (Join-Path $application 'build'), (Join-Path $application 'src/GameFlow.HidMaestro') | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Update-HIDMaestro.ps1') -Destination $scriptDirectory
Copy-Item -LiteralPath (Join-Path $repository 'build/HidMaestroSdk.props') -Destination (Join-Path $application 'build')
Copy-Item -LiteralPath (Join-Path $repository 'src/GameFlow.HidMaestro/HidMaestroSdkLocator.cs') `
    -Destination (Join-Path $application 'src/GameFlow.HidMaestro')
[IO.File]::WriteAllText((Join-Path $application 'GameFlow.sln'), '')
[IO.File]::WriteAllText((Join-Path $sdkSource 'HIDMaestro.Core.csproj'), '<Project Sdk="Microsoft.NET.Sdk" />')
$sourcePath = Join-Path $sdkSource 'HMContext.cs'
$initialSource = "namespace HIDMaestro;`npublic class HMContext`n{`n    public void Connect() { }`n}`n"
[IO.File]::WriteAllText($sourcePath, $initialSource)

& git -C $upstream init --initial-branch=main | Out-Null
& git -C $upstream add .
& git -C $upstream -c user.name=Fixture -c user.email=fixture@example.invalid commit -m 'test: Create SDK fixture' | Out-Null
$base = (& git -C $upstream rev-parse HEAD).Trim()
[IO.File]::WriteAllText($sourcePath, $initialSource.Replace('    public void Connect() { }', "    public void Connect() { }`n    public void Stop() { }"))
& git -C $upstream add .
& git -C $upstream -c user.name=Fixture -c user.email=fixture@example.invalid commit -m 'test: Add fixture method' | Out-Null

[xml]$pin = Get-Content -LiteralPath (Join-Path $repository 'build/HidMaestroSdk.props') -Raw
$pinnedVersion = $pin.SelectSingleNode('//HidMaestroSdkVersion').InnerText
$sdk = Join-Path $repository ".cache/hidmaestro/$pinnedVersion/HIDMaestro.Core.dll"
$originalHash = (Get-FileHash -LiteralPath $sdk -Algorithm SHA256).Hash
$helper = Join-Path $scriptDirectory 'Update-HIDMaestro.ps1'
& $helper -HidMaestroPath $upstream -SkipUpdate -SdkDll $sdk -ReportFrom $base `
    -Destination $copyDestination -Confirm:$false
$copied = Join-Path $copyDestination 'HIDMaestro.Core.dll'
if ((Get-FileHash -LiteralPath $copied -Algorithm SHA256).Hash -ne $originalHash) { throw 'The runtime copy differs.' }
if ((Get-FileHash -LiteralPath $sdk -Algorithm SHA256).Hash -ne $originalHash) { throw 'The pinned SDK changed.' }
$report = Join-Path $application 'docs/hidmaestro/LATEST.md'
if (!(Test-Path -LiteralPath $report)) { throw 'The report was not created.' }
if ((Get-Content -LiteralPath $report -Raw) -notmatch 'Stop') { throw 'The report omitted the changed API.' }

$blocked = $false
try {
    & $helper -HidMaestroPath $upstream -SkipUpdate -SkipReport -SdkDll $sdk `
        -Destination (Join-Path $application '.cache/blocked') -Confirm:$false
}
catch { $blocked = $_.Exception.Message -match 'inside GameFlow.*cache' }
if (!$blocked) { throw 'The helper did not protect its build cache.' }

[IO.File]::WriteAllText($sourcePath, "namespace HIDMaestro;`ninternal class InternalContext { }`n")
& git -C $upstream add .
& git -C $upstream -c user.name=Fixture -c user.email=fixture@example.invalid commit -m 'test: Remove public fixture declarations' | Out-Null
& $helper -HidMaestroPath $upstream -SkipUpdate -SkipBuild -SkipCopy -ReportFrom $base -Confirm:$false
if (!(Test-Path -LiteralPath $report)) { throw 'The helper rejected an empty public API.' }

[IO.File]::AppendAllText($sourcePath, ' // local edit')
$refused = $false
try { & $helper -HidMaestroPath $upstream -SkipUpdate -SkipBuild -SkipCopy -SkipReport -Confirm:$false }
catch { $refused = $_.Exception.Message -match 'uncommitted changes' }
if (!$refused) { throw 'The helper accepted a modified upstream checkout.' }

Write-Output 'PASS: SDK report, runtime copy, cache protection, and modified-checkout rejection.'
Write-Output "Fixture retained at $fixture"
$global:LASTEXITCODE = 0
