param([string]$IsccPath)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $projectRoot 'src\TaskbarAudioAnalyzer\TaskbarAudioAnalyzer.csproj'
$version = ([xml](Get-Content -LiteralPath $project -Raw)).Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'A stable app version is required.' }

if (-not $IsccPath) {
    $candidates = @(
        (Join-Path $projectRoot 'tools\cache\inno-6.7.3\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe')
    )
    $IsccPath = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $IsccPath) {
        $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
        if ($command) { $IsccPath = $command.Source }
    }
}
if (-not $IsccPath -or -not (Test-Path -LiteralPath $IsccPath)) {
    throw 'Install Inno Setup 6.7.3 or later, or pass -IsccPath with the compiler path: https://jrsoftware.org/isdl.php'
}

$outputRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot "artifacts\installer\v$version"))
$publishRoot = Join-Path $outputRoot 'app'
$expectedPrefix = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts\installer')) + '\'
if (-not $publishRoot.StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The publish directory escaped the installer artifacts directory.'
}
if (Test-Path -LiteralPath $publishRoot) { Remove-Item -LiteralPath $publishRoot -Recurse -Force }
New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null

dotnet publish $project --configuration Release --runtime win-x64 --self-contained false --output $publishRoot
if ($LASTEXITCODE -ne 0) { throw "Application publish failed: $LASTEXITCODE" }
foreach ($document in @('LICENSE', 'THIRD-PARTY-NOTICES.md', 'README.md', 'README.en.md')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $document) -Destination $publishRoot
}
$pdb = Join-Path $publishRoot 'TaskbarAudioAnalyzer.pdb'
if (Test-Path -LiteralPath $pdb) { Remove-Item -LiteralPath $pdb -Force }

& $IsccPath "/DAppVersion=$version" "/DPublishDir=$publishRoot" "/DOutputDir=$outputRoot" (Join-Path $projectRoot 'installer\TaskbarAudioAnalyzer.iss')
if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed: $LASTEXITCODE" }
$setup = Join-Path $outputRoot "TaskbarAudioAnalyzer-v$version-win-x64-Setup.exe"
$hash = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath (Join-Path $outputRoot 'SHA256SUMS.txt') -Value "$hash  $([IO.Path]::GetFileName($setup))" -Encoding ascii
Get-Item -LiteralPath $setup | Select-Object FullName, Length
