#Requires -Version 7.3
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PublishDirectory,
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version
)

$ErrorActionPreference = 'Stop'
$publish = [IO.Path]::GetFullPath($PublishDirectory)
$bundle = Join-Path $publish 'GameFlow.app'
if (Test-Path -LiteralPath $bundle) { throw 'The macOS application bundle already exists.' }
if (!(Test-Path -LiteralPath (Join-Path $publish 'GameFlow') -PathType Leaf)) { throw 'The published macOS executable is missing.' }
$contents = Join-Path $bundle 'Contents'
$binaryDirectory = Join-Path $contents 'MacOS'
if ([IO.Path]::GetRelativePath($publish, $binaryDirectory).Replace('\', '/') -ne 'GameFlow.app/Contents/MacOS') {
    throw 'The application bundle must remain inside the publish directory.'
}

New-Item -ItemType Directory -Path $binaryDirectory -Force | Out-Null
$files = Get-ChildItem -LiteralPath $publish -Force | Where-Object { $_.Name -ne 'GameFlow.app' }
foreach ($file in $files) {
    Move-Item -LiteralPath $file.FullName -Destination $binaryDirectory
}
$plist = @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleIdentifier</key><string>com.proxydarkness.gameflow</string>
<key>CFBundleName</key><string>GameFlow</string>
<key>CFBundleDisplayName</key><string>GameFlow</string>
<key>CFBundleExecutable</key><string>GameFlow</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>CFBundleShortVersionString</key><string>$Version</string>
<key>CFBundleVersion</key><string>$Version</string>
<key>LSMinimumSystemVersion</key><string>12.0</string>
<key>NSHighResolutionCapable</key><true/>
</dict></plist>
"@
[IO.File]::WriteAllText((Join-Path $contents 'Info.plist'), $plist, [Text.UTF8Encoding]::new($false))
if ($IsMacOS) {
    & chmod +x (Join-Path $binaryDirectory 'GameFlow')
    if ($LASTEXITCODE -ne 0) { throw 'Cannot set the application executable permission.' }
}
Write-Output "Prepared $bundle"
