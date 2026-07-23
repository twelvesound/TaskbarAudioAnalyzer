$projectRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $projectRoot "src\TaskbarAudioAnalyzer\TaskbarAudioAnalyzer.csproj"
$exe = Join-Path $projectRoot "src\TaskbarAudioAnalyzer\bin\Debug\net10.0-windows\TaskbarAudioAnalyzer.exe"

$running = Get-Process TaskbarAudioAnalyzer -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "TaskbarAudioAnalyzer is already running."
    exit
}

if (Test-Path $exe) {
    Start-Process -FilePath $exe
    exit
}

dotnet run --project $project
