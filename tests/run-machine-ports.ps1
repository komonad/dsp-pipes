param([string]$InspectSave = '')

$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
dotnet build (Join-Path $PSScriptRoot 'SceneCapture.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) {
    throw 'Machine-port scene build failed.'
}
if (Get-Process DSPGAME -ErrorAction SilentlyContinue) {
    throw 'An existing game process is running.'
}

[xml]$settings = Get-Content -LiteralPath (Join-Path $project 'Local.props') -Raw
$gameDirectory = Split-Path (Split-Path ([string]$settings.Project.PropertyGroup.Managed) -Parent) -Parent
$sourceBepDirectory = Split-Path ([string]$settings.Project.PropertyGroup.BepInExCore) -Parent
$runDirectory = Join-Path $project ('obj/machine-port-checks/' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$bepDirectory = Join-Path $runDirectory 'BepInEx'
$pluginDirectory = Join-Path $bepDirectory 'plugins'
$configDirectory = Join-Path $bepDirectory 'config'
New-Item -ItemType Directory -Path $pluginDirectory, $configDirectory -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $sourceBepDirectory 'core') -Destination $bepDirectory -Recurse
foreach ($dependency in @(
    'CommonAPI-CommonAPI', 'CommonAPI-DSPModSave',
    'xiaoye97-LDBTool', 'nebula-NebulaMultiplayerModApi'
)) {
    Copy-Item -LiteralPath (Join-Path $sourceBepDirectory ('plugins/' + $dependency)) -Destination $pluginDirectory -Recurse
}
Copy-Item -LiteralPath (Join-Path $project 'bin/Release/FluidPipes.dll') -Destination $pluginDirectory
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'bin/Release/SceneCapture.dll') -Destination $pluginDirectory
Set-Content -LiteralPath (Join-Path $configDirectory 'com.komonad.dsp.fluidpipes.cfg') -Value "[Integration]`nEnableMachinePorts = true" -Encoding utf8NoBOM

$arguments = @(
    '-batchmode', '-screen-width', '1920', '-screen-height', '1080',
    '-logFile', ('"' + (Join-Path $runDirectory 'Player.log') + '"'),
    '--doorstop-enable', 'true', '--doorstop-target',
    ('"' + (Join-Path $bepDirectory 'core/BepInEx.Preloader.dll') + '"')
)
$environment = @{
    SteamAppId = '1366540'
    SteamGameId = '1366540'
    FluidPipesCapture = 'machine-ports'
    FluidPipesInspectSave = $InspectSave
}
$process = Start-Process -FilePath (Join-Path $gameDirectory 'DSPGAME.exe') -ArgumentList $arguments -WorkingDirectory $gameDirectory -WindowStyle Hidden -PassThru -Environment $environment
Write-Output "Machine-port checks PID=$($process.Id)"
Write-Output "Run directory=$runDirectory"
try {
    $elapsed = [Diagnostics.Stopwatch]::StartNew()
    while (!$process.WaitForExit(1000)) {
        if ($elapsed.Elapsed.TotalSeconds -gt 240) {
            throw 'Machine-port checks timed out.'
        }
    }
    $outputDirectory = Join-Path $pluginDirectory 'scene-output'
    $resultFile = Join-Path $outputDirectory 'machine-ports-results.txt'
    if (!(Test-Path -LiteralPath $resultFile)) {
        throw 'Game exited before the machine-port report was written.'
    }
    Get-Content -LiteralPath $resultFile
    if ($process.ExitCode -ne 0) {
        throw 'Machine-port runtime checks failed.'
    }
    $pictures = @(Get-ChildItem -LiteralPath $outputDirectory -Filter 'ingame-machine-ports-*.png' -File)
    $expectedPictures = if ([string]::IsNullOrEmpty($InspectSave)) { 13 } else { 3 }
    if ($pictures.Count -ne $expectedPictures) {
        throw "Machine-port checks did not produce $expectedPictures screenshots."
    }
    $destination = Join-Path $project 'screenshots/machine-ports'
    if (![string]::IsNullOrEmpty($InspectSave)) {
        $destination = Join-Path $destination 'reload'
    }
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    foreach ($picture in $pictures) {
        Copy-Item -LiteralPath $picture.FullName -Destination $destination -Force
    }
    Copy-Item -LiteralPath $resultFile -Destination $destination -Force
} finally {
    if (!$process.HasExited) {
        Stop-Process -Id $process.Id
    }
    $process.Dispose()
}
