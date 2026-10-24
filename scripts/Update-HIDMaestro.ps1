#Requires -Version 7.0
<#
.SYNOPSIS
Updates the local HIDMaestro clone, writes a change report for GameFlow, builds
HIDMaestro.Core and copies it to where GameFlow looks for it at runtime.

.DESCRIPTION
The script runs six steps. Each step that changes something honours -WhatIf
and -Confirm.

1. Update. Checks that the HIDMaestro clone is a git repository whose tracked
   files have no uncommitted changes, records HEAD, runs git fetch and
   fast-forwards to the tip of the default branch, or to -Ref. It refuses a
   dirty tree and anything that is not a fast-forward. It never commits,
   pushes, resets or checks out anything.

2. Report. Writes docs/hidmaestro/CHANGES-<from>-<to>.md in the GameFlow repo
   and the same text to docs/hidmaestro/LATEST.md. The report holds the
   commits in the range, the changed files grouped by area, the changes to
   public declarations in sdk/HIDMaestro.Core, the profile changes, and a
   section that maps GameFlow files to the HIDMaestro types they use. The
   report is read from git objects, so it does not depend on the working tree.
   docs/hidmaestro-update.md describes it.

3. Build. Runs "dotnet build -c Release" on sdk/HIDMaestro.Core in the clone.
   HIDMaestro.Core embeds a native driver payload from the clone's build/
   folder, so the managed build only works after that payload exists. With
   -BuildNative the script runs the clone's scripts\build_all.cmd instead,
   which builds the payload and then the SDK. That needs Visual Studio with the
   C++ tools and the Windows Driver Kit (see docs/design/coexistence.md in the
   clone). The environment variables HMSkipArm64=true and HMSkipUsbip=true are
   passed through to both builds.

4. Version check. Reads the assembly version of the DLL and stops unless it is
   in the range GameFlow accepts. The range comes from
   src/GameFlow.HidMaestro/HidMaestroSdkLocator.cs (today 2.0.1 up to, not
   including, 3.0.0). -Force copies anyway.

5. Copy. Copies HIDMaestro.Core.dll, its .pdb when there is one, and any
   referenced assembly that is not part of .NET into a HIDMaestro subfolder
   next to every GameFlow.exe under src/GameFlow.App/bin (Debug or
   Release, any target framework or runtime identifier), and into each
   -Destination folder. Folders named "publish" are skipped unless
   -IncludePublish is given, so the SDK does not end up in a release package
   by accident. The pinned compile-time copy in .cache/hidmaestro is not
   touched unless -UpdateBuildCache is given.

   GameFlow searches GAMEFLOW_HIDMAESTRO_DIR first, then the folder of
   GameFlow.exe, then its HIDMaestro subfolder, and uses the first
   HIDMaestro.Core.dll it finds. An older copy right next to GameFlow.exe
   therefore hides the new copy in the subfolder. The script warns about such
   copies and replaces them only with -OverwriteAppFolderCopy.

6. Summary. Prints the commit range, the DLL version, where it was copied and
   the report path.

.PARAMETER HidMaestroPath
The HIDMaestro clone. Default: a HIDMaestro folder next to the GameFlow repo
(..\HIDMaestro relative to the repo root).

.PARAMETER Ref
A tag, branch or commit to move to instead of the tip of the default branch,
for example v2.0.3. The move must still be a fast-forward from the current
HEAD. Cannot be combined with -SkipUpdate.

.PARAMETER ReportFrom
The commit, tag or branch the report starts from. Default: the HEAD the clone
had before the update. When the update moved nothing, the default is the tag
of the version GameFlow pins (for example v2.0.1), so the report still shows
what changed since the version GameFlow was written against. Use
"-ReportFrom v2.0.1 -SkipUpdate" to write that cumulative report at any time.

.PARAMETER SkipUpdate
Do not fetch and do not fast-forward. The script works with the clone's
current HEAD.

.PARAMETER SkipReport
Do not write the report.

.PARAMETER SkipBuild
Do not build. The version check and the copy then use the existing build
output in the clone, if there is one. Without any build output they are
skipped.

.PARAMETER BuildNative
Run the clone's scripts\build_all.cmd, which builds the native driver payload
and then HIDMaestro.Core, instead of building only the managed project.

.PARAMETER SdkDll
Use this HIDMaestro.Core.dll instead of building one, for example the DLL from
an official release zip on a machine without the driver kit. The version check
and the copy still run. Implies -SkipBuild.

.PARAMETER SkipCopy
Do not copy anything into GameFlow.

.PARAMETER Destination
Extra folders to copy the SDK files into, in addition to the build outputs.
The files go straight into each folder, not into a HIDMaestro subfolder. A
folder that does not exist is created.

.PARAMETER IncludePublish
Also copy next to GameFlow.exe files inside "publish" folders.

.PARAMETER OverwriteAppFolderCopy
When a HIDMaestro.Core.dll already sits right next to GameFlow.exe, replace
it as well. Without this switch the script leaves it alone and warns, because
GameFlow loads that copy before it looks in the HIDMaestro subfolder.

.PARAMETER UpdateBuildCache
Also overwrite .cache/hidmaestro/<pinned version>/HIDMaestro.Core.dll, the
copy GameFlow compiles against. Use it to try GameFlow's adapter against the
new SDK before moving the pin. The trade-offs:
- The cache then holds a DLL that matches neither the pinned version nor the
  SHA-256 in build/HidMaestroSdk.props, and the build does not notice,
  because it only downloads the release when the file is missing.
- tests/GameFlow.HidMaestro.Tests/SdkPinTests.cs fails, because it expects the
  pinned version.
- CI still compiles against the pinned release, so code that builds locally
  can fail there.
To go back, delete the .cache/hidmaestro/<pinned version> folder and build
GameFlow again; the build downloads the pinned release. To move the pin for
real, change build/HidMaestroSdk.props and SdkPinTests instead (the report
lists the files).

.PARAMETER Force
Copy the DLL even when its version is outside the range GameFlow accepts.
GameFlow will then report the SDK as unsupported at runtime.

.EXAMPLE
./scripts/Update-HIDMaestro.ps1 -WhatIf

Shows what a full run would do without changing anything. Under -WhatIf the
script does not fetch, so the remote state is the one from the last fetch.

.EXAMPLE
./scripts/Update-HIDMaestro.ps1 -SkipBuild -SkipCopy

Fetches, fast-forwards the clone and writes the report only.

.EXAMPLE
./scripts/Update-HIDMaestro.ps1 -BuildNative

Fetches, fast-forwards, writes the report, builds the native payload and the
SDK, checks the version and copies the DLL into GameFlow's build outputs.

.EXAMPLE
./scripts/Update-HIDMaestro.ps1 -SkipUpdate -ReportFrom v2.0.1 -SkipBuild -SkipCopy

Writes a report of everything that changed since the version GameFlow pins,
without touching the clone.

.EXAMPLE
./scripts/Update-HIDMaestro.ps1 -SkipUpdate -SkipReport -SdkDll C:\Downloads\HIDMaestro-v2.0.3\HIDMaestro.Core.dll

Version-checks a DLL from a release zip and copies it into the build outputs.

