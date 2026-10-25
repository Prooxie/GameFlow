param(
    [Parameter(Mandatory)][string]$PublishDirectory,
    [Parameter(Mandatory)][string]$AssetsFile
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$destination = (Resolve-Path -LiteralPath $PublishDirectory).Path
$assets = Get-Content -LiteralPath $AssetsFile -Raw | ConvertFrom-Json
$licenseDirectory = Join-Path $destination 'licenses'
New-Item -ItemType Directory -Force -Path $licenseDirectory | Out-Null

foreach ($file in @('README.md', 'WIKI.md', 'CHANGELOG.md', 'LICENSE', 'THIRD-PARTY-NOTICES.md')) {
    Copy-Item -LiteralPath (Join-Path $repository $file) -Destination $destination
}
Copy-Item -LiteralPath (Join-Path $repository 'licenses') -Destination $destination -Recurse -Force
$publicDocs = Join-Path $destination 'docs'
New-Item -ItemType Directory -Path $publicDocs -Force | Out-Null
foreach ($file in @('index.html', '.nojekyll', 'asset-audit.json', 'asset-pack.md', 'localization.md', 'obs-inputs.md', 'release.md')) {
    Copy-Item -LiteralPath (Join-Path $repository "docs/$file") -Destination $publicDocs
}
foreach ($folder in @('images', 'brand')) {
    Copy-Item -LiteralPath (Join-Path $repository "docs/$folder") -Destination $publicDocs -Recurse -Force
}
Copy-Item -LiteralPath (Join-Path $repository 'brand') -Destination $destination -Recurse -Force
$linuxSetup = Join-Path $destination 'platform/linux'
New-Item -ItemType Directory -Path $linuxSetup -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repository 'platform/linux/70-gameflow-uinput.rules') -Destination $linuxSetup

$inventory = foreach ($library in $assets.libraries.PSObject.Properties | Sort-Object Name) {
    if ($library.Value.type -ne 'package') { continue }
    $id, $version = $library.Name.Split('/')
    $packageDirectory = $null
    foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
        $candidate = Join-Path $folder $library.Value.path
        if (Test-Path -LiteralPath $candidate -PathType Container) {
            $packageDirectory = $candidate
            break
        }
    }
    if (!$packageDirectory) { throw "Restored package missing: $($library.Name)" }
    $packageNotices = Join-Path $licenseDirectory "$id-$version"
    New-Item -ItemType Directory -Force -Path $packageNotices | Out-Null
    $nuspec = Get-ChildItem -LiteralPath $packageDirectory -Filter '*.nuspec' | Select-Object -First 1
    [xml]$metadata = Get-Content -LiteralPath $nuspec.FullName -Raw
    Copy-Item -LiteralPath $nuspec.FullName -Destination $packageNotices
    foreach ($notice in Get-ChildItem -LiteralPath $packageDirectory -Recurse -File |
            Where-Object { $_.Name -match '^(LICENSE|COPYING|NOTICE|THIRD.PARTY|ThirdPartyNotices)' }) {
        $relative = [IO.Path]::GetRelativePath($packageDirectory, $notice.FullName)
        $target = Join-Path $packageNotices $relative
        New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null
        Copy-Item -LiteralPath $notice.FullName -Destination $target
    }
    [ordered]@{
        id = $id
        version = $version
        license = $metadata.package.metadata.license.InnerText
        licenseUrl = $metadata.package.metadata.licenseUrl
        packageUrl = "https://www.nuget.org/packages/$id/$version"
    }
}
$inventory | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $destination 'dependencies.json') -Encoding utf8NoBOM

# Framework runtime packs are downloads rather than normal entries in libraries.
# Their notices also cover many native components embedded in the single file.
foreach ($framework in $assets.project.frameworks.PSObject.Properties) {
    foreach ($runtimePack in $framework.Value.downloadDependencies | Where-Object { $_.name -match '^Microsoft\..*\.App\.' }) {
        $runtimeId = $runtimePack.name
        $runtimeVersion = $runtimePack.version.Trim('[', ']').Split(',')[0].Trim()
        foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
            $runtimeDirectory = Join-Path $folder "$($runtimeId.ToLowerInvariant())/$runtimeVersion"
            if (!(Test-Path -LiteralPath $runtimeDirectory)) { continue }
            $targetDirectory = Join-Path $licenseDirectory "$runtimeId-$runtimeVersion"
            New-Item -ItemType Directory -Force -Path $targetDirectory | Out-Null
            foreach ($notice in Get-ChildItem -LiteralPath $runtimeDirectory -File |
                    Where-Object { $_.Name -match '^(LICENSE|THIRD.PARTY)' }) {
                Copy-Item -LiteralPath $notice.FullName -Destination $targetDirectory
            }
            break
        }
    }
}

# These symbols alone add around 100 MB to the Windows archive; they aren't
# needed to run the app. Managed symbols remain useful for support stack traces.
Get-ChildItem -LiteralPath $destination -Filter 'lib*.pdb' -File | Remove-Item
Write-Output "Prepared release with $(@($inventory).Count) package records and available upstream notices."
