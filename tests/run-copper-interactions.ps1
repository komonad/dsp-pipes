param([switch]$WithoutBuiltInContent)
$ErrorActionPreference = 'Stop'
$projectDirectory = Split-Path $PSScriptRoot -Parent
dotnet build (Join-Path $PSScriptRoot 'SceneCapture.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) {
    throw 'Copper interaction scene build failed.'
}
dotnet build (Join-Path $projectDirectory 'examples/CopperPipes/CopperPipes.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) {
    throw 'Independent CopperPipes consumer build failed.'
}
if ($WithoutBuiltInContent) {
    dotnet build (Join-Path $PSScriptRoot 'EarlyContentOptOut.csproj') -c Release --nologo
    if ($LASTEXITCODE -ne 0) {
        throw 'External content opt-out startup plugin build failed.'
    }
}
if (Get-Process DSPGAME -ErrorAction SilentlyContinue) {
    throw 'An existing game process is running.'
}

[xml]$settings = Get-Content -LiteralPath (Join-Path $projectDirectory 'Local.props') -Raw
$gameDirectory = Split-Path (Split-Path ([string]$settings.Project.PropertyGroup.Managed) -Parent) -Parent
$sourceBepDirectory = Split-Path ([string]$settings.Project.PropertyGroup.BepInExCore) -Parent
$runDirectory = Join-Path $projectDirectory ('obj/copper-interactions/' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
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
if ($WithoutBuiltInContent) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'bin/Release/EarlyContentOptOut.dll') -Destination $pluginDirectory
}

$arguments = @(
    '-batchmode', '-screen-width', '1920', '-screen-height', '1080',
    '-logFile', ('"' + (Join-Path $runDirectory 'Player.log') + '"'),
    '--doorstop-enable', 'true', '--doorstop-target',
    ('"' + (Join-Path $bepDirectory 'core/BepInEx.Preloader.dll') + '"')
)
$environment = @{
    SteamAppId = '1366540'
    SteamGameId = '1366540'
    FluidPipesCapture = 'copper-interactions'
    FluidPipesBuiltInContent = $(if ($WithoutBuiltInContent) { '0' } else { '1' })
}
$process = Start-Process -FilePath (Join-Path $gameDirectory 'DSPGAME.exe') -ArgumentList $arguments -WorkingDirectory $gameDirectory -WindowStyle Hidden -PassThru -Environment $environment
Write-Output "Copper interaction checks PID=$($process.Id)"
Write-Output "Run directory=$runDirectory"
try {
    $elapsed = [Diagnostics.Stopwatch]::StartNew()
    while (!$process.WaitForExit(1000)) {
        if ($elapsed.Elapsed.TotalSeconds -gt 300) {
            throw 'Copper interaction checks timed out.'
        }
    }
    $outputDirectory = Join-Path $pluginDirectory 'scene-output'
    $resultFile = Join-Path $outputDirectory 'copper-interactions-results.txt'
    if (!(Test-Path -LiteralPath $resultFile)) {
        throw 'Game exited before the native interaction report was written.'
    }
    Get-Content -LiteralPath $resultFile
    if ($process.ExitCode -ne 0) {
        throw 'Copper native interaction checks failed.'
    }
    $pictures = @(Get-ChildItem -LiteralPath $outputDirectory -Filter 'ingame-copper-interactions-*.png' -File)
    $expectedPictureCount = if ($WithoutBuiltInContent) { 2 } else { 4 }
    if ($pictures.Count -ne $expectedPictureCount) {
        throw "Copper interaction checks did not produce $expectedPictureCount endpoint screenshots."
    }
    $destination = Join-Path $projectDirectory $(if ($WithoutBuiltInContent) { 'screenshots/copper-library-only' } else { 'screenshots/copper-interactions' })
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    foreach ($picture in $pictures) {
        Copy-Item -LiteralPath $picture.FullName -Destination $destination -Force
    }
    Copy-Item -LiteralPath $resultFile -Destination $destination -Force
    if ($WithoutBuiltInContent) {
        Copy-Item -LiteralPath (Join-Path $pluginDirectory 'disabled-content-bootstrap.txt') -Destination $destination -Force
    }
} finally {
    if (!$process.HasExited) {
        Stop-Process -Id $process.Id
    }
    $process.Dispose()
}