.NOTES
Requires PowerShell 7, git and the .NET SDK on PATH. The script reads the
GameFlow repo but writes only docs/hidmaestro/*.md, the HIDMaestro subfolders
of the build outputs, the -Destination folders and, with -UpdateBuildCache,
the .cache copy. In the HIDMaestro clone it runs git fetch, git merge
--ff-only and the build, nothing else.
#>
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'Medium')]
param(
    [string]$HidMaestroPath = (Join-Path $PSScriptRoot '..\..\HIDMaestro'),
    [string]$Ref,
    [string]$ReportFrom,
    [switch]$SkipUpdate,
    [switch]$SkipReport,
    [switch]$SkipBuild,
    [switch]$BuildNative,
    [string]$SdkDll,
    [switch]$SkipCopy,
    [string[]]$Destination = @(),
    [switch]$IncludePublish,
    [switch]$OverwriteAppFolderCopy,
    [switch]$UpdateBuildCache,
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

$script:Cmdlet = $PSCmdlet
$script:Warnings = [System.Collections.Generic.List[string]]::new()
$script:Utf8NoBom = [System.Text.UTF8Encoding]::new($false)
$script:HmRoot = $null
$script:GitExe = $null

#region Helpers

function Write-Step {
    param([Parameter(Mandatory)][string]$Message)
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Write-Detail {
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Message)
    Write-Host "    $Message"
}

function Add-RunWarning {
    param([Parameter(Mandatory)][string]$Message)
    $script:Warnings.Add($Message)
    Write-Warning $Message
}

function Resolve-Tool {
    param([Parameter(Mandatory)][string]$Name)
    $command = Get-Command -Name $Name -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $command) {
        throw "$Name was not found on PATH. Install it or add it to PATH, then run the script again."
    }
    $command.Source
}

# Runs a program with exact arguments (no shell quoting) and returns its exit
# code and output. With -Stream the output goes straight to the console.
function Invoke-Tool {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][string[]]$ArgumentList,
        [string]$WorkingDirectory,
        [switch]$Stream
    )
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new($FilePath)
    foreach ($argument in $ArgumentList) { $startInfo.ArgumentList.Add($argument) }
    $startInfo.UseShellExecute = $false
    if ($WorkingDirectory) { $startInfo.WorkingDirectory = $WorkingDirectory }
    $startInfo.Environment['GIT_TERMINAL_PROMPT'] = '0'
    if (-not $Stream) {
        $startInfo.RedirectStandardOutput = $true
        $startInfo.RedirectStandardError = $true
        $startInfo.RedirectStandardInput = $true
        $startInfo.StandardOutputEncoding = $script:Utf8NoBom
        $startInfo.StandardErrorEncoding = $script:Utf8NoBom
        $startInfo.CreateNoWindow = $true
    }
    $process = [System.Diagnostics.Process]::Start($startInfo)
    try {
        if ($Stream) {
            $process.WaitForExit()
            return [pscustomobject]@{ ExitCode = $process.ExitCode; Output = ''; Error = '' }
        }
        $process.StandardInput.Close()
        $outputTask = $process.StandardOutput.ReadToEndAsync()
        $errorTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        [pscustomobject]@{
            ExitCode = $process.ExitCode
            Output   = $outputTask.GetAwaiter().GetResult()
            Error    = $errorTask.GetAwaiter().GetResult()
        }
    }
    finally {
        $process.Dispose()
    }
}

function Invoke-Git {
    param(
        [Parameter(Mandatory, Position = 0)][string[]]$Arguments,
        [string]$Repository = $script:HmRoot,
        [int[]]$AllowedExitCodes = @(0)
    )
    $all = @('-C', $Repository, '-c', 'core.quotepath=false', '-c', 'color.ui=false', '-c', 'log.showSignature=false') + $Arguments
    $result = Invoke-Tool -FilePath $script:GitExe -ArgumentList $all
    if ($result.ExitCode -notin $AllowedExitCodes) {
        $detail = (@($result.Error.Trim(), $result.Output.Trim()) | Where-Object { $_ }) -join ' '
        throw "git $($Arguments -join ' ') failed in '$Repository' with exit code $($result.ExitCode). $detail"
    }
    $result
}

# Output lines of a git command. An allowed non-zero exit code gives no lines.
function Get-GitLines {
    param(
        [Parameter(Mandatory, Position = 0)][string[]]$Arguments,
        [string]$Repository = $script:HmRoot,
        [int[]]$AllowedExitCodes = @(0)
    )
    $result = Invoke-Git $Arguments -Repository $Repository -AllowedExitCodes $AllowedExitCodes
    if ($result.ExitCode -ne 0) { return @() }
    @($result.Output -split "`r?`n" | Where-Object { $_ -ne '' })
}

# First output line of a git command, or $null when it fails.
function Get-GitValue {
    param(
        [Parameter(Mandatory, Position = 0)][string[]]$Arguments,
        [string]$Repository = $script:HmRoot
    )
    $result = Invoke-Git $Arguments -Repository $Repository -AllowedExitCodes @(0..255)
    if ($result.ExitCode -ne 0) { return $null }
    $line = @($result.Output -split "`r?`n" | Where-Object { $_ -ne '' }) | Select-Object -First 1
    if ($null -eq $line) { return $null }
    $line.Trim()
}

function Resolve-Commit {
    param([Parameter(Mandatory)][string]$Name, [string]$Remote)
    $candidates = @($Name)
    if ($Remote) { $candidates += "$Remote/$Name" }
    foreach ($candidate in $candidates) {
        $sha = Get-GitValue @('rev-parse', '--verify', '--quiet', "$candidate^{commit}")
        if ($sha) { return $sha }
    }
    $null
}

function Get-GitFileText {
    param([Parameter(Mandatory)][string]$Rev, [Parameter(Mandatory)][string]$Path)
    $result = Invoke-Git @('show', "${Rev}:$Path") -AllowedExitCodes @(0, 128)
    if ($result.ExitCode -ne 0) { return '' }
    $result.Output
}

function Get-ShortSha {
    param([Parameter(Mandatory)][string]$Sha)
    $Sha.Substring(0, [Math]::Min(7, $Sha.Length))
}

function Test-SamePath {
    param([string]$Left, [string]$Right)
    $a = [System.IO.Path]::GetFullPath($Left).TrimEnd('\', '/')
    $b = [System.IO.Path]::GetFullPath($Right).TrimEnd('\', '/')
    [string]::Equals($a, $b, [StringComparison]::OrdinalIgnoreCase)
}

function Test-PathInside {
    param([string]$Path, [string]$Folder)
    $p = [System.IO.Path]::GetFullPath($Path).TrimEnd('\', '/') + '\'
    $f = [System.IO.Path]::GetFullPath($Folder).TrimEnd('\', '/') + '\'
    $p.StartsWith($f, [StringComparison]::OrdinalIgnoreCase)
}

function Get-RelativePath {
    param([string]$Root, [string]$Path)
    [System.IO.Path]::GetRelativePath($Root, $Path).Replace('\', '/')
}

# Major.minor.build only; an unset build counts as 0. Matches
# HidMaestroSdkLocator.Normalize.
function Get-NormalizedVersion {
    param([Parameter(Mandatory)][version]$Version)
    [version]::new($Version.Major, $Version.Minor, [Math]::Max($Version.Build, 0))
}

function Format-Cell {
    param([AllowNull()][object]$Value)
    if ($null -eq $Value) { return '' }
    ("$Value" -replace '\|', '\|') -replace "`r?`n", ' '
}

function Format-Code {
    param([AllowNull()][string]$Value)
    if (-not $Value) { return '' }
    '`{0}`' -f $Value
}

#endregion

#region Source scanning

$script:TypeDeclarationPattern = '^(?:\[[^\]]*\]\s*)*(?<mods>(?:(?:public|internal|private|protected|file|static|sealed|abstract|partial|readonly|ref|unsafe|new)\s+)*)(?<kind>record\s+struct|record\s+class|class|struct|interface|enum|record)\s+(?<name>[A-Za-z_]\w*)'
$script:DelegatePattern = '^(?:\[[^\]]*\]\s*)*(?<mods>(?:(?:public|internal|private|protected|file|static|unsafe|new)\s+)*)delegate\s+.+?\s+(?<name>[A-Za-z_]\w*)\s*(?:<[^>]*>)?\s*\('
$script:StringLiteralPattern = '@"(?:[^"]|"")*"|"(?:[^"\\]|\\.)*"|''(?:[^''\\]|\\.)*'''

# Replaces the contents of string and char literals with underscores, keeping
# the length, so braces and comment markers inside literals are not counted.
function Get-MaskedLine {
    param([AllowEmptyString()][string]$Line)
    $evaluator = [System.Text.RegularExpressions.MatchEvaluator] {
        param($match)
        $value = $match.Value
        '{0}{1}{2}' -f $value[0], ('_' * ($value.Length - 2)), $value[$value.Length - 1]
    }
    [regex]::Replace($Line, $script:StringLiteralPattern, $evaluator)
}

function Format-Declaration {
    param([Parameter(Mandatory)][string]$Code)
    $s = ($Code -replace '\s+', ' ').Trim()
    $paren = 0
    $brace = 0
    $sawParen = $false
    for ($k = 0; $k -lt $s.Length; $k++) {
        $ch = $s[$k]
        if ($ch -eq '(') { $paren++; $sawParen = $true }
        elseif ($ch -eq ')') { $paren-- }
        elseif ($paren -eq 0) {
            if ($ch -eq '{') {
                if ($sawParen) { return $s.Substring(0, $k).TrimEnd() }
                $brace++
            }
            elseif ($ch -eq '}') { $brace-- }
            elseif ($ch -eq '=' -and $brace -eq 0) {
                $previous = if ($k -gt 0) { [string]$s[$k - 1] } else { ' ' }
                $next = if ($k + 1 -lt $s.Length) { [string]$s[$k + 1] } else { ' ' }
                if ($previous -notin @('=', '!', '<', '>') -and $next -ne '=') {
                    return $s.Substring(0, $k).TrimEnd()
                }
            }
        }
    }
    ($s -replace '\s*\{\s*$', '').TrimEnd(';').TrimEnd()
}

# Lists the public declarations of a C# file: public types, public members of
# public types, and enum members. A light line-based reader, not a compiler;
# good enough to show what changed in an API between two commits.
function ConvertTo-ApiEntries {
    param([AllowEmptyString()][string]$Text)
    $entries = [System.Collections.Generic.List[object]]::new()
    if (-not $Text) { return , $entries }

    $rawLines = $Text -split "`r?`n"
    $count = $rawLines.Count
    $codeLines = [string[]]::new($count)
    $maskLines = [string[]]::new($count)
    $inBlock = $false
    for ($i = 0; $i -lt $count; $i++) {
        $code = $rawLines[$i]
        $mask = Get-MaskedLine $code
        if ($inBlock) {
            $end = $mask.IndexOf('*/')
            if ($end -lt 0) { $codeLines[$i] = ''; $maskLines[$i] = ''; continue }
            $code = (' ' * ($end + 2)) + $code.Substring($end + 2)
            $mask = (' ' * ($end + 2)) + $mask.Substring($end + 2)
            $inBlock = $false
        }
        while ($true) {
            $lineComment = $mask.IndexOf('//')
            $blockStart = $mask.IndexOf('/*')
            if ($blockStart -ge 0 -and ($lineComment -lt 0 -or $blockStart -lt $lineComment)) {
                $blockEnd = $mask.IndexOf('*/', $blockStart + 2)
                if ($blockEnd -lt 0) {
                    $code = $code.Substring(0, $blockStart)
                    $mask = $mask.Substring(0, $blockStart)
                    $inBlock = $true
                    break
                }
                $blank = ' ' * ($blockEnd + 2 - $blockStart)
                $code = $code.Substring(0, $blockStart) + $blank + $code.Substring($blockEnd + 2)
                $mask = $mask.Substring(0, $blockStart) + $blank + $mask.Substring($blockEnd + 2)
                continue
            }
            if ($lineComment -ge 0) {
                $code = $code.Substring(0, $lineComment)
                $mask = $mask.Substring(0, $lineComment)
            }
            break
        }
        $codeLines[$i] = $code
        $maskLines[$i] = $mask
    }

    $namespace = ''
    $depth = 0
    $frames = [System.Collections.Generic.List[object]]::new()
    $pendingFrame = $null
    $pendingAttribute = ''
    for ($i = 0; $i -lt $count; $i++) {
        $code = $codeLines[$i].Trim()
        $mask = $maskLines[$i]
        if ($code -eq '') { continue }

        # Join a declaration whose parameter list continues on later lines.
        $start = $i
        while (($mask.Split('(').Count - $mask.Split(')').Count) -gt 0 -and $i + 1 -lt $count -and ($i - $start) -lt 40) {
            $i++
            $code = $code + ' ' + $codeLines[$i].Trim()
            $mask = $mask + ' ' + $maskLines[$i]
        }

        $typeFrame = $null
        $publicContext = $true
        foreach ($frame in $frames) {
            if ($frame.IsType) {
                $typeFrame = $frame
                if (-not $frame.Public) { $publicContext = $false }
            }
        }
        $atTop = $null -eq $typeFrame
        $atMember = (-not $atTop) -and $depth -eq $typeFrame.Depth
        $isAttribute = $false
        $typeMatch = $null
        if ($atTop -or $atMember) { $typeMatch = [regex]::Match($code, $script:TypeDeclarationPattern) }

        if ($code -match '^namespace\s+([\w.]+)') {
            $namespace = $Matches[1]
            if ($mask -notmatch ';\s*$') {
                $pendingFrame = [pscustomobject]@{ Name = $namespace; IsType = $false; Public = $true; IsEnum = $false; Depth = 0 }
            }
        }
        elseif ($null -ne $typeMatch -and $typeMatch.Success) {
            $name = $typeMatch.Groups['name'].Value
            $qualified = if ($typeFrame) { "$($typeFrame.Name).$name" } else { $name }
            $isPublic = $publicContext -and ($typeMatch.Groups['mods'].Value -match '\bpublic\b')
            if ($isPublic) {
                $entries.Add([pscustomobject]@{ Namespace = $namespace; Type = $qualified; Declaration = $pendingAttribute + (Format-Declaration $code) })
            }
            $pendingFrame = [pscustomobject]@{
                Name   = $qualified
                IsType = $true
                Public = $isPublic
                IsEnum = $typeMatch.Groups['kind'].Value -eq 'enum'
                Depth  = 0
            }
        }
        elseif ($atMember -and $typeFrame.IsEnum) {
            if ($typeFrame.Public -and $publicContext) {
                foreach ($part in ($code -split ',')) {
                    $item = ($part -replace '^\s*(?:\[[^\]]*\]\s*)*', '').Trim().TrimEnd('}').Trim()
                    if ($item -match '^(?<name>[A-Za-z_]\w*)\s*(?:=\s*(?<value>.+))?$') {
                        $declaration = if ($Matches['value']) { "$($Matches['name']) = $($Matches['value'].Trim())" } else { $Matches['name'] }
                        $entries.Add([pscustomobject]@{ Namespace = $namespace; Type = $typeFrame.Name; Declaration = $declaration })
                    }
                }
            }
        }
        elseif (($atTop -or $atMember) -and $code -match '^\[\s*Obsolete\b') {
            $pendingAttribute = ($code -replace '\s+', ' ') + ' '
            $isAttribute = $true
        }
        elseif (($atTop -or $atMember) -and $publicContext -and $code -match '^(?:\[[^\]]*\]\s*)*public\b') {
            $owner = $null
            if ($atMember) { $owner = $typeFrame.Name }
            else {
                $delegate = [regex]::Match($code, $script:DelegatePattern)
                if ($delegate.Success) { $owner = $delegate.Groups['name'].Value }
            }
            if ($owner) {
                $entries.Add([pscustomobject]@{ Namespace = $namespace; Type = $owner; Declaration = $pendingAttribute + (Format-Declaration $code) })
            }
        }
        if (-not $isAttribute) { $pendingAttribute = '' }

        foreach ($ch in $mask.ToCharArray()) {
            if ($ch -eq '{') {
                $depth++
                if ($null -ne $pendingFrame) {
                    $pendingFrame.Depth = $depth
                    $frames.Add($pendingFrame)
                    $pendingFrame = $null
                }
            }
            elseif ($ch -eq '}') {
                if ($frames.Count -gt 0 -and $frames[$frames.Count - 1].Depth -eq $depth) {
                    $frames.RemoveAt($frames.Count - 1)
                }
                $depth--
            }
        }
        if ($null -ne $pendingFrame -and $pendingFrame.IsType -and $mask.TrimEnd().EndsWith(';')) {
            $pendingFrame = $null
        }
    }
    , $entries
}

