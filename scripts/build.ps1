<#
.SYNOPSIS
    Builds and tests all of Goop: the native C++ core and the .NET bindings.

.DESCRIPTION
    Runs the whole pipeline in order and stops at the first failure, naming the
    step that broke:

        1. cmake configure   -> ./build      (x64)
        2. cmake --build                     (the chosen configuration)
        3. ctest                             (the Catch2 suite)
        4. dotnet build      dotnet/Goop.sln (net48 and net8.0)
        5. dotnet test       dotnet/Goop.sln (net48 and net8.0)

    Steps 3 and 5 are skipped with -SkipTests.

    The Visual Studio generator is NOT specified. CMake picks the newest
    installed Visual Studio itself and this script only passes the architecture
    (-A x64), so the same command works on VS 2022 and later.

.PARAMETER Configuration
    Debug (default) or Release. Used for both the native and the managed build,
    which matters: dotnet/Directory.Build.props derives GoopNativeBinDir from
    $(Configuration), so a Release managed build looks for a Release goop.dll.

.PARAMETER SkipTests
    Skip both test steps (ctest and dotnet test). Builds only.

.EXAMPLE
    .\scripts\build.ps1

.EXAMPLE
    .\scripts\build.ps1 -Configuration Release -SkipTests
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug',

    [switch] $SkipTests
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot  = Split-Path -Parent $PSScriptRoot
$BuildDir  = Join-Path $RepoRoot 'build'
$Solution  = Join-Path $RepoRoot 'dotnet\Goop.sln'

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

