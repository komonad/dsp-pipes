param([string]$ProfilePath = (Join-Path $PSScriptRoot 'obj/playtest/profile'),[switch]$PrepareOnly)
$ErrorActionPreference = 'Stop'
[xml]$settings = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Local.props') -Raw
$game = Split-Path (Split-Path ([string]$settings.Project.PropertyGroup.Managed) -Parent) -Parent
$source = Split-Path ([string]$settings.Project.PropertyGroup.BepInExCore) -Parent
$bep = Join-Path $ProfilePath 'BepInEx'
New-Item -ItemType Directory -Path $bep -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $source 'core') -Destination $bep -Recurse -Force
$plugins = Join-Path $bep 'plugins'
New-Item -ItemType Directory -Path $plugins -Force | Out-Null
foreach ($dependency in @('CommonAPI-CommonAPI','CommonAPI-DSPModSave','xiaoye97-LDBTool','nebula-NebulaMultiplayerModApi')) {
    Copy-Item -LiteralPath (Join-Path $source ('plugins/' + $dependency)) -Destination $plugins -Recurse -Force
}
$installed = @(Get-ChildItem -LiteralPath $plugins -Filter 'FluidPipes.dll' -File -Recurse)
$targets = if ($installed.Count -gt 0) { $installed.FullName } else { Join-Path $plugins 'FluidPipes.dll' }
foreach ($target in $targets) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'bin/Release/FluidPipes.dll') -Destination $target -Force
}
if ($PrepareOnly) { Write-Output "Prepared profile: $ProfilePath"; return }
$arguments = @('--doorstop-enable','true','--doorstop-target',('"' + (Join-Path $bep 'core/BepInEx.Preloader.dll') + '"'))
Start-Process -FilePath (Join-Path $game 'DSPGAME.exe') -ArgumentList $arguments -WorkingDirectory $game -WindowStyle Hidden -Environment @{SteamAppId='1366540';SteamGameId='1366540'}
