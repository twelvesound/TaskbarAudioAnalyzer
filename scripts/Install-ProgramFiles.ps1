$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'Build-Installer.ps1')
$project = Join-Path $projectRoot 'src\TaskbarAudioAnalyzer\TaskbarAudioAnalyzer.csproj'
$version = ([xml](Get-Content -LiteralPath $project -Raw)).Project.PropertyGroup.Version
$setup = Join-Path $projectRoot "artifacts\installer\v$version\TaskbarAudioAnalyzer-v$version-win-x64-Setup.exe"
# Launch normally so Setup can retain the original user's identity through UAC.
$process = Start-Process -FilePath $setup -PassThru -Wait
if ($process.ExitCode -ne 0) { throw "Setup did not complete: $($process.ExitCode)" }
