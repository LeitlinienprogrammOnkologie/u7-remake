# Build Exult's usecode compiler into scripts/ucc/bin/ucc.exe.
#   pwsh scripts/ucc/build.ps1
# Needs the Exult 1.12.1 source in exult/ (untracked), Visual Studio's C++
# tools (its bundled CMake) and WinFlexBison
# (winget install WinFlexBison.win_flex_bison).
$ErrorActionPreference = "Stop"
$here = $PSScriptRoot
$vs = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -property installationPath
$cmake = Join-Path $vs "Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe"
$flexBison = Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages" -Directory -Filter "WinFlexBison*" |
    Select-Object -First 1
if ($flexBison) { $env:PATH = "$($flexBison.FullName);$env:PATH" }

$build = Join-Path $here "build"
& $cmake -S $here -B $build -A x64
if ($LASTEXITCODE) { exit $LASTEXITCODE }
& $cmake --build $build --config Release
if ($LASTEXITCODE) { exit $LASTEXITCODE }

$bin = Join-Path $here "bin"
New-Item -ItemType Directory -Force $bin | Out-Null
Copy-Item (Join-Path $build "Release\ucc.exe") $bin -Force
"built $(Join-Path $bin 'ucc.exe')"