# Public type names declared in sdk/HIDMaestro.Core at a commit, with the file
# that declares each one.
function Get-PublicTypes {
    param([Parameter(Mandatory)][string]$Rev)
    $lines = Get-GitLines @('grep', '-n', '-I', '-E', 'public[[:space:]].*(class|struct|interface|enum|record|delegate)[[:space:]]', $Rev, '--', 'sdk/HIDMaestro.Core/*.cs') -AllowedExitCodes @(0, 1)
    $types = @{}
    foreach ($line in $lines) {
        if ($line -notmatch '^[^:]+:(?<path>[^:]+):\d+:(?<text>.*)$') { continue }
        $path = $Matches['path']
        $text = $Matches['text'].Trim()
        $match = [regex]::Match($text, $script:TypeDeclarationPattern)
        if (-not $match.Success) { $match = [regex]::Match($text, $script:DelegatePattern) }
        if (-not $match.Success -or $match.Groups['mods'].Value -notmatch '\bpublic\b') { continue }
        $name = $match.Groups['name'].Value
        if (-not $types.ContainsKey($name)) {
            $types[$name] = [pscustomobject]@{
                Name     = $name
                Path     = $path
                Internal = $path -match '/(Internal|ProgramForm)/'
            }
        }
    }
    $types
}

# The controller profile catalog at a commit: id, name and type of every
# profiles/<vendor>/*.json file (models excluded).
function Get-ProfileCatalog {
    param([Parameter(Mandatory)][string]$Rev)
    $lines = Get-GitLines @('grep', '-n', '-o', '-I', '-E', '"(id|name|type)"[[:space:]]*:[[:space:]]*"[^"]*"', $Rev, '--', 'profiles/*/*.json', ':(exclude)profiles/models/*') -AllowedExitCodes @(0, 1)
    $byPath = @{}
    foreach ($line in $lines) {
        if ($line -notmatch '^[^:]+:(?<path>[^:]+):\d+:"(?<key>id|name|type)"\s*:\s*"(?<value>[^"]*)"$') { continue }
        $path = $Matches['path']
        $key = $Matches['key']
        $value = $Matches['value']
        if (-not $byPath.ContainsKey($path)) { $byPath[$path] = @{ id = $null; name = $null; type = $null } }
        if ($null -eq $byPath[$path][$key]) { $byPath[$path][$key] = $value }
    }
    $byId = [System.Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
    foreach ($path in $byPath.Keys) {
        $id = $byPath[$path]['id']
        if ($id -and -not $byId.ContainsKey($id)) { $byId[$id] = $path }
    }
    [pscustomobject]@{ ByPath = $byPath; ById = $byId }
}

function Get-FileArea {
    param([Parameter(Mandatory)][string]$Path)
    switch -Regex ($Path) {
        '^sdk/HIDMaestro\.Core/(Internal|ProgramForm)/.*\.cs$' { return 'SdkInternal' }
        '^sdk/HIDMaestro\.Core/[^/]+\.cs$' { return 'SdkPublic' }
        '^sdk/' { return 'SdkBuild' }
        '^profiles/models/' { return 'ProfileModels' }
        '^profiles/[^/]+/.+\.json$' { return 'Profiles' }
        '^profiles/' { return 'ProfileData' }
        '^driver/' { return 'Driver' }
        '^test/' { return 'Tests' }
        '^example/' { return 'Example' }
        '^docs/|^[^/]+\.md$' { return 'Docs' }
        '^(scripts|tools|\.github)/|^[^/]+\.(props|targets|sln|slnx|json|cmd|ps1)$|^\.git' { return 'BuildTools' }
        default { return 'Other' }
    }
}

$script:AreaTitles = [ordered]@{
    SdkPublic     = 'SDK public API (sdk/HIDMaestro.Core/*.cs, namespace HIDMaestro)'
    SdkInternal   = 'SDK internals (sdk/HIDMaestro.Core/Internal and ProgramForm, namespace HIDMaestro.Internal)'
    SdkBuild      = 'SDK project, resources and code generator (other files under sdk/)'
    Profiles      = 'Controller profiles (profiles/<vendor>/*.json)'
    ProfileModels = 'Profile models (profiles/models)'
    ProfileData   = 'Profile schema and shared data (other files under profiles/)'
    Driver        = 'Native driver (driver/)'
    Tests         = 'Tests (test/)'
    Docs          = 'Docs (docs/ and top-level *.md)'
    Example       = 'Example app (example/)'
    BuildTools    = 'Build scripts, tools and CI'
    Other         = 'Other'
}

$script:StatusNames = @{ A = 'Added'; M = 'Modified'; D = 'Deleted'; R = 'Renamed'; C = 'Copied'; T = 'Type changed'; U = 'Unmerged'; X = 'Unknown' }

function Get-ChangedFiles {
    param([Parameter(Mandatory)][string]$From, [Parameter(Mandatory)][string]$To)
    $files = [System.Collections.Generic.List[object]]::new()
    $tokens = (Invoke-Git @('diff', '--name-status', '-z', '-M', $From, $To)).Output -split "`0"
    $i = 0
    while ($i -lt $tokens.Count) {
        $code = $tokens[$i]
        if ($code -eq '') { $i++; continue }
        $letter = $code.Substring(0, 1)
        if ($letter -in @('R', 'C')) {
            $old = $tokens[$i + 1]; $new = $tokens[$i + 2]; $i += 3
        }
        else {
            $old = $tokens[$i + 1]; $new = $tokens[$i + 1]; $i += 2
        }
        if ($letter -eq 'A') { $old = $null }
        if ($letter -eq 'D') { $new = $null }
        $path = if ($new) { $new } else { $old }
        $files.Add([pscustomobject]@{
                Status  = $letter
                OldPath = $old
                Path    = $path
                Area    = Get-FileArea $path
                Added   = ''
                Deleted = ''
            })
    }

    $stats = @{}
    $numTokens = (Invoke-Git @('diff', '--numstat', '-z', '-M', $From, $To)).Output -split "`0"
    $i = 0
    while ($i -lt $numTokens.Count) {
        $token = $numTokens[$i]
        if ($token -eq '') { $i++; continue }
        $parts = $token -split "`t", 3
        if ($parts.Count -lt 3) { $i++; continue }
        if ($parts[2] -eq '') { $path = $numTokens[$i + 2]; $i += 3 }
        else { $path = $parts[2]; $i++ }
        $stats[$path] = $parts
    }
    foreach ($file in $files) {
        if ($stats.ContainsKey($file.Path)) {
            $file.Added = $stats[$file.Path][0]
            $file.Deleted = $stats[$file.Path][1]
        }
    }
    , $files
}

function Get-Commits {
    param([Parameter(Mandatory)][string]$From, [Parameter(Mandatory)][string]$To)
    $format = '%H%x1f%h%x1f%an%x1f%cd%x1f%D%x1f%s%x1f%b%x1e'
    $output = (Invoke-Git @('log', '--reverse', '--no-merges', '--date=format:%Y-%m-%d %H:%M', "--format=$format", "$From..$To")).Output
    foreach ($record in ($output -split [char]0x1e)) {
        if (-not $record.Trim()) { continue }
        $fields = $record.TrimStart("`r", "`n") -split [char]0x1f
        if ($fields.Count -lt 7) { continue }
        $tags = @(($fields[4] -split ',\s*') | Where-Object { $_ -like 'tag: *' } | ForEach-Object { $_.Substring(5) })
        [pscustomobject]@{
            Sha     = $fields[0]
            Short   = $fields[1]
            Author  = $fields[2]
            Date    = $fields[3]
            Tags    = $tags
            Subject = $fields[5]
            Body    = $fields[6].Trim()
        }
    }
}

function Get-CommitInfo {
    param([Parameter(Mandatory)][string]$Sha)
    $fields = (Invoke-Git @('show', '-s', '--date=format:%Y-%m-%d %H:%M %z', '--format=%h%x1f%cd%x1f%s', $Sha)).Output.Trim() -split [char]0x1f
    $tag = Get-GitValue @('describe', '--tags', '--exact-match', $Sha)
    $describe = Get-GitValue @('describe', '--tags', '--always', $Sha)
    $props = Get-GitFileText -Rev $Sha -Path 'Directory.Build.props'
    $version = if ($props -match '<Version>\s*([^<]+?)\s*</Version>') { $Matches[1] } else { $null }
    $label = if ($tag) { $tag } else { $fields[0] }
    [pscustomobject]@{
        Sha      = $Sha
        Short    = $fields[0]
        Date     = $fields[1]
        Subject  = $fields[2]
        Tag      = $tag
        Describe = $describe
        Version  = $version
        Label    = $label -replace '[\\/:*?"<>|]', '_'
    }
}

# Public declarations that differ between two commits, over the changed .cs
# files of sdk/HIDMaestro.Core, grouped by type.
function Get-ApiChanges {
    param([Parameter(Mandatory)][AllowEmptyCollection()][object[]]$ChangedFiles, [Parameter(Mandatory)][string]$From, [Parameter(Mandatory)][string]$To)
    $fromEntries = @{}
    $toEntries = @{}
    foreach ($file in $ChangedFiles) {
        $paths = @($file.OldPath, $file.Path) | Where-Object { $_ }
        if (-not ($paths | Where-Object { $_ -match '^sdk/HIDMaestro\.Core/.*\.cs$' })) { continue }
        if ($file.OldPath) {
            foreach ($entry in (ConvertTo-ApiEntries (Get-GitFileText -Rev $From -Path $file.OldPath))) {
                $fromEntries["$($entry.Namespace)|$($entry.Type)|$($entry.Declaration)"] = [pscustomobject]@{ Entry = $entry; File = $file.OldPath }
            }
        }
        if ($file.Status -ne 'D') {
            foreach ($entry in (ConvertTo-ApiEntries (Get-GitFileText -Rev $To -Path $file.Path))) {
                $toEntries["$($entry.Namespace)|$($entry.Type)|$($entry.Declaration)"] = [pscustomobject]@{ Entry = $entry; File = $file.Path }
            }
        }
    }
    $groups = [ordered]@{}
    foreach ($side in @(
            @{ Source = $fromEntries; Other = $toEntries; Kind = 'Removed' },
            @{ Source = $toEntries; Other = $fromEntries; Kind = 'Added' })) {
        foreach ($key in ($side.Source.Keys | Sort-Object)) {
            if ($side.Other.ContainsKey($key)) { continue }
            $item = $side.Source[$key]
            $groupKey = "$($item.Entry.Namespace)|$($item.Entry.Type)"
            if (-not $groups.Contains($groupKey)) {
                $groups[$groupKey] = [pscustomobject]@{
                    Namespace = $item.Entry.Namespace
                    Type      = $item.Entry.Type
                    RootType  = ($item.Entry.Type -split '\.')[0]
                    File      = $item.File
                    Removed   = [System.Collections.Generic.List[string]]::new()
                    Added     = [System.Collections.Generic.List[string]]::new()
                }
            }
            $groups[$groupKey].($side.Kind).Add($item.Entry.Declaration)
        }
    }
    @($groups.Values)
}

# Which GameFlow files name which HIDMaestro types (and Type.Member uses).
function Get-GameFlowUsage {
    param([Parameter(Mandatory)][string]$RepoRoot, [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$TypeNames)
    $primaryRoots = @('src/GameFlow.HidMaestro', 'src/GameFlow.Infrastructure/Runtime/HidMaestro', 'tests/GameFlow.HidMaestro.Tests')
    $names = @($TypeNames | Sort-Object { $_.Length } -Descending)
    $usage = [System.Collections.Generic.List[object]]::new()
    $other = [System.Collections.Generic.List[object]]::new()
    if ($names.Count -eq 0) { return [pscustomobject]@{ Primary = $usage; Other = $other; PrimaryRoots = $primaryRoots } }

    $alternation = ($names | ForEach-Object { [regex]::Escape($_) }) -join '|'
    $regex = [regex]::new("\b(?<type>$alternation)\b(?:\s*\.\s*(?<member>[A-Za-z_]\w*))?")
    $hmNames = @($names | Where-Object { $_ -cmatch '^HM[A-Z]' })
    $hmRegex = if ($hmNames.Count) { [regex]::new('\b(?<type>' + (($hmNames | ForEach-Object { [regex]::Escape($_) }) -join '|') + ')\b') } else { $null }

    $primaryFull = @($primaryRoots | ForEach-Object { [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $_)) })
    $allFiles = foreach ($top in @('src', 'tests')) {
        $folder = Join-Path $RepoRoot $top
        if (Test-Path -LiteralPath $folder) {
            Get-ChildItem -LiteralPath $folder -Recurse -File -Filter '*.cs' |
                Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
        }
    }
    foreach ($file in $allFiles) {
        $text = [System.IO.File]::ReadAllText($file.FullName)
        $relative = Get-RelativePath $RepoRoot $file.FullName
        $isPrimary = [bool]($primaryFull | Where-Object { Test-PathInside $file.FullName $_ })
        if ($isPrimary) {
            $types = [ordered]@{}
            foreach ($match in $regex.Matches($text)) {
                $type = $match.Groups['type'].Value
                if (-not $types.Contains($type)) { $types[$type] = [System.Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal) }
                if ($match.Groups['member'].Success) { [void]$types[$type].Add($match.Groups['member'].Value) }
            }
            if ($types.Count) { $usage.Add([pscustomobject]@{ File = $relative; Types = $types }) }
        }
        elseif ($hmRegex) {
            $found = [System.Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal)
            foreach ($match in $hmRegex.Matches($text)) { [void]$found.Add($match.Groups['type'].Value) }
            if ($found.Count) { $other.Add([pscustomobject]@{ File = $relative; Types = @($found) }) }
        }
    }
    [pscustomobject]@{ Primary = $usage; Other = $other; PrimaryRoots = $primaryRoots }
}

# Catalog profile ids that appear as string literals in GameFlow's C# code.
function Get-GameFlowProfileIds {
    param([Parameter(Mandatory)][string]$RepoRoot, [Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.Generic.HashSet[string]]$KnownIds)
    $result = [ordered]@{}
    $regex = [regex]::new('"(?<id>[a-z0-9]+(?:-[a-z0-9]+)+)"')
    foreach ($top in @('src', 'tests')) {
        $folder = Join-Path $RepoRoot $top
        if (-not (Test-Path -LiteralPath $folder)) { continue }
        foreach ($file in Get-ChildItem -LiteralPath $folder -Recurse -File -Filter '*.cs' | Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }) {
            $text = [System.IO.File]::ReadAllText($file.FullName)
            foreach ($match in $regex.Matches($text)) {
                $id = $match.Groups['id'].Value
                if (-not $KnownIds.Contains($id)) { continue }
                if (-not $result.Contains($id)) { $result[$id] = [System.Collections.Generic.SortedSet[string]]::new([StringComparer]::OrdinalIgnoreCase) }
                [void]$result[$id].Add((Get-RelativePath $RepoRoot $file.FullName))
            }
        }
    }
    $result
}

