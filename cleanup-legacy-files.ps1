# cleanup-legacy-files.ps1
#
# Removes the legacy provider files that Autofire-Next-patched.zip no longer
# contains. Extracting a zip OVER an existing working copy overwrites files
# but never deletes the ones the zip dropped — so these stale files survived
# on disk and broke the build (they used to be masked by <Compile Remove>
# entries that are intentionally gone from the new csproj).
#
# Place this script in the repository root (next to the src folder) and run:
#   powershell -ExecutionPolicy Bypass -File .\cleanup-legacy-files.ps1

$ErrorActionPreference = 'Stop'
$root  = Split-Path -Parent $MyInvocation.MyCommand.Path
$infra = Join-Path $root 'src\Autofire.Infrastructure\Runtime'

$targets = @(
    'XInput',                          # folder — legacy XInput interop
    'XInputInputSource.cs',
    'OpenXInput',                      # folder
    'X360ce',                          # folder
    'Ps3',                             # folder
    'GameInput',                       # folder — GameInput interop
    'GameInputInputSource.cs',
    'WindowsMidi',                     # folder — MIDI in/out scaffolds
    'VJoy',                            # folder
    'ScaffoldedInputSourceBase.cs',
    'Providers',                       # folder — broken IInputProvider prototypes
    'SdlGamepadInputSource.cs',        # pre-refactor prototype (may not exist)
    'Sdl\Sdl3Interop.cs',
    'Sdl\SdlGamepadInterop.cs',
    'Sdl\SdlRuntimeLease.cs'
)

foreach ($relative in $targets) {
    $path = Join-Path $infra $relative
    if (Test-Path $path) {
        Remove-Item -Path $path -Recurse -Force
        Write-Host "removed:      $relative"
    }
    else {
        Write-Host "already gone: $relative"
    }
}

# Verify no legacy file survived anywhere under Runtime.
$patterns = @(
    'XInput*','OpenXInput*','X360ce*','Ps3*','GameInput*','WindowsMidi*',
    'VJoy*','Sdl3Interop*','SdlGamepadInterop*','SdlRuntimeLease*',
    'ScaffoldedInputSourceBase*','Sdl3InputProvider*','LinuxEvdev*','MacOsCoreHid*'
)
$leftovers = Get-ChildItem -Path $infra -Recurse -Include $patterns -ErrorAction SilentlyContinue

if ($leftovers) {
    Write-Warning 'Some legacy files remain:'
    $leftovers | ForEach-Object { Write-Warning "  $($_.FullName)" }
}
else {
    Write-Host ''
    Write-Host 'Clean. Rebuild should now succeed.' -ForegroundColor Green
}