function Write-Step
{
    param([string] $Message)
    Write-Host ''
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Stop-WithFailure
{
    param(
        [string] $Step,
        [string] $Hint = ''
    )

    Write-Host ''
    Write-Host "BUILD FAILED at step: $Step" -ForegroundColor Red
    if ($Hint)
    {
        Write-Host $Hint -ForegroundColor Yellow
    }
    exit 1
}

<#
    Locates a CMake tool (cmake.exe / ctest.exe).

    Prefers whatever is on PATH. If CMake is not installed standalone, falls
    back to the copy that ships with Visual Studio's "C++ CMake tools for
    Windows" component, discovered through vswhere rather than hardcoded - so
    this keeps working across VS versions and installation paths.
#>
function Resolve-CMakeTool
{
    param([Parameter(Mandatory)] [string] $Name)

    $onPath = Get-Command $Name -ErrorAction SilentlyContinue
    if ($onPath)
    {
        return $onPath.Source
    }

    $vsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path $vsWhere)
    {
        # -latest picks the newest install; requiring the C++ toolchain avoids
        # matching installs (Build Tools for other workloads, SSMS) that cannot
        # compile this project anyway.
        $vsPath = & $vsWhere -latest -prerelease -products * `
            -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
            -property installationPath

        if ($vsPath)
        {
            $candidate = Join-Path $vsPath "Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\$Name.exe"
            if (Test-Path $candidate)
            {
                return $candidate
            }
        }
    }

    Stop-WithFailure -Step "locate $Name" -Hint @"
Could not find $Name.exe on PATH or inside a Visual Studio installation.

Fix either way:
  * install CMake 3.20 or newer from https://cmake.org/download/ and tick
    "Add CMake to the system PATH", or
  * in the Visual Studio Installer, add the "C++ CMake tools for Windows"
    component to the "Desktop development with C++" workload.
"@
}

function Assert-LastExitCode
{
    param(
        [string] $Step,
        [string] $Hint = ''
    )

    if ($LASTEXITCODE -ne 0)
    {
        Stop-WithFailure -Step $Step -Hint $Hint
    }
}

# ---------------------------------------------------------------------------
# Preflight
# ---------------------------------------------------------------------------

Write-Host "Goop build" -ForegroundColor Green
Write-Host "  Repo root:     $RepoRoot"
Write-Host "  Configuration: $Configuration"
Write-Host "  Skip tests:    $SkipTests"

$cmake = Resolve-CMakeTool -Name 'cmake'
$ctest = Resolve-CMakeTool -Name 'ctest'
Write-Host "  cmake:         $cmake"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue))
{
    Stop-WithFailure -Step 'locate dotnet' -Hint @"
The .NET SDK was not found on PATH. Install it from
https://dotnet.microsoft.com/download - the repo targets net8.0 and net48, and
an SDK of 8.0 or newer can build both.
"@
}

# ---------------------------------------------------------------------------
# 1. cmake configure
# ---------------------------------------------------------------------------

Write-Step "1/5  cmake configure -> $BuildDir"

& $cmake -S $RepoRoot -B $BuildDir -A x64
Assert-LastExitCode -Step 'cmake configure' -Hint @"
Configuration failed. Common causes:
  * the C++ workload is not installed in Visual Studio (no compiler found)
  * Catch2 could not be fetched - FetchContent needs git and network access on
    the first configure. Run with -D GOOP_BUILD_TESTS=OFF to build without it.
If the cache is stale, delete the build directory and try again:
  Remove-Item -Recurse -Force '$BuildDir'
"@

# ---------------------------------------------------------------------------
# 2. cmake build
# ---------------------------------------------------------------------------

Write-Step "2/5  cmake --build ($Configuration)"

& $cmake --build $BuildDir --config $Configuration
Assert-LastExitCode -Step "cmake --build ($Configuration)" -Hint @"
The native build failed. The compiler output above names the file and line.
"@

$nativeDll = Join-Path $BuildDir "bin\$Configuration\goop.dll"
if (Test-Path $nativeDll)
{
    Write-Host "     goop.dll -> $nativeDll" -ForegroundColor DarkGray
}
else
{
    Stop-WithFailure -Step "cmake --build ($Configuration)" -Hint @"
The build reported success but goop.dll is not at:
  $nativeDll
Check CMAKE_RUNTIME_OUTPUT_DIRECTORY in the top-level CMakeLists.txt - the
managed projects derive GoopNativeBinDir from exactly this path.
"@
}

# ---------------------------------------------------------------------------
# 3. ctest (Catch2)
# ---------------------------------------------------------------------------

if ($SkipTests)
{
    Write-Step '3/5  ctest - SKIPPED (-SkipTests)'
}
else
{
    Write-Step "3/5  ctest ($Configuration)"

    & $ctest --test-dir $BuildDir --build-config $Configuration --output-on-failure
    Assert-LastExitCode -Step "ctest ($Configuration)" -Hint @"
The C++ (Catch2) tests failed. Failing assertions are printed above. If ctest
reports no tests at all, the build was configured with GOOP_BUILD_TESTS=OFF.
"@
}

# ---------------------------------------------------------------------------
# 4. dotnet build
# ---------------------------------------------------------------------------

Write-Step "4/5  dotnet build ($Configuration, net48 + net8.0)"

& dotnet build $Solution --configuration $Configuration --nologo
Assert-LastExitCode -Step "dotnet build ($Configuration)" -Hint @"
The managed build failed. If the error mentions net48, check that the
.NET Framework 4.8 Developer Pack is installed:
  https://dotnet.microsoft.com/download/dotnet-framework/net48
(the "Developer Pack", not just the runtime - building needs the reference
assemblies, running does not).
"@

# ---------------------------------------------------------------------------
# 5. dotnet test
# ---------------------------------------------------------------------------

if ($SkipTests)
{
    Write-Step '5/5  dotnet test - SKIPPED (-SkipTests)'
}
else
{
    Write-Step "5/5  dotnet test ($Configuration, net48 + net8.0)"

    & dotnet test $Solution --configuration $Configuration --no-build --nologo
    Assert-LastExitCode -Step "dotnet test ($Configuration)" -Hint @"
The .NET tests failed. A DllNotFoundException for 'goop' means the native DLL
did not get copied next to the test binaries - check the CopyGoopNativeLibrary
target in the test project and that goop.dll exists at:
  $nativeDll
A BadImageFormatException means the test process is 32-bit; check PlatformTarget
and Prefer32Bit in dotnet/Directory.Build.props.
"@
}

# ---------------------------------------------------------------------------

Write-Host ''
Write-Host "Goop build succeeded ($Configuration)." -ForegroundColor Green
exit 0
