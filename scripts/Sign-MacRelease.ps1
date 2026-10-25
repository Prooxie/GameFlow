#Requires -Version 7.3
[CmdletBinding()]
param([Parameter(Mandatory)][string]$BundleDirectory)

$ErrorActionPreference = 'Stop'
if (!$env:MACOS_SIGNING_CERTIFICATE) {
    Write-Warning 'The macOS archive is unsigned. Native gamepad output requires Apple-approved virtual HID signing.'
    exit 0
}
foreach ($name in @('MACOS_CERTIFICATE_PASSWORD', 'MACOS_SIGNING_IDENTITY', 'MACOS_PROVISIONING_PROFILE',
        'MACOS_NOTARY_APPLE_ID', 'MACOS_NOTARY_TEAM_ID', 'MACOS_NOTARY_PASSWORD')) {
    if (![Environment]::GetEnvironmentVariable($name)) { throw "The signing configuration is incomplete: $name" }
}
if (!$IsMacOS) { throw 'Signing a macOS release requires macOS.' }
$temporary = Join-Path $env:RUNNER_TEMP "gameflow-sign-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $temporary | Out-Null
$certificate = Join-Path $temporary 'certificate.p12'
$profile = Join-Path $temporary 'profile.provisionprofile'
$keychain = Join-Path $temporary 'signing.keychain-db'
$password = [guid]::NewGuid().ToString('N')
$oldKeychains = @(& security list-keychains -d user | ForEach-Object { $_.Trim().Trim('"') })
try {
    [IO.File]::WriteAllBytes($certificate, [Convert]::FromBase64String($env:MACOS_SIGNING_CERTIFICATE))
    [IO.File]::WriteAllBytes($profile, [Convert]::FromBase64String($env:MACOS_PROVISIONING_PROFILE))
    & security create-keychain -p $password $keychain
    if ($LASTEXITCODE -ne 0) { throw 'Cannot create the release keychain.' }
    & security set-keychain-settings -lut 21600 $keychain
    if ($LASTEXITCODE -ne 0) { throw 'Cannot set the release keychain lifetime.' }
    & security unlock-keychain -p $password $keychain
    if ($LASTEXITCODE -ne 0) { throw 'Cannot unlock the release keychain.' }
    & security import $certificate -P $env:MACOS_CERTIFICATE_PASSWORD -A -t cert -f pkcs12 -k $keychain
    if ($LASTEXITCODE -ne 0) { throw 'Cannot import the Developer ID certificate.' }
    & security set-key-partition-list -S apple-tool:,apple:,codesign: -s -k $password $keychain | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Cannot authorize the release signing key.' }
    & security list-keychains -d user -s $keychain @oldKeychains
    if ($LASTEXITCODE -ne 0) { throw 'Cannot select the release keychain.' }
    & (Join-Path $PSScriptRoot 'Sign-MacBundle.ps1') -BundleDirectory $BundleDirectory `
        -Identity $env:MACOS_SIGNING_IDENTITY -ProvisioningProfile $profile
    $archive = Join-Path $temporary 'GameFlow.zip'
    & ditto -c -k --keepParent $BundleDirectory $archive
    if ($LASTEXITCODE -ne 0) { throw 'Cannot assemble the notarization archive.' }
    & xcrun notarytool submit $archive --apple-id $env:MACOS_NOTARY_APPLE_ID `
        --team-id $env:MACOS_NOTARY_TEAM_ID --password $env:MACOS_NOTARY_PASSWORD --wait
    if ($LASTEXITCODE -ne 0) { throw 'Apple did not accept the notarization request.' }
    & xcrun stapler staple $BundleDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Cannot attach the notarization ticket.' }
    & spctl --assess --type execute --verbose=2 $BundleDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Gatekeeper rejected the macOS application.' }
}
finally {
    & security list-keychains -d user -s @oldKeychains
    & security delete-keychain $keychain
    foreach ($file in @($certificate, $profile, (Join-Path $temporary 'GameFlow.zip'))) {
        Remove-Item -LiteralPath $file -ErrorAction SilentlyContinue
    }
}