#endregion

#region Report

function New-ChangeReport {
    param(
        [Parameter(Mandatory)]$FromInfo,
        [Parameter(Mandatory)]$ToInfo,
        [Parameter(Mandatory)][hashtable]$Context
    )
    $from = $FromInfo.Sha
    $to = $ToInfo.Sha
    $range = "$($FromInfo.Short)..$($ToInfo.Short)"

    Write-Detail 'Reading commits and changed files'
    $changed = @(Get-ChangedFiles -From $from -To $to)
    $shortStat = (Invoke-Git @('diff', '--shortstat', $from, $to)).Output.Trim()
    $commitCount = [int](Get-GitValue @('rev-list', '--count', "$from..$to"))
    $mergeCount = [int](Get-GitValue @('rev-list', '--count', '--merges', "$from..$to"))
    $commits = @(Get-Commits -From $from -To $to)
    $tagsInRange = @(Get-GitLines @('tag', '--sort=v:refname', '--merged', $to, '--no-merged', $from))
    $fromIsAncestor = (Invoke-Git @('merge-base', '--is-ancestor', $from, $to) -AllowedExitCodes @(0, 1)).ExitCode -eq 0

    Write-Detail 'Reading public types and the profile catalog'
    $typesFrom = Get-PublicTypes -Rev $from
    $typesTo = Get-PublicTypes -Rev $to
    $catalogFrom = Get-ProfileCatalog -Rev $from
    $catalogTo = Get-ProfileCatalog -Rev $to

    Write-Detail 'Comparing public declarations'
    $api = @(Get-ApiChanges -ChangedFiles $changed -From $from -To $to)

    Write-Detail 'Scanning GameFlow for HIDMaestro types and profile ids'
    $allTypeNames = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($name in @($typesFrom.Keys) + @($typesTo.Keys)) { [void]$allTypeNames.Add($name) }
    $usage = Get-GameFlowUsage -RepoRoot $Context.RepoRoot -TypeNames @($allTypeNames)
    $knownIds = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($id in @($catalogFrom.ById.Keys) + @($catalogTo.ById.Keys)) { [void]$knownIds.Add($id) }
    $profileIds = Get-GameFlowProfileIds -RepoRoot $Context.RepoRoot -KnownIds $knownIds

    # Types GameFlow uses, with the GameFlow files that use them.
    $usedTypes = [ordered]@{}
    foreach ($fileUsage in $usage.Primary) {
        foreach ($type in $fileUsage.Types.Keys) {
            if (-not $usedTypes.Contains($type)) { $usedTypes[$type] = [System.Collections.Generic.List[string]]::new() }
            $usedTypes[$type].Add($fileUsage.File)
        }
    }
    $changedPaths = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($file in $changed) {
        if ($file.Path) { [void]$changedPaths.Add($file.Path) }
        if ($file.OldPath) { [void]$changedPaths.Add($file.OldPath) }
    }
    $typeInfo = {
        param($name)
        if ($typesTo.ContainsKey($name)) { return $typesTo[$name] }
        if ($typesFrom.ContainsKey($name)) { return $typesFrom[$name] }
        $null
    }

    $md = [System.Collections.Generic.List[string]]::new()
    $md.Add("# HIDMaestro changes $range")
    $md.Add('')
    $md.Add(('Generated by `scripts/Update-HIDMaestro.ps1` on {0}. It is a reference for maintainers who adapt GameFlow to a newer HIDMaestro. Nothing in it is applied to GameFlow automatically.' -f (Get-Date -Format 'yyyy-MM-dd HH:mm')))
    $md.Add('')

    # Summary
    $md.Add('## Summary')
    $md.Add('')
    $md.Add('| | Commit | Date | Tag or describe | Version in Directory.Build.props | Subject |')
    $md.Add('|---|---|---|---|---|---|')
    foreach ($row in @(@('From', $FromInfo), @('To', $ToInfo))) {
        $info = $row[1]
        $md.Add(('| {0} | `{1}` | {2} | {3} | {4} | {5} |' -f $row[0], $info.Short, $info.Date, (Format-Cell $info.Describe), (Format-Cell $info.Version), (Format-Cell $info.Subject)))
    }
    $md.Add('')
    $md.Add(('- Clone: `{0}`, remote `{1}` ({2}), branch `{3}`.' -f $Context.ClonePath, $Context.Remote, $Context.RemoteUrl, $Context.Branch))
    $md.Add(('- Full commits: `{0}` to `{1}`.' -f $from, $to))
    if (-not $fromIsAncestor) {
        $md.Add('- The "from" commit is not an ancestor of the "to" commit. The log below lists commits reachable from "to" but not from "from", and the diff compares the two trees directly.')
    }
    $md.Add("- Commits in range: $commitCount ($mergeCount merges, which the log below leaves out).")
    $md.Add("- Files changed: $(if ($shortStat) { $shortStat } else { 'none' }).")
    $md.Add("- Tags in range: $(if ($tagsInRange.Count) { ($tagsInRange | ForEach-Object { Format-Code $_ }) -join ', ' } else { 'none' }).")
    $md.Add("- Controller profiles in the catalog: $($catalogFrom.ByPath.Count) at from, $($catalogTo.ByPath.Count) at to.")
    $md.Add(('- GameFlow pins HIDMaestro {0} (`build/HidMaestroSdk.props`) and accepts {1} <= version < {2} at runtime (`src/GameFlow.HidMaestro/HidMaestroSdkLocator.cs`).' -f $Context.Pinned, $Context.Minimum, $Context.MaximumExclusive))
    if ($Context.RemoteUrl -match 'github\.com[:/](?<repo>[^/]+/[^/]+?)(?:\.git)?/?$') {
        $md.Add(('- Compare on GitHub: https://github.com/{0}/compare/{1}...{2}' -f $Matches['repo'], $from, $to))
    }
    $md.Add(('- Full diff locally: `git -C "{0}" diff {1} {2}`' -f $Context.ClonePath, $FromInfo.Short, $ToInfo.Short))
    $md.Add('')

    # Attention
    $attention = [System.Collections.Generic.List[string]]::new()
    $toVersion = $null
    if ($ToInfo.Version -and [version]::TryParse(($ToInfo.Version -replace '[-+].*$', ''), [ref]$toVersion)) {
        $normalized = Get-NormalizedVersion $toVersion
        if ($normalized -lt $Context.Minimum -or $normalized -ge $Context.MaximumExclusive) {
            $attention.Add("The version at ``$($ToInfo.Short)`` is $($ToInfo.Version), outside the range GameFlow accepts ($($Context.Minimum) <= version < $($Context.MaximumExclusive)). GameFlow will refuse this SDK until HidMaestroSdkLocator's range and the adapter are updated.")
        }
        elseif ($normalized -ne (Get-NormalizedVersion ([version]$Context.Pinned))) {
            $attention.Add("The version at ``$($ToInfo.Short)`` is $($ToInfo.Version). GameFlow pins $($Context.Pinned). The runtime accepts it, but GameFlow is compiled and tested against $($Context.Pinned). See ""Moving GameFlow's pin"" below.")
        }
    }
    foreach ($type in $usedTypes.Keys) {
        if (-not $typesTo.ContainsKey($type)) {
            $attention.Add("GameFlow names ``$type`` ($(($usedTypes[$type] | ForEach-Object { Format-Code $_ }) -join ', ')), which is not a public type at ``$($ToInfo.Short)``. It was removed, renamed or made non-public.")
        }
    }
    foreach ($group in $api) {
        if (-not $usedTypes.Contains($group.RootType)) { continue }
        $line = "Public declarations of ``$($group.Type)`` changed ($($group.Removed.Count) removed or changed, $($group.Added.Count) added). GameFlow uses this type in $(($usedTypes[$group.RootType] | ForEach-Object { Format-Code $_ }) -join ', ')."
        if ($group.Removed.Count) {
            $shown = @($group.Removed | Select-Object -First 8 | ForEach-Object { Format-Code $_ })
            $line += " Removed or changed: $($shown -join '; ')$(if ($group.Removed.Count -gt 8) { '; ...' })."
        }
        $attention.Add($line)
    }
    foreach ($id in $profileIds.Keys) {
        if (-not $catalogTo.ById.ContainsKey($id)) {
            $attention.Add("GameFlow references profile id ``$id`` ($(($profileIds[$id] | ForEach-Object { Format-Code $_ }) -join ', ')), which is not in the catalog at ``$($ToInfo.Short)``.")
        }
    }
    $usedTypeFiles = [System.Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal)
    foreach ($type in $usedTypes.Keys) {
        $info = & $typeInfo $type
        if ($info -and $changedPaths.Contains($info.Path)) { [void]$usedTypeFiles.Add($info.Path) }
    }

    $md.Add('## Attention for GameFlow')
    $md.Add('')
    if ($attention.Count) {
        foreach ($item in $attention) { $md.Add("- $item") }
    }
    else {
        $md.Add('No change in this range removes or changes a HIDMaestro type, public declaration or profile id that GameFlow names. Behaviour can still change without an API change, so read the commit log for the files GameFlow depends on.')
    }
    $md.Add('')
    if ($usedTypeFiles.Count) {
        $md.Add('Commits in this range that touch files declaring types GameFlow uses:')
        $md.Add('')
        $touching = @(Get-GitLines (@('log', '--reverse', '--no-merges', '--format=%h %s', "$from..$to", '--') + @($usedTypeFiles)))
        foreach ($line in ($touching | Select-Object -First 60)) {
            $parts = $line -split ' ', 2
            $md.Add(('- `{0}` {1}' -f $parts[0], $(if ($parts.Count -gt 1) { $parts[1] } else { '' })))
        }
        if ($touching.Count -gt 60) { $md.Add("- ... and $($touching.Count - 60) more") }
        $md.Add('')
        $md.Add("Files: $(($usedTypeFiles | ForEach-Object { Format-Code $_ }) -join ', ').")
        $md.Add('')
    }

    # Changed files by area
    $md.Add('## Changed files by area')
    $md.Add('')
    if (-not $changed.Count) {
        $md.Add('No files changed.')
        $md.Add('')
    }
    foreach ($area in $script:AreaTitles.Keys) {
        $inArea = @($changed | Where-Object { $_.Area -eq $area } | Sort-Object Path)
        if (-not $inArea.Count) { continue }
        $md.Add("### $($script:AreaTitles[$area]) ($($inArea.Count) files)")
        $md.Add('')
        $md.Add('| Status | File | + | - |')
        $md.Add('|---|---|---|---|')
        foreach ($file in ($inArea | Select-Object -First 150)) {
            $name = if ($file.Status -in @('R', 'C') -and $file.OldPath -ne $file.Path) { ('`{0}` -> `{1}`' -f $file.OldPath, $file.Path) } else { Format-Code $file.Path }
            $status = if ($script:StatusNames.ContainsKey($file.Status)) { $script:StatusNames[$file.Status] } else { $file.Status }
            $md.Add("| $status | $name | $($file.Added) | $($file.Deleted) |")
        }
        if ($inArea.Count -gt 150) { $md.Add("| | ... and $($inArea.Count - 150) more | | |") }
        $md.Add('')
    }

    # Public declarations
    $md.Add('## Public API changes')
    $md.Add('')
    $md.Add('Public types, public members of public types and enum members whose declaration text differs between the two commits, read from the changed `.cs` files under `sdk/HIDMaestro.Core`. A changed signature shows as one removed and one added line. Method bodies are not compared, so check the diff for behaviour changes. Types in `HIDMaestro.Internal` are public but not meant as a stable API.')
    $md.Add('')
    if (-not $api.Count) {
        $md.Add('No public declaration changed.')
        $md.Add('')
    }
    else {
        $lineBudget = 800
        $ordered = @($api | Sort-Object @{ Expression = { $_.Namespace -ne 'HIDMaestro' } }, Type)
        $md.Add('````diff')
        foreach ($group in $ordered) {
            if ($lineBudget -le 0) { break }
            $usedMark = if ($usedTypes.Contains($group.RootType)) { ', used by GameFlow' } else { '' }
            $md.Add("# $($group.Type) ($($group.Namespace), $($group.File)$usedMark)")
            foreach ($declaration in $group.Removed) { $md.Add("- $declaration"); $lineBudget-- }
            foreach ($declaration in $group.Added) { $md.Add("+ $declaration"); $lineBudget-- }
        }
        $md.Add('````')
        if ($lineBudget -le 0) {
            $md.Add('')
            $md.Add(('The list was cut at 800 lines. Run `git -C "{0}" diff {1} {2} -- sdk/HIDMaestro.Core` for the rest.' -f $Context.ClonePath, $FromInfo.Short, $ToInfo.Short))
        }
        $md.Add('')
        $newTypes = @($typesTo.Keys | Where-Object { -not $typesFrom.ContainsKey($_) } | Sort-Object)
        $goneTypes = @($typesFrom.Keys | Where-Object { -not $typesTo.ContainsKey($_) } | Sort-Object)
        if ($newTypes.Count) { $md.Add("New public types: $(($newTypes | ForEach-Object { Format-Code $_ }) -join ', ').") ; $md.Add('') }
        if ($goneTypes.Count) { $md.Add("Public types that are gone: $(($goneTypes | ForEach-Object { Format-Code $_ }) -join ', ').") ; $md.Add('') }
    }

    # Profiles
    $md.Add('## Profile changes')
    $md.Add('')
    $profileFiles = @($changed | Where-Object { $_.Area -eq 'Profiles' })
    if (-not $profileFiles.Count) {
        $md.Add('No controller profile changed.')
        $md.Add('')
    }
    else {
        $md.Add('Id, name and type come from the profile JSON (the "to" version, or the "from" version for a deleted file). For modified profiles the last column lists the top-level keys whose values differ.')
        $md.Add('')
        $describe = {
            param($catalog, $path)
            if ($path -and $catalog.ByPath.ContainsKey($path)) { return $catalog.ByPath[$path] }
            @{ id = $null; name = $null; type = $null }
        }
        $keyBudget = 80
        foreach ($status in @('A', 'D', 'R', 'M', 'C', 'T')) {
            $set = @($profileFiles | Where-Object { $_.Status -eq $status } | Sort-Object Path)
            if (-not $set.Count) { continue }
            $md.Add("### $($script:StatusNames[$status]) ($($set.Count))")
            $md.Add('')
            if ($status -eq 'M') {
                $md.Add('| Id | Name | Type | Changed keys | File |')
                $md.Add('|---|---|---|---|---|')
            }
            elseif ($status -eq 'R') {
                $md.Add('| Id (from -> to) | Name | Type | File (from -> to) |')
                $md.Add('|---|---|---|---|')
            }
            else {
                $md.Add('| Id | Name | Type | File |')
                $md.Add('|---|---|---|---|')
            }
            foreach ($file in $set) {
                $toMeta = & $describe $catalogTo $file.Path
                $fromMeta = & $describe $catalogFrom $file.OldPath
                $meta = if ($status -eq 'D') { $fromMeta } else { $toMeta }
                if ($status -eq 'M') {
                    $keys = ''
                    if ($keyBudget -gt 0) {
                        $keyBudget--
                        try {
                            $before = (Get-GitFileText -Rev $from -Path $file.OldPath) | ConvertFrom-Json -AsHashtable -Depth 100
                            $after = (Get-GitFileText -Rev $to -Path $file.Path) | ConvertFrom-Json -AsHashtable -Depth 100
                            $names = [System.Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal)
                            foreach ($key in @($before.Keys) + @($after.Keys)) { [void]$names.Add($key) }
                            $diffKeys = foreach ($key in $names) {
                                if (-not $before.ContainsKey($key)) { "+$key" }
                                elseif (-not $after.ContainsKey($key)) { "-$key" }
                                elseif ((ConvertTo-Json -InputObject $before[$key] -Compress -Depth 100) -ne (ConvertTo-Json -InputObject $after[$key] -Compress -Depth 100)) { $key }
                            }
                            $keys = (@($diffKeys) | ForEach-Object { Format-Code $_ }) -join ', '
                        }
                        catch { $keys = 'not readable as JSON' }
                    }
                    else { $keys = 'not compared (limit of 80 files)' }
                    $md.Add(('| {0} | {1} | {2} | {3} | `{4}` |' -f (Format-Code $meta['id']), (Format-Cell $meta['name']), (Format-Cell $meta['type']), $keys, $file.Path))
                }
                elseif ($status -eq 'R') {
                    $md.Add(('| {0} -> {1} | {2} | {3} | `{4}` -> `{5}` |' -f (Format-Code $fromMeta['id']), (Format-Code $toMeta['id']), (Format-Cell $toMeta['name']), (Format-Cell $toMeta['type']), $file.OldPath, $file.Path))
                }
                else {
                    $md.Add(('| {0} | {1} | {2} | `{3}` |' -f (Format-Code $meta['id']), (Format-Cell $meta['name']), (Format-Cell $meta['type']), $file.Path))
                }
            }
            $md.Add('')
        }
    }
    $otherProfileFiles = @($changed | Where-Object { $_.Area -in @('ProfileModels', 'ProfileData') })
    if ($otherProfileFiles.Count) {
        $md.Add("Profile models, schema and shared data changed too: $(($otherProfileFiles | ForEach-Object { Format-Code $_.Path }) -join ', ').")
        $md.Add('')
    }

    # Commit log
    $md.Add('## Commit log')
    $md.Add('')
    $md.Add("Oldest first, merges left out ($($commits.Count) commits).")
    $md.Add('')
    foreach ($commit in $commits) {
        $md.Add("### $($commit.Short) $($commit.Subject)")
        $md.Add('')
        $tagText = if ($commit.Tags.Count) { " Tags: $(($commit.Tags | ForEach-Object { Format-Code $_ }) -join ', ')." } else { '' }
        $md.Add("$($commit.Date), $($commit.Author).$tagText")
        $md.Add('')
        if ($commit.Body) {
            $md.Add('````text')
            foreach ($bodyLine in ($commit.Body -split "`r?`n")) { $md.Add($bodyLine.TrimEnd()) }
            $md.Add('````')
            $md.Add('')
        }
    }

    # Notes for implementing in GameFlow
    $md.Add('## Notes for implementing in GameFlow')
    $md.Add('')
    $md.Add('GameFlow calls HIDMaestro only from the typed adapter in `src/GameFlow.HidMaestro`. The seam in `src/GameFlow.Infrastructure/Runtime/HidMaestro` (sink, catalog service, status texts) holds no SDK types, and the adapter tests live in `tests/GameFlow.HidMaestro.Tests`. The tables below come from scanning those three folders for HIDMaestro public type names at either commit. A member shows only when the code writes `Type.Member`; calls through a variable (for example `profile.VendorId`) are not listed, so open the file to see them. The background is in `docs/plans/2026-10-21-hidmaestro-adapter.md` in the folder above the GameFlow repo.')
    $md.Add('')
    $md.Add('### GameFlow files and the HIDMaestro types they name')
    $md.Add('')
    if (-not $usage.Primary.Count) {
        $md.Add('No GameFlow file in those folders names a HIDMaestro type.')
    }
    else {
        $md.Add('| GameFlow file | HIDMaestro types (members written as Type.Member) |')
        $md.Add('|---|---|')
        foreach ($fileUsage in ($usage.Primary | Sort-Object File)) {
            $parts = foreach ($type in $fileUsage.Types.Keys) {
                $members = @($fileUsage.Types[$type])
                if ($members.Count) { '{0} ({1})' -f (Format-Code $type), ($members -join ', ') } else { Format-Code $type }
            }
            $md.Add(('| `{0}` | {1} |' -f $fileUsage.File, (Format-Cell ($parts -join '; '))))
        }
    }
    $md.Add('')
    $md.Add('### HIDMaestro types GameFlow uses')
    $md.Add('')
    if ($usedTypes.Count) {
        $md.Add('| Type | Declared in | Namespace | Changed in this range | Used by |')
        $md.Add('|---|---|---|---|---|')
        foreach ($type in ($usedTypes.Keys | Sort-Object)) {
            $info = & $typeInfo $type
            $declared = if ($info) { Format-Code $info.Path } else { 'unknown' }
            $namespace = if ($info -and $info.Internal) { 'HIDMaestro.Internal' } else { 'HIDMaestro' }
            $changedText = if (-not $typesTo.ContainsKey($type)) { 'gone at "to"' } elseif ($info -and $changedPaths.Contains($info.Path)) { 'file changed' } else { 'no' }
            $md.Add("| $(Format-Code $type) | $declared | $namespace | $changedText | $(($usedTypes[$type] | Sort-Object -Unique | ForEach-Object { Format-Code $_ }) -join ', ') |")
        }
        $internalUsed = @($usedTypes.Keys | Where-Object { $info = & $typeInfo $_; $info -and $info.Internal } | Sort-Object)
        if ($internalUsed.Count) {
            $md.Add('')
            $md.Add("GameFlow depends on types from ``HIDMaestro.Internal``: $(($internalUsed | ForEach-Object { Format-Code $_ }) -join ', '). These can change in any release. ``HidMaestroSession`` reads ``HMProfile.ExtendedReport`` (an ``ExtendedReportSpec``) inside a try block that falls back to ``HMProfile.HasExtendedInput``; keep that guard when adapting.")
        }
    }
    else {
        $md.Add('None found.')
    }
    $md.Add('')
    if ($usage.Other.Count) {
        $md.Add('### Other GameFlow files that mention HIDMaestro types')
        $md.Add('')
        $md.Add('Usually comments or display text, but check them when a type is renamed.')
        $md.Add('')
        foreach ($item in ($usage.Other | Sort-Object File)) {
            $md.Add(('- `{0}`: {1}' -f $item.File, (($item.Types | ForEach-Object { Format-Code $_ }) -join ', ')))
        }
        $md.Add('')
    }
    $md.Add('### Profile ids GameFlow references')
    $md.Add('')
    if ($profileIds.Count) {
        $md.Add('| Id | At from | At to | Referenced in |')
        $md.Add('|---|---|---|---|')
        foreach ($id in $profileIds.Keys) {
            $atFrom = if ($catalogFrom.ById.ContainsKey($id)) { Format-Code $catalogFrom.ById[$id] } else { 'missing' }
            $atTo = if ($catalogTo.ById.ContainsKey($id)) { Format-Code $catalogTo.ById[$id] } else { '**missing**' }
            $md.Add("| $(Format-Code $id) | $atFrom | $atTo | $(($profileIds[$id] | ForEach-Object { Format-Code $_ }) -join ', ') |")
        }
        $md.Add('')
        $md.Add('The default id per controller kind lives in `src/GameFlow.Infrastructure/Runtime/HidMaestro/HidMaestroProfiles.cs`, and the adapter tests check each one against the shipped catalog.')
    }
    else {
        $md.Add('No catalog profile id found as a string literal in GameFlow code.')
    }
    $md.Add('')

    $md.Add("### Moving GameFlow's pin")
    $md.Add('')
    $targetVersion = if ($ToInfo.Version) { $ToInfo.Version } else { '<new version>' }
    $md.Add("GameFlow compiles against HIDMaestro $($Context.Pinned). To move it to $($targetVersion):")
    $md.Add('')
    $md.Add(('1. `build/HidMaestroSdk.props`: set `HidMaestroSdkVersion` to {0} and `HidMaestroSdkZipSha256` to the SHA-256 of `HIDMaestro-v{0}.zip` (GitHub shows it as the release asset digest). The URL follows the version. The build downloads the new release into `.cache/hidmaestro/{0}/`.' -f $targetVersion))
    $md.Add(('2. `tests/GameFlow.HidMaestro.Tests/SdkPinTests.cs`: expect version {0}.' -f $targetVersion))
    $md.Add('3. `src/GameFlow.HidMaestro/HidMaestroSdkLocator.cs`: change `MinimumVersion` or `ExclusiveMaximumVersion` only when the supported range changes (for example when the adapter starts to need an API that the minimum version lacks).')
    $md.Add('4. Adapt the files listed under "GameFlow files and the HIDMaestro types they name" to the API changes above.')
    if ($Context.PinMentions.Count) {
        $md.Add("5. Update text that names the pinned version. These tracked files mention HIDMaestro $($Context.Pinned) (CHANGELOG entries describe past releases, so add a new entry instead of editing old ones):")
        foreach ($mention in $Context.PinMentions) { $md.Add(('   - `{0}` ({1} lines)' -f $mention.Name, $mention.Count)) }
        $md.Add('6. Run `dotnet test --solution GameFlow.sln -c Release`.')
    }
    else {
        $md.Add('5. Run `dotnet test --solution GameFlow.sln -c Release`.')
    }
    $md.Add('')
    $md.Add('### Suggested update order')
    $md.Add('')
    $md.Add('1. Read "Attention for GameFlow" and the commits listed under it.')
    $md.Add('2. For each type marked "used by GameFlow" in "Public API changes", open the GameFlow files that use it and adapt the calls.')
    $md.Add('3. Check "Profile ids GameFlow references" for ids that are missing at "to".')
    $md.Add('4. Read the commit log for behaviour changes that need no API change (driver install, elevation, rumble, timing).')
    $md.Add('5. Move the pin as described above, then build and run the adapter tests.')
    $md.Add('')

    ($md -join "`n")
}

