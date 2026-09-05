param([switch]$Development)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $projectRoot "src\TaskbarAudioAnalyzer\TaskbarAudioAnalyzer.csproj"
$exe = Join-Path $projectRoot "src\TaskbarAudioAnalyzer\bin\Debug\net10.0-windows\TaskbarAudioAnalyzer.exe"

$running = Get-Process TaskbarAudioAnalyzer -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "TaskbarAudioAnalyzer is already running."
    exit
}

$installedExe = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'TaskbarAudioAnalyzer\TaskbarAudioAnalyzer.exe'
if (-not $Development -and (Test-Path -LiteralPath $installedExe)) {
    Start-Process -FilePath $installedExe -WorkingDirectory (Split-Path $installedExe)
    exit
}

dotnet build $project --configuration Debug
if ($LASTEXITCODE -ne 0) { throw "Application build failed: $LASTEXITCODE" }
Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe)
