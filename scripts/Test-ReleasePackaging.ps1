#Requires -Version 7.3
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$publicMarkdown = @('README.md', 'WIKI.md', 'CHANGELOG.md', 'THIRD-PARTY-NOTICES.md',
    'docs/asset-pack.md', 'docs/localization.md', 'docs/obs-inputs.md', 'docs/release.md',
    'src/GameFlow.App/Assets/ControllerOverlays/README.md')
foreach ($tracked in & git -C $repository ls-files '*.md') {
    if ($tracked -notin $publicMarkdown) { throw "Git tracks a non-public document: $tracked" }
}
$fixture = Join-Path $repository ('.cache/release-packaging-' + [guid]::NewGuid().ToString('N'))
$source = Join-Path $fixture 'source'
$publish = Join-Path $fixture 'publish'
New-Item -ItemType Directory -Force -Path (Join-Path $source 'scripts'), $publish | Out-Null
Get-ChildItem -LiteralPath $repository -Filter '*.md' -File | Copy-Item -Destination $source
foreach ($name in @('docs', 'brand', 'licenses')) {
    Copy-Item -LiteralPath (Join-Path $repository $name) -Destination $source -Recurse
}
Copy-Item -LiteralPath (Join-Path $repository 'LICENSE') -Destination $source
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Prepare-Release.ps1') -Destination (Join-Path $source 'scripts')
[IO.File]::WriteAllText((Join-Path $source 'INTERNAL.md'), 'Local working notes.')
[IO.File]::WriteAllText((Join-Path $source 'docs/internal-sentinel.md'), 'Local working notes.')
New-Item -ItemType Directory -Path (Join-Path $source 'docs/private') -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $source 'docs/private/internal-sentinel.md'), 'Local working notes.')

& (Join-Path $source 'scripts/Prepare-Release.ps1') -PublishDirectory $publish `
    -AssetsFile (Join-Path $repository 'src/GameFlow.App/obj/project.assets.json')

$rootDocuments = @('README.md', 'WIKI.md', 'CHANGELOG.md', 'THIRD-PARTY-NOTICES.md')
$publicDocuments = @('asset-pack.md', 'localization.md', 'obs-inputs.md', 'release.md')
foreach ($file in Get-ChildItem -LiteralPath $publish -Recurse -File) {
    $relative = [IO.Path]::GetRelativePath($publish, $file.FullName).Replace('\', '/')
    if ($relative.StartsWith('docs/')) {
        $name = $relative.Substring(5)
        if ($name -notin ($publicDocuments + @('index.html', '.nojekyll', 'asset-audit.json')) `
            -and !$name.StartsWith('images/') -and !$name.StartsWith('brand/')) {
            throw "The archive contains a non-public document: $relative"
        }
    }
    elseif ($file.DirectoryName -eq $publish -and $file.Extension -eq '.md' -and $file.Name -notin $rootDocuments) {
        throw "The archive contains a non-public document: $relative"
    }
}
foreach ($required in @('README.md', 'docs/index.html', 'docs/images/dashboard.png', 'docs/release.md', 'dependencies.json')) {
    if (!(Test-Path -LiteralPath (Join-Path $publish $required))) { throw "The archive omits $required" }
}
Write-Output 'PASS: Public release documents and screenshots are complete. Local working documents are excluded.'
$global:LASTEXITCODE = 0