#endregion

#region Main

# Inputs and environment
if ($SkipUpdate -and $Ref) { throw '-Ref needs the update step. Remove -SkipUpdate, or remove -Ref.' }
if ($SdkDll -and $BuildNative) { throw '-SdkDll skips the build, so it cannot be combined with -BuildNative.' }
if ($SdkDll) { $SkipBuild = $true }

$RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not (Test-Path -LiteralPath (Join-Path $RepoRoot 'GameFlow.sln'))) {
    throw "Expected the GameFlow repo at '$RepoRoot' (no GameFlow.sln there). Run the script from its place in GameFlow/scripts."
}
$script:GitExe = Resolve-Tool 'git'
$dotnetExe = if (-not $SkipBuild) { Resolve-Tool 'dotnet' } else { $null }

$propsPath = Join-Path $RepoRoot 'build/HidMaestroSdk.props'
[xml]$propsXml = Get-Content -LiteralPath $propsPath -Raw
$pinnedNode = $propsXml.SelectSingleNode('//*[local-name()="HidMaestroSdkVersion"]')
if (-not $pinnedNode) { throw "No HidMaestroSdkVersion in '$propsPath'." }
$pinnedVersion = $pinnedNode.InnerText.Trim()

$minimumVersion = [version]'2.0.1'
$maximumExclusive = [version]'3.0.0'
$locatorPath = Join-Path $RepoRoot 'src/GameFlow.HidMaestro/HidMaestroSdkLocator.cs'
if (Test-Path -LiteralPath $locatorPath) {
    $locatorText = [System.IO.File]::ReadAllText($locatorPath)
    if ($locatorText -match 'MinimumVersion\s*=\s*new(?:\s*Version)?\s*\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\)') {
        $minimumVersion = [version]::new([int]$Matches[1], [int]$Matches[2], [int]$Matches[3])
    }
    if ($locatorText -match 'ExclusiveMaximumVersion\s*=\s*new(?:\s*Version)?\s*\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\)') {
        $maximumExclusive = [version]::new([int]$Matches[1], [int]$Matches[2], [int]$Matches[3])
    }
}
else {
    Add-RunWarning "Could not read '$locatorPath'; using the range 2.0.1 <= version < 3.0.0."
}

