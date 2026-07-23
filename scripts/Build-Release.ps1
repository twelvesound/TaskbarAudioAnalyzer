param(
    [ValidatePattern('^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$')]
    [string]$Version = "1.1.0"
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $projectRoot "src\TaskbarAudioAnalyzer\TaskbarAudioAnalyzer.csproj"
$vst3Bundle = Join-Path $projectRoot "artifacts\build\vst3-release\VST3\Release\TaskbarAudioTap.vst3"
$releaseParent = [IO.Path]::GetFullPath((Join-Path $projectRoot "artifacts\release"))
$releaseRoot = [IO.Path]::GetFullPath((Join-Path $releaseParent "v$Version"))
$expectedPrefix = $releaseParent.TrimEnd('\') + '\'
if (-not $releaseRoot.StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "The release output escaped the expected artifacts directory."
}

$stagingRoot = Join-Path $releaseRoot "staging"
$appFolderName = "TaskbarAudioAnalyzer-v$Version-win-x64"
$vst3FolderName = "TaskbarAudioTap-v$Version-win-x64"
$appStage = Join-Path $stagingRoot $appFolderName
$vst3Stage = Join-Path $stagingRoot $vst3FolderName
$appArchive = Join-Path $releaseRoot "$appFolderName.zip"
$vst3Archive = Join-Path $releaseRoot "$vst3FolderName.zip"

if (Test-Path -LiteralPath $releaseRoot) {
    Remove-Item -LiteralPath $releaseRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $appStage, $vst3Stage | Out-Null

dotnet publish $project `
    --configuration Release `
    --runtime win-x64 `
    --self-contained false `
    -p:Version=$Version `
    --output $appStage
if ($LASTEXITCODE -ne 0) {
    throw "The WPF application publish failed with exit code $LASTEXITCODE."
}

$appPdb = Join-Path $appStage "TaskbarAudioAnalyzer.pdb"
if (Test-Path -LiteralPath $appPdb) {
    Remove-Item -LiteralPath $appPdb -Force
}

& (Join-Path $PSScriptRoot "Build-Vst3.ps1")
if (-not (Test-Path -LiteralPath $vst3Bundle)) {
    throw "The VST3 bundle was not found at $vst3Bundle"
}
Copy-Item -LiteralPath $vst3Bundle -Destination (Join-Path $vst3Stage "TaskbarAudioTap.vst3") -Recurse

$distributionDocuments = @("LICENSE", "THIRD-PARTY-NOTICES.md", "README.md", "README.en.md")
foreach ($document in $distributionDocuments) {
    $sourceDocument = Join-Path $projectRoot $document
    Copy-Item -LiteralPath $sourceDocument -Destination (Join-Path $appStage $document)
    Copy-Item -LiteralPath $sourceDocument -Destination (Join-Path $vst3Stage $document)
}

Compress-Archive -LiteralPath $appStage -DestinationPath $appArchive -CompressionLevel Optimal
Compress-Archive -LiteralPath $vst3Stage -DestinationPath $vst3Archive -CompressionLevel Optimal

$checksumLines = @($appArchive, $vst3Archive) | ForEach-Object {
    $item = Get-Item -LiteralPath $_
    $hash = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $($item.Name)"
}
$checksumPath = Join-Path $releaseRoot "SHA256SUMS.txt"
Set-Content -LiteralPath $checksumPath -Value $checksumLines -Encoding ascii

Remove-Item -LiteralPath $stagingRoot -Recurse -Force

Write-Host "Release packages:"
Get-Item -LiteralPath $appArchive, $vst3Archive, $checksumPath |
    Select-Object Name, Length, LastWriteTime
