$ErrorActionPreference = 'Stop'
$version = '0.4.0'
$assembly = Join-Path $PSScriptRoot 'bin/Release/FluidPipes.dll'
$dist = Join-Path $PSScriptRoot 'dist'
$archive = Join-Path $dist ('FluidPipes-' + $version + '.zip')
if (-not (Test-Path -LiteralPath $assembly -PathType Leaf)) { throw 'Build the release assembly before packaging.' }
New-Item -ItemType Directory -Path $dist -Force | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
$stream = [IO.File]::Open($archive, [IO.FileMode]::Create)
try {
    $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $true)
    try {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $assembly, 'BepInEx/plugins/FluidPipes/FluidPipes.dll', [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    } finally { $zip.Dispose() }
} finally { $stream.Dispose() }
Get-FileHash -LiteralPath $archive -Algorithm SHA256
