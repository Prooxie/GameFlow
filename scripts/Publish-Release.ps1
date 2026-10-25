#Requires -Version 7.3
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'linux-x64', 'osx-arm64', 'osx-x64')]
    [string[]]$Runtime = @('win-x64', 'linux-x64', 'osx-arm64', 'osx-x64'),
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
[xml]$properties = Get-Content -LiteralPath (Join-Path $repository 'Directory.Build.props') -Raw
$version = $properties.SelectSingleNode('/Project/PropertyGroup/Version').InnerText
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'The release version must have three numeric parts.' }
if (!$OutputDirectory) { $OutputDirectory = Join-Path $repository "artifacts/releases/$version" }
$destination = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $destination -Force | Out-Null

Push-Location $repository
try {
    foreach ($identifier in $Runtime) {
        $publishDirectory = Join-Path $destination "publish/$identifier"
        if (Test-Path -LiteralPath $publishDirectory) { throw "The publish directory already exists: $publishDirectory" }
        & dotnet publish src/GameFlow.App/GameFlow.App.csproj -c Release -r $identifier --self-contained true `
            -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false `
            -p:RestoreLockedMode=true "-p:Version=$version" -o $publishDirectory
        if ($LASTEXITCODE -ne 0) { throw "Publish failed for $identifier." }
        & (Join-Path $PSScriptRoot 'Prepare-Release.ps1') -PublishDirectory $publishDirectory `
            -AssetsFile (Join-Path $repository 'src/GameFlow.App/obj/project.assets.json')
        if ($identifier.StartsWith('osx-')) {
            & (Join-Path $PSScriptRoot 'Prepare-MacBundle.ps1') -PublishDirectory $publishDirectory -Version $version
        }

        $archive = Join-Path $destination "GameFlow-$version-$identifier"
        if ($identifier -eq 'win-x64') {
            $archive += '.zip'
            Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $archive -CompressionLevel Optimal
        }
        else {
            $archive += '.tar.gz'
            $archiveStream = [IO.File]::Create($archive)
            $gzip = [IO.Compression.GZipStream]::new($archiveStream, [IO.Compression.CompressionLevel]::Optimal)
            $writer = [System.Formats.Tar.TarWriter]::new($gzip, [System.Formats.Tar.TarEntryFormat]::Pax, $true)
            try {
                foreach ($file in Get-ChildItem -LiteralPath $publishDirectory -Recurse -File | Sort-Object FullName) {
                    $relative = [IO.Path]::GetRelativePath($publishDirectory, $file.FullName).Replace('\', '/')
                    $entry = [System.Formats.Tar.PaxTarEntry]::new([System.Formats.Tar.TarEntryType]::RegularFile, $relative)
                    $entry.Mode = [IO.UnixFileMode][Convert]::ToInt32($(if ($relative -eq 'GameFlow' -or $relative -eq 'GameFlow.app/Contents/MacOS/GameFlow') { '755' } else { '644' }), 8)
                    $entry.DataStream = [IO.File]::OpenRead($file.FullName)
                    try { $writer.WriteEntry($entry) }
                    finally { $entry.DataStream.Dispose() }
                }
            }
            finally {
                $writer.Dispose()
                $gzip.Dispose()
                $archiveStream.Dispose()
            }
        }

        $hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
        [IO.File]::WriteAllText("$archive.sha256", "$hash  $([IO.Path]::GetFileName($archive))`n", [Text.UTF8Encoding]::new($false))
        Write-Output "Prepared $archive"
    }
}
finally {
    Pop-Location
}
