param(
    [Parameter(Mandatory = $true)]
    [string]$SourceProfile,
    [switch]$Vanilla,
    [switch]$WithBuildBarTool,
    [string]$OrbitalPlugin,
    [switch]$Components
)

$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
dotnet build (Join-Path $PSScriptRoot 'OrbitalRingChecks.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) {
    throw 'OrbitalRing checks build failed.'
}
if (Get-Process DSPGAME -ErrorAction SilentlyContinue) {
    throw 'An existing game process is running.'
}

$runDirectory = Join-Path $project ('obj/orbitalring-checks/' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$bepDirectory = Join-Path $runDirectory 'BepInEx'
$pluginDirectory = Join-Path $bepDirectory 'plugins'
New-Item -ItemType Directory -Path $pluginDirectory -Force | Out-Null
$sourceBepDirectory = Join-Path $SourceProfile 'BepInEx'
foreach ($directory in @('core', 'patchers')) {
    Copy-Item -LiteralPath (Join-Path $sourceBepDirectory $directory) -Destination $bepDirectory -Recurse
}
foreach ($dependency in @(
    'CommonAPI-CommonAPI', 'CommonAPI-DSPModSave', 'xiaoye97-LDBTool',
    'nebula-NebulaMultiplayerModApi', 'ProfessorCat-OrbitalRing', 'starfi5h-ModFixerOne'
)) {
    if ($Vanilla -and $dependency -eq 'ProfessorCat-OrbitalRing') {
        continue
    }
    Copy-Item -LiteralPath (Join-Path $sourceBepDirectory ('plugins/' + $dependency)) -Destination $pluginDirectory -Recurse
}
if ($WithBuildBarTool) {
    Copy-Item -LiteralPath (Join-Path $sourceBepDirectory 'plugins/jinxOAO-BuildBarTool') -Destination $pluginDirectory -Recurse
}
Copy-Item -LiteralPath (Join-Path $project 'bin/Release/FluidPipes.dll') -Destination $pluginDirectory
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'bin/Release/OrbitalRingChecks.dll') -Destination $pluginDirectory
if ($OrbitalPlugin) {
    $installedPlugin = Join-Path $pluginDirectory 'ProfessorCat-OrbitalRing/ProjectOrbitalRing.dll'
    if (!(Test-Path -LiteralPath $installedPlugin)) {
        throw 'The isolated profile must contain ProjectOrbitalRing.dll.'
    }
    Copy-Item -LiteralPath $OrbitalPlugin -Destination $installedPlugin -Force
}

[xml]$settings = Get-Content -LiteralPath (Join-Path $project 'Local.props') -Raw
$gameDirectory = Split-Path (Split-Path ([string]$settings.Project.PropertyGroup.Managed) -Parent) -Parent
$arguments = @(
    '-batchmode', '-screen-width', '1280', '-screen-height', '720',
    '-logFile', ('"' + (Join-Path $runDirectory 'Player.log') + '"'),
    '--doorstop-enable', 'true', '--doorstop-target',
    ('"' + (Join-Path $bepDirectory 'core/BepInEx.Preloader.dll') + '"')
)
$process = Start-Process -FilePath (Join-Path $gameDirectory 'DSPGAME.exe') -ArgumentList $arguments -WorkingDirectory $gameDirectory -WindowStyle Hidden -PassThru -Environment @{SteamAppId='1366540';SteamGameId='1366540';FluidPipesOrbitalComponents=$(if($Components){'1'}else{'0'})}
Write-Output "OrbitalRing checks PID=$($process.Id)"
Write-Output "Run directory=$runDirectory"
try {
    $elapsed = [Diagnostics.Stopwatch]::StartNew()
    while (!$process.WaitForExit(1000)) {
        if ($elapsed.Elapsed.TotalSeconds -gt 240) {
            throw 'OrbitalRing checks timed out.'
        }
    }
    $resultFile = Join-Path $pluginDirectory 'orbitalring-results.txt'
    if (!(Test-Path -LiteralPath $resultFile)) {
        throw 'Game exited before test results were written.'
    }
    Get-Content -LiteralPath $resultFile
    if ((Get-Content -LiteralPath $resultFile -Tail 1) -ne 'Completed=1') {
        throw 'OrbitalRing checks failed.'
    }
} finally {
    if (!$process.HasExited) {
        Stop-Process -Id $process.Id
    }
    $process.Dispose()
}
