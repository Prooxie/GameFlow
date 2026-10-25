#Requires -Version 7.3
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BundleDirectory,
    [Parameter(Mandatory)][string]$Identity,
    [Parameter(Mandatory)][string]$ProvisioningProfile
)

$ErrorActionPreference = 'Stop'
if (!$IsMacOS) { throw 'Apple code signing requires macOS.' }
$bundle = [IO.Path]::GetFullPath($BundleDirectory)
$contents = Join-Path $bundle 'Contents'
if (!(Test-Path -LiteralPath (Join-Path $contents 'Info.plist'))) { throw 'The application bundle is missing.' }
$profileXml = & security cms -D -i $ProvisioningProfile
if ($LASTEXITCODE -ne 0) { throw 'Cannot decode the provisioning profile.' }
[xml]$profile = $profileXml -join "`n"
$entitlements = $profile.SelectSingleNode('/plist/dict/key[text()="Entitlements"]/following-sibling::*[1]')
$virtualHid = $entitlements.SelectSingleNode('key[text()="com.apple.developer.hid.virtual.device"]/following-sibling::*[1]')
if ($virtualHid.Name -ne 'true') { throw 'Apple has not granted virtual HID access in this provisioning profile.' }
$applicationId = $entitlements.SelectSingleNode('key[text()="com.apple.application-identifier"]/following-sibling::*[1]')
if (!$applicationId) { throw 'The provisioning profile has no application identifier.' }
[xml]$information = Get-Content -LiteralPath (Join-Path $contents 'Info.plist') -Raw
$bundleId = $information.SelectSingleNode('/plist/dict/key[text()="CFBundleIdentifier"]/following-sibling::*[1]')
if (!$applicationId.InnerText.EndsWith(".$($bundleId.InnerText)", [StringComparison]::Ordinal)) {
    throw 'The provisioning profile does not match the application bundle identifier.'
}
$expiration = $profile.SelectSingleNode('/plist/dict/key[text()="ExpirationDate"]/following-sibling::*[1]')
if (!$expiration -or [DateTimeOffset]::Parse($expiration.InnerText) -le [DateTimeOffset]::UtcNow) {
    throw 'The provisioning profile has expired.'
}
Copy-Item -LiteralPath $ProvisioningProfile -Destination (Join-Path $contents 'embedded.provisionprofile')
$signingFile = Join-Path ([IO.Path]::GetTempPath()) "gameflow-entitlements-$([guid]::NewGuid().ToString('N')).plist"
try {
    $signingXml = @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>com.apple.developer.hid.virtual.device</key><true/>
<key>com.apple.application-identifier</key><string>$($applicationId.InnerText)</string>
<key>com.apple.security.cs.allow-jit</key><true/>
<key>com.apple.security.cs.allow-unsigned-executable-memory</key><true/>
<key>com.apple.security.cs.disable-library-validation</key><true/>
</dict></plist>
"@
    [IO.File]::WriteAllText($signingFile, $signingXml, [Text.UTF8Encoding]::new($false))
    foreach ($library in Get-ChildItem -LiteralPath (Join-Path $contents 'MacOS') -Filter '*.dylib' -Recurse -File) {
        & codesign --force --timestamp --options runtime --sign $Identity $library.FullName
        if ($LASTEXITCODE -ne 0) { throw "Cannot sign $($library.Name)." }
    }
    & codesign --force --timestamp --options runtime --entitlements $signingFile --sign $Identity $bundle
    if ($LASTEXITCODE -ne 0) { throw 'Cannot sign the macOS application bundle.' }
    & codesign --verify --deep --strict --verbose=2 $bundle
    if ($LASTEXITCODE -ne 0) { throw 'The macOS application signature is invalid.' }
}
finally {
    Remove-Item -LiteralPath $signingFile -ErrorAction SilentlyContinue
}
