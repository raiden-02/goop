<#
.SYNOPSIS
    Runs clang-format over the native C++ sources using .clang-format.

.DESCRIPTION
    clang-format is not on PATH on a stock Windows box. It ships inside Visual
    Studio's LLVM component, and NOT necessarily in the newest VS install - on
    this machine it lives under VS 2022, not VS 18. This script finds it
    wherever it is rather than making you remember the path.

    Search order: PATH, then every Visual Studio install vswhere reports.

.PARAMETER Check
    Do not modify anything. Report which files would change and exit non-zero
    if any would. This is the mode a CI step or a pre-commit hook wants.

.EXAMPLE
    .\scripts\format.ps1

.EXAMPLE
    .\scripts\format.ps1 -Check
#>
[CmdletBinding()]
param(
    [switch] $Check
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot

function Resolve-ClangFormat
{
    $onPath = Get-Command clang-format -ErrorAction SilentlyContinue
    if ($onPath)
    {
        return $onPath.Source
    }

    $vsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path $vsWhere)
    {
        # -all, not -latest: the newest VS install is not necessarily the one
        # carrying the LLVM component.
        $installs = & $vsWhere -all -prerelease -products * -property installationPath
        foreach ($install in $installs)
        {
            $candidate = Join-Path $install 'VC\Tools\Llvm\bin\clang-format.exe'
            if (Test-Path $candidate)
            {
                return $candidate
            }
        }
    }

    Write-Host 'clang-format not found.' -ForegroundColor Red
    Write-Host 'Install the "C++ Clang tools for Windows" component in the Visual Studio Installer,' -ForegroundColor Yellow
    Write-Host 'or put clang-format.exe on PATH.' -ForegroundColor Yellow
    exit 1
}

$clangFormat = Resolve-ClangFormat
Write-Host "clang-format: $clangFormat" -ForegroundColor DarkGray
& $clangFormat --version

$sources = Get-ChildItem -Path (Join-Path $RepoRoot 'native') -Recurse -File -Include *.c, *.cc, *.cpp, *.cxx, *.h, *.hh, *.hpp, *.hxx |
    Where-Object { $_.FullName -notmatch '\\build\\' }

if (-not $sources)
{
    Write-Host 'No native sources found.' -ForegroundColor Yellow
    exit 0
}

if ($Check)
{
    # Use clang-format's own --dry-run instead of diffing its stdout against the
    # file. Capturing native output through the PowerShell pipeline re-encodes it
    # and normalises line endings, so a hand-rolled comparison reports drift on
    # files that are already correctly formatted. --dry-run -Werror does the
    # comparison inside clang-format, where the bytes are still the real bytes,
    # and sets the exit code for us.
    $paths = $sources | ForEach-Object { $_.FullName }
    & $clangFormat --style=file --dry-run -Werror @paths
    $formatExit = $LASTEXITCODE

    if ($formatExit -ne 0)
    {
        Write-Host ''
        Write-Host 'Formatting drift detected (see warnings above).' -ForegroundColor Red
        Write-Host 'Run: .\scripts\format.ps1' -ForegroundColor Yellow
        exit 1
    }

    Write-Host ''
    Write-Host "All $($sources.Count) file(s) already formatted." -ForegroundColor Green
    exit 0
}

foreach ($file in $sources)
{
    & $clangFormat --style=file -i $file.FullName
}

Write-Host ''
Write-Host "Formatted $($sources.Count) file(s)." -ForegroundColor Green
exit 0