# 1. Clone checks
Write-Step 'Checking the HIDMaestro clone'
if (-not (Test-Path -LiteralPath $HidMaestroPath -PathType Container)) {
    throw "No HIDMaestro clone at '$HidMaestroPath'. Clone https://github.com/hifihedgehog/HIDMaestro there, or pass -HidMaestroPath."
}
$script:HmRoot = (Resolve-Path -LiteralPath $HidMaestroPath).Path.TrimEnd('\')
$topLevel = Get-GitValue @('rev-parse', '--show-toplevel')
if (-not $topLevel) { throw "'$($script:HmRoot)' is not a git repository." }
if (-not (Test-SamePath $topLevel $script:HmRoot)) {
    throw "'$($script:HmRoot)' is inside the git repository '$topLevel' but is not its root. Point -HidMaestroPath at the root of the HIDMaestro clone."
}
$csproj = Join-Path $script:HmRoot 'sdk/HIDMaestro.Core/HIDMaestro.Core.csproj'
if (-not (Test-Path -LiteralPath $csproj)) {
    $found = Get-ChildItem -LiteralPath $script:HmRoot -Recurse -File -Filter 'HIDMaestro.Core.csproj' -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } | Select-Object -First 1
    if (-not $found) { throw "'$($script:HmRoot)' has no HIDMaestro.Core.csproj. Is it a HIDMaestro clone?" }
    $csproj = $found.FullName
}
$dirty = @(Get-GitLines @('status', '--porcelain=v1', '--untracked-files=no'))
if ($dirty.Count) {
    $list = ($dirty | Select-Object -First 10) -join '; '
    throw "The HIDMaestro clone has uncommitted changes to tracked files ($($dirty.Count)): $list. Commit, stash or discard them in the clone yourself, then run the script again."
}
$beforeSha = Get-GitValue @('rev-parse', 'HEAD')
$branch = Get-GitValue @('symbolic-ref', '--quiet', '--short', 'HEAD')
$remote = if ($branch) { Get-GitValue @('config', "branch.$branch.remote") } else { $null }
if (-not $remote) {
    $remotes = @(Get-GitLines @('remote'))
    $remote = if ($remotes -contains 'origin') { 'origin' } elseif ($remotes.Count) { $remotes[0] } else { $null }
}
if (-not $remote -and -not $SkipUpdate) { throw 'The HIDMaestro clone has no git remote, so there is nothing to fetch. Use -SkipUpdate.' }
$remoteUrl = if ($remote) { Get-GitValue @('remote', 'get-url', $remote) } else { '' }
Write-Detail "Clone:  $($script:HmRoot)"
Write-Detail "Remote: $remote $remoteUrl"
Write-Detail "HEAD:   $(Get-ShortSha $beforeSha) on $(if ($branch) { "branch $branch" } else { 'a detached HEAD' })"

# Fail before changing anything when the build cannot run.
if (-not $SkipBuild) {
    $problem = $null
    if ($BuildNative) {
        if (-not (Test-Path -LiteralPath (Join-Path $script:HmRoot 'scripts/build_all.cmd'))) {
            $problem = "The clone has no scripts\build_all.cmd, so -BuildNative cannot run."
        }
    }
    else {
        $nativeX64 = Join-Path $script:HmRoot 'build/HIDMaestro.dll'
        $nativeArm = Join-Path $script:HmRoot 'build/arm64/HIDMaestro.dll'
        if (-not (Test-Path -LiteralPath $nativeX64)) {
            $problem = "HIDMaestro.Core embeds the native driver payload from '$(Join-Path $script:HmRoot 'build')', and there is no HIDMaestro.dll there, so the managed build cannot run. Run again with -BuildNative (runs the clone's scripts\build_all.cmd; needs Visual Studio with the C++ tools and the Windows Driver Kit, see docs/design/coexistence.md in the clone), with -SkipBuild to stop after the report, or with -SdkDll pointing at a HIDMaestro.Core.dll from an official release."
        }
        elseif ($env:HMSkipArm64 -ne 'true' -and -not (Test-Path -LiteralPath $nativeArm)) {
            $problem = "The clone has the x64 driver payload but no ARM64 one ('$nativeArm'). Set `$env:HMSkipArm64 = 'true' for an x64-only build, or run again with -BuildNative."
        }
    }
    if ($problem) {
        if ($WhatIfPreference) { Add-RunWarning $problem }
        else { throw $problem }
    }
}

