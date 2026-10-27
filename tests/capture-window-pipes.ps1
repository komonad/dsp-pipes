$ErrorActionPreference = 'Stop'
$projectDirectory = Split-Path $PSScriptRoot -Parent
dotnet build (Join-Path $PSScriptRoot 'SceneCapture.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) {
    throw 'Window pipe preview scene build failed.'
}
dotnet build (Join-Path $projectDirectory 'examples/CopperPipes/CopperPipes.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) {
    throw 'Independent CopperPipes consumer build failed.'
}
if (Get-Process DSPGAME -ErrorAction SilentlyContinue) {
    throw 'An existing game process is running.'
}

[xml]$settings = Get-Content -LiteralPath (Join-Path $projectDirectory 'Local.props') -Raw
$gameDirectory = Split-Path (Split-Path ([string]$settings.Project.PropertyGroup.Managed) -Parent) -Parent
$sourceBepDirectory = Split-Path ([string]$settings.Project.PropertyGroup.BepInExCore) -Parent
$runDirectory = Join-Path $projectDirectory ('obj/window-pipe-preview/' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$bepDirectory = Join-Path $runDirectory 'BepInEx'
$pluginDirectory = Join-Path $bepDirectory 'plugins'
New-Item -ItemType Directory -Path $pluginDirectory -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $sourceBepDirectory 'core') -Destination $bepDirectory -Recurse
foreach ($dependency in @(
    'CommonAPI-CommonAPI', 'CommonAPI-DSPModSave',
    'xiaoye97-LDBTool', 'nebula-NebulaMultiplayerModApi'
)) {
    Copy-Item -LiteralPath (Join-Path $sourceBepDirectory ('plugins/' + $dependency)) -Destination $pluginDirectory -Recurse
}
Copy-Item -LiteralPath (Join-Path $projectDirectory 'bin/Release/FluidPipes.dll') -Destination $pluginDirectory
Copy-Item -LiteralPath (Join-Path $projectDirectory 'examples/CopperPipes/bin/Release/CopperPipes.dll') -Destination $pluginDirectory
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'bin/Release/SceneCapture.dll') -Destination $pluginDirectory

$arguments = @(
    '-batchmode', '-screen-width', '1920', '-screen-height', '1080',
    '-logFile', ('"' + (Join-Path $runDirectory 'Player.log') + '"'),
    '--doorstop-enable', 'true', '--doorstop-target',
    ('"' + (Join-Path $bepDirectory 'core/BepInEx.Preloader.dll') + '"')
)
$environment = @{
    SteamAppId = '1366540'
    SteamGameId = '1366540'
    FluidPipesCapture = 'window-pipes'
}
$process = Start-Process -FilePath (Join-Path $gameDirectory 'DSPGAME.exe') -ArgumentList $arguments -WorkingDirectory $gameDirectory -WindowStyle Hidden -PassThru -Environment $environment
Write-Output "Background window pipe capture PID=$($process.Id)"
Write-Output "Run directory=$runDirectory"
try {
    $elapsed = [Diagnostics.Stopwatch]::StartNew()
    while (!$process.WaitForExit(1000)) {
        if ($elapsed.Elapsed.TotalSeconds -gt 180) { throw 'Window pipe capture timed out.' }
    }
    if ($process.ExitCode -ne 0) { throw ('Window pipe capture failed. See ' + $runDirectory) }
    $outputDirectory = Join-Path $pluginDirectory 'scene-output'
    $destination = Join-Path $projectDirectory 'screenshots/window-pipes'
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    Get-ChildItem -LiteralPath $outputDirectory -Filter 'ingame-window-pipes-*.png' -File | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $destination -Force
        Write-Output $_.Name
    }
} finally {
    if (!$process.HasExited) { Stop-Process -Id $process.Id }
    $process.Dispose()
}
