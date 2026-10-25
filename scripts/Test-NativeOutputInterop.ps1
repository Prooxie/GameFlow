#Requires -Version 7.3
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$probeDirectory = Join-Path $repository '.cache/native-output-abi'
New-Item -ItemType Directory -Path $probeDirectory -Force | Out-Null
$probe = Join-Path $probeDirectory 'probe'
if ($IsLinux) {
    & cc -std=c11 -Wall -Wextra -Werror (Join-Path $repository 'tests/native/linux-uinput-abi.c') -o $probe
}
elseif ($IsMacOS) {
    & clang -std=c11 -Wall -Wextra -Werror (Join-Path $repository 'tests/native/macos-hid-abi.c') `
        -framework IOKit -framework CoreFoundation -o $probe
}
else {
    Write-Output 'Native ABI checks require Linux or macOS headers.'
    exit 0
}
if ($LASTEXITCODE -ne 0) { throw 'Native output declarations do not match the platform headers.' }
& $probe
if ($LASTEXITCODE -ne 0) { throw 'Native output API exports are unavailable.' }
Write-Output 'Native output declarations match the platform headers.'