# Update
$targetSha = $beforeSha
$updateNote = 'not updated (-SkipUpdate)'
if (-not $SkipUpdate) {
    Write-Step "Fetching $remote"
    $fetched = $false
    if ($script:Cmdlet.ShouldProcess("$remote ($remoteUrl) into $($script:HmRoot)", 'git fetch --tags')) {
        [void](Invoke-Git @('fetch', '--tags', $remote))
        $fetched = $true
    }
    else {
        Write-Detail 'Not fetched; using the remote-tracking refs from the last fetch.'
    }

    if ($Ref) {
        $targetSha = Resolve-Commit -Name $Ref -Remote $remote
        if (-not $targetSha) { throw "-Ref '$Ref' does not name a commit, tag or branch in the clone (also tried '$remote/$Ref')." }
        $targetName = $Ref
    }
    else {
        $defaultBranch = Get-GitValue @('symbolic-ref', '--quiet', '--short', "refs/remotes/$remote/HEAD")
        if ($defaultBranch) { $defaultBranch = $defaultBranch.Substring($remote.Length + 1) }
        else {
            $line = @(Get-GitLines @('ls-remote', '--symref', $remote, 'HEAD') -AllowedExitCodes @(0, 2, 128)) | Where-Object { $_ -match '^ref:\s+refs/heads/(\S+)\s+HEAD' } | Select-Object -First 1
            if ($line -and $line -match '^ref:\s+refs/heads/(\S+)') { $defaultBranch = $Matches[1] }
        }
        if (-not $defaultBranch) { throw "Could not find the default branch of '$remote'. Pass -Ref with the branch or tag to move to." }
        if (-not $branch) { throw "The clone has a detached HEAD. Check out '$defaultBranch' in the clone, or pass -Ref." }
        if ($branch -ne $defaultBranch) { throw "The clone is on branch '$branch', not on the default branch '$defaultBranch'. Check out '$defaultBranch' in the clone, or pass -Ref." }
        $targetSha = Resolve-Commit -Name "$remote/$defaultBranch"
        if (-not $targetSha) { throw "'$remote/$defaultBranch' does not exist in the clone. Fetch it first." }
        $targetName = "$remote/$defaultBranch"
    }

    if ($targetSha -eq $beforeSha) {
        $updateNote = "already at $targetName"
        Write-Detail "Already at $targetName ($(Get-ShortSha $targetSha))."
    }
    else {
        $forward = (Invoke-Git @('merge-base', '--is-ancestor', $beforeSha, $targetSha) -AllowedExitCodes @(0, 1)).ExitCode -eq 0
        if (-not $forward) {
            $behind = (Invoke-Git @('merge-base', '--is-ancestor', $targetSha, $beforeSha) -AllowedExitCodes @(0, 1)).ExitCode -eq 0
            if ($behind) {
                throw "HEAD ($(Get-ShortSha $beforeSha)) already contains $targetName ($(Get-ShortSha $targetSha)). Moving back is not a fast-forward, so the script stops. Check out $targetName in the clone yourself if you want to build it."
            }
            throw "HEAD ($(Get-ShortSha $beforeSha)) and $targetName ($(Get-ShortSha $targetSha)) have diverged, so this is not a fast-forward. Sort out the clone's history yourself, then run the script again."
        }
        $count = Get-GitValue @('rev-list', '--count', "$beforeSha..$targetSha")
        $what = if ($branch) { "branch $branch" } else { 'detached HEAD' }
        if ($script:Cmdlet.ShouldProcess($script:HmRoot, "Fast-forward $what from $(Get-ShortSha $beforeSha) to $targetName ($(Get-ShortSha $targetSha), $count commits)")) {
            [void](Invoke-Git @('merge', '--ff-only', '--quiet', $targetSha))
            $updateNote = "fast-forwarded $count commits to $targetName"
            Write-Detail "Fast-forwarded $count commits to $targetName ($(Get-ShortSha $targetSha))."
        }
        else {
            $updateNote = "would fast-forward $count commits to $targetName"
        }
        if (-not $fetched) { $updateNote += ' (remote refs not refreshed)' }
    }
}
$headSha = Get-GitValue @('rev-parse', 'HEAD')

# 2. Report
$reportPath = $null
$reportNote = 'skipped (-SkipReport)'
if (-not $SkipReport) {
    Write-Step 'Writing the change report'
    $fromSha = $null
    if ($ReportFrom) {
        $fromSha = Resolve-Commit -Name $ReportFrom -Remote $remote
        if (-not $fromSha) { throw "-ReportFrom '$ReportFrom' does not name a commit, tag or branch in the clone." }
    }
    elseif ($beforeSha -ne $targetSha) {
        $fromSha = $beforeSha
    }
    else {
        $pinnedTagSha = Resolve-Commit -Name "v$pinnedVersion"
        if ($pinnedTagSha -and $pinnedTagSha -ne $targetSha) {
            $fromSha = $pinnedTagSha
            Write-Detail "No new commits; comparing with v$pinnedVersion, the version GameFlow pins."
        }
    }
    if (-not $fromSha -or $fromSha -eq $targetSha) {
        $reportNote = "skipped (no commits between the report start and $(Get-ShortSha $targetSha); pass -ReportFrom to choose a start)"
        Write-Detail $reportNote
    }
    else {
        $fromInfo = Get-CommitInfo -Sha $fromSha
        $toInfo = Get-CommitInfo -Sha $targetSha
        $reportDirectory = Join-Path $RepoRoot 'docs/hidmaestro'
        $fileName = "CHANGES-$($fromInfo.Label)-$($toInfo.Label).md"
        $reportPath = Join-Path $reportDirectory $fileName
        $latestPath = Join-Path $reportDirectory 'LATEST.md'
        if ($script:Cmdlet.ShouldProcess($reportPath, "Write the HIDMaestro change report $($fromInfo.Short)..$($toInfo.Short) and update LATEST.md")) {
            $pinMentions = @()
            $grep = Get-GitLines @('grep', '-I', '-i', '-c', '-E', "hid ?maestro.{0,60}$([regex]::Escape($pinnedVersion))|$([regex]::Escape($pinnedVersion)).{0,60}hid ?maestro") -Repository $RepoRoot -AllowedExitCodes @(0, 1)
            foreach ($line in $grep) {
                if ($line -notmatch '^(?<name>.+):(?<count>\d+)$') { continue }
                $name = $Matches['name']
                if ($name -like 'docs/hidmaestro/*' -or $name -like '*.lock.json') { continue }
                $pinMentions += [pscustomobject]@{ Name = $name; Count = [int]$Matches['count'] }
            }
            $context = @{
                RepoRoot         = $RepoRoot
                ClonePath        = $script:HmRoot
                Remote           = $remote
                RemoteUrl        = $remoteUrl
                Branch           = $(if ($branch) { $branch } else { '(detached)' })
                Pinned           = $pinnedVersion
                Minimum          = $minimumVersion
                MaximumExclusive = $maximumExclusive
                PinMentions      = $pinMentions
            }
            $report = New-ChangeReport -FromInfo $fromInfo -ToInfo $toInfo -Context $context
            [void][System.IO.Directory]::CreateDirectory($reportDirectory)
            [System.IO.File]::WriteAllText($reportPath, $report + "`n", $script:Utf8NoBom)
            $split = $report -split "`n", 2
            $latest = $split[0] + "`n`n" + ('> Copy of `{0}`, the newest report written by `scripts/Update-HIDMaestro.ps1`.' -f $fileName) + "`n" + $split[1]
            [System.IO.File]::WriteAllText($latestPath, $latest + "`n", $script:Utf8NoBom)
            $reportNote = Get-RelativePath $RepoRoot $reportPath
            Write-Detail "Wrote $reportNote and docs/hidmaestro/LATEST.md"
        }
        else {
            $reportNote = "would write $(Get-RelativePath $RepoRoot $reportPath)"
        }
    }
}

$copied = [System.Collections.Generic.List[string]]::new()
$failures = [System.Collections.Generic.List[string]]::new()
$sdkSource = $null
$sdkOrigin = ''
$versionNote = 'not checked'
$buildNote = 'skipped'

# 3. Build
if ($SdkDll) {
    if (-not (Test-Path -LiteralPath $SdkDll -PathType Leaf)) { throw "-SdkDll '$SdkDll' does not exist." }
    $sdkSource = (Resolve-Path -LiteralPath $SdkDll).Path
    $sdkOrigin = 'given with -SdkDll'
    $buildNote = 'skipped (-SdkDll)'
}
else {
    $expectedOutput = $null
    if ($dotnetExe -or (Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue)) {
        $dotnetForQuery = if ($dotnetExe) { $dotnetExe } else { Resolve-Tool 'dotnet' }
        $query = Invoke-Tool -FilePath $dotnetForQuery -ArgumentList @('msbuild', $csproj, '-nologo', '-getProperty:TargetPath', '-p:Configuration=Release')
        $candidate = @($query.Output -split "`r?`n" | Where-Object { $_.Trim() }) | Select-Object -Last 1
        if ($query.ExitCode -eq 0 -and $candidate) { $expectedOutput = $candidate.Trim() }
    }
    if (-not $expectedOutput) {
        $fallback = Get-ChildItem -LiteralPath (Join-Path (Split-Path $csproj -Parent) 'bin/Release') -Recurse -File -Filter 'HIDMaestro.Core.dll' -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if ($fallback) { $expectedOutput = $fallback.FullName }
    }

    if ($SkipBuild) {
        $buildNote = 'skipped (-SkipBuild)'
        if ($expectedOutput -and (Test-Path -LiteralPath $expectedOutput)) {
            $sdkSource = $expectedOutput
            $sdkOrigin = 'existing build output, not rebuilt'
        }
        else {
            Add-RunWarning 'There is no HIDMaestro.Core build output in the clone, so the version check and the copy are skipped.'
        }
    }
    else {
        Write-Step 'Building HIDMaestro.Core (Release)'
        $buildTarget = if ($BuildNative) { Join-Path $script:HmRoot 'scripts\build_all.cmd' } else { $csproj }
        $buildAction = if ($BuildNative) { 'Run build_all.cmd (native payload and HIDMaestro.Core)' } else { 'dotnet build -c Release' }
        if ($script:Cmdlet.ShouldProcess($buildTarget, $buildAction)) {
            $result = if ($BuildNative) {
                Invoke-Tool -FilePath $env:ComSpec -ArgumentList @('/d', '/c', 'build_all.cmd') -WorkingDirectory (Join-Path $script:HmRoot 'scripts') -Stream
            }
            else {
                Invoke-Tool -FilePath $dotnetExe -ArgumentList @('build', $csproj, '-c', 'Release', '-nologo', '-v', 'minimal') -WorkingDirectory $script:HmRoot -Stream
            }
            if ($result.ExitCode -ne 0) {
                throw "The HIDMaestro build failed with exit code $($result.ExitCode). The messages above name the cause. Nothing was copied into GameFlow."
            }
            if (-not $expectedOutput -or -not (Test-Path -LiteralPath $expectedOutput)) {
                $fallback = Get-ChildItem -LiteralPath (Join-Path (Split-Path $csproj -Parent) 'bin/Release') -Recurse -File -Filter 'HIDMaestro.Core.dll' -ErrorAction SilentlyContinue |
                    Sort-Object LastWriteTime -Descending | Select-Object -First 1
                if (-not $fallback) { throw 'The build reported success but no HIDMaestro.Core.dll was found under sdk/HIDMaestro.Core/bin/Release.' }
                $expectedOutput = $fallback.FullName
            }
            $sdkSource = $expectedOutput
            $sdkOrigin = "built from $(Get-ShortSha $headSha)"
            $buildNote = "built $(Get-RelativePath $script:HmRoot $sdkSource)"
        }
        else {
            $buildNote = 'would build'
        }
    }
}

