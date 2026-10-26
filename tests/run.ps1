param([switch]$RenderIcon,[switch]$IconOnly,[switch]$WithoutExtensions)
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
dotnet build (Join-Path $PSScriptRoot 'RuntimeChecks.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Runtime checks build failed.' }
if (!$WithoutExtensions) {
    dotnet build (Join-Path $project 'examples/CopperPipes/CopperPipes.csproj') -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'External CopperPipes consumer build failed.' }
}
$root = Join-Path $project 'obj/runtime-checks'
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
Copy-Item -LiteralPath (Join-Path $project 'bin/Release/FluidPipes.dll') -Destination $plugins -Force
Copy-Item -LiteralPath (Join-Path $project 'bin/Release/FluidPipes.xml') -Destination $plugins -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'bin/Release/RuntimeChecks.dll') -Destination $plugins -Force
$consumerAssembly = Join-Path $plugins 'CopperPipes.dll'
if ($WithoutExtensions) {
    if (Test-Path -LiteralPath $consumerAssembly) { Remove-Item -LiteralPath $consumerAssembly }
} else {
    Copy-Item -LiteralPath (Join-Path $project 'examples/CopperPipes/bin/Release/CopperPipes.dll') -Destination $consumerAssembly -Force
}
$resultFile = Join-Path $plugins 'results.txt'
if (Test-Path -LiteralPath $resultFile) { Remove-Item -LiteralPath $resultFile }
$arguments = @('-batchmode','-screen-width','1280','-screen-height','720','-logFile',('"' + (Join-Path $root 'Player.log') + '"'),'--doorstop-enable','true','--doorstop-target',('"' + (Join-Path $bep 'core/BepInEx.Preloader.dll') + '"'))
$process = $null
try {
    for ($attempt = 0; $attempt -lt 2; $attempt++) {
        $process = Start-Process -FilePath (Join-Path $game 'DSPGAME.exe') -ArgumentList $arguments -WorkingDirectory $game -WindowStyle Hidden -PassThru -Environment @{SteamAppId='1366540';SteamGameId='1366540';FluidPipesRenderIcon=$(if($RenderIcon){'1'}else{'0'});FluidPipesIconOnly=$(if($IconOnly){'1'}else{'0'});FluidPipesExternalConsumers=$(if($WithoutExtensions){'0'}else{'1'})}
        if (!$process.WaitForExit(2000)) { break }
        $process.Dispose(); $process = $null
    }
    if (!$process) { throw 'DSP exited before test startup.' }
    Write-Output "Fluid pipe checks PID=$($process.Id)"
    $elapsed = [Diagnostics.Stopwatch]::StartNew()
    while (!$process.WaitForExit(1000)) { if ($elapsed.Elapsed.TotalSeconds -gt 300) { throw 'Fluid pipe checks timed out.' } }
    if (!(Test-Path -LiteralPath $resultFile)) { throw 'DSP exited without fresh test results.' }
    Copy-Item -LiteralPath $resultFile -Destination (Join-Path $PSScriptRoot $(if($RenderIcon){'icon-render-results.txt'}elseif($IconOnly){'icon-results.txt'}elseif($WithoutExtensions){'runtime-standalone-results.txt'}else{'runtime-results.txt'})) -Force
    Get-Content -LiteralPath $resultFile
    if ((Get-Content -LiteralPath $resultFile -Tail 1) -ne 'Failures=0') { throw 'Fluid pipe runtime checks failed.' }
    $screenshots = Join-Path $project 'screenshots'
    New-Item -ItemType Directory -Path $screenshots -Force | Out-Null
    foreach ($picture in Get-ChildItem -LiteralPath $plugins -Filter 'pipe-*.png' -File) {
        Copy-Item -LiteralPath $picture.FullName -Destination $screenshots -Force
    }
} finally {
    if ($process) { if (!$process.HasExited) { Stop-Process -Id $process.Id }; $process.Dispose() }
}
