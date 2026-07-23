$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$bundle = Join-Path $projectRoot "artifacts\build\vst3-release\VST3\Release\TaskbarAudioTap.vst3"
if (-not (Test-Path -LiteralPath $bundle)) {
    & (Join-Path $PSScriptRoot "Build-Vst3.ps1")
}

$vst3Directory = "C:\Program Files\Common Files\VST3\12sound"
$destination = Join-Path $vst3Directory "TaskbarAudioTap.vst3"
$relativeBinary = "Contents\x86_64-win\TaskbarAudioTap.vst3"
$sourceBinary = Join-Path $bundle $relativeBinary
$legacyDestinations = @(
    "C:\Program Files\Common Files\VST3\TaskbarAudioTap.vst3",
    (Join-Path $env:LOCALAPPDATA "Programs\Common\VST3\TaskbarAudioTap.vst3")
)

if (-not (Test-Path -LiteralPath $sourceBinary)) {
    throw "The built VST3 binary was not found at $sourceBinary"
}

New-Item -ItemType Directory -Force -Path $vst3Directory | Out-Null
$staging = Join-Path $vst3Directory ".TaskbarAudioTap.installing-$PID"
$backup = Join-Path $vst3Directory ".TaskbarAudioTap.backup-$(Get-Date -Format 'yyyyMMddHHmmss')-$PID"
$hadExistingInstallation = Test-Path -LiteralPath $destination

try {
    if (Test-Path -LiteralPath $staging) {
        Remove-Item -LiteralPath $staging -Recurse -Force
    }

    Copy-Item -LiteralPath $bundle -Destination $staging -Recurse
    $stagedBinary = Join-Path $staging $relativeBinary
    $sourceHash = (Get-FileHash -LiteralPath $sourceBinary -Algorithm SHA256).Hash
    $stagedHash = (Get-FileHash -LiteralPath $stagedBinary -Algorithm SHA256).Hash
    if ($sourceHash -ne $stagedHash) {
        throw "The staged VST3 binary did not match the build output."
    }

    if ($hadExistingInstallation) {
        Move-Item -LiteralPath $destination -Destination $backup
    }

    Move-Item -LiteralPath $staging -Destination $destination
    $installedBinary = Join-Path $destination $relativeBinary
    $installedHash = (Get-FileHash -LiteralPath $installedBinary -Algorithm SHA256).Hash
    if ($sourceHash -ne $installedHash) {
        throw "The installed VST3 binary did not match the build output."
    }

    if (Test-Path -LiteralPath $backup) {
        Remove-Item -LiteralPath $backup -Recurse -Force
    }
}
catch {
    if (Test-Path -LiteralPath $backup) {
        if (Test-Path -LiteralPath $destination) {
            Remove-Item -LiteralPath $destination -Recurse -Force
        }
        Move-Item -LiteralPath $backup -Destination $destination
    }

    throw
}
finally {
    if (Test-Path -LiteralPath $staging) {
        Remove-Item -LiteralPath $staging -Recurse -Force
    }
}

foreach ($legacyDestination in $legacyDestinations) {
    if (Test-Path -LiteralPath $legacyDestination) {
        try {
            Remove-Item -LiteralPath $legacyDestination -Recurse -Force
        }
        catch {
            Write-Warning "Could not remove the legacy plug-in while a DAW is using it: $legacyDestination"
        }
    }
}

Write-Host "Installed: $destination"
Write-Host "SHA256: $sourceHash"
