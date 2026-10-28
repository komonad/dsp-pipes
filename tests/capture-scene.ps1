param([switch]$Connections,[switch]$Cube,[switch]$Structures,[switch]$Gameplay,[switch]$Inserters,[switch]$MinimalLine,[switch]$ParallelLine,[switch]$SolidsGallery,[switch]$InserterGallery,[string]$InspectSave='',[string]$PluginDll='')
$ErrorActionPreference = 'Stop'
if (@($Connections,$Cube,$Structures,$Gameplay,$Inserters,$MinimalLine,$ParallelLine,$SolidsGallery,$InserterGallery | Where-Object { $_ }).Count -gt 1) { throw 'Select one capture scene.' }
$project = Split-Path $PSScriptRoot -Parent
if($InspectSave){$InspectSave=(Resolve-Path -LiteralPath $InspectSave).Path}
dotnet build (Join-Path $PSScriptRoot 'SceneCapture.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Scene capture build failed.' }
$root = Join-Path $project 'obj/scene-capture'
[xml]$settings = Get-Content -LiteralPath (Join-Path $project 'Local.props') -Raw
$game = Split-Path (Split-Path ([string]$settings.Project.PropertyGroup.Managed) -Parent) -Parent
$source = Split-Path ([string]$settings.Project.PropertyGroup.BepInExCore) -Parent
$bep = Join-Path $root 'profile/BepInEx'
New-Item -ItemType Directory -Path $bep -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $source 'core') -Destination $bep -Recurse -Force
$plugins = Join-Path $bep 'plugins'
New-Item -ItemType Directory -Path $plugins -Force | Out-Null
foreach ($dependency in @('CommonAPI-CommonAPI','CommonAPI-DSPModSave','xiaoye97-LDBTool','nebula-NebulaMultiplayerModApi')) {
    Copy-Item -LiteralPath (Join-Path $source ('plugins/' + $dependency)) -Destination $plugins -Recurse -Force
}
if(!$PluginDll){$PluginDll=Join-Path $project 'bin/Release/FluidPipes.dll'}
Copy-Item -LiteralPath $PluginDll -Destination (Join-Path $plugins 'FluidPipes.dll') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'bin/Release/SceneCapture.dll') -Destination $plugins -Force
$arguments = @('-batchmode','-screen-width','1920','-screen-height','1080','-logFile',('"' + (Join-Path $root 'Player.log') + '"'),'--doorstop-enable','true','--doorstop-target',('"' + (Join-Path $bep 'core/BepInEx.Preloader.dll') + '"'))
$process = $null
$captureStart = [DateTime]::UtcNow
$captureMode = if($SolidsGallery){'solids-gallery'}elseif($InserterGallery){'inserter-gallery'}elseif($ParallelLine){'parallel-line'}elseif($MinimalLine){'minimal-line'}elseif($Inserters){'inserters'}elseif($Gameplay){'gameplay'}elseif ($Structures) { 'structures' } elseif ($Cube) { 'cube' } elseif ($Connections) { 'connections' } else { 'comparison' }
try {
    for ($attempt = 0; $attempt -lt 2; $attempt++) {
        $launchStart = [DateTime]::UtcNow
        $process = Start-Process -FilePath (Join-Path $game 'DSPGAME.exe') -ArgumentList $arguments -WorkingDirectory $game -WindowStyle Hidden -PassThru -Environment @{SteamAppId='1366540';SteamGameId='1366540';FluidPipesCapture=$captureMode;FluidPipesInspectSave=$InspectSave}
        Write-Output "Background scene capture PID=$($process.Id)"
        $elapsed = [Diagnostics.Stopwatch]::StartNew()
        while (!$process.WaitForExit(1000)) {
            if ($elapsed.Elapsed.TotalSeconds -gt 300) { throw 'Scene capture timed out.' }
            $log = Get-Item -LiteralPath (Join-Path $root 'Player.log') -ErrorAction SilentlyContinue
            if ($elapsed.Elapsed.TotalSeconds -gt 30 -and (!$log -or $log.LastWriteTimeUtc -lt $launchStart)) {
                Stop-Process -Id $process.Id
                $process.WaitForExit(5000) | Out-Null
                $process.Dispose()
                $process = $null
                Write-Output 'Background game startup produced no log; retrying.'
                break
            }
        }
        if ($process) { break }
    }
    if (!$process) { throw 'Background game startup produced no fresh log after two attempts.' }
    if ($process.ExitCode -ne 0) { throw "Scene capture failed: $($process.ExitCode). See obj/scene-capture/Player.log." }
    $output = Join-Path $plugins 'scene-output'
    if($InspectSave -and !$MinimalLine -and !$ParallelLine){Get-Content -LiteralPath (Join-Path $output 'inspect-results.txt');return}
    $result = Join-Path $output $(if($SolidsGallery){'solids-gallery-results.txt'}elseif($InserterGallery){'inserter-gallery-results.txt'}elseif($ParallelLine){'parallel-line-results.txt'}elseif($MinimalLine){'minimal-line-results.txt'}elseif($Inserters){'inserters-results.txt'}elseif($Gameplay){'gameplay-results.txt'}elseif ($Structures) { 'structures-results.txt' } elseif ($Cube) { 'cube-results.txt' } elseif ($Connections) { 'connections-results.txt' } else { 'scene-results.txt' })
    if (!(Test-Path -LiteralPath $result)) { throw 'Scene capture exited without a report.' }
    Get-Content -LiteralPath $result
    $pictures = @(Get-ChildItem -LiteralPath $output -Filter 'ingame-*.png' -File)
    $pictures = @($pictures | Where-Object {
        if($SolidsGallery){$_.Name -like 'ingame-solids-*'}elseif($InserterGallery){$_.Name -like 'ingame-sorters-*'}elseif($ParallelLine){$_.Name -like 'ingame-parallel-line-*'}elseif($MinimalLine){$_.Name -like 'ingame-minimal-line-*'}elseif($Inserters){$_.Name -like 'ingame-inserters-*'}elseif($Gameplay){$_.Name -like 'ingame-gameplay-*'}elseif ($Structures) { $_.Name -like 'ingame-structures-*' }
        elseif ($Cube) { $_.Name -like 'ingame-cube-*' }
        elseif ($Connections) { $_.Name -like 'ingame-connections-*' }
        else { $_.Name -in @('ingame-comparison-overview.png','ingame-comparison-straight-elbow.png','ingame-comparison-junction-riser.png','ingame-pipe-junction-closeup.png','ingame-belt-junction-closeup.png','ingame-tank-pipe-materials.png','ingame-filled-pipe.png','ingame-empty-pipe.png','ingame-single-nodes.png') }
    })
    $expected = if($SolidsGallery){11}elseif($InserterGallery){14}elseif($ParallelLine){7}elseif($MinimalLine){4}elseif($Inserters){10}elseif($Gameplay){3}elseif ($Structures) { 5 } elseif ($Cube) { 7 } elseif ($Connections) { 2 } else { 9 }
    if ($pictures.Count -lt $expected -or @($pictures | Where-Object { $_.LastWriteTimeUtc -lt $captureStart }).Count -gt 0) { throw "Scene capture did not produce $expected fresh images." }
    $destination = Join-Path $project $(if($SolidsGallery -or $InserterGallery){'screenshots/showcase'}else{'screenshots/ingame'})
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    foreach ($picture in $pictures) { Copy-Item -LiteralPath $picture.FullName -Destination $destination -Force }
    Copy-Item -LiteralPath $result -Destination $destination -Force
} finally {
    if ($process) { if (!$process.HasExited) { Stop-Process -Id $process.Id }; $process.Dispose() }
}
