<#
.SYNOPSIS
    Builds and tests all of Goop: the native C++ core and the .NET bindings.

.DESCRIPTION
    Runs the whole pipeline in order and stops at the first failure, naming the
    step that broke:

        1. cmake configure   -> ./build      (x64)
        2. cmake --build                     (the chosen configuration)
        3. ctest                             (the Catch2 suite)
        4. dotnet build      dotnet/Goop.sln (net48, net8.0, net10.0)
        5. dotnet test       dotnet/Goop.sln
        6. dotnet pack       -> artifacts/packages/Goop.0.1.0.nupkg
        7. package test      Goop.PackageTests restores the .nupkg like a stranger would

    Steps 3, 5 and 7 are skipped with -SkipTests.

    The Visual Studio generator is NOT specified. CMake picks the newest
    installed Visual Studio itself and this script only passes the architecture
    (-A x64), so the same command works on VS 2022 and later.

.PARAMETER Configuration
    Debug (default) or Release. Used for both the native and the managed build,
    which matters: dotnet/Directory.Build.props derives GoopNativeBinDir from
    $(Configuration), so a Release managed build looks for a Release goop_native.dll.

.PARAMETER SkipTests
    Skip every test step (ctest, dotnet test, package test). Still packs.

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

$RepoRoot    = Split-Path -Parent $PSScriptRoot
$BuildDir    = Join-Path $RepoRoot 'build'
$Solution    = Join-Path $RepoRoot 'dotnet\Goop.sln'
# Pins the VSTest host to x64. Without it VSTest may launch a 32-bit host,
# which cannot load the 64-bit goop_native.dll - see the comment in the file itself.
$RunSettings = Join-Path $RepoRoot 'dotnet\goop.runsettings'
$GoopProject = Join-Path $RepoRoot 'dotnet\src\Goop\Goop.csproj'
$PackageTests = Join-Path $RepoRoot 'dotnet\tests\Goop.PackageTests'

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

Write-Step "1/7  cmake configure -> $BuildDir"

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

Write-Step "2/7  cmake --build ($Configuration)"

& $cmake --build $BuildDir --config $Configuration
Assert-LastExitCode -Step "cmake --build ($Configuration)" -Hint @"
The native build failed. The compiler output above names the file and line.
"@

$nativeDll = Join-Path $BuildDir "bin\$Configuration\goop_native.dll"
if (Test-Path $nativeDll)
{
    Write-Host "     goop_native.dll -> $nativeDll" -ForegroundColor DarkGray
}
else
{
    Stop-WithFailure -Step "cmake --build ($Configuration)" -Hint @"
The build reported success but goop_native.dll is not at:
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
    Write-Step '3/7  ctest - SKIPPED (-SkipTests)'
}
else
{
    Write-Step "3/7  ctest ($Configuration)"

    & $ctest --test-dir $BuildDir --build-config $Configuration --output-on-failure
    Assert-LastExitCode -Step "ctest ($Configuration)" -Hint @"
The C++ (Catch2) tests failed. Failing assertions are printed above. If ctest
reports no tests at all, the build was configured with GOOP_BUILD_TESTS=OFF.
"@
}

# ---------------------------------------------------------------------------
# 4. dotnet build
# ---------------------------------------------------------------------------

Write-Step "4/7  dotnet build ($Configuration, net48 + net8.0 + net10.0)"

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
    Write-Step '5/7  dotnet test - SKIPPED (-SkipTests)'
}
else
{
    Write-Step "5/7  dotnet test ($Configuration, net48 + net8.0 + net10.0)"

    & dotnet test $Solution --configuration $Configuration --no-build --nologo --settings $RunSettings
    Assert-LastExitCode -Step "dotnet test ($Configuration)" -Hint @"
The .NET tests failed. A DllNotFoundException for 'goop' means the native DLL
did not get copied next to the test binaries - check the CopyGoopNativeLibrary
target in the test project and that goop_native.dll exists at:
  $nativeDll
A BadImageFormatException means the test process is 32-bit; check PlatformTarget
and Prefer32Bit in dotnet/Directory.Build.props.
"@
}

# ---------------------------------------------------------------------------
# 6. dotnet pack
# ---------------------------------------------------------------------------

Write-Step "6/7  dotnet pack ($Configuration) -> artifacts/packages"

& dotnet pack $GoopProject --configuration $Configuration --no-build --nologo
Assert-LastExitCode -Step "dotnet pack ($Configuration)" -Hint @"
Packing failed. GOOP0002 means goop_native.dll was missing for this
configuration - the package would have shipped without its native library.
"@

# ---------------------------------------------------------------------------
# 7. package test - consume the .nupkg exactly as an outside project would
# ---------------------------------------------------------------------------

if ($SkipTests)
{
    Write-Step '7/7  package test - SKIPPED (-SkipTests)'
}
else
{
    Write-Step "7/7  package test ($Configuration): restore Goop from the .nupkg, then run"

    # NuGet caches packages BY VERSION. We just repacked 0.1.0, so a cached copy
    # from an earlier run would be served instead of the new one and the test
    # would pass against a stale package. Goop.PackageTests uses a private cache
    # (RestorePackagesPath); drop Goop from it so the restore extracts the
    # package we just built.
    Remove-Item -Recurse -Force (Join-Path $PackageTests 'obj\nuget-packages\goop') -ErrorAction SilentlyContinue

    & dotnet test $PackageTests --configuration $Configuration --nologo --settings $RunSettings
    Assert-LastExitCode -Step "package test ($Configuration)" -Hint @"
The packed Goop did not work for an outside consumer. A DllNotFoundException
means goop_native.dll did not reach that target framework's output: for net48
check build/Goop.targets in the package, for net8.0/net10.0 check
runtimes/win-x64/native/ in the package.
"@
}

# ---------------------------------------------------------------------------

Write-Host ''
Write-Host "Goop build succeeded ($Configuration)." -ForegroundColor Green
exit 0