# 4. Version check
$sdkVersion = $null
if ($sdkSource) {
    Write-Step 'Checking the SDK version'
    try { $sdkVersion = [System.Reflection.AssemblyName]::GetAssemblyName($sdkSource).Version }
    catch { throw "'$sdkSource' is not a readable .NET assembly: $($_.Exception.Message)" }
    $productVersion = (Get-Item -LiteralPath $sdkSource).VersionInfo.ProductVersion
    $normalized = Get-NormalizedVersion $sdkVersion
    $supported = $normalized -ge $minimumVersion -and $normalized -lt $maximumExclusive
    $versionNote = "$sdkVersion ($productVersion), $sdkOrigin"
    Write-Detail "HIDMaestro.Core $sdkVersion, product version $productVersion ($sdkOrigin)"
    if ($sdkOrigin -notlike 'given*' -and $productVersion -match '\+(?<sha>[0-9a-f]{7,40})' -and -not $headSha.StartsWith($Matches['sha'], [StringComparison]::OrdinalIgnoreCase)) {
        Add-RunWarning "The DLL says it was built from commit $($Matches['sha']), but the clone is at $(Get-ShortSha $headSha). It may be a stale build output."
    }
    if (-not $supported) {
        $message = "HIDMaestro.Core $sdkVersion is outside the range GameFlow accepts ($minimumVersion <= version < $maximumExclusive). GameFlow would report it as unsupported at runtime."
        if ($Force) { Add-RunWarning "$message Copying anyway (-Force)." }
        else { throw "$message Nothing was copied. Pass -Force to copy it anyway." }
    }
    else {
        Write-Detail "Inside GameFlow's range ($minimumVersion <= version < $maximumExclusive)."
    }
}

# 5. Copy
if (-not $SkipCopy -and ($sdkSource -or $WhatIfPreference)) {
    Write-Step 'Copying the SDK into GameFlow'
    $sourceForCopy = if ($sdkSource) { $sdkSource } elseif ($expectedOutput) { $expectedOutput } else { Join-Path $script:HmRoot 'sdk\HIDMaestro.Core\bin\Release\HIDMaestro.Core.dll' }
    $sourceFolder = Split-Path $sourceForCopy -Parent
    $files = [System.Collections.Generic.List[string]]::new()
    $files.Add($sourceForCopy)
    $pdb = [System.IO.Path]::ChangeExtension($sourceForCopy, '.pdb')
    if (Test-Path -LiteralPath $pdb) { $files.Add($pdb) }

    # Referenced assemblies that are not part of .NET must travel with the DLL.
    if (Test-Path -LiteralPath $sourceForCopy) {
        $frameworkFolder = $null
        $dotnetForFramework = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($dotnetForFramework) {
            $sharedRoot = Join-Path (Split-Path $dotnetForFramework.Source -Parent) 'shared/Microsoft.NETCore.App'
            $frameworkFolder = Get-ChildItem -LiteralPath $sharedRoot -Directory -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -match '^\d+\.\d+\.\d+$' } | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
        }
        $stream = [System.IO.File]::OpenRead($sourceForCopy)
        try {
            $peReader = [System.Reflection.PortableExecutable.PEReader]::new($stream)
            $metadata = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($peReader)
            foreach ($handle in $metadata.AssemblyReferences) {
                $name = $metadata.GetString($metadata.GetAssemblyReference($handle).Name)
                $isFramework = if ($frameworkFolder) { Test-Path -LiteralPath (Join-Path $frameworkFolder.FullName "$name.dll") } else { $name -match '^(System|Microsoft\.Win32|mscorlib|netstandard)(\.|$)' }
                if ($isFramework) { continue }
                $dependency = Join-Path $sourceFolder "$name.dll"
                if (Test-Path -LiteralPath $dependency) { $files.Add($dependency) }
                else { Add-RunWarning "HIDMaestro.Core references $name, which is neither part of .NET nor next to the DLL. GameFlow may fail to load the SDK." }
            }
        }
        finally {
            $stream.Dispose()
        }
    }

    $targets = [System.Collections.Generic.List[object]]::new()
    $binRoot = Join-Path $RepoRoot 'src/GameFlow.App/bin'
    $executables = @(Get-ChildItem -LiteralPath $binRoot -Recurse -Depth 5 -File -Filter 'GameFlow.exe' -ErrorAction SilentlyContinue | Sort-Object FullName)
    foreach ($exe in $executables) {
        $relative = Get-RelativePath $binRoot $exe.DirectoryName
        if (-not $IncludePublish -and ($relative -split '/') -contains 'publish') {
            Write-Detail "Skipping $(Get-RelativePath $RepoRoot $exe.DirectoryName) (publish folder; use -IncludePublish)."
            continue
        }
        $targets.Add([pscustomobject]@{ Folder = Join-Path $exe.DirectoryName 'HIDMaestro'; AppFolder = $exe.DirectoryName })
    }
    if (-not $executables.Count) {
        Add-RunWarning "No GameFlow.exe under '$binRoot'. Build GameFlow first, or pass -Destination."
    }
    $cacheFolder = Join-Path $RepoRoot ".cache/hidmaestro/$pinnedVersion"
    foreach ($folder in $Destination) {
        $full = [System.IO.Path]::GetFullPath($folder, (Get-Location).Path)
        if ((Test-PathInside $full (Join-Path $RepoRoot '.cache')) -and -not $UpdateBuildCache) {
            throw "-Destination '$full' is inside GameFlow's .cache folder. Use -UpdateBuildCache to replace the compile-time copy on purpose."
        }
        $targets.Add([pscustomobject]@{ Folder = $full; AppFolder = $null })
    }

    $sourceHash = if (Test-Path -LiteralPath $sourceForCopy) { (Get-FileHash -LiteralPath $sourceForCopy -Algorithm SHA256).Hash } else { $null }
    $versionText = if ($sdkVersion) { " $sdkVersion" } else { '' }
    $copyInto = {
        param([string]$Folder, [string]$Label)
        $targetDll = Join-Path $Folder 'HIDMaestro.Core.dll'
        if ($sourceHash -and (Test-Path -LiteralPath $targetDll) -and (Get-FileHash -LiteralPath $targetDll -Algorithm SHA256).Hash -eq $sourceHash) {
            Write-Detail "$Label already holds this DLL."
            $copied.Add("$Label (unchanged)")
            return
        }
        $names = ($files | ForEach-Object { Split-Path $_ -Leaf }) -join ', '
        if ($script:Cmdlet.ShouldProcess($Folder, "Copy $names$versionText")) {
            try {
                [void][System.IO.Directory]::CreateDirectory($Folder)
                foreach ($file in $files) {
                    [System.IO.File]::Copy($file, (Join-Path $Folder (Split-Path $file -Leaf)), $true)
                }
                $copied.Add($Label)
                Write-Detail "Copied to $Label"
            }
            catch {
                $failures.Add("${Label}: $($_.Exception.Message)")
                Write-Warning "Could not copy to ${Label}: $($_.Exception.Message) Close GameFlow if it runs from that folder."
            }
        }
    }

    foreach ($target in $targets) {
        $label = if (Test-PathInside $target.Folder $RepoRoot) { Get-RelativePath $RepoRoot $target.Folder } else { $target.Folder }
        & $copyInto $target.Folder $label
        if ($target.AppFolder) {
            $besideExe = Join-Path $target.AppFolder 'HIDMaestro.Core.dll'
            if (Test-Path -LiteralPath $besideExe) {
                $appLabel = Get-RelativePath $RepoRoot $target.AppFolder
                if ($OverwriteAppFolderCopy) {
                    & $copyInto $target.AppFolder $appLabel
                }
                else {
                    $oldVersion = try { [System.Reflection.AssemblyName]::GetAssemblyName($besideExe).Version } catch { 'unreadable' }
                    if (-not $sourceHash -or (Get-FileHash -LiteralPath $besideExe -Algorithm SHA256).Hash -ne $sourceHash) {
                        Add-RunWarning "$appLabel holds HIDMaestro.Core.dll $oldVersion next to GameFlow.exe. GameFlow loads that copy before it looks in the HIDMaestro subfolder. Delete it, or run again with -OverwriteAppFolderCopy."
                    }
                }
            }
        }
    }

    if ($UpdateBuildCache) {
        $cacheDll = Join-Path $cacheFolder 'HIDMaestro.Core.dll'
        if ($script:Cmdlet.ShouldProcess($cacheDll, "Replace the pinned compile-time SDK ($pinnedVersion) with HIDMaestro.Core$versionText")) {
            [void][System.IO.Directory]::CreateDirectory($cacheFolder)
            [System.IO.File]::Copy($sourceForCopy, $cacheDll, $true)
            $copied.Add("$(Get-RelativePath $RepoRoot $cacheDll) (compile-time copy)")
            Add-RunWarning "The compile-time copy in .cache/hidmaestro/$pinnedVersion now holds$versionText. SdkPinTests will fail and CI still uses $pinnedVersion. Delete .cache/hidmaestro/$pinnedVersion and build GameFlow again to go back."
        }
    }

    foreach ($scope in @('Process', 'User', 'Machine')) {
        $override = [Environment]::GetEnvironmentVariable('GAMEFLOW_HIDMAESTRO_DIR', $scope)
        if ($override -and (Test-Path -LiteralPath (Join-Path $override 'HIDMaestro.Core.dll'))) {
            Add-RunWarning "GAMEFLOW_HIDMAESTRO_DIR ($scope) points at '$override', which holds a HIDMaestro.Core.dll. GameFlow loads that copy before any copy in its own folder."
            break
        }
    }
}
elseif ($SkipCopy) {
    Write-Detail 'Copy skipped (-SkipCopy).'
}

# 6. Summary
Write-Host ''
Write-Host 'HIDMaestro update summary' -ForegroundColor Green
$rangeText = if ($beforeSha -eq $headSha) { "$(Get-ShortSha $beforeSha) (unchanged)" } else { "$(Get-ShortSha $beforeSha) -> $(Get-ShortSha $headSha)" }
Write-Host "  Clone:    $($script:HmRoot)"
Write-Host "  Commits:  $rangeText; $updateNote"
Write-Host "  Report:   $reportNote"
Write-Host "  Build:    $buildNote"
Write-Host "  Version:  $versionNote"
if ($copied.Count) {
    Write-Host '  Copied:'
    foreach ($item in $copied) { Write-Host "    $item" }
}
else {
    Write-Host '  Copied:   nothing'
}
if ($WhatIfPreference) { Write-Host '  -WhatIf:  nothing was changed.' }
if ($script:Warnings.Count) {
    Write-Host "  Warnings: $($script:Warnings.Count) (see above)" -ForegroundColor Yellow
}
if ($failures.Count) {
    throw "Copying failed for: $($failures -join '; ')"
}

#endregion
