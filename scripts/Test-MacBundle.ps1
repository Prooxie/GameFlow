#Requires -Version 7.3
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$fixture = Join-Path $repository ".cache/mac-bundle-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path (Join-Path $fixture 'Localization') -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $fixture 'GameFlow'), 'executable fixture')
[IO.File]::WriteAllText((Join-Path $fixture 'Localization/en.po'), 'catalogue fixture')
& (Join-Path $PSScriptRoot 'Prepare-MacBundle.ps1') -PublishDirectory $fixture -Version '1.1.0'
$contents = Join-Path $fixture 'GameFlow.app/Contents'
[xml]$information = Get-Content -LiteralPath (Join-Path $contents 'Info.plist') -Raw
$version = $information.SelectSingleNode('/plist/dict/key[text()="CFBundleShortVersionString"]/following-sibling::*[1]')
if ($version.InnerText -ne '1.1.0') { throw 'The macOS bundle version is incorrect.' }
if ([IO.File]::ReadAllText((Join-Path $contents 'MacOS/GameFlow')) -ne 'executable fixture') { throw 'The executable changed.' }
if (!(Test-Path -LiteralPath (Join-Path $contents 'MacOS/Localization/en.po'))) { throw 'The bundle lost its runtime assets.' }
$blocked = $false
try { & (Join-Path $PSScriptRoot 'Prepare-MacBundle.ps1') -PublishDirectory $fixture -Version '1.1.0' }
catch { $blocked = $_.Exception.Message -eq 'The macOS application bundle already exists.' }
if (!$blocked) { throw 'The bundle helper did not protect an existing bundle.' }
Write-Output 'macOS bundle assembly checks passed.'
$global:LASTEXITCODE = 0
