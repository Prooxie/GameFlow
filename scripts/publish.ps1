param(
    [Parameter(Mandatory = $true)]
    [string]$RuntimeIdentifier,

    [switch]$SelfContained
)

$publishDir = Join-Path $PSScriptRoot "..\artifacts\publish\$RuntimeIdentifier"
$publishDir = [System.IO.Path]::GetFullPath($publishDir)

dotnet publish `
    (Join-Path $PSScriptRoot "..\src\Autofire.App\Autofire.App.csproj") `
    -c Release `
    -r $RuntimeIdentifier `
    --self-contained:$SelfContained `
    -p:PublishSingleFile=true `
    -p:DebugType=embedded `
    -o $publishDir
