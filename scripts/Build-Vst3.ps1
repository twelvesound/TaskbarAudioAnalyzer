$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$bundledCmake = Join-Path $projectRoot "tools\cmake\cmake-4.3.3-windows-x86_64\bin\cmake.exe"
$cmake = if (Test-Path -LiteralPath $bundledCmake) {
    $bundledCmake
}
else {
    $cmakeCommand = Get-Command cmake -ErrorAction SilentlyContinue
    if ($null -eq $cmakeCommand) {
        throw "CMake was not found. Install CMake and add it to PATH, or place the portable build at $bundledCmake"
    }
    $cmakeCommand.Source
}
$source = Join-Path $projectRoot "src\TaskbarAudioTap"
$sdk = Join-Path $projectRoot "external\vst3-sdk"
$build = Join-Path $projectRoot "artifacts\build\vst3-release"

if (-not (Test-Path -LiteralPath (Join-Path $sdk "CMakeLists.txt"))) {
    throw "The Steinberg VST3 SDK was not found at $sdk. Clone it with: git clone --recursive https://github.com/steinbergmedia/vst3sdk.git `"$sdk`""
}

function Invoke-PortableCMake([string]$arguments) {
    # Codex Desktop currently supplies both PATH and Path. MSBuild treats those
    # as duplicate keys, so launch it with one clean PATH entry.
    $command = "set PATH=& set Path=& set PATH=C:\Windows\System32;C:\Windows&& `"$cmake`" $arguments"
    cmd.exe /d /s /c $command
    if ($LASTEXITCODE -ne 0) {
        throw "CMake failed with exit code $LASTEXITCODE"
    }
}

Invoke-PortableCMake "-S `"$source`" -B `"$build`" -G `"Visual Studio 18 2026`" -A x64 -Dvst3sdk_SOURCE_DIR=`"$sdk`" -DSMTG_CREATE_PLUGIN_LINK=OFF -DTASKBAR_AUDIO_TAP_BUILD_TESTS=OFF"
Invoke-PortableCMake "--build `"$build`" --config Release --target TaskbarAudioTap --parallel 8"

$bundle = Join-Path $build "VST3\Release\TaskbarAudioTap.vst3"
Write-Host "Built: $bundle"
